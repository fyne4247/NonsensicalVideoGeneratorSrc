using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class AppRestartTests
{
    readonly Sandbox box;

    public AppRestartTests(Sandbox box)
    {
        this.box = box;
    }

    [Fact]
    public void Windows_keeps_the_steam_url()
    {
        string macOs = Path.Combine(box.Root, "NonsensicalVideoGenerator.app", "Contents", "MacOS");
        Directory.CreateDirectory(macOs);
        AppRestartRequest request = AppRestart.Plan(
            "windows",
            "2516360",
            Path.Combine(box.Root, "data dir"),
            macOs,
            "/ignored/NonsensicalVideoGenerator");
        Assert.Equal("steam://run/2516360", request.FileName);
        Assert.Empty(request.Arguments);
        Assert.True(request.UseShellExecute);
        Assert.Equal("open", request.Verb);
        Assert.False(request.InheritEnvironment);
        Assert.Equal("steam://run/2516360", AppRestart.FormatCommand(request));
    }

    [Fact]
    public void Mac_app_passes_data_dir_as_one_env_argument()
    {
        string macOs = Path.Combine(box.Root, "My App.app", "Contents", "MacOS");
        Directory.CreateDirectory(macOs);
        string data = Path.Combine(box.Root, "data dir", "映像");
        AppRestartRequest request = AppRestart.Plan("macos", "2516360", data, macOs + Path.DirectorySeparatorChar, "/usr/bin/false");
        string bundle = Path.GetFullPath(Path.Combine(box.Root, "My App.app"));
        Assert.Equal("open", request.FileName);
        Assert.False(request.UseShellExecute);
        Assert.Equal("", request.Verb);
        Assert.False(request.InheritEnvironment);
        Assert.Equal(new[] { "-n", "--env", "NVG_DATA_DIR=" + data, bundle }, request.Arguments);
    }

    [Fact]
    public void Mac_app_omits_env_when_data_dir_is_unset()
    {
        string macOs = Path.Combine(box.Root, "NonsensicalVideoGenerator.app", "Contents", "MacOS");
        Directory.CreateDirectory(macOs);
        string bundle = Path.GetFullPath(Path.Combine(box.Root, "NonsensicalVideoGenerator.app"));
        AppRestartRequest blank = AppRestart.Plan("macos", "2516360", "   ", macOs, "/usr/bin/false");
        AppRestartRequest missing = AppRestart.Plan("macos", "2516360", null, macOs, "/usr/bin/false");
        Assert.Equal(new[] { "-n", bundle }, blank.Arguments);
        Assert.Equal(new[] { "-n", bundle }, missing.Arguments);
    }

    [Fact]
    public void Mac_without_a_bundle_reexecs_the_same_binary()
    {
        string exe = Path.Combine(box.Root, "bin", "NonsensicalVideoGenerator");
        AppRestartRequest request = AppRestart.Plan("macos", "2516360", Path.Combine(box.Root, "data dir"), box.Root, exe);
        Assert.Equal(exe, request.FileName);
        Assert.Empty(request.Arguments);
        Assert.False(request.UseShellExecute);
        Assert.True(request.InheritEnvironment);
    }

    [Fact]
    public void Linux_reexecs_even_when_the_path_contains_an_app_bundle()
    {
        string macOs = Path.Combine(box.Root, "NonsensicalVideoGenerator.app", "Contents", "MacOS");
        Directory.CreateDirectory(macOs);
        string exe = "/opt/NVG App/NonsensicalVideoGenerator";
        AppRestartRequest request = AppRestart.Plan("linux", "2516360", "/tmp/nvg data", macOs, exe);
        Assert.Equal(exe, request.FileName);
        Assert.Empty(request.Arguments);
        Assert.False(request.UseShellExecute);
        Assert.Equal("", request.Verb);
        Assert.True(request.InheritEnvironment);
    }
}
