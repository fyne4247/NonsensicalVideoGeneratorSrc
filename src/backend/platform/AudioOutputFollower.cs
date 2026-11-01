using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// The few OpenAL calls the follower needs. Real OpenAL on macOS/Linux, a fake in tests.
    /// </summary>
    public interface IAlcApi
    {
        /// <summary>Device of the current OpenAL context, or <see cref="IntPtr.Zero"/> when audio is not up.</summary>
        IntPtr CurrentDevice();

        /// <summary>True when the library exports ALC_SOFT_reopen_device for this device.</summary>
        bool SupportsReopen(IntPtr device);

        /// <summary>Name of the output the device is playing on (ALC_ALL_DEVICES_SPECIFIER).</summary>
        string? DeviceName(IntPtr device);

        /// <summary>Name of the system default output (ALC_DEFAULT_ALL_DEVICES_SPECIFIER).</summary>
        string? DefaultName();

        /// <summary>Moves the device to the current default output. Object state is kept.</summary>
        bool ReopenDefault(IntPtr device);
    }

    /// <summary>
    /// Makes game audio follow the system output on macOS (and Linux if enabled).
    /// OpenAL Soft binds to the default device it saw at launch and never rebinds on its own,
    /// in 1.22 and in current releases. Windows uses XAudio and is untouched. The bundled 1.22
    /// has alcReopenDeviceSOFT, which moves the device to another output while keeping every
    /// buffer, source and the context, so no SoundEffect or Song is re-created.
    /// A timer polls the default output name about once a second and reopens on a change.
    /// A failed reopen backs off and is retried; it never throws into the game.
    /// </summary>
    public sealed class AudioOutputFollower : IDisposable
    {
        public const int PollMilliseconds = 1000;
        public const long MinBackoffMilliseconds = 2000;
        public const long MaxBackoffMilliseconds = 30000;
        public const int MaxConsecutiveErrors = 5;

        readonly IAlcApi api;
        readonly Func<long> clock;
        readonly Action<string> log;
        readonly object gate = new();
        Timer? timer;
        bool polling;
        bool disabled;
        string? announcedDevice;
        // Default name we last tried to move to, and when we may try that again.
        string? attemptedDefault;
        long retryAt;
        long backoff = MinBackoffMilliseconds;
        int errors;

        public AudioOutputFollower(IAlcApi api, Func<long>? clock = null, Action<string>? log = null)
        {
            this.api = api;
            this.clock = clock ?? (() => Environment.TickCount64);
            this.log = log ?? Console.WriteLine;
        }

        public int ReopenCount { get; private set; }
        public bool Disabled => disabled;

        static AudioOutputFollower? instance;

        /// <summary>
        /// Starts following the system output. Call once after the audio device exists, for
        /// example after the menu music has started. Safe to call again. Returns false when
        /// following is not used on this OS or is turned off with NVG_AUDIO_FOLLOW=0.
        /// NVG_AUDIO_FOLLOW=1 also enables it on Linux.
        /// </summary>
        public static bool Start()
        {
            lock (typeof(AudioOutputFollower))
            {
                if (instance != null)
                    return true;
                if (!ShouldRun(Environment.GetEnvironmentVariable("NVG_AUDIO_FOLLOW"),
                    OperatingSystem.IsMacOS(), OperatingSystem.IsLinux()))
                    return false;
                var follower = new AudioOutputFollower(new OpenAlApi());
                follower.StartTimer();
                instance = follower;
                return true;
            }
        }

        public static void StopShared()
        {
            lock (typeof(AudioOutputFollower))
            {
                instance?.Dispose();
                instance = null;
            }
        }

        public static bool ShouldRun(string? setting, bool isMac, bool isLinux)
        {
            string value = (setting ?? "").Trim();
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            if (isMac)
                return true;
            return isLinux && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        public void StartTimer()
        {
            lock (gate)
            {
                timer ??= new Timer(_ => Poll(), null, PollMilliseconds, PollMilliseconds);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                timer?.Dispose();
                timer = null;
            }
        }

        /// <summary>One check. Public so tests can drive it without a timer.</summary>
        public void Poll()
        {
            lock (gate)
            {
                if (polling || disabled)
                    return;
                polling = true;
            }
            try
            {
                PollOnce();
                errors = 0;
            }
            catch (Exception ex)
            {
                errors++;
                log("Audio output follow error: " + ex.GetType().Name + ": " + ex.Message);
                if (errors >= MaxConsecutiveErrors)
                {
                    disabled = true;
                    log("Audio output follow disabled after repeated errors.");
                }
            }
            finally
            {
                lock (gate)
                    polling = false;
            }
        }

        void PollOnce()
        {
            IntPtr device = api.CurrentDevice();
            if (device == IntPtr.Zero)
                return;
            if (!api.SupportsReopen(device))
            {
                disabled = true;
                log("Audio output follow disabled: OpenAL has no ALC_SOFT_reopen_device.");
                return;
            }

            string? current = api.DeviceName(device);
            if (announcedDevice != current && !string.IsNullOrEmpty(current))
            {
                log("Audio output: " + current);
                announcedDevice = current;
            }

            string? wanted = api.DefaultName();
            if (string.IsNullOrEmpty(wanted) || string.IsNullOrEmpty(current))
                return;
            if (string.Equals(wanted, current, StringComparison.Ordinal))
            {
                attemptedDefault = null;
                backoff = MinBackoffMilliseconds;
                return;
            }

            long now = clock();
            // A new default always gets an immediate try. The same default that already
            // failed (or did not change the name) waits out the backoff.
            if (string.Equals(wanted, attemptedDefault, StringComparison.Ordinal) && now < retryAt)
                return;
            if (!string.Equals(wanted, attemptedDefault, StringComparison.Ordinal))
                backoff = MinBackoffMilliseconds;
            attemptedDefault = wanted;

            bool ok = api.ReopenDefault(device);
            string? after = ok ? api.DeviceName(device) : null;
            if (ok && !string.IsNullOrEmpty(after) && !string.Equals(after, current, StringComparison.Ordinal))
            {
                ReopenCount++;
                announcedDevice = after;
                log("Audio output changed: " + current + " -> " + after);
                if (!string.Equals(after, wanted, StringComparison.Ordinal))
                    ScheduleRetry(now);
                else
                    backoff = MinBackoffMilliseconds;
                return;
            }

            log(ok
                ? "Audio output reopen left the device on " + current + " (default is " + wanted + ")."
                : "Audio output reopen failed (default is " + wanted + ", staying on " + current + ").");
            ScheduleRetry(now);
        }

        void ScheduleRetry(long now)
        {
            retryAt = now + backoff;
            backoff = Math.Min(backoff * 2, MaxBackoffMilliseconds);
        }

        /// <summary>
        /// Talks to the libopenal that MonoGame already loaded. The handle is found with
        /// RTLD_NOLOAD so this never maps a second copy with separate state.
        /// </summary>
        sealed class OpenAlApi : IAlcApi
        {
            const int AlcDeviceSpecifier = 0x1005;
            const int AlcDefaultAllDevicesSpecifier = 0x1012;
            const int AlcAllDevicesSpecifier = 0x1013;

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            delegate IntPtr GetCurrentContextFn();
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            delegate IntPtr GetContextsDeviceFn(IntPtr context);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            delegate IntPtr GetStringFn(IntPtr device, int param);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            delegate IntPtr GetProcAddressFn(IntPtr device, IntPtr name);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            delegate sbyte ReopenFn(IntPtr device, IntPtr name, IntPtr attribs);

            IntPtr lib;
            GetCurrentContextFn? getContext;
            GetContextsDeviceFn? getDevice;
            GetStringFn? getString;
            GetProcAddressFn? getProc;
            ReopenFn? reopen;
            bool failed;

            bool Load()
            {
                if (getString != null)
                    return true;
                if (failed)
                    return false;
                lib = FindLoadedLibrary();
                if (lib == IntPtr.Zero)
                    return false; // not loaded yet; try again next poll
                try
                {
                    getContext = Export<GetCurrentContextFn>("alcGetCurrentContext");
                    getDevice = Export<GetContextsDeviceFn>("alcGetContextsDevice");
                    getProc = Export<GetProcAddressFn>("alcGetProcAddress");
                    getString = Export<GetStringFn>("alcGetString");
                    return true;
                }
                catch (EntryPointNotFoundException)
                {
                    failed = true;
                    getString = null;
                    return false;
                }
            }

            T Export<T>(string name) where T : Delegate
            {
                IntPtr p = NativeLibrary.GetExport(lib, name);
                return Marshal.GetDelegateForFunctionPointer<T>(p);
            }

            public IntPtr CurrentDevice()
            {
                if (!Load())
                    return IntPtr.Zero;
                IntPtr ctx = getContext!();
                return ctx == IntPtr.Zero ? IntPtr.Zero : getDevice!(ctx);
            }

            public bool SupportsReopen(IntPtr device)
            {
                if (reopen != null)
                    return true;
                IntPtr name = Marshal.StringToHGlobalAnsi("alcReopenDeviceSOFT");
                try
                {
                    IntPtr p = getProc!(device, name);
                    if (p == IntPtr.Zero)
                        return false;
                    reopen = Marshal.GetDelegateForFunctionPointer<ReopenFn>(p);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(name);
                }
            }

            public string? DeviceName(IntPtr device)
            {
                string? name = Marshal.PtrToStringUTF8(getString!(device, AlcAllDevicesSpecifier));
                return string.IsNullOrEmpty(name)
                    ? Marshal.PtrToStringUTF8(getString(device, AlcDeviceSpecifier))
                    : name;
            }

            public string? DefaultName()
            {
                return Marshal.PtrToStringUTF8(getString!(IntPtr.Zero, AlcDefaultAllDevicesSpecifier));
            }

            public bool ReopenDefault(IntPtr device)
            {
                return reopen!(device, IntPtr.Zero, IntPtr.Zero) != 0;
            }
        }

        // ---- locating the already-loaded libopenal ----

        const int RtldLazy = 1;

        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        static extern IntPtr DlopenLinux(string? path, int flags);
        [DllImport("libSystem.dylib", EntryPoint = "dlopen")]
        static extern IntPtr DlopenMac(string? path, int flags);
        [DllImport("libSystem.dylib")]
        static extern uint _dyld_image_count();
        [DllImport("libSystem.dylib")]
        static extern IntPtr _dyld_get_image_name(uint index);

        static IntPtr Dlopen(string path, bool noLoad)
        {
            if (OperatingSystem.IsMacOS())
                return DlopenMac(path, RtldLazy | (noLoad ? 0x10 : 0));
            return DlopenLinux(path, RtldLazy | (noLoad ? 0x4 : 0));
        }

        /// <summary>Path of an already-loaded libopenal, or empty. Exposed for diagnostics.</summary>
        public static string FindLoadedLibraryPath()
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    uint count = _dyld_image_count();
                    for (uint i = 0; i < count; i++)
                    {
                        string? name = Marshal.PtrToStringUTF8(_dyld_get_image_name(i));
                        if (name != null && Path.GetFileName(name).StartsWith("libopenal", StringComparison.Ordinal))
                            return name;
                    }
                }
                else if (OperatingSystem.IsLinux())
                {
                    foreach (string line in File.ReadLines("/proc/self/maps"))
                    {
                        int slash = line.IndexOf('/');
                        if (slash < 0)
                            continue;
                        string path = line.Substring(slash);
                        if (Path.GetFileName(path).StartsWith("libopenal", StringComparison.Ordinal))
                            return path;
                    }
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        static IntPtr FindLoadedLibrary()
        {
            string path = FindLoadedLibraryPath();
            if (path.Length == 0)
                return IntPtr.Zero;
            try
            {
                // RTLD_NOLOAD: returns the existing image, never maps a second copy.
                return Dlopen(path, noLoad: true);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }
    }
}
