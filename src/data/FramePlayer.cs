using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using System.Globalization;
using System.Threading;

namespace NonsensicalVideoGenerator
{
    public static class FramePlayer
    {
        public static int currentFrame = 0;
        public static List<Texture2D> frames = new();
        public static Texture2D? audioFrame;
        public static double timeStarted = 0;
        public static int fps = 30;
        public static bool playing = false;
        public static bool audioPlaying = false;
        public static SoundEffectInstance? audio;
        public static bool processing = false;
        public static double startedProcessing = 0;
        public static string currentPath = "";
        public static BackgroundWorker worker = new();
        public static BackgroundWorker progressWorker = new();
        public static BackgroundWorker countWorker = new();
        public static BackgroundWorker audioConvertWorker = new();
        public static int count = 0;
        public static double audioLength = 0;
        public static double currentAudioTime = 0;
        public static bool canPlayBgMusic = true;
        static string ExtFrames => Path.Combine("temp", "extracted", "frames");
        static string ExtAudio => Path.Combine("temp", "extracted", "audio.wav");
        static string ExtAudioBmp => Path.Combine("temp", "extracted", "audio.bmp");
        static string ExtRoot => Path.Combine("temp", "extracted");
        static string ExtFramePattern => Path.Combine("temp", "extracted", "frames", "%d.bmp");
        private static void ExtractFramesAndAudio()
        {
            try
            {
                if(worker.CancellationPending || !processing)
                    return;
                try
                {
                    // Delete all files in the directory
                    foreach (string file2 in Directory.GetFiles(ExtFrames))
                    {
                        if(worker.CancellationPending || !processing)
                            return;
                        File.Delete(file2);
                    }
                    // Delete audio file if it exists
                    if (File.Exists(ExtAudio))
                    {
                        File.Delete(ExtAudio);
                    }
                }
                catch
                {
                }
                if(worker.CancellationPending || !processing)
                    return;
                int scale =  int.Parse(SaveData.saveValues["VideoPlaybackScale"], CultureInfo.InvariantCulture);
                int w = 100 * scale;
                int h = 78 * scale;
                ProcessStartInfo startInfo = new()
                {
                    FileName = ToolPaths.Ffmpeg(useSystemOnWindows: true),
                    // 100x78 letterboxed
                    // fastest method
                    Arguments = "-i \""+currentPath+"\" -vf scale=" + w + ":" + h + ":force_original_aspect_ratio=decrease,pad=" + w + ":" + h + ":(ow-iw)/2:(oh-ih)/2 -r " + fps + " -y " + ExtFramePattern,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                Process process = new()
                {
                    StartInfo = startInfo
                };
                process.ErrorDataReceived += (sender, args) => {
                    if (args.Data != null)
                        ConsoleOutput.WriteLine(args.Data, Color.Transparent);
                };
                ConsoleOutput.WriteLine($"> {startInfo.FileName} {startInfo.Arguments}", Color.Transparent);
                process.Start();
                if(worker.CancellationPending || !processing)
                    return;
                process.BeginErrorReadLine();
                if(worker.CancellationPending || !processing)
                    return;
                process.WaitForExit();
                if(worker.CancellationPending || !processing)
                    return;
                // Extract audio
                startInfo = new()
                {
                    FileName = ToolPaths.Ffmpeg(useSystemOnWindows: true),
                    Arguments = "-i \"" + currentPath + "\" -vn -acodec pcm_s16le -ar 44100 -ac 2 -f wav -y " + ExtAudio + "",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process = new()
                {
                    StartInfo = startInfo
                };
                process.ErrorDataReceived += (sender, args) => {
                    if(args.Data != null)
                        ConsoleOutput.WriteLine(args.Data, Color.Transparent);
                };
                if(worker.CancellationPending || !processing)
                    return;
                ConsoleOutput.WriteLine($"> {startInfo.FileName} {startInfo.Arguments}", Color.Transparent);
                process.Start();
                if(worker.CancellationPending || !processing)
                    return;
                process.BeginErrorReadLine();
                if(worker.CancellationPending || !processing)
                    return;
                process.WaitForExit();
                // Write blank audio if it doesn't exist
                if(!File.Exists(ExtAudio))
                {
                    ConsoleOutput.WriteLine("Failed to extract audio, using blank audio.", Color.Red);
                    startInfo = new()
                    {
                        FileName = ToolPaths.Ffmpeg(useSystemOnWindows: true),
                        Arguments = "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 -t 1 -acodec pcm_s16le -ar 44100 -ac 2 -f wav -y " + ExtAudio + "",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    process = new()
                    {
                        StartInfo = startInfo
                    };
                    process.ErrorDataReceived += (sender, args) => {
                        if(args.Data != null)
                            ConsoleOutput.WriteLine(args.Data, Color.Transparent);
                    };
                    if(worker.CancellationPending || !processing)
                        return;
                    ConsoleOutput.WriteLine($"> {startInfo.FileName} {startInfo.Arguments}", Color.Transparent);
                    process.Start();
                    if(worker.CancellationPending || !processing)
                        return;
                    process.BeginErrorReadLine();
                    if(worker.CancellationPending || !processing)
                        return;
                    process.WaitForExit();
                }
                // Load frames
                audioFrame = null;
                frames.Clear();
                if(worker.CancellationPending || !processing)
                    return;
                int curcount = Directory.GetFiles(ExtFrames).Length;
                count = curcount;
                for (int i = 1; i <= curcount; i++)
                {
                    if(worker.CancellationPending || !processing)
                        return;
                    Global.generator.progressText = L.T(0, "Video:StatusLoadFrame", i.ToString(CultureInfo.InvariantCulture), curcount.ToString(CultureInfo.InvariantCulture));
                    FileStream frameFile = File.OpenRead(Path.Combine(ExtFrames, i + ".bmp"));
                    if(UserInterface.instance != null)
                        frames.Add(Texture2D.FromStream(UserInterface.instance.GraphicsDevice, frameFile));
                    frameFile.Close();
                }
                // Load audio
                FileStream audioFile = File.OpenRead(ExtAudio);
                SoundEffect snd = SoundEffect.FromStream(audioFile);
                audio = snd.CreateInstance();
                //ScreenManager.PushNavigation("Video");
                //ScreenManager.GetScreen<VideoScreen>("Video")?.Show();
                audioFile.Close();
                if(worker.CancellationPending || !processing)
                    return;
            }
            catch(Exception e)
            {
                ConsoleOutput.WriteLine($"Failed to extract frames and audio.");
                ConsoleOutput.WriteLine(e.Message);
                Stop();
                GlobalContent.PlaySound("Error");
                Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
            }
            processing = false;
        }
        // Progress thread (checks directory size)
        public static void ProgressThread()
        {
            int dircount = 0;
            while(processing && !progressWorker.CancellationPending && dircount < count)
            {
                // Check directory size
                dircount = Directory.GetFiles(ExtFrames).Length;
                if(dircount > 0)
                {
                    Global.generator.progressText = L.T(0, "Video:StatusExtractFrames", dircount.ToString(CultureInfo.InvariantCulture), count.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    Global.generator.progressText = L.T(0, "Video:StatusExtractFrames", "0", count.ToString(CultureInfo.InvariantCulture));
                }
                System.Threading.Thread.Sleep(100);
            }
        }
        public static void CountThread()
        {
            // Get frame count with ffprobe
            try
            {
                if(countWorker.CancellationPending || !processing)
                    return;
                ProcessStartInfo startInfo = new()
                {
                    FileName = ToolPaths.Ffprobe(useSystemOnWindows: true),
                    Arguments = "-v error -count_frames -select_streams v:0 -show_entries stream=nb_read_frames -of default=nokey=1:noprint_wrappers=1 \"" + currentPath + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                Process process = new()
                {
                    StartInfo = startInfo
                };
                process.OutputDataReceived += (sender, args) => {
                    if(args.Data != null)
                    {
                        count = int.Parse(args.Data, CultureInfo.InvariantCulture);
                        ConsoleOutput.WriteLine(args.Data, Color.Transparent);
                    }
                };
                if(countWorker.CancellationPending || !processing)
                    return;
                ConsoleOutput.WriteLine($"> {startInfo.FileName} {startInfo.Arguments}", Color.Transparent);
                process.Start();
                if(countWorker.CancellationPending || !processing)
                    return;
                process.BeginOutputReadLine();
                if(countWorker.CancellationPending || !processing)
                    return;
                process.WaitForExit();
            }
            catch(Exception e)
            {
                ConsoleOutput.WriteLine($"Failed to get frame count.");
                ConsoleOutput.WriteLine(e.Message);
            }
        }
#if DESKTOPGL
        static readonly object previewGate = new();
        static int libraryPreviewGeneration;
        static string? libraryPreviewPath;
        static string? libraryPreviewError;

        /// <summary>
        /// Queues a DesktopGL-safe copy of a library video. Update starts the player.
        /// </summary>
        public static void PrepareLibraryPreview(string sourcePath)
        {
            int gen;
            lock (previewGate)
            {
                gen = ++libraryPreviewGeneration;
                libraryPreviewPath = null;
                libraryPreviewError = null;
            }
            string ffmpeg = ToolPaths.Ffmpeg(Global.useSystemFFmpeg);
            string ffprobe = ToolPaths.Ffprobe(Global.useSystemFFprobe);
            string cacheDir = Path.Combine(AppPaths.Writable, "cache", "play");
            string writable = AppPaths.Writable;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string playable = sourcePath;
                try
                {
                    string resolved = MediaPreview.ResolveInput(sourcePath, writable);
                    MediaGuard.Verdict verdict = MediaGuard.Inspect(resolved, ffprobe);
                    if (!verdict.Ok)
                    {
                        lock (previewGate)
                        {
                            if (gen != libraryPreviewGeneration)
                                return;
                            libraryPreviewError = verdict.Message;
                        }
                        return;
                    }
                    playable = MediaPreview.EnsurePlayable(ffmpeg, ffprobe, sourcePath, cacheDir, writable);
                    if (!MediaPreview.IsInsideDirectory(playable, cacheDir))
                        playable = VideoCache.GetCachePath(playable);
                }
                catch (Exception ex)
                {
                    ConsoleOutput.WriteLine("Library preview prepare failed: " + ex.Message, Color.Red);
                    playable = sourcePath;
                }
                lock (previewGate)
                {
                    if (gen != libraryPreviewGeneration)
                        return;
                    libraryPreviewPath = playable;
                }
            });
        }

        static void CancelLibraryPreview()
        {
            lock (previewGate)
            {
                libraryPreviewGeneration++;
                libraryPreviewPath = null;
                libraryPreviewError = null;
            }
        }

        static bool TryStartLibraryPreview()
        {
            string? ready;
            string? error;
            lock (previewGate)
            {
                ready = libraryPreviewPath;
                libraryPreviewPath = null;
                error = libraryPreviewError;
                libraryPreviewError = null;
            }
            if (!string.IsNullOrEmpty(error))
            {
                ConsoleOutput.WriteLine(error, Color.Red);
                Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                Global.videoPlaying = false;
                try
                {
                    GlobalContent.PlaySound("Error");
                }
                catch
                {
                }
                return false;
            }
            if (string.IsNullOrEmpty(ready))
                return false;
            if (UserInterface.instance == null || UserInterface.instance.videoPlayer == null)
            {
                lock (previewGate)
                {
                    if (libraryPreviewPath == null)
                        libraryPreviewPath = ready;
                }
                return false;
            }
            try
            {
                if (UserInterface.instance.video != null)
                {
                    UserInterface.instance.video.Dispose();
                    UserInterface.instance.video = null;
                }
                if (!MediaGuard.CanOpen(ready, ToolPaths.Ffprobe(Global.useSystemFFprobe), out string why))
                {
                    ConsoleOutput.WriteLine(why, Color.Red);
                    Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                    Global.videoPlaying = false;
                    UserInterface.instance.videoPath = "";
                    canPlayBgMusic = true;
                    return false;
                }
                if (!VideoLoad.TryLoad(ready, out var loaded, out _))
                {
                    UserInterface.instance.video = null;
                    UserInterface.instance.videoPath = "";
                    Global.videoPlaying = false;
                    canPlayBgMusic = true;
                    Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                    return false;
                }
                UserInterface.instance.videoPath = ready;
                UserInterface.instance.video = loaded;
                UserInterface.instance.videoPlayer.Play(loaded);
                UserInterface.instance.videoPlayer.Volume = float.Parse(SaveData.saveValues["VideoVolume"], CultureInfo.InvariantCulture) / 100f;
                Global.videoPlaying = true;
                canPlayBgMusic = false;
                Global.generator.progressText = L.T(0, "Video:StatusPlay");
                if (ScreenManager.GetScreen<VideoScreen>("Video") == null
                    || ScreenManager.GetScreen<VideoScreen>("Video")?.screenType == ScreenType.Hidden)
                {
                    ScreenManager.PushNavigation("Video");
                    ScreenManager.GetScreen<VideoScreen>("Video")?.Show();
                }
                return true;
            }
            catch (Exception ex)
            {
                ConsoleOutput.WriteLine("Library preview failed: " + ex.Message, Color.Red);
                Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                UserInterface.instance.video = null;
                UserInterface.instance.videoPath = "";
                Global.videoPlaying = false;
                canPlayBgMusic = true;
                return false;
            }
        }
#endif
        /// <summary>
        /// Stops the in-app video and hides its panel, so a new render does not
        /// run with the previous one still playing. The boot movie is left alone.
        /// </summary>
        public static void StopVideo()
        {
            UserInterface? ui = UserInterface.instance;
            if (ui == null || !ui.introFinished)
                return;
            bool wasShowing = Global.videoPlaying || ui.videoPath != "";
            Global.videoPlaying = false;
            ui.videoPath = "";
            try
            {
                if (ui.videoPlayer != null && ui.videoPlayer.State != MediaState.Stopped)
                {
                    ui.videoPlayer.Stop();
                    wasShowing = true;
                }
            }
            catch (Exception e)
            {
                ConsoleOutput.WriteLine("Failed to stop video: " + e.Message, Color.Red);
            }
            canPlayBgMusic = true;
            if (wasShowing)
            {
                ConsoleOutput.WriteLine("Stopped the playing video for a new render.", Color.Transparent);
                VideoScreen? screen = ScreenManager.GetScreen<VideoScreen>("Video");
                if (screen != null && screen.screenType != ScreenType.Hidden)
                    screen.Hide();
            }
        }
        public static bool Stop()
        {
#if DESKTOPGL
            CancelLibraryPreview();
#endif
            try
            {
                if(countWorker.IsBusy || progressWorker.IsBusy || worker.IsBusy)
                {
                    // Stop workers
                    audioConvertWorker.CancelAsync();
                    countWorker.CancelAsync();
                    progressWorker.CancelAsync();
                    worker.CancelAsync();
                    return false;
                }
                else
                {
                    processing = false;
                }
                audioPlaying = false;
                audioLength = 0;
                currentAudioTime = 0;
                playing = false;
                count = 0;
                currentFrame = 0;
                currentPath = "";
                timeStarted = 0;
                processing = false;
                startedProcessing = 0;
                canPlayBgMusic = true;
                // Unload frames
                foreach(Texture2D frame in frames)
                {
                    frame.Dispose();
                }
                frames.Clear();
                // Unload audio
                if(audio != null)
                {
                    if (audio != null)
                    {
                        audio.Stop();
                        audio.Dispose();
                    }
                    audio = null;
                    //ScreenManager.GetScreen<VideoScreen>("Video")?.Hide();
                    Global.generator.progressText = L.T(0, "Video:StatusStop");
                }
            }
            catch(Exception e)
            {
                ConsoleOutput.WriteLine($"Failed to stop playback.");
                ConsoleOutput.WriteLine(e.Message);
                return false;
            }
            return true;
        }
        public static void AudioConvertThread()
        {
            try
            {
                if(audioConvertWorker.CancellationPending || !processing)
                    return;
                ProcessStartInfo startInfo = new()
                {
                    FileName = ToolPaths.Ffmpeg(useSystemOnWindows: true),
                    Arguments = "-i \"" + currentPath + "\" -vn -acodec pcm_s16le -ar 44100 -ac 2 -f wav -y " + ExtAudio + "",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                Process process = new()
                {
                    StartInfo = startInfo
                };
                process.ErrorDataReceived += (sender, args) => {
                    if(args.Data != null)
                        ConsoleOutput.WriteLine(args.Data, Color.Transparent);
                };
                if(audioConvertWorker.CancellationPending || !processing)
                    return;
                ConsoleOutput.WriteLine($"> {startInfo.FileName} {startInfo.Arguments}", Color.Transparent);
                process.Start();
                if(audioConvertWorker.CancellationPending || !processing)
                    return;
                process.BeginErrorReadLine();
                if(audioConvertWorker.CancellationPending || !processing)
                    return;
                process.WaitForExit();
                if(!File.Exists(ExtAudio))
                {
                    throw new Exception("Failed to convert audio.");
                }
                // Unload audio
                if(audio != null)
                {
                    audio.Stop();
                    audio.Dispose();
                    audio = null;
                    //ScreenManager.GetScreen<VideoScreen>("Video")?.Hide();
                }
                FileStream audioFile = File.OpenRead(ExtAudio);
                SoundEffect snd = SoundEffect.FromStream(audioFile);
                audio = snd.CreateInstance();
                audioFile.Close();
                currentAudioTime = 0;
                audioLength = snd.Duration.TotalMilliseconds;
                canPlayBgMusic = false;
                Process waveProcess = Generator.GenerateThumbnail(currentPath, ExtAudioBmp, LibraryRootType.Audio, 100, 78);
                // Defer until the process is finished
                while(!waveProcess.HasExited)
                {
                    System.Threading.Thread.Sleep(100);
                }
                audioFrame = null;
                FileStream audioFrameFile = File.OpenRead(ExtAudioBmp);
                if(UserInterface.instance != null)
                    audioFrame = Texture2D.FromStream(UserInterface.instance.GraphicsDevice, audioFrameFile);
                audioFrameFile.Close();
                if(SaveData.saveValues["UseExternalVideoPlayer"] == "false")
                {
                    audio.Volume = float.Parse(SaveData.saveValues["VideoVolume"], CultureInfo.InvariantCulture) / 100f;
                    audio.Play();
                    //ScreenManager.PushNavigation("Video");
                    //ScreenManager.GetScreen<VideoScreen>("Video")?.Show();
                    audioPlaying = true;
                    Global.generator.progressText = L.T(0, "Video:StatusPlay");
                }
                else
                {
                    audio.Stop();
                    audioPlaying = false;
                }
            }
            catch(Exception e)
            {
                ConsoleOutput.WriteLine($"Failed to convert audio.");
                ConsoleOutput.WriteLine(e.Message);
            }
            processing = false;
        }
        public static void PlayMedia(LibraryFile file)
        {
            string mediaPath = file.Path ?? "";
            if (!MediaGuard.CanOpen(mediaPath, ToolPaths.Ffprobe(Global.useSystemFFprobe), out string rejected))
            {
                ConsoleOutput.WriteLine(rejected, Color.Red);
                Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                try
                {
                    GlobalContent.PlaySound("Error");
                }
                catch
                {
                }
                return;
            }
            if(SaveData.saveValues["UseExternalVideoPlayer"] == "true")
            {
                try
                {
                    ProcessStartInfo startInfo = new()
                    {
                        FileName = file.Path,
                        UseShellExecute = true
                    };
                    Process process = new()
                    {
                        StartInfo = startInfo
                    };
                    ConsoleOutput.WriteLine($"> {startInfo.FileName} {startInfo.Arguments}", Color.Transparent);
                    process.Start();
                }
                catch(Exception e)
                {
                    ConsoleOutput.WriteLine($"Failed to open external video player.", Color.Red);
                    ConsoleOutput.WriteLine(e.Message, Color.Red);
                    GlobalContent.PlaySound("Error");
                    Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                }
                return;
            }
            // If file is identical to current file, loop
            if(currentPath == file.Path)
            {
                if(audio != null)
                {
                    if(!audioPlaying && frames.Count == 0)
                    {
                        audio.Volume = float.Parse(SaveData.saveValues["VideoVolume"], CultureInfo.InvariantCulture) / 100f;
                        audio.Play();
                        Global.generator.progressText = L.T(0, "Video:StatusPlay");
                        canPlayBgMusic = false;
                        audioPlaying = true;
                    }
                    else
                    {
                        audio.Stop();
                        currentAudioTime = 0;
                        audioPlaying = false;
                        canPlayBgMusic = true;
                        Global.generator.progressText = L.T(0, "Video:StatusStop");
                    }
                }
                playing = false;
                return;
            }
            if(Stop())
            {
                Global.generator.progressText = L.T(0, "Video:StatusLoad");
                currentPath = file.Path ?? "";
                // Extract frames and audio
                if(!processing)
                {
                    if(file.Type != null && file.Type.RootType == LibraryRootType.Video)
                    {
                        // Run ffmpeg to extract frames and audio to .\temp\extracted\frames\* and .\temp\extracted\audio.wav
                        // Create the directory if it doesn't exist
                        if (!Directory.Exists(ExtFrames))
                        {
                            Directory.CreateDirectory(ExtFrames);
                        }
                        currentAudioTime = 0;
                        audioLength = 0;
                        processing = true;
                        startedProcessing = 0;
                        worker = new();
                        worker.WorkerSupportsCancellation = true;
                        worker.DoWork += (sender, args) => ExtractFramesAndAudio();
                        worker.RunWorkerAsync();
                        countWorker = new();
                        countWorker.WorkerSupportsCancellation = true;
                        countWorker.DoWork += (sender, args) => CountThread();
                        countWorker.RunWorkerCompleted += (sender, args) => {
                            progressWorker = new();
                            progressWorker.WorkerSupportsCancellation = true;
                            progressWorker.DoWork += (sender, args) => ProgressThread();
                            progressWorker.RunWorkerAsync();
                        };
                        countWorker.RunWorkerAsync();
                    }
                    else
                    {
                        try
                        {
                            if(file.Type == null)
                                throw new Exception("Invalid file type???");
                            // Create the directory if it doesn't exist
                            if (!Directory.Exists(ExtRoot))
                            {
                                Directory.CreateDirectory(ExtRoot);
                            }
                            currentAudioTime = 0;
                            audioLength = 0;
                            processing = true;
                            startedProcessing = 0;
                            audioConvertWorker = new();
                            audioConvertWorker.WorkerSupportsCancellation = true;
                            audioConvertWorker.DoWork += (sender, args) => AudioConvertThread();
                            audioConvertWorker.RunWorkerAsync();
                        }
                        catch(Exception e)
                        {
                            ConsoleOutput.WriteLine($"Failed to play media.");
                            ConsoleOutput.WriteLine(e.Message);
                            GlobalContent.PlaySound("Error");
                            Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                        }
                    }
                }
            }
            else
            {
                GlobalContent.PlaySound("Error");
                Global.generator.progressText = L.T(0, "Video:StatusFailStop");
            }
        }
        public static void Update(GameTime gameTime)
        {
#if DESKTOPGL
            if (Global.exiting)
                PlatformFileDialog.CancelActive();
            LibraryPage.PumpBackground();
#endif
            if (SaveData.saveValues["UseExternalVideoPlayer"] == "false")
            {
                bool startedLibraryPreview = false;
#if DESKTOPGL
                startedLibraryPreview = TryStartLibraryPreview();
#endif
                if(!startedLibraryPreview && Global.videoPlaying && UserInterface.instance != null && UserInterface.instance.videoPlayer != null && UserInterface.instance.videoPath != "" && UserInterface.instance.videoPlayer.State == MediaState.Stopped)
                {
                    Stop();
                    canPlayBgMusic = false;
                    UserInterface.instance.videoPlayer.Dispose();
                    UserInterface.instance.videoPlayer = null;
                    UserInterface.instance.videoPlayer = new MonoGame.Extended.Framework.Media.VideoPlayer(UserInterface.instance.GraphicsDevice);
                    if (UserInterface.instance.video != null)
                    {
                        UserInterface.instance.video.Dispose();
                        UserInterface.instance.video = null;
                    }
                    if (!MediaGuard.CanOpen(UserInterface.instance.videoPath, ToolPaths.Ffprobe(Global.useSystemFFprobe), out string replayWhy))
                    {
                        ConsoleOutput.WriteLine(replayWhy, Color.Red);
                        Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                        UserInterface.instance.videoPath = "";
                        Global.videoPlaying = false;
                        canPlayBgMusic = true;
                    }
                    else if (!VideoLoad.TryLoad(UserInterface.instance.videoPath, out var loaded, out _))
                    {
                        UserInterface.instance.video = null;
                        UserInterface.instance.videoPath = "";
                        Global.videoPlaying = false;
                        canPlayBgMusic = true;
                        Global.generator.progressText = L.T(0, "Video:StatusFailPlay");
                    }
                    else
                    {
                        UserInterface.instance.video = loaded;
                        UserInterface.instance.videoPlayer.Play(loaded);
                        UserInterface.instance.videoPlayer.Volume = float.Parse(SaveData.saveValues["VideoVolume"], CultureInfo.InvariantCulture) / 100f;
                        if (ScreenManager.GetScreen<VideoScreen>("Video") == null
                            || ScreenManager.GetScreen<VideoScreen>("Video")?.screenType == ScreenType.Hidden)
                        {
                            ScreenManager.PushNavigation("Video");
                            ScreenManager.GetScreen<VideoScreen>("Video")?.Show();
                        }
                    }
                }
                if(audioLength > 0 && currentAudioTime < audioLength && audioPlaying)
                {
                    currentAudioTime += gameTime.ElapsedGameTime.TotalMilliseconds;
                    if(currentAudioTime > audioLength)
                    {
                        if(audio != null)
                            audio.Stop();
                        currentAudioTime = 0;
                        audioPlaying = false;
                        canPlayBgMusic = true;
                        Global.generator.progressText = L.T(0, "Video:StatusStop");
                    }
                }
                if(frames.Count > 0 && !playing && audio != null && !processing)
                {
                    Global.generator.progressText = L.T(0, "Video:StatusPlay");
                    timeStarted = gameTime.TotalGameTime.TotalSeconds;
                    canPlayBgMusic = false;
                    audio.Volume = float.Parse(SaveData.saveValues["VideoVolume"], CultureInfo.InvariantCulture) / 100f;
                    audio.Play();
                    audio.Volume = int.Parse(SaveData.saveValues["VideoVolume"], CultureInfo.InvariantCulture) / 100f;
                    currentFrame = 0;
                    playing = true;
                }
                else if(processing && startedProcessing == 0)
                {
                    startedProcessing = gameTime.TotalGameTime.TotalSeconds;
                }
                if(playing)
                {
                    if(currentFrame >= 0 && currentFrame < frames.Count)
                    {
                        // Update frame
                        currentFrame = (int)((gameTime.TotalGameTime.TotalSeconds - timeStarted) * fps);
                        if(currentFrame >= frames.Count)
                        {
                            if(audio != null)
                                audio.Stop();
                            playing = false;
                        }
                    }
                    // Update volume on the fly
                    if(audio != null)
                    {
                        audio.Volume = int.Parse(SaveData.saveValues["VideoVolume"], CultureInfo.InvariantCulture) / 100f;
                    }
                }
            }
        }
    }
}
