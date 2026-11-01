#!/usr/bin/env python3
"""Require every Mach-O under a directory to be one architecture.

usage: macho_arch.py --arch x86_64 [--thin] <directory> [<directory> ...]

--thin  A Mach-O that contains the requested arch plus others is rewritten
        with lipo -thin. A file that does not contain that arch is an error.
        Non-Mach-O files are ignored.
"""

import os
import subprocess
import sys

MACHO_MAGICS = {
    b"\xfe\xed\xfa\xce",
    b"\xfe\xed\xfa\xcf",
    b"\xce\xfa\xed\xfe",
    b"\xcf\xfa\xed\xfe",
    b"\xca\xfe\xba\xbe",
    b"\xbe\xba\xfe\xca",
}


def is_macho(path):
    try:
        with open(path, "rb") as handle:
            return handle.read(4) in MACHO_MAGICS
    except OSError:
        return False


def lipo_archs(path):
    result = subprocess.run(
        ["lipo", "-archs", path],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if result.returncode != 0:
        err = result.stderr.decode("utf-8", "replace").strip()
        raise SystemExit("lipo -archs failed for %s: %s" % (path, err))
    return result.stdout.decode("utf-8", "replace").split()


def thin(path, arch):
    dest = path + ".nvg-thin"
    result = subprocess.run(
        ["lipo", path, "-thin", arch, "-output", dest],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if result.returncode != 0:
        subprocess.run(
            ["codesign", "--remove-signature", path],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        result = subprocess.run(
            ["lipo", path, "-thin", arch, "-output", dest],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
    if result.returncode != 0:
        err = result.stderr.decode("utf-8", "replace").strip()
        if os.path.exists(dest):
            os.remove(dest)
        raise SystemExit("lipo -thin %s failed for %s: %s" % (arch, path, err))
    os.replace(dest, path)
    os.chmod(path, os.stat(path).st_mode | 0o755)


def main(argv):
    args = argv[1:]
    arch = None
    do_thin = False
    roots = []
    i = 0
    while i < len(args):
        item = args[i]
        if item == "--thin":
            do_thin = True
        elif item == "--arch":
            i += 1
            if i >= len(args):
                raise SystemExit("usage: macho_arch.py --arch ARCH [--thin] <directory>")
            arch = args[i]
        elif item.startswith("--"):
            raise SystemExit("unknown option %s" % item)
        else:
            roots.append(item)
        i += 1
    if not arch or not roots:
        raise SystemExit("usage: macho_arch.py --arch ARCH [--thin] <directory>")

    problems = []
    seen = 0
    thinned = 0
    for root in roots:
        for dirpath, _dirnames, filenames in os.walk(root):
            for name in filenames:
                path = os.path.join(dirpath, name)
                if os.path.islink(path) or not is_macho(path):
                    continue
                archs = lipo_archs(path)
                if do_thin and arch in archs and archs != [arch]:
                    thin(path, arch)
                    thinned += 1
                    archs = lipo_archs(path)
                seen += 1
                if archs != [arch]:
                    problems.append("%s: %s" % (path, " ".join(archs) or "(none)"))
    if problems:
        sys.stderr.write("Mach-O arch is not %s only:\n%s\n" % (arch, "\n".join(problems)))
        return 1
    print("mach-o arch ok: %d files are %s (%d thinned)" % (seen, arch, thinned))
    if seen == 0:
        sys.stderr.write("error: no Mach-O files under %s\n" % " ".join(roots))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
