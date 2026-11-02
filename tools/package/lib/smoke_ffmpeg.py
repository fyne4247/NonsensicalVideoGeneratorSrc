#!/usr/bin/env python3
"""Render speed.lua's filters and a frei0r nervous pass with a bundled ffmpeg.

usage: smoke_ffmpeg.py <ffmpeg> <ffprobe> <frei0r-dir> <workdir> [report-path]

Children run with PATH=/usr/bin:/bin and FREI0R_PATH set to <frei0r-dir>.
The speed pass uses setpts=0.5*PTS and atempo=2.0, then libx264 + aac.
nervous and colorize are checked the same way. ffprobe must report h264 and aac.
libmp3lame must be listed by ffmpeg -encoders. A missing encoder fails the
smoke. The spadinner pass then runs that plugin's MP3 commands on a tiny
fixture: silence.mp3, an SFX converted to MP3, the concat protocol, and the
final amix into an h264/aac mp4.
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


def require_listed(ffmpeg, flag, name):
    result = run([ffmpeg, "-hide_banner", flag], "")
    require(result, "ffmpeg " + flag)
    text = result.stdout.decode("utf-8", "replace")
    for line in text.splitlines():
        if name in line.split():
            return
    raise SystemExit("%s %s missing from ffmpeg %s" % (flag, name, ffmpeg))


def ffprobe_codec(ffprobe, path, expected):
    result = run(
        [
            ffprobe,
            "-v",
            "error",
            "-show_entries",
            "stream=codec_name",
            "-of",
            "csv=p=0",
            path,
        ],
        "",
    )
    require(result, "ffprobe " + path)
    text = result.stdout.decode("utf-8", "replace")
    if expected not in text.split():
        raise SystemExit("expected %s in %s, got %s" % (expected, path, text.strip()))
    return text.strip()


def spadinner(ffmpeg, ffprobe, frei0r, work):
    """The stock spadinner.lua MP3 path, on a 1 second fixture.

    Command lines match baseplugins/user/spadinner/spadinner.lua with the
    plugin's default Max Delay of 0.75s and one sound effect.
    """
    require_listed(ffmpeg, "-encoders", "libmp3lame")
    require_listed(ffmpeg, "-muxers", "mp3")
    material = os.path.join(work, "material.mp4")
    sfx = os.path.join(work, "sfx.wav")
    silence = os.path.join(work, "silence.mp3")
    converted = os.path.join(work, "1.mp3")
    delayed = os.path.join(work, "1-delayed.mp3")
    combined = os.path.join(work, "combinedspadinner.mp3")
    clip = os.path.join(work, "materialclip.mp4")
    output = os.path.join(work, "spadinner.mp4")
    require(
        run(
            [
                ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=1",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=1",
                "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac",
                "-shortest", material,
            ],
            frei0r,
        ),
        "spadinner fixture",
    )
    require(
        run(
            [
                ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=44100:duration=0.4",
                "-c:a", "pcm_s16le", sfx,
            ],
            frei0r,
        ),
        "spadinner sfx",
    )
    # silence.mp3, the command that failed in the owner log.
    require(
        run(
            [
                ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi",
                "-i", "anullsrc=channel_layout=stereo:sample_rate=44100",
                "-t", "0.75",
                "-c:a", "libmp3lame", "-b:a", "192k",
                silence,
            ],
            frei0r,
        ),
        "silence.mp3",
    )
    ffprobe_codec(ffprobe, silence, "mp3")
    require(
        run(
            [
                ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                "-ss", "0", "-i", material,
                "-filter:v", "fps=10,scale=160:120",
                "-vcodec", "libx264", "-crf", "28", "-preset", "ultrafast",
                "-ac", "2", "-c:a", "aac", "-b:a", "160k",
                "-reset_timestamps", "1", "-shortest", "-fflags", "+genpts",
                "-af", "aresample=async=1000",
                clip,
            ],
            frei0r,
        ),
        "spadinner material clip",
    )
    require(
        run(
            [
                ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                "-i", sfx, "-b:a", "224k", "-ar", "44100",
                "-c:a", "libmp3lame", converted,
            ],
            frei0r,
        ),
        "spadinner convert",
    )
    ffprobe_codec(ffprobe, converted, "mp3")
    # concat: protocol, then a short seek, same arguments as the plugin.
    require(
        run(
            [
                ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                "-ss", "0.65",
                "-i", "concat:%s|%s" % (silence, converted),
                "-b:a", "224k", "-ar", "44100", "-c:a", "libmp3lame",
                delayed,
            ],
            frei0r,
        ),
        "spadinner concat",
    )
    require(
        run(
            [
                ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                "-i", "concat:%s" % delayed,
                "-acodec", "copy", combined,
            ],
            frei0r,
        ),
        "spadinner copy",
    )
    ffprobe_codec(ffprobe, combined, "mp3")
    require(
        run(
            [
                ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
                "-async", "30", "-i", clip, "-i", combined,
                "-filter_complex",
                "[0:a]volume=1[0a];[1:a]volume=1.5[1a];"
                "[0a][1a]amix=inputs=2:duration=shortest,aresample=async=1000[out]",
                "-map", "[out]", "-map", "0:v",
                "-vcodec", "libx264", "-crf", "28", "-preset", "ultrafast",
                "-ac", "2", "-c:a", "aac", "-b:a", "160k",
                "-reset_timestamps", "1", "-shortest", "-fflags", "+genpts",
                output,
            ],
            frei0r,
        ),
        "spadinner mix",
    )
    text = codecs(ffprobe, output)
    print("spadinner ok %s" % text)
    return text


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
    require_listed(ffmpeg, "-encoders", "libmp3lame")
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
    spadinner(ffmpeg, ffprobe, frei0r, work)
    if report:
        with open(report, "w", encoding="utf-8") as handle:
            handle.write("speed_filter=setpts=0.5*PTS\n")
            handle.write("speed_audio=atempo=2.0\n")
            handle.write("speed_preset=ultrafast\n")
            handle.write("speed_size=160x120\n")
            handle.write("speed_elapsed_s=%.3f\n" % speed)
            handle.write("nervous=ok\n")
            handle.write("colorize=ok\n")
            handle.write("encoder=libmp3lame\n")
            handle.write("spadinner=ok\n")
            handle.write("silence_mp3=libmp3lame -b:a 192k -t 0.75\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
