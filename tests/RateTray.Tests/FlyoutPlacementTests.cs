using System.Drawing;
using RateTray.Ui;
using static RateTray.Ui.FlyoutPlacement;

namespace RateTray.Tests;

/// <summary>
/// The fly-out has to land in the taskbar corner and stay fully on screen — on a 4K panel as
/// well as on 1080p, and whichever edge the taskbar is docked to.
/// </summary>
public class FlyoutPlacementTests
{
    private static readonly Rectangle Hd = new(0, 0, 1920, 1080);
    private static readonly Rectangle Uhd = new(0, 0, 3840, 2160);

    [Fact]
    public void Detects_a_taskbar_at_the_bottom()
    {
        Assert.Equal(Edge.Bottom, EdgeOf(new Rectangle(0, 1032, 1920, 48), Hd));
    }

    [Fact]
    public void Detects_a_taskbar_at_the_top()
    {
        Assert.Equal(Edge.Top, EdgeOf(new Rectangle(0, 0, 1920, 48), Hd));
    }

    [Fact]
    public void Detects_a_taskbar_on_the_left()
    {
        Assert.Equal(Edge.Left, EdgeOf(new Rectangle(0, 0, 62, 1080), Hd));
    }

    [Fact]
    public void Detects_a_taskbar_on_the_right()
    {
        Assert.Equal(Edge.Right, EdgeOf(new Rectangle(1858, 0, 62, 1080), Hd));
    }

    [Fact]
    public void Bottom_taskbar_anchors_the_flyout_to_the_bottom_right()
    {
        var work = new Rectangle(0, 0, 1920, 1032);
        var size = new Size(560, 400);

        var point = Locate(size, work, Edge.Bottom, 12);

        Assert.Equal(new Point(1920 - 560 - 12, 1032 - 400 - 12), point);
    }

    [Fact]
    public void Top_taskbar_anchors_the_flyout_to_the_top_right()
    {
        var work = new Rectangle(0, 48, 1920, 1032);

        var point = Locate(new Size(560, 400), work, Edge.Top, 12);

        Assert.Equal(new Point(1920 - 560 - 12, 48 + 12), point);
    }

    [Fact]
    public void Left_taskbar_anchors_the_flyout_to_the_bottom_left()
    {
        var work = new Rectangle(62, 0, 1858, 1080);

        var point = Locate(new Size(560, 400), work, Edge.Left, 12);

        Assert.Equal(new Point(62 + 12, 1080 - 400 - 12), point);
    }

    [Fact]
    public void Placement_on_a_secondary_screen_uses_that_screen_s_coordinates()
    {
        // A monitor to the right of the primary one starts at a non-zero X.
        var work = new Rectangle(1920, 0, 2560, 1392);

        var point = Locate(new Size(560, 400), work, Edge.Bottom, 12);

        Assert.Equal(new Point(1920 + 2560 - 560 - 12, 1392 - 400 - 12), point);
    }

    [Theory]
    [InlineData(1920, 1032)]
    [InlineData(3840, 2112)]
    public void The_flyout_never_leaves_the_work_area(int width, int height)
    {
        var work = new Rectangle(0, 0, width, height);
        var size = new Size(840, 620);          // 560x400 at 150 % scaling

        var point = Locate(size, work, Edge.Bottom, 18);

        Assert.True(point.X >= work.Left);
        Assert.True(point.Y >= work.Top);
        Assert.True(point.X + size.Width <= work.Right);
        Assert.True(point.Y + size.Height <= work.Bottom);
    }

    [Fact]
    public void A_window_taller_than_the_screen_is_pinned_to_the_top_left_instead_of_off_screen()
    {
        var work = new Rectangle(0, 0, 400, 300);

        var point = Locate(new Size(800, 900), work, Edge.Bottom, 12);

        Assert.Equal(new Point(0, 0), point);
    }

    [Fact]
    public void Fit_caps_the_window_to_the_work_area()
    {
        var work = new Rectangle(0, 0, 1920, 1032);

        Assert.Equal(new Size(560, 400), Fit(new Size(560, 400), work, 12));
        Assert.Equal(new Size(560, 1008), Fit(new Size(560, 4000), work, 12));
    }

    [Fact]
    public void Fit_stays_positive_even_on_an_absurdly_small_work_area()
    {
        var size = Fit(new Size(560, 400), new Rectangle(0, 0, 10, 10), 12);

        Assert.True(size.Width > 0);
        Assert.True(size.Height > 0);
    }

    [Fact]
    public void A_4k_flyout_is_proportional_to_its_hd_counterpart()
    {
        // 200 % scaling doubles every metric; the anchor distance has to double with it.
        var hd = Locate(new Size(560, 400), new Rectangle(0, 0, 1920, 1032), Edge.Bottom, 12);
        var uhd = Locate(new Size(1120, 800), new Rectangle(0, 0, 3840, 2064), Edge.Bottom, 24);

        Assert.Equal((1920 - hd.X) * 2, 3840 - uhd.X);
        Assert.Equal((1032 - hd.Y) * 2, 2064 - uhd.Y);
    }

    [Fact]
    public void Uhd_screen_is_recognised_the_same_way_as_hd()
    {
        Assert.Equal(Edge.Bottom, EdgeOf(new Rectangle(0, 2064, 3840, 96), Uhd));
    }
}
