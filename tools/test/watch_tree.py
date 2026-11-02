#!/usr/bin/env python3
"""Watch files and hash them only after their size stays still.

One watcher per data root. A second one exits 2 while the first is alive.
console.txt is mirrored to trace/console-running.txt. The launcher saves the
previous console.txt under a timestamped name before this starts.

This does not write outside the data root it was given, and it refuses the
owner evidence directories.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import sys
import time

POLL_SECONDS = 0.2
STABLE_POLLS = 2
EMPTY_SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
REFUSED = (
    "/tmp/nvg-owner-r2",
    "/tmp/nvg-owner-final",
)


class FileState:
    def __init__(self) -> None:
        self.size: int | None = None
        self.stable = 0
        self.hashed: int | None = None
        self.digest = ""
        self.present = False


def refuse(path: str) -> None:
    real = os.path.realpath(path)
    for banned in REFUSED:
        banned_real = os.path.realpath(banned)
        if real == banned_real or real.startswith(banned_real + os.sep):
            raise SystemExit("refusing owner evidence root: " + real)


def pid_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def acquire_lock(lock_path: str) -> None:
    os.makedirs(os.path.dirname(lock_path), exist_ok=True)
    for _ in range(2):
        try:
            fd = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
        except FileExistsError:
            holder = read_pid(lock_path)
            if pid_alive(holder):
                raise SystemExit(2)
            try:
                os.unlink(lock_path)
            except FileNotFoundError:
                pass
            continue
        os.write(fd, str(os.getpid()).encode("ascii") + b"\n")
        os.close(fd)
        return
    raise SystemExit(2)


def read_pid(lock_path: str) -> int:
    try:
        text = open(lock_path, "r", encoding="ascii", errors="replace").read()
    except OSError:
        return 0
    line = text.strip().splitlines()
    if not line:
        return 0
    try:
        return int(line[0].strip())
    except ValueError:
        return 0


def release_lock(lock_path: str) -> None:
    if read_pid(lock_path) != os.getpid():
        return
    try:
        os.unlink(lock_path)
    except FileNotFoundError:
        pass


def sha256_file(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def list_targets(targets: list[str]) -> list[str]:
    found: list[str] = []
    for target in targets:
        if os.path.isfile(target):
            found.append(os.path.realpath(target))
            continue
        if not os.path.isdir(target):
            continue
        for root, dirs, files in os.walk(target):
            dirs[:] = [name for name in dirs if not name.startswith(".")]
            for name in files:
                if name.startswith("."):
                    continue
                found.append(os.path.realpath(os.path.join(root, name)))
    found.sort()
    return found


def mirror_console(data_root: str) -> None:
    source = os.path.join(data_root, "console.txt")
    if not os.path.isfile(source):
        return
    dest = os.path.join(data_root, "trace", "console-running.txt")
    try:
        source_stat = os.stat(source)
        if os.path.isfile(dest):
            dest_stat = os.stat(dest)
            if dest_stat.st_size == source_stat.st_size and dest_stat.st_mtime_ns == source_stat.st_mtime_ns:
                return
        with open(source, "rb") as src, open(dest, "wb") as out:
            while True:
                chunk = src.read(1024 * 1024)
                if not chunk:
                    break
                out.write(chunk)
        os.utime(dest, ns=(source_stat.st_atime_ns, source_stat.st_mtime_ns))
    except OSError:
        return


def note(log, line: str) -> None:
    print(line, flush=True)
    log.write(line + "\n")
    log.flush()


def consider(path: str, state: FileState, log) -> None:
    if not os.path.isfile(path):
        if state.present:
            note(log, "GONE " + path)
            state.present = False
            state.size = None
            state.stable = 0
            state.hashed = None
            state.digest = ""
        return
    try:
        size = os.path.getsize(path)
    except OSError:
        return
    state.present = True
    if state.size != size:
        state.size = size
        state.stable = 1
        return
    state.stable += 1
    if state.stable < STABLE_POLLS or state.hashed == size:
        return
    try:
        digest = sha256_file(path)
    except OSError:
        return
    state.hashed = size
    state.digest = digest
    note(log, "HASH " + path + " size=" + str(size) + " sha256=" + digest)


def write_final(states: dict[str, FileState], log) -> None:
    note(log, "FINAL BEGIN")
    for path in sorted(states):
        state = states[path]
        if not state.present or state.hashed is None:
            continue
        note(log, "FINAL " + path + " size=" + str(state.hashed) + " sha256=" + state.digest)
    note(log, "FINAL END")


def load_targets(args: argparse.Namespace) -> list[str]:
    targets = list(args.target)
    if args.targets_from:
        with open(args.targets_from, "r", encoding="utf-8") as handle:
            for line in handle:
                item = line.strip()
                if item:
                    targets.append(item)
    return targets


def main() -> int:
    parser = argparse.ArgumentParser(description="Hash watched files after their size is stable.")
    parser.add_argument("--data", required=True, help="NVG data root. trace/ is created here.")
    parser.add_argument("--target", action="append", default=[], help="File or directory to watch. Repeatable.")
    parser.add_argument("--targets-from", help="Text file of target paths, one per line.")
    args = parser.parse_args()
    data = os.path.realpath(args.data)
    refuse(data)
    targets = load_targets(args)
    for target in targets:
        refuse(target)
    trace = os.path.join(data, "trace")
    os.makedirs(trace, exist_ok=True)
    lock_path = os.path.join(trace, "watcher.lock")
    stop_path = os.path.join(trace, "stop")
    log_path = os.path.join(trace, "watch.log")
    acquire_lock(lock_path)
    states: dict[str, FileState] = {}
    try:
        with open(log_path, "a", encoding="utf-8") as log:
            note(log, "WATCHER " + str(os.getpid()) + " data=" + data)
            while not os.path.exists(stop_path):
                mirror_console(data)
                for path in list_targets(targets):
                    state = states.get(path)
                    if state is None:
                        state = FileState()
                        states[path] = state
                    consider(path, state, log)
                time.sleep(POLL_SECONDS)
            mirror_console(data)
            for path in list_targets(targets):
                state = states.get(path)
                if state is None:
                    state = FileState()
                    states[path] = state
                consider(path, state, log)
            write_final(states, log)
    finally:
        release_lock(lock_path)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit as exc:
        code = exc.code
        if code is None:
            raise
        if isinstance(code, int):
            sys.exit(code)
        print(code, file=sys.stderr)
        sys.exit(2)
