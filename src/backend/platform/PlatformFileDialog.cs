using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// DesktopGL file picker. macOS uses osascript. Linux tries zenity, then kdialog.
    /// Cancel and a missing helper both return an empty list. A missing helper does not wait.
    /// The chooser process runs off the caller: <see cref="TryBegin"/> returns immediately
    /// and <see cref="OpenFiles"/> waits on that same process so Lua can stay synchronous.
    /// Only one chooser runs at a time. Cancel and process exit kill it.
    /// The start folder is the last directory picked this session, otherwise Movies.
    /// A path with a Shared segment is never used.
    /// </summary>
    public static class PlatformFileDialog
    {
        static readonly object ActiveGate = new();
        static FileChooser? active;
        static string? sessionDirectory;

        public static string? LastNotice { get; private set; }

        /// <summary>Test hook. Null uses the real process.</summary>
        public static Func<NativeCommand, IChooserProcess>? LaunchOverride { get; set; }

        /// <summary>Test hook for the Movies fallback. Null uses the platform Movies folder.</summary>
        public static Func<string?>? MoviesFolder { get; set; }

        static PlatformFileDialog()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CancelActive();
        }

        public interface IChooserProcess : IDisposable
        {
            bool Wait(int milliseconds);
            string Output { get; }
            int ExitCode { get; }
            void Kill();
        }

        public sealed class FileChooser
        {
            readonly ManualResetEventSlim done = new(false);
            readonly object gate = new();
            string[] files = Array.Empty<string>();
            bool cancel;
            IChooserProcess? running;

            public bool Finished => done.IsSet;

            public string[] Files
            {
                get
                {
                    lock (gate)
                        return files;
                }
            }

            internal bool CancelRequested
            {
                get
                {
                    lock (gate)
                        return cancel;
                }
            }

            internal void Attach(IChooserProcess process)
            {
                bool killNow;
                lock (gate)
                {
                    running = process;
                    killNow = cancel;
                }
                if (killNow)
                    process.Kill();
            }

            public void Cancel()
            {
                IChooserProcess? process;
                lock (gate)
                {
                    cancel = true;
                    process = running;
                }
                try
                {
                    process?.Kill();
                }
                catch
                {
                }
            }

            internal void Complete(string[] picked)
            {
                lock (gate)
                    files = picked ?? Array.Empty<string>();
                done.Set();
            }

            public bool WaitFinished(int milliseconds)
            {
                return done.Wait(milliseconds);
            }
        }

        public static string[] OpenFiles(string title, string filter, bool multiselect, string? initialDirectory)
        {
            try
            {
                if (!TryBegin(title, filter, multiselect, initialDirectory, out FileChooser chooser))
                    return Array.Empty<string>();
                while (!chooser.Finished)
                    chooser.WaitFinished(50);
                return chooser.Files;
            }
            catch (Exception ex)
            {
                Notice("File dialog failed: " + ex.Message);
                return Array.Empty<string>();
            }
        }

        public static bool TryBegin(string title, string filter, bool multiselect, string? initialDirectory, out FileChooser chooser)
        {
            return TryBegin(DataRoots.PlatformKind(), title, filter, multiselect, initialDirectory, out chooser);
        }

        public static bool TryBegin(string platform, string title, string filter, bool multiselect, string? initialDirectory, out FileChooser chooser)
        {
            lock (ActiveGate)
            {
                if (active != null && !active.Finished)
                {
                    chooser = active;
                    Notice("A file dialog is already open. Nothing was selected for '" + title + "'.");
                    return false;
                }
                chooser = new FileChooser();
                active = chooser;
            }
            NativeCommand? command;
            try
            {
                command = Plan(platform, title, filter, multiselect, initialDirectory, null);
            }
            catch (Exception ex)
            {
                Notice("File dialog failed: " + ex.Message);
                chooser.Complete(Array.Empty<string>());
                FinishActive(chooser);
                return true;
            }
            if (command == null)
            {
                Notice("File dialog is unavailable (osascript, zenity, and kdialog were not found). Nothing was selected for '" + title + "'.");
                chooser.Complete(Array.Empty<string>());
                FinishActive(chooser);
                return true;
            }
            NativeCommand captured = command.Value;
            FileChooser started = chooser;
            ThreadPool.QueueUserWorkItem(_ => RunChooser(started, captured));
            return true;
        }

        public static void CancelActive()
        {
            FileChooser? chooser;
            lock (ActiveGate)
                chooser = active;
            chooser?.Cancel();
        }

        public static void RememberPicked(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;
            string? dir = null;
            try
            {
                dir = Path.GetDirectoryName(filePath);
            }
            catch
            {
                return;
            }
            dir = ExistingDirectory(dir);
            if (dir == null || IsSharedLocation(dir))
                return;
            sessionDirectory = dir;
        }

        public static string? ResolveStart(string? requested)
        {
            string? candidate = ExistingDirectory(requested);
            if (candidate != null && !IsSharedLocation(candidate))
                return candidate;
            candidate = ExistingDirectory(sessionDirectory);
            if (candidate != null && !IsSharedLocation(candidate))
                return candidate;
            string? movies = null;
            try
            {
                Func<string?>? folder = MoviesFolder;
                movies = folder != null ? folder() : DefaultMoviesFolder();
            }
            catch
            {
                movies = null;
            }
            candidate = ExistingDirectory(movies);
            if (candidate != null && !IsSharedLocation(candidate))
                return candidate;
            return null;
        }

        public static bool IsSharedLocation(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string part in trimmed.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (part.Equals("Shared", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static void ResetChooserForTests()
        {
            CancelActive();
            lock (ActiveGate)
                active = null;
            sessionDirectory = null;
            LaunchOverride = null;
            MoviesFolder = null;
            LastNotice = null;
        }

        public static NativeCommand? Plan(string platform, string title, string filter, bool multiselect, string? initialDirectory, Func<string, bool>? exists = null)
        {
            Func<string, bool> probe = exists ?? CommandOnPath;
            string safeTitle = (title ?? "").Replace('\r', ' ').Replace('\n', ' ');
            string? start = ResolveStart(initialDirectory);
            (string filterName, string pattern) = SplitFilter(filter);
            if (platform == "macos")
            {
                if (!probe("osascript"))
                    return null;
                string script = MacScript(safeTitle, Extensions(filter), multiselect, start);
                return new NativeCommand("osascript", "-e", script);
            }
            if (platform == "linux")
            {
                if (probe("zenity"))
                    return new NativeCommand("zenity", ZenityArgs(safeTitle, filterName, pattern, multiselect, start));
                if (probe("kdialog"))
                    return new NativeCommand("kdialog", KdialogArgs(safeTitle, filterName, pattern, multiselect, start));
                return null;
            }
            return null;
        }

        public static bool CommandOnPath(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
                return false;
            foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, name)))
                        return true;
                }
                catch
                {
                }
            }
            return false;
        }

        public static string[] Extensions(string? filter)
        {
            var found = new List<string>();
            if (string.IsNullOrWhiteSpace(filter))
                return Array.Empty<string>();
            foreach (string part in filter.Split('|'))
            {
                foreach (string token in part.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string raw = token.Trim();
                    if (!raw.StartsWith("*.", StringComparison.Ordinal) && !raw.StartsWith(".", StringComparison.Ordinal))
                        continue;
                    string item = raw.Trim('*');
                    if (item.StartsWith(".", StringComparison.Ordinal))
                        item = item.Substring(1);
                    if (item.Length == 0 || item.IndexOfAny(new[] { '/', '\\', ' ', '*', '.' }) >= 0)
                        continue;
                    if (!found.Contains(item, StringComparer.OrdinalIgnoreCase))
                        found.Add(item);
                }
            }
            return found.ToArray();
        }

        static string DefaultMoviesFolder()
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (!string.IsNullOrWhiteSpace(videos))
                return videos;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? "";
            return Path.Combine(home, "Movies");
        }

        static void RunChooser(FileChooser chooser, NativeCommand command)
        {
            IChooserProcess? process = null;
            try
            {
                Func<NativeCommand, IChooserProcess> launch = LaunchOverride ?? StartProcess;
                process = launch(command);
                chooser.Attach(process);
                while (!process.Wait(200))
                {
                    if (chooser.CancelRequested)
                    {
                        process.Kill();
                        break;
                    }
                }
                try
                {
                    process.Wait(1000);
                }
                catch
                {
                }
                string[] files = Array.Empty<string>();
                try
                {
                    if (process.ExitCode == 0)
                        files = SplitPaths(process.Output);
                }
                catch
                {
                    files = Array.Empty<string>();
                }
                if (files.Length > 0)
                    RememberPicked(files[0]);
                chooser.Complete(files);
            }
            catch (Exception ex)
            {
                Notice("File dialog failed: " + ex.Message);
                chooser.Complete(Array.Empty<string>());
            }
            finally
            {
                try
                {
                    process?.Dispose();
                }
                catch
                {
                }
                FinishActive(chooser);
            }
        }

        static void FinishActive(FileChooser chooser)
        {
            lock (ActiveGate)
            {
                if (ReferenceEquals(active, chooser))
                    active = null;
            }
        }

        static IChooserProcess StartProcess(NativeCommand command)
        {
            var psi = new ProcessStartInfo(command.FileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in command.Arguments)
                psi.ArgumentList.Add(arg);
            Process process = new Process { StartInfo = psi };
            if (!process.Start())
                throw new InvalidOperationException("File dialog did not start: " + command.FileName);
            ProcessTree.Track(process);
            return new LiveChooser(process);
        }

        sealed class LiveChooser : IChooserProcess
        {
            readonly Process process;
            readonly StringBuilder output = new();
            readonly object gate = new();

            public LiveChooser(Process process)
            {
                this.process = process;
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        return;
                    lock (gate)
                        output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, _) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            public string Output
            {
                get
                {
                    lock (gate)
                        return output.ToString();
                }
            }

            public int ExitCode
            {
                get
                {
                    try
                    {
                        return process.HasExited ? process.ExitCode : -1;
                    }
                    catch
                    {
                        return -1;
                    }
                }
            }

            public bool Wait(int milliseconds)
            {
                try
                {
                    if (!process.WaitForExit(milliseconds))
                        return false;
                    // The timed wait returns before async stdout events are flushed.
                    process.WaitForExit();
                    return true;
                }
                catch
                {
                    return true;
                }
            }

            public void Kill()
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }

            public void Dispose()
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }
            }
        }

        static (string name, string pattern) SplitFilter(string? filter)
        {
            string[] exts = Extensions(filter);
            string pattern = exts.Length == 0 ? "*" : string.Join(" ", exts.Select(ext => "*." + ext));
            string name = "Files";
            if (!string.IsNullOrWhiteSpace(filter))
            {
                string[] parts = filter.Split('|');
                if (parts.Length >= 2 && parts[0].IndexOf('*') < 0 && parts[0].Trim().Length > 0)
                    name = parts[0].Trim();
            }
            return (name, pattern);
        }

        static string? ExistingDirectory(string? initialDirectory)
        {
            if (string.IsNullOrWhiteSpace(initialDirectory))
                return null;
            try
            {
                string full = Path.GetFullPath(initialDirectory);
                if (Directory.Exists(full))
                    return full;
            }
            catch
            {
            }
            return null;
        }

        static string MacScript(string title, string[] extensions, bool multiselect, string? start)
        {
            string prompt = AppleString(title);
            string ofType = "";
            if (extensions.Length > 0)
                ofType = " of type {" + string.Join(", ", extensions.Select(AppleString)) + "}";
            string location = "";
            if (start != null)
                location = " default location (POSIX file " + AppleString(start) + ")";
            string multiple = multiselect ? " with multiple selections allowed" : "";
            if (!multiselect)
                return "POSIX path of (choose file with prompt " + prompt + ofType + location + ")";
            return "set picked to choose file with prompt " + prompt + ofType + location + multiple + "\n"
                + "set outText to \"\"\n"
                + "repeat with f in picked\n"
                + "set outText to outText & (POSIX path of f) & linefeed\n"
                + "end repeat\n"
                + "outText";
        }

        static string[] ZenityArgs(string title, string filterName, string pattern, bool multiselect, string? start)
        {
            var args = new List<string>
            {
                "--file-selection",
                "--title=" + title,
            };
            if (multiselect)
            {
                args.Add("--multiple");
                args.Add("--separator=" + "\n");
            }
            if (start != null)
            {
                string dir = start.EndsWith(Path.DirectorySeparatorChar) ? start : start + Path.DirectorySeparatorChar;
                args.Add("--filename=" + dir);
            }
            if (pattern != "*")
                args.Add("--file-filter=" + filterName + " | " + pattern);
            return args.ToArray();
        }

        static string[] KdialogArgs(string title, string filterName, string pattern, bool multiselect, string? start)
        {
            var args = new List<string>
            {
                "--title",
                title,
            };
            if (multiselect)
            {
                args.Add("--multiple");
                args.Add("--separate-output");
            }
            args.Add("--getopenfilename");
            string folder = start ?? Environment.CurrentDirectory;
            if (IsSharedLocation(folder))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                folder = string.IsNullOrWhiteSpace(home) || IsSharedLocation(home) ? "." : home;
            }
            args.Add(folder);
            args.Add(pattern + "|" + filterName);
            return args.ToArray();
        }

        static string AppleString(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        static string[] SplitPaths(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return Array.Empty<string>();
            var files = new List<string>();
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string path = line.Trim();
                if (path.Length > 0)
                    files.Add(path);
            }
            return files.ToArray();
        }

        static void Notice(string message)
        {
            LastNotice = message;
            try
            {
                ConsoleOutput.WriteLine(message, Color.Yellow);
            }
            catch
            {
                Console.WriteLine(message);
            }
        }
    }
}
