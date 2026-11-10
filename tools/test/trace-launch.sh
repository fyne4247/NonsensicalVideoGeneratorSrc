#!/bin/sh
# Save the previous console.txt, keep one watcher on this data root, then run a command.
#
# Usage:
#   trace-launch.sh --data DIR --target PATH [--target PATH...] -- COMMAND...
#
# The previous console.txt is copied to trace/console-YYYYMMDD-HHMMSS.txt before
# COMMAND runs. The watcher mirrors the new console.txt to trace/console-running.txt
# and hashes --target paths only after their size is unchanged for two polls.
# A second watcher for the same data root exits 2. Owner evidence roots are refused.
set -eu

data=
list=$(mktemp)
trap 'rm -f "$list"' EXIT

while [ "$#" -gt 0 ]; do
  case "$1" in
    --data)
      data=$2
      shift 2
      ;;
    --target)
      printf '%s\n' "$2" >> "$list"
      shift 2
      ;;
    --)
      shift
      break
      ;;
    *)
      echo "unknown argument: $1" >&2
      echo "usage: trace-launch.sh --data DIR --target PATH [--target PATH...] -- COMMAND..." >&2
      exit 1
      ;;
  esac
done

if [ -z "$data" ]; then
  echo "missing --data" >&2
  exit 1
fi

case "$data" in
  /tmp/nvg-owner-r2|/tmp/nvg-owner-r2/*|/tmp/nvg-owner-final|/tmp/nvg-owner-final/*)
    echo "refusing owner evidence root: $data" >&2
    exit 3
    ;;
esac

if [ ! -d "$data" ]; then
  echo "data root is not a directory: $data" >&2
  exit 1
fi

here=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
mkdir -p "$data/trace"

# A live watcher already owns this data root. Do not remove its stop file
# and do not launch. A dead lock is left for the new watcher to replace.
lock="$data/trace/watcher.lock"
if [ -f "$lock" ]; then
  holder=$(head -n 1 "$lock" 2>/dev/null || true)
  if [ -n "$holder" ] && kill -0 "$holder" 2>/dev/null; then
    echo "watcher refused this data root" >&2
    exit 2
  fi
fi
rm -f "$data/trace/stop"

if [ -f "$data/console.txt" ]; then
  stamp=$(date +%Y%m%d-%H%M%S)
  dest="$data/trace/console-$stamp.txt"
  n=2
  while [ -e "$dest" ]; do
    dest="$data/trace/console-$stamp-$n.txt"
    n=$((n + 1))
  done
  cp -p "$data/console.txt" "$dest"
  echo "saved previous console as $dest"
fi

python3 "$here/watch_tree.py" --data "$data" --targets-from "$list" &
watcher=$!

ready=0
i=0
while [ "$i" -lt 50 ]; do
  if [ -f "$lock" ]; then
    holder=$(head -n 1 "$lock" 2>/dev/null || true)
    if [ "$holder" = "$watcher" ]; then
      ready=1
      break
    fi
  fi
  if ! kill -0 "$watcher" 2>/dev/null; then
    echo "watcher refused this data root" >&2
    wait "$watcher" 2>/dev/null || true
    exit 2
  fi
  i=$((i + 1))
  # Portable short wait. The lock is taken before the first poll.
  python3 -c 'import time; time.sleep(0.05)'
done

holder=$(head -n 1 "$lock" 2>/dev/null || true)
if [ "$ready" -ne 1 ] || [ "$holder" != "$watcher" ]; then
  echo "watcher did not take the lock" >&2
  # Another watcher may own the lock. Stop only the process this script started.
  if kill -0 "$watcher" 2>/dev/null; then
    kill "$watcher" 2>/dev/null || true
  fi
  wait "$watcher" 2>/dev/null || true
  exit 2
fi

status=0
if [ "$#" -gt 0 ]; then
  (
    cd "$data"
    NVG_DATA_DIR=$data NVG_TRACE=1 "$@"
  ) || status=$?
fi

touch "$data/trace/stop"
wait "$watcher" || true
exit "$status"
