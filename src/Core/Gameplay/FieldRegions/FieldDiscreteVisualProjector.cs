using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.FieldRegions
{
    public enum FieldDiscreteVisualMapModeKind : byte
    {
        Leaf = 0,
        AncestorDepth = 1,
        GroupKey = 2,
    }

    public readonly struct FieldDiscreteVisualMapMode : IEquatable<FieldDiscreteVisualMapMode>
    {
        private FieldDiscreteVisualMapMode(
            FieldDiscreteVisualMapModeKind kind,
            int depth,
            string groupKey)
        {
            Kind = kind;
            Depth = depth;
            GroupKey = groupKey;
        }

        public static FieldDiscreteVisualMapMode Leaf { get; } =
            new(FieldDiscreteVisualMapModeKind.Leaf, 0, string.Empty);

        public static FieldDiscreteVisualMapMode AncestorDepth(int depth)
        {
            if (depth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(depth));
            }

            return new FieldDiscreteVisualMapMode(
                FieldDiscreteVisualMapModeKind.AncestorDepth,
                depth,
                string.Empty);
        }

        public static FieldDiscreteVisualMapMode Group(string groupKey)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                throw new ArgumentException("Hierarchy visual group key is required.", nameof(groupKey));
            }

            return new FieldDiscreteVisualMapMode(
                FieldDiscreteVisualMapModeKind.GroupKey,
                0,
                groupKey);
        }

        public FieldDiscreteVisualMapModeKind Kind { get; }
        public int Depth { get; }
        public string GroupKey { get; }

        public bool Equals(FieldDiscreteVisualMapMode other) =>
            Kind == other.Kind &&
            Depth == other.Depth &&
            string.Equals(GroupKey, other.GroupKey, StringComparison.Ordinal);

        public override bool Equals(object? obj) =>
            obj is FieldDiscreteVisualMapMode other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Depth, GroupKey);
        public static bool operator ==(FieldDiscreteVisualMapMode left, FieldDiscreteVisualMapMode right) => left.Equals(right);
        public static bool operator !=(FieldDiscreteVisualMapMode left, FieldDiscreteVisualMapMode right) => !left.Equals(right);
    }

    public sealed class FieldDiscreteVisualProjector
    {
        private readonly Dictionary<DiscreteIdFieldLayerData, LayerProjectionState> _states = new();
        private readonly Func<int, Vector4>? _largePalette;

        public FieldDiscreteVisualProjector(Func<int, Vector4>? largePalette = null)
        {
            _largePalette = largePalette;
        }

        public int LastProjectedFieldCount { get; private set; }
        public int LastProjectedCellCount { get; private set; }
        public int LastProjectedDirtyRectCount { get; private set; }
        public int LastFullProjectionCount { get; private set; }

        public void Project(
            int scopeKeyId,
            FieldSessionStore store,
            RegionHierarchyRuntime? hierarchy,
            in FieldDiscreteVisualMapMode mode,
            GlobalFieldVisualBuffer buffer)
        {
            if (scopeKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKeyId));
            }

            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(buffer);
            if (mode.Kind != FieldDiscreteVisualMapModeKind.Leaf && hierarchy == null)
            {
                throw new InvalidOperationException(
                    $"Map mode '{mode.Kind}' requires a baked RegionHierarchyRuntime.");
            }

            LastProjectedFieldCount = 0;
            LastProjectedCellCount = 0;
            LastProjectedDirtyRectCount = 0;
            LastFullProjectionCount = 0;

            foreach (FieldLayerData layerData in store.Layers)
            {
                if (layerData is not DiscreteIdFieldLayerData layer)
                {
                    continue;
                }

                ProjectLayer(scopeKeyId, layer, hierarchy, in mode, buffer);
            }
        }

        private void ProjectLayer(
            int scopeKeyId,
            DiscreteIdFieldLayerData layer,
            RegionHierarchyRuntime? hierarchy,
            in FieldDiscreteVisualMapMode mode,
            GlobalFieldVisualBuffer buffer)
        {
            if (!_states.TryGetValue(layer, out LayerProjectionState? state))
            {
                state = new LayerProjectionState(layer.Field.OpenDirtyCursor());
                _states.Add(layer, state);
            }

            int hierarchyRevision = mode.Kind == FieldDiscreteVisualMapModeKind.Leaf
                ? 0
                : hierarchy!.GetProjectionRevision(layer.LayerId);
            int maxProjectedId = mode.Kind == FieldDiscreteVisualMapModeKind.Leaf
                ? layer.Regions.Count
                : hierarchy!.GetMaxProjectedId(layer.LayerId, in mode);
            GlobalFieldVisualValueKind valueKind = maxProjectedId <= byte.MaxValue
                ? GlobalFieldVisualValueKind.Byte
                : GlobalFieldVisualValueKind.Vector4;
            if (valueKind == GlobalFieldVisualValueKind.Vector4 && _largePalette == null)
            {
                throw new InvalidOperationException(
                    $"Field layer '{layer.LayerKey}' projects id {maxProjectedId}, exceeding byte palette capacity 255; provide a Vector4 palette callback.");
            }

            bool hierarchyInstanceChanged =
                mode.Kind != FieldDiscreteVisualMapModeKind.Leaf &&
                !ReferenceEquals(state.Hierarchy, hierarchy);
            bool fullProjection =
                !state.Initialized ||
                state.ScopeKeyId != scopeKeyId ||
                state.Mode != mode ||
                hierarchyInstanceChanged ||
                state.ValueKind != valueKind ||
                state.RegionCount != layer.Regions.Count;

            state.CellCount = 0;
            state.DirtyRectCount = 0;
            if (fullProjection)
            {
                Drain(state.Cursor);
                BuildFullProjection(state, layer, hierarchy, in mode, valueKind);
            }
            else
            {
                IntRect sourceBounds = BuildIncrementalProjection(
                    state,
                    layer,
                    hierarchy,
                    in mode,
                    valueKind);
                if (HasArea(sourceBounds) &&
                    (!HasArea(state.Bounds) || Union(state.Bounds, sourceBounds) != state.Bounds))
                {
                    BuildFullProjection(state, layer, hierarchy, in mode, valueKind);
                    fullProjection = true;
                }
            }

            if (fullProjection)
            {
                LastFullProjectionCount++;
            }

            state.Initialized = true;
            state.ScopeKeyId = scopeKeyId;
            state.Mode = mode;
            state.Hierarchy = mode.Kind == FieldDiscreteVisualMapModeKind.Leaf ? null : hierarchy;
            state.HierarchyRevision = hierarchyRevision;
            state.ValueKind = valueKind;
            state.RegionCount = layer.Regions.Count;

            if (!HasArea(state.Bounds))
            {
                return;
            }

            var id = new GlobalFieldVisualId(
                GlobalFieldVisualKind.DiscreteOwnership,
                scopeKeyId,
                layer.LayerId.Value,
                surfaceKeyId: 0);
            var descriptor = new GlobalFieldVisualDescriptor(
                id,
                layer.Definition.CellSizeCm,
                WorldCmInt2.Zero,
                state.Bounds,
                valueKind,
                paletteId: layer.LayerId.Value);
            buffer.Upsert(
                descriptor,
                state.Cells.AsSpan(0, state.CellCount),
                state.DirtyRects.AsSpan(0, state.DirtyRectCount));

            LastProjectedFieldCount++;
            LastProjectedCellCount += state.CellCount;
            LastProjectedDirtyRectCount += state.DirtyRectCount;
        }

        private void BuildFullProjection(
            LayerProjectionState state,
            DiscreteIdFieldLayerData layer,
            RegionHierarchyRuntime? hierarchy,
            in FieldDiscreteVisualMapMode mode,
            GlobalFieldVisualValueKind valueKind)
        {
            state.CellCount = 0;
            state.DirtyRectCount = 0;
            IntRect sourceBounds = default;
            bool hasSourceBounds = false;
            ChunkedField2D<int> field = layer.Field;
            for (int chunkIndex = 0; chunkIndex < field.ChunkCount; chunkIndex++)
            {
                FieldChunk2D<int> chunk = field.GetChunkAt(chunkIndex);
                AppendChunkCells(
                    state,
                    layer,
                    chunk,
                    hierarchy,
                    in mode,
                    valueKind,
                    ref sourceBounds,
                    ref hasSourceBounds);
            }

            if (hasSourceBounds)
            {
                state.Bounds = HasArea(state.Bounds) ? Union(state.Bounds, sourceBounds) : sourceBounds;
            }

            if (HasArea(state.Bounds))
            {
                EnsureDirtyRectCapacity(state, 1);
                state.DirtyRects[0] = state.Bounds;
                state.DirtyRectCount = 1;
            }
        }

        private IntRect BuildIncrementalProjection(
            LayerProjectionState state,
            DiscreteIdFieldLayerData layer,
            RegionHierarchyRuntime? hierarchy,
            in FieldDiscreteVisualMapMode mode,
            GlobalFieldVisualValueKind valueKind)
        {
            IntRect sourceBounds = default;
            bool hasSourceBounds = false;
            int chunkSize = layer.Field.Grid.ChunkSizeCells;
            while (state.Cursor.TryTakeChangedChunk(out FieldChunk2D<int> chunk))
            {
                EnsureDirtyRectCapacity(state, state.DirtyRectCount + 1);
                state.DirtyRects[state.DirtyRectCount++] = new IntRect(
                    chunk.ChunkX * chunkSize,
                    chunk.ChunkY * chunkSize,
                    chunkSize,
                    chunkSize);
                AppendChunkCells(
                    state,
                    layer,
                    chunk,
                    hierarchy,
                    in mode,
                    valueKind,
                    ref sourceBounds,
                    ref hasSourceBounds);
            }

            return hasSourceBounds ? sourceBounds : default;
        }

        private void AppendChunkCells(
            LayerProjectionState state,
            DiscreteIdFieldLayerData layer,
            FieldChunk2D<int> chunk,
            RegionHierarchyRuntime? hierarchy,
            in FieldDiscreteVisualMapMode mode,
            GlobalFieldVisualValueKind valueKind,
            ref IntRect sourceBounds,
            ref bool hasSourceBounds)
        {
            for (int local = 0; local < chunk.CellCount; local++)
            {
                int leafRegionId = chunk.Get(local);
                if (leafRegionId == 0)
                {
                    continue;
                }

                if ((uint)leafRegionId > (uint)layer.Regions.Count)
                {
                    throw new InvalidOperationException(
                        $"Field layer '{layer.LayerKey}' cell references unregistered region id {leafRegionId}.");
                }

                FieldCell2D cell = layer.Field.Grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, local);
                IncludeCell(ref sourceBounds, ref hasSourceBounds, cell);
                int projectedId = mode.Kind == FieldDiscreteVisualMapModeKind.Leaf
                    ? leafRegionId
                    : hierarchy!.ResolveProjectedId(layer.LayerId, leafRegionId, in mode);
                if (projectedId == 0)
                {
                    continue;
                }

                EnsureCellCapacity(state, state.CellCount + 1);
                state.Cells[state.CellCount++] = valueKind == GlobalFieldVisualValueKind.Byte
                    ? new GlobalFieldVisualCell(cell, checked((byte)projectedId))
                    : new GlobalFieldVisualCell(cell, _largePalette!(projectedId));
            }
        }

        private static void IncludeCell(ref IntRect bounds, ref bool hasBounds, FieldCell2D cell)
        {
            IntRect cellBounds = new(cell.X, cell.Y, 1, 1);
            bounds = hasBounds ? Union(bounds, cellBounds) : cellBounds;
            hasBounds = true;
        }

        private static void Drain(FieldDirtyCursor<int> cursor)
        {
            while (cursor.TryTakeChangedChunk(out _))
            {
            }
        }

        private static bool HasArea(IntRect rect) => rect.Width > 0 && rect.Height > 0;

        private static IntRect Union(IntRect left, IntRect right)
        {
            int x = Math.Min(left.Left, right.Left);
            int y = Math.Min(left.Top, right.Top);
            int rightEdge = Math.Max(left.Right, right.Right);
            int bottom = Math.Max(left.Bottom, right.Bottom);
            return new IntRect(x, y, rightEdge - x, bottom - y);
        }

        private static void EnsureCellCapacity(LayerProjectionState state, int required)
        {
            if (required <= state.Cells.Length)
            {
                return;
            }

            Array.Resize(ref state.Cells, NextCapacity(state.Cells.Length, required));
        }

        private static void EnsureDirtyRectCapacity(LayerProjectionState state, int required)
        {
            if (required <= state.DirtyRects.Length)
            {
                return;
            }

            Array.Resize(ref state.DirtyRects, NextCapacity(state.DirtyRects.Length, required));
        }

        private static int NextCapacity(int current, int required)
        {
            int next = Math.Max(4, current);
            while (next < required)
            {
                next *= 2;
            }

            return next;
        }

        private sealed class LayerProjectionState
        {
            public LayerProjectionState(FieldDirtyCursor<int> cursor)
            {
                Cursor = cursor;
            }

            public readonly FieldDirtyCursor<int> Cursor;
            public GlobalFieldVisualCell[] Cells = Array.Empty<GlobalFieldVisualCell>();
            public IntRect[] DirtyRects = Array.Empty<IntRect>();
            public int CellCount;
            public int DirtyRectCount;
            public IntRect Bounds;
            public bool Initialized;
            public int ScopeKeyId;
            public FieldDiscreteVisualMapMode Mode;
            public RegionHierarchyRuntime? Hierarchy;
            public int HierarchyRevision;
            public GlobalFieldVisualValueKind ValueKind;
            public int RegionCount;
        }
    }
}
