using System;
using System.IO;
using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// DesktopGL keeps the install directory read-only and writes user data elsewhere.
    /// Windows keeps the working directory the process was started in.
    /// </summary>
    public static class DataRoots
    {
        public const string FolderName = "NonsensicalVideoGenerator";

        static readonly string[] SeedDirectories = { "plugins", "templates", "locales", "library", "bin" };

        public static string ResourceRoot => AppContext.BaseDirectory;

        public static string ContentDirectory => Path.Combine(ResourceRoot, "Content");

        public static string DataRoot { get; private set; } = "";

        public static string PlatformKind()
        {
            if (OperatingSystem.IsMacOS())
                return "macos";
            if (OperatingSystem.IsLinux())
                return "linux";
            return "windows";
        }

        public static string Home()
        {
            string? env = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(env))
                return env;
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        /// <summary>
        /// Writable root. NVG_DATA_DIR wins. Otherwise macOS Application Support or the XDG data home.
        /// </summary>
        public static string ResolveDataRoot()
        {
            return ResolveDataRoot(
                Environment.GetEnvironmentVariable("NVG_DATA_DIR"),
                Home(),
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                PlatformKind());
        }

        public static string ResolveDataRoot(string? nvgDataDir, string home, string? xdgDataHome, string platform)
        {
            if (!string.IsNullOrWhiteSpace(nvgDataDir))
            {
                string expanded = ExpandHome(nvgDataDir.Trim(), home);
                return Path.GetFullPath(expanded);
            }
            if (platform == "macos")
                return Path.Combine(home, "Library", "Application Support", FolderName);
            if (platform == "linux")
            {
                if (!string.IsNullOrWhiteSpace(xdgDataHome))
                    return Path.Combine(xdgDataHome.Trim(), FolderName);
                return Path.Combine(home, ".local", "share", FolderName);
            }
            return "";
        }

        /// <summary>
        /// Create the data root, copy missing stock files into it, and make it the working directory.
        /// No-op on Windows. Existing files are left alone.
        /// </summary>
        public static void Apply()
        {
            if (PlatformKind() == "windows")
                return;
            string data = ResolveDataRoot();
            Directory.CreateDirectory(data);
            Seed(ResourceRoot, data);
            DataRoot = data;
            Environment.CurrentDirectory = data;
            try
            {
                ConsoleOutput.WriteLine("Data root: " + data, Color.Gray);
                ConsoleOutput.WriteLine("Resource root: " + ResourceRoot, Color.Gray);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Copy stock plugins, templates, locales, library, bin, and baseroot documents
        /// that are not already in the data root. Returns the number of files copied.
        /// </summary>
        public static int Seed(string resourceRoot, string dataRoot)
        {
            int copied = 0;
            Directory.CreateDirectory(dataRoot);
            foreach (string dir in SeedDirectories)
            {
                string src = Path.Combine(resourceRoot, dir);
                string dst = Path.Combine(dataRoot, dir);
                Directory.CreateDirectory(dst);
                if (Directory.Exists(src))
                    copied += CopyMissing(src, dst);
            }
            if (!Directory.Exists(resourceRoot))
                return copied;
            foreach (string file in Directory.GetFiles(resourceRoot))
            {
                string name = Path.GetFileName(file);
                if (!IsBaserootFile(name))
                    continue;
                string dest = Path.Combine(dataRoot, name);
                if (SameFile(file, dest) || File.Exists(dest))
                    continue;
                File.Copy(file, dest, false);
                copied++;
            }
            return copied;
        }

        public static bool IsBaserootFile(string name)
        {
            if (name.Equals("credits.json", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("steam_appid.txt", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.EndsWith("-LICENSE.txt", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        static int CopyMissing(string sourceDir, string destDir)
        {
            int copied = 0;
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                if (SameFile(file, dest) || File.Exists(dest))
                    continue;
                try
                {
                    File.Copy(file, dest, false);
                    copied++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Seed skip " + file + ": " + ex.Message);
                }
            }
            foreach (string sub in Directory.GetDirectories(sourceDir))
            {
                string name = Path.GetFileName(sub);
                if (name == "." || name == "..")
                    continue;
                copied += CopyMissing(sub, Path.Combine(destDir, name));
            }
            return copied;
        }

        static bool SameFile(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison());
        }

        static StringComparison PathComparison()
        {
            return OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        static string ExpandHome(string path, string home)
        {
            if (path == "~")
                return home;
            if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
                return Path.Combine(home, path.Substring(2));
            return path;
        }
    }
}
