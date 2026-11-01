using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

public sealed class AudioTests
{
    [Fact]
    public void Output_note_names_the_macos_device_and_stays_generic_on_linux()
    {
        Assert.Equal(
            "Audio output: Dime 3 (macOS default playback device).",
            AudioDiagnostics.FormatOutputNote("macos", "Dime 3"));
        Assert.Contains("unavailable", AudioDiagnostics.FormatOutputNote("macos", "  "));
        Assert.Equal(
            "Audio output: OpenAL default playback device.",
            AudioDiagnostics.FormatOutputNote("linux", null));
        Assert.Equal(
            "Audio output: default playback device.",
            AudioDiagnostics.FormatOutputNote("windows", "Speakers"));
    }

    [Fact]
    public void Default_output_query_does_not_throw()
    {
        string? name = AudioDiagnostics.DefaultOutputDeviceName();
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Null(name);
            return;
        }
        Assert.False(string.IsNullOrWhiteSpace(name));
    }
}
