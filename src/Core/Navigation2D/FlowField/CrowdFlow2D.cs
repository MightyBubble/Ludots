using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Collections;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Spatial;

namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdFlow2D : IDisposable
    {
        private readonly CrowdSurface2D _surface;
        private readonly Dictionary<long, CrowdFlowTile2D> _tiles;
        private readonly Stack<CrowdFlowTile2D> _tilePool;
        private readonly HashSet<long> _loadedTiles;
        private readonly Dictionary<long, int> _activeTileExpiryTicks;
        private readonly List<TileCandidate> _candidateTiles;
        private readonly HashSet<long> _selectedTileSet;
        private readonly List<long> _selectedTiles;
        private readonly List<long> _removalScratch;
        private readonly HashSet<long> _manualGoalCellsSet;
        private readonly List<long> _manualGoalCells;
        private readonly HashSet<long> _frameGoalCellsSet;
        private readonly List<long> _frameGoalCells;
        private readonly PriorityQueue<long> _frontier;
        private bool _hasLoadedTileSource;

        private readonly int _tileShift;
        private readonly int _tileMask;
        private readonly int _tileSizeCells;

        private bool _hasManualGoalBounds;
        private int _manualGoalMinTileX;
        private int _manualGoalMinTileY;
        private int _manualGoalMaxTileX;
        private int _manualGoalMaxTileY;
        private bool _hasFrameGoalBounds;
        private int _frameGoalMinTileX;
        private int _frameGoalMinTileY;
        private int _frameGoalMaxTileX;
        private int _frameGoalMaxTileY;
        private bool _needsRebuild;
        private bool _traversalCostDirty;
        private bool _framePrepared;
        private bool _forceFullSolve;

        private Navigation2DFlowStreamingConfig _streamingConfig;
        private Navigation2DFlowCrowdConfig _crowdConfig;
        private float _normalizedDistanceWeight;
        private float _normalizedTimeWeight;
        private float _discomfortWeight;

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
            Navigation2DFlowCrowdConfig? crowdConfig = null,
            bool hasLoadedTileSource = false,
            int initialTileCapacity = 256,
            int initialGoalCapacity = 1024,
            int initialFrontierCapacity = 1024)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            _tiles = new Dictionary<long, CrowdFlowTile2D>(Math.Max(8, initialTileCapacity));
            _tilePool = new Stack<CrowdFlowTile2D>(Math.Max(8, initialTileCapacity));
            _loadedTiles = new HashSet<long>();
            _activeTileExpiryTicks = new Dictionary<long, int>(Math.Max(8, initialTileCapacity));
            _candidateTiles = new List<TileCandidate>(Math.Max(8, initialTileCapacity));
            _selectedTileSet = new HashSet<long>(Math.Max(8, initialTileCapacity));
            _selectedTiles = new List<long>(Math.Max(8, initialTileCapacity));
            _removalScratch = new List<long>(Math.Max(8, initialTileCapacity));
            _manualGoalCellsSet = new HashSet<long>(16);
            _manualGoalCells = new List<long>(16);
            _frameGoalCellsSet = new HashSet<long>(Math.Max(16, initialGoalCapacity));
            _frameGoalCells = new List<long>(Math.Max(16, initialGoalCapacity));
            _hasLoadedTileSource = hasLoadedTileSource;

            _tileSizeCells = surface.TileSizeCells;
            _tileMask = _tileSizeCells - 1;
            _tileShift = BitOperations.TrailingZeroCount((uint)_tileSizeCells);
            _hasManualGoalBounds = false;
            _hasFrameGoalBounds = false;
            _needsRebuild = true;
            _traversalCostDirty = true;
            _framePrepared = false;
            _forceFullSolve = false;

            _streamingConfig = streamingConfig ?? new Navigation2DFlowStreamingConfig();
            _crowdConfig = crowdConfig ?? new Navigation2DFlowCrowdConfig();
            MaxPotential = _streamingConfig.MaxPotentialCells;
            _frontier = new PriorityQueue<long>(EstimateFrontierCapacity(initialFrontierCapacity, _streamingConfig.MaxActiveTilesPerFlow));
            NormalizeCostWeights();
        }

        public void ConfigureStreaming(Navigation2DFlowStreamingConfig config)
        {
            _streamingConfig = config ?? new Navigation2DFlowStreamingConfig();
            MaxPotential = _streamingConfig.MaxPotentialCells;
            _frontier.EnsureCapacity(EstimateFrontierCapacity(_frontier.Capacity, _streamingConfig.MaxActiveTilesPerFlow));
            _needsRebuild = true;
            _traversalCostDirty = true;
        }

        public void ConfigureCrowd(Navigation2DFlowCrowdConfig config)
        {
            _crowdConfig = config ?? new Navigation2DFlowCrowdConfig();
            NormalizeCostWeights();
            _needsRebuild = true;
            _traversalCostDirty = true;
        }

        public void SetGoalPoint(in Fix64Vec2 goalCm, Fix64 radiusCm)
        {
            ClearManualGoalsInternal();
            AddGoalPointInternal(
                _manualGoalCellsSet,
                _manualGoalCells,
                ref _hasManualGoalBounds,
                ref _manualGoalMinTileX,
                ref _manualGoalMinTileY,
                ref _manualGoalMaxTileX,
                ref _manualGoalMaxTileY,
                goalCm,
                radiusCm);
            _needsRebuild = true;
        }

        public void BeginGoalFrame()
        {
            if (_frameGoalCells.Count > 0 || _hasFrameGoalBounds)
            {
                _needsRebuild = true;
            }

            _frameGoalCellsSet.Clear();
            _frameGoalCells.Clear();
            _hasFrameGoalBounds = false;
        }

        public void AddFrameGoalPoint(in Fix64Vec2 goalCm, Fix64 radiusCm)
        {
            AddGoalPointInternal(
                _frameGoalCellsSet,
                _frameGoalCells,
                ref _hasFrameGoalBounds,
                ref _frameGoalMinTileX,
                ref _frameGoalMinTileY,
                ref _frameGoalMaxTileX,
                ref _frameGoalMaxTileY,
                goalCm,
                radiusCm);
            _needsRebuild = true;
        }

        public void BeginDemandFrame(int tick)
        {
            _currentTick = tick;
            _hasDemandBounds = false;
            _framePrepared = false;
            _forceFullSolve = false;
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

        public void PrepareFrame()
        {
            if (_framePrepared)
            {
                return;
            }

            ResetFrameInstrumentation();
            RefreshActiveTiles();
            _framePrepared = true;
        }

        public void MarkCrowdFieldsDirty()
        {
            _needsRebuild = true;
            _forceFullSolve = true;
            _traversalCostDirty = true;
        }

        public bool HasAnyGoals => _frameGoalCells.Count > 0 || _manualGoalCells.Count > 0;

        public bool IsTileActive(long tileKey) => _tiles.ContainsKey(tileKey);

        public bool TryGetPotentialAtCell(int cellX, int cellY, out float potential)
        {
            if (!TryResolvePotentialSlot(cellX, cellY, out var values, out int index))
            {
                potential = float.PositiveInfinity;
                return false;
            }

            potential = values![index];
            return true;
        }

        public void OnTileLoaded(long tileKey)
        {
            _loadedTiles.Add(tileKey);
        }

        public void SetLoadedTileSourceEnabled(bool enabled)
        {
            if (_hasLoadedTileSource == enabled)
            {
                return;
            }

            _hasLoadedTileSource = enabled;
            _loadedTiles.Clear();
            _framePrepared = false;
            _needsRebuild = true;
            _traversalCostDirty = true;
        }

        public void OnTileUnloaded(long tileKey)
        {
            _loadedTiles.Remove(tileKey);
            _activeTileExpiryTicks.Remove(tileKey);
            if (_tiles.Remove(tileKey, out var tile))
            {
                _surface.ReleaseTile(tileKey);
                tile.Reset();
                _tilePool.Push(tile);
                _needsRebuild = true;
                _traversalCostDirty = true;
            }
        }

        public void Step(int iterations)
        {
            PrepareFrame();

            if (iterations <= 0 && !_forceFullSolve)
            {
                return;
            }

            if (_traversalCostDirty)
            {
                RecalculateTraversalCostField();
                _traversalCostDirty = false;
            }

            if (_needsRebuild)
            {
                Rebuild();
            }

            DrainFrontier(_forceFullSolve ? int.MaxValue : iterations);
            if (_frontier.Count == 0)
            {
                _forceFullSolve = false;
            }
        }

        public bool TrySampleDesiredVelocityCm(in Fix64Vec2 positionCm, Fix64 maxSpeedCmPerSec, out Fix64Vec2 desiredVelocityCmPerSec)
        {
            desiredVelocityCmPerSec = default;

            if (_needsRebuild || _traversalCostDirty || _frontier.Count > 0)
            {
                PrepareFrame();
                if (_traversalCostDirty)
                {
                    RecalculateTraversalCostField();
                    _traversalCostDirty = false;
                }
                if (_needsRebuild)
                {
                    Rebuild();
                }

                DrainFrontier(int.MaxValue);
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
            if (!_streamingConfig.Enabled && _hasLoadedTileSource)
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
                if (!IsTileSelectable(tileKey, minTileX, minTileY, maxTileX, maxTileY))
                {
                    continue;
                }

                Nav2DKeyPacking.UnpackInt2(tileKey, out int tileX, out int tileY);
                int distance = Math.Abs(tileX - priorityTileX) + Math.Abs(tileY - priorityTileY);
                _candidateTiles.Add(new TileCandidate(tileKey, distance));
            }

            _candidateTiles.Sort(static (a, b) =>
            {
                int priorityCompare = a.Priority.CompareTo(b.Priority);
                return priorityCompare != 0 ? priorityCompare : a.TileKey.CompareTo(b.TileKey);
            });

            int maxActive = _streamingConfig.Enabled
                ? Math.Max(1, _streamingConfig.MaxActiveTilesPerFlow)
                : Math.Max(1, (maxTileX - minTileX + 1) * (maxTileY - minTileY + 1));
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
                SelectGoalDemandCorridor(minTileX, minTileY, maxTileX, maxTileY, priorityTileX, priorityTileY, maxActive - _selectedTiles.Count);
            }

            if (_selectedTiles.Count < maxActive)
            {
                FillSelectionByPriority(minTileX, minTileY, maxTileX, maxTileY, priorityTileX, priorityTileY, maxActive - _selectedTiles.Count);
            }

            InstrumentedSelectedTilesFrame = _selectedTiles.Count;
            int expiryTick = _streamingConfig.Enabled ? _currentTick + _streamingConfig.UnloadGraceTicks : int.MaxValue;
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
                    if (TrySelectTile(priorityTileX + dx, priorityTileY + dy, minTileX, minTileY, maxTileX, maxTileY))
                    {
                        remainingSlots--;
                    }

                    if (dy != 0 && remainingSlots > 0 &&
                        TrySelectTile(priorityTileX + dx, priorityTileY - dy, minTileX, minTileY, maxTileX, maxTileY))
                    {
                        remainingSlots--;
                    }
                }
            }
        }

        private void SelectGoalDemandCorridor(int minTileX, int minTileY, int maxTileX, int maxTileY, int goalTileX, int goalTileY, int remainingSlots)
        {
            if (!_hasDemandBounds || remainingSlots <= 0)
            {
                return;
            }

            int demandTileX = (_demandMinTileX + _demandMaxTileX) >> 1;
            int demandTileY = (_demandMinTileY + _demandMaxTileY) >> 1;
            int dx = demandTileX - goalTileX;
            int dy = demandTileY - goalTileY;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (steps <= 0)
            {
                TrySelectTile(goalTileX, goalTileY, minTileX, minTileY, maxTileX, maxTileY);
                return;
            }

            for (int step = 0; step <= steps && remainingSlots > 0; step++)
            {
                float t = step / (float)steps;
                int tileX = (int)MathF.Round(goalTileX + dx * t);
                int tileY = (int)MathF.Round(goalTileY + dy * t);
                if (TrySelectTile(tileX, tileY, minTileX, minTileY, maxTileX, maxTileY))
                {
                    remainingSlots--;
                }
            }
        }

        private bool TrySelectTile(int tileX, int tileY, int minTileX, int minTileY, int maxTileX, int maxTileY)
        {
            if (tileX < minTileX || tileX > maxTileX || tileY < minTileY || tileY > maxTileY)
            {
                return false;
            }

            InstrumentedWindowTileChecksFrame++;
            long tileKey = Nav2DKeyPacking.PackInt2(tileX, tileY);
            if (_selectedTileSet.Contains(tileKey) || _tiles.ContainsKey(tileKey))
            {
                return false;
            }

            if (_hasLoadedTileSource && !_loadedTiles.Contains(tileKey))
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
                bool keep = kvp.Value >= _currentTick;
                if (_hasLoadedTileSource && !_loadedTiles.Contains(kvp.Key))
                {
                    keep = false;
                }

                if (keep)
                {
                    continue;
                }

                _removalScratch.Add(kvp.Key);
            }

            for (int i = 0; i < _removalScratch.Count; i++)
            {
                long tileKey = _removalScratch[i];
                _activeTileExpiryTicks.Remove(tileKey);
                if (_tiles.Remove(tileKey, out var tile))
                {
                    _surface.ReleaseTile(tileKey);
                    tile.Reset();
                    _tilePool.Push(tile);
                    InstrumentedEvictedTilesFrame++;
                    _needsRebuild = true;
                    _traversalCostDirty = true;
                }
            }
        }

        private bool TryBuildActivationWindow(out int minTileX, out int minTileY, out int maxTileX, out int maxTileY, out int priorityTileX, out int priorityTileY)
        {
            int radius = Math.Max(0, _streamingConfig.ActivationRadiusTiles);
            if (TryGetActiveGoalBounds(out int goalMinTileX, out int goalMinTileY, out int goalMaxTileX, out int goalMaxTileY))
            {
                priorityTileX = (goalMinTileX + goalMaxTileX) >> 1;
                priorityTileY = (goalMinTileY + goalMaxTileY) >> 1;

                if (_hasDemandBounds)
                {
                    minTileX = Math.Min(_demandMinTileX, goalMinTileX) - radius;
                    minTileY = Math.Min(_demandMinTileY, goalMinTileY) - radius;
                    maxTileX = Math.Max(_demandMaxTileX, goalMaxTileX) + radius;
                    maxTileY = Math.Max(_demandMaxTileY, goalMaxTileY) + radius;
                    return true;
                }

                minTileX = goalMinTileX - radius;
                minTileY = goalMinTileY - radius;
                maxTileX = goalMaxTileX + radius;
                maxTileY = goalMaxTileY + radius;
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
            if (_streamingConfig.Enabled)
            {
                budgetClamped |= ClampAxisAroundPivot(ref minTileX, ref maxTileX, _streamingConfig.MaxActivationWindowWidthTiles, priorityTileX);
                budgetClamped |= ClampAxisAroundPivot(ref minTileY, ref maxTileY, _streamingConfig.MaxActivationWindowHeightTiles, priorityTileY);
            }

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
                if (_tiles.Remove(tileKey, out var tile))
                {
                    _surface.ReleaseTile(tileKey);
                    tile.Reset();
                    _tilePool.Push(tile);
                    InstrumentedEvictedTilesFrame++;
                    _needsRebuild = true;
                    _traversalCostDirty = true;
                }
            }

            if (addedTile && HasAnyGoals)
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
            var tile = _tilePool.Count > 0 ? _tilePool.Pop() : new CrowdFlowTile2D(_tileSizeCells);
            tile.Reset();
            _tiles[tileKey] = tile;
            _traversalCostDirty = true;
            if (allowIncrementalSeeding && HasAnyGoals && !_needsRebuild && !_forceFullSolve)
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
                TrySeedCellFromNeighbor(minCellX, y, minCellX - 1, y, FlowDirection.PosX);
                TrySeedCellFromNeighbor(maxCellX, y, maxCellX + 1, y, FlowDirection.NegX);
            }

            for (int x = minCellX; x <= maxCellX; x++)
            {
                TrySeedCellFromNeighbor(x, minCellY, x, minCellY - 1, FlowDirection.PosY);
                TrySeedCellFromNeighbor(x, maxCellY, x, maxCellY + 1, FlowDirection.NegY);
            }
        }

        private void TrySeedCellFromNeighbor(int targetCellX, int targetCellY, int sourceCellX, int sourceCellY, FlowDirection reverseDirection)
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

            float cost = GetDirectionalTraversalCost(targetCellX, targetCellY, reverseDirection);
            if (float.IsPositiveInfinity(cost))
            {
                return;
            }

            float next = source + cost;
            if (next > MaxPotential || !TrySetPotentialMin(targetCellX, targetCellY, next))
            {
                return;
            }

            EnqueueCell(targetCellX, targetCellY, next);
        }

        private void RecalculateTraversalCostField()
        {
            foreach (var kvp in _tiles)
            {
                Nav2DKeyPacking.UnpackInt2(kvp.Key, out int tileX, out int tileY);
                int baseCellX = tileX << _tileShift;
                int baseCellY = tileY << _tileShift;
                var tile = kvp.Value;
                int index = 0;

                for (int localY = 0; localY < _tileSizeCells; localY++)
                {
                    int cellY = baseCellY + localY;
                    for (int localX = 0; localX < _tileSizeCells; localX++, index++)
                    {
                        int cellX = baseCellX + localX;
                        if (_surface.IsBlockedCell(cellX, cellY))
                        {
                            tile.CostPosX[index] = float.PositiveInfinity;
                            tile.CostNegX[index] = float.PositiveInfinity;
                            tile.CostPosY[index] = float.PositiveInfinity;
                            tile.CostNegY[index] = float.PositiveInfinity;
                            continue;
                        }

                        tile.CostPosX[index] = ComputeDirectionalTraversalCost(cellX, cellY, FlowDirection.PosX);
                        tile.CostNegX[index] = ComputeDirectionalTraversalCost(cellX, cellY, FlowDirection.NegX);
                        tile.CostPosY[index] = ComputeDirectionalTraversalCost(cellX, cellY, FlowDirection.PosY);
                        tile.CostNegY[index] = ComputeDirectionalTraversalCost(cellX, cellY, FlowDirection.NegY);
                    }
                }
            }
        }

        private void Rebuild()
        {
            foreach (var tile in _tiles.Values)
            {
                Array.Fill(tile.Potential, float.PositiveInfinity);
            }

            ClearFrontier();
            InstrumentedFullRebuilds++;

            if (!HasAnyGoals)
            {
                _needsRebuild = false;
                return;
            }

            SeedGoalCells(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
            _needsRebuild = false;
        }

        private void SeedGoalCells(int minCellX, int minCellY, int maxCellX, int maxCellY)
        {
            List<long> goalCells = GetActiveGoalCells();
            for (int i = 0; i < goalCells.Count; i++)
            {
                Nav2DKeyPacking.UnpackInt2(goalCells[i], out int goalCellX, out int goalCellY);
                if (goalCellX < minCellX || goalCellX > maxCellX || goalCellY < minCellY || goalCellY > maxCellY)
                {
                    continue;
                }

                if (_surface.IsBlockedCell(goalCellX, goalCellY) || !TrySetPotentialMin(goalCellX, goalCellY, 0f))
                {
                    continue;
                }

                InstrumentedGoalSeedCellsFrame++;
                EnqueueCell(goalCellX, goalCellY, 0f);
            }
        }

        private void DrainFrontier(int iterations)
        {
            int remaining = iterations;
            while (_frontier.TryDequeue(out long cellKey, out float queuedPotential))
            {
                if (iterations != int.MaxValue && remaining-- <= 0)
                {
                    _frontier.Enqueue(cellKey, queuedPotential);
                    break;
                }

                InstrumentedFrontierProcessedFrame++;
                Nav2DKeyPacking.UnpackInt2(cellKey, out int cx, out int cy);
                float current = GetPotential(cx, cy);
                if (float.IsPositiveInfinity(current) || queuedPotential > current + 1e-4f)
                {
                    continue;
                }

                TryRelaxNeighbor(cx + 1, cy, current, FlowDirection.NegX);
                TryRelaxNeighbor(cx - 1, cy, current, FlowDirection.PosX);
                TryRelaxNeighbor(cx, cy + 1, current, FlowDirection.NegY);
                TryRelaxNeighbor(cx, cy - 1, current, FlowDirection.PosY);
            }
        }

        private void ClearFrontier()
        {
            _frontier.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnqueueCell(int cellX, int cellY, float priority)
        {
            _frontier.Enqueue(Nav2DKeyPacking.PackInt2(cellX, cellY), priority);
            InstrumentedFrontierEnqueuesFrame++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetPotential(int cellX, int cellY)
        {
            if (!TryResolvePotentialSlot(cellX, cellY, out var potential, out int index))
            {
                return float.PositiveInfinity;
            }

            return potential![index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TrySetPotentialMin(int cellX, int cellY, float value)
        {
            if (!TryResolvePotentialSlot(cellX, cellY, out var potential, out int index))
            {
                return false;
            }

            if (value >= potential![index])
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
        private bool TryResolveFlowTile(int cellX, int cellY, out CrowdFlowTile2D? tile, out int index)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            if (!_tiles.TryGetValue(tileKey, out tile))
            {
                index = 0;
                return false;
            }

            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            index = ly * _tileSizeCells + lx;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryRelaxNeighbor(int nx, int ny, float current, FlowDirection reverseDirection)
        {
            if (_surface.IsBlockedCell(nx, ny))
            {
                return;
            }

            float cost = GetDirectionalTraversalCost(nx, ny, reverseDirection);
            if (float.IsPositiveInfinity(cost))
            {
                return;
            }

            float next = current + cost;
            if (next > MaxPotential || !TrySetPotentialMin(nx, ny, next))
            {
                return;
            }

            EnqueueCell(nx, ny, next);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetDirectionalTraversalCost(int cellX, int cellY, FlowDirection direction)
        {
            if (!TryResolveFlowTile(cellX, cellY, out var tile, out int index))
            {
                return float.PositiveInfinity;
            }

            return direction switch
            {
                FlowDirection.PosX => tile!.CostPosX[index],
                FlowDirection.NegX => tile!.CostNegX[index],
                FlowDirection.PosY => tile!.CostPosY[index],
                _ => tile!.CostNegY[index],
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ComputeDirectionalTraversalCost(int cellX, int cellY, FlowDirection direction)
        {
            GetDirectionOffset(direction, out int dx, out int dy, out Vector2 directionVector);
            int targetCellX = cellX + dx;
            int targetCellY = cellY + dy;

            if (_surface.IsBlockedCell(targetCellX, targetCellY) || !TryResolveFlowTile(targetCellX, targetCellY, out _, out _))
            {
                return float.PositiveInfinity;
            }

            float density = 0f;
            _surface.TryGetDensityCell(targetCellX, targetCellY, out density);

            float discomfort = 0f;
            _surface.TryGetDiscomfortCell(targetCellX, targetCellY, out discomfort);

            Vector2 averageVelocity = Vector2.Zero;
            _surface.TryGetAverageVelocityCell(targetCellX, targetCellY, out averageVelocity);

            float speedFactor = ComputeDirectionalSpeedFactor(averageVelocity, directionVector, density);
            return (_normalizedDistanceWeight + (_normalizedTimeWeight / MathF.Max(speedFactor, 0.01f))) + (_discomfortWeight * discomfort);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ComputeDirectionalSpeedFactor(in Vector2 averageVelocity, in Vector2 direction, float density)
        {
            if (!_crowdConfig.Enabled)
            {
                return 1f;
            }

            float densityRange = Math.Max(_crowdConfig.Density.Max - _crowdConfig.Density.Min, 1e-5f);
            float densityLerp = Math.Clamp((density - _crowdConfig.Density.Min) / densityRange, 0f, 1f);
            float projectedFlow = Vector2.Dot(averageVelocity, direction) / Math.Max(_crowdConfig.Speed.FlowVelocityScaleCmPerSec, 1f);
            float flowSpeed = Math.Clamp(projectedFlow, _crowdConfig.Speed.MinFactor, _crowdConfig.Speed.MaxFactor);
            return Math.Clamp(Lerp(_crowdConfig.Speed.MaxFactor, flowSpeed, densityLerp), _crowdConfig.Speed.MinFactor, _crowdConfig.Speed.MaxFactor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsTileSelectable(long tileKey, int minTileX, int minTileY, int maxTileX, int maxTileY)
        {
            if (_hasLoadedTileSource && !_loadedTiles.Contains(tileKey))
            {
                return false;
            }

            Nav2DKeyPacking.UnpackInt2(tileKey, out int tileX, out int tileY);
            return tileX >= minTileX && tileX <= maxTileX && tileY >= minTileY && tileY <= maxTileY;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WorldToTile(in Fix64Vec2 worldCm, out int tileX, out int tileY)
        {
            _surface.WorldToCell(worldCm, out int cellX, out int cellY);
            tileX = cellX >> _tileShift;
            tileY = cellY >> _tileShift;
        }

        private void NormalizeCostWeights()
        {
            float distanceWeight = Math.Max(0f, _crowdConfig.Cost.DistanceWeight);
            float timeWeight = Math.Max(0f, _crowdConfig.Cost.TimeWeight);
            if (_crowdConfig.Cost.NormalizeDistanceAndTimeWeights)
            {
                float sum = distanceWeight + timeWeight;
                if (sum > 1e-6f)
                {
                    float inv = 1f / sum;
                    distanceWeight *= inv;
                    timeWeight *= inv;
                }
                else
                {
                    distanceWeight = 1f;
                    timeWeight = 0f;
                }
            }
            else if (distanceWeight <= 1e-6f && timeWeight <= 1e-6f)
            {
                distanceWeight = 1f;
            }

            _normalizedDistanceWeight = distanceWeight;
            _normalizedTimeWeight = timeWeight;
            _discomfortWeight = Math.Max(0f, _crowdConfig.Cost.DiscomfortWeight);
        }

        private void ClearManualGoalsInternal()
        {
            _manualGoalCellsSet.Clear();
            _manualGoalCells.Clear();
            _hasManualGoalBounds = false;
        }

        private void AddGoalPointInternal(
            HashSet<long> goalSet,
            List<long> goalCells,
            ref bool hasBounds,
            ref int minTileX,
            ref int minTileY,
            ref int maxTileX,
            ref int maxTileY,
            in Fix64Vec2 goalCm,
            Fix64 radiusCm)
        {
            _surface.WorldToCell(goalCm, out int goalCellX, out int goalCellY);
            int radiusCells = radiusCm > Fix64.Zero
                ? (radiusCm / _surface.CellSizeCm).CeilToInt()
                : 0;

            for (int cellY = goalCellY - radiusCells; cellY <= goalCellY + radiusCells; cellY++)
            {
                for (int cellX = goalCellX - radiusCells; cellX <= goalCellX + radiusCells; cellX++)
                {
                    TryAddGoalCell(
                        goalSet,
                        goalCells,
                        ref hasBounds,
                        ref minTileX,
                        ref minTileY,
                        ref maxTileX,
                        ref maxTileY,
                        cellX,
                        cellY);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryAddGoalCell(
            HashSet<long> goalSet,
            List<long> goalCells,
            ref bool hasBounds,
            ref int minTileX,
            ref int minTileY,
            ref int maxTileX,
            ref int maxTileY,
            int cellX,
            int cellY)
        {
            if (_surface.IsBlockedCell(cellX, cellY))
            {
                return;
            }

            long goalKey = Nav2DKeyPacking.PackInt2(cellX, cellY);
            if (!goalSet.Add(goalKey))
            {
                return;
            }

            goalCells.Add(goalKey);
            int tileX = cellX >> _tileShift;
            int tileY = cellY >> _tileShift;
            if (!hasBounds)
            {
                minTileX = maxTileX = tileX;
                minTileY = maxTileY = tileY;
                hasBounds = true;
                return;
            }

            if (tileX < minTileX) minTileX = tileX;
            if (tileY < minTileY) minTileY = tileY;
            if (tileX > maxTileX) maxTileX = tileX;
            if (tileY > maxTileY) maxTileY = tileY;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private List<long> GetActiveGoalCells()
        {
            return _frameGoalCells.Count > 0 ? _frameGoalCells : _manualGoalCells;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetActiveGoalBounds(out int minTileX, out int minTileY, out int maxTileX, out int maxTileY)
        {
            if (_frameGoalCells.Count > 0 && _hasFrameGoalBounds)
            {
                minTileX = _frameGoalMinTileX;
                minTileY = _frameGoalMinTileY;
                maxTileX = _frameGoalMaxTileX;
                maxTileY = _frameGoalMaxTileY;
                return true;
            }

            if (_manualGoalCells.Count > 0 && _hasManualGoalBounds)
            {
                minTileX = _manualGoalMinTileX;
                minTileY = _manualGoalMinTileY;
                maxTileX = _manualGoalMaxTileX;
                maxTileY = _manualGoalMaxTileY;
                return true;
            }

            minTileX = minTileY = maxTileX = maxTileY = 0;
            return false;
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetDirectionOffset(FlowDirection direction, out int dx, out int dy, out Vector2 vector)
        {
            switch (direction)
            {
                case FlowDirection.PosX:
                    dx = 1;
                    dy = 0;
                    vector = Vector2.UnitX;
                    break;
                case FlowDirection.NegX:
                    dx = -1;
                    dy = 0;
                    vector = -Vector2.UnitX;
                    break;
                case FlowDirection.PosY:
                    dx = 0;
                    dy = 1;
                    vector = Vector2.UnitY;
                    break;
                default:
                    dx = 0;
                    dy = -1;
                    vector = -Vector2.UnitY;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int EstimateFrontierCapacity(int minimumCapacity, int maxActiveTiles)
        {
            int activeTiles = Math.Max(1, maxActiveTiles);
            int activeCells = activeTiles * _tileSizeCells * _tileSizeCells;
            return Math.Max(minimumCapacity, activeCells);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private readonly record struct TileCandidate(long TileKey, int Priority);

        private enum FlowDirection : byte
        {
            PosX = 0,
            NegX = 1,
            PosY = 2,
            NegY = 3,
        }
    }
}
