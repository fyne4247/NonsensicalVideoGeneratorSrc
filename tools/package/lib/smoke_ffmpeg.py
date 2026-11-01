#!/usr/bin/env python3
"""Render speed.lua's filters and a frei0r nervous pass with a bundled ffmpeg.

usage: smoke_ffmpeg.py <ffmpeg> <ffprobe> <frei0r-dir> <workdir> [report-path]

Children run with PATH=/usr/bin:/bin and FREI0R_PATH set to <frei0r-dir>.
The speed pass uses setpts=0.5*PTS and atempo=2.0, then libx264 + aac.
nervous and colorize are checked the same way. ffprobe must report h264 and aac.
"""

import os
import subprocess
import sys
import time


def run(cmd, frei0r):
    env = os.environ.copy()
    env["PATH"] = "/usr/bin:/bin"
    env["FREI0R_PATH"] = frei0r
    env.pop("LD_LIBRARY_PATH", None)
    env.pop("DYLD_LIBRARY_PATH", None)
    env.pop("DYLD_FALLBACK_LIBRARY_PATH", None)
    return subprocess.run(cmd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)


def require(result, label):
    if result.returncode != 0:
        sys.stderr.buffer.write(result.stdout)
        raise SystemExit("%s failed (%s)" % (label, result.returncode))


def codecs(ffprobe, path):
    result = run(
        [
            ffprobe,
            "-v",
            "error",
            "-show_entries",
            "stream=codec_type,codec_name",
            "-of",
            "csv=p=0",
            path,
        ],
        os.environ.get("FREI0R_PATH", ""),
    )
    require(result, "ffprobe " + path)
    text = result.stdout.decode("utf-8", "replace")
    if "h264" not in text or "aac" not in text:
        raise SystemExit("expected h264 and aac in %s, got %s" % (path, text.strip()))
    return text.strip()


def encode(ffmpeg, ffprobe, frei0r, work, name, video_filter):
    dest = os.path.join(work, name + ".mp4")
    cmd = [
        ffmpeg,
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-f",
        "lavfi",
        "-i",
        "testsrc=size=160x120:rate=10:duration=1",
        "-f",
        "lavfi",
        "-i",
        "sine=frequency=440:sample_rate=48000:duration=1",
        "-filter:v",
        video_filter,
        "-filter:a",
        "atempo=2.0",
        "-c:v",
        "libx264",
        "-preset",
        "ultrafast",
        "-c:a",
        "aac",
        "-b:a",
        "128k",
        "-shortest",
        dest,
    ]
    start = time.monotonic()
    result = run(cmd, frei0r)
    elapsed = time.monotonic() - start
    require(result, name)
    text = codecs(ffprobe, dest)
    print("%s ok %.3fs %s" % (name, elapsed, text))
    return elapsed


def main(argv):
    if len(argv) not in (5, 6):
        raise SystemExit(
            "usage: smoke_ffmpeg.py <ffmpeg> <ffprobe> <frei0r-dir> <workdir> [report-path]"
        )
    ffmpeg, ffprobe, frei0r, work = argv[1:5]
    report = argv[5] if len(argv) == 6 else ""
    os.makedirs(work, exist_ok=True)
    for tool in (ffmpeg, ffprobe):
        if not os.path.isfile(tool) or not os.access(tool, os.X_OK):
            raise SystemExit("not executable: " + tool)
    if not os.path.isdir(frei0r):
        raise SystemExit("frei0r dir missing: " + frei0r)
    speed = encode(ffmpeg, ffprobe, frei0r, work, "speed", "setpts=0.5*PTS")
    encode(ffmpeg, ffprobe, frei0r, work, "nervous", "frei0r=filter_name=nervous")
    encode(
        ffmpeg,
        ffprobe,
        frei0r,
        work,
        "colorize",
        "frei0r=filter_name=colorize:filter_params=0",
    )
    if report:
        with open(report, "w", encoding="utf-8") as handle:
            handle.write("speed_filter=setpts=0.5*PTS\n")
            handle.write("speed_audio=atempo=2.0\n")
            handle.write("speed_preset=ultrafast\n")
            handle.write("speed_size=160x120\n")
            handle.write("speed_elapsed_s=%.3f\n" % speed)
            handle.write("nervous=ok\n")
            handle.write("colorize=ok\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
