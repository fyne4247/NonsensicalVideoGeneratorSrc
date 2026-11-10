using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class AppPathsTests
{
    readonly Sandbox box;

    public AppPathsTests(Sandbox box)
    {
        this.box = box;
    }

    [Fact]
    public void Writable_follows_the_os_and_resources_stay_at_the_bundle()
    {
        string asm = AppPaths.AssemblyDirectory();
        string data = Path.GetFullPath(box.Data);
        Assert.Equal(DataRoots.ResourceRoot, AppPaths.Resources);
        Assert.Equal(AppContext.BaseDirectory, AppPaths.Resources);
        Assert.Equal(asm, AppPaths.Resolve("windows", data, asm));
        Assert.Equal(".", AppPaths.Resolve("windows", data, ""));
        Assert.Equal(".", AppPaths.Resolve("windows", data, null));
        Assert.Equal(data, AppPaths.Resolve("macos", data, asm));
        Assert.Equal(data, AppPaths.Resolve("linux", data, asm));
        Assert.Equal("", AppPaths.Resolve("linux", "", asm));
        if (DataRoots.PlatformKind() == "windows")
        {
            Assert.Equal(asm, AppPaths.Writable);
        }
        else
        {
            Assert.Equal(data, AppPaths.Writable);
            Assert.NotEqual(Path.GetFullPath(asm), Path.GetFullPath(AppPaths.Writable));
            string cache = Path.GetFullPath(Path.Combine(AppPaths.Writable, "cache"));
            string render = Path.GetFullPath(Path.Combine(AppPaths.Writable, "library", "video", "renders", "Render1.mp4"));
            Assert.StartsWith(data + Path.DirectorySeparatorChar, cache);
            Assert.StartsWith(data + Path.DirectorySeparatorChar, render);
        }
    }

    [PosixFact]
    public void Plugin_job_files_stay_under_the_data_root()
    {
        string repo = RepoRoot();
        string rave = File.ReadAllText(Path.Combine(repo, "baseplugins", "user", "stock", "rave.lua"));
        string speed = File.ReadAllText(Path.Combine(repo, "baseplugins", "user", "stock", "speed.lua"));
        string handler = File.ReadAllText(Path.Combine(repo, "src", "data", "PluginHandler.cs"));
        Assert.Contains("local temp2 = \"temp2.mp4\"", rave);
        Assert.Contains("local frames = \"./frames\"", rave);
        Assert.Contains("options.inputVideo", speed);
        Assert.Contains("options.outputVideo", speed);
        Assert.Contains("Path.GetFileName(video)", handler);
        Assert.Contains("videoOptions[\"outputVideo\"] = !runLua ? \"output.mp4\"", handler);
        Assert.Contains("new Command(CommandType.FFmpeg, ReplacePlaceholders(args), jobDirectory)", handler);

        string previous = Directory.GetCurrentDirectory();
        try
        {
            DataRoots.Apply();
            File.WriteAllText(Path.Combine(box.Data, "marker.txt"), "ok");
            Assert.Equal("ok", File.ReadAllText("marker.txt"));
            string root = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string bundle = Path.GetFullPath(AppPaths.AssemblyDirectory()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resources = Path.GetFullPath(AppPaths.Resources).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.NotEqual(bundle, root);
            Assert.NotEqual(resources, root);
            string job = Path.GetFullPath(Path.Combine(NvgPaths.Native(@".\temp"), "job_sample")) + Path.DirectorySeparatorChar;
            foreach (string name in new[] { "temp2.mp4", "frames", "output.mp4", "result.mp4" })
            {
                string full = Path.GetFullPath(Path.Combine(job, name));
                Assert.StartsWith(root, full);
                Assert.False(full.StartsWith(bundle, StringComparison.Ordinal));
                Assert.False(full.StartsWith(resources, StringComparison.Ordinal));
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "baseplugins", "user", "stock", "rave.lua")))
                return dir;
            string? parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;
            dir = parent;
        }
        throw new InvalidOperationException("Could not find rave.lua above " + AppContext.BaseDirectory);
    }
}
