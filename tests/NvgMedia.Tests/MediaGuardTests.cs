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
