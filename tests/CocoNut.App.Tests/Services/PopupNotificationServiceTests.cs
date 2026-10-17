using Avalonia;
using CocoNut.App.Services;

namespace CocoNut.App.Tests.Services;

public class PopupNotificationServiceTests
{
    // Popup size/margin in DIPs, mirroring PopupNotificationService's private constants.
    private const int Width = 320;
    private const int Height = 88;
    private const int Margin = 12;

    [Fact]
    public void No_popups_yields_no_positions() =>
        Assert.Empty(PopupNotificationService.ComputeStackPositions(new PixelRect(0, 0, 1920, 1040), 1, 0));

    [Fact]
    public void At_100_percent_the_newest_popup_sits_in_the_bottom_right_corner()
    {
        var workArea = new PixelRect(0, 0, 1920, 1040);

        var position = Assert.Single(PopupNotificationService.ComputeStackPositions(workArea, 1, 1));

        Assert.Equal(new PixelPoint(1920 - Width - Margin, 1040 - Height - Margin), position);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void On_a_scaled_screen_the_whole_popup_stays_inside_the_work_area(double scaling)
    {
        var workArea = new PixelRect(0, 0, 3840, 2100);

        var position = Assert.Single(PopupNotificationService.ComputeStackPositions(workArea, scaling, 1));

        var right = position.X + (int)Math.Ceiling(Width * scaling);
        var bottom = position.Y + (int)Math.Ceiling(Height * scaling);
        Assert.True(right <= workArea.Right, $"right edge {right} exceeds {workArea.Right}");
        Assert.True(bottom <= workArea.Bottom, $"bottom edge {bottom} exceeds {workArea.Bottom}");
    }

    [Fact]
    public void A_primary_screen_offset_in_a_multi_monitor_layout_is_respected()
    {
        // Primary to the right of a secondary monitor, so its work area does not start at the origin.
        var workArea = new PixelRect(2560, 0, 3840, 2100);

        var position = Assert.Single(PopupNotificationService.ComputeStackPositions(workArea, 2, 1));

        Assert.Equal(new PixelPoint(workArea.Right - 2 * (Width + Margin), workArea.Bottom - 2 * (Height + Margin)), position);
    }

    [Fact]
    public void Older_popups_stack_upward_above_the_newest()
    {
        var workArea = new PixelRect(0, 0, 1920, 1040);

        var positions = PopupNotificationService.ComputeStackPositions(workArea, 1, 3);

        Assert.Equal(3, positions.Length);
        Assert.All(positions, p => Assert.Equal(positions[0].X, p.X));
        Assert.Equal(1040 - 3 * (Height + Margin), positions[0].Y);
        Assert.Equal(1040 - 2 * (Height + Margin), positions[1].Y);
        Assert.Equal(1040 - 1 * (Height + Margin), positions[2].Y);
    }

    [Fact]
    public void A_stack_taller_than_the_screen_is_clamped_to_the_top_of_the_work_area()
    {
        var workArea = new PixelRect(0, 40, 800, 400);

        var positions = PopupNotificationService.ComputeStackPositions(workArea, 1, 10);

        Assert.All(positions, p => Assert.True(p.Y >= workArea.Y));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void An_invalid_scaling_falls_back_to_100_percent(double scaling)
    {
        var workArea = new PixelRect(0, 0, 1920, 1040);

        Assert.Equal(
            PopupNotificationService.ComputeStackPositions(workArea, 1, 2),
            PopupNotificationService.ComputeStackPositions(workArea, scaling, 2));
    }
}
