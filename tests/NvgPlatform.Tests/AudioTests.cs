using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

public sealed class AudioTests
{
    [Fact]
    public void First_song_uses_last_track_or_the_random_index()
    {
        Assert.Equal(2, AudioDiagnostics.ChooseTrack(true, "2", 0, 5, 4));
        Assert.Equal(4, AudioDiagnostics.ChooseTrack(true, "-1", 0, 5, 4));
        Assert.Equal(1, AudioDiagnostics.ChooseTrack(true, "nope", 0, 5, 1));
        Assert.Equal(0, AudioDiagnostics.ChooseTrack(true, "9", 0, 5, 0));
        Assert.Equal(0, AudioDiagnostics.ChooseTrack(true, "-1", 0, 0, 3));
    }

    [Fact]
    public void Later_songs_advance_and_wrap()
    {
        Assert.Equal(3, AudioDiagnostics.ChooseTrack(false, "0", 2, 5, 4));
        Assert.Equal(0, AudioDiagnostics.ChooseTrack(false, "4", 4, 5, 4));
        Assert.Equal(0, AudioDiagnostics.NextIndex(-1, 5));
        Assert.Equal(0, AudioDiagnostics.NextIndex(4, 5));
        Assert.Equal(1, AudioDiagnostics.NextIndex(0, 5));
    }

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
