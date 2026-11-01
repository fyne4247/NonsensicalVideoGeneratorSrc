using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Maps Sdcb.FFmpeg / Extended.VideoPlayback import names (avcodec-61)
    /// onto FFmpeg 7 shared libraries. Does not alias system SONAMEs.
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

        public static void Register()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            // Sdcb.FFmpeg.Raw.ffmpeg's static constructor already calls
            // SetDllImportResolver and maps avcodec-61 to libavcodec.61.dylib
            // (macOS) or libavcodec.so.61 (Linux). A second resolver throws.
            // Stage those filenames next to the app so its resolver can see the
            // FFmpeg 7 libraries, which are not on the default loader path.
            Stage(baseDir);
            RegisterAssembly(Path.Combine(baseDir, "MonoGame.Extended.VideoPlayback.dll"));
            AvcodecLoaded = TryLoadImport("avcodec-61", out string loaded);
            AvcodecPath = loaded;
            Console.WriteLine(AvcodecLoaded
                ? "FFmpeg resolver smoke: avcodec-61 OK -> " + loaded
                : "FFmpeg resolver smoke: avcodec-61 FAIL");
            ProbeSdcb(baseDir);
        }

        static void Stage(string baseDir)
        {
            foreach (var pair in Map)
            {
                string? source = null;
                foreach (string dir in LibDirs())
                {
                    string candidate = LibraryFile(dir, pair.Value.Stem, pair.Value.Major);
                    if (File.Exists(candidate))
                    {
                        source = candidate;
                        break;
                    }
                }
                var names = new List<string>();
                if (OperatingSystem.IsMacOS())
                    names.Add("lib" + pair.Value.Stem + "." + pair.Value.Major + ".dylib");
                else
                {
                    // Sdcb asks for libavcodec.so.61. .NET's Linux probe then looks for
                    // libavcodec.so.61.so (it appends .so again). Stage both.
                    names.Add("lib" + pair.Value.Stem + ".so." + pair.Value.Major);
                    names.Add("lib" + pair.Value.Stem + ".so." + pair.Value.Major + ".so");
                }
                if (source == null)
                {
                    Console.WriteLine("FFmpeg resolver: missing " + names[0]);
                    continue;
                }
                foreach (string fileName in names)
                {
                    string dest = Path.Combine(baseDir, fileName);
                    try
                    {
                        if (File.Exists(dest))
                        {
                            string? link = new FileInfo(dest).LinkTarget;
                            if (link == null)
                                continue;
                            string full = Path.IsPathRooted(link) ? link : Path.GetFullPath(Path.Combine(baseDir, link));
                            if (full == Path.GetFullPath(source))
                                continue;
                            File.Delete(dest);
                        }
                        File.CreateSymbolicLink(dest, source);
                        Console.WriteLine("FFmpeg resolver: staged " + fileName + " -> " + source);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("FFmpeg resolver: stage failed " + fileName + " " + ex.Message);
                    }
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
            NativeLibrary.SetDllImportResolver(assembly, Resolve);
            Console.WriteLine("FFmpeg resolver: registered " + assembly.GetName().Name);
        }

        static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!Map.TryGetValue(libraryName, out var info))
                return IntPtr.Zero;
            foreach (string dir in LibDirs())
            {
                string file = LibraryFile(dir, info.Stem, info.Major);
                if (!File.Exists(file))
                    continue;
                if (NativeLibrary.TryLoad(file, out IntPtr handle))
                {
                    Console.WriteLine("FFmpeg resolver: " + libraryName + " -> " + file);
                    return handle;
                }
            }
            Console.WriteLine("FFmpeg resolver: FAILED " + libraryName);
            return IntPtr.Zero;
        }

        static bool TryLoadImport(string libraryName, out string loadedPath)
        {
            loadedPath = "";
            if (!Map.TryGetValue(libraryName, out var info))
                return false;
            foreach (string dir in LibDirs())
            {
                string file = LibraryFile(dir, info.Stem, info.Major);
                if (File.Exists(file) && NativeLibrary.TryLoad(file, out _))
                {
                    loadedPath = file;
                    return true;
                }
            }
            return false;
        }

        static string LibraryFile(string dir, string stem, int major)
        {
            if (OperatingSystem.IsMacOS())
                return Path.Combine(dir, "lib" + stem + "." + major + ".dylib");
            return Path.Combine(dir, "lib" + stem + ".so." + major);
        }

        static IEnumerable<string> LibDirs()
        {
            string? env = Environment.GetEnvironmentVariable("NVG_FFMPEG_LIB");
            if (!string.IsNullOrWhiteSpace(env))
                yield return env;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                yield return Path.Combine(home, "nvg-trial", "ffmpeg7", "lib");
            if (OperatingSystem.IsMacOS())
            {
                yield return "/opt/homebrew/opt/ffmpeg@7/lib";
                yield return "/usr/local/opt/ffmpeg@7/lib";
            }
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            yield return Path.Combine(baseDir, "ffmpeg7", "lib");
            yield return baseDir;
        }
    }
}
