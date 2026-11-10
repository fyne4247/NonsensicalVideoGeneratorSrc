using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace NvgMedia.Tests;

/// <summary>
/// DesktopGL menu music is a derived asset. The original ogg and wma files
/// stay byte-identical. MonoGame re-encodes the derived ogg with libvorbis,
/// then OggStream casts float samples to 16-bit before OpenAL gain. That
/// float peak has to stay at or under -1 dBFS. The LAME pin is the package
/// build's libmp3lame source.
/// </summary>
[Collection("nvg-media")]
public class MenuMusicHeadroomTests
{
    readonly NvgTempEnv env;

    public MenuMusicHeadroomTests(NvgTempEnv env)
    {
        this.env = env;
    }

    [Fact]
    public void LamePinAndPackageScriptsRequireLibmp3lame()
    {
        AssertTempHome();
        string root = RepoRoot();
        string lame = File.ReadAllText(Path.Combine(root, "tools", "lame", "build-lame.sh"));
        string ffmpeg = File.ReadAllText(Path.Combine(root, "tools", "ffmpeg7", "build-ffmpeg7.sh"));
        string common = File.ReadAllText(Path.Combine(root, "tools", "package", "lib", "package-common.sh"));
        string smoke = File.ReadAllText(Path.Combine(root, "tools", "package", "lib", "smoke_ffmpeg.py"));
        string notice = File.ReadAllText(Path.Combine(root, "tools", "package", "THIRD_PARTY.md"));
        Assert.Contains("LAME_VERSION=\"3.100\"", lame);
        Assert.Contains("ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e", lame);
        Assert.Contains("LGPL-2.0-only", lame);
        Assert.Contains("-fPIC", lame);
        Assert.Contains("--enable-libmp3lame", ffmpeg);
        Assert.Contains("libmp3lame", ffmpeg);
        Assert.Contains("build-lame.sh", common);
        Assert.Contains("NVG_LAME_PREFIX", common);
        Assert.Contains("libmp3lame", smoke);
        Assert.Contains("silence.mp3", smoke);
        Assert.Contains("LGPL-2.0-only", notice);
        Assert.Contains("libmp3lame", notice);
    }

