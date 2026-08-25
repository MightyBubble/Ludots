using System;
using System.Numerics;
using Ludots.Core.Map.Hex;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// IVisualHeightmap view over the VertexMap logic-height lattice. Sampled height is the nearest
    /// vertex's height level scaled by HeightScaleMeters — the same scale the nav bake
    /// (NavTileBuilder) and the Raylib VertexMap terrain mesh apply, so grounding, overlay and decal
    /// fitting agree with the rendered surface. The map is read through a provider so hot-swapping
    /// focused maps never leaves the adapter bound to a stale VertexMap.
    /// </summary>
    public sealed class VertexMapVisualHeightmap : IVisualHeightmap
    {
        public const float DefaultHeightScaleMeters = 2f;

        private const float VerticalRayThreshold = 0.0001f;
        private const float HitToleranceCm = 0.5f;
        private const int RefineIterations = 12;

        private readonly Func<VertexMap?> _mapProvider;
        private readonly float _heightScaleMeters;

        public VertexMapVisualHeightmap(Func<VertexMap?> mapProvider, float heightScaleMeters = DefaultHeightScaleMeters)
        {
            _mapProvider = mapProvider ?? throw new ArgumentNullException(nameof(mapProvider));
            if (!float.IsFinite(heightScaleMeters) || heightScaleMeters <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(heightScaleMeters));
            }

            _heightScaleMeters = heightScaleMeters;
        }

        public float HeightScaleMeters => _heightScaleMeters;

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = default;
            if (!ResolveLayerIndex(layerIndex, out _))
            {
                return false;
            }

            VertexMap? map = _mapProvider();
            if (map == null || !TryResolveNearestVertex(map, worldXCm * WorldUnits.MetersPerCm, worldYCm * WorldUnits.MetersPerCm, out int col, out int row))
            {
                return false;
            }

            heightCm = map.GetHeight(col, row) * _heightScaleMeters * WorldUnits.CmPerMeter;
            return float.IsFinite(heightCm);
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                throw new ArgumentException("VertexMap visual heightmap batch sample spans must have identical lengths.");
            }

            if (!ResolveLayerIndex(layerIndex, out _))
            {
                return false;
            }

            for (int i = 0; i < outHeightCm.Length; i++)
            {
                outHeightCm[i] = TrySampleHeightCm(worldXCm[i], worldYCm[i], out float heightCm, layerIndex)
                    ? heightCm
                    : float.NaN;
            }

            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
        {
            hit = default;
            if (!ResolveLayerIndex(layerIndex, out int resolvedLayer))
            {
                return false;
            }

            return TryRaycastVerticalGround(in ray, resolvedLayer, out hit) ||
                   TryRaycastGroundByMarching(in ray, resolvedLayer, out hit);
        }

        public bool RaycastGroundBatch(
            ReadOnlySpan<float> originXMeters,
            ReadOnlySpan<float> originYMeters,
            ReadOnlySpan<float> originZMeters,
            ReadOnlySpan<float> directionX,
            ReadOnlySpan<float> directionY,
            ReadOnlySpan<float> directionZ,
            Span<float> outWorldXCm,
            Span<float> outWorldYCm,
            Span<float> outHeightCm,
            Span<float> outDistanceMeters,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outLayerIndex,
            Span<byte> outHitMask,
            int layerIndex = -1)
        {
            int count = originXMeters.Length;
            if (originYMeters.Length != count ||
                originZMeters.Length != count ||
                directionX.Length != count ||
                directionY.Length != count ||
                directionZ.Length != count ||
                outWorldXCm.Length != count ||
                outWorldYCm.Length != count ||
                outHeightCm.Length != count ||
                outDistanceMeters.Length != count ||
                outNormalX.Length != count ||
                outNormalY.Length != count ||
                outNormalZ.Length != count ||
                outLayerIndex.Length != count ||
                outHitMask.Length != count)
            {
                throw new ArgumentException("VertexMap visual heightmap raycast batch spans must have identical lengths.");
            }

            if (!ResolveLayerIndex(layerIndex, out int resolvedLayer))
            {
                outHitMask.Clear();
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                ScreenRay ray = new ScreenRay(
                    new Vector3(originXMeters[i], originYMeters[i], originZMeters[i]),
                    new Vector3(directionX[i], directionY[i], directionZ[i]));

                if (TryRaycastGround(in ray, out VisualGroundHit hit, resolvedLayer))
                {
                    outWorldXCm[i] = hit.WorldXCm;
                    outWorldYCm[i] = hit.WorldYCm;
                    outHeightCm[i] = hit.HeightCm;
                    outDistanceMeters[i] = hit.DistanceMeters;
                    outNormalX[i] = hit.Normal.X;
                    outNormalY[i] = hit.Normal.Y;
                    outNormalZ[i] = hit.Normal.Z;
                    outLayerIndex[i] = hit.LayerIndex;
                    outHitMask[i] = 1;
                }
                else
                {
                    outWorldXCm[i] = float.NaN;
                    outWorldYCm[i] = float.NaN;
                    outHeightCm[i] = float.NaN;
                    outDistanceMeters[i] = float.NaN;
                    outNormalX[i] = 0f;
                    outNormalY[i] = 0f;
                    outNormalZ[i] = 0f;
                    outLayerIndex[i] = -1;
                    outHitMask[i] = 0;
                }
            }

            return true;
        }

        private static bool ResolveLayerIndex(int layerIndex, out int resolvedLayer)
        {
            resolvedLayer = layerIndex < 0 ? 0 : layerIndex;
            return resolvedLayer == 0;
        }

        private static bool TryResolveNearestVertex(VertexMap map, float worldXMeters, float worldZMeters, out int col, out int row)
        {
            col = -1;
            row = -1;
            if (!float.IsFinite(worldXMeters) || !float.IsFinite(worldZMeters))
            {
                return false;
            }

            int widthCells = map.WidthInChunks * VertexChunk.ChunkSize;
            int heightCells = map.HeightInChunks * VertexChunk.ChunkSize;
            int rowFloor = (int)MathF.Floor(worldZMeters / HexCoordinates.RowSpacing);
            float bestDistanceSq = float.PositiveInfinity;
            for (int candidateRow = rowFloor; candidateRow <= rowFloor + 1; candidateRow++)
            {
                if ((uint)candidateRow >= (uint)heightCells)
                {
                    continue;
                }

                // 奇数行顶点整体右移半列（odd-r offset lattice），每行单独求最近列
                int candidateCol = (int)MathF.Round(worldXMeters / HexCoordinates.HexWidth - 0.5f * (candidateRow & 1), MidpointRounding.AwayFromZero);
                if ((uint)candidateCol >= (uint)widthCells)
                {
                    continue;
                }

                float vertexX = HexCoordinates.HexWidth * (candidateCol + 0.5f * (candidateRow & 1));
                float vertexZ = HexCoordinates.RowSpacing * candidateRow;
                float dx = worldXMeters - vertexX;
                float dz = worldZMeters - vertexZ;
                float distanceSq = (dx * dx) + (dz * dz);
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    col = candidateCol;
                    row = candidateRow;
                }
            }

            return col >= 0;
        }

        private bool TryRaycastVerticalGround(in ScreenRay ray, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            if (MathF.Abs(ray.Direction.X) >= VerticalRayThreshold || MathF.Abs(ray.Direction.Z) >= VerticalRayThreshold)
            {
                return false;
            }

            float dirY = ray.Direction.Y;
            if (!float.IsFinite(dirY) || MathF.Abs(dirY) < VerticalRayThreshold)
            {
                return false;
            }

            float worldXCm = ray.Origin.X * WorldUnits.CmPerMeter;
            float worldYCm = ray.Origin.Z * WorldUnits.CmPerMeter;
            if (!TrySampleHeightCm(worldXCm, worldYCm, out float heightCm, layerIndex))
            {
                return false;
            }

            float t = (heightCm - (ray.Origin.Y * WorldUnits.CmPerMeter)) / (dirY * WorldUnits.CmPerMeter);
            if (!float.IsFinite(t) || t < 0f)
            {
                return false;
            }

            Vector3 point = ray.Origin + (ray.Direction * t);
            return TryBuildHit(point, ray.Origin, layerIndex, out hit);
        }

        private bool TryRaycastGroundByMarching(in ScreenRay ray, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            VertexMap? map = _mapProvider();
            if (map == null || !TryGetRayMapInterval(map, in ray, out float startT, out float endT))
            {
                return false;
            }

            startT = MathF.Max(0f, startT);
            if (!float.IsFinite(startT) || !float.IsFinite(endT) || endT < startT)
            {
                return false;
            }

            // 步长取六边形行距的一半：nearest-vertex 高度场的突变粒度是一个格距
            float stepMeters = HexCoordinates.RowSpacing * 0.5f;
            int steps = Math.Clamp((int)MathF.Ceiling((endT - startT) / MathF.Max(0.01f, stepMeters)), 8, 2048);
            if (!TryEvaluateSignedDistance(in ray, startT, layerIndex, out float previousDelta))
            {
                return false;
            }

            if (MathF.Abs(previousDelta) <= HitToleranceCm &&
                TryBuildHit(ray.Origin + (ray.Direction * startT), ray.Origin, layerIndex, out hit))
            {
                return true;
            }

            float previousT = startT;
            for (int i = 1; i <= steps; i++)
            {
                float t = startT + ((endT - startT) * i / steps);
                if (!TryEvaluateSignedDistance(in ray, t, layerIndex, out float currentDelta))
                {
                    continue;
                }

                bool crossed = (previousDelta >= 0f && currentDelta <= 0f) ||
                               (previousDelta <= 0f && currentDelta >= 0f);
                if (!crossed)
                {
                    previousT = t;
                    previousDelta = currentDelta;
                    continue;
                }

                float hitT = RefineHitT(in ray, previousT, t, previousDelta, currentDelta, layerIndex);
                return TryBuildHit(ray.Origin + (ray.Direction * hitT), ray.Origin, layerIndex, out hit);
            }

            return false;
        }

        private bool TryBuildHit(Vector3 pointMeters, Vector3 originMeters, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            float worldXCm = pointMeters.X * WorldUnits.CmPerMeter;
            float worldYCm = pointMeters.Z * WorldUnits.CmPerMeter;
            if (!TrySampleHeightCm(worldXCm, worldYCm, out float heightCm, layerIndex) ||
                !TryComputeNormal(worldXCm, worldYCm, layerIndex, out Vector3 normal))
            {
                return false;
            }

            Vector3 hitPosition = new Vector3(worldXCm * WorldUnits.MetersPerCm, heightCm * WorldUnits.MetersPerCm, worldYCm * WorldUnits.MetersPerCm);
            hit = new VisualGroundHit(
                worldXCm,
                worldYCm,
                heightCm,
                layerIndex,
                Vector3.Distance(originMeters, hitPosition),
                normal);
            return true;
        }

        private bool TryEvaluateSignedDistance(in ScreenRay ray, float t, int layerIndex, out float deltaCm)
        {
            deltaCm = default;
            Vector3 point = ray.Origin + (ray.Direction * t);
            if (!TrySampleHeightCm(point.X * WorldUnits.CmPerMeter, point.Z * WorldUnits.CmPerMeter, out float groundHeightCm, layerIndex))
            {
                return false;
            }

            deltaCm = (point.Y * WorldUnits.CmPerMeter) - groundHeightCm;
            return float.IsFinite(deltaCm);
        }

        private float RefineHitT(in ScreenRay ray, float lowT, float highT, float lowDelta, float highDelta, int layerIndex)
        {
            float a = lowT;
            float b = highT;
            float da = lowDelta;

            for (int i = 0; i < RefineIterations; i++)
            {
                float mid = (a + b) * 0.5f;
                if (!TryEvaluateSignedDistance(in ray, mid, layerIndex, out float midDelta))
                {
                    break;
                }

                if (MathF.Abs(midDelta) <= HitToleranceCm)
                {
                    return mid;
                }

                bool matchLeft = (da >= 0f && midDelta >= 0f) || (da <= 0f && midDelta <= 0f);
                if (matchLeft)
                {
                    a = mid;
                    da = midDelta;
                }
                else
                {
                    b = mid;
                }
            }

            return (a + b) * 0.5f;
        }

        private bool TryComputeNormal(float worldXCm, float worldYCm, int layerIndex, out Vector3 normal)
        {
            normal = Vector3.UnitY;
            float epsilonCm = HexCoordinates.HexWidth * WorldUnits.CmPerMeter * 0.5f;
            if (!TrySampleHeightCm(worldXCm - epsilonCm, worldYCm, out float hLeft, layerIndex) ||
                !TrySampleHeightCm(worldXCm + epsilonCm, worldYCm, out float hRight, layerIndex) ||
                !TrySampleHeightCm(worldXCm, worldYCm - epsilonCm, out float hUp, layerIndex) ||
                !TrySampleHeightCm(worldXCm, worldYCm + epsilonCm, out float hDown, layerIndex))
            {
                return false;
            }

            normal = Vector3.Normalize(new Vector3(-((hRight - hLeft) / (2f * epsilonCm)), 1f, -((hDown - hUp) / (2f * epsilonCm))));
            return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z);
        }

        private static bool TryGetRayMapInterval(VertexMap map, in ScreenRay ray, out float startT, out float endT)
        {
            startT = 0f;
            endT = float.PositiveInfinity;

            float widthCm = HexCoordinates.HexWidth * WorldUnits.CmPerMeter * ((map.WidthInChunks * VertexChunk.ChunkSize) - 1 + 0.5f);
            float heightCm = HexCoordinates.RowSpacing * WorldUnits.CmPerMeter * ((map.HeightInChunks * VertexChunk.ChunkSize) - 1);
            float originXCm = ray.Origin.X * WorldUnits.CmPerMeter;
            float originZCm = ray.Origin.Z * WorldUnits.CmPerMeter;
            float dirXCm = ray.Direction.X * WorldUnits.CmPerMeter;
            float dirZCm = ray.Direction.Z * WorldUnits.CmPerMeter;

            return TryClipAxis(originXCm, dirXCm, 0f, widthCm, ref startT, ref endT) &&
                   TryClipAxis(originZCm, dirZCm, 0f, heightCm, ref startT, ref endT);
        }

        private static bool TryClipAxis(float origin, float direction, float min, float max, ref float startT, ref float endT)
        {
            if (!float.IsFinite(origin) || !float.IsFinite(direction))
            {
                return false;
            }

            if (MathF.Abs(direction) < VerticalRayThreshold)
            {
                return origin >= min && origin <= max;
            }

            float inv = 1f / direction;
            float t0 = (min - origin) * inv;
            float t1 = (max - origin) * inv;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            startT = MathF.Max(startT, t0);
            endT = MathF.Min(endT, t1);
            return endT >= startT;
        }
    }
}
