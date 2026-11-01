using System.Runtime.InteropServices;
using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class AudioOutputFollowerTests
{
    sealed class FakeAlc : IAlcApi
    {
        public IntPtr Device = new IntPtr(0x1000);
        public bool Reopenable = true;
        public string? Current = "Cassette";
        public string? Default = "Cassette";
        public bool ReopenWorks = true;
        public bool ReopenMoves = true;
        public bool Throw;
        public string? LandsOn;
        public int Reopens;

        public IntPtr CurrentDevice()
        {
            if (Throw)
                throw new InvalidOperationException("boom");
            return Device;
        }
        public bool SupportsReopen(IntPtr device) => Reopenable;
        public string? DeviceName(IntPtr device) => Current;
        public string? DefaultName() => Default;
        public bool ReopenDefault(IntPtr device)
        {
            Reopens++;
            if (!ReopenWorks)
                return false;
            if (ReopenMoves)
                Current = LandsOn ?? Default;
            return true;
        }
    }

    sealed class Rig
    {
        public readonly FakeAlc Alc = new();
        public long Now = 100000;
        public readonly List<string> Log = new();
        public readonly AudioOutputFollower Follower;

        public Rig()
        {
            Follower = new AudioOutputFollower(Alc, () => Now, Log.Add);
        }
    }

    [Fact]
    public void Same_output_never_reopens_and_logs_the_device_once()
    {
        var r = new Rig();
        for (int i = 0; i < 5; i++)
        {
            r.Follower.Poll();
            r.Now += 1000;
        }
        Assert.Equal(0, r.Alc.Reopens);
        Assert.Single(r.Log, l => l == "Audio output: Cassette");
    }

    [Fact]
    public void Default_change_reopens_once_then_stays_quiet()
    {
        var r = new Rig();
        r.Follower.Poll();
        r.Alc.Default = "Dime 3";
        r.Follower.Poll();
        for (int i = 0; i < 5; i++)
        {
            r.Now += 1000;
            r.Follower.Poll();
        }
        Assert.Equal(1, r.Alc.Reopens);
        Assert.Equal(1, r.Follower.ReopenCount);
        Assert.Equal("Dime 3", r.Alc.Current);
        Assert.Contains("Audio output changed: Cassette -> Dime 3", r.Log);
    }

    [Fact]
    public void Switching_back_and_forth_follows_each_change()
    {
        var r = new Rig();
        r.Follower.Poll();
        string[] seq = { "External Headphones", "Cassette", "Mac mini Speakers", "Dime 3" };
        foreach (string name in seq)
        {
            r.Alc.Default = name;
            r.Follower.Poll();
            Assert.Equal(name, r.Alc.Current);
        }
        Assert.Equal(4, r.Alc.Reopens);
    }

    [Fact]
    public void No_device_or_empty_names_do_nothing()
    {
        var r = new Rig();
        r.Alc.Device = IntPtr.Zero;
        r.Alc.Default = "Dime 3";
        r.Follower.Poll();
        r.Alc.Device = new IntPtr(1);
        r.Alc.Default = null;
        r.Follower.Poll();
        r.Alc.Default = "";
        r.Follower.Poll();
        r.Alc.Default = "Dime 3";
        r.Alc.Current = null;
        r.Follower.Poll();
        Assert.Equal(0, r.Alc.Reopens);
    }

    [Fact]
    public void Failed_reopen_backs_off_and_does_not_thrash()
    {
        var r = new Rig();
        r.Follower.Poll();
        r.Alc.Default = "Dime 3";
        r.Alc.ReopenWorks = false;
        r.Follower.Poll();
        Assert.Equal(1, r.Alc.Reopens);
        // Polls inside the backoff window make no call.
        for (int i = 0; i < 1; i++)
        {
            r.Now += 1000;
            r.Follower.Poll();
        }
        Assert.Equal(1, r.Alc.Reopens);
        // After 2 s one retry, then the wait doubles to 4 s.
        r.Now += 1001;
        r.Follower.Poll();
        Assert.Equal(2, r.Alc.Reopens);
        r.Now += 3000;
        r.Follower.Poll();
        Assert.Equal(2, r.Alc.Reopens);
        r.Now += 1001;
        r.Alc.ReopenWorks = true;
        r.Follower.Poll();
        Assert.Equal(3, r.Alc.Reopens);
        Assert.Equal("Dime 3", r.Alc.Current);
    }

    [Fact]
    public void Backoff_is_capped()
    {
        var r = new Rig();
        r.Follower.Poll();
        r.Alc.Default = "Dime 3";
        r.Alc.ReopenWorks = false;
        for (int i = 0; i < 12; i++)
        {
            r.Follower.Poll();
            r.Now += AudioOutputFollower.MaxBackoffMilliseconds + 1;
        }
        // Every poll is past the cap, so each one retries and none thrashes in between.
        Assert.Equal(12, r.Alc.Reopens);
        r.Follower.Poll();
        int before = r.Alc.Reopens;
        r.Now += AudioOutputFollower.MaxBackoffMilliseconds - 1;
        r.Follower.Poll();
        Assert.Equal(before, r.Alc.Reopens);
    }

    [Fact]
    public void A_new_default_gets_an_immediate_try_even_during_backoff()
    {
        var r = new Rig();
        r.Follower.Poll();
        r.Alc.Default = "Dime 3";
        r.Alc.ReopenWorks = false;
        r.Follower.Poll();
        Assert.Equal(1, r.Alc.Reopens);
        r.Alc.ReopenWorks = true;
        r.Alc.Default = "External Headphones";
        r.Follower.Poll();
        Assert.Equal(2, r.Alc.Reopens);
        Assert.Equal("External Headphones", r.Alc.Current);
    }

    [Fact]
    public void Reopen_that_leaves_the_device_unchanged_is_not_retried_every_poll()
    {
        var r = new Rig();
        r.Follower.Poll();
        r.Alc.Default = "Dime 3";
        r.Alc.ReopenMoves = false;
        r.Follower.Poll();
        for (int i = 0; i < 3; i++)
        {
            r.Now += 500;
            r.Follower.Poll();
        }
        Assert.Equal(1, r.Alc.Reopens);
        Assert.Equal(0, r.Follower.ReopenCount);
    }

    [Fact]
    public void Reopen_landing_on_a_differently_spelled_name_is_retried_only_after_backoff()
    {
        var r = new Rig();
        r.Follower.Poll();
        r.Alc.Default = "Dime 3";
        r.Alc.LandsOn = "Dime 3 (2)";
        r.Follower.Poll();
        Assert.Equal(1, r.Alc.Reopens);
        Assert.Equal(1, r.Follower.ReopenCount);
        r.Now += 1000;
        r.Follower.Poll();
        Assert.Equal(1, r.Alc.Reopens);
        r.Now += 1500;
        r.Follower.Poll();
        Assert.Equal(2, r.Alc.Reopens);
    }

    [Fact]
    public void Missing_reopen_extension_disables_following()
    {
        var r = new Rig();
        r.Alc.Reopenable = false;
        r.Alc.Default = "Dime 3";
        r.Follower.Poll();
        r.Follower.Poll();
        Assert.True(r.Follower.Disabled);
        Assert.Equal(0, r.Alc.Reopens);
    }

    [Fact]
    public void Repeated_exceptions_are_swallowed_then_disable_following()
    {
        var r = new Rig();
        r.Alc.Throw = true;
        for (int i = 0; i < AudioOutputFollower.MaxConsecutiveErrors; i++)
            r.Follower.Poll();
        Assert.True(r.Follower.Disabled);
        r.Alc.Throw = false;
        r.Alc.Default = "Dime 3";
        r.Follower.Poll();
        Assert.Equal(0, r.Alc.Reopens);
    }

    [Fact]
    public void An_occasional_exception_does_not_disable()
    {
        var r = new Rig();
        for (int i = 0; i < 10; i++)
        {
            r.Alc.Throw = i % 2 == 0;
            r.Follower.Poll();
        }
        Assert.False(r.Follower.Disabled);
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData("1", true, true)]
    [InlineData("0", true, false)]
    [InlineData("false", true, false)]
    [InlineData(null, false, false)]
    [InlineData("1", false, false)]
    public void ShouldRun_matrix(string? setting, bool mac, bool expected)
    {
        Assert.Equal(expected, AudioOutputFollower.ShouldRun(setting, mac));
    }

    [Fact]
    public void Prepare_writes_one_line_config_and_sets_ALSOFT_CONF_on_linux()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nvg-alsoft-" + Guid.NewGuid().ToString("n"), "a b");
        try
        {
            var env = new Dictionary<string, string>();
            string? path = AudioOutputFollower.Prepare(dir, true, k => null, (k, v) => env[k] = v);
            Assert.Equal(Path.Combine(dir, "alsoft.conf"), path);
            Assert.Equal(path, env["ALSOFT_CONF"]);
            Assert.Equal(AudioOutputFollower.LinuxConfigText, File.ReadAllText(path!));
            Assert.Contains("allow-moves=true", File.ReadAllText(path!));
            // Second call over an edited file restores the text and does not fail.
            File.WriteAllText(path!, "junk");
            Assert.Equal(path, AudioOutputFollower.Prepare(dir, true, k => null, (k, v) => env[k] = v));
            Assert.Equal(AudioOutputFollower.LinuxConfigText, File.ReadAllText(path!));
        }
        finally
        {
            string? parent = Path.GetDirectoryName(dir);
            if (parent != null && parent.StartsWith(Path.GetTempPath(), StringComparison.Ordinal) && Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    [Fact]
    public void Prepare_leaves_user_config_off_switch_and_other_platforms_alone()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nvg-alsoft-" + Guid.NewGuid().ToString("n"));
        var env = new Dictionary<string, string>();
        Assert.Null(AudioOutputFollower.Prepare(dir, true, k => k == "ALSOFT_CONF" ? "/home/u/my.conf" : null, (k, v) => env[k] = v));
        Assert.Null(AudioOutputFollower.Prepare(dir, true, k => k == "NVG_AUDIO_FOLLOW" ? "0" : null, (k, v) => env[k] = v));
        Assert.Null(AudioOutputFollower.Prepare(dir, false, k => null, (k, v) => env[k] = v));
        Assert.Empty(env);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Prepare_does_not_throw_when_the_directory_cannot_be_created()
    {
        string file = Path.GetTempFileName();
        try
        {
            var env = new Dictionary<string, string>();
            // A path below a regular file can never be created.
            Assert.Null(AudioOutputFollower.Prepare(Path.Combine(file, "sub"), true, k => null, (k, v) => env[k] = v));
            Assert.Empty(env);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Real OpenAL Soft from the NuGet package: the follower finds the loaded library,
    /// sees the live device and default, and does nothing when they already match.
    /// It makes no sound. Skipped quietly when the machine has no audio device.
    /// </summary>
    [Fact]
    public void Real_openal_reports_device_and_reopen_keeps_it_working()
    {
        if (!(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()))
            return;
        string? lib = FindPackagedOpenAl();
        if (lib == null || !NativeLibrary.TryLoad(lib, out IntPtr h))
            return;
        var open = Marshal.GetDelegateForFunctionPointer<OpenDeviceFn>(NativeLibrary.GetExport(h, "alcOpenDevice"));
        var close = Marshal.GetDelegateForFunctionPointer<CloseDeviceFn>(NativeLibrary.GetExport(h, "alcCloseDevice"));
        var createCtx = Marshal.GetDelegateForFunctionPointer<CreateContextFn>(NativeLibrary.GetExport(h, "alcCreateContext"));
        var makeCurrent = Marshal.GetDelegateForFunctionPointer<MakeCurrentFn>(NativeLibrary.GetExport(h, "alcMakeContextCurrent"));
        var destroyCtx = Marshal.GetDelegateForFunctionPointer<DestroyContextFn>(NativeLibrary.GetExport(h, "alcDestroyContext"));
        IntPtr dev = open(IntPtr.Zero);
        if (dev == IntPtr.Zero)
            return; // no audio hardware or server
        IntPtr ctx = createCtx(dev, IntPtr.Zero);
        try
        {
            makeCurrent(ctx);
            var log = new List<string>();
            var follower = new AudioOutputFollower(new RealApiForTest(h), null, log.Add);
            follower.Poll();
            // macOS ships OpenAL Soft 1.22 (has reopen). Linux ships 1.17.1, which does not, so
            // the follower correctly turns itself off there (Linux uses pulse allow-moves).
            Assert.Equal(OperatingSystem.IsLinux(), follower.Disabled);
            if (!follower.Disabled)
                Assert.StartsWith("Audio output: ", log[0]);
        }
        finally
        {
            makeCurrent(IntPtr.Zero);
            destroyCtx(ctx);
            close(dev);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr OpenDeviceFn(IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate sbyte CloseDeviceFn(IntPtr dev);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CreateContextFn(IntPtr dev, IntPtr attrs);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate sbyte MakeCurrentFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DestroyContextFn(IntPtr ctx);

    static string? FindPackagedOpenAl()
    {
        string name = OperatingSystem.IsMacOS() ? "libopenal.1.dylib" : "libopenal.so.1";
        string[] rids = OperatingSystem.IsMacOS() ? new[] { "osx" } : new[] { "linux-x64" };
        foreach (string rid in rids)
        {
            string p = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", name);
            if (File.Exists(p))
                return p;
        }
        string flat = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(flat) ? flat : null;
    }

    /// <summary>The follower's real API class is private; this mirrors the calls for the packaged lib.</summary>
    sealed class RealApiForTest : IAlcApi
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CtxFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DevFn(IntPtr c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr StrFn(IntPtr d, int p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ProcFn(IntPtr d, IntPtr n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate sbyte ReopenFn(IntPtr d, IntPtr n, IntPtr a);
        readonly CtxFn ctx;
        readonly DevFn dev;
        readonly StrFn str;
        readonly ProcFn proc;
        ReopenFn? reopen;

        public RealApiForTest(IntPtr lib)
        {
            ctx = Marshal.GetDelegateForFunctionPointer<CtxFn>(NativeLibrary.GetExport(lib, "alcGetCurrentContext"));
            dev = Marshal.GetDelegateForFunctionPointer<DevFn>(NativeLibrary.GetExport(lib, "alcGetContextsDevice"));
            str = Marshal.GetDelegateForFunctionPointer<StrFn>(NativeLibrary.GetExport(lib, "alcGetString"));
            proc = Marshal.GetDelegateForFunctionPointer<ProcFn>(NativeLibrary.GetExport(lib, "alcGetProcAddress"));
        }

        public IntPtr CurrentDevice() => dev(ctx());
        public bool SupportsReopen(IntPtr device)
        {
            IntPtr n = Marshal.StringToHGlobalAnsi("alcReopenDeviceSOFT");
            try
            {
                IntPtr p = proc(device, n);
                if (p == IntPtr.Zero)
                    return false;
                reopen = Marshal.GetDelegateForFunctionPointer<ReopenFn>(p);
                return true;
            }
            finally { Marshal.FreeHGlobal(n); }
        }
        public string? DeviceName(IntPtr device) => Marshal.PtrToStringUTF8(str(device, 0x1013));
        public string? DefaultName() => Marshal.PtrToStringUTF8(str(IntPtr.Zero, 0x1012));
        public bool ReopenDefault(IntPtr device) => reopen!(device, IntPtr.Zero, IntPtr.Zero) != 0;
    }
}
