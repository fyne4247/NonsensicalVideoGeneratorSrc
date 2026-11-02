using NonsensicalVideoGenerator;

namespace NvgPlatform.Tests;

public sealed class DesktopFullscreenTests
{
    static DesktopFullscreen.DisplayBounds Box(int x, int y, int width, int height)
    {
        return new DesktopFullscreen.DisplayBounds(x, y, width, height);
    }

    [Fact]
    public void Window_on_the_second_display_does_not_pick_the_primary()
    {
        var displays = new[]
        {
            Box(0, 0, 1920, 1080),
            Box(1920, 0, 1440, 900),
        };
        Assert.Equal(1, DesktopFullscreen.ChooseIndex(2000, 40, 640, 480, displays));
    }

    [Fact]
    public void The_larger_overlap_wins_when_the_window_straddles()
    {
        var displays = new[]
        {
            Box(0, 0, 1000, 1000),
            Box(1000, 0, 1000, 1000),
        };
        Assert.Equal(1, DesktopFullscreen.ChooseIndex(900, 0, 400, 100, displays));
        Assert.Equal(0, DesktopFullscreen.ChooseIndex(700, 0, 400, 100, displays));
    }

    [Fact]
    public void A_point_outside_every_display_uses_the_nearest_center()
    {
        var displays = new[]
        {
            Box(0, 0, 1000, 1000),
            Box(2000, 0, 1000, 1000),
        };
        Assert.Equal(1, DesktopFullscreen.ChooseIndex(2400, 10, 0, 0, displays));
        Assert.Equal(-1, DesktopFullscreen.ChooseIndex(0, 0, 10, 10, Array.Empty<DesktopFullscreen.DisplayBounds>()));
    }
}
