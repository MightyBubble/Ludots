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
        private readonly Dictionary<long, int> _retainCounts;

        public CrowdSurface2D(Fix64 cellSizeCm, int tileSizeCells, int initialTileCapacity = 256)
        {
            if (tileSizeCells <= 0) throw new ArgumentOutOfRangeException(nameof(tileSizeCells));
            if ((tileSizeCells & (tileSizeCells - 1)) != 0) throw new ArgumentException("tileSizeCells must be a power of two.", nameof(tileSizeCells));

            CellSizeCm = cellSizeCm;
            TileSizeCells = tileSizeCells;
            _tileMask = tileSizeCells - 1;
            _tileShift = BitOperations.TrailingZeroCount((uint)tileSizeCells);
            _tiles = new Dictionary<long, CrowdWorldTile2D>(Math.Max(8, initialTileCapacity));
            _retainCounts = new Dictionary<long, int>(Math.Max(8, initialTileCapacity));
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

        public CrowdWorldTile2D RetainTile(long tileKey)
        {
            var tile = GetOrCreateTile(tileKey);
            _retainCounts.TryGetValue(tileKey, out int count);
            _retainCounts[tileKey] = count + 1;
            return tile;
        }

        public void ReleaseTile(long tileKey)
        {
            if (!_retainCounts.TryGetValue(tileKey, out int count))
            {
                return;
            }

            count--;
            if (count > 0)
            {
                _retainCounts[tileKey] = count;
                return;
            }

            _retainCounts.Remove(tileKey);
            _tiles.Remove(tileKey);
        }

        public void RemoveTile(long tileKey)
        {
            _retainCounts.Remove(tileKey);
            _tiles.Remove(tileKey);
        }

        public void ClearObstacleField()
        {
            foreach (var tile in _tiles.Values)
            {
                tile.ClearObstacles();
            }
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

        public void SplatObstacleCircle(in Vector2 positionCm, float radiusCm)
        {
            if (!(radiusCm > 0f))
            {
                return;
            }

            float cellSize = CellSizeCm.ToFloat();
            if (!(cellSize > 1e-6f))
            {
                return;
            }

            float paddedRadius = radiusCm + cellSize * 0.5f;
            float paddedRadiusSq = paddedRadius * paddedRadius;
            int minCellX = FloorToCell(positionCm.X - paddedRadius);
            int maxCellX = FloorToCell(positionCm.X + paddedRadius);
            int minCellY = FloorToCell(positionCm.Y - paddedRadius);
            int maxCellY = FloorToCell(positionCm.Y + paddedRadius);

            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    var center = CellCenterToWorldCm(cellX, cellY).ToVector2();
                    Vector2 delta = center - positionCm;
                    if (delta.LengthSquared() > paddedRadiusSq)
                    {
                        continue;
                    }

                    SetObstacleCell(cellX, cellY, blocked: true);
                }
            }
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FloorToCell(float worldCm)
        {
            return (Fix64.FromFloat(worldCm) / CellSizeCm).FloorToInt();
        }
    }
}
