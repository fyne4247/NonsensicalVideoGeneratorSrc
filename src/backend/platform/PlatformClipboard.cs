using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// DesktopGL clipboard. SDL2 is already loaded by MonoGame. SDL-owned text is released with SDL_free.
    /// pbcopy/pbpaste and xclip are the fallback when SDL has no text.
    /// </summary>
    public static class PlatformClipboard
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr GetTextDel();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int SetTextDel([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void FreeDel(IntPtr mem);

        static readonly object Gate = new();
        static bool attempted;
        static IntPtr library;
        static GetTextDel? getText;
        static SetTextDel? setText;
        static FreeDel? freeMem;

        public static string GetText()
        {
            try
            {
                if (EnsureLoaded() && getText != null && freeMem != null)
                {
                    IntPtr text = getText();
                    if (text != IntPtr.Zero)
                    {
                        try
                        {
                            string value = Marshal.PtrToStringUTF8(text) ?? "";
                            if (value.Length > 0)
                                return value;
                        }
                        finally
                        {
                            freeMem(text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SDL clipboard read failed: " + ex.Message);
            }
            return ReadViaTool();
        }

        public static void SetText(string text)
        {
            text ??= "";
            try
            {
                if (EnsureLoaded() && setText != null && setText(text) == 0)
                    return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SDL clipboard write failed: " + ex.Message);
            }
            WriteViaTool(text);
        }

        internal static bool TryEnsureLoaded()
        {
            return EnsureLoaded();
        }

        static bool EnsureLoaded()
        {
            lock (Gate)
            {
                if (attempted)
                    return library != IntPtr.Zero && getText != null && setText != null && freeMem != null;
                attempted = true;
                foreach (string candidate in Candidates())
                {
                    if (candidate.IndexOf(Path.DirectorySeparatorChar) >= 0 && !File.Exists(candidate))
                        continue;
                    if (!NativeLibrary.TryLoad(candidate, out IntPtr handle))
                        continue;
                    if (!NativeLibrary.TryGetExport(handle, "SDL_GetClipboardText", out IntPtr getPtr))
                        continue;
                    if (!NativeLibrary.TryGetExport(handle, "SDL_SetClipboardText", out IntPtr setPtr))
                        continue;
                    if (!NativeLibrary.TryGetExport(handle, "SDL_free", out IntPtr freePtr))
                        continue;
                    library = handle;
                    getText = Marshal.GetDelegateForFunctionPointer<GetTextDel>(getPtr);
                    setText = Marshal.GetDelegateForFunctionPointer<SetTextDel>(setPtr);
                    freeMem = Marshal.GetDelegateForFunctionPointer<FreeDel>(freePtr);
                    return true;
                }
                return false;
            }
        }

        static IEnumerable<string> Candidates()
        {
            string managedDir = Path.GetDirectoryName(typeof(PlatformClipboard).Assembly.Location) ?? "";
            string[] names = LibraryNames();
            string[] ridDirs = OperatingSystem.IsLinux()
                ? new[] { Path.Combine("runtimes", "linux-x64", "native"), Path.Combine("runtimes", "linux", "native") }
                : OperatingSystem.IsMacOS()
                    ? new[] { Path.Combine("runtimes", "osx", "native"), Path.Combine("runtimes", "osx-arm64", "native"), Path.Combine("runtimes", "osx-x64", "native") }
                    : new[] { Path.Combine("runtimes", "win-x64", "native") };
            foreach (string dir in new[] { AppContext.BaseDirectory, managedDir })
            {
                if (string.IsNullOrEmpty(dir))
                    continue;
                foreach (string name in names)
                    yield return Path.Combine(dir, name);
                foreach (string rid in ridDirs)
                {
                    foreach (string name in names)
                        yield return Path.Combine(dir, rid, name);
                }
            }
            foreach (string name in names)
                yield return name;
        }

        static string[] LibraryNames()
        {
            if (OperatingSystem.IsLinux())
                return new[] { "libSDL2-2.0.so.0", "libSDL2.so.0", "libSDL2.so", "SDL2" };
            if (OperatingSystem.IsMacOS())
                return new[] { "libSDL2.dylib", "libSDL2-2.0.0.dylib", "SDL2" };
            return new[] { "SDL2.dll", "SDL2" };
        }

        static string ReadViaTool()
        {
            try
            {
                if (OperatingSystem.IsMacOS() && PlatformFileDialog.CommandOnPath("pbpaste"))
                    return Run("pbpaste", Array.Empty<string>());
                if (OperatingSystem.IsLinux() && PlatformFileDialog.CommandOnPath("xclip"))
                    return Run("xclip", new[] { "-selection", "clipboard", "-o" });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Clipboard tool read failed: " + ex.Message);
            }
            return "";
        }

        static void WriteViaTool(string text)
        {
            try
            {
                if (OperatingSystem.IsMacOS() && PlatformFileDialog.CommandOnPath("pbcopy"))
                {
                    WriteStdin("pbcopy", Array.Empty<string>(), text);
                    return;
                }
                if (OperatingSystem.IsLinux() && PlatformFileDialog.CommandOnPath("xclip"))
                    WriteStdin("xclip", new[] { "-selection", "clipboard", "-i" }, text);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Clipboard tool write failed: " + ex.Message);
            }
        }

        static string Run(string file, string[] args)
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
                psi.ArgumentList.Add(arg);
            using Process? process = Process.Start(psi);
            if (process == null)
                return "";
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }

        static void WriteStdin(string file, string[] args, string text)
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
                psi.ArgumentList.Add(arg);
            using Process? process = Process.Start(psi);
            if (process == null)
                return;
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit();
        }
    }
}
