using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Spatial;

namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdSurface2D
    {
        public readonly Fix64 CellSizeCm;
        public readonly int TileSizeCells;

        private readonly int _tileShift;
        private readonly int _tileMask;
        private readonly Dictionary<long, CrowdWorldTile2D> _tiles;

        public CrowdSurface2D(Fix64 cellSizeCm, int tileSizeCells, int initialTileCapacity = 256)
        {
            if (tileSizeCells <= 0) throw new ArgumentOutOfRangeException(nameof(tileSizeCells));
            if ((tileSizeCells & (tileSizeCells - 1)) != 0) throw new ArgumentException("tileSizeCells must be a power of two.", nameof(tileSizeCells));

            CellSizeCm = cellSizeCm;
            TileSizeCells = tileSizeCells;
            _tileMask = tileSizeCells - 1;
            _tileShift = BitOperations.TrailingZeroCount((uint)tileSizeCells);
            _tiles = new Dictionary<long, CrowdWorldTile2D>(Math.Max(8, initialTileCapacity));
        }

        public bool TryGetTile(long tileKey, out CrowdWorldTile2D tile) => _tiles.TryGetValue(tileKey, out tile);

        public CrowdWorldTile2D GetOrCreateTile(long tileKey)
        {
            if (!_tiles.TryGetValue(tileKey, out var tile))
            {
                tile = new CrowdWorldTile2D(TileSizeCells);
                _tiles[tileKey] = tile;
            }
            return tile;
        }

        public void RemoveTile(long tileKey)
        {
            _tiles.Remove(tileKey);
        }

        public void SetObstacleCell(int cellX, int cellY, bool blocked)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            var tile = GetOrCreateTile(tileKey);
            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            int idx = ly * TileSizeCells + lx;
            tile.Obstacles[idx] = blocked ? (byte)1 : (byte)0;
        }

        public bool IsBlockedCell(int cellX, int cellY)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            if (!_tiles.TryGetValue(tileKey, out var tile)) return false;
            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            int idx = ly * TileSizeCells + lx;
            return tile.Obstacles[idx] != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WorldToCell(in Fix64Vec2 worldCm, out int cellX, out int cellY)
        {
            cellX = (worldCm.X / CellSizeCm).FloorToInt();
            cellY = (worldCm.Y / CellSizeCm).FloorToInt();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fix64Vec2 CellCenterToWorldCm(int cellX, int cellY)
        {
            Fix64 half = CellSizeCm / (Fix64)2;
            return new Fix64Vec2(Fix64.FromInt(cellX) * CellSizeCm + half, Fix64.FromInt(cellY) * CellSizeCm + half);
        }
    }
}
