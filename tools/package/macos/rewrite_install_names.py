#!/usr/bin/env python3
"""Rewrite Mach-O install names inside a macOS app bundle.

Every bundled Mach-O gets an @rpath id when it has one. Dependencies that
name a library also present in the bundle are pointed at @loader_path
relative to the file being edited. /usr/lib and /System dependencies stay.
An absolute path under /opt/homebrew or /usr/local, or any other absolute
path that is not a bundled library, fails the run.

Usage: rewrite_install_names.py <Contents/MacOS directory or .app directory>
"""

import os
import subprocess
import sys

SYSTEM_PREFIXES = ("/usr/lib/", "/System/")
BAD_MARKERS = ("/opt/homebrew", "/usr/local")
MACHO_MAGICS = {
    b"\xfe\xed\xfa\xce",
    b"\xfe\xed\xfa\xcf",
    b"\xce\xfa\xed\xfe",
    b"\xcf\xfa\xed\xfe",
    b"\xca\xfe\xba\xbe",
    b"\xbe\xba\xfe\xca",
}


def run(cmd):
    result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return result.returncode, result.stdout.decode("utf-8", "replace"), result.stderr.decode("utf-8", "replace")


def is_macho(path):
    try:
        with open(path, "rb") as handle:
            return handle.read(4) in MACHO_MAGICS
    except OSError:
        return False


def index_names(root):
    found = {}
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in filenames:
            found.setdefault(name, os.path.join(dirpath, name))
    return found


def otool_deps(path):
    """Load-command paths from otool -L.

    Universal binaries repeat a non-indented ``path (architecture …):`` banner
    for each slice. Those banners are not dependencies. The same path is listed
    once per slice; install_name_tool updates every slice, so duplicates are
    dropped.
    """
    code, out, err = run(["otool", "-L", path])
    if code != 0:
        raise SystemExit("otool -L failed for %s: %s" % (path, err.strip()))
    deps = []
    seen = set()
    marker = " (compatibility version"
    for line in out.splitlines():
        if not line.startswith(("\t", " ")):
            continue
        text = line.strip()
        if marker not in text:
            continue
        text = text.split(marker, 1)[0]
        if text and text not in seen:
            seen.add(text)
            deps.append(text)
    return deps


def otool_ids(path):
    """Install names from otool -D, one per architecture slice."""
    code, out, err = run(["otool", "-D", path])
    if code != 0:
        raise SystemExit("otool -D failed for %s: %s" % (path, err.strip()))
    ids = []
    seen = set()
    for line in out.splitlines():
        text = line.strip()
        if not text or text.endswith(":"):
            continue
        if text not in seen:
            seen.add(text)
            ids.append(text)
    return ids


def rpaths(path):
    code, out, err = run(["otool", "-l", path])
    if code != 0:
        raise SystemExit("otool -l failed for %s: %s" % (path, err.strip()))
    found = []
    pending = False
    for line in out.splitlines():
        text = line.strip()
        if text.startswith("cmd LC_RPATH"):
            pending = True
            continue
        if pending and text.startswith("path "):
            value = text[len("path ") :].split(" (", 1)[0].strip()
            found.append(value)
            pending = False
    return found


