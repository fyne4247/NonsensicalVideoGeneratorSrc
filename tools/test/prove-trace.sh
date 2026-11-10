#!/bin/sh
# Prove the trace launcher on a new disposable directory. Does not touch owner evidence.
set -eu

root=$(mktemp -d /tmp/nvg-hard19-trace.XXXXXX)
case "$root" in
  /tmp/nvg-hard19-trace.*) ;;
  *)
    echo "refusing unexpected proof directory: $root" >&2
    exit 3
    ;;
esac
case "$root" in
  /tmp/nvg-owner-r2*|/tmp/nvg-owner-final*)
    echo "refusing owner evidence root: $root" >&2
    exit 3
    ;;
esac

cleanup() {
  if [ -n "${watcher:-}" ]; then
    if [ -d "${data:-}" ]; then
      touch "$data/trace/stop" 2>/dev/null || true
    fi
  fi
  case "${root:-}" in
    /tmp/nvg-hard19-trace.*) rm -rf "$root" ;;
  esac
}
trap cleanup EXIT

data="$root/data"
materials="$data/library/video/materials"
mkdir -p "$materials"
printf 'alpha\n' > "$data/console.txt"
clip="$materials/clip.bin"

here=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
cat > "$root/write-clip.py" << 'PY'
import sys
import time
data, clip = sys.argv[1], sys.argv[2]
time.sleep(0.4)
with open(data + "/console.txt", "w", encoding="utf-8") as handle:
    handle.write("beta\n")
end = time.time() + 0.6
with open(clip, "wb") as handle:
    while time.time() < end:
        handle.write(b"0123456789abcdef")
        handle.flush()
        time.sleep(0.05)
time.sleep(1.0)
PY

sh "$here/trace-launch.sh" --data "$data" --target "$materials" -- python3 "$root/write-clip.py" "$data" "$clip"

saved=$(find "$data/trace" -name 'console-*.txt' ! -name 'console-running.txt' | head -1)
if [ -z "$saved" ]; then
  echo "previous console was not saved" >&2
  exit 1
fi
grep -q 'alpha' "$saved"
grep -q 'beta' "$data/trace/console-running.txt"
python3 - "$clip" "$data/trace/watch.log" << 'PY'
import hashlib
import pathlib
import sys
clip = pathlib.Path(sys.argv[1]).resolve()
log = pathlib.Path(sys.argv[2]).read_text(encoding="utf-8")
digest = hashlib.sha256(clip.read_bytes()).hexdigest()
empty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
if empty in log:
    raise SystemExit("watcher hashed a file before it had bytes:\n" + log)
if digest not in log:
    raise SystemExit("stable hash missing:\n" + log)
if "FINAL BEGIN" not in log or "FINAL END" not in log:
    raise SystemExit("final listing missing:\n" + log)
print("stable hash", digest)
PY

rm -f "$data/trace/stop"
python3 "$here/watch_tree.py" --data "$data" --target "$materials" &
watcher=$!
i=0
while [ "$i" -lt 50 ]; do
  if [ -f "$data/trace/watcher.lock" ]; then
    break
  fi
  i=$((i + 1))
  python3 -c 'import time; time.sleep(0.05)'
done
if [ ! -f "$data/trace/watcher.lock" ]; then
  echo "first watcher did not lock" >&2
  exit 1
fi
set +e
python3 "$here/watch_tree.py" --data "$data" --target "$materials"
second=$?
set -e
if [ "$second" -ne 2 ]; then
  echo "second watcher exited $second, expected 2" >&2
  exit 1
fi
touch "$data/trace/stop"
wait "$watcher"
watcher=

echo "trace launcher proof passed on $root"
