using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Resolves ffmpeg, ffprobe, magick, yt-dlp, deno and vocoder.
    /// Windows keeps today's rule: a use-system flag selects the bare name on PATH,
    /// otherwise <c>.\bin\&lt;name&gt;.exe</c>. deno and vocoder stay bundled on Windows.
    /// Everywhere else the order is the bundled file under the resource root
    /// (<c>bin/&lt;name&gt;</c>, then <c>bin/&lt;name&gt;.exe</c>), then PATH.
    /// On macOS, <see cref="MacWellKnownBins"/> is searched after PATH. A
    /// Finder-launched app inherits launchd's PATH and would otherwise miss
    /// a Homebrew ffmpeg in <c>/opt/homebrew/bin</c> or <c>/usr/local/bin</c>.
    /// </summary>
    public static class ToolPaths
    {
        public static readonly string[] RequiredEncoders = { "libx264", "aac", "libmp3lame" };
        public static readonly string[] RequiredCodecs = { "h264", "aac", "pcm_s16le" };
        public static readonly string[] RequiredFilters =
        {
            "aresample", "concat", "reverse", "areverse", "setpts", "atempo",
            "scale", "fps", "volume", "amix", "overlay", "colorkey",
            "chorus", "vibrato", "hue", "hflip", "vflip", "negate", "apad",
            "setsar", "format", "eq", "frei0r",
        };

        static readonly Dictionary<string, ToolReport> FfmpegCache = new();

        static string? resourceRoot;

        /// <summary>Read-only app directory. Tests point this at a temp folder.</summary>
        public static string ResourceRoot
        {
            get => string.IsNullOrEmpty(resourceRoot) ? AppContext.BaseDirectory : resourceRoot!;
            set => resourceRoot = value;
        }

        /// <summary>Test hook. "windows", "macos" or "linux". Null uses the host OS.</summary>
        public static string? PlatformOverride { get; set; }

        /// <summary>
        /// Directories searched on macOS after the bundled file and PATH.
        /// Tests replace the array and restore <see cref="DefaultMacWellKnownBins"/>.
        /// </summary>
        public static readonly string[] DefaultMacWellKnownBins = { "/opt/homebrew/bin", "/usr/local/bin" };

        public static string[] MacWellKnownBins { get; set; } = new[] { "/opt/homebrew/bin", "/usr/local/bin" };

        public static string Platform()
        {
            if (!string.IsNullOrEmpty(PlatformOverride))
                return PlatformOverride!;
            if (OperatingSystem.IsWindows())
                return "windows";
            if (OperatingSystem.IsMacOS())
                return "macos";
            return "linux";
        }

        public static string Ffmpeg(bool useSystemOnWindows = false) => Resolve("ffmpeg", useSystemOnWindows);
        public static string Ffprobe(bool useSystemOnWindows = false) => Resolve("ffprobe", useSystemOnWindows);
        public static string Magick(bool useSystemOnWindows = false) => Resolve("magick", useSystemOnWindows);
        public static string YtDlp(bool useSystemOnWindows = false) => Resolve("yt-dlp", useSystemOnWindows);
        public static string Deno(bool useSystemOnWindows = false) => Resolve("deno", useSystemOnWindows);
        public static string Vocoder(bool useSystemOnWindows = false) => Resolve("vocoder", useSystemOnWindows);

        public static string Resolve(string name, bool useSystemOnWindows = false)
        {
            if (Platform() == "windows")
                return ResolveWindows(name, useSystemOnWindows);
            return ResolveBundledThenPath(name);
        }

        public static bool IsAvailable(string name, bool useSystemOnWindows = false)
        {
            if (Platform() == "windows")
            {
                if (name == "deno" || name == "vocoder" || !useSystemOnWindows)
                    return File.Exists(Path.Combine(".", "bin", name + ".exe"))
                        || File.Exists(Path.Combine(ResourceRoot, "bin", name + ".exe"));
                return FindOnPath(name) != null;
            }
            string resolved = ResolveBundledThenPath(name);
            return !IsBareName(resolved) && File.Exists(resolved);
        }

        /// <summary>
        /// ffmpeg presence plus the encoders, codecs and filters stock effects use.
        /// The live probe is cached per resolved path. Pass <paramref name="runner"/>
        /// to parse sample output without launching ffmpeg.
        /// </summary>
        public static ToolReport InspectFfmpeg(bool useSystemOnWindows = false, Func<string, string>? runner = null)
        {
            string resolved = Resolve("ffmpeg", useSystemOnWindows);
            if (!IsAvailable("ffmpeg", useSystemOnWindows))
            {
                var missing = new List<string>();
                foreach (string name in RequiredEncoders)
                    missing.Add("encoder:" + name);
                foreach (string name in RequiredCodecs)
                    missing.Add("codec:" + name);
                foreach (string name in RequiredFilters)
                    missing.Add("filter:" + name);
                return new ToolReport
                {
                    Ok = false,
                    Path = resolved,
                    Missing = missing,
                    Message = MissingFfmpegMessage(),
                };
            }
            if (runner == null && FfmpegCache.TryGetValue(resolved, out ToolReport? cached))
                return cached;
            string encoders = runner != null ? runner("-encoders") : Probe(resolved, "-encoders");
            string codecs = runner != null ? runner("-codecs") : Probe(resolved, "-codecs");
            string filters = runner != null ? runner("-filters") : Probe(resolved, "-filters");
            List<string> gaps = MissingCapabilities(encoders, codecs, filters);
            var report = new ToolReport
            {
                Ok = gaps.Count == 0,
                Path = resolved,
                Missing = gaps,
                Message = gaps.Count == 0
                    ? "ffmpeg " + resolved + ": required codecs and filters present."
                    : "ffmpeg " + resolved + ": missing " + string.Join(", ", gaps) + ".",
            };
            if (runner == null)
                FfmpegCache[resolved] = report;
            return report;
        }

        public static List<string> MissingCapabilities(string encodersText, string codecsText, string filtersText)
        {
            var missing = new List<string>();
            foreach (string name in RequiredEncoders)
            {
                if (!HasToken(encodersText, name))
                    missing.Add("encoder:" + name);
            }
            foreach (string name in RequiredCodecs)
            {
                if (!HasToken(codecsText, name))
                    missing.Add("codec:" + name);
            }
            foreach (string name in RequiredFilters)
            {
                if (!HasToken(filtersText, name))
                    missing.Add("filter:" + name);
            }
            return missing;
        }

        public static bool HasToken(string? text, string name)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            return Regex.IsMatch(text, @"(^|[\s])" + Regex.Escape(name) + @"($|[\s])", RegexOptions.Multiline);
        }

        public static string? FindOnPath(string name)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;
            string[] suffixes = Platform() == "windows" ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string suffix in suffixes)
                {
                    string candidate = Path.Combine(dir, name + suffix);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            return null;
        }

        static string ResolveWindows(string name, bool useSystemOnWindows)
        {
            if (name == "deno")
                return @".\bin\deno.exe";
            if (name == "vocoder")
                return @".\bin\vocoder.exe";
            if (useSystemOnWindows)
                return name;
            return @".\bin\" + name + ".exe";
        }

        static string ResolveBundledThenPath(string name)
        {
            foreach (string fileName in BundledFileNames(name))
            {
                string bundled = Path.Combine(ResourceRoot, "bin", fileName);
                if (File.Exists(bundled))
                    return bundled;
            }
            string? onPath = FindOnPath(name);
            if (onPath != null)
                return onPath;
            if (Platform() == "macos")
            {
                string? wellKnown = FindInDirectories(name, MacWellKnownBins);
                if (wellKnown != null)
                    return wellKnown;
            }
            return name;
        }

        static string MissingFfmpegMessage()
        {
            string looked = "ffmpeg missing. Looked in bundled " + Path.Combine(ResourceRoot, "bin") + " then PATH";
            if (Platform() == "macos")
                looked += " then " + string.Join(" and ", MacWellKnownBins);
            return looked + ".";
        }

        static string? FindInDirectories(string name, IEnumerable<string> directories)
        {
            foreach (string dir in directories)
            {
                if (string.IsNullOrEmpty(dir))
                    continue;
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        public static IReadOnlyList<string> BundledFileNames(string name)
        {
            if (Platform() == "windows")
                return new[] { name + ".exe", name };
            return new[] { name, name + ".exe" };
        }

        static bool IsBareName(string resolved)
        {
            return resolved.IndexOfAny(new[] { '/', '\\' }) < 0;
        }

        static string Probe(string ffmpeg, string arg)
        {
            try
            {
                var startInfo = new ProcessStartInfo(ffmpeg, "-hide_banner " + arg)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using Process? process = Process.Start(startInfo);
                if (process == null)
                    return "";
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(15000);
                return output;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    public sealed class ToolReport
    {
        public bool Ok { get; init; }
        public string Path { get; init; } = "";
        public string Message { get; init; } = "";
        public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
    }
}
