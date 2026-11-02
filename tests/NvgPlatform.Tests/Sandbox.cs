using System.Diagnostics;

namespace NvgPlatform.Tests;

[CollectionDefinition("nvg", DisableParallelization = true)]
public sealed class NvgCollection : ICollectionFixture<Sandbox>
{
}

/// <summary>
/// Points HOME, XDG_DATA_HOME, NVG_DATA_DIR, and the working directory at a temp folder
/// before any test can resolve a data root. The real user profile is never the target.
/// </summary>
public sealed class Sandbox : IDisposable
{
    readonly string? previousHome;
    readonly string? previousProfile;
    readonly string? previousXdg;
    readonly string? previousNvg;
    readonly string previousCwd;

    public Sandbox()
    {
        RealHome = Environment.GetEnvironmentVariable("HOME") ?? "";
        if (string.IsNullOrWhiteSpace(RealHome))
            throw new InvalidOperationException("HOME is empty. Refusing to run platform tests.");
        Root = Path.Combine(Path.GetTempPath(), "nvg-platform-tests-" + Guid.NewGuid().ToString("n"));
        string fullRoot = Path.GetFullPath(Root);
        string fullTemp = Path.GetFullPath(Path.GetTempPath());
        if (!fullRoot.StartsWith(fullTemp, StringComparison.Ordinal))
            throw new InvalidOperationException("Test root is outside the temp directory: " + fullRoot);
        if (fullRoot.StartsWith(RealMacData, StringComparison.Ordinal) || fullRoot.StartsWith(RealLinuxData, StringComparison.Ordinal))
            throw new InvalidOperationException("Test root is inside the real NVG data directory.");
        Home = Path.Combine(Root, "home");
        Xdg = Path.Combine(Home, "xdg-data");
        Data = Path.Combine(Root, "data dir", "映像");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Xdg);
        previousCwd = Directory.GetCurrentDirectory();
        previousHome = Environment.GetEnvironmentVariable("HOME");
        previousProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        previousXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        previousNvg = Environment.GetEnvironmentVariable("NVG_DATA_DIR");
        Environment.SetEnvironmentVariable("HOME", Home);
        Environment.SetEnvironmentVariable("USERPROFILE", Home);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Xdg);
        Environment.SetEnvironmentVariable("NVG_DATA_DIR", Data);
        Directory.SetCurrentDirectory(Root);
    }

    public string RealHome { get; }
    public string Root { get; }
    public string Home { get; }
    public string Xdg { get; }
    public string Data { get; }
    public string RealMacData => Path.Combine(RealHome, "Library", "Application Support", "NonsensicalVideoGenerator");
    public string RealLinuxData => Path.Combine(RealHome, ".local", "share", "NonsensicalVideoGenerator");

    public void Dispose()
    {
        Directory.SetCurrentDirectory(previousCwd);
        Environment.SetEnvironmentVariable("HOME", previousHome);
        Environment.SetEnvironmentVariable("USERPROFILE", previousProfile);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousXdg);
        Environment.SetEnvironmentVariable("NVG_DATA_DIR", previousNvg);
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch
        {
        }
    }
}

public static class TestWait
{
    public static void Until(Func<bool> predicate)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(3))
                throw new TimeoutException("Timed out waiting for a platform test condition.");
            Thread.Sleep(10);
        }
    }
}
