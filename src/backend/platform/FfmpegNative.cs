using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Loads FFmpeg 7 shared libraries for Sdcb.FFmpeg (avcodec-61 and the
    /// matching majors). The real SONAME is opened by absolute path.
    /// .NET on Linux appends another ".so" when a name already contains
    /// ".so.", so a request for libavcodec.so.61.so is mapped back to
    /// libavcodec.so.61. No alias file is staged.
    /// </summary>
    public static class FfmpegNative
    {
        static readonly Dictionary<string, (string Stem, int Major)> Map = new()
        {
            ["avcodec-61"] = ("avcodec", 61),
            ["avformat-61"] = ("avformat", 61),
            ["avutil-59"] = ("avutil", 59),
            ["avfilter-10"] = ("avfilter", 10),
            ["avdevice-61"] = ("avdevice", 61),
            ["swscale-8"] = ("swscale", 8),
            ["swresample-5"] = ("swresample", 5),
            ["postproc-58"] = ("postproc", 58),
        };

        public static bool AvcodecLoaded { get; private set; }
        public static string AvcodecPath { get; private set; } = "";

        public static string LibraryFileName(string stem, int major)
        {
            return LibraryFileName(stem, major, HostOs());
        }

        public static string LibraryFileName(string stem, int major, string os)
        {
            if (os == "windows")
                return stem + "-" + major + ".dll";
            if (os == "macos")
                return "lib" + stem + "." + major + ".dylib";
            return "lib" + stem + ".so." + major;
        }

        /// <summary>
        /// Maps an import name or a Linux probe name onto the real library file.
        /// <c>libavcodec.so.61.so</c> maps to <c>libavcodec.so.61</c>.
        /// </summary>
        public static string? CanonicalLibraryName(string requested)
        {
            if (!TryParse(requested, out string stem, out int major, out string fileName))
                return null;
            return fileName;
        }

        public static void Register()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            // Sdcb.FFmpeg.Raw.ffmpeg's static constructor calls SetDllImportResolver
            // and then NativeLibrary.TryLoad("libavcodec.so.61") (Linux) or the
            // macOS dylib name. A second resolver on that assembly throws.
            // Preload the real absolute SONAME first. glibc returns the existing
            // object when Sdcb later dlopens the SONAME, including the extra
            // ".so" variation, once the library is already mapped.
            Preload();
            RegisterAssembly(Path.Combine(baseDir, "MonoGame.Extended.VideoPlayback.dll"));
            AvcodecLoaded = TryLoadImport("avcodec-61", out string loaded);
            AvcodecPath = loaded;
            Console.WriteLine(AvcodecLoaded
                ? "FFmpeg resolver: avcodec-61 OK -> " + loaded
                : "FFmpeg resolver: avcodec-61 FAIL");
            ProbeSdcb(baseDir);
        }

        static void Preload()
        {
            foreach (var pair in Map)
            {
                string fileName = LibraryFileName(pair.Value.Stem, pair.Value.Major);
                foreach (string dir in LibDirs())
                {
                    string file = Path.Combine(dir, fileName);
                    if (!File.Exists(file))
                        continue;
                    if (NativeLibrary.TryLoad(file, out _))
                    {
                        Console.WriteLine("FFmpeg resolver: preloaded " + file);
                        break;
                    }
                    Console.WriteLine("FFmpeg resolver: preload failed " + file);
                }
            }
        }

        static void ProbeSdcb(string baseDir)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(Path.Combine(baseDir, "Sdcb.FFmpeg.dll"));
                Type? ffmpeg = assembly.GetType("Sdcb.FFmpeg.Raw.ffmpeg");
                MethodInfo? version = ffmpeg?.GetMethod("avcodec_version");
                object? value = version?.Invoke(null, null);
                Console.WriteLine("FFmpeg Sdcb init OK avcodec_version=" + value);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FFmpeg Sdcb init FAIL " + ex.GetType().Name + ": " + ex.Message);
                if (ex.InnerException != null)
                    Console.WriteLine("FFmpeg Sdcb init inner " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
            }
        }

        static void RegisterAssembly(string dllPath)
        {
            if (!File.Exists(dllPath))
            {
                Console.WriteLine("FFmpeg resolver: assembly missing " + dllPath);
                return;
            }
            Assembly assembly = Assembly.LoadFrom(dllPath);
            try
            {
                NativeLibrary.SetDllImportResolver(assembly, Resolve);
                Console.WriteLine("FFmpeg resolver: registered " + assembly.GetName().Name);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("FFmpeg resolver: already registered " + assembly.GetName().Name + " " + ex.Message);
            }
        }

        static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!TryParse(libraryName, out string stem, out int major, out string fileName))
                return IntPtr.Zero;
            // Import names follow the host. A Linux ".so.N.so" probe stays on the real SONAME.
            if (libraryName.IndexOf(".so.", StringComparison.Ordinal) < 0)
                fileName = LibraryFileName(stem, major);
            foreach (string dir in LibDirs())
            {
                string file = Path.Combine(dir, fileName);
                if (!File.Exists(file))
                    continue;
                if (NativeLibrary.TryLoad(file, out IntPtr handle))
                {
                    Console.WriteLine("FFmpeg resolver: " + libraryName + " -> " + file);
                    return handle;
                }
            }
            Console.WriteLine("FFmpeg resolver: FAILED " + libraryName + " (" + fileName + ")");
            return IntPtr.Zero;
        }

        static bool TryLoadImport(string libraryName, out string loadedPath)
        {
            loadedPath = "";
            if (!TryParse(libraryName, out string stem, out int major, out _))
                return false;
            string fileName = LibraryFileName(stem, major);
            foreach (string dir in LibDirs())
            {
                string file = Path.Combine(dir, fileName);
                if (File.Exists(file) && NativeLibrary.TryLoad(file, out _))
                {
                    loadedPath = file;
                    return true;
                }
            }
            return false;
        }

        static bool TryParse(string requested, out string stem, out int major, out string fileName)
        {
            foreach (var pair in Map)
            {
                stem = pair.Value.Stem;
                major = pair.Value.Major;
                string linux = LibraryFileName(stem, major, "linux");
                string macos = LibraryFileName(stem, major, "macos");
                string windows = LibraryFileName(stem, major, "windows");
                if (requested == pair.Key)
                {
                    fileName = LibraryFileName(stem, major);
                    return true;
                }
                if (requested == linux || requested == linux + ".so")
                {
                    fileName = linux;
                    return true;
                }
                if (requested == macos)
                {
                    fileName = macos;
                    return true;
                }
                if (requested == windows)
                {
                    fileName = windows;
                    return true;
                }
            }
            stem = "";
            major = 0;
            fileName = "";
            return false;
        }

        static string HostOs()
        {
            if (OperatingSystem.IsWindows())
                return "windows";
            if (OperatingSystem.IsMacOS())
                return "macos";
            return "linux";
        }

        static IEnumerable<string> LibDirs()
        {
            string? env = Environment.GetEnvironmentVariable("NVG_FFMPEG_LIB");
            if (!string.IsNullOrWhiteSpace(env))
                yield return env;
            string root = ToolPaths.ResourceRoot;
            if (!string.IsNullOrEmpty(root))
            {
                yield return Path.Combine(root, "ffmpeg7", "lib");
                yield return Path.Combine(root, "lib");
                yield return root;
            }
            if (OperatingSystem.IsMacOS())
            {
                yield return "/opt/homebrew/opt/ffmpeg@7/lib";
                yield return "/usr/local/opt/ffmpeg@7/lib";
            }
        }
    }
}
