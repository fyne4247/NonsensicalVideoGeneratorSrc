using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class PlatformUrlTests
{
    public PlatformUrlTests()
    {
        PlatformUrl.ResetForTests();
    }

    const string Workshop = "steam://openurl/https://steamcommunity.com/sharedfiles/filedetails/?id=42";
    const string WorkshopHttps = "https://steamcommunity.com/sharedfiles/filedetails/?id=42";
    const string News = "steam://openurl/https://store.steampowered.com/news/app/2516360/view/%postid%?l=%locale%";
    const string NewsHttps = "https://store.steampowered.com/news/app/2516360/view/%postid%?l=%locale%";

    [Fact]
    public void Desktop_without_a_handler_plans_the_https_page()
    {
        UrlLaunch mac = PlatformUrl.Plan("macos", Workshop, false);
        UrlLaunch linux = PlatformUrl.Plan("linux", Workshop, false);
        Assert.Equal("open", mac.FileName);
        Assert.Equal(new[] { WorkshopHttps }, mac.Arguments);
        Assert.False(mac.UseShellExecute);
        Assert.Null(mac.FallbackUrl);
        Assert.Equal("xdg-open", linux.FileName);
        Assert.Equal(new[] { WorkshopHttps }, linux.Arguments);
        Assert.Equal(NewsHttps, PlatformUrl.Address("macos", News, false));
        Assert.Contains("%postid%", PlatformUrl.Address("linux", News, false));
        Assert.Contains("%locale%", PlatformUrl.Address("linux", News, false));
        Assert.DoesNotContain("steam://", PlatformUrl.Address("macos", News, false));
    }

    [Fact]
    public void Desktop_with_a_handler_plans_steam_and_keeps_https_as_fallback()
    {
        UrlLaunch mac = PlatformUrl.Plan("macos", Workshop, true);
        Assert.Equal("open", mac.FileName);
        Assert.Equal(new[] { Workshop }, mac.Arguments);
        Assert.Equal(WorkshopHttps, mac.FallbackUrl);
        Assert.Equal(Workshop, PlatformUrl.Address("linux", Workshop, true));
    }

    [Fact]
    public void Windows_keeps_steam_first_whether_or_not_a_handler_was_reported()
    {
        UrlLaunch absent = PlatformUrl.Plan("windows", Workshop, false);
        UrlLaunch present = PlatformUrl.Plan("windows", Workshop, true);
        Assert.Equal(Workshop, absent.FileName);
        Assert.Equal(Workshop, present.FileName);
        Assert.Empty(absent.Arguments);
        Assert.True(absent.UseShellExecute);
        Assert.Equal(WorkshopHttps, absent.FallbackUrl);
        Assert.Equal(News, PlatformUrl.Address("windows", News, false));
        Assert.Contains("%postid%", PlatformUrl.Address("windows", News, true));
    }

    [Fact]
    public void Open_uses_the_fake_launcher_and_does_not_start_steam()
    {
        var launched = new List<UrlLaunch>();
        PlatformUrl.LaunchOverride = launch =>
        {
            launched.Add(launch);
            return true;
        };
        PlatformUrl.Open("macos", Workshop, false);
        PlatformUrl.Open("linux", Workshop, true);
        PlatformUrl.Open("windows", News, false);
        Assert.Equal(3, launched.Count);
        Assert.Equal(WorkshopHttps, launched[0].Arguments[0]);
        Assert.Equal(Workshop, launched[1].Arguments[0]);
        Assert.Equal(News, launched[2].FileName);
        Assert.All(launched, launch =>
        {
            string name = Path.GetFileName(launch.FileName.Replace('\\', '/'));
            Assert.False(
                name.Equals("Steam", StringComparison.OrdinalIgnoreCase)
                || name.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Steam.app", StringComparison.OrdinalIgnoreCase),
                "launcher started the Steam application: " + launch.FileName);
            Assert.DoesNotContain("steam://run/", PlatformUrl.Format(launch));
        });
    }

    [Fact]
    public void Windows_falls_back_to_https_only_when_the_steam_launch_fails()
    {
        var launched = new List<string>();
        PlatformUrl.LaunchOverride = launch =>
        {
            launched.Add(PlatformUrl.Format(launch));
            return !launch.FileName.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);
        };
        PlatformUrl.Open("windows", Workshop, true);
        Assert.Equal(new[] { Workshop, WorkshopHttps }, launched);
    }

    [Fact]
    public void Desktop_does_not_try_steam_when_no_handler_exists()
    {
        int calls = 0;
        PlatformUrl.LaunchOverride = launch =>
        {
            calls++;
            Assert.DoesNotContain("steam://", PlatformUrl.Format(launch));
            return true;
        };
        PlatformUrl.Open("macos", Workshop, false);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Scheme_checks_read_text_only()
    {
        Assert.True(PlatformUrl.XmlDeclaresSteamScheme("<key>CFBundleURLSchemes</key><array><string>steam</string></array>"));
        Assert.False(PlatformUrl.XmlDeclaresSteamScheme("<string>com.valvesoftware.steam</string>"));
        Assert.False(PlatformUrl.XmlDeclaresSteamScheme(""));
        Assert.True(PlatformUrl.XdgReportsHandler("steam.desktop\n"));
        Assert.False(PlatformUrl.XdgReportsHandler(""));
        Assert.False(PlatformUrl.XdgReportsHandler("not a handler"));
        Assert.Equal(WorkshopHttps, PlatformUrl.HttpsTarget(Workshop));
        Assert.Null(PlatformUrl.HttpsTarget("steam://run/2516360"));
    }

    [Fact]
    public void Blog_url_follows_the_plan_and_keeps_placeholders()
    {
        string saved = BlogData.SteamUrl;
        try
        {
            BlogData.SteamUrl = News;
            PlatformUrl.HandlerOverride = () => false;
            string url = BlogData.Url;
            Assert.Contains("%postid%", url);
            Assert.Contains("%locale%", url);
            if (DataRoots.PlatformKind() == "windows")
                Assert.StartsWith("steam://", url);
            else
                Assert.StartsWith("https://", url);
            PlatformUrl.HandlerOverride = () => true;
            if (DataRoots.PlatformKind() == "windows")
                Assert.StartsWith("steam://", BlogData.Url);
            else
                Assert.StartsWith("steam://", BlogData.Url);
        }
        finally
        {
            BlogData.SteamUrl = saved;
            PlatformUrl.ResetForTests();
        }
    }
}
