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
        private readonly Stack<CrowdWorldTile2D> _tilePool;

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
            _tilePool = new Stack<CrowdWorldTile2D>(Math.Max(8, initialTileCapacity));
        }

        public bool TryGetTile(long tileKey, out CrowdWorldTile2D tile) => _tiles.TryGetValue(tileKey, out tile);

        public CrowdWorldTile2D GetOrCreateTile(long tileKey)
        {
            if (_tiles.TryGetValue(tileKey, out var existing))
            {
                return existing;
            }

            var tile = _tilePool.Count > 0 ? _tilePool.Pop() : new CrowdWorldTile2D(TileSizeCells);
            tile.Reset();
            _tiles[tileKey] = tile;
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
            if (_tiles.Remove(tileKey, out var tile))
            {
                tile.Reset();
                _tilePool.Push(tile);
            }
        }

        public void RemoveTile(long tileKey)
        {
            _retainCounts.Remove(tileKey);
            if (_tiles.Remove(tileKey, out var tile))
            {
                tile.Reset();
                _tilePool.Push(tile);
            }
        }

        public void ClearObstacleField()
        {
            foreach (var tile in _tiles.Values)
            {
                tile.ClearObstacles();
            }
        }

        public void ClearCrowdFields()
        {
            foreach (var tile in _tiles.Values)
            {
                tile.ClearCrowdFields();
            }
        }

        public void NormalizeAverageVelocityField()
        {
            foreach (var tile in _tiles.Values)
            {
                var density = tile.Density;
                var avgVelocityX = tile.AverageVelocityX;
                var avgVelocityY = tile.AverageVelocityY;
                for (int i = 0; i < density.Length; i++)
                {
                    float value = density[i];
                    if (value <= 1e-6f)
                    {
                        avgVelocityX[i] = 0f;
                        avgVelocityY[i] = 0f;
                        continue;
                    }

                    float invDensity = 1f / value;
                    avgVelocityX[i] *= invDensity;
                    avgVelocityY[i] *= invDensity;
                }
            }
        }

        public void SetObstacleCell(int cellX, int cellY, bool blocked, bool createTilesIfMissing = true)
        {
            if (!TryResolveTileIndex(cellX, cellY, createTilesIfMissing, out var tile, out int index))
            {
                return;
            }

            tile!.Obstacles[index] = blocked ? (byte)1 : (byte)0;
        }

        public bool IsBlockedCell(int cellX, int cellY)
        {
            if (!TryResolveTileIndex(cellX, cellY, createTilesIfMissing: false, out var tile, out int index))
            {
                return false;
            }

            return tile!.Obstacles[index] != 0;
        }

        public void SplatObstacleCircle(in Vector2 positionCm, float radiusCm, bool createTilesIfMissing = true)
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

                    SetObstacleCell(cellX, cellY, blocked: true, createTilesIfMissing);
                }
            }
        }

        public void SplatDiscomfortCircle(
            in Vector2 positionCm,
            float radiusCm,
            float centerValue,
            float edgeValue,
            bool createTilesIfMissing = true)
        {
            if (!(radiusCm > 0f) || centerValue <= 0f && edgeValue <= 0f)
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
                    if (IsBlockedCell(cellX, cellY))
                    {
                        continue;
                    }

                    var center = CellCenterToWorldCm(cellX, cellY).ToVector2();
                    Vector2 delta = center - positionCm;
                    float distanceSq = delta.LengthSquared();
                    if (distanceSq > paddedRadiusSq)
                    {
                        continue;
                    }

                    if (!TryResolveTileIndex(cellX, cellY, createTilesIfMissing, out var tile, out int index))
                    {
                        continue;
                    }

                    float t = paddedRadiusSq <= 1e-6f ? 0f : Math.Clamp(distanceSq / paddedRadiusSq, 0f, 1f);
                    tile!.Discomfort[index] += Lerp(centerValue, edgeValue, t);
                }
            }
        }

        public void SplatDensity(
            in Vector2 positionCm,
            in Vector2 velocityCmPerSec,
            float exponent,
            bool createTilesIfMissing = true)
        {
            float cellSize = CellSizeCm.ToFloat();
            if (!(cellSize > 1e-6f))
            {
                return;
            }

            float pointX = positionCm.X / cellSize;
            float pointY = positionCm.Y / cellSize;

            int cellAX = (int)MathF.Floor(pointX - 0.5f);
            int cellAY = (int)MathF.Floor(pointY - 0.5f);
            float deltaX = Math.Clamp(pointX - (cellAX + 0.5f), 0f, 1f);
            float deltaY = Math.Clamp(pointY - (cellAY + 0.5f), 0f, 1f);
            float oneMinusDeltaX = 1f - deltaX;
            float oneMinusDeltaY = 1f - deltaY;
            float safeExponent = Math.Clamp(exponent, 0f, 1f);

            TryAccumulateDensityCell(cellAX, cellAY, ComputeDensityWeight(oneMinusDeltaX, oneMinusDeltaY, safeExponent), velocityCmPerSec, createTilesIfMissing);
            TryAccumulateDensityCell(cellAX + 1, cellAY, ComputeDensityWeight(deltaX, oneMinusDeltaY, safeExponent), velocityCmPerSec, createTilesIfMissing);
            TryAccumulateDensityCell(cellAX + 1, cellAY + 1, ComputeDensityWeight(deltaX, deltaY, safeExponent), velocityCmPerSec, createTilesIfMissing);
            TryAccumulateDensityCell(cellAX, cellAY + 1, ComputeDensityWeight(oneMinusDeltaX, deltaY, safeExponent), velocityCmPerSec, createTilesIfMissing);
        }

        public bool TryGetDensityCell(int cellX, int cellY, out float density)
        {
            if (!TryResolveTileIndex(cellX, cellY, createTilesIfMissing: false, out var tile, out int index))
            {
                density = 0f;
                return false;
            }

            density = tile!.Density[index];
            return true;
        }

        public bool TryGetDiscomfortCell(int cellX, int cellY, out float discomfort)
        {
            if (!TryResolveTileIndex(cellX, cellY, createTilesIfMissing: false, out var tile, out int index))
            {
                discomfort = 0f;
                return false;
            }

            discomfort = tile!.Discomfort[index];
            return true;
        }

        public bool TryGetAverageVelocityCell(int cellX, int cellY, out Vector2 averageVelocity)
        {
            if (!TryResolveTileIndex(cellX, cellY, createTilesIfMissing: false, out var tile, out int index))
            {
                averageVelocity = Vector2.Zero;
                return false;
            }

            averageVelocity = new Vector2(tile!.AverageVelocityX[index], tile.AverageVelocityY[index]);
            return true;
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
        private bool TryResolveTileIndex(int cellX, int cellY, bool createTilesIfMissing, out CrowdWorldTile2D? tile, out int index)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            if (!_tiles.TryGetValue(tileKey, out tile))
            {
                if (!createTilesIfMissing)
                {
                    index = 0;
                    return false;
                }

                tile = GetOrCreateTile(tileKey);
            }

            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            index = ly * TileSizeCells + lx;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryAccumulateDensityCell(int cellX, int cellY, float densityContribution, in Vector2 velocityCmPerSec, bool createTilesIfMissing)
        {
            if (densityContribution <= 1e-6f || IsBlockedCell(cellX, cellY))
            {
                return;
            }

            if (!TryResolveTileIndex(cellX, cellY, createTilesIfMissing, out var tile, out int index))
            {
                return;
            }

            tile!.Density[index] += densityContribution;
            tile.AverageVelocityX[index] += velocityCmPerSec.X * densityContribution;
            tile.AverageVelocityY[index] += velocityCmPerSec.Y * densityContribution;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeDensityWeight(float x, float y, float exponent)
        {
            float weight = MathF.Min(x, y);
            if (weight <= 0f)
            {
                return 0f;
            }

            if (exponent <= 1e-6f)
            {
                return 1f;
            }

            return MathF.Pow(weight, exponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FloorToCell(float worldCm)
        {
            return (Fix64.FromFloat(worldCm) / CellSizeCm).FloorToInt();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
