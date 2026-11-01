using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// DesktopGL clipboard. Tries SDL2 (already loaded by MonoGame), then pbcopy/xclip.
    /// </summary>
    public static class PlatformClipboard
    {
        public static string GetText()
        {
            try
            {
                IntPtr text = SDL_GetClipboardText();
                if (text != IntPtr.Zero)
                {
                    string value = Marshal.PtrToStringUTF8(text) ?? "";
                    SDL_free(text);
                    if (value.Length > 0)
                        return value;
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
            try
            {
                if (SDL_SetClipboardText(text ?? "") == 0)
                    return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SDL clipboard write failed: " + ex.Message);
            }
            WriteViaTool(text ?? "");
        }

        static string ReadViaTool()
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                    return Run("pbpaste", "");
                if (OperatingSystem.IsLinux())
                    return Run("xclip", "-selection clipboard -o");
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
                if (OperatingSystem.IsMacOS())
                {
                    var psi = new ProcessStartInfo("pbcopy")
                    {
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using Process? process = Process.Start(psi);
                    if (process == null)
                        return;
                    process.StandardInput.Write(text);
                    process.StandardInput.Close();
                    process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Clipboard tool write failed: " + ex.Message);
            }
        }

        static string Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process? process = Process.Start(psi);
            if (process == null)
                return "";
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr SDL_GetClipboardText();

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
        static extern int SDL_SetClipboardText(string text);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
        static extern void SDL_free(IntPtr mem);
    }
}
