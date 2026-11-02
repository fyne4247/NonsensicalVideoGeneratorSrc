using System.Diagnostics;
using System.Security.Cryptography;
using NonsensicalVideoGenerator;
using Xunit;

namespace NvgMedia.Tests;

[Collection("nvg-media")]
public class MediaGuardTests
{
    readonly NvgTempEnv env;

    public MediaGuardTests(NvgTempEnv env)
    {
        this.env = env;
    }

    [Fact]
    public void Missing_Empty_And_Unprobeable_Are_Rejected_And_Left_In_Place()
    {
        string ffprobe = RequireFfprobe();
        string dir = Path.Combine(env.Data, "guard");
        Directory.CreateDirectory(dir);

        string missing = Path.Combine(dir, "missing.mp4");
        MediaGuard.Verdict missingVerdict = MediaGuard.Inspect(missing, ffprobe);
        Assert.False(missingVerdict.Ok);
        Assert.Equal(MediaGuard.MissingCode, missingVerdict.Code);
        Assert.False(File.Exists(missing));

        string empty = Path.Combine(dir, "empty.mp4");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        byte[] emptyBefore = File.ReadAllBytes(empty);
        MediaGuard.Verdict emptyVerdict = MediaGuard.Inspect(empty, ffprobe);
        Assert.False(emptyVerdict.Ok);
        Assert.Equal(MediaGuard.EmptyCode, emptyVerdict.Code);
        Assert.Equal(emptyBefore, File.ReadAllBytes(empty));

        string truncated = WriteTruncated(dir);
        byte[] truncatedBefore = File.ReadAllBytes(truncated);
        Assert.True(truncatedBefore.Length > 0);
        MediaGuard.Verdict truncatedVerdict = MediaGuard.Inspect(truncated, ffprobe);
        Assert.False(truncatedVerdict.Ok);
        Assert.Equal(MediaGuard.UnprobeableCode, truncatedVerdict.Code);
        Assert.Equal(truncatedBefore, File.ReadAllBytes(truncated));

        string junk = Path.Combine(dir, "junk.mp4");
        File.WriteAllBytes(junk, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.False(MediaGuard.CanOpen(junk, ffprobe, out string junkWhy));
        Assert.Contains("damaged", junkWhy, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, new FileInfo(junk).Length);
    }

    [Fact]
    public void Twenty_Cycles_Of_Bad_Files_Stay_Rejected()
    {
        string ffprobe = RequireFfprobe();
        string dir = Path.Combine(env.Data, "guard-cycles");
        Directory.CreateDirectory(dir);
        string empty = Path.Combine(dir, "empty.mp4");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        string truncated = WriteTruncated(dir);
        string emptyHash = Hash(empty);
        string truncatedHash = Hash(truncated);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(MediaGuard.EmptyCode, MediaGuard.Inspect(empty, ffprobe).Code);
            Assert.Equal(MediaGuard.UnprobeableCode, MediaGuard.Inspect(truncated, ffprobe).Code);
        }
        Assert.Equal(emptyHash, Hash(empty));
        Assert.Equal(truncatedHash, Hash(truncated));
        Assert.True(File.Exists(empty));
        Assert.True(File.Exists(truncated));
    }

    [Fact]
    public void Readable_Clip_Is_Accepted()
    {
        string ffprobe = RequireFfprobe();
        string? ffmpeg = FindTool("ffmpeg");
        if (ffmpeg == null)
            Assert.Fail("ffmpeg was not found");
        string dir = Path.Combine(env.Data, "guard-good");
        Directory.CreateDirectory(dir);
        string good = Path.Combine(dir, "good.mp4");
        Run(ffmpeg!, "-y -loglevel error -f lavfi -i testsrc=size=64x64:rate=10:duration=1 -pix_fmt yuv420p -c:v libx264 -preset ultrafast \"" + good + "\"");
        long before = new FileInfo(good).Length;
        MediaGuard.Verdict verdict = MediaGuard.Inspect(good, ffprobe);
        Assert.True(verdict.Ok, verdict.Message);
        Assert.Equal(before, new FileInfo(good).Length);
    }

    [Fact]
    public void HasContent_Rejects_Missing_And_Empty_Without_A_Probe()
    {
        string dir = Path.Combine(env.Data, "guard-present");
        Directory.CreateDirectory(dir);
        string missing = Path.Combine(dir, "missing.mp4");
        Assert.False(MediaGuard.HasContent(missing, out string missingWhy));
        Assert.Contains("missing", missingWhy, StringComparison.OrdinalIgnoreCase);

        string empty = Path.Combine(dir, "empty.mp4");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        Assert.False(MediaGuard.HasContent(empty, out string emptyWhy));
        Assert.Contains("empty", emptyWhy, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, new FileInfo(empty).Length);

        string bytes = Path.Combine(dir, "bytes.mp4");
        File.WriteAllBytes(bytes, new byte[] { 9, 8, 7, 6 });
        Assert.True(MediaGuard.HasContent(bytes, out string ok));
        Assert.Equal("", ok);
        Assert.Equal(4, new FileInfo(bytes).Length);
    }

