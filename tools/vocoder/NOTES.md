# Native vocoder build trial (S2)

`build-vocoder.sh` builds a native (non-Windows) replacement for the
app's Windows-only `bin/vocoder.exe`. It fetches the upstream source at a
pinned commit into a scratch directory, builds with the system C
compiler, and copies the `vocoder` binary to a directory given on the
command line. No sudo/apt/brew and no system packages are used; it needs
only `git`, `cc` (overridable via `CC`), and `make`.

## Upstream

- Repository: https://github.com/borsboom/vocoder (Zerius Vocoder 1.3)
- Pinned commit: `3faed27b4cb457c458a1feddc309c3fd0271af5a`
  (master HEAD at fetch time; the script verifies this hash after
  cloning and refuses to build anything else)
- Plain C (K&R era), no dependencies beyond libc `-lm`; upstream ships
  its own `Makefile` and reads/writes AIFF, RIFF/WAVE and raw "extended"
  80-bit-float files.
- Licence: no formal SPDX licence file. `README.txt` ("Copyright"
  section) grants: "You are free to do whatever you like with the
  vocoder, as long as the copyright notice stays intact and you note any
  changes. There is no warranty." Zerius Vocoder is Copyright (C)
  1996-1999, 2002 Emanuel Borsboom; the FFT code (`fftn.c`, `fftaux.c`,
  `fft.h`, `spt.h`) is Copyright (C) 1993 Steven Trainoff; the IEEE
  extended-precision conversion code (`extended.c`) is Copyright (C)
  1988-1991 Apple Computer Inc. Permissive, notice-preserving terms.

## How the app invokes the vocoder (read-only findings)

The binary is resolved in `src/data/PluginHandler.cs:88`
(`CommandType.Vocoder`) as
`Path.Combine(<assembly dir>, "bin", "vocoder.exe")`, and executed by
`Command.Call()` (`src/data/PluginHandler.cs:127+`) with
`Arguments = args`, `UseShellExecute = false`, redirected
stdout/stderr, and `WorkingDirectory` defaulting to the assembly
directory. `src/backend/Generator.cs` never names the vocoder directly;
it goes through `PluginHandler.PickRandom(...).Call(...)`.

The exact arguments come from the stock plugin
`baseplugins/user/stock/vocoder.lua`:

```lua
functions.runVocoder("-b 1024 \"" .. modulator .. "\" \"" .. carrierresult .. "\" \"" .. vocoded .. "\"")
```

i.e. the app's argument shape is:

```
vocoder -b 1024 <modulator.wav> <carrier.wav> <vocoded.wav>
```

with the three temp files `modulator.wav`, `carrier.wav`, `vocoded.wav`
in the working directory, both inputs pre-converted by the plugin with
`-acodec pcm_s16le -ar 44100 -ac 1`.

## Commands run

Usage: `tools/vocoder/build-vocoder.sh <output-dir> [scratch-dir]`

Sample run (identical on both hosts):

```sh
ffmpeg -f lavfi -i "sine=frequency=440:duration=2" -acodec pcm_s16le -ar 44100 -ac 1 -y modulator.wav
ffmpeg -f lavfi -i "anoisesrc=d=2:c=pink:a=0.5"    -acodec pcm_s16le -ar 44100 -ac 1 -y carrier.wav
./vocoder -b 1024 modulator.wav carrier.wav vocoded.wav
ffprobe -hide_banner vocoded.wav
```

## Mac (arm64, macOS) — PASS

- Compiler: `Apple clang version 21.0.0 (clang-2100.3.34.2)` via `cc`
- ffmpeg/ffprobe 9.0.2 (Homebrew)
- Build from a clean scratch dir: pass, exit 0.
- Warnings: 7x `-Wdeprecated-non-prototype` in `fftn.c` (K&R-style
  function definitions in the 1993 Trainoff FFT code). No errors.
- Sample run: `vocoder -b 1024 ...` exit 0 (progress bar to 100%, 85/85
  windows).
- `ffprobe vocoded.wav`: `Duration: 00:00:02.02, bitrate: 705 kb/s`,
  `pcm_s16le, 44100 Hz, 1 channels, s16` — valid WAV. PASS.

## Linux (ssh 750, Linux Mint 22.3, Ubuntu noble base, x86_64, ~/nvg-vocoder/) — PASS

- Compiler: `cc (Ubuntu 13.3.0-6ubuntu2~24.04.1) 13.3.0` (gcc)
- System ffmpeg/ffprobe present; a C compiler exists, so no early stop
  was needed.
- Build from a clean scratch dir (`~/nvg-vocoder/scratch`): pass,
  exit 0. Output: `~/nvg-vocoder/out/vocoder`
  (`ELF 64-bit LSB pie executable, x86-64`, not stripped).
- Warnings: 7x `-Wunused-result` (`fgets`/`fread` return values ignored)
  in `main.c`, `wave.c`, `aiff.c`, `riff.c`. No errors.
- Sample run inside `~/nvg-vocoder/` only: exit 0.
- `ffprobe vocoded.wav`: `Duration: 00:00:02.02, bitrate: 705 kb/s`,
  `pcm_s16le, 44100 Hz, 1 channels, s16` — valid WAV. PASS.

## Result

Both hosts build and run the native vocoder from a clean scratch
directory with the app's exact argument shape (`-b 1024 mod carrier
out`), and both outputs are valid WAVs per `ffprobe`. A native
`vocoder` binary is a drop-in for `bin/vocoder.exe` on macOS/Linux as
far as the stock plugin is concerned.

Known limitations: the upstream code is K&R-era C and produces compiler
warnings on both hosts (harmless, listed above). The binary writes AIFF
by default only when the output filename ends in `.aif`/`.aiff`; the
plugin's `.wav` extension gets RIFF/WAVE, which is what the app expects.

## EOF exit

`build-vocoder.sh` patches `prompt_user` in `main.c` after checking out
the pinned commit. Upstream ignores the `fgets` result, so `argc <= 1`
treats a closed stdin as an empty line, prints "You must enter a value."
and asks again. The patch exits with status 1 when `fgets` returns NULL
and writes `vocoder: end of input` to stderr. The upstream copyright
notice stays. The stock plugin always passes `-b 1024` and three paths,
so the patch only changes the no-argument launch. DesktopGL
`Command.Call` also closes stdin. Only the vocoder command is bounded
(`ProcessTree.VocoderTimeout`, 10 minutes). ffmpeg, ffprobe, magick, and
yt-dlp wait until they exit. Cancel still kills the processes this job started.
