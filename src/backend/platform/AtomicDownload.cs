using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Downloads to a unique partial in the destination directory, then publishes
    /// with one move. The partial name is not a library extension, so the library
    /// scan does not list it. The same directory keeps the partial on the same
    /// volume as the final file, so the move is a rename.
    /// Failure, cancel, timeout, and process exit delete the partial and do not
    /// replace a non-empty final file. A 0-byte final is absent and is replaced
    /// only after a good download.
    /// DesktopGL and WindowsDX share this path. The move, the length check, and
    /// the partial name are the same on both. ffprobe's stdin pipe is DesktopGL
    /// only, matching the other tool launches.
    /// Name lookup uses the process default resolver (SocketsHttpHandler, which
    /// calls the OS). Nothing here changes DNS, proxy, or system network settings.
    /// </summary>
    public static class AtomicDownload
    {
        public const string PartialMarker = ".nvgpart";

        /// <summary>TCP connect, including the OS name lookup that happens inside it.</summary>
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(30);

        /// <summary>Whole HTTP transfer. A hang ends instead of blocking until the process is killed.</summary>
        public static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromMinutes(10);

        /// <summary>yt-dlp and the other staged tools. Cancel and app exit still stop them sooner.</summary>
        public static readonly TimeSpan DefaultToolTimeout = TimeSpan.FromMinutes(30);

        public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(30);

        public const int SocketTimeoutSeconds = 30;

        static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".webm", ".mov", ".avi", ".mkv", ".wmv" };
        static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg", ".m4a", ".flac" };
        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        static readonly object Gate = new();
        static readonly HashSet<string> Active = new(PathComparer());
        static readonly List<CancellationTokenSource> LiveTokens = new();
        static readonly List<Process> LiveTools = new();

        static AtomicDownload()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CancelAll();
        }

        /// <summary>
        /// True when a path segment is one of our partials. The library scan and
        /// the sweeper both use this. A normal media name does not match.
        /// </summary>
        public static bool IsPartialPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string normalized = path.Replace('\\', '/');
            foreach (string segment in normalized.Split('/'))
            {
                if (segment.Length == 0)
                    continue;
                if (segment.StartsWith(PartialMarker, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (segment.EndsWith(PartialMarker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Library listing rule: the stock extension test, plus a refusal of partials.
        /// A 0-byte final still matches. Selection uses <see cref="IsSelectable"/> and skips it.
        /// </summary>
        public static bool ListedByLibraryScan(string? path, IEnumerable<string>? extensions)
        {
            if (string.IsNullOrEmpty(path) || extensions == null || IsPartialPath(path))
                return false;
            foreach (string extension in extensions)
            {
                if (!string.IsNullOrEmpty(extension) && path.EndsWith(extension, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A clip GetRandomLibraryFile or generation may return.
        /// Missing, partial, and 0-byte files are not selectable.
        /// When every candidate fails this check, PickRandom returns "" and
        /// GetRandomLibraryFile returns "" as well.
        /// </summary>
        public static bool IsSelectable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || IsPartialPath(path))
                return false;
            try
            {
                var info = new FileInfo(path);
                return info.Exists && info.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Lua "already exists". A 0-byte final is treated as absent.
        /// A non-empty file is left byte-identical.
        /// </summary>
        public static bool ExistsAsValidFile(string? path) => IsSelectable(path);

        public static bool NeedsProbe(string? path)
        {
            return ExtensionIs(path, VideoExtensions) || ExtensionIs(path, AudioExtensions);
        }

        /// <summary>
        /// Non-empty, and equal to the server's Content-Length when it sent one.
        /// A missing length only requires a non-empty body. The probe is separate.
        /// </summary>
        public static bool LengthAccepted(long actual, long? expected)
        {
            if (actual <= 0)
                return false;
            if (expected is long want)
                return actual == want;
            return true;
        }

        public static string SocketTimeoutArgument()
        {
            return "--socket-timeout " + SocketTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        }

        public static string StagingTemplate(string stagingDirectory)
        {
            return Path.Combine(stagingDirectory, "%(title)s.%(ext)s");
        }

        public static string CreateStaging(string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            string staging = Path.Combine(destinationDirectory, PartialMarker + "-" + Guid.NewGuid().ToString("N"));
            Mark(staging);
            try
            {
                Directory.CreateDirectory(staging);
                return staging;
            }
            catch (Exception)
            {
                Unmark(staging);
                throw;
            }
        }

        public static void DeleteStaging(string? staging)
        {
            if (string.IsNullOrWhiteSpace(staging) || !IsPartialPath(staging))
                return;
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            catch (Exception)
            {
            }
            finally
            {
                Unmark(staging);
            }
        }

        /// <summary>
        /// Deletes leftover partial files and staging directories under
        /// <paramref name="root"/>. Skips anything a live download has marked.
        /// Does not delete ordinary files, including 0-byte finals.
        /// Skips reparse points so a link cannot point the delete outside the tree.
        /// </summary>
        public static int SweepPartials(string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return 0;
            string full;
            try
            {
                if (!Directory.Exists(root))
                    return 0;
                full = Path.GetFullPath(root);
            }
            catch (Exception)
            {
                return 0;
            }
            return SweepDirectory(full);
        }

        public static void CancelAll()
        {
            CancellationTokenSource[] tokens;
            Process[] tools;
            lock (Gate)
            {
                tokens = LiveTokens.ToArray();
                tools = LiveTools.ToArray();
            }
            foreach (CancellationTokenSource token in tokens)
            {
                try
                {
                    token.Cancel();
                }
                catch (Exception)
                {
                }
            }
            foreach (Process process in tools)
                TryKill(process);
        }

        public static DownloadResult Fetch(Request request)
        {
            // The caller may be a UI thread with a sync context. Hopping to the
            // pool keeps the HTTP continuation off that context. The caller still
            // waits, which is the same shape as WebClient.DownloadFile.
            return Task.Run(() => FetchAsync(request)).GetAwaiter().GetResult();
        }

        public static async Task<DownloadResult> FetchAsync(Request request)
        {
            if (request == null)
                return DownloadResult.Fail("invalid");
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return DownloadResult.Fail("invalid url");
            if (string.IsNullOrWhiteSpace(request.FinalPath))
                return DownloadResult.Fail("invalid");
            if (request.KeepValidExisting && ExistsAsValidFile(request.FinalPath))
                return DownloadResult.Exists(request.FinalPath);

            string? destinationDir = Path.GetDirectoryName(request.FinalPath);
            if (string.IsNullOrEmpty(destinationDir))
                destinationDir = ".";
            string partial = Path.Combine(
                destinationDir,
                Path.GetFileName(request.FinalPath) + "." + Guid.NewGuid().ToString("N") + PartialMarker);
            Mark(partial);
            TimeSpan overall = request.OverallTimeout ?? DefaultOverallTimeout;
            using var timeout = new CancellationTokenSource(overall);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Cancellation, timeout.Token);
            int timerStopped = 0;
            using var timer = new Timer(_ =>
            {
                if (Volatile.Read(ref timerStopped) != 0)
                    return;
                try
                {
                    if (request.ShouldStop?.Invoke() == true)
                        linked.Cancel();
                }
                catch (Exception)
                {
                    try { linked.Cancel(); } catch (Exception) { }
                }
            }, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
            Register(linked);
            bool published = false;
            try
            {
                Directory.CreateDirectory(destinationDir);
                SweepPartials(destinationDir);
                long? expected;
                try
                {
                    expected = await DownloadBody(request, uri, partial, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return DownloadResult.Fail(CancelDetail(request, timeout));
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
                {
                    return DownloadResult.Fail(FailureDetail(ex));
                }

                var info = new FileInfo(partial);
                long actual = info.Exists ? info.Length : 0;
                if (!LengthAccepted(actual, expected))
                    return DownloadResult.Fail(actual <= 0 ? "empty" : "length");
                if (NeedsProbe(request.FinalPath))
                {
                    bool probed;
                    try
                    {
                        probed = RunProbe(request.Probe, partial);
                    }
                    catch (Exception ex)
                    {
                        return DownloadResult.Fail("probe " + ex.Message);
                    }
                    if (!probed)
                        return DownloadResult.Fail("probe");
                }
                try
                {
                    Trace("File.Move", partial, request.FinalPath);
                    File.Move(partial, request.FinalPath, overwrite: true);
                    published = true;
                }
                catch (Exception ex)
                {
                    return DownloadResult.Fail("move " + ex.Message);
                }
                return DownloadResult.Ok(request.FinalPath);
            }
            catch (OperationCanceledException)
            {
                return DownloadResult.Fail(CancelDetail(request, timeout));
            }
            catch (Exception ex)
            {
                return DownloadResult.Fail(FailureDetail(ex));
            }
            finally
            {
                Interlocked.Exchange(ref timerStopped, 1);
                timer.Dispose();
                Unregister(linked);
                if (!published)
                    TryDeleteFile(partial);
                Unmark(partial);
            }
        }

        /// <summary>
        /// Waits for a tool that is writing into <paramref name="stagingDirectory"/>.
        /// Publishes only after it exits 0 and each audio or video output probes.
        /// </summary>
        public static DownloadResult FinishStaged(
            Process process,
            string stagingDirectory,
            string destinationDirectory,
            Func<bool>? shouldStop,
            Func<string, bool>? probe,
            TimeSpan? overallTimeout,
            CancellationToken cancellation)
        {
            if (process == null)
                return DownloadResult.Fail("invalid");
            RegisterTool(process);
            TimeSpan budget = overallTimeout ?? DefaultToolTimeout;
            DateTime deadline = DateTime.UtcNow + budget;
            try
            {
                while (!HasExited(process))
                {
                    if (cancellation.IsCancellationRequested || Stop(shouldStop))
                    {
                        TryKill(process);
                        Drain(process);
                        return DownloadResult.Fail("cancelled");
                    }
                    if (DateTime.UtcNow >= deadline)
                    {
                        TryKill(process);
                        Drain(process);
                        return DownloadResult.Fail("timeout");
                    }
                    try
                    {
                        if (process.WaitForExit(100))
                            break;
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
                Drain(process);
                int code;
                try
                {
                    code = process.ExitCode;
                }
                catch (Exception)
                {
                    return DownloadResult.Fail("exit");
                }
                if (code != 0)
                    return DownloadResult.Fail("exit " + code.ToString(CultureInfo.InvariantCulture));
                return PublishStaging(stagingDirectory, destinationDirectory, probe);
            }
            finally
            {
                UnregisterTool(process);
            }
        }

        public static bool ProbeWithFfprobe(string? ffprobe, string? mediaPath)
        {
            if (string.IsNullOrWhiteSpace(ffprobe) || string.IsNullOrWhiteSpace(mediaPath))
                return false;
            if (!File.Exists(mediaPath))
                return false;
            Process? process = null;
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = ffprobe,
                    Arguments = "-v error -show_entries format=format_name -of default=noprint_wrappers=1:nokey=1 \""
                        + mediaPath.Replace("\"", "", StringComparison.Ordinal) + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
#if DESKTOPGL
                    RedirectStandardInput = true,
#endif
                };
                process = new Process { StartInfo = start };
                ProcessTree.StartTracked(process);
                RegisterTool(process);
#if DESKTOPGL
                process.StandardInput.Close();
#endif
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                bool exited = ProcessTree.WaitForExit(process, DefaultProbeTimeout);
                if (!exited)
                    return false;
                string text = stdout.GetAwaiter().GetResult().Trim();
                stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    return false;
                return text.Length > 0 && !text.Equals("N/A", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (process != null)
                    UnregisterTool(process);
            }
        }

        static async Task<long?> DownloadBody(Request request, Uri uri, string partial, CancellationToken token)
        {
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = request.ConnectTimeout ?? DefaultConnectTimeout,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                UseProxy = true,
            };
            using var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.UserAgent.ParseAdd("NonsensicalVideoGenerator");
            using HttpResponseMessage response = await client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    "http " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            }
            long? expected = response.Content.Headers.ContentLength;
            await using Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(
                partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (read == 0)
                    break;
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
            await output.FlushAsync(token).ConfigureAwait(false);
            return expected;
        }

        static DownloadResult PublishStaging(string stagingDirectory, string destinationDirectory, Func<string, bool>? probe)
        {
            if (!Directory.Exists(stagingDirectory))
                return DownloadResult.Fail("empty");
            var published = new List<string>();
            string failure = "empty";
            foreach (string source in Directory.GetFiles(stagingDirectory, "*", SearchOption.AllDirectories))
            {
                if (IsReparse(source) || IsPartialPath(Path.GetFileName(source)))
                    continue;
                if (!PublishableOutput(source))
                    continue;
                var info = new FileInfo(source);
                if (!info.Exists || info.Length <= 0)
                {
                    failure = "empty";
                    continue;
                }
                if (NeedsProbe(source))
                {
                    bool ok;
                    try
                    {
                        ok = RunProbe(probe, source);
                    }
                    catch (Exception)
                    {
                        ok = false;
                    }
                    if (!ok)
                    {
                        failure = "probe";
                        continue;
                    }
                }
                string destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
                try
                {
                    Trace("File.Move", source, destination);
                    File.Move(source, destination, overwrite: true);
                    published.Add(destination);
                }
                catch (Exception)
                {
                    failure = "move";
                }
            }
            if (published.Count == 0)
                return DownloadResult.Fail(failure);
            return DownloadResult.OkMany(published);
        }

        static bool RunProbe(Func<string, bool>? probe, string path)
        {
            if (probe != null)
                return probe(path);
            return ProbeWithFfprobe(DefaultFfprobe(), path);
        }

        static string DefaultFfprobe()
        {
            // Matches Global's default: bundled ffprobe on Windows, system ffprobe elsewhere.
            return ToolPaths.Ffprobe(!OperatingSystem.IsWindows());
        }

        static string CancelDetail(Request request, CancellationTokenSource timeout)
        {
            if (request.Cancellation.IsCancellationRequested || Stop(request.ShouldStop))
                return "cancelled";
            if (timeout.IsCancellationRequested)
                return "timeout";
            return "timeout";
        }

        static string FailureDetail(Exception ex)
        {
            string text = ex.ToString();
            if (text.Contains("prematurely", StringComparison.OrdinalIgnoreCase))
                return "length";
            string message = ex.Message;
            if (ex.InnerException != null
                && message.IndexOf(ex.InnerException.Message, StringComparison.Ordinal) < 0)
                message += " " + ex.InnerException.Message;
            if (message.Contains("prematurely", StringComparison.OrdinalIgnoreCase))
                return "length";
            return message;
        }

        static bool Stop(Func<bool>? shouldStop)
        {
            if (shouldStop == null)
                return false;
            try
            {
                return shouldStop();
            }
            catch (Exception)
            {
                return true;
            }
        }

        static int SweepDirectory(string directory)
        {
            if (IsActive(directory))
                return 0;
            int removed = 0;
            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception)
            {
                return removed;
            }
            foreach (string child in children)
            {
                if (IsReparse(child))
                    continue;
                if (IsPartialPath(child))
                {
                    if (IsActive(child))
                        continue;
                    try
                    {
                        Directory.Delete(child, true);
                        removed++;
                    }
                    catch (Exception)
                    {
                    }
                    continue;
                }
                removed += SweepDirectory(child);
            }
            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception)
            {
                return removed;
            }
            foreach (string file in files)
            {
                if (IsReparse(file) || !IsPartialPath(file) || IsActive(file))
                    continue;
                if (TryDeleteFile(file))
                    removed++;
            }
            return removed;
        }

        static bool PublishableOutput(string path)
        {
            return ExtensionIs(path, VideoExtensions)
                || ExtensionIs(path, AudioExtensions)
                || ExtensionIs(path, ImageExtensions);
        }

        static bool ExtensionIs(string? path, string[] extensions)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string extension = Path.GetExtension(path);
            foreach (string candidate in extensions)
            {
                if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static bool IsReparse(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }

        static bool HasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (Exception)
            {
                return true;
            }
        }

        static void Drain(Process process)
        {
            try
            {
                process.WaitForExit();
            }
            catch (Exception)
            {
            }
        }

        static void TryKill(Process process)
        {
            try
            {
                if (!HasExited(process))
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
            }
        }

        static bool TryDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;
                Trace("File.Delete", path, "");
                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Trace(string op, string left, string right)
        {
            if (Environment.GetEnvironmentVariable("NVG_TRACE") != "1")
                return;
            Console.WriteLine("NVG_TRACE " + op + " " + left + " " + right);
        }

        static void Mark(string path)
        {
            lock (Gate)
                Active.Add(Full(path));
        }

        static void Unmark(string path)
        {
            lock (Gate)
                Active.Remove(Full(path));
        }

        static bool IsActive(string path)
        {
            string full = Full(path);
            lock (Gate)
            {
                if (Active.Contains(full))
                    return true;
                foreach (string active in Active)
                {
                    if (IsUnder(active, full))
                        return true;
                }
            }
            return false;
        }

        static bool IsUnder(string root, string candidate)
        {
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, PathComparison());
        }

        static string Full(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }

        static void Register(CancellationTokenSource source)
        {
            lock (Gate)
                LiveTokens.Add(source);
        }

        static void Unregister(CancellationTokenSource source)
        {
            lock (Gate)
                LiveTokens.Remove(source);
        }

        static void RegisterTool(Process process)
        {
            lock (Gate)
                LiveTools.Add(process);
        }

        static void UnregisterTool(Process process)
        {
            lock (Gate)
                LiveTools.Remove(process);
        }

        static StringComparer PathComparer()
        {
            return OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        static StringComparison PathComparison()
        {
            return OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        public sealed class Request
        {
            public string Url { get; init; } = "";
            public string FinalPath { get; init; } = "";

            /// <summary>
            /// When true, a non-empty final file is returned as already existing
            /// and is not opened. A 0-byte file is still replaced after a good download.
            /// </summary>
            public bool KeepValidExisting { get; init; }

            public CancellationToken Cancellation { get; init; }
            public Func<bool>? ShouldStop { get; init; }
            public Func<string, bool>? Probe { get; init; }
            public TimeSpan? ConnectTimeout { get; init; }
            public TimeSpan? OverallTimeout { get; init; }
        }

        public sealed class DownloadResult
        {
            public bool Published { get; init; }
            public bool AlreadyExisted { get; init; }
            public string FinalPath { get; init; } = "";
            public IReadOnlyList<string> PublishedPaths { get; init; } = Array.Empty<string>();
            public string Detail { get; init; } = "";

            public static DownloadResult Ok(string path)
            {
                return new DownloadResult
                {
                    Published = true,
                    FinalPath = path,
                    PublishedPaths = new[] { path },
                    Detail = "ok",
                };
            }

            public static DownloadResult OkMany(IReadOnlyList<string> paths)
            {
                string first = paths.Count > 0 ? paths[0] : "";
                return new DownloadResult
                {
                    Published = paths.Count > 0,
                    FinalPath = first,
                    PublishedPaths = paths,
                    Detail = "ok",
                };
            }

            public static DownloadResult Exists(string path)
            {
                return new DownloadResult
                {
                    Published = false,
                    AlreadyExisted = true,
                    FinalPath = path,
                    PublishedPaths = Array.Empty<string>(),
                    Detail = "already exists",
                };
            }

            public static DownloadResult Fail(string detail)
            {
                return new DownloadResult
                {
                    Published = false,
                    Detail = string.IsNullOrEmpty(detail) ? "failed" : detail,
                };
            }
        }
    }
}
