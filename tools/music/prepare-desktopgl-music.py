#!/usr/bin/env python3
"""Build DesktopGL menu-music sources without touching the original files.

MonoGame's DesktopGL SongProcessor re-encodes a song with:

    ffmpeg -y -i SOURCE -vn -c:a libvorbis -b:a 192000 -ar RATE
        -f:a ogg -strict experimental PLAYED.ogg

OggStream then decodes that ogg with NVorbis and casts each float sample with
(int)(32767f * sample), clamping to a short before OpenAL applies gain.
Samples above full scale are already gone by then.

This writes derived oggs under content/desktopgl-music/. Content.DesktopGL.mgcb
reads those and still emits the music/themeN.xnb names. Original content oggs
and Windows wma files are not modified. Provenance records the source sha256,
the ffmpeg command, and the gain.
"""

import hashlib
import os
import platform
import pwd
import re
import shutil
import subprocess
import sys
import tempfile

TARGET_PLAYED_DBTP = -1.5
LIMIT_DBTP = -1.0


def real_home():
    return pwd.getpwuid(os.getuid()).pw_dir


def repo_root():
    return os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def run(cmd):
    result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if result.returncode != 0:
        sys.stderr.buffer.write(result.stdout)
        raise SystemExit("command failed (%s): %s" % (result.returncode, " ".join(cmd)))
    return result.stdout.decode("utf-8", "replace")


def ffmpeg_argv(binary):
    argv = [binary]
    if platform.system() == "Darwin" and platform.machine() == "arm64":
        kind = subprocess.run(["file", "-b", binary], stdout=subprocess.PIPE, text=True).stdout
        if "x86_64" in kind and "arm64" not in kind:
            argv = ["arch", "-x86_64", binary]
    return argv


def find_mgcb_tool(name):
    override = os.environ.get("NVG_MGCB_FFMPEG") if name == "ffmpeg" else os.environ.get("NVG_MGCB_FFPROBE")
    if override:
        return override
    root = os.path.join(real_home(), ".nuget", "packages", "dotnet-mgcb")
    if not os.path.isdir(root):
        raise SystemExit("dotnet-mgcb is not installed under " + root)
    versions = sorted(os.listdir(root))
    if not versions:
        raise SystemExit("dotnet-mgcb has no versions under " + root)
    base = os.path.join(root, versions[-1], "tools", "net8.0", "any")
    system = platform.system()
    if system == "Darwin":
        path = os.path.join(base, "osx", name)
    elif system == "Linux":
        path = os.path.join(base, "linux", name)
    else:
        path = os.path.join(base, name + ".exe")
    if not os.path.isfile(path):
        raise SystemExit("missing " + path)
    return path


def find_nvorbis():
    override = os.environ.get("NVG_NVORBIS_DLL")
    if override:
        return override
    ffmpeg = find_mgcb_tool("ffmpeg")
    # .../osx/ffmpeg -> .../NVorbis.dll, or .../linux/ffmpeg
    base = os.path.dirname(os.path.dirname(ffmpeg))
    path = os.path.join(base, "NVorbis.dll")
    if not os.path.isfile(path):
        raise SystemExit("missing " + path)
    return path


def song_builds(mgcb_path):
    text = open(mgcb_path, encoding="utf-8").read()
    blocks = re.split(r"(?=^#begin )", text, flags=re.M)
    found = []
    for block in blocks:
        if "SongProcessor" not in block:
            continue
        match = re.search(r"^/build:(.+)$", block, flags=re.M)
        if not match:
            continue
        rel = match.group(1).strip().replace("\\", "/")
        if rel.endswith(".ogg"):
            found.append(rel)
    if not found:
        raise SystemExit("no SongProcessor ogg builds in " + mgcb_path)
    return found


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sample_rate(ffprobe, path):
    text = run(ffprobe + [
        "-v", "error", "-select_streams", "a:0",
        "-show_entries", "stream=sample_rate", "-of", "csv=p=0", path,
    ]).strip()
    return int(text.splitlines()[0])


def ebur128(ffmpeg, path):
    text = run(ffmpeg + [
        "-hide_banner", "-nostats", "-i", path,
        "-af", "ebur128=peak=true", "-f", "null", "-",
    ])
    summary = text.rfind("Integrated loudness:")
    chunk = text[summary:] if summary >= 0 else text
    peak = None
    lufs = None
    for line in chunk.splitlines():
        line = line.strip()
        if line.startswith("Peak:"):
            peak = float(line.split()[1])
        elif line.startswith("I:"):
            lufs = float(line.split()[1])
    if peak is None or lufs is None:
        raise SystemExit("ebur128 summary missing for " + path)
    return peak, lufs


def mgcb_encode(ffmpeg, source, dest, rate):
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    cmd = ffmpeg + [
        "-y", "-i", source, "-vn",
        "-c:a", "libvorbis", "-b:a", "192000", "-ar", str(rate),
        "-f:a", "ogg", "-strict", "experimental", dest,
    ]
    run(cmd)
    return cmd


