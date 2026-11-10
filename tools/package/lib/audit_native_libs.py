#!/usr/bin/env python3
"""Fail if a bundled native file links Homebrew, /usr/local, or a missing library.

usage: audit_native_libs.py <directory> [<directory> ...]
"""

import os
import subprocess
import sys

BAD_MARKERS = ("/opt/homebrew", "/usr/local")
MAC_SYSTEM = ("/usr/lib/", "/System/")
# .NET publishes this provider with an optional NEEDED entry. The runtime
# uses it only when LTTng is installed, and the media tools do not link it.
OPTIONAL_MISSING = {
    ("libcoreclrtraceptprovider.so", "liblttng-ust.so.0"),
}


def run(cmd):
    result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return result.returncode, result.stdout.decode("utf-8", "replace"), result.stderr.decode("utf-8", "replace")


def is_macho(path):
    try:
        with open(path, "rb") as handle:
            magic = handle.read(4)
    except OSError:
        return False
    return magic in {
        b"\xfe\xed\xfa\xce",
        b"\xfe\xed\xfa\xcf",
        b"\xce\xfa\xed\xfe",
        b"\xcf\xfa\xed\xfe",
        b"\xca\xfe\xba\xbe",
        b"\xbe\xba\xfe\xca",
    }


def is_elf(path):
    try:
        with open(path, "rb") as handle:
            return handle.read(4) == b"\x7fELF"
    except OSError:
        return False


def walk(roots):
    for root in roots:
        for dirpath, _dirnames, filenames in os.walk(root):
            for name in filenames:
                path = os.path.join(dirpath, name)
                if os.path.islink(path):
                    continue
                yield path


def audit_macho(path, problems):
    code, out, err = run(["otool", "-L", path])
    if code != 0:
        problems.append("otool -L failed for %s: %s" % (path, err.strip()))
        return
    marker = " (compatibility version"
    for line in out.splitlines():
        if not line.startswith(("\t", " ")):
            continue
        text = line.strip()
        if marker not in text:
            continue
        dep = text.split(marker, 1)[0]
        for bad in BAD_MARKERS:
            if bad in dep:
                problems.append("%s -> %s" % (path, dep))
        if dep.startswith("/") and not dep.startswith(MAC_SYSTEM):
            problems.append("%s -> %s" % (path, dep))
    code, out, err = run(["otool", "-l", path])
    if code != 0:
        problems.append("otool -l failed for %s: %s" % (path, err.strip()))
        return
    pending = False
    for line in out.splitlines():
        text = line.strip()
        if text.startswith("cmd LC_RPATH"):
            pending = True
            continue
        if pending and text.startswith("path "):
            value = text[len("path ") :].split(" (", 1)[0].strip()
            pending = False
            for bad in BAD_MARKERS:
                if bad in value:
                    problems.append("rpath %s -> %s" % (path, value))
            if value.startswith("/"):
                problems.append("rpath %s -> %s" % (path, value))


def audit_elf(path, problems):
    code, out, err = run(["ldd", path])
    if code != 0 and "not a dynamic executable" in (out + err):
        return
    text = out + err
    if "not a dynamic executable" in text or "statically linked" in text:
        return
    for line in text.splitlines():
        if "not found" in line:
            missing_name = line.split("=>", 1)[0].strip()
            if (os.path.basename(path), missing_name) in OPTIONAL_MISSING:
                continue
            problems.append("missing %s: %s" % (path, line.strip()))
        for bad in BAD_MARKERS:
            if bad in line:
                problems.append("%s: %s" % (path, line.strip()))
    code, out, err = run(["readelf", "-d", path])
    if code != 0:
        return
    for line in out.splitlines():
        if "RPATH" not in line and "RUNPATH" not in line:
            continue
        for bad in BAD_MARKERS:
            if bad in line:
                problems.append("rpath %s: %s" % (path, line.strip()))
        if "[" in line:
            value = line.split("[", 1)[1].split("]", 1)[0]
            for part in value.split(":"):
                if part.startswith("/") and "$ORIGIN" not in part:
                    problems.append("rpath %s -> %s" % (path, part))


def main(argv):
    if len(argv) < 2:
        raise SystemExit("usage: audit_native_libs.py <directory> [<directory> ...]")
    problems = []
    system = os.uname().sysname
    for path in walk(argv[1:]):
        if system == "Darwin" and is_macho(path):
            audit_macho(path, problems)
        elif system == "Linux" and is_elf(path):
            audit_elf(path, problems)
    if problems:
        sys.stderr.write("\n".join(problems) + "\n")
        return 1
    print("native library audit ok (%d roots)" % (len(argv) - 1))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
