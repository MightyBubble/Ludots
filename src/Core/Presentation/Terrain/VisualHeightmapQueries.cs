using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    internal interface IVisualHeightmapSampleAccessor
    {
        bool TryReadSampleCm(int layerSampleOffset, int sampleX, int sampleY, out float heightCm);
    }

    internal static class VisualHeightmapQueries
    {
        private const float MToCm = 100f;
        private const float CmToM = 0.01f;
        private const float HitToleranceCm = 0.5f;
        private const float AxisEpsilon = 0.0001f;
        private const float TriangleEpsilon = 0.00001f;

        public static bool TrySampleHeightCm(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerSampleOffset,
            float worldXCm,
            float worldYCm,
            out float heightCm)
        {
            heightCm = default;
            if (!TryResolveSampleSpace(
                    in bounds,
                    sampleColumns,
                    sampleRows,
                    worldXCm,
                    worldYCm,
                    out int x0,
                    out int x1,
                    out int y0,
                    out int y1,
                    out float tx,
                    out float ty))
            {
                return false;
            }

            if (!TryReadCellSamples(accessor, layerSampleOffset, x0, x1, y0, y1, out float h00, out float h10, out float h01, out float h11))
            {
                return false;
            }

            heightCm = EvaluateHeight(interpolationMode, x0 == x1 || y0 == y1, h00, h10, h01, h11, tx, ty);
            return float.IsFinite(heightCm);
        }

        public static void SampleHeightsCm(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerSampleOffset,
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldYCm,
            Span<float> outHeightCm)
        {
            float invWidth = 1f / Math.Max(1f, bounds.Width);
            float invHeight = 1f / Math.Max(1f, bounds.Height);
            float sampleScaleX = sampleColumns > 1 ? sampleColumns - 1 : 0f;
            float sampleScaleY = sampleRows > 1 ? sampleRows - 1 : 0f;
            int maxCellX = Math.Max(0, sampleColumns - 2);
            int maxCellY = Math.Max(0, sampleRows - 2);
            for (int i = 0; i < outHeightCm.Length; i++)
            {
                outHeightCm[i] = TrySampleHeightCmFast(
                    accessor,
                    in bounds,
                    sampleColumns,
                    sampleRows,
                    interpolationMode,
                    layerSampleOffset,
                    invWidth,
                    invHeight,
                    sampleScaleX,
                    sampleScaleY,
                    maxCellX,
                    maxCellY,
                    worldXCm[i],
                    worldYCm[i],
                    out float heightCm)
                    ? heightCm
                    : float.NaN;
            }
        }

        public static bool TrySampleSurface(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerSampleOffset,
            float worldXCm,
            float worldYCm,
            out float heightCm,
            out Vector3 normal)
        {
            normal = Vector3.UnitY;
            if (!TrySampleHeightCm(
                    accessor,
                    in bounds,
                    sampleColumns,
                    sampleRows,
                    interpolationMode,
                    layerSampleOffset,
                    worldXCm,
                    worldYCm,
                    out heightCm))
            {
                return false;
            }

            return TryComputeNormal(
                accessor,
                in bounds,
                sampleColumns,
                sampleRows,
                interpolationMode,
                layerSampleOffset,
                worldXCm,
                worldYCm,
                out normal);
        }

        public static bool TryRaycastGround(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerIndex,
            int layerSampleOffset,
            in ScreenRay ray,
            out VisualGroundHit hit)
        {
            hit = default;
            if (TryRaycastVerticalGround(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerIndex, layerSampleOffset, in ray, out hit))
            {
                return true;
            }

            if (interpolationMode == VisualHeightmapInterpolationMode.TriangleHeightfield &&
                sampleColumns > 1 &&
                sampleRows > 1 &&
                TryRaycastTriangleHeightfieldExact(accessor, in bounds, sampleColumns, sampleRows, layerIndex, layerSampleOffset, in ray, out hit))
            {
                return true;
            }

            if (!TryGetRayBoundsInterval(in bounds, in ray, out float startT, out float endT))
            {
                return false;
            }

            startT = Math.Max(0f, startT);
            if (!float.IsFinite(startT) || !float.IsFinite(endT) || endT < startT)
            {
                return false;
            }

            int steps = ComputeRaySteps(in bounds, sampleColumns, sampleRows, in ray, startT, endT);
            if (!TryEvaluateSignedDistance(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerSampleOffset, in ray, startT, out float previousDelta))
            {
                return false;
            }

            if (MathF.Abs(previousDelta) <= HitToleranceCm &&
                TryBuildHit(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerIndex, layerSampleOffset, ray.Origin, in ray, startT, out hit))
            {
                return true;
            }

            float previousT = startT;
            for (int i = 1; i <= steps; i++)
            {
                float t = startT + ((endT - startT) * i / steps);
                if (!TryEvaluateSignedDistance(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerSampleOffset, in ray, t, out float currentDelta))
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

                float hitT = RefineHitT(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerSampleOffset, in ray, previousT, t, previousDelta);
                return TryBuildHit(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerIndex, layerSampleOffset, ray.Origin, in ray, hitT, out hit);
            }

            return false;
        }

        private static bool TryRaycastVerticalGround(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerIndex,
            int layerSampleOffset,
            in ScreenRay ray,
            out VisualGroundHit hit)
        {
            hit = default;
            if (MathF.Abs(ray.Direction.X) >= AxisEpsilon || MathF.Abs(ray.Direction.Z) >= AxisEpsilon)
            {
                return false;
            }

            float dirY = ray.Direction.Y;
            if (!float.IsFinite(dirY) || MathF.Abs(dirY) < AxisEpsilon)
            {
                return false;
            }

            float worldXCm = ray.Origin.X * MToCm;
            float worldYCm = ray.Origin.Z * MToCm;
            if (!TrySampleHeightCm(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerSampleOffset, worldXCm, worldYCm, out float heightCm))
            {
                return false;
            }

            float originHeightCm = ray.Origin.Y * MToCm;
            float t = (heightCm - originHeightCm) / (dirY * MToCm);
            if (!float.IsFinite(t) || t < 0f)
            {
                return false;
            }

            return TryBuildHit(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerIndex, layerSampleOffset, ray.Origin, in ray, t, out hit);
        }

        private static bool TryBuildHit(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerIndex,
            int layerSampleOffset,
            Vector3 origin,
            in ScreenRay ray,
            float t,
            out VisualGroundHit hit)
        {
            hit = default;
            Vector3 point = ray.Origin + (ray.Direction * t);
            float worldXCm = point.X * MToCm;
            float worldYCm = point.Z * MToCm;
            if (!TrySampleHeightCm(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerSampleOffset, worldXCm, worldYCm, out float heightCm) ||
                !TryComputeNormal(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerSampleOffset, worldXCm, worldYCm, out Vector3 normal))
            {
                return false;
            }

            Vector3 hitPosition = new Vector3(worldXCm * CmToM, heightCm * CmToM, worldYCm * CmToM);
            hit = new VisualGroundHit(worldXCm, worldYCm, heightCm, layerIndex, Vector3.Distance(origin, hitPosition), normal);
            return true;
        }

        private static float EvaluateHeight(
            VisualHeightmapInterpolationMode interpolationMode,
            bool degenerateCell,
            float h00,
            float h10,
            float h01,
            float h11,
            float tx,
            float ty)
        {
            if (interpolationMode == VisualHeightmapInterpolationMode.TriangleHeightfield && !degenerateCell)
            {
                if (tx + ty <= 1f)
                {
                    return h00 + ((h10 - h00) * tx) + ((h01 - h00) * ty);
                }

                return h11 + ((h01 - h11) * (1f - tx)) + ((h10 - h11) * (1f - ty));
            }

            float hx0 = h00 + ((h10 - h00) * tx);
            float hx1 = h01 + ((h11 - h01) * tx);
            return hx0 + ((hx1 - hx0) * ty);
        }

        private static bool TryComputeNormal(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerSampleOffset,
            float worldXCm,
            float worldYCm,
            out Vector3 normal)
        {
            normal = Vector3.UnitY;
            if (!TryResolveSampleSpace(
                    in bounds,
                    sampleColumns,
                    sampleRows,
                    worldXCm,
                    worldYCm,
                    out int x0,
                    out int x1,
                    out int y0,
                    out int y1,
                    out float tx,
                    out float ty))
            {
                return false;
            }

            if (!TryReadCellSamples(accessor, layerSampleOffset, x0, x1, y0, y1, out float h00, out float h10, out float h01, out float h11))
            {
                return false;
            }

            float cellWidthCm = GetCellWidthCm(in bounds, sampleColumns);
            float cellHeightCm = GetCellHeightCm(in bounds, sampleRows);
            if (interpolationMode == VisualHeightmapInterpolationMode.TriangleHeightfield &&
                x0 != x1 &&
                y0 != y1)
            {
                float x0m = (bounds.Left + (x0 * cellWidthCm)) * CmToM;
                float x1m = (bounds.Left + (x1 * cellWidthCm)) * CmToM;
                float y0m = (bounds.Top + (y0 * cellHeightCm)) * CmToM;
                float y1m = (bounds.Top + (y1 * cellHeightCm)) * CmToM;
                Vector3 a;
                Vector3 b;
                Vector3 c;
                if (tx + ty <= 1f)
                {
                    a = new Vector3(x0m, h00 * CmToM, y0m);
                    b = new Vector3(x0m, h01 * CmToM, y1m);
                    c = new Vector3(x1m, h10 * CmToM, y0m);
                }
                else
                {
                    a = new Vector3(x1m, h11 * CmToM, y1m);
                    b = new Vector3(x1m, h10 * CmToM, y0m);
                    c = new Vector3(x0m, h01 * CmToM, y1m);
                }

                normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                if (normal.Y < 0f)
                {
                    normal = -normal;
                }

                return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z);
            }

            float dhdx = (((h10 - h00) * (1f - ty)) + ((h11 - h01) * ty)) / MathF.Max(1f, cellWidthCm);
            float dhdy = (((h01 - h00) * (1f - tx)) + ((h11 - h10) * tx)) / MathF.Max(1f, cellHeightCm);
            normal = Vector3.Normalize(new Vector3(-dhdx, 1f, -dhdy));
            return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z);
        }

        private static bool TryRaycastTriangleHeightfieldExact(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            int layerIndex,
            int layerSampleOffset,
            in ScreenRay ray,
            out VisualGroundHit hit)
        {
            hit = default;
            if (!TryGetRayBoundsInterval(in bounds, in ray, out float startT, out float endT))
            {
                return false;
            }

            startT = Math.Max(0f, startT);
            if (!float.IsFinite(startT) || !float.IsFinite(endT) || endT < startT)
            {
                return false;
            }

            float cellWidthCm = GetCellWidthCm(in bounds, sampleColumns);
            float cellHeightCm = GetCellHeightCm(in bounds, sampleRows);
            Vector3 startPoint = ray.Origin + (ray.Direction * startT);
            float startXCm = Math.Clamp(startPoint.X * MToCm, bounds.Left, bounds.Right);
            float startYCm = Math.Clamp(startPoint.Z * MToCm, bounds.Top, bounds.Bottom);
            int cellX = ResolveCellIndex(startXCm, bounds.Left, cellWidthCm, sampleColumns);
            int cellY = ResolveCellIndex(startYCm, bounds.Top, cellHeightCm, sampleRows);

            int stepX = ray.Direction.X > AxisEpsilon ? 1 : (ray.Direction.X < -AxisEpsilon ? -1 : 0);
            int stepY = ray.Direction.Z > AxisEpsilon ? 1 : (ray.Direction.Z < -AxisEpsilon ? -1 : 0);

            float nextBoundaryTX = stepX == 0
                ? float.PositiveInfinity
                : ComputeNextBoundaryT(ray.Origin.X, ray.Direction.X, bounds.Left, cellWidthCm, cellX, stepX);
            float nextBoundaryTY = stepY == 0
                ? float.PositiveInfinity
                : ComputeNextBoundaryT(ray.Origin.Z, ray.Direction.Z, bounds.Top, cellHeightCm, cellY, stepY);
            float deltaTX = stepX == 0 ? float.PositiveInfinity : MathF.Abs((cellWidthCm * CmToM) / ray.Direction.X);
            float deltaTY = stepY == 0 ? float.PositiveInfinity : MathF.Abs((cellHeightCm * CmToM) / ray.Direction.Z);

            float cellEnterT = startT;
            while (cellX >= 0 &&
                   cellX < sampleColumns - 1 &&
                   cellY >= 0 &&
                   cellY < sampleRows - 1 &&
                   cellEnterT <= endT)
            {
                float cellExitT = MathF.Min(endT, MathF.Min(nextBoundaryTX, nextBoundaryTY));
                if (TryIntersectCellTriangles(
                        accessor,
                        in bounds,
                        cellWidthCm,
                        cellHeightCm,
                        layerSampleOffset,
                        cellX,
                        cellY,
                        in ray,
                        cellEnterT,
                        cellExitT,
                        out float hitT))
                {
                    return TryBuildHit(
                        accessor,
                        in bounds,
                        sampleColumns,
                        sampleRows,
                        VisualHeightmapInterpolationMode.TriangleHeightfield,
                        layerIndex,
                        layerSampleOffset,
                        ray.Origin,
                        in ray,
                        hitT,
                        out hit);
                }

                bool advanceX = nextBoundaryTX <= nextBoundaryTY + TriangleEpsilon;
                bool advanceY = nextBoundaryTY <= nextBoundaryTX + TriangleEpsilon;
                cellEnterT = cellExitT;
                if (advanceX)
                {
                    cellX += stepX;
                    nextBoundaryTX += deltaTX;
                }

                if (advanceY)
                {
                    cellY += stepY;
                    nextBoundaryTY += deltaTY;
                }
            }

            return false;
        }

        private static bool TryIntersectCellTriangles(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            float cellWidthCm,
            float cellHeightCm,
            int layerSampleOffset,
            int cellX,
            int cellY,
            in ScreenRay ray,
            float minT,
            float maxT,
            out float hitT)
        {
            hitT = default;
            if (!TryReadCellSamples(accessor, layerSampleOffset, cellX, cellX + 1, cellY, cellY + 1, out float h00, out float h10, out float h01, out float h11))
            {
                return false;
            }

            float x0m = (bounds.Left + (cellX * cellWidthCm)) * CmToM;
            float x1m = (bounds.Left + ((cellX + 1) * cellWidthCm)) * CmToM;
            float y0m = (bounds.Top + (cellY * cellHeightCm)) * CmToM;
            float y1m = (bounds.Top + ((cellY + 1) * cellHeightCm)) * CmToM;

            Vector3 p00 = new Vector3(x0m, h00 * CmToM, y0m);
            Vector3 p10 = new Vector3(x1m, h10 * CmToM, y0m);
            Vector3 p01 = new Vector3(x0m, h01 * CmToM, y1m);
            Vector3 p11 = new Vector3(x1m, h11 * CmToM, y1m);

            bool hitA = TryIntersectTriangle(in ray, p00, p01, p10, minT, maxT, out float hitTA);
            bool hitB = TryIntersectTriangle(in ray, p11, p10, p01, minT, maxT, out float hitTB);
            if (!hitA && !hitB)
            {
                return false;
            }

            hitT = hitA && hitB ? MathF.Min(hitTA, hitTB) : (hitA ? hitTA : hitTB);
            return true;
        }

        private static bool TryIntersectTriangle(
            in ScreenRay ray,
            in Vector3 a,
            in Vector3 b,
            in Vector3 c,
            float minT,
            float maxT,
            out float hitT)
        {
            hitT = default;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(ray.Direction, edge2);
            float det = Vector3.Dot(edge1, p);
            if (MathF.Abs(det) < TriangleEpsilon)
            {
                return false;
            }

            float invDet = 1f / det;
            Vector3 tVec = ray.Origin - a;
            float u = Vector3.Dot(tVec, p) * invDet;
            if (u < -TriangleEpsilon || u > 1f + TriangleEpsilon)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(tVec, edge1);
            float v = Vector3.Dot(ray.Direction, q) * invDet;
            if (v < -TriangleEpsilon || u + v > 1f + TriangleEpsilon)
            {
                return false;
            }

            float candidateT = Vector3.Dot(edge2, q) * invDet;
            if (!float.IsFinite(candidateT) || candidateT < minT - TriangleEpsilon || candidateT > maxT + TriangleEpsilon)
            {
                return false;
            }

            hitT = Math.Clamp(candidateT, minT, maxT);
            return true;
        }

        private static float RefineHitT(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerSampleOffset,
            in ScreenRay ray,
            float lowT,
            float highT,
            float lowDelta)
        {
            float a = lowT;
            float b = highT;
            float da = lowDelta;

            for (int i = 0; i < 12; i++)
            {
                float mid = (a + b) * 0.5f;
                if (!TryEvaluateSignedDistance(accessor, in bounds, sampleColumns, sampleRows, interpolationMode, layerSampleOffset, in ray, mid, out float midDelta))
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

        private static bool TryEvaluateSignedDistance(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerSampleOffset,
            in ScreenRay ray,
            float t,
            out float deltaCm)
        {
            deltaCm = default;
            Vector3 point = ray.Origin + (ray.Direction * t);
            if (!TrySampleHeightCm(
                    accessor,
                    in bounds,
                    sampleColumns,
                    sampleRows,
                    interpolationMode,
                    layerSampleOffset,
                    point.X * MToCm,
                    point.Z * MToCm,
                    out float groundHeightCm))
            {
                return false;
            }

            deltaCm = (point.Y * MToCm) - groundHeightCm;
            return float.IsFinite(deltaCm);
        }

        private static bool TryResolveSampleSpace(
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            float worldXCm,
            float worldYCm,
            out int x0,
            out int x1,
            out int y0,
            out int y1,
            out float tx,
            out float ty)
        {
            x0 = x1 = y0 = y1 = 0;
            tx = ty = 0f;
            if (!float.IsFinite(worldXCm) || !float.IsFinite(worldYCm))
            {
                return false;
            }

            if (worldXCm < bounds.Left ||
                worldXCm > bounds.Right ||
                worldYCm < bounds.Top ||
                worldYCm > bounds.Bottom)
            {
                return false;
            }

            float sampleX = sampleColumns > 1
                ? (sampleColumns - 1) * Math.Clamp((worldXCm - bounds.Left) / Math.Max(1f, bounds.Width), 0f, 1f)
                : 0f;
            float sampleY = sampleRows > 1
                ? (sampleRows - 1) * Math.Clamp((worldYCm - bounds.Top) / Math.Max(1f, bounds.Height), 0f, 1f)
                : 0f;

            x0 = sampleColumns > 1 ? Math.Clamp((int)MathF.Floor(sampleX), 0, sampleColumns - 2) : 0;
            y0 = sampleRows > 1 ? Math.Clamp((int)MathF.Floor(sampleY), 0, sampleRows - 2) : 0;
            x1 = sampleColumns > 1 ? x0 + 1 : 0;
            y1 = sampleRows > 1 ? y0 + 1 : 0;
            tx = sampleColumns > 1 ? sampleX - x0 : 0f;
            ty = sampleRows > 1 ? sampleY - y0 : 0f;
            return true;
        }

        private static bool TrySampleHeightCmFast(
            IVisualHeightmapSampleAccessor accessor,
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            VisualHeightmapInterpolationMode interpolationMode,
            int layerSampleOffset,
            float invWidth,
            float invHeight,
            float sampleScaleX,
            float sampleScaleY,
            int maxCellX,
            int maxCellY,
            float worldXCm,
            float worldYCm,
            out float heightCm)
        {
            heightCm = default;
            if (!float.IsFinite(worldXCm) || !float.IsFinite(worldYCm))
            {
                return false;
            }

            if (worldXCm < bounds.Left ||
                worldXCm > bounds.Right ||
                worldYCm < bounds.Top ||
                worldYCm > bounds.Bottom)
            {
                return false;
            }

            float sampleX = sampleColumns > 1
                ? sampleScaleX * Math.Clamp((worldXCm - bounds.Left) * invWidth, 0f, 1f)
                : 0f;
            float sampleY = sampleRows > 1
                ? sampleScaleY * Math.Clamp((worldYCm - bounds.Top) * invHeight, 0f, 1f)
                : 0f;
            int x0 = sampleColumns > 1 ? Math.Clamp((int)sampleX, 0, maxCellX) : 0;
            int y0 = sampleRows > 1 ? Math.Clamp((int)sampleY, 0, maxCellY) : 0;
            int x1 = sampleColumns > 1 ? x0 + 1 : 0;
            int y1 = sampleRows > 1 ? y0 + 1 : 0;
            float tx = sampleColumns > 1 ? sampleX - x0 : 0f;
            float ty = sampleRows > 1 ? sampleY - y0 : 0f;
            if (!TryReadCellSamples(accessor, layerSampleOffset, x0, x1, y0, y1, out float h00, out float h10, out float h01, out float h11))
            {
                return false;
            }

            heightCm = EvaluateHeight(interpolationMode, x0 == x1 || y0 == y1, h00, h10, h01, h11, tx, ty);
            return float.IsFinite(heightCm);
        }

        private static bool TryReadCellSamples(
            IVisualHeightmapSampleAccessor accessor,
            int layerSampleOffset,
            int x0,
            int x1,
            int y0,
            int y1,
            out float h00,
            out float h10,
            out float h01,
            out float h11)
        {
            h00 = h10 = h01 = h11 = default;
            return accessor.TryReadSampleCm(layerSampleOffset, x0, y0, out h00) &&
                   accessor.TryReadSampleCm(layerSampleOffset, x1, y0, out h10) &&
                   accessor.TryReadSampleCm(layerSampleOffset, x0, y1, out h01) &&
                   accessor.TryReadSampleCm(layerSampleOffset, x1, y1, out h11);
        }

        private static int ComputeRaySteps(
            in WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            in ScreenRay ray,
            float startT,
            float endT)
        {
            float distanceMeters = MathF.Max(0f, endT - startT);
            float dxCm = MathF.Abs(ray.Direction.X * distanceMeters * MToCm);
            float dyCm = MathF.Abs(ray.Direction.Z * distanceMeters * MToCm);
            float cellWidthCm = GetCellWidthCm(in bounds, sampleColumns);
            float cellHeightCm = GetCellHeightCm(in bounds, sampleRows);
            float cellSteps = MathF.Max(
                dxCm / MathF.Max(1f, cellWidthCm),
                dyCm / MathF.Max(1f, cellHeightCm));
            return Math.Clamp((int)MathF.Ceiling(cellSteps * 2f), 8, 1024);
        }

        private static bool TryGetRayBoundsInterval(
            in WorldAabbCm bounds,
            in ScreenRay ray,
            out float startT,
            out float endT)
        {
            startT = 0f;
            endT = float.PositiveInfinity;

            float originXCm = ray.Origin.X * MToCm;
            float originYCm = ray.Origin.Z * MToCm;
            float dirXCm = ray.Direction.X * MToCm;
            float dirYCm = ray.Direction.Z * MToCm;

            return TryClipAxis(originXCm, dirXCm, bounds.Left, bounds.Right, ref startT, ref endT) &&
                   TryClipAxis(originYCm, dirYCm, bounds.Top, bounds.Bottom, ref startT, ref endT);
        }

        private static bool TryClipAxis(float origin, float direction, float min, float max, ref float startT, ref float endT)
        {
            if (!float.IsFinite(origin) || !float.IsFinite(direction))
            {
                return false;
            }

            if (MathF.Abs(direction) < AxisEpsilon)
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

        private static float GetCellWidthCm(in WorldAabbCm bounds, int sampleColumns)
        {
            return sampleColumns > 1
                ? Math.Max(1f, bounds.Width / (float)(sampleColumns - 1))
                : Math.Max(1f, bounds.Width);
        }

        private static float GetCellHeightCm(in WorldAabbCm bounds, int sampleRows)
        {
            return sampleRows > 1
                ? Math.Max(1f, bounds.Height / (float)(sampleRows - 1))
                : Math.Max(1f, bounds.Height);
        }

        private static int ResolveCellIndex(float worldCm, int minCm, float cellSizeCm, int sampleCount)
        {
            if (sampleCount <= 1)
            {
                return 0;
            }

            int maxCellIndex = sampleCount - 2;
            float sampleIndex = (worldCm - minCm) / MathF.Max(1f, cellSizeCm);
            return Math.Clamp((int)MathF.Floor(sampleIndex), 0, maxCellIndex);
        }

        private static float ComputeNextBoundaryT(float originMeters, float directionMeters, int minCm, float cellSizeCm, int cellIndex, int step)
        {
            float boundaryMeters = (minCm + ((step > 0 ? cellIndex + 1 : cellIndex) * cellSizeCm)) * CmToM;
            return (boundaryMeters - originMeters) / directionMeters;
        }
    }
}
