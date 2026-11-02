using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace NonsensicalVideoGenerator
{
    // Dirty workaround for the fact that MonoGame.Extended2.VideoPlayback doesn't allow file unlocking
    public static class VideoCache
    {
        static readonly object Gate = new();
        static readonly Dictionary<string, string> Cache = new();

        public static string GetCachePath(string video)
        {
            lock (Gate)
            {
                string source = Resolve(video);
                if (!File.Exists(source))
                    return source;
                string videoUniqueKey;
                try
                {
                    FileInfo info = new FileInfo(source);
                    videoUniqueKey = Path.GetFileName(source)
                        + "_" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                        + "_" + info.Length.ToString(CultureInfo.InvariantCulture);
                }
                catch
                {
                    return source;
                }
                if (Cache.TryGetValue(videoUniqueKey, out string? cached) && !string.IsNullOrEmpty(cached) && File.Exists(cached))
                    return cached;
                string cache = Path.Combine(AppPaths.Writable, "cache");
                if (!Directory.Exists(cache))
                    Directory.CreateDirectory(cache);
                string cachePath = Path.Combine(cache, Path.GetRandomFileName() + Path.GetExtension(source));
                try
                {
                    File.Copy(source, cachePath);
                }
                catch
                {
                    // The player locked the file. Do not remember a path that was never copied.
                    try
                    {
                        if (File.Exists(cachePath))
                            File.Delete(cachePath);
                    }
                    catch
                    {
                    }
                    return source;
                }
                Cache[videoUniqueKey] = cachePath;
                return cachePath;
            }
        }

        public static void ClearCache()
        {
            lock (Gate)
            {
                DeleteFiles(Path.Combine(AppPaths.Writable, "cache"));
                DeleteFiles(Path.Combine(AppPaths.Writable, "cache", "play"));
                Cache.Clear();
            }
        }

        static string Resolve(string video)
        {
            if (string.IsNullOrEmpty(video))
                return video ?? "";
            try
            {
                if (Path.IsPathRooted(video) || File.Exists(video))
                    return Path.GetFullPath(video);
                return Path.GetFullPath(Path.Combine(AppPaths.Writable, video));
            }
            catch
            {
                return video;
            }
        }

        static void DeleteFiles(string cache)
        {
            if (!Directory.Exists(cache))
                return;
            foreach (string file in Directory.GetFiles(cache))
            {
                File.Delete(file);
            }
        }
    }
}
