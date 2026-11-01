using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Minimal DesktopGL file picker. macOS uses osascript; Linux uses zenity.
    /// A missing helper or a cancel returns an empty list. It does not hang.
    /// </summary>
    public static class PlatformFileDialog
    {
        public static string[] OpenFiles(string title, string filter, bool multiselect, string? initialDirectory)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                    return MacChoose(title);
                if (OperatingSystem.IsLinux())
                    return ZenityChoose(title, multiselect);
            }
            catch (Exception ex)
            {
                Console.WriteLine("File dialog failed: " + ex.Message);
            }
            Console.WriteLine("File dialog unavailable for title '" + title + "' filter '" + filter + "'.");
            return Array.Empty<string>();
        }

        static string[] MacChoose(string title)
        {
            string script = "POSIX path of (choose file with prompt " + Quote(title) + ")";
            string output = Run("osascript", "-e " + Quote(script));
            string path = output.Trim();
            if (path.Length == 0)
                return Array.Empty<string>();
            return new[] { path };
        }

        static string[] ZenityChoose(string title, bool multiselect)
        {
            string args = "--file-selection --title=" + Quote(title);
            if (multiselect)
                args += " --multiple --separator='\n'";
            string output = Run("zenity", args);
            if (string.IsNullOrWhiteSpace(output))
                return Array.Empty<string>();
            var files = new List<string>();
            foreach (string line in output.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    files.Add(line.Trim());
            }
            return files.ToArray();
        }

        static string Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process? process = Process.Start(psi);
            if (process == null)
                return "";
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return "";
            return output;
        }

        static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
