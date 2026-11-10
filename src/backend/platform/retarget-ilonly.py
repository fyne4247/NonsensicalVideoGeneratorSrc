#!/usr/bin/env python3
"""Rewrite IL-only AMD64 assemblies to the AnyCPU machine id.

CoreCLR on arm64 refuses a PE machine of AMD64 even when COMIMAGE_FLAGS_ILONLY
is set. MonoGame.Extended2 and Extended.VideoPlayback are pure IL marked x64.
Native images (no ILONLY flag) are left alone. The packages/ originals stay
untouched; this runs against the DesktopGL output directory.
"""
import struct
import sys
from pathlib import Path

IMAGE_FILE_MACHINE_I386 = 0x14C
IMAGE_FILE_MACHINE_AMD64 = 0x8664
COMIMAGE_FLAGS_ILONLY = 0x1


def corflags(data: bytes):
    if data[:2] != b"MZ":
        return None
    e = struct.unpack_from("<I", data, 0x3C)[0]
    if data[e:e + 4] != b"PE\0\0":
        return None
    machine = struct.unpack_from("<H", data, e + 4)[0]
    magic = struct.unpack_from("<H", data, e + 24)[0]
    if magic == 0x10B:
        dd_off = e + 24 + 96
        dd_count = struct.unpack_from("<I", data, e + 24 + 92)[0]
    elif magic == 0x20B:
        dd_off = e + 24 + 112
        dd_count = struct.unpack_from("<I", data, e + 24 + 108)[0]
    else:
        return machine, None
    if dd_count < 15:
        return machine, None
    rva, size = struct.unpack_from("<II", data, dd_off + 14 * 8)
    if size < 20:
        return machine, None
    nsec = struct.unpack_from("<H", data, e + 6)[0]
    optsize = struct.unpack_from("<H", data, e + 20)[0]
    sec = e + 24 + optsize
    off = None
    for i in range(nsec):
        o = sec + i * 40
        vsz, va, rsz, raw = struct.unpack_from("<IIII", data, o + 8)
        if va <= rva < va + max(vsz, rsz):
            off = raw + (rva - va)
            break
    if off is None:
        return machine, None
    return machine, struct.unpack_from("<I", data, off + 16)[0]


def main() -> int:
    root = Path(sys.argv[1])
    changed = 0
    for path in sorted(root.glob("*.dll")):
        data = bytearray(path.read_bytes())
        info = corflags(data)
        if info is None:
            continue
        machine, flags = info
        if machine != IMAGE_FILE_MACHINE_AMD64 or flags is None:
            continue
        if not (flags & COMIMAGE_FLAGS_ILONLY):
            print(f"skip native {path.name}")
            continue
        e = struct.unpack_from("<I", data, 0x3C)[0]
        struct.pack_into("<H", data, e + 4, IMAGE_FILE_MACHINE_I386)
        path.write_bytes(data)
        changed += 1
        print(f"retargeted {path.name} AMD64 -> I386 (ILONLY)")
    print(f"retargeted {changed} assemblies")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
