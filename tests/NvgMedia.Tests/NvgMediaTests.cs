using System.Diagnostics;
using NonsensicalVideoGenerator;
using Xunit;

namespace NvgMedia.Tests;

[CollectionDefinition("nvg-media", DisableParallelization = true)]
public class NvgMediaCollection : ICollectionFixture<NvgTempEnv>
{
}

public sealed class NvgTempEnv : IDisposable
{
    public string Home { get; }
    public string Data { get; }
    readonly string? oldHome;
    readonly string? oldData;
    readonly string? oldXdg;
    readonly string? oldPath;

    public NvgTempEnv()
    {
        Home = Directory.CreateTempSubdirectory("nvg-media-home-").FullName;
        Data = Directory.CreateTempSubdirectory("nvg-media-data-").FullName;
        oldHome = Environment.GetEnvironmentVariable("HOME");
        oldData = Environment.GetEnvironmentVariable("NVG_DATA_DIR");
        oldXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        oldPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("HOME", Home);
        Environment.SetEnvironmentVariable("NVG_DATA_DIR", Data);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(Home, ".local", "share"));
        if (!string.IsNullOrEmpty(oldHome))
        {
            string realSupport = Path.Combine(oldHome, "Library", "Application Support", "NonsensicalVideoGenerator");
            string realShare = Path.Combine(oldHome, ".local", "share", "NonsensicalVideoGenerator");
            if (Directory.Exists(realSupport) || Directory.Exists(realShare))
            {
                // The suite must not create these. Existing user data is left untouched.
            }
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", oldHome);
        Environment.SetEnvironmentVariable("NVG_DATA_DIR", oldData);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldXdg);
        Environment.SetEnvironmentVariable("PATH", oldPath);
        ToolPaths.PlatformOverride = null;
        ToolPaths.ResourceRoot = "";
        ToolPaths.MacWellKnownBins = new[] { "/opt/homebrew/bin", "/usr/local/bin" };
        try { Directory.Delete(Home, true); } catch { }
        try { Directory.Delete(Data, true); } catch { }
    }
}

[Collection("nvg-media")]
public class ToolPathsTests
{
    readonly NvgTempEnv env;

    public ToolPathsTests(NvgTempEnv env)
    {
        this.env = env;
        ToolPaths.PlatformOverride = null;
        ToolPaths.ResourceRoot = "";
    }

