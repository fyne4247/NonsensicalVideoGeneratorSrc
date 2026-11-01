using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// DesktopGL file picker. macOS uses osascript. Linux tries zenity, then kdialog.
    /// Cancel and a missing helper both return an empty list. A missing helper does not wait.
    /// </summary>
    public static class PlatformFileDialog
    {
        public static string? LastNotice { get; private set; }

        public static string[] OpenFiles(string title, string filter, bool multiselect, string? initialDirectory)
        {
            try
            {
                NativeCommand? command = Plan(DataRoots.PlatformKind(), title, filter, multiselect, initialDirectory, null);
                if (command == null)
                {
                    Notice("File dialog is unavailable (osascript, zenity, and kdialog were not found). Nothing was selected for '" + title + "'.");
                    return Array.Empty<string>();
                }
                if (!Run(command.Value, out string output))
                    return Array.Empty<string>();
                return SplitPaths(output);
            }
            catch (Exception ex)
            {
                Notice("File dialog failed: " + ex.Message);
                return Array.Empty<string>();
            }
        }

        public static NativeCommand? Plan(string platform, string title, string filter, bool multiselect, string? initialDirectory, Func<string, bool>? exists = null)
        {
            Func<string, bool> probe = exists ?? CommandOnPath;
            string safeTitle = (title ?? "").Replace('\r', ' ').Replace('\n', ' ');
            string? start = ExistingDirectory(initialDirectory);
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
            args.Add(start ?? Environment.CurrentDirectory);
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

        static bool Run(NativeCommand command, out string output)
        {
            output = "";
            var psi = new ProcessStartInfo(command.FileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            foreach (string arg in command.Arguments)
                psi.ArgumentList.Add(arg);
            using Process? process = Process.Start(psi);
            if (process == null)
            {
                Notice("File dialog did not start: " + command.FileName);
                return false;
            }
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
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
