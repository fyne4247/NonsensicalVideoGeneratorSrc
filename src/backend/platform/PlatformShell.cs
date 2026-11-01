using System;
using System.Diagnostics;
using System.IO;

namespace NonsensicalVideoGenerator
{
    public static class PlatformShell
    {
        public static void Open(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (OperatingSystem.IsMacOS())
                    Start("open", Quote(full));
                else if (OperatingSystem.IsLinux())
                    Start("xdg-open", Quote(full));
                else
                    Start(full, "");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Shell open failed: " + ex.Message);
            }
        }

        public static void Reveal(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (OperatingSystem.IsMacOS())
                    Start("open", "-R " + Quote(full));
                else if (OperatingSystem.IsLinux())
                    Start("xdg-open", Quote(Path.GetDirectoryName(full) ?? full));
                else
                    Start("explorer.exe", "/select, " + Quote(full));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Shell reveal failed: " + ex.Message);
            }
        }

        static void Start(string file, string args)
        {
            Console.WriteLine("> " + file + " " + args);
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false });
        }

        static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
