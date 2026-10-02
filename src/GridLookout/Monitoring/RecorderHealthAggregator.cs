using GridLookout.Layout;
using GridLookout.Milestone;

namespace GridLookout.Monitoring;

/// <summary>One live tile's fact as F2's per-recorder health rollup needs it — deliberately NOT any
/// WallForm-internal type (no <c>ActiveTile</c>, no SDK/WinForms dependency) so this whole
/// aggregation stays pure and unit-testable without a live MIP session or an STA thread, the same
/// "SDK-free, plain-data" convention <c>Layout.LayoutResolver.CameraCatalogEntry</c> already follows
/// (see that type's own doc comment) and for the identical reason <c>WallSetSwapperTests</c> exercises
/// <c>WallSetSwapper</c> through a fake form rather than a real <c>WallForm</c> — constructing a real
/// one needs an STA thread and a live MIP session, neither of which a fast unit test wants to stand
/// up. <c>WallForm.GetRecorderTileFacts()</c> is the only production source: a trivial projection of
/// its already-computed per-tile state (the same state <c>GetHealthSnapshot</c> reads, unchanged by
/// this feature) into this shape — no new classification logic lives there.
///
/// Buyer-review defect #9 fix: <see cref="RecorderId"/> (the stable FQID ObjectId, blank —
/// <c>Guid.Empty</c> — in single-recorder mode, exactly like <see cref="RecorderName"/>'s own
/// single-mode-blank convention) is now the grouping KEY <see cref="RecorderHealthAggregator.Aggregate"/>
/// uses; <see cref="RecorderName"/> is retained purely for display (two distinct recorders can
/// legitimately share a display name — grouping by name silently collapsed them into one health
/// row before this fix).</summary>
public readonly record struct RecorderTileFact(Guid RecorderId, string RecorderName, bool EverRendered, bool Stalled);

/// <summary>One recorder's live tile rollup — see <see cref="RecorderHealthAggregator.Aggregate"/>.
/// <see cref="Rendering"/> mirrors <c>WallFormHealth.TilesWithFrames</c>'s convention exactly
/// (includes currently-stalled tiles; <see cref="Stalled"/> is a SUBSET, not a separate bucket) —
/// see <c>RecorderHealth.TilesRendering</c>'s own doc comment for why that convention is kept
/// consistent between the per-form and per-recorder views. <see cref="RecorderName"/> is carried
/// alongside the counts (rather than looked up separately by the caller) purely for display — see
/// <see cref="RecorderTileFact.RecorderId"/>'s own doc comment for why <see cref="RecorderHealthAggregator.Aggregate"/>
/// groups by id, not name.</summary>
public sealed record RecorderTileCounts(string RecorderName, int Expected, int Rendering, int Stalled, int NeverFramed);

/// <summary>
/// F2 (multi-recorder walls): pure per-recorder health rollup, split out of <c>WallForm</c>/
/// <c>Program.cs</c> specifically so it can be unit-tested — see <see cref="RecorderTileFact"/>'s
/// own doc comment for why. Two independent aggregations live here because they run on different
/// cadences: <see cref="Aggregate"/> runs on every health-write tick (5s) against LIVE per-tile
/// state; <see cref="AggregateUnavailableByRecorder"/> runs once per successful wall rebuild against
/// the RESOLVED layout plan AND the auto-layout ordinal slice together (unavailable cells never
/// change between rebuilds — only a rebuild, e.g. a camera being disabled/deleted/renamed, can
/// change which cells are unavailable) — see Program.cs's <c>RebuildWall</c> for where each is
/// called.
/// </summary>
public static class RecorderHealthAggregator
{
    /// <summary>
    /// Groups per-tile facts (from one or more <c>WallForm</c>s — a recorder's cameras can span more
    /// than one monitor/window) by <see cref="RecorderTileFact.RecorderId"/> (buyer-review defect #9
    /// fix — was <see cref="RecorderTileFact.RecorderName"/>, which two distinct recorders can share)
    /// into the SAME classification <c>WallForm.GetHealthSnapshot</c> already uses for its per-form
    /// aggregate. Facts with an empty (<c>Guid.Empty</c>) <see cref="RecorderTileFact.RecorderId"/>
    /// (single-recorder mode; see <c>Milestone.CameraInfo.RecorderId</c>'s own doc comment) are
    /// excluded — they can never be attributed to a specific recorder, and F2's per-recorder block is
    /// itself only ever populated in multi-recorder mode. The display name carried into each result
    /// entry is the FIRST fact's name seen for that id — display-only, never part of the key, so two
    /// facts for the same recorder id can never split into two rows even if a caption momentarily
    /// disagreed about the name.
    /// </summary>
    public static IReadOnlyDictionary<Guid, RecorderTileCounts> Aggregate(IReadOnlyList<RecorderTileFact> facts)
    {
        var accumulators = new Dictionary<Guid, (string Name, int Expected, int Rendering, int Stalled, int NeverFramed)>();

        foreach (var fact in facts)
        {
            if (fact.RecorderId == Guid.Empty)
            {
                continue;
            }

            accumulators.TryGetValue(fact.RecorderId, out var acc);
            string name = string.IsNullOrEmpty(acc.Name) ? fact.RecorderName : acc.Name;
            int expected = acc.Expected + 1;
            int rendering = acc.Rendering + (fact.EverRendered ? 1 : 0);
            int stalled = acc.Stalled + (fact.EverRendered && fact.Stalled ? 1 : 0);
            int neverFramed = acc.NeverFramed + (fact.EverRendered ? 0 : 1);
            accumulators[fact.RecorderId] = (name, expected, rendering, stalled, neverFramed);
        }

        var result = new Dictionary<Guid, RecorderTileCounts>();
        foreach (var entry in accumulators)
        {
            result[entry.Key] = new RecorderTileCounts(entry.Value.Name, entry.Value.Expected, entry.Value.Rendering, entry.Value.Stalled, entry.Value.NeverFramed);
        }

        return result;
    }

