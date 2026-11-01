using System;
using System.Diagnostics;
using System.IO;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// How to relaunch after a setting that cannot apply in-process (UI scale).
    /// Windows keeps steam://run. DesktopGL starts this install again.
    /// </summary>
    public readonly struct AppRestartRequest
    {
        public AppRestartRequest(string fileName, string[] arguments, bool useShellExecute, string verb, bool inheritEnvironment)
        {
            FileName = fileName ?? "";
            Arguments = arguments ?? Array.Empty<string>();
            UseShellExecute = useShellExecute;
            Verb = verb ?? "";
            InheritEnvironment = inheritEnvironment;
        }

        public string FileName { get; }
        public string[] Arguments { get; }
        public bool UseShellExecute { get; }
        public string Verb { get; }
        public bool InheritEnvironment { get; }
    }

    public static class AppRestart
    {
        /// <summary>
        /// Decide the relaunch command. Does not start a process.
        /// macOS inside a .app uses open -n, and passes NVG_DATA_DIR as one --env value when it is set.
        /// macOS outside a bundle, and Linux, re-exec the same binary and inherit the environment.
        /// </summary>
        public static AppRestartRequest Plan(string platform, string steamAppId, string? nvgDataDir, string? baseDirectory, string? executablePath)
        {
            if (platform == "windows")
            {
                return new AppRestartRequest(
                    "steam://run/" + (steamAppId ?? ""),
                    Array.Empty<string>(),
                    true,
                    "open",
                    false);
            }

            if (platform == "macos")
            {
                string? bundle = EnclosingApp(baseDirectory);
                if (!string.IsNullOrEmpty(bundle))
                {
                    string? data = string.IsNullOrWhiteSpace(nvgDataDir) ? null : nvgDataDir.Trim();
                    if (data == null)
                    {
                        return new AppRestartRequest(
                            "open",
                            new[] { "-n", bundle },
                            false,
                            "",
                            false);
                    }
                    return new AppRestartRequest(
                        "open",
                        new[] { "-n", "--env", "NVG_DATA_DIR=" + data, bundle },
                        false,
                        "",
                        false);
                }
            }

            return new AppRestartRequest(
                executablePath ?? "",
                Array.Empty<string>(),
                false,
                "",
                true);
        }

        /// <summary>
        /// Walk up from the app base directory to the enclosing Foo.app bundle, if any.
        /// </summary>
        public static string? EnclosingApp(string? baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                return null;
            DirectoryInfo? dir;
            try
            {
                dir = new DirectoryInfo(baseDirectory);
            }
            catch
            {
                return null;
            }
            while (dir != null)
            {
                if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        public static string FormatCommand(AppRestartRequest request)
        {
            if (request.Arguments.Length == 0)
                return request.FileName;
            return request.FileName + " " + string.Join(" ", request.Arguments);
        }

        public static void Start(AppRestartRequest request)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                UseShellExecute = request.UseShellExecute,
            };
            if (request.UseShellExecute)
                startInfo.Verb = request.Verb;
            else
            {
                foreach (string argument in request.Arguments)
                    startInfo.ArgumentList.Add(argument);
            }
            Process.Start(startInfo);
        }
    }
}
