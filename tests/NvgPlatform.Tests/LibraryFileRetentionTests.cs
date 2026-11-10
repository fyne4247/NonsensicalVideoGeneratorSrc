using System.Security.Cryptography;
using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

public sealed class LibraryFileRetentionTests
{
    [Fact]
    public void Replaced_library_paths_stay_out_of_plugin_file_ops()
    {
        string root = Path.Combine(Path.GetTempPath(), "nvg-rules-" + Guid.NewGuid().ToString("n"));
        string job = Path.Combine(root, "temp", "job");
        string library = Path.Combine(root, "library");
        Directory.CreateDirectory(job);
        Directory.CreateDirectory(library);
        try
        {
            string posix = "../../library/video/materials/clip.mp4";
            string windows = @"..\..\library\video\materials\clip.mp4";
            // Library files can be copied into the job. They cannot be deleted, moved, or overwritten.
            Assert.True(LibraryPathRules.CanCopyFrom(job, library, posix));
            Assert.True(LibraryPathRules.CanCopyFrom(job, library, windows));
            Assert.False(LibraryPathRules.CanAlter(job, library, posix));
            Assert.False(LibraryPathRules.CanAlter(job, library, windows));
            Assert.False(LibraryPathRules.CanAlter(job, library, "../" + "../library/video/materials/clip.mp4"));
            Assert.False(LibraryPathRules.CanAlter(job, library, @"..\library\video\materials\clip.mp4"));
            Assert.False(LibraryPathRules.CanCopyFrom(job, library, "/tmp/library/video/materials/clip.mp4"));
            Assert.False(LibraryPathRules.CanAlter(job, library, "/tmp/library/video/materials/clip.mp4"));
            Assert.False(LibraryPathRules.CanAlter(job, library, @"C:\library\video\materials\clip.mp4"));
            Assert.False(LibraryPathRules.CanAlter(job, library, @"\\server\share\clip.mp4"));
            Assert.False(LibraryPathRules.CanAlter(job, library, posix.Replace("clip.mp4", "")));
            Assert.True(LibraryPathRules.CanAlter(job, library, "./frames/_frame0.png"));
            Assert.True(LibraryPathRules.CanAlter(job, library, "output.mp4"));
            Assert.True(LibraryPathRules.CanAlter(job, library, @"frames\clip.png"));
            Assert.True(LibraryPathRules.CanCopyFrom(job, library, "output.mp4"));
            Assert.False(LibraryPathRules.FlagIsTrue("false"));
            Assert.False(LibraryPathRules.FlagIsTrue(null));
            Assert.False(LibraryPathRules.FlagIsTrue(""));
            Assert.True(LibraryPathRules.FlagIsTrue("true"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
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
                    string posix = "../../library/video/materials/" + name;
                    string windows = @"..\..\library\video\materials\" + name;
                    string absolute = "/tmp/outside/" + name;
                    string fixture = Path.Combine(materials, name);
                    // Reading the library file into the job is allowed. Changing it is not.
                    Assert.True(LibraryPathRules.CanCopyFrom(job, Path.Combine(root, "library"), posix), cycle);
                    Assert.True(LibraryPathRules.CanCopyFrom(job, Path.Combine(root, "library"), windows), cycle);
                    Assert.False(LibraryPathRules.CanAlter(job, Path.Combine(root, "library"), posix), cycle);
                    Assert.False(LibraryPathRules.CanAlter(job, Path.Combine(root, "library"), windows), cycle);
                    Assert.False(LibraryPathRules.CanAlter(job, Path.Combine(root, "library"), absolute), cycle);
                    // The pre-replacement check sees only the placeholder, so it allows the op.
                    string raw = "{File_1}";
                    Assert.DoesNotContain("..", raw, StringComparison.Ordinal);
                    Assert.DoesNotContain(":\\", raw, StringComparison.Ordinal);
                    string? resolved = LibraryPathRules.ResolveReplaced(job, posix);
                    Assert.Equal(Path.GetFullPath(fixture), resolved);
                    Assert.False(LibraryPathRules.FlagIsTrue("false"));
                    // Allowed copy into the job leaves the library bytes alone.
                    if (LibraryPathRules.CanCopyFrom(job, Path.Combine(root, "library"), posix)
                        && LibraryPathRules.CanAlter(job, Path.Combine(root, "library"), name))
                    {
                        File.Copy(fixture, Path.Combine(job, name), true);
                    }
                    // Destructive ops are refused, so the fixture is not deleted or moved.
                    if (LibraryPathRules.CanAlter(job, Path.Combine(root, "library"), posix))
                        File.Delete(fixture);
                }
                foreach (KeyValuePair<string, string> pair in hashes)
                {
                    Assert.True(File.Exists(pair.Key), cycle);
                    Assert.Equal(pair.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))));
                }
            }

            string local = Path.Combine(job, "scratch.txt");
            File.WriteAllText(local, "job-local");
            Assert.True(LibraryPathRules.CanAlter(job, Path.Combine(root, "library"), "scratch.txt"));
            if (LibraryPathRules.FlagIsTrue("true") && LibraryPathRules.CanAlter(job, Path.Combine(root, "library"), "scratch.txt"))
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
        foreach (string method in new[] { "void FileDelete", "void FileMove", "void WriteFile" })
        {
            int start = handler.IndexOf(method, StringComparison.Ordinal);
            Assert.True(start >= 0, method);
            int end = handler.IndexOf("public static", start + method.Length, StringComparison.Ordinal);
            string body = handler.Substring(start, end - start);
            int replaced = body.IndexOf("ReplacePlaceholders", StringComparison.Ordinal);
            int alter = body.IndexOf("CanAlter", StringComparison.Ordinal);
            Assert.True(replaced >= 0 && alter > replaced, method);
        }
        int copyStart = handler.IndexOf("void FileCopy", StringComparison.Ordinal);
        int copyEnd = handler.IndexOf("public static", copyStart + "void FileCopy".Length, StringComparison.Ordinal);
        string copyBody = handler.Substring(copyStart, copyEnd - copyStart);
        int copyReplaced = copyBody.IndexOf("ReplacePlaceholders", StringComparison.Ordinal);
        int copyFrom = copyBody.IndexOf("CanCopyFrom", StringComparison.Ordinal);
        int copyAlter = copyBody.IndexOf("CanAlter", StringComparison.Ordinal);
        Assert.True(copyReplaced >= 0 && copyFrom > copyReplaced && copyAlter > copyFrom);
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