    /// <summary>
    /// Attributes each UNAVAILABLE resolved cell (F3 rule 5) to the recorder owning its PRIMARY
    /// member's pinned camera id, when known — mirrors <c>WallForm.RenderResolvedPage</c>'s own
    /// "renders unavailable iff every member is unavailable, using <c>Members[0]</c> for the label"
    /// decision exactly (<c>cell.FirstAvailable is null</c> is the identical test), so this never
    /// drifts from what the wall actually shows without touching <c>WallForm</c> itself. A cell whose
    /// primary member never successfully pinned ANY camera id at all (an unknown alias/guid, or a
    /// still-out-of-range ordinal — <c>ResolvedMember.CameraId</c> null) has no recorder to attribute
    /// and is simply not counted here — see <c>RecorderHealth.TilesUnavailable</c>'s own doc comment
    /// for why that's an accepted, documented undercount rather than a defect.
    ///
    /// Buyer-review defect #9 fix: keyed by <paramref name="recorderIdByCameraId"/>'s recorder GUID,
    /// not name — see <see cref="Aggregate"/>'s own doc comment for why. <c>Program.cs</c>'s caller
    /// (<c>BuildRecorderHealthList</c>) resolves the display name for each id separately, from the
    /// last-selected catalog.
    ///
    /// Stable-ordinals follow-up (2026-09-27): <paramref name="autoLayoutCameras"/> is the
    /// auto-layout (<c>Monitors[].Cameras</c>) counterpart of <paramref name="monitors"/> — a
    /// disabled camera in that ordinal slice renders the identical UNAVAILABLE placeholder (see
    /// <c>WallForm.RenderAutoLayoutPage</c>) but has no <see cref="ResolvedMonitorPlan"/> cell to
    /// walk, since that path never goes through <c>Layout.LayoutResolver</c> at all. No
    /// <paramref name="recorderIdByCameraId"/> lookup is needed for this half — a <c>CameraInfo</c>
    /// already carries its owning recorder id directly (<c>Milestone.CameraInfo.RecorderId</c>,
    /// blank in single-recorder mode exactly like <see cref="RecorderTileFact.RecorderId"/>'s own
    /// convention, excluded here the same way). Optional and defaults to null/empty so every
    /// existing token-mode-only caller (and every test above) keeps compiling and behaving
    /// unchanged. The two sources are summed into ONE result dictionary — <c>Program.cs</c>'s
    /// "token mode is exclusive for the whole wall" rule (see <c>ComputeWallFormSpecs</c>) makes
    /// both ever being non-empty at once unreachable in production today, but nothing here assumes
    /// that; a recorder appearing in both is added together, not overwritten.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> AggregateUnavailableByRecorder(
        IReadOnlyList<ResolvedMonitorPlan> monitors,
        IReadOnlyDictionary<Guid, Guid> recorderIdByCameraId,
        IReadOnlyList<CameraInfo>? autoLayoutCameras = null)
    {
        var result = new Dictionary<Guid, int>();

        foreach (var monitor in monitors)
        {
            foreach (var page in monitor.Pages)
            {
                foreach (var row in page.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.FirstAvailable is not null)
                        {
                            continue; // renders live (fixed cell, or a rotation cell with SOME available member)
                        }

                        var primary = cell.Members[0];
                        if (primary.CameraId is not Guid pinnedId)
                        {
                            continue; // never pinned any camera id — no recorder to attribute
                        }

                        if (!recorderIdByCameraId.TryGetValue(pinnedId, out var recorderId) || recorderId == Guid.Empty)
                        {
                            continue;
                        }

                        result.TryGetValue(recorderId, out var count);
                        result[recorderId] = count + 1;
                    }
                }
            }
        }

        if (autoLayoutCameras is not null)
        {
            foreach (var camera in autoLayoutCameras)
            {
                if (camera.Enabled || camera.RecorderId == Guid.Empty)
                {
                    continue; // live tile, or single-recorder mode — nothing to attribute
                }

                result.TryGetValue(camera.RecorderId, out var count);
                result[camera.RecorderId] = count + 1;
            }
        }

        return result;
    }
}
