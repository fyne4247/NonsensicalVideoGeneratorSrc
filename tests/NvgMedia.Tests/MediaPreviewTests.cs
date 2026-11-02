using System.Diagnostics;
using System.Globalization;
using System.Text;
using NonsensicalVideoGenerator;
using Xunit;

namespace NvgMedia.Tests;

[Collection("nvg-media")]
public class MediaPreviewTests
{
    readonly NvgTempEnv env;

    public MediaPreviewTests(NvgTempEnv env)
    {
        this.env = env;
    }

    [Fact]
    public void ProbeText_TreatsOnlyZeroAsFriendly()
    {
        Assert.False(MediaPreview.ProbeTextIsFriendly(null));
        Assert.False(MediaPreview.ProbeTextIsFriendly(""));
        Assert.False(MediaPreview.ProbeTextIsFriendly("2"));
        Assert.False(MediaPreview.ProbeTextIsFriendly("1\n"));
        Assert.False(MediaPreview.ProbeTextIsFriendly("N/A"));
        Assert.True(MediaPreview.ProbeTextIsFriendly("0"));
        Assert.True(MediaPreview.ProbeTextIsFriendly("0\r\n"));
    }

    [Fact]
    public void Fingerprint_ChangesWhenTheFileChanges()
    {
        string dir = Path.Combine(env.Data, "fingerprint");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "clip.mp4");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
        string first = MediaPreview.ThumbnailFile(dir, file, 58, 46);
        File.AppendAllText(file, "x");
        string second = MediaPreview.ThumbnailFile(dir, file, 58, 46);
        Assert.NotEqual(first, second);
        Assert.EndsWith(".bmp", first, StringComparison.Ordinal);
        Assert.DoesNotContain("thumb.bmp", first, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelledThumbnail_DoesNotStartWork()
    {
        MediaPreview.ThumbnailResult result = MediaPreview.EnsureThumbnail(
            "ffmpeg",
            "does-not-matter.mp4",
            Path.Combine(env.Data, "cancel-thumbs"),
            58,
            46,
            MediaPreview.VideoKind,
            null,
            () => false);
        Assert.True(result.Cancelled);
        Assert.False(result.Ok);
    }

    [Fact]
    public void PlaybackArguments_ForceNoBFrames()
    {
        string args = MediaPreview.PlaybackArguments("in.mp4", "out.mp4");
        Assert.Contains("-bf 0", args, StringComparison.Ordinal);
        Assert.Contains("-preset ultrafast", args, StringComparison.Ordinal);
        Assert.Contains("libx264", args, StringComparison.Ordinal);
    }

    [Fact]
    public void HighProfile_IsTranscoded_Baseline_IsNot()
    {
        string? ffmpeg = FindTool("ffmpeg");
        string? ffprobe = FindTool("ffprobe");
        if (ffmpeg == null || ffprobe == null)
        {
            if (!OperatingSystem.IsWindows())
                Assert.Fail("ffmpeg and ffprobe were not found");
            return;
        }
        string dir = Path.Combine(env.Data, "play");
        Directory.CreateDirectory(dir);
        string baseline = Path.Combine(dir, "baseline.mp4");
        string high = Path.Combine(dir, "high profile.mp4");
        Encode(ffmpeg, baseline, bf: 0, durationSeconds: 2, withAudio: true);
        Encode(ffmpeg, high, bf: 2, durationSeconds: 2, withAudio: true);
        Assert.True(MediaPreview.IsDesktopPlayerFriendly(ffprobe, baseline), "baseline has_b_frames");
        Assert.False(MediaPreview.IsDesktopPlayerFriendly(ffprobe, high), "high has_b_frames " + ProbeBFrames(ffprobe, high));

        string cache = Path.Combine(dir, "cache");
        string baselinePlay = MediaPreview.EnsurePlayable(ffmpeg, ffprobe, baseline, cache, dir);
        string highPlay = MediaPreview.EnsurePlayable(ffmpeg, ffprobe, high, cache, dir);
        Assert.Equal(Path.GetFullPath(baseline), baselinePlay);
        Assert.NotEqual(Path.GetFullPath(high), highPlay);
        Assert.True(MediaPreview.IsInsideDirectory(highPlay, cache));
        Assert.True(MediaPreview.IsDesktopPlayerFriendly(ffprobe, highPlay), "transcode has_b_frames " + ProbeBFrames(ffprobe, highPlay));

        string again = MediaPreview.EnsurePlayable(ffmpeg, ffprobe, high, cache, dir);
        Assert.Equal(highPlay, again);

        string missing = MediaPreview.EnsurePlayable(Path.Combine(dir, "no-ffmpeg"), ffprobe, high, Path.Combine(dir, "cache-missing"), dir);
        Assert.Equal(Path.GetFullPath(high), missing);
    }

    [Fact]
    public void Picker_FortyClips_OpenDoesNotWait_SecondPassIsCache()
    {
        string? ffmpeg = FindTool("ffmpeg");
        if (ffmpeg == null)
        {
            if (!OperatingSystem.IsWindows())
                Assert.Fail("ffmpeg was not found");
            return;
        }
        string dir = Path.Combine(env.Data, "picker");
        string library = Path.Combine(dir, "library");
        string cache = Path.Combine(dir, "thumbs");
        Directory.CreateDirectory(library);
        string master = Path.Combine(dir, "master.mp4");
        Encode(ffmpeg, master, bf: 0, durationSeconds: 4, withAudio: false);
        var clips = new List<string>();
        for (int i = 0; i < 40; i++)
        {
            string name = Path.Combine(library, "clip " + i.ToString("00", CultureInfo.InvariantCulture) + ".mp4");
            File.Copy(master, name);
            clips.Add(name);
        }

        string shared = Path.Combine(dir, "thumb.bmp");
        long legacyMs = TimeLegacySerial(ffmpeg, clips, shared, 58, 46);

        var open = Stopwatch.StartNew();
        ThumbnailBatch batch = MediaPreview.BeginThumbnails(ffmpeg, clips, cache, 58, 46, MediaPreview.VideoKind, library);
        open.Stop();
        var firstWall = Stopwatch.StartNew();
        bool finished = batch.Wait(TimeSpan.FromMinutes(3));
        firstWall.Stop();
        long readyMs = firstWall.ElapsedMilliseconds;
        Assert.True(finished, "thumbnail batch did not finish");
        IReadOnlyList<MediaPreview.ThumbnailResult> results = batch.Results;
        Assert.Equal(40, results.Count);
        Assert.All(results, result =>
        {
            Assert.True(result.Ok, result.Error);
            Assert.False(result.FromCache);
            Assert.True(MediaPreview.BmpMatches(result.Path, 58, 46));
            Assert.DoesNotContain("thumb.bmp", result.Path, StringComparison.Ordinal);
        });
        Assert.Equal(40, results.Select(result => result.Path).Distinct().Count());

        var second = Stopwatch.StartNew();
        ThumbnailBatch cached = MediaPreview.BeginThumbnails(ffmpeg, clips, cache, 58, 46, MediaPreview.VideoKind, library);
        long secondOpenMs = second.ElapsedMilliseconds;
        Assert.True(cached.Wait(TimeSpan.FromSeconds(30)));
        second.Stop();
        Assert.All(cached.Results, result =>
        {
            Assert.True(result.Ok, result.Error);
            Assert.True(result.FromCache);
        });

        string report = "legacySerialMs=" + legacyMs.ToString(CultureInfo.InvariantCulture)
            + " openMs=" + open.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)
            + " firstBatchWallMs=" + readyMs.ToString(CultureInfo.InvariantCulture)
            + " secondOpenMs=" + secondOpenMs.ToString(CultureInfo.InvariantCulture)
            + " secondWallMs=" + second.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
        File.WriteAllText("/tmp/nvg-hard14-picker-timing.txt", report + "\n");
        Assert.True(open.ElapsedMilliseconds < 500, report);
        Assert.True(second.ElapsedMilliseconds < legacyMs, report);
    }

