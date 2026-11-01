#if DESKTOPGL
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// DesktopGL: SIGTERM and SIGINT request a normal game exit and cancel
    /// the runtime's default termination.
    /// CoreCLR's SIGTERM handler calls exit while MonoGame's OpenAL device is
    /// still open. OpenAL Soft's atexit cleanup then locks a std::recursive_mutex.
    /// Under Rosetta that lock returns EINVAL and libc++ aborts the process.
    /// Native arm64 and Linux finish the same forced exit without aborting.
    /// </summary>
    public static class ShutdownSignals
    {
        static readonly object Gate = new();
        static int once;
        static Action? requestExit;
        static PosixSignalRegistration? term;
        static PosixSignalRegistration? intr;

        public static void Register(Action exit)
        {
            if (exit == null)
                throw new ArgumentNullException(nameof(exit));
            lock (Gate)
            {
                requestExit = exit;
                term ??= PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
                intr ??= PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
            }
        }

        public static void Unregister()
        {
            lock (Gate)
            {
                term?.Dispose();
                intr?.Dispose();
                term = null;
                intr = null;
                requestExit = null;
                Interlocked.Exchange(ref once, 0);
            }
        }

        /// <summary>
        /// Cancel default termination. <paramref name="exit"/> runs once.
        /// A later signal stays canceled and does not call it again.
        /// If <paramref name="exit"/> throws, the one-shot gate reopens so a
        /// second signal can retry, and the exception does not escape.
        /// </summary>
        public static bool Handle(PosixSignalContext context, ref int onceGate, Action exit)
        {
            context.Cancel = true;
            if (Interlocked.CompareExchange(ref onceGate, 1, 0) != 0)
                return false;
            try
            {
                exit();
                return true;
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref onceGate, 0);
                Console.Error.WriteLine("NVG shutdown signal failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        static void OnSignal(PosixSignalContext context)
        {
            Action? exit;
            lock (Gate)
                exit = requestExit;
            if (exit == null)
            {
                context.Cancel = true;
                return;
            }
            Handle(context, ref once, exit);
        }
    }
}
#endif