    [PosixFact]
    public void Stub_Probe_Stdout_Is_Accepted()
    {
        string dir = Path.Combine(env.Data, "guard-stub-ok");
        Directory.CreateDirectory(dir);
        string stub = Path.Combine(dir, "ffprobe-ok.sh");
        File.WriteAllText(stub, "#!/bin/sh\nprintf '%s\\n' 'mov,mp4'\n");
        Chmod(stub);
        string media = Path.Combine(dir, "clip.mp4");
        File.WriteAllBytes(media, new byte[] { 1, 2, 3, 4 });
        MediaGuard.Verdict verdict = MediaGuard.Inspect(media, stub);
        Assert.True(verdict.Ok, verdict.Message);
        Assert.Equal(4, new FileInfo(media).Length);
    }

    [PosixFact(Timeout = 20000)]
    public async Task Hung_Probe_Is_Killed_Within_The_Timeout()
    {
        string dir = Path.Combine(env.Data, "guard-stub-hang");
        Directory.CreateDirectory(dir);
        string stub = Path.Combine(dir, "ffprobe-hang.sh");
        File.WriteAllText(stub, "#!/bin/sh\n/bin/sleep 137\n");
        Chmod(stub);
        string media = Path.Combine(dir, "clip.mp4");
        byte[] before = new byte[] { 4, 5, 6, 7, 8 };
        File.WriteAllBytes(media, before);
        var watch = Stopwatch.StartNew();
        try
        {
            MediaGuard.Verdict verdict = await Task.Run(() => MediaGuard.Inspect(media, stub));
            watch.Stop();
            Assert.False(verdict.Ok);
            Assert.Equal(MediaGuard.UnprobeableCode, verdict.Code);
            Assert.Contains("timed out", verdict.Message, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(watch.Elapsed.TotalSeconds, 4, 15);
            Assert.Equal(before, File.ReadAllBytes(media));
            Assert.False(ProbeStillRunning(stub), "hung ffprobe stub was still running");
        }
        finally
        {
            KillMatching(stub);
            KillMatching("/bin/sleep 137");
        }
    }

    static void Chmod(string path)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "/bin/chmod",
            ArgumentList = { "+x", path },
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("chmod");
        process.WaitForExit(5000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
    }

    static bool ProbeStillRunning(string token)
    {
        return MatchingPids(token).Count > 0;
    }

    static void KillMatching(string token)
    {
        foreach (int pid in MatchingPids(token))
        {
            try
            {
                Process.GetProcessById(pid).Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }

    static List<int> MatchingPids(string token)
    {
        var pids = new List<int>();
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/pgrep",
            ArgumentList = { "-fl", token },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("pgrep");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(2000);
        foreach (string line in output.Split('\n'))
        {
            if (line.Contains("pgrep", StringComparison.Ordinal))
                continue;
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            string pidText = trimmed.Split(' ', 2)[0];
            if (int.TryParse(pidText, out int pid) && pid > 0)
                pids.Add(pid);
        }
        return pids;
    }

    static string WriteTruncated(string dir)
    {
        string? ffmpeg = FindTool("ffmpeg");
        if (ffmpeg == null)
            Assert.Fail("ffmpeg was not found");
        string full = Path.Combine(dir, "full.mp4");
        string truncated = Path.Combine(dir, "truncated.mp4");
        Run(ffmpeg!, "-y -loglevel error -f lavfi -i testsrc=size=64x64:rate=10:duration=1 -pix_fmt yuv420p -c:v libx264 -preset ultrafast \"" + full + "\"");
        byte[] bytes = File.ReadAllBytes(full);
        Assert.True(bytes.Length > 512);
        File.WriteAllBytes(truncated, bytes.AsSpan(0, 512).ToArray());
        return truncated;
    }

    static string RequireFfprobe()
    {
        string? ffprobe = FindTool("ffprobe");
        if (ffprobe == null)
            Assert.Fail("ffprobe was not found");
        return ffprobe!;
    }

    static string? FindTool(string name)
    {
        foreach (string dir in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" })
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return ToolPaths.FindOnPath(name);
    }

    static string Hash(string path)
    {
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
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
}
