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
        /// DesktopGL budget for one <c>Command.Call</c>. A no-argument vocoder
        /// used to spin on EOF; this kills that process and returns.
        /// Real encodes share the same budget. Tests pass a shorter span to
        /// <see cref="WaitForExit"/>.
        /// </summary>
        public static TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(30);

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
        /// Returns true when the process exits on its own. On timeout, kills
        /// that process tree and returns false.
        /// </summary>
        public static bool WaitForExit(Process process, TimeSpan? timeout = null)
        {
            TimeSpan budget = timeout ?? CommandTimeout;
            bool exited = process.WaitForExit(budget);
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
