using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Fields;
using Ludots.Core.Map;

namespace Ludots.Core.Gameplay.FieldRegions
{
    /// <summary>One runtime redraw edit: author region key plus inclusive rect strokes.</summary>
    public sealed record FieldRegionStrokeEdit(string RegionKey, IReadOnlyList<FieldCellRectStroke> Rects);

    /// <summary>Summary of one applied redraw batch.</summary>
    public sealed record FieldRedrawResult(string LayerKey, int RegionsRegistered, int CellsChanged);

    /// <summary>
    /// Runtime re-authoring of a discrete region layer (e.g. a player-issued redraw of
    /// county borders). Two-phase and fail-closed: every region key is registered before
    /// the first cell write, so a full catalog aborts without partial application. After
    /// the writes: missing region entities are materialized, footprints are re-tallied,
    /// and hierarchy remaps rebuild so projections stay consistent. Stationary tracked
    /// entities re-evaluate membership through the per-chunk change stamp on their next
    /// tick, which re-uses the ordinary FieldRegionEntered/Exited event line.
    /// </summary>
    public static class FieldRegionRedraw
    {
        public static FieldRedrawResult ApplyDiscrete(
            World world,
            MapSession session,
            string layerKey,
            IReadOnlyList<FieldRegionStrokeEdit> edits)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(edits);

            FieldSessionStore fields = session.Fields
                ?? throw new InvalidOperationException($"Map '{session.MapId.Value}' has no active field session.");
            if (!fields.TryGetByKey(layerKey, out FieldLayerData layerData) ||
                layerData is not DiscreteIdFieldLayerData layer)
            {
                throw new InvalidOperationException(
                    $"Field layer '{layerKey}' is not an active discrete-id layer of map '{session.MapId.Value}'.");
            }

            RegionEntityIndex index = session.RegionIndex
                ?? throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' has no materialized region index; load the map first.");

            // Phase 1: keys. Registration is atomic against capacity, so this aborts
            // before any cell write when the catalog is full.
            int registered = 0;
            var regionIds = new List<int>(edits.Count);
            foreach (FieldRegionStrokeEdit edit in edits)
            {
                int before = layer.Regions.Count;
                int regionId = layer.Regions.Register(edit.RegionKey);
                if (layer.Regions.Count != before)
                {
                    registered++;
                }

                regionIds.Add(regionId);
            }

            // Phase 2: strokes. FillRect marks affected chunks dirty; stationary tracked
            // entities re-evaluate through the change stamp on their next tick.
            int cellsChanged = 0;
            for (int i = 0; i < edits.Count; i++)
            {
                int regionId = regionIds[i];
                foreach (FieldCellRectStroke rect in edits[i].Rects)
                {
                    cellsChanged += layer.Field.FillRect(rect.X0, rect.Y0, rect.X1, rect.Y1, regionId);
                }
            }

            // Phase 3: entities, footprints, projections.
            Dictionary<int, int> counts = FieldRegionMaterializer.CountRegionCells(layer.Field);
            FieldRegionMaterializer.EnsureRuntimeRegions(world, session, index, layer, counts);
            for (int regionId = 1; regionId <= layer.Regions.Count; regionId++)
            {
                if (!index.TryResolve(layer.LayerId, regionId, out Entity entity))
                {
                    continue;
                }

                ref var footprint = ref world.Get<RegionFootprintCm>(entity);
                footprint.CellCount = counts.TryGetValue(regionId, out int count) ? count : 0;
            }

            session.RegionGroups?.RebuildRemaps(world, session);

            return new FieldRedrawResult(layerKey, registered, cellsChanged);
        }
    }
}
