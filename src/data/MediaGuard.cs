using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Rejects missing, empty, and unprobeable media before VideoHelper.LoadFromFile.
    /// The vendored player used to double-free the AVFormatContext when
    /// avformat_open_input failed. That free is patched, so a failed open is a
    /// managed FFmpegException. The guard stays so a bad file is turned away with
    /// a message instead of that exception. The file itself is not deleted.
    /// A file the probe accepts can still fail in the decoder; VideoLoad.TryLoad
    /// catches that and leaves the file in place.
    /// </summary>
    public static class MediaGuard
    {
        public const string MissingCode = "missing";
        public const string EmptyCode = "empty";
        public const string UnprobeableCode = "unprobeable";

        public readonly struct Verdict
        {
            public bool Ok { get; init; }
            public string Code { get; init; }
            public string Message { get; init; }

            public static Verdict Accept()
            {
                return new Verdict { Ok = true, Code = "", Message = "" };
            }

            public static Verdict Reject(string code, string message)
            {
                return new Verdict { Ok = false, Code = code, Message = message };
            }
        }

        public static bool CanOpen(string? path, string? ffprobePath, out string message)
        {
            Verdict verdict = Inspect(path, ffprobePath);
            message = verdict.Message;
            return verdict.Ok;
        }

        /// <summary>
        /// Exists and is non-empty. Does not spawn ffprobe.
        /// </summary>
        public static bool HasContent(string? path, out string message)
        {
            if (!TryDescribeFile(path, out _, out Verdict reject))
            {
                message = reject.Message;
                return false;
            }
            message = "";
            return true;
        }

        public static Verdict Inspect(string? path, string? ffprobePath)
        {
            if (!TryDescribeFile(path, out string full, out Verdict reject))
                return reject;
            if (string.IsNullOrWhiteSpace(ffprobePath) || !CanRun(ffprobePath))
                return Verdict.Reject(UnprobeableCode, "Cannot play media: ffprobe is not available, so the file was not opened.");
            if (!Probe(ffprobePath, full, out string detail))
            {
                string suffix = string.IsNullOrWhiteSpace(detail) ? "" : " " + detail;
                return Verdict.Reject(UnprobeableCode, "Cannot play media: the file is damaged or not a readable clip." + suffix);
            }
            return Verdict.Accept();
        }

        static bool TryDescribeFile(string? path, out string full, out Verdict reject)
        {
            full = "";
            reject = default;
            if (string.IsNullOrWhiteSpace(path))
            {
                reject = Verdict.Reject(MissingCode, "Cannot play media: the file is missing.");
                return false;
            }
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                reject = Verdict.Reject(MissingCode, "Cannot play media: the path is not usable (" + ex.Message + ").");
                return false;
            }
            if (!File.Exists(full))
            {
                reject = Verdict.Reject(MissingCode, "Cannot play media: file is missing.");
                return false;
            }
            long length;
            try
            {
                length = new FileInfo(full).Length;
            }
            catch (Exception ex)
            {
                reject = Verdict.Reject(UnprobeableCode, "Cannot play media: the file could not be read (" + ex.Message + ").");
                return false;
            }
            if (length <= 0)
            {
                reject = Verdict.Reject(EmptyCode, "Cannot play media: the file is empty.");
                return false;
            }
            return true;
        }

        static bool CanRun(string fileName)
        {
            if (File.Exists(fileName))
                return true;
            return fileName.IndexOf('/') < 0 && fileName.IndexOf('\\') < 0;
        }

        static bool Probe(string ffprobePath, string fullPath, out string detail)
        {
            if (!TryReadProbe(ffprobePath, "format=format_name", fullPath, out string stdout, out detail))
                return false;
            string format = FirstLine(stdout);
            if (format.Length == 0 || format.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            {
                detail = "no media format.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Runs ffprobe and returns when it exits or when 5 seconds have passed.
        /// Stdout is read on a background thread. Reading it first would wait until
        /// the process closes the pipe, so a hang would never reach the timeout.
        /// </summary>
        internal static bool TryReadProbe(string ffprobePath, string showEntries, string mediaPath, out string stdout, out string detail)
        {
            stdout = "";
            detail = "";
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
#if DESKTOPGL
            // Other DesktopGL tool launches close stdin so a reader sees EOF
            // instead of inheriting the app's pipe.
            psi.RedirectStandardInput = true;
#endif
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add(showEntries);
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("csv=p=0");
            psi.ArgumentList.Add(mediaPath);
            Process? process = null;
            var output = new StringBuilder();
            var error = new StringBuilder();
            try
            {
                process = new Process();
                process.StartInfo = psi;
                process.OutputDataReceived += (_, e) => AppendCapped(output, e.Data);
                process.ErrorDataReceived += (_, e) => AppendCapped(error, e.Data);
                if (!process.Start())
                {
                    detail = "ffprobe did not start.";
                    return false;
                }
#if DESKTOPGL
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                }
#endif
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(5000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                    try
                    {
                        process.WaitForExit(2000);
                    }
                    catch
                    {
                    }
                    detail = "ffprobe timed out.";
                    return false;
                }
                // The parameterless wait finishes the async readers.
                process.WaitForExit();
                stdout = ReadCapped(output);
                if (process.ExitCode != 0)
                {
                    detail = FirstLine(ReadCapped(error));
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
            finally
            {
                if (process != null)
                {
                    try
                    {
                        process.CancelOutputRead();
                    }
                    catch
                    {
                    }
                    try
                    {
                        process.CancelErrorRead();
                    }
                    catch
                    {
                    }
                    process.Dispose();
                }
            }
        }

        static void AppendCapped(StringBuilder buffer, string? line)
        {
            if (line == null)
                return;
            lock (buffer)
            {
                if (buffer.Length > 4000)
                    return;
                buffer.AppendLine(line);
            }
        }

        static string ReadCapped(StringBuilder buffer)
        {
            lock (buffer)
                return buffer.ToString();
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            string line = text.Trim();
            int cut = line.IndexOfAny(new[] { '\r', '\n' });
            if (cut >= 0)
                line = line.Substring(0, cut).Trim();
            if (line.Length > 180)
                line = line.Substring(0, 180);
            return line;
        }
    }
}
