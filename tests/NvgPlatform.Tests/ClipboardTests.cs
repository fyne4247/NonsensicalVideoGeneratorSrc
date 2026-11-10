using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

[Collection("nvg")]
public sealed class ClipboardTests
{
    [Fact]
    public void Sdl_clipboard_library_exports_get_set_and_free()
    {
        Assert.True(PlatformClipboard.TryEnsureLoaded(), "SDL2 did not load. Clipboard needs SDL_GetClipboardText, SDL_SetClipboardText, and SDL_free.");
    }
}
