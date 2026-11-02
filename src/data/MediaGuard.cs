using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Rejects missing, empty, and unprobeable media before VideoHelper.LoadFromFile.
    /// The vendored player double-frees the AVFormatContext when avformat_open_input
    /// fails. FFmpeg 7 frees that context and nulls the local pointer, then
    /// DecodeContext.Dispose calls avformat_free_context on the stale field and
    /// SIGSEGVs in av_opt_free. A rejected file must not be passed to the native decoder.
    /// The file itself is not deleted.
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

        public static Verdict Inspect(string? path, string? ffprobePath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Verdict.Reject(MissingCode, "Cannot play media: the file is missing.");
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                return Verdict.Reject(MissingCode, "Cannot play media: the path is not usable (" + ex.Message + ").");
            }
            if (!File.Exists(full))
                return Verdict.Reject(MissingCode, "Cannot play media: file is missing.");
            long length;
            try
            {
                length = new FileInfo(full).Length;
            }
            catch (Exception ex)
            {
                return Verdict.Reject(UnprobeableCode, "Cannot play media: the file could not be read (" + ex.Message + ").");
            }
            if (length <= 0)
                return Verdict.Reject(EmptyCode, "Cannot play media: the file is empty.");
            if (string.IsNullOrWhiteSpace(ffprobePath) || !CanRun(ffprobePath))
                return Verdict.Reject(UnprobeableCode, "Cannot play media: ffprobe is not available, so the file was not opened.");
            if (!Probe(ffprobePath, full, out string detail))
            {
                string suffix = string.IsNullOrWhiteSpace(detail) ? "" : " " + detail;
                return Verdict.Reject(UnprobeableCode, "Cannot play media: the file is damaged or not a readable clip." + suffix);
            }
            return Verdict.Accept();
        }

        static bool CanRun(string fileName)
        {
            if (File.Exists(fileName))
                return true;
            return fileName.IndexOf('/') < 0 && fileName.IndexOf('\\') < 0;
        }

        static bool Probe(string ffprobePath, string fullPath, out string detail)
        {
            detail = "";
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("format=format_name");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("csv=p=0");
            psi.ArgumentList.Add(fullPath);
            try
            {
                using Process process = new Process();
                process.StartInfo = psi;
                var error = new StringBuilder();
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null || error.Length > 300)
                        return;
                    error.AppendLine(e.Data);
                };
                if (!process.Start())
                {
                    detail = "ffprobe did not start.";
                    return false;
                }
                process.BeginErrorReadLine();
                string stdout = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(5000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                    detail = "ffprobe timed out.";
                    return false;
                }
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    detail = FirstLine(error.ToString());
                    return false;
                }
                string format = FirstLine(stdout);
                if (format.Length == 0 || format.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                {
                    detail = "no media format.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
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
