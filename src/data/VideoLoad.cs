using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using MonoGame.Extended.Framework.Media;
using MonoGame.Extended.VideoPlayback;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Opens a file the guard already accepted. A failed native open throws
    /// FFmpegException now that the format-context double-free is patched.
    /// The file is left in place.
    /// </summary>
    public static class VideoLoad
    {
        public static bool TryLoad(string path, [NotNullWhen(true)] out Video? video, out string why)
        {
            video = null;
            why = "";
            try
            {
                video = VideoHelper.LoadFromFile(path);
                if (video != null)
                    return true;
                why = "the player returned no video.";
                ConsoleOutput.WriteLine("Could not load video: " + why, Color.Red);
                return false;
            }
            catch (Exception ex)
            {
                video = null;
                why = Describe(ex);
                ConsoleOutput.WriteLine("Could not load video: " + why, Color.Red);
                return false;
            }
        }

        static string Describe(Exception ex)
        {
            string why = ex.GetType().Name + ": " + ex.Message;
            Exception? inner = ex.InnerException;
            while (inner != null)
            {
                why += " (" + inner.GetType().Name + ": " + inner.Message + ")";
                inner = inner.InnerException;
            }
            return why;
        }
    }
}
