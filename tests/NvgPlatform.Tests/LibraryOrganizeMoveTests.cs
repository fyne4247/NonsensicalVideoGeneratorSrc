using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

public sealed class LibraryOrganizeMoveTests
{
    [Fact]
    public void Same_file_paths_match_after_normalization()
    {
        string root = Path.Combine(Path.GetTempPath(), "nvg-same-" + Guid.NewGuid().ToString("n"));
        string src = Path.Combine(root, "library", "video", "materials", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        byte[] bytes = { 1, 2, 3, 4 };
        File.WriteAllBytes(src, bytes);
        try
        {
            string viaDot = Path.Combine(Path.GetDirectoryName(src)!, ".", "clip.mp4");
            Assert.True(LibraryPathRules.SameFile(src, src));
            Assert.True(LibraryPathRules.SameFile(src, viaDot));
            Assert.True(LibraryPathRules.SameFile(src, Path.GetFullPath(src)));
            Assert.False(LibraryPathRules.SameFile(src, Path.Combine(Path.GetDirectoryName(src)!, "other.mp4")));
            Assert.False(LibraryPathRules.SameFile(src, null));
            // A same-file organize must not enter the delete. File.Move onto itself
            // succeeds on macOS and throws on Windows; either way the bytes stay.
            try
            {
                File.Move(src, src);
            }
            catch (IOException)
            {
            }
            Assert.Equal(bytes, File.ReadAllBytes(src));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Organize_will_not_delete_a_file_that_is_its_own_destination()
    {
        string data = File.ReadAllText(Path.Combine(RepoRoot(), "src", "data", "LibraryData.cs"));
        int start = data.IndexOf("LibraryFile Organize", StringComparison.Ordinal);
        int end = data.IndexOf("Download thread", start, StringComparison.Ordinal);
        string body = data.Substring(start, end - start);
        int same = body.IndexOf("LibraryPathRules.SameFile(source.Path, finalpath)", StringComparison.Ordinal);
        int delete = body.IndexOf("File.Delete(finalpath)", StringComparison.Ordinal);
        Assert.True(same >= 0 && delete > same);
        Assert.Contains("Source and destination are the same file.", body, StringComparison.Ordinal);
        Assert.Contains("Replacing existing library file:", body, StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "src", "data", "LibraryData.cs")))
                return dir;
            string? parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;
            dir = parent;
        }
        throw new InvalidOperationException("Could not find the repo above " + AppContext.BaseDirectory);
    }
}
