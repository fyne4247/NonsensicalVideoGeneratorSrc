using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Opens a steam:// or https address without touching a Steam account.
    /// DesktopGL uses steam:// only when a handler is installed, and otherwise
    /// the https page inside steam://openurl/. Windows tries steam:// first and
    /// uses https only when that launch fails. Plans are pure. The real launch
    /// goes through <see cref="LaunchOverride"/> in tests so Steam is not started.
    /// </summary>
    public readonly struct UrlLaunch
    {
        public UrlLaunch(string fileName, string[] arguments, bool useShellExecute, string? fallbackUrl)
        {
            FileName = fileName ?? "";
            Arguments = arguments ?? Array.Empty<string>();
            UseShellExecute = useShellExecute;
            FallbackUrl = string.IsNullOrEmpty(fallbackUrl) ? null : fallbackUrl;
        }

        public string FileName { get; }
        public string[] Arguments { get; }
        public bool UseShellExecute { get; }

        /// <summary>Https page to open when <see cref="FileName"/> fails. Null when there is nothing else to try.</summary>
        public string? FallbackUrl { get; }
    }

    public static class PlatformUrl
    {
        /// <summary>Test hook. Null starts a process. Return false to simulate a failed launch.</summary>
        public static Func<UrlLaunch, bool>? LaunchOverride { get; set; }

        /// <summary>Test hook. Null asks the operating system. Must not launch Steam.</summary>
        public static Func<bool>? HandlerOverride { get; set; }

        public static void ResetForTests()
        {
            LaunchOverride = null;
            HandlerOverride = null;
        }

        public static bool IsSteam(string? url)
        {
            return !string.IsNullOrWhiteSpace(url)
                && url.Trim().StartsWith("steam://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The https or http page a steam://openurl/ link points at.
        /// An address that is already http(s) is returned unchanged.
        /// </summary>
        public static string? HttpsTarget(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            string trimmed = url.Trim();
            if (IsWeb(trimmed))
                return trimmed;
            const string prefix = "steam://openurl/";
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            string rest = trimmed.Substring(prefix.Length);
            if (!IsWeb(rest))
                return null;
            return rest;
        }

        public static UrlLaunch Plan(string platform, string? url, bool steamHandlerExists)
        {
            string trimmed = (url ?? "").Trim();
            string? https = HttpsTarget(trimmed);
            bool steam = IsSteam(trimmed);
            if (platform == "windows")
            {
                if (steam)
                    return new UrlLaunch(trimmed, Array.Empty<string>(), true, https);
                return new UrlLaunch(trimmed, Array.Empty<string>(), true, null);
            }
            string target = trimmed;
            string? fallback = null;
            if (steam)
            {
                if (steamHandlerExists)
                {
                    target = trimmed;
                    fallback = https;
                }
                else if (!string.IsNullOrEmpty(https))
                {
                    target = https;
                }
            }
            if (platform == "macos")
                return new UrlLaunch("open", new[] { target }, false, fallback);
            if (platform == "linux")
                return new UrlLaunch("xdg-open", new[] { target }, false, fallback);
            return new UrlLaunch(target, Array.Empty<string>(), true, fallback);
        }

        /// <summary>The address a person would see opened, after the steam-or-https choice.</summary>
        public static string Address(string platform, string? url, bool steamHandlerExists)
        {
            UrlLaunch launch = Plan(platform, url, steamHandlerExists);
            if (launch.UseShellExecute || launch.Arguments.Length == 0)
                return launch.FileName;
            return launch.Arguments[launch.Arguments.Length - 1];
        }

        public static string Format(UrlLaunch launch)
        {
            if (launch.Arguments.Length == 0)
                return launch.FileName;
            return launch.FileName + " " + string.Join(" ", launch.Arguments);
        }

        public static bool SteamHandlerInstalled()
        {
            Func<bool>? hook = HandlerOverride;
            if (hook != null)
                return hook();
            return Detect(DataRoots.PlatformKind());
        }

        /// <summary>
        /// Whether a steam:// handler is installed. Reads plists, xdg-mime, or the
        /// Windows protocol key. It does not start Steam or open a URL.
        /// </summary>
        public static bool Detect(string platform)
        {
            try
            {
                if (platform == "macos")
                    return MacSteamInstalled();
                if (platform == "linux")
                    return LinuxSteamHandler();
                if (platform == "windows")
                    return WindowsSteamProtocol();
            }
            catch
            {
            }
            return false;
        }

        public static bool XmlDeclaresSteamScheme(string? xml)
        {
            if (string.IsNullOrEmpty(xml))
                return false;
            return xml.IndexOf(">steam<", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool XdgReportsHandler(string? stdout)
        {
            if (string.IsNullOrWhiteSpace(stdout))
                return false;
            return stdout.Trim().EndsWith(".desktop", StringComparison.Ordinal);
        }

        public static void Open(string? url)
        {
            Open(DataRoots.PlatformKind(), url, SteamHandlerInstalled());
        }

        public static void Open(string platform, string? url, bool steamHandlerExists)
        {
            UrlLaunch launch = Plan(platform, url, steamHandlerExists);
            if (TryLaunch(launch))
                return;
            if (string.IsNullOrEmpty(launch.FallbackUrl))
            {
                Notice("Could not open " + (url ?? "") + ".");
                return;
            }
            UrlLaunch fallback = Plan(platform, launch.FallbackUrl, false);
            if (!TryLaunch(fallback))
                Notice("Could not open " + launch.FallbackUrl + ".");
        }

        static bool TryLaunch(UrlLaunch launch)
        {
            Func<UrlLaunch, bool>? hook = LaunchOverride;
            if (hook != null)
                return hook(launch);
            try
            {
                ConsoleOutput.WriteLine("> " + Format(launch), Color.Transparent);
                var start = new ProcessStartInfo
                {
                    FileName = launch.FileName,
                    UseShellExecute = launch.UseShellExecute,
                };
                if (launch.UseShellExecute)
                    start.Verb = "open";
                else
                {
                    foreach (string argument in launch.Arguments)
                        start.ArgumentList.Add(argument);
                }
                using Process? process = Process.Start(start);
                if (process == null)
                    return false;
                if (launch.UseShellExecute)
                    return true;
                if (!process.WaitForExit(3000))
                    return true;
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Notice(ex.Message);
                return false;
            }
        }

        static bool IsWeb(string value)
        {
            return value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        static bool MacSteamInstalled()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? "";
            string[] plists =
            {
                "/Applications/Steam.app/Contents/Info.plist",
                Path.Combine(home, "Applications", "Steam.app", "Contents", "Info.plist"),
            };
            foreach (string plist in plists)
            {
                if (!File.Exists(plist))
                    continue;
                if (XmlDeclaresSteamScheme(ReadPlistXml(plist)))
                    return true;
            }
            return false;
        }

        static string ReadPlistXml(string path)
        {
            if (Run("plutil", out string stdout, out _, "-convert", "xml1", "-o", "-", path)
                && stdout.Length > 0)
                return stdout;
            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                return "";
            }
        }

        static bool LinuxSteamHandler()
        {
            if (!Run("xdg-mime", out string stdout, out _, "query", "default", "x-scheme-handler/steam"))
                return false;
            return XdgReportsHandler(stdout);
        }

        static bool WindowsSteamProtocol()
        {
            if (!OperatingSystem.IsWindows())
                return false;
            try
            {
                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"steam\shell\open\command");
                return key?.GetValue(null) is string command && command.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        static bool Run(string file, out string stdout, out string stderr, params string[] args)
        {
            stdout = "";
            stderr = "";
            try
            {
                var start = new ProcessStartInfo(file)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                foreach (string argument in args)
                    start.ArgumentList.Add(argument);
                using Process? process = Process.Start(start);
                if (process == null)
                    return false;
                Task<string> outTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(3000))
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                    }
                    return false;
                }
                stdout = outTask.GetAwaiter().GetResult();
                stderr = errTask.GetAwaiter().GetResult();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        static void Notice(string message)
        {
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
