using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.LowLevel;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Spatial;

namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdFlow2D : IDisposable
    {
        private readonly CrowdSurface2D _surface;
        private readonly Dictionary<long, CrowdFlowTile2D> _tiles;
        private readonly HashSet<long> _loadedTiles;
        private readonly Dictionary<long, int> _activeTileExpiryTicks;
        private readonly List<TileCandidate> _candidateTiles;
        private readonly List<long> _removalScratch;
        private UnsafeQueue<long> _frontier;

        private Fix64Vec2 _goalCm;
        private Fix64 _goalRadiusCm;
        private bool _hasGoal;
        private bool _needsRebuild;

        private readonly int _tileShift;
        private readonly int _tileMask;
        private Navigation2DFlowStreamingConfig _streamingConfig;

        private int _currentTick;
        private bool _hasDemandBounds;
        private int _demandMinTileX;
        private int _demandMinTileY;
        private int _demandMaxTileX;
        private int _demandMaxTileY;

        public float MaxPotential { get; set; } = 300f;
        public int ActiveTileCount => _tiles.Count;
        public int LoadedTileCount => _loadedTiles.Count;

        public CrowdFlow2D(
            CrowdSurface2D surface,
            Navigation2DFlowStreamingConfig? streamingConfig = null,
            int initialTileCapacity = 256,
            int initialFrontierCapacity = 1024)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            _tiles = new Dictionary<long, CrowdFlowTile2D>(Math.Max(8, initialTileCapacity));
            _loadedTiles = new HashSet<long>();
            _activeTileExpiryTicks = new Dictionary<long, int>(Math.Max(8, initialTileCapacity));
            _candidateTiles = new List<TileCandidate>(Math.Max(8, initialTileCapacity));
            _removalScratch = new List<long>(Math.Max(8, initialTileCapacity));
            _frontier = new UnsafeQueue<long>(Math.Max(16, initialFrontierCapacity));

            int tileSize = surface.TileSizeCells;
            _tileMask = tileSize - 1;
            _tileShift = BitOperations.TrailingZeroCount((uint)tileSize);
            _goalCm = default;
            _goalRadiusCm = Fix64.Zero;
            _hasGoal = false;
            _needsRebuild = true;
            _streamingConfig = streamingConfig ?? new Navigation2DFlowStreamingConfig();
            MaxPotential = _streamingConfig.MaxPotentialCells;
        }

        public void ConfigureStreaming(Navigation2DFlowStreamingConfig config)
        {
            _streamingConfig = config ?? new Navigation2DFlowStreamingConfig();
            MaxPotential = _streamingConfig.MaxPotentialCells;
            _needsRebuild = true;
        }

        public void SetGoalPoint(in Fix64Vec2 goalCm, Fix64 radiusCm)
        {
            bool changed = !_hasGoal || _goalCm.X != goalCm.X || _goalCm.Y != goalCm.Y || _goalRadiusCm != radiusCm;
            _goalCm = goalCm;
            _goalRadiusCm = radiusCm;
            _hasGoal = true;
            if (changed)
            {
                _needsRebuild = true;
            }
        }

        public void BeginDemandFrame(int tick)
        {
            _currentTick = tick;
            _hasDemandBounds = false;
        }

        public void AddDemandPoint(in Fix64Vec2 positionCm)
        {
            WorldToTile(positionCm, out int tileX, out int tileY);
            if (!_hasDemandBounds)
            {
                _demandMinTileX = tileX;
                _demandMinTileY = tileY;
                _demandMaxTileX = tileX;
                _demandMaxTileY = tileY;
                _hasDemandBounds = true;
                return;
            }

            if (tileX < _demandMinTileX) _demandMinTileX = tileX;
            if (tileY < _demandMinTileY) _demandMinTileY = tileY;
            if (tileX > _demandMaxTileX) _demandMaxTileX = tileX;
            if (tileY > _demandMaxTileY) _demandMaxTileY = tileY;
        }

        public bool IsTileActive(long tileKey) => _tiles.ContainsKey(tileKey);

        public void OnTileLoaded(long tileKey)
        {
            _loadedTiles.Add(tileKey);
            _needsRebuild = true;
        }

        public void OnTileUnloaded(long tileKey)
        {
            _loadedTiles.Remove(tileKey);
            _activeTileExpiryTicks.Remove(tileKey);
            if (_tiles.Remove(tileKey))
            {
                _surface.ReleaseTile(tileKey);
                _needsRebuild = true;
            }
        }

        public void Step(int iterations)
        {
            RefreshActiveTiles();
            if (iterations <= 0)
            {
                return;
            }

            if (_needsRebuild)
            {
                Rebuild();
            }

            if (_frontier.Count == 0)
            {
                return;
            }

            const float DiagCost = 1.41421356f;
            int remaining = iterations;
            while (remaining-- > 0 && _frontier.Count > 0)
            {
                long cellKey = _frontier.Dequeue();
                Nav2DKeyPacking.UnpackInt2(cellKey, out int cx, out int cy);
                float current = GetPotential(cx, cy);
                if (float.IsPositiveInfinity(current))
                {
                    continue;
                }

                TryRelaxNeighbor(cx + 1, cy, current, 1f);
                TryRelaxNeighbor(cx - 1, cy, current, 1f);
                TryRelaxNeighbor(cx, cy + 1, current, 1f);
                TryRelaxNeighbor(cx, cy - 1, current, 1f);
                TryRelaxNeighbor(cx + 1, cy + 1, current, DiagCost);
                TryRelaxNeighbor(cx + 1, cy - 1, current, DiagCost);
                TryRelaxNeighbor(cx - 1, cy + 1, current, DiagCost);
                TryRelaxNeighbor(cx - 1, cy - 1, current, DiagCost);
            }
        }

        public bool TrySampleDesiredVelocityCm(in Fix64Vec2 positionCm, Fix64 maxSpeedCmPerSec, out Fix64Vec2 desiredVelocityCmPerSec)
        {
            desiredVelocityCmPerSec = default;

            if (_needsRebuild)
            {
                Rebuild();
            }

            _surface.WorldToCell(positionCm, out int cx, out int cy);
            float p0 = GetPotential(cx, cy);
            if (float.IsPositiveInfinity(p0) || p0 <= 0.001f)
            {
                return false;
            }

            float pxp = GetPotentialClamped(cx + 1, cy, p0);
            float pxn = GetPotentialClamped(cx - 1, cy, p0);
            float pyp = GetPotentialClamped(cx, cy + 1, p0);
            float pyn = GetPotentialClamped(cx, cy - 1, p0);
            float ppp = GetPotentialClamped(cx + 1, cy + 1, p0);
            float ppn = GetPotentialClamped(cx + 1, cy - 1, p0);
            float pnp = GetPotentialClamped(cx - 1, cy + 1, p0);
            float pnn = GetPotentialClamped(cx - 1, cy - 1, p0);

            const float diag = 0.7071f;
            float gx = (pxp - pxn) + diag * ((ppp - pnp) + (ppn - pnn));
            float gy = (pyp - pyn) + diag * ((ppp - ppn) + (pnp - pnn));
            float dx = -gx;
            float dy = -gy;

            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f)
            {
                return false;
            }

            float invLen = 1f / len;
            float maxSpeed = maxSpeedCmPerSec.ToFloat();
            desiredVelocityCmPerSec = Fix64Vec2.FromFloat(dx * invLen * maxSpeed, dy * invLen * maxSpeed);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetPotentialClamped(int cellX, int cellY, float fallback)
        {
            if (_surface.IsBlockedCell(cellX, cellY))
            {
                return fallback;
            }

            float value = GetPotential(cellX, cellY);
            return float.IsPositiveInfinity(value) ? fallback : value;
        }

        private void RefreshActiveTiles()
        {
            if (!_streamingConfig.Enabled)
            {
                MirrorLoadedTiles();
                return;
            }

            _candidateTiles.Clear();
            if (TryBuildActivationWindow(out int minTileX, out int minTileY, out int maxTileX, out int maxTileY, out int priorityTileX, out int priorityTileY))
            {
                foreach (long tileKey in _loadedTiles)
                {
                    Nav2DKeyPacking.UnpackInt2(tileKey, out int tileX, out int tileY);
                    if (tileX < minTileX || tileX > maxTileX || tileY < minTileY || tileY > maxTileY)
                    {
                        continue;
                    }

                    int distance = Math.Abs(tileX - priorityTileX) + Math.Abs(tileY - priorityTileY);
                    int priority = _tiles.ContainsKey(tileKey) ? distance : distance + 1024;
                    _candidateTiles.Add(new TileCandidate(tileKey, priority));
                }

                _candidateTiles.Sort(static (a, b) =>
                {
                    int priorityCompare = a.Priority.CompareTo(b.Priority);
                    return priorityCompare != 0 ? priorityCompare : a.TileKey.CompareTo(b.TileKey);
                });

                int maxActive = Math.Max(1, _streamingConfig.MaxActiveTilesPerFlow);
                int takeCount = Math.Min(maxActive, _candidateTiles.Count);
                int expiryTick = _currentTick + _streamingConfig.UnloadGraceTicks;
                for (int i = 0; i < takeCount; i++)
                {
                    long tileKey = _candidateTiles[i].TileKey;
                    EnsureTileActive(tileKey);
                    _activeTileExpiryTicks[tileKey] = expiryTick;
                }
            }

            _removalScratch.Clear();
            foreach (var kvp in _activeTileExpiryTicks)
            {
                if (_loadedTiles.Contains(kvp.Key) && kvp.Value >= _currentTick)
                {
                    continue;
                }

                _removalScratch.Add(kvp.Key);
            }

            for (int i = 0; i < _removalScratch.Count; i++)
            {
                long tileKey = _removalScratch[i];
                _activeTileExpiryTicks.Remove(tileKey);
                if (_tiles.Remove(tileKey))
                {
                    _surface.ReleaseTile(tileKey);
                    _needsRebuild = true;
                }
            }
        }

        private bool TryBuildActivationWindow(out int minTileX, out int minTileY, out int maxTileX, out int maxTileY, out int priorityTileX, out int priorityTileY)
        {
            int radius = Math.Max(0, _streamingConfig.ActivationRadiusTiles);
            if (_hasGoal)
            {
                WorldToTile(_goalCm, out int goalTileX, out int goalTileY);
                priorityTileX = goalTileX;
                priorityTileY = goalTileY;

                if (_hasDemandBounds)
                {
                    minTileX = Math.Min(_demandMinTileX, goalTileX) - radius;
                    minTileY = Math.Min(_demandMinTileY, goalTileY) - radius;
                    maxTileX = Math.Max(_demandMaxTileX, goalTileX) + radius;
                    maxTileY = Math.Max(_demandMaxTileY, goalTileY) + radius;
                    return true;
                }

                minTileX = goalTileX - radius;
                minTileY = goalTileY - radius;
                maxTileX = goalTileX + radius;
                maxTileY = goalTileY + radius;
                return true;
            }

            if (_hasDemandBounds)
            {
                minTileX = _demandMinTileX - radius;
                minTileY = _demandMinTileY - radius;
                maxTileX = _demandMaxTileX + radius;
                maxTileY = _demandMaxTileY + radius;
                priorityTileX = (_demandMinTileX + _demandMaxTileX) >> 1;
                priorityTileY = (_demandMinTileY + _demandMaxTileY) >> 1;
                return true;
            }

            minTileX = minTileY = maxTileX = maxTileY = priorityTileX = priorityTileY = 0;
            return false;
        }

        private void MirrorLoadedTiles()
        {
            foreach (long tileKey in _loadedTiles)
            {
                EnsureTileActive(tileKey);
                _activeTileExpiryTicks[tileKey] = int.MaxValue;
            }

            _removalScratch.Clear();
            foreach (long tileKey in _tiles.Keys)
            {
                if (_loadedTiles.Contains(tileKey))
                {
                    continue;
                }

                _removalScratch.Add(tileKey);
            }

            for (int i = 0; i < _removalScratch.Count; i++)
            {
                long tileKey = _removalScratch[i];
                _activeTileExpiryTicks.Remove(tileKey);
                if (_tiles.Remove(tileKey))
                {
                    _surface.ReleaseTile(tileKey);
                    _needsRebuild = true;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureTileActive(long tileKey)
        {
            if (_tiles.ContainsKey(tileKey))
            {
                return;
            }

            _surface.RetainTile(tileKey);
            _tiles[tileKey] = new CrowdFlowTile2D(_surface.TileSizeCells);
            _needsRebuild = true;
        }

        private void Rebuild()
        {
            foreach (var tile in _tiles.Values)
            {
                tile.Reset();
            }

            while (_frontier.Count > 0)
            {
                _frontier.Dequeue();
            }

            if (!_hasGoal)
            {
                _needsRebuild = false;
                return;
            }

            _surface.WorldToCell(_goalCm, out int gx, out int gy);
            if (_goalRadiusCm > Fix64.Zero)
            {
                int radius = (_goalRadiusCm / _surface.CellSizeCm).CeilToInt();
                for (int y = gy - radius; y <= gy + radius; y++)
                {
                    for (int x = gx - radius; x <= gx + radius; x++)
                    {
                        if (_surface.IsBlockedCell(x, y) || !SetPotential(x, y, 0f))
                        {
                            continue;
                        }

                        _frontier.Enqueue(Nav2DKeyPacking.PackInt2(x, y));
                    }
                }
            }
            else if (!_surface.IsBlockedCell(gx, gy) && SetPotential(gx, gy, 0f))
            {
                _frontier.Enqueue(Nav2DKeyPacking.PackInt2(gx, gy));
            }

            _needsRebuild = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetPotential(int cellX, int cellY)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            if (!_tiles.TryGetValue(tileKey, out var tile))
            {
                return float.PositiveInfinity;
            }

            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            return tile.Potential[ly * _surface.TileSizeCells + lx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SetPotential(int cellX, int cellY, float value)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            if (!_tiles.TryGetValue(tileKey, out var tile))
            {
                return false;
            }

            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            tile.Potential[ly * _surface.TileSizeCells + lx] = value;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryRelaxNeighbor(int nx, int ny, float current, float cost)
        {
            float next = current + cost;
            if (next > MaxPotential || _surface.IsBlockedCell(nx, ny))
            {
                return;
            }

            float old = GetPotential(nx, ny);
            if (next >= old || !SetPotential(nx, ny, next))
            {
                return;
            }

            _frontier.Enqueue(Nav2DKeyPacking.PackInt2(nx, ny));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WorldToTile(in Fix64Vec2 worldCm, out int tileX, out int tileY)
        {
            _surface.WorldToCell(worldCm, out int cellX, out int cellY);
            tileX = cellX >> _tileShift;
            tileY = cellY >> _tileShift;
        }

        public void Dispose()
        {
            _frontier.Dispose();
        }

        private readonly record struct TileCandidate(long TileKey, int Priority);
    }
}
