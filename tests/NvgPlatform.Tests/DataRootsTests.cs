using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class DataRootsTests
{
    readonly Sandbox box;

    public DataRootsTests(Sandbox box)
    {
        this.box = box;
    }

    [Fact]
    public void Override_is_the_temp_data_dir()
    {
        string resolved = DataRoots.ResolveDataRoot();
        Assert.Equal(Path.GetFullPath(box.Data), resolved);
        Assert.NotEqual(box.RealMacData, resolved);
        Assert.NotEqual(box.RealLinuxData, resolved);
        Assert.StartsWith(Path.GetFullPath(box.Root), resolved);
    }

    [Fact]
    public void Mac_and_linux_defaults_do_not_create_directories()
    {
        bool macExisted = Directory.Exists(box.RealMacData);
        bool linuxExisted = Directory.Exists(box.RealLinuxData);
        string mac = DataRoots.ResolveDataRoot(null, box.RealHome, box.Xdg, "macos");
        string linuxXdg = DataRoots.ResolveDataRoot(null, box.RealHome, box.Xdg, "linux");
        string linuxHome = DataRoots.ResolveDataRoot(null, box.Home, "  ", "linux");
        string tilde = DataRoots.ResolveDataRoot("~/nvg-override", box.Home, null, "linux");
        Assert.Equal(box.RealMacData, mac);
        Assert.Equal(Path.Combine(box.Xdg, "NonsensicalVideoGenerator"), linuxXdg);
        Assert.Equal(Path.Combine(box.Home, ".local", "share", "NonsensicalVideoGenerator"), linuxHome);
        Assert.Equal(Path.GetFullPath(Path.Combine(box.Home, "nvg-override")), tilde);
        Assert.Equal(macExisted, Directory.Exists(box.RealMacData));
        Assert.Equal(linuxExisted, Directory.Exists(box.RealLinuxData));
        Assert.False(Directory.Exists(Path.Combine(box.Home, ".local", "share", "NonsensicalVideoGenerator")));
    }

    [Fact]
    public void Apply_uses_the_override_and_leaves_the_real_profile_alone()
    {
        DateTime? macWrite = Directory.Exists(box.RealMacData) ? Directory.GetLastWriteTimeUtc(box.RealMacData) : null;
        DateTime? linuxWrite = Directory.Exists(box.RealLinuxData) ? Directory.GetLastWriteTimeUtc(box.RealLinuxData) : null;
        Directory.CreateDirectory(box.Data);
        File.WriteAllText(Path.Combine(box.Data, "marker.txt"), "ok");
        DataRoots.Apply();
        Assert.Equal("ok", File.ReadAllText("marker.txt"));
        Assert.True(Directory.Exists(Path.Combine(box.Data, "plugins")));
        Assert.True(Directory.Exists(Path.Combine(box.Data, "templates")));
        Assert.True(Directory.Exists(Path.Combine(box.Data, "locales")));
        Assert.Equal(macWrite, Directory.Exists(box.RealMacData) ? Directory.GetLastWriteTimeUtc(box.RealMacData) : null);
        Assert.Equal(linuxWrite, Directory.Exists(box.RealLinuxData) ? Directory.GetLastWriteTimeUtc(box.RealLinuxData) : null);
    }

    [Fact]
    public void Seed_adds_missing_stock_files_and_does_not_overwrite()
    {
        string resource = Path.Combine(box.Root, "resource app");
        string data = Path.Combine(box.Root, "user data", "フォルダ");
        Put(Path.Combine(resource, "plugins", "user", "stock", "speed.lua"), "original");
        Put(Path.Combine(resource, "plugins", "user", "stock", "名前.lua"), "unicode-name");
        Put(Path.Combine(resource, "templates", "my layer", "effect.lua"), "template");
        Put(Path.Combine(resource, "locales", "english.json"), "{}");
        Put(Path.Combine(resource, "library", "video", "outros", "defaultoutro.mp4"), "video");
        Put(Path.Combine(resource, "bin", "images", "affine.png"), "png");
        Put(Path.Combine(resource, "credits.json"), "{\"credits\":true}");
        Put(Path.Combine(resource, "FFmpeg-LICENSE.txt"), "gpl");
        Put(Path.Combine(resource, "NonsensicalVideoGenerator.dll"), "not-stock");
        int first = DataRoots.Seed(resource, data);
        Assert.Equal(8, first);
        Assert.Equal("original", File.ReadAllText(Path.Combine(data, "plugins", "user", "stock", "speed.lua")));
        Assert.Equal("unicode-name", File.ReadAllText(Path.Combine(data, "plugins", "user", "stock", "名前.lua")));
        Assert.Equal("template", File.ReadAllText(Path.Combine(data, "templates", "my layer", "effect.lua")));
        Assert.False(File.Exists(Path.Combine(data, "NonsensicalVideoGenerator.dll")));
        File.WriteAllText(Path.Combine(data, "plugins", "user", "stock", "speed.lua"), "changed by user");
        Put(Path.Combine(resource, "plugins", "user", "stock", "new.lua"), "added later");
        int second = DataRoots.Seed(resource, data);
        Assert.Equal(1, second);
        Assert.Equal("changed by user", File.ReadAllText(Path.Combine(data, "plugins", "user", "stock", "speed.lua")));
        Assert.Equal("added later", File.ReadAllText(Path.Combine(data, "plugins", "user", "stock", "new.lua")));
    }

    static void Put(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
