using System.Drawing;
using GridLookout.UI;
using Xunit;

namespace GridLookout.Tests.UI;

/// <summary>
/// Gitea #7: a wall rebuild (layout-token change) re-creates every WallForm; these cover the pure
/// decision of what compact placement the replacement window for each monitor inherits. The
/// WinForms half (WallForm.CaptureCompactSnapshot/RestoreCompact) is not driven here.
/// </summary>
public class CompactWindowCarryOverTests
{
    private static readonly Rectangle Moved = new(2944, 216, 1152, 648);

    [Fact]
    public void Capture_FullscreenWindows_CarryNothing()
    {
        var carried = CompactWindowCarryOver.Capture(new CompactWindowSnapshot?[] { null, null });

        Assert.Empty(carried);
        Assert.Null(CompactWindowCarryOver.ForMonitor(carried, 1, kioskLock: false));
    }

    [Fact]
    public void ForMonitor_CompactWindow_CarriesItsExactPlacementToTheSameMonitor()
    {
        var snapshot = new CompactWindowSnapshot(2, Moved, CompactShowState.Normal);
        var carried = CompactWindowCarryOver.Capture(new CompactWindowSnapshot?[] { null, snapshot });

        var result = CompactWindowCarryOver.ForMonitor(carried, 2, kioskLock: false);

        Assert.NotNull(result);
        Assert.Equal(Moved, result!.Bounds);
        Assert.Equal(CompactShowState.Normal, result.ShowState);
    }

    [Fact]
    public void ForMonitor_OtherMonitorStaysFullscreen()
    {
        var carried = CompactWindowCarryOver.Capture(new CompactWindowSnapshot?[] { new(2, Moved, CompactShowState.Normal) });

        Assert.Null(CompactWindowCarryOver.ForMonitor(carried, 1, kioskLock: false));
        // A monitor that disappeared from the token set simply has no new window to apply to;
        // a monitor that is new in the token set has no snapshot and starts fullscreen.
        Assert.Null(CompactWindowCarryOver.ForMonitor(carried, 3, kioskLock: false));
    }

    [Fact]
    public void ForMonitor_KioskLock_NeverCarriesCompact()
    {
        var carried = CompactWindowCarryOver.Capture(new CompactWindowSnapshot?[] { new(1, Moved, CompactShowState.Normal) });

        Assert.Null(CompactWindowCarryOver.ForMonitor(carried, 1, kioskLock: true));
    }

    [Fact]
    public void Capture_DuplicateMonitor_FirstWins()
    {
        var first = new CompactWindowSnapshot(1, Moved, CompactShowState.Normal);
        var second = new CompactWindowSnapshot(1, new Rectangle(0, 0, 10, 10), CompactShowState.Maximized);

        var carried = CompactWindowCarryOver.Capture(new CompactWindowSnapshot?[] { first, second });

        Assert.Same(first, carried[1]);
    }

    [Fact]
    public void ChooseBounds_NormalWindow_UsesLiveBounds()
    {
        var restore = new Rectangle(1, 2, 3, 4);

        Assert.Equal(Moved, CompactWindowCarryOver.ChooseBounds(CompactShowState.Normal, Moved, restore));
    }

    [Theory]
    [InlineData(CompactShowState.Maximized)]
    [InlineData(CompactShowState.Minimized)]
    public void ChooseBounds_MaximizedOrMinimized_UsesRestoreBounds(CompactShowState state)
    {
        var parked = new Rectangle(-32000, -32000, 160, 28);

        Assert.Equal(Moved, CompactWindowCarryOver.ChooseBounds(state, parked, Moved));
    }
}
