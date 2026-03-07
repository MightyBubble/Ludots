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
        private readonly HashSet<long> _selectedTileSet;
        private readonly List<long> _selectedTiles;
        private readonly List<long> _removalScratch;
        private UnsafeQueue<long> _frontier;

        private readonly int _tileShift;
        private readonly int _tileMask;
        private readonly int _tileSizeCells;

        private Fix64Vec2 _goalCm;
        private Fix64 _goalRadiusCm;
        private bool _hasGoal;
        private bool _needsRebuild;

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

        public int InstrumentedFullRebuilds { get; private set; }
        public int InstrumentedWindowWidthTiles { get; private set; }
        public int InstrumentedWindowHeightTiles { get; private set; }
        public int InstrumentedWindowTileChecksFrame { get; private set; }
        public int InstrumentedSelectedTilesFrame { get; private set; }
        public int InstrumentedRetainedTilesFrame { get; private set; }
        public int InstrumentedNewTilesActivatedFrame { get; private set; }
        public int InstrumentedEvictedTilesFrame { get; private set; }
        public int InstrumentedIncrementalSeededTilesFrame { get; private set; }
        public int InstrumentedGoalSeedCellsFrame { get; private set; }
        public int InstrumentedFrontierEnqueuesFrame { get; private set; }
        public int InstrumentedFrontierProcessedFrame { get; private set; }
        public bool InstrumentedWindowWorldClampedFrame { get; private set; }
        public bool InstrumentedWindowBudgetClampedFrame { get; private set; }

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
            _selectedTileSet = new HashSet<long>(Math.Max(8, initialTileCapacity));
            _selectedTiles = new List<long>(Math.Max(8, initialTileCapacity));
            _removalScratch = new List<long>(Math.Max(8, initialTileCapacity));
            _frontier = new UnsafeQueue<long>(Math.Max(16, initialFrontierCapacity));

            _tileSizeCells = surface.TileSizeCells;
            _tileMask = _tileSizeCells - 1;
            _tileShift = BitOperations.TrailingZeroCount((uint)_tileSizeCells);
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
            ResetFrameInstrumentation();
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
                InstrumentedFrontierProcessedFrame++;
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

            if (!TryBuildActivationWindow(out int minTileX, out int minTileY, out int maxTileX, out int maxTileY, out int priorityTileX, out int priorityTileY) ||
                !ClampActivationWindow(ref minTileX, ref minTileY, ref maxTileX, ref maxTileY, ref priorityTileX, ref priorityTileY))
            {
                InstrumentedWindowWidthTiles = 0;
                InstrumentedWindowHeightTiles = 0;
                InstrumentedSelectedTilesFrame = 0;
                RemoveExpiredTiles();
                return;
            }

            InstrumentedWindowWidthTiles = maxTileX - minTileX + 1;
            InstrumentedWindowHeightTiles = maxTileY - minTileY + 1;

            _candidateTiles.Clear();
            _selectedTileSet.Clear();
            _selectedTiles.Clear();

            foreach (long tileKey in _tiles.Keys)
            {
                if (!_loadedTiles.Contains(tileKey))
                {
                    continue;
                }

                Nav2DKeyPacking.UnpackInt2(tileKey, out int tileX, out int tileY);
                if (tileX < minTileX || tileX > maxTileX || tileY < minTileY || tileY > maxTileY)
                {
                    continue;
                }

                int distance = Math.Abs(tileX - priorityTileX) + Math.Abs(tileY - priorityTileY);
                _candidateTiles.Add(new TileCandidate(tileKey, distance));
            }

            _candidateTiles.Sort(static (a, b) =>
            {
                int priorityCompare = a.Priority.CompareTo(b.Priority);
                return priorityCompare != 0 ? priorityCompare : a.TileKey.CompareTo(b.TileKey);
            });

            int maxActive = Math.Max(1, _streamingConfig.MaxActiveTilesPerFlow);
            int retainedCount = Math.Min(maxActive, _candidateTiles.Count);
            for (int i = 0; i < retainedCount; i++)
            {
                long tileKey = _candidateTiles[i].TileKey;
                _selectedTileSet.Add(tileKey);
                _selectedTiles.Add(tileKey);
            }

            InstrumentedRetainedTilesFrame = retainedCount;
            if (_selectedTiles.Count < maxActive)
            {
                FillSelectionByPriority(minTileX, minTileY, maxTileX, maxTileY, priorityTileX, priorityTileY, maxActive - _selectedTiles.Count);
            }

            InstrumentedSelectedTilesFrame = _selectedTiles.Count;
            int expiryTick = _currentTick + _streamingConfig.UnloadGraceTicks;
            for (int i = 0; i < _selectedTiles.Count; i++)
            {
                long tileKey = _selectedTiles[i];
                bool added = EnsureTileActive(tileKey, allowIncrementalSeeding: true);
                _activeTileExpiryTicks[tileKey] = expiryTick;
                if (added)
                {
                    InstrumentedNewTilesActivatedFrame++;
                }
            }

            RemoveExpiredTiles();
        }

        private void FillSelectionByPriority(int minTileX, int minTileY, int maxTileX, int maxTileY, int priorityTileX, int priorityTileY, int remainingSlots)
        {
            int maxRing = Math.Max(Math.Abs(priorityTileX - minTileX), Math.Abs(maxTileX - priorityTileX))
                + Math.Max(Math.Abs(priorityTileY - minTileY), Math.Abs(maxTileY - priorityTileY));

            for (int radius = 0; radius <= maxRing && remainingSlots > 0; radius++)
            {
                for (int dx = -radius; dx <= radius && remainingSlots > 0; dx++)
                {
                    int dy = radius - Math.Abs(dx);
                    if (TrySelectLoadedTile(priorityTileX + dx, priorityTileY + dy, minTileX, minTileY, maxTileX, maxTileY))
                    {
                        remainingSlots--;
                    }

                    if (dy != 0 && remainingSlots > 0 &&
                        TrySelectLoadedTile(priorityTileX + dx, priorityTileY - dy, minTileX, minTileY, maxTileX, maxTileY))
                    {
                        remainingSlots--;
                    }
                }
            }
        }

        private bool TrySelectLoadedTile(int tileX, int tileY, int minTileX, int minTileY, int maxTileX, int maxTileY)
        {
            if (tileX < minTileX || tileX > maxTileX || tileY < minTileY || tileY > maxTileY)
            {
                return false;
            }

            InstrumentedWindowTileChecksFrame++;
            long tileKey = Nav2DKeyPacking.PackInt2(tileX, tileY);
            if (_selectedTileSet.Contains(tileKey) || _tiles.ContainsKey(tileKey) || !_loadedTiles.Contains(tileKey))
            {
                return false;
            }

            _selectedTileSet.Add(tileKey);
            _selectedTiles.Add(tileKey);
            return true;
        }

        private void RemoveExpiredTiles()
        {
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
                    InstrumentedEvictedTilesFrame++;
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

        private bool ClampActivationWindow(ref int minTileX, ref int minTileY, ref int maxTileX, ref int maxTileY, ref int priorityTileX, ref int priorityTileY)
        {
            bool worldClamped = false;
            bool budgetClamped = false;

            if (_streamingConfig.WorldBoundsEnabled)
            {
                int originalMinX = minTileX;
                int originalMinY = minTileY;
                int originalMaxX = maxTileX;
                int originalMaxY = maxTileY;
                minTileX = Math.Max(minTileX, _streamingConfig.WorldMinTileX);
                minTileY = Math.Max(minTileY, _streamingConfig.WorldMinTileY);
                maxTileX = Math.Min(maxTileX, _streamingConfig.WorldMaxTileX);
                maxTileY = Math.Min(maxTileY, _streamingConfig.WorldMaxTileY);
                worldClamped = originalMinX != minTileX || originalMinY != minTileY || originalMaxX != maxTileX || originalMaxY != maxTileY;
            }

            if (minTileX > maxTileX || minTileY > maxTileY)
            {
                InstrumentedWindowWorldClampedFrame = worldClamped;
                InstrumentedWindowBudgetClampedFrame = budgetClamped;
                return false;
            }

            priorityTileX = Math.Clamp(priorityTileX, minTileX, maxTileX);
            priorityTileY = Math.Clamp(priorityTileY, minTileY, maxTileY);
            budgetClamped |= ClampAxisAroundPivot(ref minTileX, ref maxTileX, _streamingConfig.MaxActivationWindowWidthTiles, priorityTileX);
            budgetClamped |= ClampAxisAroundPivot(ref minTileY, ref maxTileY, _streamingConfig.MaxActivationWindowHeightTiles, priorityTileY);

            InstrumentedWindowWorldClampedFrame = worldClamped;
            InstrumentedWindowBudgetClampedFrame = budgetClamped;
            return minTileX <= maxTileX && minTileY <= maxTileY;
        }

        private static bool ClampAxisAroundPivot(ref int minValue, ref int maxValue, int maxSpan, int pivot)
        {
            if (maxSpan <= 0)
            {
                return false;
            }

            int span = maxValue - minValue + 1;
            if (span <= maxSpan)
            {
                return false;
            }

            int halfLow = (maxSpan - 1) / 2;
            int halfHigh = maxSpan - 1 - halfLow;
            int targetMin = pivot - halfLow;
            int targetMax = pivot + halfHigh;

            if (targetMin < minValue)
            {
                targetMax += minValue - targetMin;
                targetMin = minValue;
            }

            if (targetMax > maxValue)
            {
                targetMin -= targetMax - maxValue;
                targetMax = maxValue;
            }

            if (targetMin < minValue)
            {
                targetMin = minValue;
            }

            if (targetMax > maxValue)
            {
                targetMax = maxValue;
            }

            bool changed = targetMin != minValue || targetMax != maxValue;
            minValue = targetMin;
            maxValue = targetMax;
            return changed;
        }

        private void MirrorLoadedTiles()
        {
            bool addedTile = false;
            foreach (long tileKey in _loadedTiles)
            {
                addedTile |= EnsureTileActive(tileKey, allowIncrementalSeeding: false);
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
                    InstrumentedEvictedTilesFrame++;
                    _needsRebuild = true;
                }
            }

            if (addedTile && _hasGoal)
            {
                _needsRebuild = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EnsureTileActive(long tileKey, bool allowIncrementalSeeding)
        {
            if (_tiles.ContainsKey(tileKey))
            {
                return false;
            }

            _surface.RetainTile(tileKey);
            _tiles[tileKey] = new CrowdFlowTile2D(_tileSizeCells);
            if (allowIncrementalSeeding && _hasGoal && !_needsRebuild)
            {
                SeedTileIncrementally(tileKey);
            }

            return true;
        }

        private void SeedTileIncrementally(long tileKey)
        {
            InstrumentedIncrementalSeededTilesFrame++;
            Nav2DKeyPacking.UnpackInt2(tileKey, out int tileX, out int tileY);
            int minCellX = tileX << _tileShift;
            int minCellY = tileY << _tileShift;
            int maxCellX = minCellX + _tileSizeCells - 1;
            int maxCellY = minCellY + _tileSizeCells - 1;

            SeedGoalCells(minCellX, minCellY, maxCellX, maxCellY);

            for (int y = minCellY; y <= maxCellY; y++)
            {
                TrySeedCellFromNeighbor(minCellX, y, minCellX - 1, y, 1f);
                TrySeedCellFromNeighbor(maxCellX, y, maxCellX + 1, y, 1f);
            }

            for (int x = minCellX; x <= maxCellX; x++)
            {
                TrySeedCellFromNeighbor(x, minCellY, x, minCellY - 1, 1f);
                TrySeedCellFromNeighbor(x, maxCellY, x, maxCellY + 1, 1f);
            }

            const float DiagCost = 1.41421356f;
            TrySeedCellFromNeighbor(minCellX, minCellY, minCellX - 1, minCellY - 1, DiagCost);
            TrySeedCellFromNeighbor(minCellX, maxCellY, minCellX - 1, maxCellY + 1, DiagCost);
            TrySeedCellFromNeighbor(maxCellX, minCellY, maxCellX + 1, minCellY - 1, DiagCost);
            TrySeedCellFromNeighbor(maxCellX, maxCellY, maxCellX + 1, maxCellY + 1, DiagCost);
        }

        private void TrySeedCellFromNeighbor(int targetCellX, int targetCellY, int sourceCellX, int sourceCellY, float cost)
        {
            if (_surface.IsBlockedCell(targetCellX, targetCellY))
            {
                return;
            }

            float source = GetPotential(sourceCellX, sourceCellY);
            if (float.IsPositiveInfinity(source))
            {
                return;
            }

            float next = source + cost;
            if (next > MaxPotential || !TrySetPotentialMin(targetCellX, targetCellY, next))
            {
                return;
            }

            EnqueueCell(targetCellX, targetCellY);
        }

        private void Rebuild()
        {
            foreach (var tile in _tiles.Values)
            {
                tile.Reset();
            }

            ClearFrontier();
            InstrumentedFullRebuilds++;

            if (!_hasGoal)
            {
                _needsRebuild = false;
                return;
            }

            SeedGoalCells(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
            _needsRebuild = false;
        }

        private void SeedGoalCells(int minCellX, int minCellY, int maxCellX, int maxCellY)
        {
            _surface.WorldToCell(_goalCm, out int gx, out int gy);
            if (_goalRadiusCm > Fix64.Zero)
            {
                int radius = (_goalRadiusCm / _surface.CellSizeCm).CeilToInt();
                int seedMinX = Math.Max(gx - radius, minCellX);
                int seedMinY = Math.Max(gy - radius, minCellY);
                int seedMaxX = Math.Min(gx + radius, maxCellX);
                int seedMaxY = Math.Min(gy + radius, maxCellY);
                for (int y = seedMinY; y <= seedMaxY; y++)
                {
                    for (int x = seedMinX; x <= seedMaxX; x++)
                    {
                        if (_surface.IsBlockedCell(x, y) || !TrySetPotentialMin(x, y, 0f))
                        {
                            continue;
                        }

                        InstrumentedGoalSeedCellsFrame++;
                        EnqueueCell(x, y);
                    }
                }

                return;
            }

            if (_surface.IsBlockedCell(gx, gy) || gx < minCellX || gx > maxCellX || gy < minCellY || gy > maxCellY || !TrySetPotentialMin(gx, gy, 0f))
            {
                return;
            }

            InstrumentedGoalSeedCellsFrame++;
            EnqueueCell(gx, gy);
        }

        private void ClearFrontier()
        {
            while (_frontier.Count > 0)
            {
                _frontier.Dequeue();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnqueueCell(int cellX, int cellY)
        {
            _frontier.Enqueue(Nav2DKeyPacking.PackInt2(cellX, cellY));
            InstrumentedFrontierEnqueuesFrame++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetPotential(int cellX, int cellY)
        {
            if (!TryResolvePotentialSlot(cellX, cellY, out float[]? potential, out int index))
            {
                return float.PositiveInfinity;
            }

            return potential[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TrySetPotentialMin(int cellX, int cellY, float value)
        {
            if (!TryResolvePotentialSlot(cellX, cellY, out float[]? potential, out int index))
            {
                return false;
            }

            if (value >= potential[index])
            {
                return false;
            }

            potential[index] = value;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryResolvePotentialSlot(int cellX, int cellY, out float[]? potential, out int index)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            if (!_tiles.TryGetValue(tileKey, out var tile))
            {
                potential = null;
                index = 0;
                return false;
            }

            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            potential = tile.Potential;
            index = ly * _tileSizeCells + lx;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryRelaxNeighbor(int nx, int ny, float current, float cost)
        {
            float next = current + cost;
            if (next > MaxPotential || _surface.IsBlockedCell(nx, ny) || !TrySetPotentialMin(nx, ny, next))
            {
                return;
            }

            EnqueueCell(nx, ny);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WorldToTile(in Fix64Vec2 worldCm, out int tileX, out int tileY)
        {
            _surface.WorldToCell(worldCm, out int cellX, out int cellY);
            tileX = cellX >> _tileShift;
            tileY = cellY >> _tileShift;
        }

        private void ResetFrameInstrumentation()
        {
            InstrumentedWindowWidthTiles = 0;
            InstrumentedWindowHeightTiles = 0;
            InstrumentedWindowTileChecksFrame = 0;
            InstrumentedSelectedTilesFrame = 0;
            InstrumentedRetainedTilesFrame = 0;
            InstrumentedNewTilesActivatedFrame = 0;
            InstrumentedEvictedTilesFrame = 0;
            InstrumentedIncrementalSeededTilesFrame = 0;
            InstrumentedGoalSeedCellsFrame = 0;
            InstrumentedFrontierEnqueuesFrame = 0;
            InstrumentedFrontierProcessedFrame = 0;
            InstrumentedWindowWorldClampedFrame = false;
            InstrumentedWindowBudgetClampedFrame = false;
        }

        public void Dispose()
        {
            _frontier.Dispose();
        }

        private readonly record struct TileCandidate(long TileKey, int Priority);
    }
}