    [Fact]
    public void BundledToolWinsOverPath()
    {
        string root = Directory.CreateTempSubdirectory("nvg-bin-").FullName;
        string pathDir = Directory.CreateTempSubdirectory("nvg-path-").FullName;
        string? savedPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            AssertTempEnv();
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            string bundled = Path.Combine(root, "bin", "ffmpeg");
            File.WriteAllText(bundled, "bundled\n");
            string onPath = Path.Combine(pathDir, "ffmpeg");
            File.WriteAllText(onPath, "path\n");
            Environment.SetEnvironmentVariable("PATH", pathDir + Path.PathSeparator + savedPath);
            ToolPaths.ResourceRoot = root;
            ToolPaths.PlatformOverride = "linux";

            string resolved = ToolPaths.Resolve("ffmpeg");

            Assert.Equal(bundled, resolved);
            Assert.True(ToolPaths.IsAvailable("ffmpeg"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", savedPath);
            ToolPaths.PlatformOverride = null;
            ToolPaths.ResourceRoot = "";
            Directory.Delete(root, true);
            Directory.Delete(pathDir, true);
        }
    }

    [Fact]
    public void MissingToolStaysBareAndUnavailable()
    {
        string root = Directory.CreateTempSubdirectory("nvg-empty-").FullName;
        string pathDir = Directory.CreateTempSubdirectory("nvg-emptypath-").FullName;
        try
        {
            AssertTempEnv();
            ToolPaths.ResourceRoot = root;
            ToolPaths.PlatformOverride = "linux";
            string? savedPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", pathDir);
            try
            {
            Assert.Equal("ffprobe", ToolPaths.Resolve("ffprobe"));
            Assert.False(ToolPaths.IsAvailable("ffprobe"));
            ToolReport report = ToolPaths.InspectFfmpeg();
            Assert.False(report.Ok);
            Assert.Contains("ffmpeg missing", report.Message);
            Assert.Contains("encoder:libx264", report.Missing);
            Assert.Contains("filter:concat", report.Missing);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", savedPath);
            }
        }
        finally
        {
            ToolPaths.PlatformOverride = null;
            ToolPaths.ResourceRoot = "";
            Directory.Delete(root, true);
            Directory.Delete(pathDir, true);
        }
    }

    [Fact]
    public void WindowsResolutionStaysBundledExeUnlessSystemFlag()
    {
        try
        {
            AssertTempEnv();
            ToolPaths.PlatformOverride = "windows";
            Assert.Equal(@".\bin\ffmpeg.exe", ToolPaths.Ffmpeg(false));
            Assert.Equal("ffmpeg", ToolPaths.Ffmpeg(true));
            Assert.Equal(@".\bin\ffprobe.exe", ToolPaths.Ffprobe(false));
            Assert.Equal(@".\bin\magick.exe", ToolPaths.Magick(false));
            Assert.Equal(@".\bin\yt-dlp.exe", ToolPaths.YtDlp(false));
            Assert.Equal("yt-dlp", ToolPaths.YtDlp(true));
            Assert.Equal(@".\bin\deno.exe", ToolPaths.Deno(true));
            Assert.Equal(@".\bin\vocoder.exe", ToolPaths.Vocoder());
        }
        finally
        {
            ToolPaths.PlatformOverride = null;
        }
    }

    [Fact]
    public void CapabilityProbeReportsMissingCodecsAndFilters()
    {
        AssertTempEnv();
        string root = Directory.CreateTempSubdirectory("nvg-probe-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "bin", "ffmpeg"), "x");
            ToolPaths.ResourceRoot = root;
            ToolPaths.PlatformOverride = "linux";
            ToolReport report = ToolPaths.InspectFfmpeg(runner: arg => arg switch
            {
                "-encoders" => " V....D libx264\n A....D aac\n",
                "-codecs" => " DEV.LS h264\n DEA.L. aac\n",
                "-filters" => " .. concat\n .. scale\n",
                _ => "",
            });
            Assert.False(report.Ok);
            Assert.Contains("codec:pcm_s16le", report.Missing);
            Assert.Contains("filter:reverse", report.Missing);
            Assert.DoesNotContain("encoder:libx264", report.Missing);
            Assert.Contains("missing", report.Message);
        }
        finally
        {
            ToolPaths.PlatformOverride = null;
            ToolPaths.ResourceRoot = "";
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MacFallbackDirectoriesAreSearchedAfterPath()
    {
        string root = Directory.CreateTempSubdirectory("nvg-mac-root-").FullName;
        string pathDir = Directory.CreateTempSubdirectory("nvg-mac-path-").FullName;
        string brewDir = Directory.CreateTempSubdirectory("nvg-mac-brew-").FullName;
        string? savedPath = Environment.GetEnvironmentVariable("PATH");
        string[] savedBins = ToolPaths.MacWellKnownBins;
        try
        {
            AssertTempEnv();
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            string fallback = Path.Combine(brewDir, "ffmpeg");
            File.WriteAllText(fallback, "brew\n");
            string onPath = Path.Combine(pathDir, "ffprobe");
            File.WriteAllText(onPath, "path\n");
            Environment.SetEnvironmentVariable("PATH", pathDir);
            ToolPaths.ResourceRoot = root;
            ToolPaths.PlatformOverride = "macos";
            ToolPaths.MacWellKnownBins = new[] { brewDir, Path.Combine(brewDir, "absent") };

            Assert.Equal(fallback, ToolPaths.Resolve("ffmpeg"));
            Assert.True(ToolPaths.IsAvailable("ffmpeg"));
            Assert.Equal(onPath, ToolPaths.Resolve("ffprobe"));

            string bundled = Path.Combine(root, "bin", "ffmpeg");
            File.WriteAllText(bundled, "bundled\n");
            Assert.Equal(bundled, ToolPaths.Resolve("ffmpeg"));

            string brewMagick = Path.Combine(brewDir, "magick");
            File.WriteAllText(brewMagick, "brew-magick\n");
            Assert.Equal(brewMagick, ToolPaths.Resolve("magick"));

            ToolPaths.PlatformOverride = "linux";
            Assert.Equal("magick", ToolPaths.Resolve("magick"));
            Assert.False(ToolPaths.IsAvailable("magick"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", savedPath);
            ToolPaths.PlatformOverride = null;
            ToolPaths.ResourceRoot = "";
            ToolPaths.MacWellKnownBins = savedBins;
            Directory.Delete(root, true);
            Directory.Delete(pathDir, true);
            Directory.Delete(brewDir, true);
        }
    }

    void AssertTempEnv()
    {
        Assert.Equal(env.Home, Environment.GetEnvironmentVariable("HOME"));
        Assert.Equal(env.Data, Environment.GetEnvironmentVariable("NVG_DATA_DIR"));
        Assert.StartsWith(Path.GetTempPath(), env.Home);
        Assert.StartsWith(Path.GetTempPath(), env.Data);
    }
}

[Collection("nvg-media")]
public class ProcessTreeTests
{
    readonly NvgTempEnv env;

    public ProcessTreeTests(NvgTempEnv env)
    {
        this.env = env;
    }

    [Fact]
    public void CancelKillsOwnedSleepAndLeavesUnrelatedSleep()
    {
        Assert.Equal(env.Data, Environment.GetEnvironmentVariable("NVG_DATA_DIR"));
        Process? owned = null;
        Process? other = null;
        try
        {
            owned = StartSleep();
            other = StartSleep();
            ProcessTree.Track(owned);
            ProcessTree.KillOwned();
            Assert.True(owned.WaitForExit(5000));
            Assert.False(other.HasExited);
        }
        finally
        {
            KillQuiet(owned);
            KillQuiet(other);
        }
    }

    [Fact]
    public void VocoderNoArgReturnsWithinTimeout()
    {
        Assert.Equal(env.Home, Environment.GetEnvironmentVariable("HOME"));
        string? vocoder = Environment.GetEnvironmentVariable("NVG_VOCODER");
        if (!string.IsNullOrEmpty(vocoder) && File.Exists(vocoder))
        {
            var real = new ProcessStartInfo(vocoder)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using Process realProcess = Start(real);
            realProcess.StandardInput.Close();
            var realWatch = Stopwatch.StartNew();
            bool realExited = ProcessTree.WaitForExit(realProcess, TimeSpan.FromSeconds(8));
            realWatch.Stop();
            Assert.True(realExited, "patched vocoder with no arguments must exit on EOF");
            Assert.True(realWatch.Elapsed < TimeSpan.FromSeconds(8));
            string err = realProcess.StandardError.ReadToEnd();
            Assert.Contains("end of input", err);
        }

        var spinner = new ProcessStartInfo(FindProgram("python3"), "-c \"import time\nprint('Modulator filename (required)?', flush=True)\nwhile True:\n    time.sleep(0.2)\n\"")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using Process process = Start(spinner);
        process.StandardInput.Close();
        ProcessTree.Track(process);
        var watch = Stopwatch.StartNew();
        bool finished = ProcessTree.WaitForExit(process, TimeSpan.FromSeconds(3));
        watch.Stop();
        Assert.False(finished);
        Assert.True(process.HasExited);
        Assert.InRange(watch.Elapsed.TotalSeconds, 2, 8);
    }

    [Fact]
    public void OnlyVocoderIsTimedOut()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), ProcessTree.TimeoutFor("/opt/nvg/bin/vocoder"));
        Assert.Equal(TimeSpan.FromMinutes(10), ProcessTree.TimeoutFor(@".\bin\vocoder.exe"));
        Assert.Null(ProcessTree.TimeoutFor("ffmpeg"));
        Assert.Null(ProcessTree.TimeoutFor("/usr/bin/ffmpeg"));
        Assert.Null(ProcessTree.TimeoutFor(@".\bin\ffmpeg.exe"));
        Assert.Null(ProcessTree.TimeoutFor("ffprobe"));
        Assert.Null(ProcessTree.TimeoutFor("magick"));
        Assert.Null(ProcessTree.TimeoutFor("yt-dlp"));
        Assert.Null(ProcessTree.TimeoutFor(@".\bin\yt-dlp.exe"));

        TimeSpan saved = ProcessTree.VocoderTimeout;
        Process? spinner = null;
        Process? quick = null;
        try
        {
            ProcessTree.VocoderTimeout = TimeSpan.FromSeconds(2);
            spinner = Start(new ProcessStartInfo(FindProgram("python3"), "-c \"import time\nwhile True:\n time.sleep(0.2)\n\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            var watch = Stopwatch.StartNew();
            bool vocoderDone = ProcessTree.WaitForExit(spinner, ProcessTree.TimeoutFor("vocoder"));
            watch.Stop();
            Assert.False(vocoderDone);
            Assert.True(spinner.HasExited);
            Assert.InRange(watch.Elapsed.TotalSeconds, 1, 8);

            quick = Start(new ProcessStartInfo(FindProgram("sleep"), "0.3")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            watch.Restart();
            bool ffmpegDone = ProcessTree.WaitForExit(quick, ProcessTree.TimeoutFor("ffmpeg"));
            watch.Stop();
            Assert.True(ffmpegDone);
            Assert.True(quick.HasExited);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        }
        finally
        {
            ProcessTree.VocoderTimeout = saved;
            KillQuiet(spinner);
            KillQuiet(quick);
        }
    }

    static Process StartSleep()
    {
        return Start(new ProcessStartInfo(FindProgram("sleep"), "30")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    static Process Start(ProcessStartInfo info)
    {
        Process? process = Process.Start(info);
        Assert.NotNull(process);
        return process!;
    }

    static string FindProgram(string name)
    {
        foreach (string dir in new[] { "/bin", "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin" })
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new InvalidOperationException(name + " was not found under /bin or /usr/bin");
    }

    static void KillQuiet(Process? process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}

[Collection("nvg-media")]
public class FfmpegNativeTests
{
    readonly NvgTempEnv env;

    public FfmpegNativeTests(NvgTempEnv env)
    {
        this.env = env;
    }

    [Fact]
    public void LibraryNamesFollowTheOsAndDoNotAliasTheLinuxSoname()
    {
        Assert.Equal(env.Data, Environment.GetEnvironmentVariable("NVG_DATA_DIR"));
        Assert.Equal("libavcodec.61.dylib", FfmpegNative.LibraryFileName("avcodec", 61, "macos"));
        Assert.Equal("avcodec-61.dll", FfmpegNative.LibraryFileName("avcodec", 61, "windows"));
        Assert.Equal("libavcodec.so.61", FfmpegNative.LibraryFileName("avcodec", 61, "linux"));
        Assert.DoesNotContain(".so.61.so", FfmpegNative.LibraryFileName("avcodec", 61, "linux"));
        Assert.Equal("libavformat.so.61", FfmpegNative.LibraryFileName("avformat", 61, "linux"));
        Assert.Equal("libavutil.so.59", FfmpegNative.LibraryFileName("avutil", 59, "linux"));
        Assert.Equal("libavfilter.so.10", FfmpegNative.LibraryFileName("avfilter", 10, "linux"));
        Assert.Equal("libswscale.so.8", FfmpegNative.LibraryFileName("swscale", 8, "linux"));
        Assert.Equal("libswresample.so.5", FfmpegNative.LibraryFileName("swresample", 5, "linux"));
        Assert.Equal("libpostproc.so.58", FfmpegNative.LibraryFileName("postproc", 58, "linux"));
    }

    [Fact]
    public void LinuxProbeNameMapsBackToTheRealSoname()
    {
        Assert.Equal("libavcodec.so.61", FfmpegNative.CanonicalLibraryName("libavcodec.so.61.so"));
        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NVG_FFMPEG_LIB")))
        {
            FfmpegNative.Register();
            Assert.True(FfmpegNative.AvcodecLoaded);
            Assert.EndsWith("libavcodec.so.61", FfmpegNative.AvcodecPath.Replace('\\', '/'));
            Assert.DoesNotContain(".so.61.so", FfmpegNative.AvcodecPath);
        }

        Assert.Equal("libavcodec.so.61", FfmpegNative.CanonicalLibraryName("libavcodec.so.61"));
        string? importName = FfmpegNative.CanonicalLibraryName("avcodec-61");
        Assert.NotNull(importName);
        Assert.DoesNotContain(".so.61.so", importName);
        Assert.NotEqual("libavcodec.so.61.so", FfmpegNative.CanonicalLibraryName("libavcodec.so.61.so"));
    }
}
