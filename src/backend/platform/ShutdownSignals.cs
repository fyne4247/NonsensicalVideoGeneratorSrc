#if DESKTOPGL
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// DesktopGL: the first SIGTERM or SIGINT requests a normal game exit and
    /// cancels the runtime's default termination. A later signal is not
    /// cancelled, so a hung game loop can still be stopped with SIGTERM or Ctrl+C.
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
        /// The first call cancels default termination and runs <paramref name="exit"/> once.
        /// A later call leaves <see cref="PosixSignalContext.Cancel"/> false, so the
        /// runtime's default termination runs, and does not call <paramref name="exit"/> again.
        /// If <paramref name="exit"/> throws, the one-shot gate reopens so a
        /// second signal can retry, and the exception does not escape.
        /// </summary>
        public static bool Handle(PosixSignalContext context, ref int onceGate, Action exit)
        {
            if (Interlocked.CompareExchange(ref onceGate, 1, 0) != 0)
            {
                context.Cancel = false;
                return false;
            }
            context.Cancel = true;
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
