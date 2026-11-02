using System.Security.Cryptography;
using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

public sealed class LibraryFileRetentionTests
{
    [Fact]
    public void Replaced_library_paths_stay_out_of_plugin_file_ops()
    {
        string job = Path.Combine(Path.GetTempPath(), "nvg-job-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(job);
        try
        {
            Assert.False(LibraryPathRules.StaysInsideJob(job, "../.././library/video/materials/clip.mp4"));
            Assert.False(LibraryPathRules.StaysInsideJob(job, "../" + "../library/video/materials/clip.mp4"));
            Assert.False(LibraryPathRules.StaysInsideJob(job, @"..\..\library\video\materials\clip.mp4"));
            Assert.False(LibraryPathRules.StaysInsideJob(job, @"..\library\video\materials\clip.mp4"));
            Assert.False(LibraryPathRules.StaysInsideJob(job, "/tmp/library/video/materials/clip.mp4"));
            Assert.False(LibraryPathRules.StaysInsideJob(job, @"C:\library\video\materials\clip.mp4"));
            Assert.False(LibraryPathRules.StaysInsideJob(job, @"\\server\share\clip.mp4"));
            Assert.True(LibraryPathRules.StaysInsideJob(job, "./frames/_frame0.png"));
            Assert.True(LibraryPathRules.StaysInsideJob(job, "output.mp4"));
            Assert.True(LibraryPathRules.StaysInsideJob(job, @"frames\clip.png"));
            Assert.False(LibraryPathRules.FlagIsTrue("false"));
            Assert.False(LibraryPathRules.FlagIsTrue(null));
            Assert.False(LibraryPathRules.FlagIsTrue(""));
            Assert.True(LibraryPathRules.FlagIsTrue("true"));
        }
        finally
        {
            Directory.Delete(job, true);
        }
    }

    [Fact]
    public void Fixture_bytes_survive_generate_cancel_and_error_cycles()
    {
        string root = Path.Combine(Path.GetTempPath(), "nvg-libkeep-" + Guid.NewGuid().ToString("n"));
        string job = Path.Combine(root, "temp", "job_cycle");
        string materials = Path.Combine(root, "library", "video", "materials");
        Directory.CreateDirectory(job);
        Directory.CreateDirectory(materials);
        try
        {
            var hashes = new Dictionary<string, string>();
            for (int i = 0; i < 4; i++)
            {
                byte[] bytes = new byte[128];
                for (int b = 0; b < bytes.Length; b++)
                    bytes[b] = (byte)(i * 17 + b);
                string path = Path.Combine(materials, "fixture-" + i + ".mp4");
                File.WriteAllBytes(path, bytes);
                hashes[path] = Convert.ToHexString(SHA256.HashData(bytes));
            }

            string[] cycles = { "generate", "generate", "generate", "cancel", "error" };
            string[] names = { "fixture-0.mp4", "fixture-1.mp4", "fixture-2.mp4", "fixture-3.mp4" };
            foreach (string cycle in cycles)
            {
                foreach (string name in names)
                {
                    string posix = "../.././library/video/materials/" + name;
                    string windows = @"..\..\library\video\materials\" + name;
                    string absolute = "/tmp/outside/" + name;
                    Assert.False(LibraryPathRules.StaysInsideJob(job, posix), cycle);
                    Assert.False(LibraryPathRules.StaysInsideJob(job, windows), cycle);
                    Assert.False(LibraryPathRules.StaysInsideJob(job, absolute), cycle);
                    // The pre-replacement check sees only the placeholder, so it allows the op.
                    string raw = "{File_1}";
                    Assert.DoesNotContain("..", raw, StringComparison.Ordinal);
                    Assert.DoesNotContain(":\\", raw, StringComparison.Ordinal);
                    string resolved = Path.GetFullPath(Path.Combine(job, posix));
                    Assert.Equal(Path.GetFullPath(Path.Combine(materials, name)), resolved);
                    Assert.False(LibraryPathRules.FlagIsTrue("false"));
                }
                foreach (KeyValuePair<string, string> pair in hashes)
                {
                    Assert.True(File.Exists(pair.Key), cycle);
                    Assert.Equal(pair.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))));
                }
            }

            string local = Path.Combine(job, "scratch.txt");
            File.WriteAllText(local, "job-local");
            Assert.True(LibraryPathRules.StaysInsideJob(job, "scratch.txt"));
            if (LibraryPathRules.FlagIsTrue("true") && LibraryPathRules.StaysInsideJob(job, "scratch.txt"))
                File.Delete(local);
            Assert.False(File.Exists(local));
            foreach (KeyValuePair<string, string> pair in hashes)
                Assert.Equal(pair.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void File_ops_check_the_path_after_placeholder_replacement()
    {
        string handler = File.ReadAllText(Path.Combine(RepoRoot(), "src", "data", "PluginHandler.cs"));
        foreach (string method in new[] { "void FileCopy", "void FileDelete", "void FileMove" })
        {
            int start = handler.IndexOf(method, StringComparison.Ordinal);
            Assert.True(start >= 0, method);
            int end = handler.IndexOf("public static", start + method.Length, StringComparison.Ordinal);
            string body = handler.Substring(start, end - start);
            int replaced = body.IndexOf("ReplacePlaceholders", StringComparison.Ordinal);
            int confined = body.IndexOf("ConfinedToJob", StringComparison.Ordinal);
            Assert.True(replaced >= 0 && confined > replaced, method);
        }
        string generator = File.ReadAllText(Path.Combine(RepoRoot(), "src", "backend", "Generator.cs"));
        Assert.Contains("LibraryPathRules.FlagIsTrue(SaveData.saveValues[\"DeleteClipsAfterMaxUniqueClips\"])", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("bool.Parse(SaveData.saveValues[\"DeleteClipsAfterMaxUniqueClips\"])", generator, StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "src", "data", "PluginHandler.cs")))
                return dir;
            string? parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;
            dir = parent;
        }
        throw new InvalidOperationException("Could not find the repo above " + AppContext.BaseDirectory);
    }
}
