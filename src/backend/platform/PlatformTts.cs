#if DESKTOPGL
namespace NonsensicalVideoGenerator
{
    public enum SynthesizerState
    {
        Ready,
        Speaking,
    }

    /// <summary>
    /// Experimental stub. Narration is not wired for the feasibility trial.
    /// Calls do not throw when no speech service is installed.
    /// </summary>
    public sealed class PlatformTts
    {
        public SynthesizerState State { get; private set; } = SynthesizerState.Ready;

        public void SetOutputToDefaultAudioDevice()
        {
        }

        public void SpeakAsync(string text)
        {
            State = SynthesizerState.Ready;
            ConsoleOutput.WriteLine("TTS stub (DesktopGL): " + text, Microsoft.Xna.Framework.Color.Gray);
        }

        public void SpeakAsyncCancelAll()
        {
            State = SynthesizerState.Ready;
        }
    }
}
#endif
