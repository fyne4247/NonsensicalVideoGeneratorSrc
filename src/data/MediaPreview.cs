using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Thumbnail cache and DesktopGL playback files.
    /// The vendored player treats EAGAIN as -11. macOS EAGAIN is 35, so a
    /// B-frame packet kills the decode thread and the preview stays blank.
    /// Library thumbnails used to share one temp/thumb.bmp and rerun ffmpeg
    /// on every open. Callers that must not block the game thread invoke
    /// these methods from a background worker.
    /// </summary>
    public static class MediaPreview
    {
        public const string VideoKind = "video";
        public const string AudioKind = "audio";
        public const string ImageKind = "image";

        static readonly object PublishGate = new();

        public readonly struct ThumbnailResult
        {
            public string Path { get; init; }
            public bool FromCache { get; init; }
            public bool Cancelled { get; init; }
            public string Error { get; init; }
            public bool Ok => !Cancelled && string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(Path) && File.Exists(Path);

            public static ThumbnailResult Cached(string path) => new() { Path = path, FromCache = true, Error = "" };
            public static ThumbnailResult Created(string path) => new() { Path = path, FromCache = false, Error = "" };
            public static ThumbnailResult Failed(string error) => new() { Path = "", FromCache = false, Error = error ?? "" };
            public static ThumbnailResult CancelledResult() => new() { Path = "", Cancelled = true, Error = "" };
        }

        public static string Fingerprint(string fullPath, long ticks, long length, int width, int height)
        {
            string raw = fullPath
                + "\n" + ticks.ToString(CultureInfo.InvariantCulture)
                + "\n" + length.ToString(CultureInfo.InvariantCulture)
                + "\n" + width.ToString(CultureInfo.InvariantCulture)
                + "x" + height.ToString(CultureInfo.InvariantCulture);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash);
        }

        public static string ThumbnailFile(string cacheDir, string sourcePath, int width, int height)
        {
            string full = Path.GetFullPath(sourcePath);
            long ticks = 0;
            long length = 0;
            if (File.Exists(full))
            {
                FileInfo info = new FileInfo(full);
                ticks = info.LastWriteTimeUtc.Ticks;
                length = info.Length;
            }
            return Path.Combine(cacheDir, Fingerprint(full, ticks, length, width, height) + ".bmp");
        }

        public static string PlaybackFile(string cacheDir, string sourcePath)
        {
            string full = Path.GetFullPath(sourcePath);
            long ticks = 0;
            long length = 0;
            if (File.Exists(full))
            {
                FileInfo info = new FileInfo(full);
                ticks = info.LastWriteTimeUtc.Ticks;
                length = info.Length;
            }
            return Path.Combine(cacheDir, Fingerprint(full, ticks, length, 0, 0) + ".mp4");
        }

        public static bool ProbeTextIsFriendly(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string line = text.Trim();
            int cut = line.IndexOfAny(new[] { '\r', '\n' });
            if (cut >= 0)
                line = line.Substring(0, cut).Trim();
            return line == "0";
        }

        public static bool IsDesktopPlayerFriendly(string ffprobe, string mediaPath)
        {
            if (!CanRun(ffprobe) || string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
                return false;
            string output = RunCapture(ffprobe, "-v error -select_streams v:0 -show_entries stream=has_b_frames -of csv=p=0 " + Q(Path.GetFullPath(mediaPath)));
            return ProbeTextIsFriendly(output);
        }

        /// <summary>
        /// Returns a file the DesktopGL player can decode.
        /// A stream with has_b_frames 0 is returned as-is. Anything else is
        /// transcoded with -bf 0 into <paramref name="cacheDir"/>. A missing
        /// ffmpeg returns the original path.
        /// </summary>
        public static string EnsurePlayable(string ffmpeg, string ffprobe, string mediaPath, string cacheDir, string? writableRoot = null)
        {
            string resolved = ResolveInput(mediaPath, writableRoot);
            if (!File.Exists(resolved))
                return mediaPath;
            if (IsDesktopPlayerFriendly(ffprobe, resolved))
                return resolved;
            if (!CanRun(ffmpeg))
                return resolved;
            Directory.CreateDirectory(cacheDir);
            string finalPath = PlaybackFile(cacheDir, resolved);
            if (IsReady(finalPath))
            {
                if (IsDesktopPlayerFriendly(ffprobe, finalPath))
                    return finalPath;
                TryDelete(finalPath);
            }
            string partial = finalPath + "." + Guid.NewGuid().ToString("N") + ".partial.mp4";
            try
            {
                int code = Run(ffmpeg, PlaybackArguments(resolved, partial), null, out string err);
                if (!IsReady(partial))
                    return resolved;
                try
                {
                    Publish(partial, finalPath);
                }
                catch (Exception ex)
                {
                    if (!IsReady(finalPath))
                        throw new IOException(string.IsNullOrWhiteSpace(err) ? ex.Message : err, ex);
                }
                if (IsReady(finalPath))
                    return finalPath;
                _ = code;
                return resolved;
            }
            finally
            {
                TryDelete(partial);
            }
        }

        public static string PlaybackArguments(string source, string output)
        {
            return "-y -loglevel error -i " + Q(source)
                + " -map 0:v:0 -map 0:a:0? -c:v libx264 -preset ultrafast -bf 0 -pix_fmt yuv420p"
                + " -c:a aac -ac 2 -ar 44100 -b:a 160k -movflags +faststart -fflags +genpts -reset_timestamps 1 "
                + Q(output);
        }

        public static string ThumbnailArguments(string source, string output, int width, int height, string kind, bool seek)
        {
            string w = width.ToString(CultureInfo.InvariantCulture);
            string h = height.ToString(CultureInfo.InvariantCulture);
            if (kind == AudioKind)
            {
                return "-y -loglevel error -i " + Q(source)
                    + " -filter_complex \"[0:a]aformat=channel_layouts=mono,showwavespic=s=" + w + "x" + h + ":colors=white[thumb]\""
                    + " -map \"[thumb]\" -frames:v 1 -update 1 -pix_fmt bgr24 " + Q(output);
            }
            string vf = "scale=" + w + ":" + h + ":force_original_aspect_ratio=decrease,pad=" + w + ":" + h + ":(ow-iw)/2:(oh-ih)/2";
            string ss = seek && kind != ImageKind ? "-ss 0.5 " : "";
            return "-y -loglevel error " + ss + "-i " + Q(source) + " -frames:v 1 -vf \"" + vf + "\" -pix_fmt bgr24 " + Q(output);
        }

        /// <summary>
        /// Returns a cached BMP of <paramref name="width"/> by <paramref name="height"/>.
        /// A hit does not start ffmpeg. The file is never the shared temp/thumb.bmp.
        /// </summary>
        public static ThumbnailResult EnsureThumbnail(string ffmpeg, string sourcePath, string cacheDir, int width, int height, string kind, string? writableRoot = null, Func<bool>? stillCurrent = null)
        {
            if (stillCurrent != null && !stillCurrent())
                return ThumbnailResult.CancelledResult();
            string resolved = ResolveInput(sourcePath, writableRoot);
            if (!File.Exists(resolved))
                return ThumbnailResult.Failed("missing " + sourcePath);
            Directory.CreateDirectory(cacheDir);
            string finalPath = ThumbnailFile(cacheDir, resolved, width, height);
            if (IsReady(finalPath) && BmpMatches(finalPath, width, height))
                return ThumbnailResult.Cached(finalPath);
            if (File.Exists(finalPath))
                TryDelete(finalPath);
            if (!CanRun(ffmpeg))
                return ThumbnailResult.Failed("ffmpeg missing");
            string partial = Path.Combine(
                Path.GetDirectoryName(finalPath) ?? cacheDir,
                Path.GetFileNameWithoutExtension(finalPath) + "." + Guid.NewGuid().ToString("N") + ".partial.bmp");
            try
            {
                if (stillCurrent != null && !stillCurrent())
                    return ThumbnailResult.CancelledResult();
                bool seek = kind != AudioKind && kind != ImageKind;
                int code = Run(ffmpeg, ThumbnailArguments(resolved, partial, width, height, kind, seek), stillCurrent, out string err);
                if (stillCurrent != null && !stillCurrent())
                    return ThumbnailResult.CancelledResult();
                if (!IsReady(partial) && seek)
                {
                    TryDelete(partial);
                    code = Run(ffmpeg, ThumbnailArguments(resolved, partial, width, height, kind, false), stillCurrent, out err);
                    if (stillCurrent != null && !stillCurrent())
                        return ThumbnailResult.CancelledResult();
                }
                if (!IsReady(partial))
                    return ThumbnailResult.Failed(string.IsNullOrWhiteSpace(err) ? "ffmpeg exit " + code.ToString(CultureInfo.InvariantCulture) : err.Trim());
                Publish(partial, finalPath);
                if (!IsReady(finalPath))
                    return ThumbnailResult.Failed("could not publish thumbnail");
                return ThumbnailResult.Created(finalPath);
            }
            catch (Exception ex)
            {
                return ThumbnailResult.Failed(ex.Message);
            }
            finally
            {
                TryDelete(partial);
            }
        }

        /// <summary>
        /// Queues thumbnail work and returns without waiting for ffmpeg.
        /// </summary>
        public static ThumbnailBatch BeginThumbnails(string ffmpeg, IReadOnlyList<string> sources, string cacheDir, int width, int height, string kind, string? writableRoot = null)
        {
            ThumbnailBatch batch = new ThumbnailBatch(sources.Count);
            foreach (string source in sources)
            {
                string captured = source;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    ThumbnailResult result;
                    try
                    {
                        result = EnsureThumbnail(ffmpeg, captured, cacheDir, width, height, kind, writableRoot, null);
                    }
                    catch (Exception ex)
                    {
                        result = ThumbnailResult.Failed(ex.Message);
                    }
                    batch.Complete(result);
                });
            }
            return batch;
        }

        public static bool IsInsideDirectory(string path, string directory)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetFullPath(directory);
                if (!root.EndsWith(Path.DirectorySeparatorChar))
                    root += Path.DirectorySeparatorChar;
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string ResolveInput(string path, string? writableRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path ?? "";
            try
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
                if (!string.IsNullOrEmpty(writableRoot) && !Path.IsPathRooted(path))
                {
                    string combined = Path.Combine(writableRoot, path);
                    if (File.Exists(combined))
                        return Path.GetFullPath(combined);
                }
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        static void Publish(string partial, string finalPath)
        {
            lock (PublishGate)
            {
                if (IsReady(finalPath))
                    return;
                string? destDir = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                File.Move(partial, finalPath);
            }
        }

        public static bool BmpMatches(string path, int width, int height)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                byte[] header = new byte[26];
                if (stream.Read(header, 0, header.Length) < header.Length)
                    return false;
                if (header[0] != (byte)'B' || header[1] != (byte)'M')
                    return false;
                int w = BitConverter.ToInt32(header, 18);
                int h = BitConverter.ToInt32(header, 22);
                return w == width && Math.Abs(h) == height;
            }
            catch
            {
                return false;
            }
        }

        static bool IsReady(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        static bool CanRun(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;
            if (File.Exists(fileName))
                return true;
            return fileName.IndexOf('/') < 0 && fileName.IndexOf('\\') < 0;
        }

        static string Q(string path)
        {
            return "\"" + (path ?? "").Replace("\"", "", StringComparison.Ordinal) + "\"";
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        static int Run(string fileName, string arguments, Func<bool>? stillCurrent, out string stderr)
        {
            StringBuilder tail = new StringBuilder();
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using Process process = new Process();
            process.StartInfo = psi;
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;
                lock (tail)
                {
                    if (tail.Length < 2000)
                        tail.AppendLine(e.Data);
                }
            };
            process.OutputDataReceived += (_, _) => { };
            ProcessTree.StartTracked(process);
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            while (!process.HasExited)
            {
                if (stillCurrent != null && !stillCurrent())
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                    break;
                }
                process.WaitForExit(100);
            }
            try
            {
                process.WaitForExit();
            }
            catch
            {
            }
            lock (tail)
                stderr = tail.ToString();
            try
            {
                return process.HasExited ? process.ExitCode : -1;
            }
            catch
            {
                return -1;
            }
        }

        static string RunCapture(string fileName, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using Process process = new Process();
            process.StartInfo = psi;
            process.ErrorDataReceived += (_, _) => { };
            ProcessTree.StartTracked(process);
            process.BeginErrorReadLine();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return stdout ?? "";
        }
    }

    public sealed class ThumbnailBatch
    {
        readonly int expected;
        readonly List<MediaPreview.ThumbnailResult> results = new();
        readonly object gate = new();
        int finished;
        readonly ManualResetEventSlim done = new(false);

        public ThumbnailBatch(int expected)
        {
            this.expected = expected;
            if (expected <= 0)
                done.Set();
        }

        public void Complete(MediaPreview.ThumbnailResult result)
        {
            lock (gate)
            {
                results.Add(result);
                finished++;
                if (finished >= expected)
                    done.Set();
            }
        }

        public bool Wait(TimeSpan timeout)
        {
            return done.Wait(timeout);
        }

        public IReadOnlyList<MediaPreview.ThumbnailResult> Results
        {
            get
            {
                lock (gate)
                    return results.ToArray();
            }
        }
    }
}
