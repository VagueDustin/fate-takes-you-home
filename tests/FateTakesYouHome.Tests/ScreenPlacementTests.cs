using System.Windows;
using FateTakesYouHome.Interop;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// The maths behind anchoring the tray panel.
/// </summary>
/// <remarks>
/// Worth testing precisely because it is impossible to eyeball: the failures are a panel a few
/// pixels under the taskbar, or off the edge of a secondary monitor, and both look like "it
/// mostly works" until somebody has the taskbar on the left.
/// </remarks>
public sealed class ScreenPlacementTests
{
    private static readonly MonitorGeometry Fhd = new(
        Bounds: new Int32Rect(0, 0, 1920, 1080),
        WorkArea: new Int32Rect(0, 0, 1920, 1032),
        Dpi: 96);

    private static TaskbarInfo Bar(TaskbarEdge edge, bool autoHide = false) => edge switch
    {
        TaskbarEdge.Bottom => new TaskbarInfo(edge, new Int32Rect(0, 1032, 1920, 48), autoHide),
        TaskbarEdge.Top => new TaskbarInfo(edge, new Int32Rect(0, 0, 1920, 48), autoHide),
        TaskbarEdge.Left => new TaskbarInfo(edge, new Int32Rect(0, 0, 48, 1080), autoHide),
        _ => new TaskbarInfo(edge, new Int32Rect(1872, 0, 48, 1080), autoHide),
    };

    private static readonly Int32Rect PanelSize = new(0, 0, 368, 400);

    [Fact]
    public void ABottomTaskbarPutsThePanelAboveIt()
    {
        // A tray icon near the right-hand end of a bottom taskbar.
        var anchor = new Int32Rect(1700, 1040, 24, 24);

        FlyoutPlacement placement = ScreenPlacement.Compute(
            anchor, PanelSize, Bar(TaskbarEdge.Bottom), Fhd, marginPx: 12);

        Assert.Equal(1032 - 12 - 400, placement.Bounds.Y);
        Assert.Equal(1, placement.SlideOffsetY);
        Assert.Equal(0, placement.SlideOffsetX);
    }

    [Fact]
    public void ThePanelIsCentredOnTheTrayIcon()
    {
        var anchor = new Int32Rect(1000, 1040, 24, 24);

        FlyoutPlacement placement = ScreenPlacement.Compute(
            anchor, PanelSize, Bar(TaskbarEdge.Bottom), Fhd, marginPx: 12);

        int anchorCentre = 1000 + 12;
        int panelCentre = placement.Bounds.X + (placement.Bounds.Width / 2);

        Assert.Equal(anchorCentre, panelCentre);
    }

    [Fact]
    public void ATopTaskbarPutsThePanelBelowItAndReversesTheTravel()
    {
        var top = new MonitorGeometry(
            new Int32Rect(0, 0, 1920, 1080), new Int32Rect(0, 48, 1920, 1032), 96);

        FlyoutPlacement placement = ScreenPlacement.Compute(
            new Int32Rect(1700, 10, 24, 24), PanelSize, Bar(TaskbarEdge.Top), top, marginPx: 12);

        Assert.Equal(48 + 12, placement.Bounds.Y);
        Assert.Equal(-1, placement.SlideOffsetY);
    }

    [Fact]
    public void ALeftTaskbarPutsThePanelBesideItAndTravelsSideways()
    {
        var left = new MonitorGeometry(
            new Int32Rect(0, 0, 1920, 1080), new Int32Rect(48, 0, 1872, 1080), 96);

        FlyoutPlacement placement = ScreenPlacement.Compute(
            new Int32Rect(10, 900, 24, 24), PanelSize, Bar(TaskbarEdge.Left), left, marginPx: 12);

        Assert.Equal(48 + 12, placement.Bounds.X);
        Assert.Equal(-1, placement.SlideOffsetX);
        Assert.Equal(0, placement.SlideOffsetY);
    }

