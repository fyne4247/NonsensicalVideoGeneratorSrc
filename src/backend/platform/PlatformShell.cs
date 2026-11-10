using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Opens a folder or file in the desktop environment.
    /// macOS uses open. Linux uses xdg-open. Windows keeps explorer.exe.
    /// </summary>
    public static class PlatformShell
    {
        public static string? LastNotice { get; private set; }

        public static void Open(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    StartJoined(Path.GetFullPath(path), "");
                    return;
                }
                Start(PlanOpen(DataRoots.PlatformKind(), path));
            }
            catch (Exception ex)
            {
                Notice("Shell open failed: " + ex.Message);
            }
        }

        public static void Reveal(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    string full = Path.GetFullPath(path);
                    StartJoined("explorer.exe", "/select, " + Quote(full));
                    return;
                }
                Start(PlanReveal(DataRoots.PlatformKind(), path));
            }
            catch (Exception ex)
            {
                Notice("Shell reveal failed: " + ex.Message);
            }
        }

        public static NativeCommand PlanOpen(string platform, string path)
        {
            string full = Path.GetFullPath(path);
            if (platform == "macos")
                return new NativeCommand("open", full);
            if (platform == "linux")
                return new NativeCommand("xdg-open", full);
            return new NativeCommand(full);
        }

        public static NativeCommand PlanReveal(string platform, string path)
        {
            string full = Path.GetFullPath(path);
            if (platform == "macos")
                return new NativeCommand("open", "-R", full);
            if (platform == "linux")
            {
                string folder = Path.GetDirectoryName(full) ?? full;
                return new NativeCommand("xdg-open", folder);
            }
            return new NativeCommand("explorer.exe", "/select,", full);
        }

        static void Start(NativeCommand command)
        {
            if (!PlatformFileDialog.CommandOnPath(command.FileName))
            {
                Notice("Cannot run '" + command.FileName + "' (not on PATH). Nothing was opened.");
                return;
            }
            var psi = new ProcessStartInfo(command.FileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in command.Arguments)
                psi.ArgumentList.Add(arg);
            Console.WriteLine("> " + command.FileName + " " + string.Join(" ", command.Arguments));
            Process.Start(psi);
        }

        static void StartJoined(string file, string args)
        {
            Console.WriteLine("> " + file + " " + args);
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false });
        }

        static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
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
