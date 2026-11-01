using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Tracks processes this job started and kills only those, with
    /// <see cref="Process.Kill(bool)"/> <c>entireProcessTree: true</c>.
    /// An ffmpeg that this job did not start is left alone.
    /// </summary>
    public static class ProcessTree
    {
        static readonly object Gate = new();
        static readonly List<Process> Owned = new();

        /// <summary>
        /// Bound for a vocoder launch. A no-argument vocoder used to spin on
        /// EOF. ffmpeg, ffprobe, magick, and yt-dlp are not bounded; cancel
        /// still goes through <see cref="KillOwned"/>.
        /// </summary>
        public static TimeSpan VocoderTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Vocoder gets <see cref="VocoderTimeout"/>. Every other tool waits
        /// until it exits. <paramref name="fileName"/> may be a path.
        /// </summary>
        public static TimeSpan? TimeoutFor(string? fileName)
        {
            string name = fileName ?? "";
            int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (slash >= 0 && slash < name.Length - 1)
                name = name[(slash + 1)..];
            if (name.Equals("vocoder", StringComparison.OrdinalIgnoreCase)
                || name.Equals("vocoder.exe", StringComparison.OrdinalIgnoreCase))
                return VocoderTimeout;
            return null;
        }

        public static void Track(Process process)
        {
            lock (Gate)
            {
                Owned.RemoveAll(Exited);
                Owned.Add(process);
            }
        }

        public static void StartTracked(Process process)
        {
            process.Start();
            Track(process);
        }

        public static void KillOwned()
        {
            Process[] snapshot;
            lock (Gate)
            {
                snapshot = Owned.ToArray();
                Owned.Clear();
            }
            foreach (Process process in snapshot)
            {
                try
                {
                    if (!Exited(process))
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not kill owned process: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Returns true when the process exits on its own. A null timeout
        /// waits without a budget. A timeout kills that process tree and
        /// returns false.
        /// </summary>
        public static bool WaitForExit(Process process, TimeSpan? timeout = null)
        {
            if (timeout is null)
            {
                process.WaitForExit();
                return true;
            }
            bool exited = process.WaitForExit(timeout.Value);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Command timed out and could not be killed: " + ex.Message);
                }
            }
            // The parameterless wait drains asynchronous stdout and stderr.
            try
            {
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Wait after command exit failed: " + ex.Message);
            }
            return exited;
        }

        static bool Exited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        }
    }
}