    static long TimeLegacySerial(string ffmpeg, IReadOnlyList<string> files, string sharedBmp, int width, int height)
    {
        var sw = Stopwatch.StartNew();
        foreach (string file in files)
        {
            if (File.Exists(sharedBmp))
                File.Delete(sharedBmp);
            string args = "-i \"" + file + "\" -filter_complex \"[0:v]thumbnail,scale="
                + width.ToString(CultureInfo.InvariantCulture) + ":"
                + height.ToString(CultureInfo.InvariantCulture)
                + "[thumb]\" -map \"[thumb]\" -frames:v 1 -update 1 -y \"" + sharedBmp + "\"";
            Run(ffmpeg, args);
            if (!File.Exists(sharedBmp))
                throw new InvalidOperationException("legacy thumbnail missing for " + file);
        }
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    static void Encode(string ffmpeg, string output, int bf, int durationSeconds, bool withAudio)
    {
        string duration = durationSeconds.ToString(CultureInfo.InvariantCulture);
        string preset = bf == 0 ? "ultrafast" : "medium";
        var args = new StringBuilder();
        args.Append("-y -loglevel error -f lavfi -i testsrc=size=192x144:rate=30:duration=").Append(duration);
        if (withAudio)
            args.Append(" -f lavfi -i sine=frequency=440:sample_rate=44100:duration=").Append(duration);
        args.Append(" -c:v libx264 -preset ").Append(preset);
        args.Append(" -profile:v high -bf ").Append(bf.ToString(CultureInfo.InvariantCulture));
        args.Append(" -pix_fmt yuv420p");
        if (withAudio)
            args.Append(" -c:a aac -ar 44100 -shortest");
        else
            args.Append(" -an");
        args.Append(" \"").Append(output).Append('"');
        Run(ffmpeg, args.ToString());
    }

    static string ProbeBFrames(string ffprobe, string path)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            Arguments = "-v error -select_streams v:0 -show_entries stream=has_b_frames -of csv=p=0 \"" + path + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe");
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return stdout.Trim();
    }

    static void Run(string fileName, string arguments)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException(fileName);
        string stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(fileName + " " + arguments + "\n" + stderr);
    }

    static string? FindTool(string name)
    {
        string envName = name == "ffprobe" ? "NVG_FFPROBE" : "NVG_FFMPEG";
        string? env = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
            return env;
        foreach (string dir in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" })
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return ToolPaths.FindOnPath(name);
    }
}
