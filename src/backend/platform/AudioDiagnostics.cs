using System;
using System.Runtime.InteropServices;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Read-only macOS default-output name for the startup log.
    /// OpenAL Soft already opens the CoreAudio default. This names that device.
    /// It does not pin a different device.
    /// </summary>
    public static class AudioDiagnostics
    {
        public static string FormatOutputNote(string? platform, string? deviceName)
        {
            if (platform == "macos")
            {
                if (string.IsNullOrWhiteSpace(deviceName))
                    return "Audio output: macOS default playback device (name unavailable).";
                return "Audio output: " + deviceName.Trim() + " (macOS default playback device).";
            }
            if (platform == "linux")
                return "Audio output: OpenAL default playback device.";
            return "Audio output: default playback device.";
        }

        /// <summary>
        /// CoreAudio default output name. Null when this is not macOS or the query fails.
        /// Does not open an OpenAL device.
        /// </summary>
        public static string? DefaultOutputDeviceName()
        {
            if (!OperatingSystem.IsMacOS())
                return null;
            try
            {
                return QueryMacDefaultOutput();
            }
            catch
            {
                return null;
            }
        }

        static string? QueryMacDefaultOutput()
        {
            uint size = sizeof(uint);
            IntPtr idBuf = Marshal.AllocHGlobal((int)size);
            try
            {
                int status = GetProperty(SystemObject, DefaultOutputDevice, idBuf, ref size);
                if (status != 0 || size < sizeof(uint))
                    return null;
                uint device = (uint)Marshal.ReadInt32(idBuf);
                if (device == 0)
                    return null;
                string? cName = ReadCStringName(device);
                if (!string.IsNullOrWhiteSpace(cName))
                    return cName;
                return ReadCfStringName(device);
            }
            finally
            {
                Marshal.FreeHGlobal(idBuf);
            }
        }

        static string? ReadCStringName(uint device)
        {
            uint size = 256;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                for (int i = 0; i < size; i++)
                    Marshal.WriteByte(buf, i, 0);
                int status = GetProperty(device, DeviceNameCString, buf, ref size);
                if (status != 0 || size == 0)
                    return null;
                string name = Marshal.PtrToStringUTF8(buf) ?? "";
                name = name.Trim();
                return name.Length == 0 ? null : name;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        static string? ReadCfStringName(uint device)
        {
            uint size = (uint)IntPtr.Size;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                int status = GetProperty(device, ObjectName, buf, ref size);
                if (status != 0)
                    return null;
                IntPtr cf = Marshal.ReadIntPtr(buf);
                if (cf == IntPtr.Zero)
                    return null;
                try
                {
                    byte[] text = new byte[512];
                    if (CFStringGetCString(cf, text, text.Length, CfStringUtf8) == 0)
                        return null;
                    int end = Array.IndexOf(text, (byte)0);
                    if (end < 0)
                        end = text.Length;
                    string name = System.Text.Encoding.UTF8.GetString(text, 0, end).Trim();
                    return name.Length == 0 ? null : name;
                }
                finally
                {
                    CFRelease(cf);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        static int GetProperty(uint objectId, uint selector, IntPtr data, ref uint size)
        {
            var address = new AudioObjectPropertyAddress
            {
                Selector = selector,
                Scope = ScopeGlobal,
                Element = ElementMain,
            };
            return AudioObjectGetPropertyData(objectId, ref address, 0, IntPtr.Zero, ref size, data);
        }

        const uint SystemObject = 1;
        const uint DefaultOutputDevice = 0x644F7574; // 'dOut'
        const uint ScopeGlobal = 0x676C6F62; // 'glob'
        const uint ElementMain = 0;
        const uint DeviceNameCString = 0x6E616D65; // 'name'
        const uint ObjectName = 0x6C6E616D; // 'lnam'
        const uint CfStringUtf8 = 0x08000100;

        [StructLayout(LayoutKind.Sequential)]
        struct AudioObjectPropertyAddress
        {
            public uint Selector;
            public uint Scope;
            public uint Element;
        }

        [DllImport("/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
        static extern int AudioObjectGetPropertyData(
            uint inObjectID,
            ref AudioObjectPropertyAddress inAddress,
            uint inQualifierDataSize,
            IntPtr inQualifierData,
            ref uint ioDataSize,
            IntPtr outData);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        static extern byte CFStringGetCString(IntPtr theString, byte[] buffer, nint bufferSize, uint encoding);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        static extern void CFRelease(IntPtr cf);
    }
}