def unsign(path):
    subprocess.run(["codesign", "--remove-signature", path], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def install_change(path, old, new):
    if old == new:
        return
    code, _out, err = run(["install_name_tool", "-change", old, new, path])
    if code != 0:
        raise SystemExit("install_name_tool -change failed for %s (%s -> %s): %s" % (path, old, new, err.strip()))


def install_id(path, new):
    code, _out, err = run(["install_name_tool", "-id", new, path])
    if code != 0:
        raise SystemExit("install_name_tool -id failed for %s (%s): %s" % (path, new, err.strip()))


def add_rpath(path, value):
    if value in rpaths(path):
        return
    code, _out, err = run(["install_name_tool", "-add_rpath", value, path])
    if code != 0:
        raise SystemExit("install_name_tool -add_rpath failed for %s (%s): %s" % (path, value, err.strip()))


def delete_rpath(path, value):
    code, _out, err = run(["install_name_tool", "-delete_rpath", value, path])
    if code != 0:
        raise SystemExit("install_name_tool -delete_rpath failed for %s (%s): %s" % (path, value, err.strip()))


def is_system(path):
    return path.startswith(SYSTEM_PREFIXES)


def loader_target(macho_path, dep, names):
    base = os.path.basename(dep)
    if not base or base not in names:
        return None
    here = os.path.dirname(macho_path)
    sibling = os.path.join(here, base)
    if os.path.lexists(sibling):
        target = sibling
    else:
        target = names[base]
    rel = os.path.relpath(target, here)
    return "@loader_path/" + rel


def rewrite(root):
    root = os.path.abspath(root)
    if root.endswith(".app"):
        root = os.path.join(root, "Contents", "MacOS")
    if not os.path.isdir(root):
        raise SystemExit("not a directory: " + root)
    names = index_names(root)
    machos = []
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in filenames:
            path = os.path.join(dirpath, name)
            if os.path.islink(path):
                continue
            if is_macho(path):
                machos.append(path)
    if not machos:
        raise SystemExit("no Mach-O files under " + root)

    for path in machos:
        idents = otool_ids(path)
        deps = otool_deps(path)
        unsign(path)
        new_id = "@rpath/" + os.path.basename(path)
        # One -id rewrites every slice. Skip libraries whose id is already a
        # system path (nothing in this bundle should be, but do not retarget them).
        if any(ident and not is_system(ident) and ident != new_id for ident in idents):
            install_id(path, new_id)
        skip_ids = set(idents)
        skip_ids.add(new_id)
        for dep in deps:
            if dep in skip_ids:
                continue
            if is_system(dep):
                continue
            target = loader_target(path, dep, names)
            if target:
                # @rpath is already relocatable. Don't grow the load command
                # when the @loader_path form is longer; the bundle rpath covers it.
                if dep.startswith("@") and len(target) > len(dep):
                    continue
                install_change(path, dep, target)
                continue
            if dep.startswith("@loader_path/") or dep.startswith("@executable_path/"):
                continue
            if dep.startswith("@rpath/"):
                continue
            raise SystemExit("unresolved dependency in %s: %s" % (path, dep))
        if os.path.basename(path) == "NonsensicalVideoGenerator":
            add_rpath(path, "@executable_path/lib")
        lib_dir = os.path.join(root, "lib")
        if path.startswith(lib_dir + os.sep):
            add_rpath(path, "@loader_path")
        # ffmpeg and ffprobe live in bin/ and keep @rpath deps when the
        # @loader_path form would be longer. Point that rpath at ../lib.
        if os.path.dirname(path) == os.path.join(root, "bin"):
            if any(dep.startswith("@rpath/") for dep in otool_deps(path)):
                add_rpath(path, "@loader_path/../lib")
        for entry in list(rpaths(path)):
            if entry.startswith("/"):
                delete_rpath(path, entry)

    bad = []
    for path in machos:
        for dep in otool_deps(path):
            for marker in BAD_MARKERS:
                if marker in dep:
                    bad.append("%s -> %s" % (path, dep))
            if dep.startswith("/") and not is_system(dep):
                bad.append("%s -> %s" % (path, dep))
    if bad:
        raise SystemExit("Mach-O dependencies still point outside the bundle:\n" + "\n".join(bad))
    # install_name_tool strips the linker signature. arm64 macOS then
    # kills the process (SIGKILL) until each image is signed again.
    # Signing the apphost signs the whole bundle, which fails until the
    # later codesign --deep. Smoke only needs the bundled tools.
    host = os.path.join(root, "NonsensicalVideoGenerator")
    for path in sorted(machos, key=lambda item: (0 if "/lib/" in item else 1, item)):
        if path == host:
            continue
        code, _out, err = run(["codesign", "--force", "-s", "-", path])
        if code != 0:
            raise SystemExit("codesign failed for %s: %s" % (path, err.strip()))
    print("rewrote %d Mach-O files under %s" % (len(machos), root))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: rewrite_install_names.py <MacOS-dir-or-app>")
    rewrite(sys.argv[1])
