using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Vision
{
    public sealed class FogField
    {
        private readonly int _scopeKeyId;
        private readonly FogLayerDefinition _layer;
        private readonly int _chunkSize;
        private readonly int _chunkShift;
        private FogChunk[] _chunks;
        private int _chunkCount;
        private int _dirtyCount;

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

            if (chunkSizeCells <= 0 || (chunkSizeCells & (chunkSizeCells - 1)) != 0)
            {
                throw new ArgumentException("FogField chunk size must be a positive power of two.", nameof(chunkSizeCells));
            }

            if (initialChunkCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialChunkCapacity));
            }

            _scopeKeyId = scopeKeyId;
            _layer = layer;
            _chunkSize = chunkSizeCells;
            _chunkShift = CalculateChunkShift(chunkSizeCells);
            _chunks = new FogChunk[initialChunkCapacity];
        }

        public int ScopeKeyId => _scopeKeyId;
        public FogLayerId LayerId => _layer.Id;
        public int CellSizeCm => _layer.CellSizeCm;
        public int ChunkCount => _chunkCount;
        public int DirtyCount => _dirtyCount;

        public FogCell WorldToCell(WorldCmInt2 world)
        {
            return new FogCell(
                MathUtil.FloorDiv(world.X, _layer.CellSizeCm),
                MathUtil.FloorDiv(world.Y, _layer.CellSizeCm));
        }

        public WorldCmInt2 CellCenterToWorld(FogCell cell)
        {
            int half = _layer.CellSizeCm / 2;
            return new WorldCmInt2(
                (cell.X * _layer.CellSizeCm) + half,
                (cell.Y * _layer.CellSizeCm) + half);
        }

        public CellVisibility GetVisibility(FogCell cell)
        {
            int chunkIndex = FindChunkIndex(ChunkCoord(cell.X), ChunkCoord(cell.Y));
            if (chunkIndex < 0)
            {
                return CellVisibility.Unseen;
            }

            ref FogChunk chunk = ref _chunks[chunkIndex];
            int local = LocalIndex(cell.X, cell.Y);
            return chunk.Get(local);
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
            ref FogChunk chunk = ref GetOrCreateChunk(ChunkCoord(cell.X), ChunkCoord(cell.Y));
            int local = LocalIndex(cell.X, cell.Y);
            CellVisibility previous = chunk.Get(local);
            if (previous == visibility)
            {
                return;
            }

            chunk.Set(local, visibility);
            MarkDirtyInChunk(ref chunk, cell);
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
            for (int chunkIndex = 0; chunkIndex < _chunkCount; chunkIndex++)
            {
                ref FogChunk chunk = ref _chunks[chunkIndex];
                int cellCount = _chunkSize * _chunkSize;
                for (int local = 0; local < cellCount; local++)
                {
                    if (chunk.Get(local) != CellVisibility.Visible)
                    {
                        continue;
                    }

                    chunk.Set(local, CellVisibility.Explored);
                    int localX = local & (_chunkSize - 1);
                    int localY = local >> _chunkShift;
                    MarkDirtyInChunk(ref chunk, new FogCell((chunk.ChunkX * _chunkSize) + localX, (chunk.ChunkY * _chunkSize) + localY));
                }
            }
        }

        public void MarkDirtyRegion(IntRect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            int endY = rect.Y + rect.Height;
            int endX = rect.X + rect.Width;
            for (int y = rect.Y; y < endY; y++)
            {
                for (int x = rect.X; x < endX; x++)
                {
                    ref FogChunk chunk = ref GetOrCreateChunk(ChunkCoord(x), ChunkCoord(y));
                    MarkDirtyInChunk(ref chunk, new FogCell(x, y));
                }
            }
        }

        public int EnumerateDirtyCells(Span<FogCell> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            for (int chunkIndex = 0; chunkIndex < _chunkCount && written < destination.Length; chunkIndex++)
            {
                ref FogChunk chunk = ref _chunks[chunkIndex];
                for (int i = 0; i < chunk.DirtyCount && written < destination.Length; i++)
                {
                    destination[written++] = chunk.DirtyCells[i];
                }
            }

            return written;
        }

        public void ClearDirty()
        {
            for (int chunkIndex = 0; chunkIndex < _chunkCount; chunkIndex++)
            {
                _chunks[chunkIndex].DirtyCount = 0;
            }

            _dirtyCount = 0;
        }

        public int CopyCells(Span<FogCellState> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            int cellCount = _chunkSize * _chunkSize;
            for (int chunkIndex = 0; chunkIndex < _chunkCount && written < destination.Length; chunkIndex++)
            {
                ref FogChunk chunk = ref _chunks[chunkIndex];
                for (int local = 0; local < cellCount && written < destination.Length; local++)
                {
                    CellVisibility visibility = chunk.Get(local);
                    if (visibility == CellVisibility.Unseen)
                    {
                        continue;
                    }

                    int localX = local & (_chunkSize - 1);
                    int localY = local >> _chunkShift;
                    destination[written++] = new FogCellState(
                        new FogCell((chunk.ChunkX * _chunkSize) + localX, (chunk.ChunkY * _chunkSize) + localY),
                        visibility);
                }
            }

            return written;
        }

        public void Clear()
        {
            _chunkCount = 0;
            _dirtyCount = 0;
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

        private static int CalculateChunkShift(int chunkSize)
        {
            int shift = 0;
            int value = chunkSize;
            while (value > 1)
            {
                value >>= 1;
                shift++;
            }

            return shift;
        }

        private int ChunkCoord(int cellCoord) => MathUtil.FloorDiv(cellCoord, _chunkSize);

        private int LocalIndex(int cellX, int cellY)
        {
            int localX = cellX - (ChunkCoord(cellX) * _chunkSize);
            int localY = cellY - (ChunkCoord(cellY) * _chunkSize);
            return (localY * _chunkSize) + localX;
        }

        private ref FogChunk GetOrCreateChunk(int chunkX, int chunkY)
        {
            int existing = FindChunkIndex(chunkX, chunkY);
            if (existing >= 0)
            {
                return ref _chunks[existing];
            }

            EnsureChunkCapacity(_chunkCount + 1);
            int index = _chunkCount++;
            _chunks[index] = new FogChunk(chunkX, chunkY, _chunkSize);
            return ref _chunks[index];
        }

        private int FindChunkIndex(int chunkX, int chunkY)
        {
            for (int i = 0; i < _chunkCount; i++)
            {
                if (_chunks[i].ChunkX == chunkX && _chunks[i].ChunkY == chunkY)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureChunkCapacity(int required)
        {
            if (required <= _chunks.Length)
            {
                return;
            }

            int next = _chunks.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _chunks, next);
        }

        private void MarkDirtyInChunk(ref FogChunk chunk, FogCell cell)
        {
            if (chunk.TryAddDirty(cell))
            {
                _dirtyCount++;
            }
        }

        private struct FogChunk
        {
            private readonly byte[] _states;
            public readonly FogCell[] DirtyCells;

            public FogChunk(int chunkX, int chunkY, int chunkSize)
            {
                ChunkX = chunkX;
                ChunkY = chunkY;
                _states = new byte[chunkSize * chunkSize];
                DirtyCells = new FogCell[chunkSize * chunkSize];
                DirtyCount = 0;
            }

            public readonly int ChunkX;
            public readonly int ChunkY;
            public int DirtyCount;

            public CellVisibility Get(int localIndex) => (CellVisibility)_states[localIndex];

            public void Set(int localIndex, CellVisibility visibility)
            {
                _states[localIndex] = (byte)visibility;
            }

            public bool TryAddDirty(FogCell cell)
            {
                for (int i = 0; i < DirtyCount; i++)
                {
                    if (DirtyCells[i] == cell)
                    {
                        return false;
                    }
                }

                DirtyCells[DirtyCount++] = cell;
                return true;
            }
        }
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
