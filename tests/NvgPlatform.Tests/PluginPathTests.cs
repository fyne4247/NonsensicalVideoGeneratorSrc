using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

public sealed class PluginPathTests
{
    static readonly string[] Stored =
    {
        "video",
        "audio",
        "image",
        @"audio\sfx",
        @"audio\music",
        @"video\materials",
        @"video\transitions",
        @"video\intros",
        @"video\outros",
        @"video\overlays",
        @"video\renders",
        @"image\noimages",
        "video/fear",
        "video/recall",
        "video/boom",
        "audio/spadinner",
        "audio/whatthe",
        "audio/rave",
        "audio/distort",
        "audio/remix",
        @"audio\vocoder",
    };

    [Fact]
    public void SubType_matches_backslash_and_slash_forms()
    {
        Assert.True(LibraryPathRules.Matches(@"audio\music", "audio", "music"));
        Assert.True(LibraryPathRules.Matches("audio/music", "audio", "music"));
        Assert.True(LibraryPathRules.Matches(@"video\materials", "video", "materials"));
        Assert.True(LibraryPathRules.Matches("video/materials", "video", "materials"));
        Assert.True(LibraryPathRules.Matches("video/recall", "video", "recall"));
        Assert.True(LibraryPathRules.Matches(@"video\recall", "video", "recall"));
        Assert.True(LibraryPathRules.Matches("video/fear", "video", "fear"));
        Assert.True(LibraryPathRules.Matches(@"video\fear", "video", "fear"));
        Assert.True(LibraryPathRules.Matches("audio/spadinner", "audio", "spadinner"));
        Assert.True(LibraryPathRules.Matches(@"audio\spadinner", "audio", "spadinner"));
        Assert.True(LibraryPathRules.Matches("video/boom", "video", "boom"));
        Assert.True(LibraryPathRules.Matches(@"audio\vocoder", "audio", "vocoder"));
        Assert.True(LibraryPathRules.Matches("audio/vocoder", "audio", "vocoder"));
    }

    [Fact]
    public void SubType_rejects_a_different_library_and_keeps_case()
    {
        Assert.False(LibraryPathRules.Matches("video/recall", "video", "fear"));
        Assert.False(LibraryPathRules.Matches(@"audio\music", "audio", "rave"));
        Assert.False(LibraryPathRules.Matches(@"video\Fear", "video", "fear"));
        Assert.False(LibraryPathRules.Matches("video/recall", "audio", "recall"));
        Assert.False(LibraryPathRules.Matches(null, "video", "recall"));
        Assert.False(LibraryPathRules.Matches("video/recall", "", "recall"));
        Assert.False(LibraryPathRules.Matches("video/recall", null, "recall"));
        Assert.False(LibraryPathRules.Matches("", "video", "recall"));
    }

    [Fact]
    public void Windows_backslash_compare_stays_the_same()
    {
        string[] windows =
        {
            @"audio\music",
            @"audio\sfx",
            @"video\materials",
            @"video\renders",
            @"video\fear",
            @"video\recall",
            @"audio\spadinner",
            @"audio\whatthe",
        };
        foreach (string stored in windows)
        {
            int slash = stored.IndexOf('\\');
            string root = stored.Substring(0, slash);
            string sub = stored.Substring(slash + 1);
            bool legacy = stored == root + "\\" + sub;
            Assert.Equal(legacy, LibraryPathRules.Matches(stored, root, sub));
            Assert.True(legacy);
        }
    }

    [Fact]
    public void Registered_plugin_paths_resolve_for_recall_fear_and_dance()
    {
        Assert.Equal("video/recall", Find("video", "recall"));
        Assert.True(LibraryPathRules.Matches(@"video\recall", "video", "recall"));
        Assert.Equal("video/fear", Find("video", "fear"));
        Assert.Equal(@"audio\music", Find("audio", "music"));
        Assert.Equal("audio/spadinner", Find("audio", "spadinner"));
        Assert.Null(Find("video", "not-a-library"));
        Assert.Null(Find("image", "recall"));
    }

    [Fact]
    public void Empty_library_pick_is_rejected_for_both_separators()
    {
        string nativeEmpty = Path.Join("..", "..", "");
        Assert.True(LibraryPathRules.IsEmptyLibraryPick("", nativeEmpty));
        Assert.True(LibraryPathRules.IsEmptyLibraryPick(null, @"..\.."));
        Assert.True(LibraryPathRules.IsEmptyLibraryPick("", @"..\.."));
        Assert.True(LibraryPathRules.IsEmptyLibraryPick("", "../.."));
        Assert.True(LibraryPathRules.IsEmptyLibraryPick("song.mp3", "../.."));
        Assert.True(LibraryPathRules.IsEmptyLibraryPick("song.mp3", @"..\.."));
        Assert.True(LibraryPathRules.IsEmptyLibraryPick("song.mp3", "../../"));
        Assert.True(LibraryPathRules.IsEmptyLibraryPick("song.mp3", @"..\..\"));

        string relative = Path.Combine("library", "audio", "music", "song.mp3");
        string joined = Path.Join("..", "..", relative);
        Assert.False(LibraryPathRules.IsEmptyLibraryPick(relative, joined));
        Assert.NotEqual(Path.Join("..", ".."), joined);

        string windowsFile = @"library\audio\music\song.mp3";
        string windowsJoined = @"..\..\library\audio\music\song.mp3";
        Assert.False(LibraryPathRules.IsEmptyLibraryPick(windowsFile, windowsJoined));
        Assert.False(string.Equals(windowsJoined, @"..\..", StringComparison.Ordinal));
    }

    [Fact]
    public void Handler_uses_the_separator_agnostic_checks()
    {
        string repo = RepoRoot();
        string handler = File.ReadAllText(Path.Combine(repo, "src", "data", "PluginHandler.cs"));
        Assert.DoesNotContain("pair.Value == rootType + \"\\\\\" + subType", handler);
        Assert.Equal(3, Count(handler, "LibraryPathRules.Matches(pair.Value, rootType, subType)"));
        Assert.Contains("LibraryPathRules.IsEmptyLibraryPick(file, thepath)", handler);
        Assert.DoesNotContain("thepath != \"..\\\\..\"", handler);
    }

    static string? Find(string root, string sub)
    {
        foreach (string stored in Stored)
        {
            if (LibraryPathRules.Matches(stored, root, sub))
                return stored;
        }
        return null;
    }

    static int Count(string text, string needle)
    {
        int count = 0;
        int from = 0;
        while (true)
        {
            int at = text.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0)
                return count;
            count++;
            from = at + needle.Length;
        }
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
        throw new InvalidOperationException("Could not find PluginHandler.cs above " + AppContext.BaseDirectory);
    }
}