def attenuate(ffmpeg, source, dest, rate, gain_db):
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    cmd = ffmpeg + [
        "-y", "-i", source, "-vn",
        "-af", "volume=%.2fdB" % gain_db,
        "-c:a", "libvorbis", "-b:a", "192000", "-ar", str(rate),
        "-f:a", "ogg", "-strict", "experimental", dest,
    ]
    run(cmd)
    return cmd


def main(argv):
    root = repo_root()
    out_dir = os.path.join(root, "content", "desktopgl-music")
    if "--out" in argv:
        out_dir = os.path.abspath(argv[argv.index("--out") + 1])
    content = os.path.join(root, "content")
    mgcb = os.path.join(content, "Content.DesktopGL.mgcb")
    ffmpeg = ffmpeg_argv(find_mgcb_tool("ffmpeg"))
    ffprobe = ffmpeg_argv(find_mgcb_tool("ffprobe"))
    songs = song_builds(mgcb)
    os.makedirs(out_dir, exist_ok=True)
    records = []
    for rel in songs:
        source = os.path.join(content, rel)
        if not os.path.isfile(source):
            raise SystemExit("missing original " + source)
        dest = os.path.join(out_dir, rel)
        played = os.path.join(out_dir, "played", rel)
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        os.makedirs(os.path.dirname(played), exist_ok=True)
        rate = sample_rate(ffprobe, source)
        source_sha = sha256_file(source)
        with tempfile.TemporaryDirectory(prefix="nvg-music-") as tmp:
            probe = os.path.join(tmp, "played.ogg")
            source_peak, source_lufs = ebur128(ffmpeg, source)
            mgcb_encode(ffmpeg, source, probe, rate)
            played_peak, played_lufs = ebur128(ffmpeg, probe)
            gain = 0.0
            command = "copy"
            method = "copy"
            if played_peak > LIMIT_DBTP:
                # libvorbis can add a non-linear spike on an already-hot
                # source. That spike does not scale with gain, so the first
                # cut is based on the source peak. Later steps use the
                # pipeline peak and never exceed 6 dB.
                gain = TARGET_PLAYED_DBTP - source_peak
                if gain > -0.01:
                    gain = TARGET_PLAYED_DBTP - played_peak
                for _ in range(4):
                    attenuate(ffmpeg, source, dest, rate, gain)
                    mgcb_encode(ffmpeg, dest, probe, rate)
                    played_peak, played_lufs = ebur128(ffmpeg, probe)
                    if played_peak <= LIMIT_DBTP:
                        break
                    step = TARGET_PLAYED_DBTP - played_peak
                    if step < -6.0:
                        step = -6.0
                    if step > -0.1:
                        step = -0.5
                    gain += step
                else:
                    raise SystemExit("%s played peak %.2f still above %.1f" % (rel, played_peak, LIMIT_DBTP))
                method = "volume"
                command = "ffmpeg -y -i %s -vn -af volume=%.2fdB -c:a libvorbis -b:a 192000 -ar %s -f:a ogg -strict experimental %s" % (
                    rel, gain, rate, rel)
                shutil.copyfile(probe, played)
            else:
                shutil.copyfile(source, dest)
                shutil.copyfile(probe, played)
        records.append({
            "source": rel,
            "source_sha256": source_sha,
            "gain_db": gain,
            "method": method,
            "command": command,
            "sample_rate": rate,
            "source_true_peak_dbtp": source_peak,
            "source_lufs": source_lufs,
            "output_sha256": sha256_file(dest),
            "played_sha256": sha256_file(played),
            "played_true_peak_dbtp": played_peak,
            "played_lufs": played_lufs,
            "played": os.path.relpath(played, out_dir),
        })
        print("%s method=%s gain=%.2f source_peak=%.2f played_true_peak=%.2f played_lufs=%.2f" % (
            rel, method, gain, source_peak, played_peak, played_lufs))
    provenance = os.path.join(out_dir, "PROVENANCE.txt")
    with open(provenance, "w", encoding="utf-8") as handle:
        handle.write("decoder=NVorbis ReadSamples then (int)(32767f * sample) clamp to short\n")
        handle.write("pipeline=ffmpeg -y -i SOURCE -vn -c:a libvorbis -b:a 192000 -ar RATE -f:a ogg -strict experimental\n")
        handle.write("target_played_dbtp=%.1f\n" % TARGET_PLAYED_DBTP)
        for record in records:
            handle.write("---\n")
            for key in (
                "source", "source_sha256", "output_sha256", "gain_db", "method",
                "command", "sample_rate", "source_true_peak_dbtp", "source_lufs",
                "played_sha256", "played_true_peak_dbtp", "played_lufs", "played",
            ):
                value = record[key]
                if isinstance(value, float):
                    handle.write("%s=%.2f\n" % (key, value))
                else:
                    handle.write("%s=%s\n" % (key, value))
    print("wrote " + provenance)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