    [PosixFact]
    public void DesktopGlPlayedMusicDoesNotClipBeforeGain()
    {
        AssertTempHome();
        string root = RepoRoot();
        string[] originals =
        {
            "content/music/theme1.ogg",
            "content/music/theme2.ogg",
            "content/music/theme3.ogg",
            "content/music/theme4.ogg",
            "content/music/theme5.ogg",
            "content/themes/halloween/music/theme1.ogg",
        };
        AssertOriginalMusic(root);
        string desktop = File.ReadAllText(Path.Combine(root, "content", "Content.DesktopGL.mgcb"));
        string windows = File.ReadAllText(Path.Combine(root, "content", "Content.mgcb"));
        Assert.Contains("#begin desktopgl-music/music/theme1.ogg", desktop);
        Assert.Contains("/build:music/theme1.ogg", desktop);
        Assert.Contains("#begin music/theme1.ogg", windows);
        Assert.DoesNotContain("desktopgl-music", windows);
        string realHome = Run(root, "python3", "-c", "import pwd,os; print(pwd.getpwuid(os.getuid()).pw_dir)").Text.Trim();
        string nvorbis = Path.Combine(realHome, ".nuget", "packages", "dotnet-mgcb", "3.8.2.1105", "tools", "net8.0", "any", "NVorbis.dll");
        Assert.True(File.Exists(nvorbis), nvorbis);
        string dotnet = Path.Combine(realHome, ".dotnet-nvg", "dotnet");
        Assert.True(File.Exists(dotnet), dotnet);
        string outDir = Path.Combine(Path.GetTempPath(), "nvg-hard15-music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var prepare = Run(root, "python3", Path.Combine(root, "tools", "music", "prepare-desktopgl-music.py"), "--out", outDir);
            Assert.True(prepare.ExitCode == 0, prepare.Text);
            string provenance = File.ReadAllText(Path.Combine(outDir, "PROVENANCE.txt"));
            Assert.Contains("libvorbis", provenance);
            Assert.Contains("-b:a 192000", provenance);
            var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string block in provenance.Split("\n---\n", StringSplitOptions.None).Skip(1))
            {
                blocks[Value(block, "source")] = block;
            }
            Assert.Equal(originals.Length, blocks.Count);
            foreach (string rel in originals)
            {
                string key = rel.Substring("content/".Length);
                Assert.True(blocks.ContainsKey(key), key);
                string block = blocks[key];
                string derived = Path.Combine(outDir, key.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(derived), derived);
                Assert.Equal(Sha256(Path.Combine(root, rel)), Value(block, "source_sha256"));
                Assert.Equal(Sha256(derived), Value(block, "output_sha256"));
                Assert.False(string.IsNullOrWhiteSpace(Value(block, "gain_db")));
                string command = Value(block, "command");
                Assert.False(string.IsNullOrWhiteSpace(command));
                if (Value(block, "method") == "volume")
                {
                    Assert.Contains("volume=", command);
                    Assert.Contains("libvorbis", command);
                }
                string played = Path.Combine(outDir, Value(block, "played").Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(played), played);
                Assert.Equal(Sha256(played), Value(block, "played_sha256"));
            }
            string contentBuild = File.ReadAllText(Path.Combine(root, "tools", "build-content-desktopgl.sh"));
            string linuxPkg = File.ReadAllText(Path.Combine(root, "tools", "package", "build-linux-package.sh"));
            string macPkg = File.ReadAllText(Path.Combine(root, "tools", "package", "build-macos-installer.sh"));
            Assert.Contains("desktopgl-music-PROVENANCE.txt", contentBuild);
            Assert.Contains("desktopgl-music-PROVENANCE.txt", linuxPkg);
            Assert.Contains("desktopgl-music-PROVENANCE.txt", macPkg);
            AssertOriginalMusic(root);
            string measureDir = Path.Combine(outDir, "measure");
            var build = Run(
                root,
                dotnet,
                "build",
                Path.Combine(root, "tools", "music", "MeasureVorbisPeak"),
                "-p:NVorbisDll=" + nvorbis,
                "-o",
                measureDir);
            Assert.True(build.ExitCode == 0, build.Text);
            string measureDll = Path.Combine(measureDir, "MeasureVorbisPeak.dll");
            int measured = 0;
            foreach (string played in Directory.GetFiles(Path.Combine(outDir, "played"), "*.ogg", SearchOption.AllDirectories))
            {
                var measure = Run(root, dotnet, "exec", measureDll, played);
                Assert.True(measure.ExitCode == 0, measure.Text);
                double peak = double.Parse(Value(measure.Text, "peak_db"), CultureInfo.InvariantCulture);
                int clamps = int.Parse(Value(measure.Text, "clamps"), CultureInfo.InvariantCulture);
                Assert.True(peak <= -1.0, played + " float peak " + peak.ToString("0.00", CultureInfo.InvariantCulture) + " dBFS");
                Assert.Equal(0, clamps);
                Assert.Contains("has_clipped=false", measure.Text);
                measured++;
            }
            Assert.Equal(originals.Length, measured);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    [Fact]
    public void OriginalMusicMatchesPinnedUpstreamBytes()
    {
        AssertTempHome();
        AssertOriginalMusic(RepoRoot());
    }

    static void AssertOriginalMusic(string root)
    {
        // Upstream baseline 58ecdd1. Works in git archives and exported source trees too.
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["basetemplates/defaultlayer/music/theme1.wma"] = "2844f2d87bab12d834154ca8ba5bdd0d716c508dfce766fdbba61684fbad0dac",
            ["basetemplates/defaultlayer/music/theme2.wma"] = "63ed3e479e238f653ebbbecdb47674ee68afa5a46c24a084e4b078abe7720477",
            ["basetemplates/defaultlayer/music/theme3.wma"] = "485938a73735c6e47e64a36b4f7dfd8b082be56b268577aedc23198b5292dd87",
            ["basetemplates/defaultlayer/music/theme4.wma"] = "d6e7b54d9389de77c398c201cf67f4edeb7bd15701524edba1bc4b5b5e29eed2",
            ["basetemplates/defaultlayer/music/theme5.wma"] = "f49d99ad54cd57faa3e3b4451ada13a2b761e70ee84ebc18102522a706eff60d",
            ["basetemplates/defaultlayer/music/theme6.wma"] = "d44b675820423588a41e27b43a89d096c2b7f886a48dcf70ca59d80c44cabfe2",
            ["content/music/theme1.ogg"] = "4aa861c8f669e06af450b515cd17158198a98c6a8d0de9621869ef48bfdb96ee",
            ["content/music/theme2.ogg"] = "fe683f012ea9eaca8853af5e70ffeb443069177d81167063bccf2b28b677d4a7",
            ["content/music/theme3.ogg"] = "234b8798424f5777744510ac73d91346e4fb912d9f4e13b792a8b478e015da2a",
            ["content/music/theme4.ogg"] = "fe2a95605ff0dfc332543905fa00adfda910046a24bd3dfc087bdf60f5ec5eaa",
            ["content/music/theme5.ogg"] = "6fc5e94fe962a5fe2d78cb658778af58f1bda41023db95a8b171996c28c88c74",
            ["content/themes/halloween/music/theme1.ogg"] = "6bd75cea9066cd81b9102739287cb7d2d84236f72aa838c2cb26e0b7ecb7ac29",
        };
        foreach (var (relative, expected) in hashes)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(expected, Sha256(path));
        }
    }

    static string Value(string text, string key)
    {
        string prefix = key + "=";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line.Substring(prefix.Length);
            }
        }
        throw new InvalidOperationException(key + " missing:\n" + text);
    }

    static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    readonly record struct CommandResult(int ExitCode, string Text);

    static CommandResult Run(string root, string file, params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.Environment["DOTNET_CLI_HOME"] = Path.GetTempPath();
        start.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(file) ?? "";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using Process process = Process.Start(start) ?? throw new InvalidOperationException(file + " did not start");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(600000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(file + " timed out");
        }
        return new CommandResult(process.ExitCode, stdout + stderr);
    }

    void AssertTempHome()
    {
        string home = Environment.GetEnvironmentVariable("HOME") ?? "";
        string data = Environment.GetEnvironmentVariable("NVG_DATA_DIR") ?? "";
        Assert.Equal(env.Home, home);
        Assert.Equal(env.Data, data);
        Assert.StartsWith(Path.GetTempPath(), home, StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string script = Path.Combine(dir.FullName, "tools", "lame", "build-lame.sh");
            if (File.Exists(script))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }

}
