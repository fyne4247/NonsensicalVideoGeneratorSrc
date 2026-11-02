using System.Diagnostics;
using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class ChooserTests
{
    readonly Sandbox box;

    public ChooserTests(Sandbox box)
    {
        this.box = box;
    }

    [Fact]
    public void Start_folder_is_last_used_else_movies_and_never_shared()
    {
        string movies = Path.Combine(box.Root, "Movies");
        string used = Path.Combine(box.Root, "last used");
        string shared = Path.Combine(box.Root, "Users", "Shared");
        Directory.CreateDirectory(movies);
        Directory.CreateDirectory(used);
        Directory.CreateDirectory(shared);
        string clip = Path.Combine(used, "clip.mp4");
        File.WriteAllText(clip, "x");
        File.WriteAllText(Path.Combine(shared, "fear.mp4"), "x");
        try
        {
            PlatformFileDialog.MoviesFolder = () => movies;
            Assert.Equal(Path.GetFullPath(movies), PlatformFileDialog.ResolveStart(null));
            Assert.Equal(Path.GetFullPath(used), PlatformFileDialog.ResolveStart(used));
            PlatformFileDialog.RememberPicked(clip);
            Assert.Equal(Path.GetFullPath(used), PlatformFileDialog.ResolveStart(null));
            Assert.Equal(Path.GetFullPath(used), PlatformFileDialog.ResolveStart(shared));
            PlatformFileDialog.RememberPicked(Path.Combine(shared, "fear.mp4"));
            Assert.Equal(Path.GetFullPath(used), PlatformFileDialog.ResolveStart(null));
            NativeCommand? command = PlatformFileDialog.Plan("macos", "Pick", "*.mp4", false, null, _ => true);
            Assert.NotNull(command);
            string script = command.Value.Arguments[1];
            Assert.Contains(Path.GetFullPath(used), script);
            Assert.DoesNotContain(Path.GetFullPath(shared), script);

            PlatformFileDialog.ResetChooserForTests();
            PlatformFileDialog.MoviesFolder = () => movies;
            Assert.Equal(Path.GetFullPath(movies), PlatformFileDialog.ResolveStart(shared));
            Assert.Equal(Path.GetFullPath(movies), PlatformFileDialog.ResolveStart(null));
            NativeCommand? sharedStart = PlatformFileDialog.Plan("macos", "Pick", "*.mp4", false, shared, _ => true);
            Assert.NotNull(sharedStart);
            Assert.Contains(Path.GetFullPath(movies), sharedStart.Value.Arguments[1]);
            Assert.DoesNotContain(Path.GetFullPath(shared), sharedStart.Value.Arguments[1]);

            PlatformFileDialog.ResetChooserForTests();
            PlatformFileDialog.MoviesFolder = () => movies;
            NativeCommand? fresh = PlatformFileDialog.Plan("macos", "Pick", "*.mp4", false, null, _ => true);
            Assert.NotNull(fresh);
            Assert.Contains(Path.GetFullPath(movies), fresh.Value.Arguments[1]);
            Assert.DoesNotContain("Shared", fresh.Value.Arguments[1], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            PlatformFileDialog.ResetChooserForTests();
        }
    }

    [Fact]
    public void Only_one_chooser_runs_and_cancel_kills_it()
    {
        var first = new ScriptedChooser();
        var second = new ScriptedChooser();
        int which = 0;
        try
        {
            PlatformFileDialog.LaunchOverride = _ => which++ == 0 ? first : second;
            Assert.True(PlatformFileDialog.TryBegin("macos", "First", "*.mp4", true, box.Root, out PlatformFileDialog.FileChooser chooser));
            Assert.False(chooser.Finished);
            Assert.False(PlatformFileDialog.TryBegin("macos", "Second", "*.mp4", true, box.Root, out _));
            Assert.NotNull(PlatformFileDialog.LastNotice);
            Assert.Contains("already open", PlatformFileDialog.LastNotice, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, second.Kills);
            PlatformFileDialog.CancelActive();
            TestWait.Until(() => chooser.Finished);
            Assert.True(first.Kills > 0);
            Assert.Empty(chooser.Files);
            Assert.Equal(1, which);
        }
        finally
        {
            first.Release(0, "");
            PlatformFileDialog.ResetChooserForTests();
        }
    }

    [Fact]
    public void Missing_helper_finishes_immediately_and_windows_plan_is_null()
    {
        try
        {
            Assert.Null(PlatformFileDialog.Plan("windows", "Title", "*.mp4", true, box.Root, _ => true));
            var watch = Stopwatch.StartNew();
            Assert.True(PlatformFileDialog.TryBegin("windows", "Title", "*.mp4", true, box.Root, out PlatformFileDialog.FileChooser chooser));
            watch.Stop();
            Assert.True(chooser.Finished);
            Assert.Empty(chooser.Files);
            Assert.NotNull(PlatformFileDialog.LastNotice);
            Assert.Contains("unavailable", PlatformFileDialog.LastNotice, StringComparison.OrdinalIgnoreCase);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally
        {
            PlatformFileDialog.ResetChooserForTests();
        }
    }

    [Fact]
    public void Linux_zenity_then_kdialog_use_the_same_runner()
    {
        string bin = Path.Combine(box.Root, "dialog-bin");
        Directory.CreateDirectory(bin);
        string marker = Path.Combine(box.Root, "helper-name.txt");
        string zenityPick = Path.Combine(box.Root, "from zenity.mp4");
        string kdialogPick = Path.Combine(box.Root, "from kdialog.mp4");
        File.WriteAllText(zenityPick, "zenity");
        File.WriteAllText(kdialogPick, "kdialog");
        WriteHelper(Path.Combine(bin, "zenity"), marker, zenityPick);
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", bin);
            Assert.True(PlatformFileDialog.TryBegin("linux", "Title", "Clips|*.mp4", false, box.Root, out PlatformFileDialog.FileChooser zenity));
            Assert.True(zenity.WaitFinished(5000));
            Assert.Equal(zenityPick, Assert.Single(zenity.Files));
            Assert.Contains("zenity", File.ReadAllText(marker), StringComparison.Ordinal);

            File.Delete(Path.Combine(bin, "zenity"));
            WriteHelper(Path.Combine(bin, "kdialog"), marker, kdialogPick);
            Assert.True(PlatformFileDialog.TryBegin("linux", "Title", "Clips|*.mp4", false, box.Root, out PlatformFileDialog.FileChooser kdialog));
            Assert.True(kdialog.WaitFinished(5000));
            Assert.Equal(kdialogPick, Assert.Single(kdialog.Files));
            Assert.Contains("kdialog", File.ReadAllText(marker), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            PlatformFileDialog.ResetChooserForTests();
        }
    }

    [Fact]
    public void Cancel_leaves_no_chooser_process()
    {
        string bin = Path.Combine(box.Root, "sleep-bin");
        Directory.CreateDirectory(bin);
        string script = Path.Combine(bin, "zenity");
        File.WriteAllText(script, "#!/bin/sh\n/bin/sleep 30\n");
        Chmod(script);
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", bin);
            Assert.True(PlatformFileDialog.TryBegin("linux", "Title", "*.mp4", false, box.Root, out PlatformFileDialog.FileChooser chooser));
            TestWait.Until(() => Running(script) || chooser.Finished);
            Assert.False(chooser.Finished);
            Assert.True(Running(script));
            PlatformFileDialog.CancelActive();
            TestWait.Until(() => chooser.Finished && !Running(script));
            Assert.Empty(chooser.Files);
        }
        finally
        {
            PlatformFileDialog.CancelActive();
            Environment.SetEnvironmentVariable("PATH", oldPath);
            PlatformFileDialog.ResetChooserForTests();
        }
    }

    static void WriteHelper(string path, string marker, string picked)
    {
        string body = "#!/bin/sh\nprintf '%s\\n' \"$0\" > " + Quote(marker) + "\nprintf '%s\\n' " + Quote(picked) + "\n";
        File.WriteAllText(path, body);
        Chmod(path);
    }

    static void Chmod(string path)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "/bin/chmod",
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("+x");
        psi.ArgumentList.Add(path);
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("chmod");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("chmod failed for " + path);
    }

    static string Quote(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    static bool Running(string script)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/pgrep",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-fl");
        psi.ArgumentList.Add(script);
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("pgrep");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            return false;
        foreach (string line in output.Split('\n'))
        {
            if (line.Contains("pgrep", StringComparison.Ordinal))
                continue;
            if (line.Contains(script, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    sealed class ScriptedChooser : PlatformFileDialog.IChooserProcess
    {
        readonly object gate = new();
        bool released;
        string output = "";
        int exitCode = -1;

        public int Kills { get; private set; }
        public string Output
        {
            get { lock (gate) return output; }
        }
        public int ExitCode
        {
            get { lock (gate) return exitCode; }
        }

        public void Release(int code, string text)
        {
            lock (gate)
            {
                exitCode = code;
                output = text;
                released = true;
            }
        }

        public bool Wait(int milliseconds)
        {
            lock (gate)
            {
                if (released)
                    return true;
            }
            Thread.Sleep(Math.Min(Math.Max(milliseconds, 1), 50));
            lock (gate)
                return released;
        }

        public void Kill()
        {
            Kills++;
            Release(-1, "");
        }

        public void Dispose()
        {
        }
    }
}
