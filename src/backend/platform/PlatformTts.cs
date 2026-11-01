#if DESKTOPGL
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator
{
    public enum SynthesizerState
    {
        Ready,
        Speaking,
    }

    internal interface ISpeechJob
    {
        void Kill();
        void Wait();
    }

    /// <summary>
    /// Speaks with say on macOS, or spd-say then espeak-ng on Linux.
    /// Speech is queued. SpeakAsyncCancelAll drops the queue and stops the current utterance.
    /// A missing voice service is reported once and does not throw.
    /// </summary>
    public sealed class PlatformTts
    {
        readonly object gate = new();
        readonly Queue<string> queue = new();
        ISpeechJob? current;
        string? helper;
        bool started;
        bool announced;
        int ticket;

        public SynthesizerState State { get; private set; } = SynthesizerState.Ready;

        public string? LastNotice { get; private set; }

        internal static Func<string, bool>? HelperExists = null;

        internal static Func<string, string[], ISpeechJob?>? StartJob = null;

        public void SetOutputToDefaultAudioDevice()
        {
        }

        public void SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            if (!EnsureHelper())
            {
                Announce();
                State = SynthesizerState.Ready;
                return;
            }
            lock (gate)
            {
                queue.Enqueue(text);
                State = SynthesizerState.Speaking;
                if (!started)
                {
                    started = true;
                    var thread = new Thread(Pump)
                    {
                        IsBackground = true,
                        Name = "nvg-tts",
                    };
                    thread.Start();
                }
                Monitor.Pulse(gate);
            }
        }

        public void SpeakAsyncCancelAll()
        {
            ISpeechJob? job;
            lock (gate)
            {
                ticket++;
                queue.Clear();
                job = current;
                State = SynthesizerState.Ready;
                Monitor.PulseAll(gate);
            }
            job?.Kill();
            if (StartJob == null && helper == "spd-say" && PlatformFileDialog.CommandOnPath("spd-say"))
            {
                try
                {
                    var psi = new ProcessStartInfo("spd-say")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    psi.ArgumentList.Add("-C");
                    using Process? process = Process.Start(psi);
                    process?.WaitForExit(2000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("spd-say cancel failed: " + ex.Message);
                }
            }
        }

        bool EnsureHelper()
        {
            if (helper != null)
                return true;
            Func<string, bool> probe = HelperExists ?? PlatformFileDialog.CommandOnPath;
            if (OperatingSystem.IsMacOS())
            {
                if (probe("say"))
                    helper = "say";
            }
            else if (OperatingSystem.IsLinux())
            {
                if (probe("spd-say"))
                    helper = "spd-say";
                else if (probe("espeak-ng"))
                    helper = "espeak-ng";
            }
            return helper != null;
        }

        void Announce()
        {
            if (announced)
                return;
            announced = true;
            LastNotice = "Text to speech is unavailable (say, spd-say, or espeak-ng was not found).";
            try
            {
                ConsoleOutput.WriteLine(LastNotice, Color.Yellow);
            }
            catch
            {
                Console.WriteLine(LastNotice);
            }
        }

        void Pump()
        {
            while (true)
            {
                string text;
                string bin;
                int seen;
                lock (gate)
                {
                    while (queue.Count == 0)
                        Monitor.Wait(gate);
                    text = queue.Dequeue();
                    seen = ticket;
                    bin = helper ?? "";
                    State = SynthesizerState.Speaking;
                }
                ISpeechJob? job = Launch(bin, text);
                lock (gate)
                {
                    if (seen != ticket)
                    {
                        job?.Kill();
                        if (queue.Count == 0)
                            State = SynthesizerState.Ready;
                        continue;
                    }
                    current = job;
                }
                try
                {
                    job?.Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("TTS wait failed: " + ex.Message);
                }
                lock (gate)
                {
                    if (current == job)
                        current = null;
                    if (queue.Count == 0)
                        State = SynthesizerState.Ready;
                }
            }
        }

        static ISpeechJob? Launch(string bin, string text)
        {
            if (bin.Length == 0)
                return null;
            string[] args = bin == "spd-say"
                ? new[] { "-w", "--", text }
                : new[] { "--", text };
            if (StartJob != null)
                return StartJob(bin, args);
            try
            {
                var psi = new ProcessStartInfo(bin)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (string arg in args)
                    psi.ArgumentList.Add(arg);
                Process? process = Process.Start(psi);
                if (process == null)
                    return null;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return new ProcessSpeechJob(process);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TTS start failed: " + ex.Message);
                return null;
            }
        }

        sealed class ProcessSpeechJob : ISpeechJob
        {
            readonly Process process;

            public ProcessSpeechJob(Process process)
            {
                this.process = process;
            }

            public void Kill()
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }

            public void Wait()
            {
                try
                {
                    process.WaitForExit();
                }
                catch
                {
                }
            }
        }
    }
}
#endif
