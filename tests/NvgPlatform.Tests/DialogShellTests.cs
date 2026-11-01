using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class DialogShellTests
{
    readonly Sandbox box;

    public DialogShellTests(Sandbox box)
    {
        this.box = box;
    }

    [Fact]
    public void Linux_zenity_argv_keeps_spaces_and_quotes_in_one_element()
    {
        string dir = Path.Combine(box.Root, "dir with spaces", "フォルダ");
        Directory.CreateDirectory(dir);
        NativeCommand? command = PlatformFileDialog.Plan(
            "linux",
            "Title \"quoted\"",
            "Clips|*.mp4;*.mkv",
            true,
            dir,
            name => name == "zenity");
        Assert.NotNull(command);
        Assert.Equal("zenity", command.Value.FileName);
        Assert.Equal(new[]
        {
            "--file-selection",
            "--title=Title \"quoted\"",
            "--multiple",
            "--separator=\n",
            "--filename=" + dir + Path.DirectorySeparatorChar,
            "--file-filter=Clips | *.mp4 *.mkv",
        }, command.Value.Arguments);
    }

    [Fact]
    public void Linux_falls_through_to_kdialog_and_reports_a_missing_helper()
    {
        string dir = Path.Combine(box.Root, "dir with spaces", "フォルダ");
        Directory.CreateDirectory(dir);
        NativeCommand? both = PlatformFileDialog.Plan("linux", "Title", "*.mp4", true, dir, name => name is "zenity" or "kdialog");
        NativeCommand? kdialog = PlatformFileDialog.Plan("linux", "Title \"quoted\"", "Clips|*.mp4;*.mkv", true, dir, name => name == "kdialog");
        NativeCommand? none = PlatformFileDialog.Plan("linux", "Title", "*.mp4", false, dir, _ => false);
        Assert.Equal("zenity", both!.Value.FileName);
        Assert.NotNull(kdialog);
        Assert.Equal("kdialog", kdialog.Value.FileName);
        Assert.Equal(new[]
        {
            "--title",
            "Title \"quoted\"",
            "--multiple",
            "--separate-output",
            "--getopenfilename",
            dir,
            "*.mp4 *.mkv|Clips",
        }, kdialog.Value.Arguments);
        Assert.Null(none);
    }

    [Fact]
    public void Mac_osascript_script_is_one_argument()
    {
        string dir = Path.Combine(box.Root, "dir with spaces", "フォルダ");
        Directory.CreateDirectory(dir);
        NativeCommand? many = PlatformFileDialog.Plan("macos", "Title \"quoted\"", "Clips|*.mp4;*.mkv", true, dir, _ => true);
        NativeCommand? one = PlatformFileDialog.Plan("macos", "One", "*.wav", false, null, _ => true);
        Assert.NotNull(many);
        Assert.Equal("osascript", many.Value.FileName);
        Assert.Equal(2, many.Value.Arguments.Length);
        Assert.Equal("-e", many.Value.Arguments[0]);
        string script = many.Value.Arguments[1];
        Assert.Contains("with multiple selections allowed", script);
        Assert.Contains("\"mp4\"", script);
        Assert.Contains("\"mkv\"", script);
        Assert.Contains(dir, script);
        Assert.Contains("\\\"", script);
        Assert.NotNull(one);
        Assert.DoesNotContain("multiple selections", one.Value.Arguments[1]);
        Assert.Contains("\"wav\"", one.Value.Arguments[1]);
    }

    [Fact]
    public void Missing_dialog_helper_returns_empty_without_waiting()
    {
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.Combine(box.Root, "empty-path"));
            Directory.CreateDirectory(Path.Combine(box.Root, "empty-path"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string[] picked = PlatformFileDialog.OpenFiles("Pick a clip", "Clips|*.mp4", true, box.Root);
            watch.Stop();
            Assert.Empty(picked);
            Assert.NotNull(PlatformFileDialog.LastNotice);
            Assert.Contains("unavailable", PlatformFileDialog.LastNotice);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    [Fact]
    public void Shell_argv_is_one_element_per_path()
    {
        string dir = Path.Combine(box.Root, "dir with spaces", "フォルダ");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "clip name.mp4");
        NativeCommand linuxOpen = PlatformShell.PlanOpen("linux", file);
        NativeCommand macReveal = PlatformShell.PlanReveal("macos", file);
        NativeCommand linuxReveal = PlatformShell.PlanReveal("linux", file);
        Assert.Equal("xdg-open", linuxOpen.FileName);
        Assert.Equal(new[] { Path.GetFullPath(file) }, linuxOpen.Arguments);
        Assert.Equal("open", macReveal.FileName);
        Assert.Equal(new[] { "-R", Path.GetFullPath(file) }, macReveal.Arguments);
        Assert.Equal("xdg-open", linuxReveal.FileName);
        Assert.Equal(new[] { Path.GetFullPath(dir) }, linuxReveal.Arguments);
    }

    [Fact]
    public void Missing_shell_helper_returns_without_waiting()
    {
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.Combine(box.Root, "empty-path"));
            Directory.CreateDirectory(Path.Combine(box.Root, "empty-path"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            PlatformShell.Open(Path.Combine(box.Root, "dir with spaces"));
            watch.Stop();
            Assert.NotNull(PlatformShell.LastNotice);
            Assert.Contains("not on PATH", PlatformShell.LastNotice);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }
}
