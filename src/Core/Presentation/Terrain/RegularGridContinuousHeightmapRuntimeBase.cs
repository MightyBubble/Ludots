using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    internal abstract class RegularGridContinuousHeightmapRuntimeBase : IContinuousHeightmap
    {
        private const float MToCm = 100f;
        private const float HitToleranceCm = 0.5f;
        private const float RaycastEpsilonT = 0.0001f;

        protected abstract WorldAabbCm Bounds { get; }
        protected abstract int SampleColumns { get; }
        protected abstract int SampleRows { get; }
        protected abstract ContinuousHeightmapLayerDefinition[] Layers { get; }
        protected abstract int DefaultLayerIndex { get; }
        protected abstract ContinuousHeightmapInterpolationMode InterpolationMode { get; }

        protected abstract bool TryReadSampleCm(int layerSampleOffset, int globalSampleX, int globalSampleY, out float heightCm);

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = default;
            if (!TryResolveLayer(layerIndex, out ContinuousHeightmapLayerDefinition layer) ||
                !TryGetNormalizedCoordinates(worldXCm, worldYCm, out float sampleX, out float sampleY))
            {
                return false;
            }

            if (!TryResolveCell(sampleX, sampleY, out int cellX, out int cellY, out float tx, out float ty))
            {
                return false;
            }

            if (!TryReadCellSamples(layer.SampleOffset, cellX, cellY, out float h00, out float h10, out float h01, out float h11))
            {
                return false;
            }

            heightCm = SampleCell(h00, h10, h01, h11, tx, ty, InterpolationMode);
            return float.IsFinite(heightCm);
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                throw new ArgumentException("Visual heightmap batch sample spans must have identical lengths.");
            }

            if (!TryResolveLayer(layerIndex, out _))
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
            if (!TryResolveLayerIndex(layerIndex, out int resolvedLayer))
            {
                return false;
            }

            if (TryRaycastVerticalGround(in ray, resolvedLayer, out hit))
            {
                return true;
            }

            if (InterpolationMode == ContinuousHeightmapInterpolationMode.TriangleHeightfield)
            {
                return TryRaycastTriangleGroundExact(in ray, resolvedLayer, out hit);
            }

            return TryRaycastGroundByMarching(in ray, resolvedLayer, out hit);
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
                throw new ArgumentException("Visual heightmap batch raycast spans must have identical lengths.");
            }

            if (!TryResolveLayer(layerIndex, out _))
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                ScreenRay ray = new ScreenRay(
                    new Vector3(originXMeters[i], originYMeters[i], originZMeters[i]),
                    new Vector3(directionX[i], directionY[i], directionZ[i]));

                if (TryRaycastGround(in ray, out VisualGroundHit hit, layerIndex))
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

        private bool TryRaycastGroundByMarching(in ScreenRay ray, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            if (!TryGetRayBoundsInterval(in ray, out float startT, out float endT))
            {
                return false;
            }

            startT = Math.Max(0f, startT);
            if (!float.IsFinite(startT) || !float.IsFinite(endT) || endT < startT)
            {
                return false;
            }

            int steps = ComputeRaySteps(in ray, startT, endT);
            if (!TryEvaluateSignedDistance(in ray, startT, layerIndex, out float previousDelta))
            {
                return false;
            }

            if (MathF.Abs(previousDelta) <= HitToleranceCm &&
                TryBuildHit(in ray, startT, layerIndex, out hit))
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
                return TryBuildHit(in ray, hitT, layerIndex, out hit);
            }

            return false;
        }

        private bool TryRaycastTriangleGroundExact(in ScreenRay ray, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            if (SampleColumns < 2 || SampleRows < 2)
            {
                return false;
            }

            if (!TryGetRayBoundsInterval(in ray, out float startT, out float endT))
            {
                return false;
            }

            startT = Math.Max(0f, startT);
            if (!float.IsFinite(startT) || !float.IsFinite(endT) || endT < startT)
            {
                return false;
            }

            Vector3 originCm = ray.Origin * MToCm;
            Vector3 directionCm = ray.Direction * MToCm;
            float currentT = Math.Min(endT, startT + RaycastEpsilonT);
            Vector3 currentPoint = originCm + (directionCm * currentT);

            float clampedX = Math.Clamp(currentPoint.X, Bounds.Left, Bounds.Right);
            float clampedY = Math.Clamp(currentPoint.Z, Bounds.Top, Bounds.Bottom);
            int cellX = ClampCellX((int)MathF.Floor((clampedX - Bounds.Left) / GetCellWidthCm()));
            int cellY = ClampCellY((int)MathF.Floor((clampedY - Bounds.Top) / GetCellHeightCm()));

            int stepX = directionCm.X > 0f ? 1 : directionCm.X < 0f ? -1 : 0;
            int stepY = directionCm.Z > 0f ? 1 : directionCm.Z < 0f ? -1 : 0;

            float tMaxX = ComputeNextBoundaryT(currentT, clampedX, directionCm.X, Bounds.Left, GetCellWidthCm(), cellX, stepX);
            float tMaxY = ComputeNextBoundaryT(currentT, clampedY, directionCm.Z, Bounds.Top, GetCellHeightCm(), cellY, stepY);
            float tDeltaX = stepX == 0 ? float.PositiveInfinity : GetCellWidthCm() / MathF.Abs(directionCm.X);
            float tDeltaY = stepY == 0 ? float.PositiveInfinity : GetCellHeightCm() / MathF.Abs(directionCm.Z);

            while ((uint)cellX < (uint)(SampleColumns - 1) &&
                   (uint)cellY < (uint)(SampleRows - 1))
            {
                float cellExitT = Math.Min(endT, Math.Min(tMaxX, tMaxY));
                if (TryRaycastCellTriangles(
                    originCm,
                    directionCm,
                    currentT,
                    cellExitT,
                    layerIndex,
                    cellX,
                    cellY,
                    out hit))
                {
                    return true;
                }

                if (cellExitT >= endT)
                {
                    break;
                }

                bool advanceX = tMaxX <= tMaxY;
                bool advanceY = tMaxY <= tMaxX;
                currentT = Math.Min(tMaxX, tMaxY);

                if (advanceX)
                {
                    cellX += stepX;
                    tMaxX += tDeltaX;
                }

                if (advanceY)
                {
                    cellY += stepY;
                    tMaxY += tDeltaY;
                }
            }

            return false;
        }

        private bool TryRaycastCellTriangles(
            Vector3 originCm,
            Vector3 directionCm,
            float cellStartT,
            float cellEndT,
            int layerIndex,
            int cellX,
            int cellY,
            out VisualGroundHit hit)
        {
            hit = default;
            if (!TryResolveLayer(layerIndex, out ContinuousHeightmapLayerDefinition layer) ||
                !TryReadCellSamples(layer.SampleOffset, cellX, cellY, out float h00, out float h10, out float h01, out float h11))
            {
                return false;
            }

            float x0 = Bounds.Left + (cellX * GetCellWidthCm());
            float x1 = x0 + GetCellWidthCm();
            float y0 = Bounds.Top + (cellY * GetCellHeightCm());
            float y1 = y0 + GetCellHeightCm();

            Vector3 v00 = new Vector3(x0, h00, y0);
            Vector3 v10 = new Vector3(x1, h10, y0);
            Vector3 v01 = new Vector3(x0, h01, y1);
            Vector3 v11 = new Vector3(x1, h11, y1);

            bool hasHit = false;
            float bestT = float.PositiveInfinity;
            Vector3 bestPoint = default;
            Vector3 bestNormal = Vector3.UnitY;

            if (TryIntersectTriangle(originCm, directionCm, v00, v11, v10, out float tA, out Vector3 pointA, out Vector3 normalA) &&
                IsWithinCellRayInterval(tA, cellStartT, cellEndT) &&
                tA < bestT)
            {
                hasHit = true;
                bestT = tA;
                bestPoint = pointA;
                bestNormal = normalA;
            }

            if (TryIntersectTriangle(originCm, directionCm, v00, v01, v11, out float tB, out Vector3 pointB, out Vector3 normalB) &&
                IsWithinCellRayInterval(tB, cellStartT, cellEndT) &&
                tB < bestT)
            {
                hasHit = true;
                bestT = tB;
                bestPoint = pointB;
                bestNormal = normalB;
            }

            if (!hasHit)
            {
                return false;
            }

            hit = new VisualGroundHit(
                bestPoint.X,
                bestPoint.Z,
                bestPoint.Y,
                layerIndex,
                bestT,
                bestNormal);
            return true;
        }

        private bool TryBuildHit(in ScreenRay ray, float t, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            Vector3 point = ray.Origin + (ray.Direction * t);
            return TryBuildHit(point, ray.Origin, layerIndex, out hit);
        }

        private bool TryBuildHit(Vector3 pointMeters, Vector3 originMeters, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            float worldXCm = pointMeters.X * MToCm;
            float worldYCm = pointMeters.Z * MToCm;
            if (!TrySampleHeightCm(worldXCm, worldYCm, out float heightCm, layerIndex) ||
                !TryComputeNormal(worldXCm, worldYCm, layerIndex, out Vector3 normal))
            {
                return false;
            }

            Vector3 hitPosition = new Vector3(worldXCm * 0.01f, heightCm * 0.01f, worldYCm * 0.01f);
            hit = new VisualGroundHit(
                worldXCm,
                worldYCm,
                heightCm,
                layerIndex,
                Vector3.Distance(originMeters, hitPosition),
                normal);
            return true;
        }

        private bool TryRaycastVerticalGround(in ScreenRay ray, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            if (MathF.Abs(ray.Direction.X) >= 0.0001f || MathF.Abs(ray.Direction.Z) >= 0.0001f)
            {
                return false;
            }

            float dirY = ray.Direction.Y;
            if (!float.IsFinite(dirY) || MathF.Abs(dirY) < 0.0001f)
            {
                return false;
            }

            float worldXCm = ray.Origin.X * MToCm;
            float worldYCm = ray.Origin.Z * MToCm;
            if (!TrySampleHeightCm(worldXCm, worldYCm, out float heightCm, layerIndex))
            {
                return false;
            }

            float originHeightCm = ray.Origin.Y * MToCm;
            float t = (heightCm - originHeightCm) / (dirY * MToCm);
            if (!float.IsFinite(t) || t < 0f)
            {
                return false;
            }

            Vector3 point = ray.Origin + (ray.Direction * t);
            return TryBuildHit(point, ray.Origin, layerIndex, out hit);
        }

        private bool TryEvaluateSignedDistance(in ScreenRay ray, float t, int layerIndex, out float deltaCm)
        {
            deltaCm = default;
            Vector3 point = ray.Origin + (ray.Direction * t);
            float worldXCm = point.X * MToCm;
            float worldYCm = point.Z * MToCm;
            if (!TrySampleHeightCm(worldXCm, worldYCm, out float groundHeightCm, layerIndex))
            {
                return false;
            }

            float rayHeightCm = point.Y * MToCm;
            deltaCm = rayHeightCm - groundHeightCm;
            return float.IsFinite(deltaCm);
        }

        private float RefineHitT(in ScreenRay ray, float lowT, float highT, float lowDelta, float highDelta, int layerIndex)
        {
            float a = lowT;
            float b = highT;
            float da = lowDelta;

            for (int i = 0; i < 12; i++)
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
            if (!TryResolveLayer(layerIndex, out ContinuousHeightmapLayerDefinition layer) ||
                !TryGetNormalizedCoordinates(worldXCm, worldYCm, out float sampleX, out float sampleY) ||
                !TryResolveCell(sampleX, sampleY, out int cellX, out int cellY, out float tx, out float ty) ||
                !TryReadCellSamples(layer.SampleOffset, cellX, cellY, out float h00, out float h10, out float h01, out float h11))
            {
                return false;
            }

            float cellWidthCm = GetCellWidthCm();
            float cellHeightCm = GetCellHeightCm();
            if (InterpolationMode == ContinuousHeightmapInterpolationMode.BilinearHeightfield)
            {
                float dHeightDxCm = ((h10 - h00) * (1f - ty)) + ((h11 - h01) * ty);
                float dHeightDyCm = ((h01 - h00) * (1f - tx)) + ((h11 - h10) * tx);
                normal = Vector3.Normalize(new Vector3(-(dHeightDxCm / cellWidthCm), 1f, -(dHeightDyCm / cellHeightCm)));
                return Vector3.Dot(normal, normal) > 0f && float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z);
            }

            float x0 = Bounds.Left + (cellX * cellWidthCm);
            float x1 = x0 + cellWidthCm;
            float y0 = Bounds.Top + (cellY * cellHeightCm);
            float y1 = y0 + cellHeightCm;

            Vector3 v00 = new Vector3(x0, h00, y0);
            Vector3 v10 = new Vector3(x1, h10, y0);
            Vector3 v01 = new Vector3(x0, h01, y1);
            Vector3 v11 = new Vector3(x1, h11, y1);
            normal = tx >= ty
                ? ComputeTriangleNormal(v00, v11, v10)
                : ComputeTriangleNormal(v00, v01, v11);
            return Vector3.Dot(normal, normal) > 0f && float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z);
        }

        private bool TryReadCellSamples(int layerSampleOffset, int cellX, int cellY, out float h00, out float h10, out float h01, out float h11)
        {
            h00 = default;
            h10 = default;
            h01 = default;
            h11 = default;
            return TryReadSampleCm(layerSampleOffset, cellX, cellY, out h00) &&
                   TryReadSampleCm(layerSampleOffset, cellX + 1, cellY, out h10) &&
                   TryReadSampleCm(layerSampleOffset, cellX, cellY + 1, out h01) &&
                   TryReadSampleCm(layerSampleOffset, cellX + 1, cellY + 1, out h11);
        }

        private bool TryResolveCell(float sampleX, float sampleY, out int cellX, out int cellY, out float tx, out float ty)
        {
            cellX = default;
            cellY = default;
            tx = default;
            ty = default;

            if (SampleColumns < 2 || SampleRows < 2)
            {
                return false;
            }

            cellX = ClampCellX((int)MathF.Floor(sampleX));
            cellY = ClampCellY((int)MathF.Floor(sampleY));
            tx = Math.Clamp(sampleX - cellX, 0f, 1f);
            ty = Math.Clamp(sampleY - cellY, 0f, 1f);
            return true;
        }

        private bool TryResolveLayer(int layerIndex, out ContinuousHeightmapLayerDefinition layer)
        {
            if (!TryResolveLayerIndex(layerIndex, out int resolvedLayer))
            {
                layer = default;
                return false;
            }

            layer = Layers[resolvedLayer];
            return true;
        }

        private bool TryResolveLayerIndex(int layerIndex, out int resolvedLayer)
        {
            resolvedLayer = layerIndex >= 0 ? layerIndex : DefaultLayerIndex;
            return (uint)resolvedLayer < (uint)Layers.Length;
        }

        private bool TryGetNormalizedCoordinates(float worldXCm, float worldYCm, out float sampleX, out float sampleY)
        {
            sampleX = default;
            sampleY = default;

            if (!float.IsFinite(worldXCm) || !float.IsFinite(worldYCm))
            {
                return false;
            }

            if (worldXCm < Bounds.Left ||
                worldXCm > Bounds.Right ||
                worldYCm < Bounds.Top ||
                worldYCm > Bounds.Bottom)
            {
                return false;
            }

            float u = (worldXCm - Bounds.Left) / Math.Max(1f, Bounds.Width);
            float v = (worldYCm - Bounds.Top) / Math.Max(1f, Bounds.Height);
            sampleX = (SampleColumns - 1) * Math.Clamp(u, 0f, 1f);
            sampleY = (SampleRows - 1) * Math.Clamp(v, 0f, 1f);
            return true;
        }

        private float GetCellWidthCm()
        {
            return SampleColumns > 1
                ? Math.Max(1f, (float)Bounds.Width / (SampleColumns - 1))
                : Math.Max(1f, Bounds.Width);
        }

        private float GetCellHeightCm()
        {
            return SampleRows > 1
                ? Math.Max(1f, (float)Bounds.Height / (SampleRows - 1))
                : Math.Max(1f, Bounds.Height);
        }

        private int ComputeRaySteps(in ScreenRay ray, float startT, float endT)
        {
            float distanceMeters = MathF.Max(0f, endT - startT);
            float dxCm = MathF.Abs(ray.Direction.X * distanceMeters * MToCm);
            float dyCm = MathF.Abs(ray.Direction.Z * distanceMeters * MToCm);
            float cellSteps = MathF.Max(
                dxCm / MathF.Max(1f, GetCellWidthCm()),
                dyCm / MathF.Max(1f, GetCellHeightCm()));
            return Math.Clamp((int)MathF.Ceiling(cellSteps * 2f), 8, 1024);
        }

        private bool TryGetRayBoundsInterval(in ScreenRay ray, out float startT, out float endT)
        {
            startT = 0f;
            endT = float.PositiveInfinity;

            float originXCm = ray.Origin.X * MToCm;
            float originYCm = ray.Origin.Z * MToCm;
            float dirXCm = ray.Direction.X * MToCm;
            float dirYCm = ray.Direction.Z * MToCm;

            return TryClipAxis(originXCm, dirXCm, Bounds.Left, Bounds.Right, ref startT, ref endT) &&
                   TryClipAxis(originYCm, dirYCm, Bounds.Top, Bounds.Bottom, ref startT, ref endT);
        }

        private int ClampCellX(int cellX)
        {
            return Math.Clamp(cellX, 0, Math.Max(0, SampleColumns - 2));
        }

        private int ClampCellY(int cellY)
        {
            return Math.Clamp(cellY, 0, Math.Max(0, SampleRows - 2));
        }

        private static float SampleCell(float h00, float h10, float h01, float h11, float tx, float ty, ContinuousHeightmapInterpolationMode mode)
        {
            return mode == ContinuousHeightmapInterpolationMode.TriangleHeightfield
                ? SampleTriangleCell(h00, h10, h01, h11, tx, ty)
                : SampleBilinearCell(h00, h10, h01, h11, tx, ty);
        }

        private static float SampleBilinearCell(float h00, float h10, float h01, float h11, float tx, float ty)
        {
            float hx0 = h00 + ((h10 - h00) * tx);
            float hx1 = h01 + ((h11 - h01) * tx);
            return hx0 + ((hx1 - hx0) * ty);
        }

        private static float SampleTriangleCell(float h00, float h10, float h01, float h11, float tx, float ty)
        {
            if (tx >= ty)
            {
                return h00 + (tx * (h10 - h00)) + (ty * (h11 - h10));
            }

            return h00 + (tx * (h11 - h01)) + (ty * (h01 - h00));
        }

        private static float ComputeNextBoundaryT(float currentT, float currentAxisCm, float directionAxisCm, int minAxisCm, float cellSizeCm, int cellIndex, int step)
        {
            if (step == 0)
            {
                return float.PositiveInfinity;
            }

            float boundaryAxisCm = minAxisCm + ((step > 0 ? cellIndex + 1 : cellIndex) * cellSizeCm);
            return currentT + ((boundaryAxisCm - currentAxisCm) / directionAxisCm);
        }

        private static bool TryIntersectTriangle(
            Vector3 originCm,
            Vector3 directionCm,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out float t,
            out Vector3 pointCm,
            out Vector3 normal)
        {
            t = default;
            pointCm = default;
            normal = default;

            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(directionCm, edge2);
            float det = Vector3.Dot(edge1, p);
            if (MathF.Abs(det) < 1e-6f)
            {
                return false;
            }

            float invDet = 1f / det;
            Vector3 tVec = originCm - a;
            float u = Vector3.Dot(tVec, p) * invDet;
            if (u < -1e-5f || u > 1f + 1e-5f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(tVec, edge1);
            float v = Vector3.Dot(directionCm, q) * invDet;
            if (v < -1e-5f || u + v > 1f + 1e-5f)
            {
                return false;
            }

            t = Vector3.Dot(edge2, q) * invDet;
            if (!float.IsFinite(t) || t < 0f)
            {
                return false;
            }

            pointCm = originCm + (directionCm * t);
            normal = ComputeTriangleNormal(a, b, c);
            return true;
        }

        private static Vector3 ComputeTriangleNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(normal, normal) <= 1e-10f)
            {
                return Vector3.UnitY;
            }

            return Vector3.Normalize(normal);
        }

        private static bool IsWithinCellRayInterval(float t, float cellStartT, float cellEndT)
        {
            return t >= Math.Max(0f, cellStartT - RaycastEpsilonT) &&
                   t <= cellEndT + RaycastEpsilonT;
        }

        private static bool TryClipAxis(float origin, float direction, float min, float max, ref float startT, ref float endT)
        {
            if (!float.IsFinite(origin) || !float.IsFinite(direction))
            {
                return false;
            }

            if (MathF.Abs(direction) < 0.0001f)
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
