using System.Diagnostics;
using System.Security.Cryptography;
using Xunit;

namespace NvgMedia.Tests;

[Collection("nvg-media")]
public class VideoPlaybackLoadTests
{
    readonly NvgTempEnv env;

    public VideoPlaybackLoadTests(NvgTempEnv env)
    {
        this.env = env;
    }

    [Fact]
    public void Patched_Dll_Matches_The_Recorded_Patch()
    {
        var start = new ProcessStartInfo
        {
            FileName = "sh",
            Arguments = "\"" + Path.Combine(RepoRoot(), "tools/videoplayback/patch-open-input.sh") + "\" --check",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DOTNET_ROOT"] = DotnetRoot();
        start.Environment["NVG_MONOGAME_FRAMEWORK"] = RequireFramework();
        start.Environment["NUGET_PACKAGES"] = NuGetPackages();
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("patch check");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stdout + "\n" + stderr);
        Assert.Contains("patch check ok", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_Load_Throws_For_Bad_Files_And_Decodes_A_Good_File()
    {
        string probe = BuildProbe();
        string ffmpegLib = RequireFfmpegLib();
        string dir = Path.Combine(env.Data, "playback");
        Directory.CreateDirectory(dir);
        string good = Path.Combine(dir, "good.mp4");
        string ffmpeg = RequireTool("ffmpeg");
        RunTool(ffmpeg, "-y -loglevel error -f lavfi -i testsrc=size=64x64:rate=10:duration=1 -bf 0 -pix_fmt yuv420p -c:v libx264 -preset ultrafast \"" + good + "\"");
        string empty = Path.Combine(dir, "empty.mp4");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        byte[] full = File.ReadAllBytes(good);
        Assert.True(full.Length > 512);
        string truncated = Path.Combine(dir, "truncated.mp4");
        File.WriteAllBytes(truncated, full.AsSpan(0, 512).ToArray());
        string emptyHash = Hash(empty);
        string truncatedHash = Hash(truncated);
        string goodHash = Hash(good);

        Cycles(probe, ffmpegLib, empty, "bad", 20);
        Cycles(probe, ffmpegLib, truncated, "bad", 20);
        Cycles(probe, ffmpegLib, good, "good", 20);

        Assert.Equal(emptyHash, Hash(empty));
        Assert.Equal(truncatedHash, Hash(truncated));
        Assert.Equal(goodHash, Hash(good));
    }

    void Cycles(string probe, string ffmpegLib, string file, string mode, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var start = new ProcessStartInfo
            {
                FileName = probe,
                Arguments = mode + " \"" + file + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = env.Data,
            };
            start.Environment["HOME"] = env.Home;
            start.Environment["NVG_DATA_DIR"] = env.Data;
            start.Environment["XDG_DATA_HOME"] = Path.Combine(env.Home, ".local", "share");
            start.Environment["NVG_FFMPEG_LIB"] = ffmpegLib;
            start.Environment["DOTNET_ROOT"] = DotnetRoot();
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("probe");
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail(mode + " cycle " + i + " timed out");
            }
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, mode + " cycle " + i + " exit " + process.ExitCode + "\n" + stdout + "\n" + stderr);
            if (mode == "bad")
                Assert.Contains("managed ", stdout, StringComparison.Ordinal);
            else
            {
                Assert.Contains("decoded=True", stdout, StringComparison.Ordinal);
                Assert.Contains("closed", stdout, StringComparison.Ordinal);
            }
        }
    }

    static string BuildProbe()
    {
        string repo = RepoRoot();
        string output = "/tmp/nvg-hard-20/probe";
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo
        {
            FileName = Dotnet(),
            Arguments = "build \"" + Path.Combine(repo, "tools/videoplayback/LoadProbe/LoadProbe.csproj") + "\" -o \"" + output + "\" -p:MonoGameFrameworkDll=\"" + RequireFramework() + "\" --nologo -v q",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DOTNET_ROOT"] = DotnetRoot();
        start.Environment["NUGET_PACKAGES"] = NuGetPackages();
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("probe build");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stdout + "\n" + stderr);
        // CoreCLR on arm64 will not load an IL-only image marked AMD64. The
        // package file stays AMD64; only this output copy is retargeted.
        RetargetOutput(output);
        string built = Path.Combine(output, "LoadProbe");
        if (OperatingSystem.IsWindows())
            built += ".exe";
        Assert.True(File.Exists(built));
        return built;
    }

    static void RetargetOutput(string output)
    {
        string script = Path.Combine(RepoRoot(), "src/backend/platform/retarget-ilonly.py");
        var start = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = "\"" + script + "\" \"" + output + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("retarget");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stdout + "\n" + stderr);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NonsensicalVideoGenerator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo root was not found from " + AppContext.BaseDirectory);
    }

    static string RequireFramework()
    {
        string user = Environment.UserName;
        string[] candidates =
        {
            Environment.GetEnvironmentVariable("NVG_MONOGAME_FRAMEWORK") ?? "",
            "/Users/" + user + "/.nuget/packages/monogame.framework.desktopgl/3.8.2.1105/lib/net8.0/MonoGame.Framework.dll",
            "/home/" + user + "/.nuget/packages/monogame.framework.desktopgl/3.8.2.1105/lib/net8.0/MonoGame.Framework.dll",
        };
        foreach (string candidate in candidates)
        {
            if (candidate.Length > 0 && File.Exists(candidate))
                return candidate;
        }
        throw new InvalidOperationException("MonoGame.Framework.dll was not found");
    }

    static string RequireFfmpegLib()
    {
        string user = Environment.UserName;
        string[] dirs =
        {
            Environment.GetEnvironmentVariable("NVG_FFMPEG_LIB") ?? "",
            "/opt/homebrew/opt/ffmpeg@7/lib",
            "/usr/local/opt/ffmpeg@7/lib",
            "/usr/lib/x86_64-linux-gnu",
            "/usr/" + user,
        };
        foreach (string dir in dirs)
        {
            if (dir.Length == 0 || !Directory.Exists(dir))
                continue;
            if (File.Exists(Path.Combine(dir, "libavformat.61.dylib"))
                || File.Exists(Path.Combine(dir, "libavformat.so.61"))
                || File.Exists(Path.Combine(dir, "avformat-61.dll")))
                return dir;
        }
        throw new InvalidOperationException("FFmpeg 7 libraries were not found");
    }

    static string RequireTool(string name)
    {
        foreach (string dir in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" })
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new InvalidOperationException(name + " was not found");
    }

    static void RunTool(string fileName, string arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException(fileName);
        string stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(fileName + "\n" + stderr);
    }

    static string Dotnet()
    {
        string candidate = Path.Combine(DotnetRoot(), "dotnet");
        return File.Exists(candidate) ? candidate : "dotnet";
    }

    static string DotnetRoot()
    {
        string? env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "dotnet")))
            return env;
        string mac = "/Users/" + Environment.UserName + "/.dotnet-nvg";
        if (File.Exists(Path.Combine(mac, "dotnet")))
            return mac;
        return "";
    }

    static string NuGetPackages()
    {
        string? env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env))
            return env;
        return "/Users/" + Environment.UserName + "/.nuget/packages";
    }

    static string Hash(string path)
    {
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
