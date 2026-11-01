#!/usr/bin/env python3
"""Point ELF DT_RPATH / DT_RUNPATH entries that mention a build prefix at $ORIGIN.

FFmpeg's --enable-rpath stores the absolute build lib directory. After the
package is copied, that path is wrong. $ORIGIN is the directory containing
the library being loaded, which is where the sibling FFmpeg libraries live.

The string is overwritten in place and must fit. If a dynamic-string offset
lands inside the old rpath bytes, the file is left unchanged and reported.

Usage: set_rpath_origin.py <directory> <old-prefix> [replacement]

replacement defaults to $ORIGIN. Binaries in bin/ need $ORIGIN/../lib
because the shared libraries live next to the directory that contains them.
The new string is written in place and must fit in the old one.
"""

import os
import struct
import sys

ELF_MAGIC = b"\x7fELF"
PT_LOAD = 1
PT_DYNAMIC = 2
DT_NULL = 0
DT_NEEDED = 1
DT_STRTAB = 5
DT_RPATH = 15
DT_RUNPATH = 29
TAG_NAMES = {DT_NEEDED: "NEEDED", DT_RPATH: "RPATH", DT_RUNPATH: "RUNPATH"}


def u16(buf, off):
    return struct.unpack_from("<H", buf, off)[0]


def u32(buf, off):
    return struct.unpack_from("<I", buf, off)[0]


def u64(buf, off):
    return struct.unpack_from("<Q", buf, off)[0]


def cstring(buf, off):
    end = buf.find(b"\x00", off)
    if end < 0:
        raise ValueError("unterminated string at %#x" % off)
    return buf[off:end], end - off


def vaddr_to_off(loads, vaddr):
    for virt, off, size in loads:
        if virt <= vaddr < virt + size:
            return off + (vaddr - virt)
    raise ValueError("vaddr %#x is not in a LOAD segment" % vaddr)


def parse_elf(buf):
    if buf[:4] != ELF_MAGIC or buf[4] != 2 or buf[5] != 1:
        return None
    phoff = u64(buf, 32)
    phentsize = u16(buf, 54)
    phnum = u16(buf, 56)
    loads = []
    dynamic = None
    for index in range(phnum):
        off = phoff + index * phentsize
        p_type = u32(buf, off)
        p_offset = u64(buf, off + 8)
        p_vaddr = u64(buf, off + 16)
        p_filesz = u64(buf, off + 32)
        if p_type == PT_LOAD:
            loads.append((p_vaddr, p_offset, p_filesz))
        elif p_type == PT_DYNAMIC:
            dynamic = (p_offset, p_filesz)
    if dynamic is None:
        return None
    return loads, dynamic


def dynamic_entries(buf, loads, dynamic):
    dyn_off, dyn_size = dynamic
    entries = []
    off = dyn_off
    end = dyn_off + dyn_size
    strtab_off = None
    while off + 16 <= end:
        tag = u64(buf, off)
        val = u64(buf, off + 8)
        if tag == DT_NULL:
            break
        if tag == DT_STRTAB:
            strtab_off = vaddr_to_off(loads, val)
        entries.append((off, tag, val))
        off += 16
    return entries, strtab_off


def other_string_offsets(entries, strtab_off, skip_val):
    used = set()
    for _off, tag, val in entries:
        if tag in TAG_NAMES and val != skip_val:
            used.add(strtab_off + val)
    return used


def rewrite_rpath_text(text, old_prefix, replacement):
    if old_prefix not in text:
        return None
    parts = []
    for part in text.split(":"):
        if part == old_prefix or part.startswith(old_prefix + "/"):
            parts.append(replacement)
        else:
            parts.append(part)
    folded = []
    for part in parts:
        if part not in folded:
            folded.append(part)
    return ":".join(folded)


def patch_file(path, old_prefix, replacement):
    with open(path, "rb") as handle:
        original = handle.read()
    parsed = parse_elf(original)
    if parsed is None:
        return "skip"
    loads, dynamic = parsed
    entries, strtab_off = dynamic_entries(original, loads, dynamic)
    if strtab_off is None:
        return "skip"
    buf = bytearray(original)
    changed = False
    notes = []
    for dyn_off, tag, val in entries:
        if tag not in (DT_RPATH, DT_RUNPATH):
            continue
        str_off = strtab_off + val
        old, old_len = cstring(buf, str_off)
        text = old.decode("utf-8", "surrogateescape")
        new_text = rewrite_rpath_text(text, old_prefix, replacement)
        if new_text is None or new_text == text:
            continue
        new = new_text.encode("utf-8")
        if len(new) > old_len:
            notes.append("rpath does not fit in %s (%s)" % (path, text))
            continue
        span = range(str_off + 1, str_off + old_len)
        clashes = [item for item in other_string_offsets(entries, strtab_off, val) if item in span]
        if clashes:
            notes.append("refusing overlapping string in %s" % path)
            continue
        buf[str_off : str_off + old_len] = new + b"\x00" * (old_len - len(new))
        changed = True
        notes.append("%s %s -> %s" % (path, text, new_text))
    if changed:
        with open(path, "wb") as handle:
            handle.write(buf)
        return "patched"
    if notes:
        return "warn"
    return "skip"


def main(argv):
    if len(argv) not in (3, 4):
        raise SystemExit("usage: set_rpath_origin.py <directory> <old-prefix> [replacement]")
    root = os.path.abspath(argv[1])
    old_prefix = os.path.abspath(argv[2])
    replacement = argv[3] if len(argv) == 4 else "$ORIGIN"
    patched = 0
    warned = 0
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in filenames:
            path = os.path.join(dirpath, name)
            if os.path.islink(path):
                continue
            try:
                with open(path, "rb") as handle:
                    magic = handle.read(4)
            except OSError:
                continue
            if magic != ELF_MAGIC:
                continue
            result = patch_file(path, old_prefix, replacement)
            if result == "patched":
                patched += 1
                print("rpath $ORIGIN " + path)
            elif result == "warn":
                warned += 1
                print("warning: left rpath unchanged " + path, file=sys.stderr)
    print("patched %d ELF files, %d left unchanged" % (patched, warned))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
