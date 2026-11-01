#if DESKTOPGL
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.VideoPlayback;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Experimental headless checks for the S1/L1 trial. Enabled only with -trial.
    /// The window still runs; this does not replace a real click-through.
    /// </summary>
    public static class TrialGate
    {
        enum Phase
        {
            Idle,
            Playing,
            Done,
        }

        static Phase phase = Phase.Idle;
        static double playStart;
        static string effectSummary = "not started";

        public static void Tick(GameTime gameTime, GraphicsDevice device, UserInterface ui)
        {
            if (!Global.parameters.Contains("-trial"))
                return;
            if (phase == Phase.Idle)
            {
                phase = Phase.Playing;
                playStart = gameTime.TotalGameTime.TotalSeconds;
                try
                {
                    Run(device, ui);
                }
                catch (Exception ex)
                {
                    Log("TRIAL exception: " + ex);
                    phase = Phase.Done;
                }
            }
            else if (phase == Phase.Playing && gameTime.TotalGameTime.TotalSeconds - playStart >= 2)
            {
                LogPlayback(ui);
                phase = Phase.Done;
                Log("TRIAL effect: " + effectSummary);
                Log("TRIAL gate finished");
            }
        }

        static void Run(GraphicsDevice device, UserInterface ui)
        {
            Log("TRIAL begin");
            Log("TRIAL avcodec " + (FfmpegNative.AvcodecLoaded ? "OK " + FfmpegNative.AvcodecPath : "FAIL"));
            string fixture = NvgPaths.Native(@".\library\video\outros\defaultoutro.mp4");
            if (!File.Exists(fixture))
            {
                Log("TRIAL fixture missing: " + fixture);
                return;
            }
            LibraryFile? imported = LibraryData.Load(new LibraryFile("trial-fixture", Path.GetFullPath(fixture), DefaultLibraryTypes.Material));
            if (imported == null || imported.Path == null)
            {
                Log("TRIAL import FAIL");
                return;
            }
            Log("TRIAL import OK " + imported.Path);
            string tempDir = NvgPaths.Native(@".\temp");
            Directory.CreateDirectory(tempDir);
            string thumb = Path.Combine(tempDir, "trial-thumb.bmp");
            Generator.GenerateThumbnail(imported.Path, thumb, LibraryRootType.Video);
            if (!File.Exists(thumb))
            {
                Log("TRIAL thumbnail file missing");
            }
            else
            {
                int scale = int.Parse(SaveData.saveValues["VideoPlaybackScale"], CultureInfo.InvariantCulture);
                Color[] pixels = BmpThumbnail.Read(thumb, 29 * scale, 23 * scale);
                var texture = new Texture2D(device, 29 * scale, 23 * scale);
                texture.SetData(pixels);
                texture.Dispose();
                Log("TRIAL thumbnail OK pixels=" + pixels.Length + " bytes=" + new FileInfo(thumb).Length);
            }
            try
            {
                string cache = VideoCache.GetCachePath(imported.Path);
                ui.videoPath = cache;
                ui.video = VideoHelper.LoadFromFile(cache);
                ui.videoPlayer ??= new MonoGame.Extended.Framework.Media.VideoPlayer(device);
                ui.videoPlayer.Play(ui.video);
                ui.videoPlayer.Volume = 0.2f;
                Global.videoPlaying = true;
                Log("TRIAL playback started duration=" + ui.video.Duration.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Log("TRIAL playback FAIL " + ex.GetType().Name + ": " + ex.Message);
            }
            Task.Run(() =>
            {
                try
                {
                    effectSummary = RenderStock(imported.Path);
                }
                catch (Exception ex)
                {
                    effectSummary = "FAIL " + ex;
                }
                Log("TRIAL effect: " + effectSummary);
            });
        }

        static string RenderStock(string videoPath)
        {
            PluginHandler.LoadPlugins();
            Plugin? speed = PluginHandler.plugins.FirstOrDefault(plugin => plugin.path.Replace('\\', '/').EndsWith("stock/speed.lua", StringComparison.OrdinalIgnoreCase));
            if (speed == null)
                return "FAIL speed.lua not loaded (" + PluginHandler.plugins.Count + " plugins)";
            PluginReturnValue result = speed.Call(videoPath);
            if (!result.success || string.IsNullOrEmpty(result.jobFolder))
                return "FAIL speed.lua Call success=" + result.success;
            string mp4 = Path.Combine(result.jobFolder, "output.mp4");
            if (!File.Exists(mp4))
                return "FAIL output missing " + mp4;
            string probe = Probe(mp4);
            return "OK " + mp4 + " | " + probe.Replace('\n', ' ');
        }

        static string Probe(string mp4)
        {
            var psi = new ProcessStartInfo("ffprobe", "-hide_banner -show_streams -of compact=p=0:nk=0 \"" + mp4 + "\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process? process = Process.Start(psi);
            if (process == null)
                return "ffprobe did not start";
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return "ffprobe exit " + process.ExitCode + " " + stderr;
            return stdout.Trim();
        }

        static void LogPlayback(UserInterface ui)
        {
            try
            {
                if (ui.videoPlayer == null || ui.video == null)
                {
                    Log("TRIAL playback state: no player");
                    return;
                }
                Log("TRIAL playback state=" + ui.videoPlayer.State + " position=" + ui.videoPlayer.PlayPosition.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Log("TRIAL playback state FAIL " + ex.Message);
            }
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
            try
            {
                ConsoleOutput.WriteLine(message, Color.LightGreen);
            }
            catch
            {
            }
        }
    }
}
#endif