    [Fact]
    public void ARightTaskbarPutsThePanelAgainstTheRightEdge()
    {
        var right = new MonitorGeometry(
            new Int32Rect(0, 0, 1920, 1080), new Int32Rect(0, 0, 1872, 1080), 96);

        FlyoutPlacement placement = ScreenPlacement.Compute(
            new Int32Rect(1880, 900, 24, 24), PanelSize, Bar(TaskbarEdge.Right), right, marginPx: 12);

        Assert.Equal(1872 - 12 - 368, placement.Bounds.X);
        Assert.Equal(1, placement.SlideOffsetX);
    }

    /// <summary>
    /// A tray icon at the extreme right must not push the panel off the screen.
    /// </summary>
    [Fact]
    public void ThePanelIsClampedInsideTheWorkArea()
    {
        FlyoutPlacement placement = ScreenPlacement.Compute(
            new Int32Rect(1910, 1040, 10, 24), PanelSize, Bar(TaskbarEdge.Bottom), Fhd, marginPx: 12);

        Assert.True(
            placement.Bounds.X + placement.Bounds.Width <= 1920 - 12,
            $"Panel right edge {placement.Bounds.X + placement.Bounds.Width} is off screen.");

        Assert.True(placement.Bounds.X >= 12, "Panel left edge is off screen.");
    }

    [Fact]
    public void APanelTallerThanTheScreenIsPinnedToTheTopRatherThanScrolledOff()
    {
        var tall = new Int32Rect(0, 0, 368, 5000);

        FlyoutPlacement placement = ScreenPlacement.Compute(
            new Int32Rect(1700, 1040, 24, 24), tall, Bar(TaskbarEdge.Bottom), Fhd, marginPx: 12);

        Assert.Equal(12, placement.Bounds.Y);
    }

    /// <summary>
    /// An auto-hidden taskbar leaves the work area covering the whole screen, so the bar's own
    /// thickness has to be reserved or the panel sits underneath it the moment it reveals.
    /// </summary>
    [Fact]
    public void AnAutoHiddenTaskbarStillGetsItsSpaceReserved()
    {
        var fullScreenWorkArea = new MonitorGeometry(
            new Int32Rect(0, 0, 1920, 1080), new Int32Rect(0, 0, 1920, 1080), 96);

        FlyoutPlacement pinned = ScreenPlacement.Compute(
            new Int32Rect(1700, 1070, 24, 10),
            PanelSize,
            Bar(TaskbarEdge.Bottom, autoHide: true),
            fullScreenWorkArea,
            marginPx: 12);

        FlyoutPlacement notPinned = ScreenPlacement.Compute(
            new Int32Rect(1700, 1070, 24, 10),
            PanelSize,
            Bar(TaskbarEdge.Bottom),
            fullScreenWorkArea,
            marginPx: 12);

        Assert.True(
            pinned.Bounds.Y < notPinned.Bounds.Y,
            "An auto-hidden taskbar should push the panel further up than a pinned one.");
    }

    [Fact]
    public void PlacementIsRelativeToTheGivenMonitorNotTheOrigin()
    {
        // A second monitor sitting to the right of the primary.
        var second = new MonitorGeometry(
            new Int32Rect(1920, 0, 2560, 1440), new Int32Rect(1920, 0, 2560, 1392), 144);

        FlyoutPlacement placement = ScreenPlacement.Compute(
            new Int32Rect(4400, 1400, 24, 24),
            PanelSize,
            new TaskbarInfo(TaskbarEdge.Bottom, new Int32Rect(1920, 1392, 2560, 48), false),
            second,
            marginPx: 12);

        Assert.True(placement.Bounds.X >= 1920, "Panel landed on the wrong monitor.");
        Assert.Equal(1392 - 12 - 400, placement.Bounds.Y);
    }

    [Theory]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 1.5, 150)]
    [InlineData(12.4, 1.0, 13)]
    public void PhysicalConversionRoundsUpToWholePixels(double dips, double scale, int expected)
    {
        Assert.Equal(expected, ScreenPlacement.ToPhysical(dips, scale));
    }
}
