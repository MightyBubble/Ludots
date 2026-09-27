using System;
using System.Numerics;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// 世界 HUD 地形遮挡的帧内地平线包络。每帧从相机对各方位角扇区沿高度场行进一次，
    /// 记录各距离桶内已越过地形的最大仰角；锚点可见性退化为一次仰角比较。
    /// 成本随（扇区数 × 地图采样线）走，与实体数量、相机运动方式、会话时长无关；
    /// 无跨帧状态、无容量、无失效语义——地形修订由采样快照的版本比对覆盖。
    /// 可见性为近似：扇区取样宽度内的尖锐地形可能造成贴脊锚点的少量误显/误藏；
    /// 锚点仰角取相邻扇区包络的较小值判定，误差偏向误显（宁可多显示，不误藏活单位）。
    /// 仅支持网格采样高度图；相机位缺失/不在图内/高度图不可采样时返回不可用，
    /// 由调用方逐项回退精确 raycast。
    /// </summary>
    public sealed class TerrainHudOcclusionHorizon
    {
        public const int SectorCount = 128;
        public const int DistanceBucketCount = 32;
        private const int GridResolution = 257;
        private const float NearCameraVisibleDistanceCm = 50f;
        private const float DirectionEpsilon = 1e-6f;
        private const float AngleEpsilon = 1e-4f;
        private const float SectorWidthRadians = 2f * MathF.PI / SectorCount;

        private readonly float[] _envelope = new float[SectorCount * DistanceBucketCount];
        private readonly float[] _bucketEdges = new float[DistanceBucketCount + 1];
        private float[] _samples = Array.Empty<float>();
        private float[] _gridWorldX = Array.Empty<float>();
        private float[] _gridWorldY = Array.Empty<float>();
        private IContinuousHeightmapRenderSource? _samplesSource;
        private int _samplesRevision = -1;
        private WorldAabbCm _snapshotBounds;
        private int _columns;
        private int _rows;
        private WorldAabbCm _bounds;
        private float _cellWidthCm = 1f;
        private float _cellHeightCm = 1f;
        private float _cameraXCm;
        private float _cameraYCm;
        private float _cameraZCm;
        private float _maxDistanceCm;
        private bool _valid;

        public bool IsValid => _valid;

        public bool TryRebuild(IContinuousHeightmap heightmap, Vector3 cameraPositionMeters)
        {
            _valid = false;
            if (heightmap is not IContinuousHeightmapRenderSource source)
            {
                return false;
            }

            if (cameraPositionMeters == Vector3.Zero ||
                !float.IsFinite(cameraPositionMeters.X) ||
                !float.IsFinite(cameraPositionMeters.Y) ||
                !float.IsFinite(cameraPositionMeters.Z))
            {
                return false;
            }

            _bounds = source.Bounds;
            _cameraXCm = cameraPositionMeters.X * 100f;
            _cameraYCm = cameraPositionMeters.Y * 100f;
            _cameraZCm = cameraPositionMeters.Z * 100f;
            // 自由相机会越出地图边：把行进原点钳回图内一格，包络本就是近似，
            // 不为出图相机回退逐项精确 raycast（那会把最坏 80ms 的裸射线风暴带回来）。
            _cameraXCm = Math.Clamp(_cameraXCm, _bounds.Left, _bounds.Right);
            _cameraZCm = Math.Clamp(_cameraZCm, _bounds.Top, _bounds.Bottom);

            _columns = GridResolution;
            _rows = GridResolution;
            _cellWidthCm = _bounds.Width / (float)(_columns - 1);
            _cellHeightCm = _bounds.Height / (float)(_rows - 1);
            _maxDistanceCm = MathF.Sqrt(
                _bounds.Width * (float)_bounds.Width + _bounds.Height * (float)_bounds.Height);
            for (int k = 0; k <= DistanceBucketCount; k++)
            {
                float t = k / (float)DistanceBucketCount;
                _bucketEdges[k] = _maxDistanceCm * t * t;
            }

            try
            {
                if (!SnapshotSamples(heightmap, source))
                {
                    return false;
                }
            }
            catch (Exception)
            {
                // 批量采样失败的实现：回退逐项精确 raycast，不冒进。
                return false;
            }

            MarchAllSectors();
            _valid = true;
            return true;
        }

        public bool IsVisible(Vector3 worldPositionMeters)
        {
            float dxCm = worldPositionMeters.X * 100f - _cameraXCm;
            float dzCm = worldPositionMeters.Z * 100f - _cameraZCm;
            float dyCm = worldPositionMeters.Y * 100f - _cameraYCm;
            float distanceCm = MathF.Sqrt(dxCm * dxCm + dzCm * dzCm);
            if (distanceCm <= NearCameraVisibleDistanceCm || distanceCm >= _maxDistanceCm)
            {
                return true;
            }

            int bucket = DistanceBucketOf(distanceCm);
            float sectorFloat = MathF.Atan2(dxCm, dzCm) / (2f * MathF.PI) * SectorCount;
            if (sectorFloat < 0f)
            {
                sectorFloat += SectorCount;
            }

            int sectorLow = (int)sectorFloat % SectorCount;
            int sectorHigh = (sectorLow + 1) % SectorCount;
            float envelopeLow = _envelope[sectorLow * DistanceBucketCount + bucket];
            float envelopeHigh = _envelope[sectorHigh * DistanceBucketCount + bucket];
            float envelope = MathF.Min(envelopeLow, envelopeHigh);
            // 量化松弛：采样格与扇区取样在地表 Lipschitz-1 假设下的高度误差
            // 折成角度从包络扣除——包络只可能因此更宽容（偏向显示）。
            float slackCm = _cellWidthCm + SectorWidthRadians * distanceCm;
            float slack = MathF.Atan2(slackCm, distanceCm);
            float anchorAngle = MathF.Atan2(dyCm, distanceCm);
            return anchorAngle >= envelope - AngleEpsilon - slack;
        }

        private int DistanceBucketOf(float distanceCm)
        {
            float t = MathF.Sqrt(Math.Clamp(distanceCm / _maxDistanceCm, 0f, 1f));
            int bucket = (int)(t * DistanceBucketCount);
            return Math.Clamp(bucket, 0, DistanceBucketCount - 1);
        }

        /// <summary>
        /// 采样快照走 IContinuousHeightmap 公开批量接口（自带图层解析），
        /// 网格分辨率与地形实现解耦。缓存键为 (实例, Revision, Bounds 值)——
        /// 服务定位器每帧可能返回等价的新包装实例，不能只按引用判等。
        /// </summary>
        private bool SnapshotSamples(IContinuousHeightmap heightmap, IContinuousHeightmapRenderSource source)
        {
            int revision = source.Revision;
            if (ReferenceEquals(_samplesSource, source) && revision == _samplesRevision &&
                _snapshotBounds == _bounds &&
                _samples.Length == _columns * _rows)
            {
                return true;
            }

            int gridLength = _columns * _rows;
            if (_samples.Length != gridLength)
            {
                _samples = new float[gridLength];
                _gridWorldX = new float[gridLength];
                _gridWorldY = new float[gridLength];
            }

            int writeIndex = 0;
            for (int y = 0; y < _rows; y++)
            {
                float worldY = _bounds.Top + y * _cellHeightCm;
                for (int x = 0; x < _columns; x++)
                {
                    _gridWorldX[writeIndex] = _bounds.Left + x * _cellWidthCm;
                    _gridWorldY[writeIndex] = worldY;
                    writeIndex++;
                }
            }

            if (!heightmap.SampleHeightsCm(_gridWorldX, _gridWorldY, _samples, layerIndex: -1))
            {
                return false;
            }

            for (int i = 0; i < _samples.Length; i++)
            {
                if (!float.IsFinite(_samples[i]))
                {
                    _samples[i] = float.NegativeInfinity;
                }
            }

            _samplesSource = source;
            _samplesRevision = revision;
            _snapshotBounds = _bounds;
            return true;
        }

        private void MarchAllSectors()
        {
            for (int sector = 0; sector < SectorCount; sector++)
            {
                MarchSector(sector, sector / (float)SectorCount * 2f * MathF.PI);
            }
        }

        private void MarchSector(int sector, float theta)
        {
            float dirX = MathF.Sin(theta);
            float dirZ = MathF.Cos(theta);
            int stepX = dirX > DirectionEpsilon ? 1 : dirX < -DirectionEpsilon ? -1 : 0;
            int stepZ = dirZ > DirectionEpsilon ? 1 : dirZ < -DirectionEpsilon ? -1 : 0;
            int cellX = (int)MathF.Floor((_cameraXCm - _bounds.Left) / _cellWidthCm);
            int cellZ = (int)MathF.Floor((_cameraZCm - _bounds.Top) / _cellHeightCm);
            float tMaxX = stepX != 0
                ? ((_bounds.Left + (cellX + (stepX > 0 ? 1 : 0)) * _cellWidthCm) - _cameraXCm) / dirX
                : float.PositiveInfinity;
            float tMaxZ = stepZ != 0
                ? ((_bounds.Top + (cellZ + (stepZ > 0 ? 1 : 0)) * _cellHeightCm) - _cameraZCm) / dirZ
                : float.PositiveInfinity;
            float tDeltaX = stepX != 0 ? MathF.Abs(_cellWidthCm / dirX) : float.PositiveInfinity;
            float tDeltaZ = stepZ != 0 ? MathF.Abs(_cellHeightCm / dirZ) : float.PositiveInfinity;

            int envelopeBase = sector * DistanceBucketCount;
            float runningMax = float.NegativeInfinity;
            int nextEdge = 1;
            while (true)
            {
                float t;
                bool alongX;
                if (tMaxX <= tMaxZ)
                {
                    t = tMaxX;
                    alongX = true;
                }
                else
                {
                    t = tMaxZ;
                    alongX = false;
                }

                if (!float.IsFinite(t) || t > _maxDistanceCm)
                {
                    break;
                }

                if (alongX)
                {
                    cellX += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    cellZ += stepZ;
                    tMaxZ += tDeltaZ;
                }

                if (cellX < 0 || cellX > _columns - 1 || cellZ < 0 || cellZ > _rows - 1)
                {
                    break;
                }

                // 按桶近边界提交：桶 k 的包络只覆盖 [0, edges[k]]，
                // 绝不把锚点以远的地形算进遮挡（高机位下远处平地仰角会反超贴地锚点）。
                while (nextEdge < DistanceBucketCount && t > _bucketEdges[nextEdge])
                {
                    _envelope[envelopeBase + nextEdge] = runningMax;
                    nextEdge++;
                }

                float heightCm = alongX
                    ? SampleColumnMax(cellX, cellZ)
                    : SampleRowMax(cellX, cellZ);
                if (float.IsFinite(heightCm))
                {
                    float angle = MathF.Atan2(heightCm - _cameraYCm, t);
                    if (angle > runningMax)
                    {
                        runningMax = angle;
                    }
                }
            }

            for (int bucket = nextEdge; bucket < DistanceBucketCount; bucket++)
            {
                _envelope[envelopeBase + bucket] = runningMax;
            }
        }

        /// <summary>穿越纵向采样线：线上 sampleX 精确，仅沿连续 z 轴取 floor/+1 括号采样的大者。</summary>
        private float SampleColumnMax(int sampleX, int sampleZ)
        {
            int z1 = Math.Min(sampleZ + 1, _rows - 1);
            float a = _samples[sampleZ * _columns + sampleX];
            float b = _samples[z1 * _columns + sampleX];
            return MathF.Max(a, b);
        }

        /// <summary>穿越横向采样线：线上 sampleZ 精确，仅沿连续 x 轴取 floor/+1 括号采样的大者。</summary>
        private float SampleRowMax(int sampleX, int sampleZ)
        {
            int x1 = Math.Min(sampleX + 1, _columns - 1);
            float a = _samples[sampleZ * _columns + sampleX];
            float b = _samples[sampleZ * _columns + x1];
            return MathF.Max(a, b);
        }
    }
}
