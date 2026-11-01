using System.Runtime.InteropServices;
using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class ShutdownSignalsTests
{
    [Fact]
    public void First_signal_cancels_termination_and_requests_exit_once()
    {
        int calls = 0;
        int once = 0;
        var term = new PosixSignalContext(PosixSignal.SIGTERM);
        var intr = new PosixSignalContext(PosixSignal.SIGINT);

        Assert.True(ShutdownSignals.Handle(term, ref once, () => calls++));
        Assert.True(term.Cancel);
        Assert.False(ShutdownSignals.Handle(intr, ref once, () => calls++));
        Assert.True(intr.Cancel);
        Assert.Equal(1, calls);
        Assert.Equal(1, once);
    }

    [Fact]
    public void Thrown_exit_does_not_escape_and_a_second_signal_retries()
    {
        int calls = 0;
        int once = 0;
        var first = new PosixSignalContext(PosixSignal.SIGTERM);
        var second = new PosixSignalContext(PosixSignal.SIGTERM);

        bool threw = false;
        try
        {
            bool ran = ShutdownSignals.Handle(first, ref once, () =>
            {
                calls++;
                throw new InvalidOperationException("exit failed");
            });
            Assert.False(ran);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.False(threw);
        Assert.True(first.Cancel);
        Assert.Equal(0, once);
        Assert.True(ShutdownSignals.Handle(second, ref once, () => calls++));
        Assert.True(second.Cancel);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Sigterm_reaches_the_registration_and_does_not_kill_the_process()
    {
        int calls = 0;
        using var saw = new ManualResetEventSlim(false);
        ShutdownSignals.Register(() =>
        {
            Interlocked.Increment(ref calls);
            saw.Set();
        });
        try
        {
            Assert.Equal(0, Kill(Environment.ProcessId, SigTerm));
            Assert.True(saw.Wait(TimeSpan.FromSeconds(5)), "SIGTERM handler did not run");
            Assert.Equal(1, Volatile.Read(ref calls));

            Assert.Equal(0, Kill(Environment.ProcessId, SigTerm));
            var slept = Environment.TickCount64;
            Thread.Sleep(300);
            var woke = Environment.TickCount64 - slept;
            Assert.True(woke >= 250, "process did not stay up after the second SIGTERM (" + woke + "ms)");
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            ShutdownSignals.Unregister();
        }
    }

    const int SigTerm = 15;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    static extern int Kill(int pid, int sig);
}
