using System;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Vision
{
    public sealed class FogField
    {
        private readonly int _scopeKeyId;
        private readonly FogLayerDefinition _layer;
        private readonly ChunkedField2D<CellVisibility> _cells;

        public FogField(int scopeKeyId, in FogLayerDefinition layer, int chunkSizeCells = 16, int initialChunkCapacity = 8)
        {
            if (scopeKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKeyId));
            }

            if (layer.Id.Value <= 0)
            {
                throw new ArgumentException("FogField requires a registered layer.", nameof(layer));
            }

            _scopeKeyId = scopeKeyId;
            _layer = layer;
            _cells = new ChunkedField2D<CellVisibility>(
                new FieldGridSpec2D(layer.CellSizeCm, chunkSizeCells),
                CellVisibility.Unseen,
                initialChunkCapacity);
        }

        public int ScopeKeyId => _scopeKeyId;
        public FogLayerId LayerId => _layer.Id;
        public int CellSizeCm => _layer.CellSizeCm;
        public int ChunkCount => _cells.ChunkCount;
        public int DirtyCount => _cells.DirtyCount;
        public int NonDefaultCount => _cells.NonDefaultCount;

        public FogCell WorldToCell(WorldCmInt2 world)
        {
            FieldCell2D cell = _cells.WorldToCell(world);
            return new FogCell(cell.X, cell.Y);
        }

        public WorldCmInt2 CellCenterToWorld(FogCell cell)
        {
            return _cells.CellCenterToWorld(ToFieldCell(cell));
        }

        public CellVisibility GetVisibility(FogCell cell)
        {
            return _cells.Get(ToFieldCell(cell));
        }

        public void SetVisible(FogCell cell) => SetVisibility(cell, CellVisibility.Visible);

        public void SetVisible(FogCell cell, FogDenyMode denyMode)
            => SetVisibility(cell, MergeVisibility(GetVisibility(cell), CellVisibility.Visible, denyMode));

        public void SetDenied(FogCell cell) => SetVisibility(cell, CellVisibility.Denied);

        public void SetDenied(FogCell cell, FogDenyMode denyMode)
            => SetVisibility(cell, MergeVisibility(GetVisibility(cell), CellVisibility.Denied, denyMode));

        public void SetExplored(FogCell cell) => SetVisibility(cell, CellVisibility.Explored);

        public void SetVisibility(FogCell cell, CellVisibility visibility)
        {
            _cells.Set(ToFieldCell(cell), visibility);
        }

        public void Age(FogCell cell)
        {
            if (GetVisibility(cell) == CellVisibility.Visible)
            {
                SetVisibility(cell, CellVisibility.Explored);
            }
        }

        public void AgeVisibleToExplored()
        {
            _cells.ReplaceValue(CellVisibility.Visible, CellVisibility.Explored);
        }

        public void MarkDirtyRegion(IntRect rect)
        {
            _cells.MarkDirtyRegion(rect);
        }

        public int EnumerateDirtyCells(Span<FogCell> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            FieldGridSpec2D grid = _cells.Grid;
            for (int chunkIndex = 0; chunkIndex < _cells.ChunkCount && written < destination.Length; chunkIndex++)
            {
                FieldChunk2D<CellVisibility> chunk = _cells.GetChunkAt(chunkIndex);
                for (int i = 0; i < chunk.DirtyCount && written < destination.Length; i++)
                {
                    FieldCell2D cell = grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, chunk.DirtyLocals[i]);
                    destination[written++] = new FogCell(cell.X, cell.Y);
                }
            }

            return written;
        }

        public void ClearDirty()
        {
            _cells.ClearDirty();
        }

        public int CopyCells(Span<FogCellState> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            FieldGridSpec2D grid = _cells.Grid;
            int cellCount = grid.ChunkSizeCells * grid.ChunkSizeCells;
            for (int chunkIndex = 0; chunkIndex < _cells.ChunkCount && written < destination.Length; chunkIndex++)
            {
                FieldChunk2D<CellVisibility> chunk = _cells.GetChunkAt(chunkIndex);
                for (int local = 0; local < cellCount && written < destination.Length; local++)
                {
                    CellVisibility visibility = chunk.Get(local);
                    if (visibility == CellVisibility.Unseen)
                    {
                        continue;
                    }

                    FieldCell2D cell = grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, local);
                    destination[written++] = new FogCellState(new FogCell(cell.X, cell.Y), visibility);
                }
            }

            return written;
        }

        public void Clear()
        {
            _cells.Clear();
        }

        public void ApplySnapshot(ReadOnlySpan<FogCellState> states)
        {
            Clear();
            for (int i = 0; i < states.Length; i++)
            {
                SetVisibility(states[i].Cell, states[i].Visibility);
            }

            ClearDirty();
        }

        private static CellVisibility MergeVisibility(CellVisibility current, CellVisibility incoming, FogDenyMode denyMode)
        {
            if (incoming == CellVisibility.Denied)
            {
                return denyMode == FogDenyMode.RevealDominates && current == CellVisibility.Visible
                    ? CellVisibility.Visible
                    : CellVisibility.Denied;
            }

            if (incoming == CellVisibility.Visible)
            {
                return denyMode == FogDenyMode.DenyDominates && current == CellVisibility.Denied
                    ? CellVisibility.Denied
                    : CellVisibility.Visible;
            }

            if (incoming == CellVisibility.Explored && current == CellVisibility.Unseen)
            {
                return CellVisibility.Explored;
            }

            return current;
        }

        private static FieldCell2D ToFieldCell(FogCell cell) => new(cell.X, cell.Y);
    }

    public readonly struct FogCellState
    {
        public FogCellState(FogCell cell, CellVisibility visibility)
        {
            Cell = cell;
            Visibility = visibility;
        }

        public readonly FogCell Cell;
        public readonly CellVisibility Visibility;
    }
}
