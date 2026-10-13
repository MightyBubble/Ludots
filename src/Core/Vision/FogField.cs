using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Vision
{
    public sealed class FogField
    {
        private readonly int _scopeKeyId;
        private readonly FogLayerDefinition _layer;
        private readonly ChunkedField2D<CellVisibility> _cells;
        private int _visibleCount;
        private int _exploredInclusiveCount;
        private CellVisibility[][]? _lanes;
        private FieldChunk2D<CellVisibility>[]? _laneChunks;
        private byte[]? _visibleMirror;
        private int[]? _exploreX;
        private int[]? _exploreY;
        private int[]? _changed;
        private int _blockSide;
        private int _chunkShift;
        private int _chunkMask;
        private int _chunksPerSide;

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
        public int VisibleCount => _visibleCount;
        public int ExploredInclusiveCount => _exploredInclusiveCount;

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
            if (TryLane(cell.X, cell.Y, out CellVisibility[] lane, out int local, out _))
            {
                return lane[local];
            }

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
            if (TryLane(cell.X, cell.Y, out CellVisibility[] lane, out int local, out FieldChunk2D<CellVisibility> chunk))
            {
                WriteLane(lane, chunk, local, cell.X, cell.Y, visibility);
                return;
            }

            CellVisibility previous = GetVisibility(cell);
            if (previous == visibility)
            {
                return;
            }

            AdjustCounts(previous, -1);
            AdjustCounts(visibility, 1);
            _cells.Set(ToFieldCell(cell), visibility);
        }

        public bool PromoteToVisible(FogCell cell, out bool firstExplore)
        {
            CellVisibility previous;
            if (TryLane(cell.X, cell.Y, out CellVisibility[] lane, out int local, out FieldChunk2D<CellVisibility> chunk))
            {
                previous = lane[local];
                if (previous == CellVisibility.Visible)
                {
                    firstExplore = false;
                    return false;
                }

                firstExplore = previous == CellVisibility.Unseen;
                WriteLane(lane, chunk, local, cell.X, cell.Y, CellVisibility.Visible);
                return true;
            }

            previous = GetVisibility(cell);
            if (previous == CellVisibility.Visible)
            {
                firstExplore = false;
                return false;
            }

            firstExplore = previous == CellVisibility.Unseen;
            AdjustCounts(previous, -1);
            AdjustCounts(CellVisibility.Visible, 1);
            _cells.Set(ToFieldCell(cell), CellVisibility.Visible);
            return true;
        }

        public void EnsureCellBlock(int sideCells)
        {
            if (sideCells <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sideCells));
            }

            FieldGridSpec2D grid = _cells.Grid;
            int chunk = grid.ChunkSizeCells;
            _blockSide = sideCells;
            _chunkShift = grid.ChunkShift;
            _chunkMask = grid.ChunkMask;
            _chunksPerSide = (sideCells + chunk - 1) / chunk;
            _lanes = new CellVisibility[_chunksPerSide * _chunksPerSide][];
            _laneChunks = new FieldChunk2D<CellVisibility>[_lanes.Length];
            for (int cy = 0; cy < _chunksPerSide; cy++)
            {
                for (int cx = 0; cx < _chunksPerSide; cx++)
                {
                    _cells.EnsureChunk(new FieldCell2D(cx * chunk, cy * chunk));
                    FieldChunk2D<CellVisibility> materialized = _cells.GetChunk(cx, cy);
                    int index = cy * _chunksPerSide + cx;
                    _laneChunks[index] = materialized;
                    _lanes[index] = materialized.StructLane;
                }
            }

            int cells = sideCells * sideCells;
            _visibleMirror = new byte[cells];
            _exploreX = new int[cells];
            _exploreY = new int[cells];
            _changed = new int[cells];
            for (int y = 0; y < sideCells; y++)
            {
                for (int x = 0; x < sideCells; x++)
                {
                    if (TryLane(x, y, out CellVisibility[] lane, out int local, out _))
                    {
                        _visibleMirror[y * sideCells + x] = lane[local] == CellVisibility.Visible ? (byte)1 : (byte)0;
                    }
                }
            }
        }

        public void Age(FogCell cell)
        {
            if (GetVisibility(cell) == CellVisibility.Visible)
            {
                SetVisibility(cell, CellVisibility.Explored);
            }
        }

        internal void CommitVisibleMask<TFilter>(ReadOnlySpan<byte> mask, int side, ref TFilter filter)
            where TFilter : struct, IVisionStampFilter
        {
            if (_lanes == null || _laneChunks == null || _visibleMirror == null || _exploreX == null || _exploreY == null
                || _changed == null || side != _blockSide || mask.Length != side * side)
            {
                throw new InvalidOperationException("Visible mask requires a prepared fog block of the same side.");
            }

            int changes = CollectChanges(mask, _visibleMirror, _changed);
            int found = 0;
            for (int n = 0; n < changes; n++)
            {
                int index = _changed[n];
                int y = index / side;
                int x = index - y * side;
                if (!TryLane(x, y, out CellVisibility[] lane, out int local, out FieldChunk2D<CellVisibility> chunk))
                {
                    continue;
                }

                CellVisibility previous = lane[local];
                byte want = mask[index];
                if (want != 0)
                {
                    if (previous != CellVisibility.Visible)
                    {
                        bool firstExplore = previous == CellVisibility.Unseen;
                        ApplyVisible(lane, chunk, local, previous);
                        if (firstExplore)
                        {
                            _exploreX[found] = x;
                            _exploreY[found] = y;
                            found++;
                        }
                    }
                }
                else if (previous == CellVisibility.Visible)
                {
                    lane[local] = CellVisibility.Explored;
                    _visibleCount--;
                    _cells.NoteDirty(chunk, local);
                }

                _visibleMirror[index] = want == 0 ? (byte)0 : (byte)1;
            }

            for (int i = 0; i < found; i++)
            {
                filter.OnFirstExplore(_exploreX[i], _exploreY[i]);
            }
        }

        public void AgeVisibleToExplored()
        {
            if (_lanes != null && _laneChunks != null)
            {
                int replaced = 0;
                int cellCount = _lanes[0].Length;
                int shift = _chunkShift;
                for (int chunkIndex = 0; chunkIndex < _lanes.Length; chunkIndex++)
                {
                    CellVisibility[] lane = _lanes[chunkIndex];
                    FieldChunk2D<CellVisibility> chunk = _laneChunks[chunkIndex];
                    int cy = chunkIndex / _chunksPerSide;
                    int cx = chunkIndex - cy * _chunksPerSide;
                    int x0 = cx << shift;
                    int y0 = cy << shift;
                    for (int local = 0; local < cellCount; local++)
                    {
                        if (lane[local] != CellVisibility.Visible)
                        {
                            continue;
                        }

                        lane[local] = CellVisibility.Explored;
                        NoteMirror(x0 + (local & _chunkMask), y0 + (local >> shift), false);
                        _cells.NoteDirty(chunk, local);
                        replaced++;
                    }
                }

                _visibleCount -= replaced;
                return;
            }

            int slow = _cells.ReplaceValue(CellVisibility.Visible, CellVisibility.Explored);
            _visibleCount -= slow;
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
            _visibleCount = 0;
            _exploredInclusiveCount = 0;
            _lanes = null;
            _laneChunks = null;
            _visibleMirror = null;
            _exploreX = null;
            _exploreY = null;
            _changed = null;
            _blockSide = 0;
        }

        private bool TryLane(int x, int y, out CellVisibility[] lane, out int local, out FieldChunk2D<CellVisibility> chunk)
        {
            if (_lanes == null || _laneChunks == null || (uint)x >= (uint)_blockSide || (uint)y >= (uint)_blockSide)
            {
                lane = null!;
                local = 0;
                chunk = null!;
                return false;
            }

            int cx = x >> _chunkShift;
            int cy = y >> _chunkShift;
            int index = cy * _chunksPerSide + cx;
            lane = _lanes[index];
            chunk = _laneChunks[index];
            local = ((y & _chunkMask) << _chunkShift) + (x & _chunkMask);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyVisible(CellVisibility[] lane, FieldChunk2D<CellVisibility> chunk, int local, CellVisibility previous)
        {
            lane[local] = CellVisibility.Visible;
            if (previous == CellVisibility.Explored)
            {
                _visibleCount++;
            }
            else if (previous == CellVisibility.Unseen)
            {
                _visibleCount++;
                _exploredInclusiveCount++;
                _cells.AddNonDefault(1);
            }
            else
            {
                AdjustCounts(previous, -1);
                AdjustCounts(CellVisibility.Visible, 1);
            }

            _cells.NoteDirty(chunk, local);
        }

        private void WriteLane(CellVisibility[] lane, FieldChunk2D<CellVisibility> chunk, int local, int x, int y, CellVisibility visibility)
        {
            CellVisibility previous = lane[local];
            if (previous == visibility)
            {
                return;
            }

            AdjustCounts(previous, -1);
            AdjustCounts(visibility, 1);
            if (previous == CellVisibility.Unseen)
            {
                _cells.AddNonDefault(1);
            }
            else if (visibility == CellVisibility.Unseen)
            {
                _cells.AddNonDefault(-1);
            }

            lane[local] = visibility;
            NoteMirror(x, y, visibility == CellVisibility.Visible);
            _cells.NoteDirty(chunk, local);
        }

        private static int CollectChanges(ReadOnlySpan<byte> mask, byte[] mirror, int[] changed)
        {
            int count = 0;
            int length = mask.Length;
            for (int i = 0; i < length; i++)
            {
                if (mask[i] != mirror[i])
                {
                    changed[count++] = i;
                }
            }

            return count;
        }

        private void NoteMirror(int x, int y, bool visible)
        {
            if (_visibleMirror == null || (uint)x >= (uint)_blockSide || (uint)y >= (uint)_blockSide)
            {
                return;
            }

            _visibleMirror[y * _blockSide + x] = visible ? (byte)1 : (byte)0;
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

        private static void AdjustCounts(ref int visibleCount, ref int exploredInclusiveCount, CellVisibility visibility, int delta)
        {
            if (visibility == CellVisibility.Visible)
            {
                visibleCount += delta;
                exploredInclusiveCount += delta;
                return;
            }

            if (visibility == CellVisibility.Explored)
            {
                exploredInclusiveCount += delta;
            }
        }

        private void AdjustCounts(CellVisibility visibility, int delta)
        {
            AdjustCounts(ref _visibleCount, ref _exploredInclusiveCount, visibility, delta);
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
