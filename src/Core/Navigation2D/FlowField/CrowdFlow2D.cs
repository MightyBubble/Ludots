// TODO: CrowdFlowTile2D is a class but LongKeyMap<T> requires unmanaged struct.
// Refactor CrowdFlowTile2D to use fixed-size buffers or switch to Dictionary<long, ...>.
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.LowLevel;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Spatial;

namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdFlow2D : IDisposable
    {
        private readonly CrowdSurface2D _surface;
        private readonly Dictionary<long, CrowdFlowTile2D> _tiles;
        private UnsafeQueue<long> _frontier;

        private Fix64Vec2 _goalCm;
        private Fix64 _goalRadiusCm;
        private bool _needsRebuild;

        private readonly int _tileShift;
        private readonly int _tileMask;

        /// <summary>
        /// BFS 传播的最大势能上限。超过此值的格子不再入队，防止 flowfield 无限扩展。
        /// 值的含义：大致等于从目标出发的"格子距离"（cardinal=1, diagonal≈1.414）。
        /// 默认 300 ≈ 覆盖半径 300 格 × CellSize 的区域。
        /// </summary>
        public float MaxPotential { get; set; } = 300f;

        // #region agent log
        private static readonly string _dbgLogPath = @"c:\AIProjects\Ludots\.cursor\debug.log";
        private int _dbgStepCount;
        private static int _dbgFlowIdCounter;
        private readonly int _dbgFlowId;
        private static void DbgLog(string hypothesisId, string location, string message, string dataJson)
        {
            try { File.AppendAllText(_dbgLogPath, $"{{\"hypothesisId\":\"{hypothesisId}\",\"location\":\"{location}\",\"message\":\"{message}\",\"data\":{dataJson},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}\n"); } catch { }
        }
        // #endregion

        public CrowdFlow2D(CrowdSurface2D surface, int initialTileCapacity = 256, int initialFrontierCapacity = 1024)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            _tiles = new Dictionary<long, CrowdFlowTile2D>(Math.Max(8, initialTileCapacity));
            _frontier = new UnsafeQueue<long>(Math.Max(16, initialFrontierCapacity));

            int tileSize = surface.TileSizeCells;
            _tileMask = tileSize - 1;
            _tileShift = BitOperations.TrailingZeroCount((uint)tileSize);
            _needsRebuild = true;
            _goalCm = default;
            _goalRadiusCm = Fix64.Zero;
            // #region agent log
            _dbgFlowId = _dbgFlowIdCounter++;
            // #endregion
        }

        // #region agent log
        private int _dbgSetGoalCount;
        // #endregion

        public void SetGoalPoint(in Fix64Vec2 goalCm, Fix64 radiusCm)
        {
            // Fix2: 仅当目标实际改变时才标记需要重建
            bool changed = _goalCm.X != goalCm.X || _goalCm.Y != goalCm.Y || _goalRadiusCm != radiusCm;
            // #region agent log
            _dbgSetGoalCount++;
            if (_dbgSetGoalCount % 60 == 1) DbgLog("H2", "CrowdFlow2D.SetGoalPoint", "goal_set", $"{{\"flowId\":{_dbgFlowId},\"callCount\":{_dbgSetGoalCount},\"changed\":{changed.ToString().ToLower()},\"goalCm\":\"{goalCm.X.ToFloat():F0},{goalCm.Y.ToFloat():F0}\",\"wasAlreadyNeedingRebuild\":{_needsRebuild.ToString().ToLower()}}}");
            // #endregion
            _goalCm = goalCm;
            _goalRadiusCm = radiusCm;
            if (changed)
            {
                _needsRebuild = true;
            }
        }

        public void OnTileLoaded(long tileKey)
        {
            _surface.GetOrCreateTile(tileKey);
            if (!_tiles.ContainsKey(tileKey))
            {
                _tiles[tileKey] = new CrowdFlowTile2D(_surface.TileSizeCells);
            }
        }

        public void OnTileUnloaded(long tileKey)
        {
            _tiles.Remove(tileKey);
            _surface.RemoveTile(tileKey);
            _needsRebuild = true;
        }

        public void Step(int iterations)
        {
            if (iterations <= 0) return;
            // #region agent log
            _dbgStepCount++;
            bool dbgLog = _dbgStepCount % 60 == 1;
            bool didRebuild = _needsRebuild;
            // #endregion
            if (_needsRebuild) Rebuild();
            // #region agent log
            int frontierAfterRebuild = _frontier.Count;
            // #endregion
            if (_frontier.Count == 0) return;

            // Fix3b: 8邻域BFS传播（对角线代价√2），生成更平滑的类欧几里得距离场
            const float DiagCost = 1.41421356f;
            int remaining = iterations;
            while (remaining-- > 0 && _frontier.Count > 0)
            {
                long cellKey = _frontier.Dequeue();
                Nav2DKeyPacking.UnpackInt2(cellKey, out int cx, out int cy);
                float current = GetPotential(cx, cy);
                if (float.IsPositiveInfinity(current)) continue;

                // 4 cardinal neighbors (cost 1)
                TryRelaxNeighbor(cx + 1, cy, current, 1f);
                TryRelaxNeighbor(cx - 1, cy, current, 1f);
                TryRelaxNeighbor(cx, cy + 1, current, 1f);
                TryRelaxNeighbor(cx, cy - 1, current, 1f);
                // 4 diagonal neighbors (cost √2)
                TryRelaxNeighbor(cx + 1, cy + 1, current, DiagCost);
                TryRelaxNeighbor(cx + 1, cy - 1, current, DiagCost);
                TryRelaxNeighbor(cx - 1, cy + 1, current, DiagCost);
                TryRelaxNeighbor(cx - 1, cy - 1, current, DiagCost);
            }
            // #region agent log
            int consumed = iterations - remaining - 1;
            if (dbgLog) DbgLog("H2,H5", "CrowdFlow2D.Step", "step_summary", $"{{\"flowId\":{_dbgFlowId},\"didRebuild\":{didRebuild.ToString().ToLower()},\"frontierAfterRebuild\":{frontierAfterRebuild},\"itersUsed\":{consumed},\"itersTotal\":{iterations},\"frontierRemaining\":{_frontier.Count},\"tileCount\":{_tiles.Count},\"goalCm\":\"{_goalCm.X.ToFloat():F0},{_goalCm.Y.ToFloat():F0}\"}}");
            // #endregion
        }

        // #region agent log
        private int _dbgSampleCounter;
        // #endregion

        public bool TrySampleDesiredVelocityCm(in Fix64Vec2 positionCm, Fix64 maxSpeedCmPerSec, out Fix64Vec2 desiredVelocityCmPerSec)
        {
            desiredVelocityCmPerSec = default;

            if (_needsRebuild) Rebuild();

            _surface.WorldToCell(positionCm, out int cx, out int cy);
            float p0 = GetPotential(cx, cy);
            if (float.IsPositiveInfinity(p0)) return false;
            if (p0 <= 0.001f) return false;

            // Fix3: 使用中心差分计算势场梯度，产出平滑连续方向
            // 采样 8 邻域用于梯度计算
            float pxp = GetPotentialClamped(cx + 1, cy, p0);   // +X
            float pxn = GetPotentialClamped(cx - 1, cy, p0);   // -X
            float pyp = GetPotentialClamped(cx, cy + 1, p0);   // +Y
            float pyn = GetPotentialClamped(cx, cy - 1, p0);   // -Y
            float ppp = GetPotentialClamped(cx + 1, cy + 1, p0); // +X+Y
            float ppn = GetPotentialClamped(cx + 1, cy - 1, p0); // +X-Y
            float pnp = GetPotentialClamped(cx - 1, cy + 1, p0); // -X+Y
            float pnn = GetPotentialClamped(cx - 1, cy - 1, p0); // -X-Y

            // Sobel-like gradient: 对角方向贡献 1/√2 ≈ 0.7071
            const float diag = 0.7071f;
            float gx = (pxp - pxn) + diag * ((ppp - pnp) + (ppn - pnn));
            float gy = (pyp - pyn) + diag * ((ppp - ppn) + (pnp - pnn));

            // 梯度指向 potential 增大方向，我们要往 potential 减小方向走，取反
            float dx = -gx;
            float dy = -gy;

            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f) return false;

            // 归一化后乘以最大速度
            float invLen = 1f / len;
            float maxSpd = maxSpeedCmPerSec.ToFloat();
            desiredVelocityCmPerSec = Fix64Vec2.FromFloat(dx * invLen * maxSpd, dy * invLen * maxSpd);

            // #region agent log
            _dbgSampleCounter++;
            if (_dbgSampleCounter % 3000 == 1) DbgLog("H3", "CrowdFlow2D.TrySample", "sample_result", $"{{\"flowId\":{_dbgFlowId},\"cell\":\"{cx},{cy}\",\"p0\":{p0:F2},\"gx\":{gx:F3},\"gy\":{gy:F3},\"dir\":\"{dx * invLen:F3},{dy * invLen:F3}\"}}");
            // #endregion

            return true;
        }

        /// <summary>
        /// 获取势场值；如果目标格不可达(Infinity/障碍)，返回 fallback 以避免梯度被 Infinity 污染。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetPotentialClamped(int cellX, int cellY, float fallback)
        {
            if (_surface.IsBlockedCell(cellX, cellY)) return fallback;
            float v = GetPotential(cellX, cellY);
            return float.IsPositiveInfinity(v) ? fallback : v;
        }

        private void Rebuild()
        {
            foreach (var kv in _tiles)
            {
                kv.Value.Reset();
            }

            while (_frontier.Count > 0) _frontier.Dequeue();

            _surface.WorldToCell(_goalCm, out int gx, out int gy);
            if (_goalRadiusCm > Fix64.Zero)
            {
                int r = (_goalRadiusCm / _surface.CellSizeCm).CeilToInt();
                for (int y = gy - r; y <= gy + r; y++)
                {
                    for (int x = gx - r; x <= gx + r; x++)
                    {
                        if (_surface.IsBlockedCell(x, y)) continue;
                        SetPotential(x, y, 0f);
                        _frontier.Enqueue(Nav2DKeyPacking.PackInt2(x, y));
                    }
                }
            }
            else
            {
                if (!_surface.IsBlockedCell(gx, gy))
                {
                    SetPotential(gx, gy, 0f);
                    _frontier.Enqueue(Nav2DKeyPacking.PackInt2(gx, gy));
                }
            }

            _needsRebuild = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetPotential(int cellX, int cellY)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            if (!_tiles.TryGetValue(tileKey, out var tile)) return float.PositiveInfinity;
            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            return tile.Potential[ly * _surface.TileSizeCells + lx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetPotential(int cellX, int cellY, float value)
        {
            long tileKey = Nav2DKeyPacking.PackInt2(cellX >> _tileShift, cellY >> _tileShift);
            _surface.GetOrCreateTile(tileKey);
            if (!_tiles.TryGetValue(tileKey, out var tile))
            {
                tile = new CrowdFlowTile2D(_surface.TileSizeCells);
                _tiles[tileKey] = tile;
            }
            int lx = cellX & _tileMask;
            int ly = cellY & _tileMask;
            tile.Potential[ly * _surface.TileSizeCells + lx] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryRelaxNeighbor(int nx, int ny, float current, float cost = 1f)
        {
            float next = current + cost;
            // Fix5: 超过最大势能上限的格子不再传播，防止 BFS 无限扩展
            if (next > MaxPotential) return;
            if (_surface.IsBlockedCell(nx, ny)) return;
            float old = GetPotential(nx, ny);
            if (next >= old) return;
            SetPotential(nx, ny, next);
            _frontier.Enqueue(Nav2DKeyPacking.PackInt2(nx, ny));
        }

        public void Dispose()
        {
            _frontier.Dispose();
        }
    }
}
