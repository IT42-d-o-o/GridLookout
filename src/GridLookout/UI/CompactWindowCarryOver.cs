using System.Drawing;

namespace GridLookout.UI;

/// <summary>How a compact (operator-windowed) wall window was shown when it was captured.
/// Deliberately our own enum rather than <c>System.Windows.Forms.FormWindowState</c> so this file
/// stays WinForms-free and unit-testable.</summary>
public enum CompactShowState
{
    Normal,
    Maximized,
    Minimized,
}

/// <summary>Everything a wall rebuild must carry from an old compact window to its replacement on
/// the same monitor: where the operator left it (restored bounds, i.e. including any manual move
/// or resize after the double-click) and whether it was maximized/minimized.</summary>
public sealed record CompactWindowSnapshot(int Monitor, Rectangle Bounds, CompactShowState ShowState);

/// <summary>
/// Gitea #7: every "rebuilding grids without restart" goes through Program.RebuildWall, which
/// builds a brand-new <c>WallForm</c> set (each constructor starts in the fullscreen kiosk posture)
/// and then closes the old set - so a window the operator had double-clicked into compact mode
/// snapped back to fullscreen on every layout-token change. The fix captures each old compact
/// window's placement before the swap and re-applies it to the new window for the same monitor.
/// Only an explicit double-click changes the mode; a rebuild never does. This class is the
/// SDK/WinForms-free decision half; <c>WallForm.CaptureCompactSnapshot</c>/<c>RestoreCompact</c>
/// are the WinForms half.
/// </summary>
public static class CompactWindowCarryOver
{
    /// <summary>Keys the captured snapshots by monitor number. Null entries are windows that were
    /// NOT compact (nothing to carry). If two windows report the same monitor, the first wins.</summary>
    public static IReadOnlyDictionary<int, CompactWindowSnapshot> Capture(IEnumerable<CompactWindowSnapshot?> snapshots)
    {
        var byMonitor = new Dictionary<int, CompactWindowSnapshot>();
        foreach (var snapshot in snapshots)
        {
            if (snapshot is not null && !byMonitor.ContainsKey(snapshot.Monitor))
            {
                byMonitor[snapshot.Monitor] = snapshot;
            }
        }

        return byMonitor;
    }

    /// <summary>The snapshot to apply to a NEW window for <paramref name="monitor"/>, or null to
    /// leave it in its constructed fullscreen posture: no compact window existed for that monitor,
    /// or <paramref name="kioskLock"/> is on (compact is impossible under lock).</summary>
    public static CompactWindowSnapshot? ForMonitor(IReadOnlyDictionary<int, CompactWindowSnapshot> carried, int monitor, bool kioskLock)
    {
        if (kioskLock)
        {
            return null;
        }

        return carried.TryGetValue(monitor, out var snapshot) ? snapshot : null;
    }

    /// <summary>Which rectangle represents where the operator put the window: its live
    /// <c>Bounds</c> while shown normally, otherwise <c>RestoreBounds</c> (a minimized window's
    /// Bounds is the off-screen -32000 parking spot, a maximized one's is the whole work area).</summary>
    public static Rectangle ChooseBounds(CompactShowState showState, Rectangle bounds, Rectangle restoreBounds)
        => showState == CompactShowState.Normal ? bounds : restoreBounds;
}
