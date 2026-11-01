using System;
using System.Collections.Generic;
using System.Threading;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Runs texture uploads on the MonoGame update thread.
    /// DesktopGL's OpenGL context is not usable from the thumbnail worker.
    /// </summary>
    public static class GraphicsThread
    {
        static readonly object Gate = new();
        static readonly Queue<Action> Pending = new();

        public static void Invoke(Action action)
        {
            Exception? error = null;
            using var done = new ManualResetEventSlim(false);
            lock (Gate)
            {
                Pending.Enqueue(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
            }
            if (!done.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Graphics thread did not run the queued upload.");
            if (error != null)
                throw error;
        }

        public static void Pump()
        {
            while (true)
            {
                Action? next;
                lock (Gate)
                {
                    if (Pending.Count == 0)
                        return;
                    next = Pending.Dequeue();
                }
                next();
            }
        }
    }
}
