using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    public sealed class VisualHeightmapRuntime : IVisualHeightmap
    {
        private const float MToCm = 100f;
        private const float HitToleranceCm = 0.5f;
        private readonly VisualHeightmapAsset _asset;

        public VisualHeightmapRuntime(VisualHeightmapAsset asset)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        }

        public VisualHeightmapAsset Asset => _asset;

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
        {
            heightCm = default;
            if (!TryResolveLayer(layerIndex, out var layer) ||
                !TryGetNormalizedCoordinates(worldXCm, worldYCm, out float sampleX, out float sampleY))
            {
                return false;
            }

            int x0 = (int)MathF.Floor(sampleX);
            int y0 = (int)MathF.Floor(sampleY);
            int x1 = Math.Min(x0 + 1, _asset.SampleColumns - 1);
            int y1 = Math.Min(y0 + 1, _asset.SampleRows - 1);
            float tx = sampleX - x0;
            float ty = sampleY - y0;

            float h00 = ReadSampleCm(layer.SampleOffset, x0, y0);
            float h10 = ReadSampleCm(layer.SampleOffset, x1, y0);
            float h01 = ReadSampleCm(layer.SampleOffset, x0, y1);
            float h11 = ReadSampleCm(layer.SampleOffset, x1, y1);

            float hx0 = h00 + ((h10 - h00) * tx);
            float hx1 = h01 + ((h11 - h01) * tx);
            heightCm = hx0 + ((hx1 - hx0) * ty);
            return true;
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
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

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
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
            int layerIndex = 0)
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
                var ray = new ScreenRay(
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

        private bool TryBuildHit(in ScreenRay ray, float t, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            Vector3 point = ray.Origin + (ray.Direction * t);
            return TryBuildHit(point, ray.Origin, layerIndex, out hit);
        }

        private bool TryBuildHit(Vector3 point, Vector3 origin, int layerIndex, out VisualGroundHit hit)
        {
            hit = default;
            float worldXCm = point.X * MToCm;
            float worldYCm = point.Z * MToCm;
            if (!TrySampleHeightCm(worldXCm, worldYCm, out float heightCm, layerIndex))
            {
                return false;
            }

            if (!TryComputeNormal(worldXCm, worldYCm, heightCm, layerIndex, out Vector3 normal))
            {
                return false;
            }

            Vector3 hitPosition = new Vector3(worldXCm * 0.01f, heightCm * 0.01f, worldYCm * 0.01f);
            float distanceMeters = Vector3.Distance(origin, hitPosition);
            hit = new VisualGroundHit(worldXCm, worldYCm, heightCm, layerIndex, distanceMeters, normal);
            return true;
        }

        private int ComputeRaySteps(in ScreenRay ray, float startT, float endT)
        {
            float distanceMeters = MathF.Max(0f, endT - startT);
            float dxCm = MathF.Abs(ray.Direction.X * distanceMeters * MToCm);
            float dyCm = MathF.Abs(ray.Direction.Z * distanceMeters * MToCm);
            float cellWidthCm = GetCellWidthCm();
            float cellHeightCm = GetCellHeightCm();
            float cellSteps = MathF.Max(
                dxCm / MathF.Max(1f, cellWidthCm),
                dyCm / MathF.Max(1f, cellHeightCm));
            return Math.Clamp((int)MathF.Ceiling(cellSteps * 2f), 8, 1024);
        }

        private float RefineHitT(in ScreenRay ray, float lowT, float highT, float lowDelta, float highDelta, int layerIndex)
        {
            float a = lowT;
            float b = highT;
            float da = lowDelta;
            float db = highDelta;

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
                    db = midDelta;
                }
            }

            return (a + b) * 0.5f;
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

        private bool TryGetNormalizedCoordinates(float worldXCm, float worldYCm, out float sampleX, out float sampleY)
        {
            sampleX = default;
            sampleY = default;

            if (!float.IsFinite(worldXCm) || !float.IsFinite(worldYCm))
            {
                return false;
            }

            WorldAabbCm bounds = _asset.Bounds;
            if (worldXCm < bounds.Left ||
                worldXCm > bounds.Right ||
                worldYCm < bounds.Top ||
                worldYCm > bounds.Bottom)
            {
                return false;
            }

            float widthCm = Math.Max(1f, bounds.Width);
            float heightCm = Math.Max(1f, bounds.Height);
            float u = (worldXCm - bounds.Left) / widthCm;
            float v = (worldYCm - bounds.Top) / heightCm;
            sampleX = (_asset.SampleColumns - 1) * Math.Clamp(u, 0f, 1f);
            sampleY = (_asset.SampleRows - 1) * Math.Clamp(v, 0f, 1f);
            return true;
        }

        private float ReadSampleCm(int sampleOffset, int x, int y)
        {
            int index = sampleOffset + (y * _asset.SampleColumns) + x;
            return _asset.HeightSamplesCm[index];
        }

        private bool TryResolveLayerIndex(int layerIndex, out int resolvedLayer)
        {
            resolvedLayer = layerIndex >= 0 ? layerIndex : _asset.DefaultLayerIndex;
            return (uint)resolvedLayer < (uint)_asset.Layers.Length;
        }

        private bool TryResolveLayer(int layerIndex, out VisualHeightmapLayerDefinition layer)
        {
            if (!TryResolveLayerIndex(layerIndex, out int resolvedLayer))
            {
                layer = default;
                return false;
            }

            layer = _asset.Layers[resolvedLayer];
            return true;
        }

        private bool TryComputeNormal(float worldXCm, float worldYCm, float heightCm, int layerIndex, out Vector3 normal)
        {
            normal = Vector3.UnitY;

            WorldAabbCm bounds = _asset.Bounds;
            float stepXCm = GetCellWidthCm();
            float stepYCm = GetCellHeightCm();

            float x0 = Math.Clamp(worldXCm - stepXCm, bounds.Left, bounds.Right);
            float x1 = Math.Clamp(worldXCm + stepXCm, bounds.Left, bounds.Right);
            float y0 = Math.Clamp(worldYCm - stepYCm, bounds.Top, bounds.Bottom);
            float y1 = Math.Clamp(worldYCm + stepYCm, bounds.Top, bounds.Bottom);

            float leftHeight = heightCm;
            float rightHeight = heightCm;
            float topHeight = heightCm;
            float bottomHeight = heightCm;

            if (x0 != worldXCm && !TrySampleHeightCm(x0, worldYCm, out leftHeight, layerIndex))
            {
                return false;
            }

            if (x1 != worldXCm && !TrySampleHeightCm(x1, worldYCm, out rightHeight, layerIndex))
            {
                return false;
            }

            if (y0 != worldYCm && !TrySampleHeightCm(worldXCm, y0, out topHeight, layerIndex))
            {
                return false;
            }

            if (y1 != worldYCm && !TrySampleHeightCm(worldXCm, y1, out bottomHeight, layerIndex))
            {
                return false;
            }

            float deltaXMeters = Math.Max(0.01f, (x1 - x0) * 0.01f);
            float deltaYMeters = Math.Max(0.01f, (y1 - y0) * 0.01f);
            float dhdx = ((rightHeight - leftHeight) * 0.01f) / deltaXMeters;
            float dhdy = ((bottomHeight - topHeight) * 0.01f) / deltaYMeters;
            normal = Vector3.Normalize(new Vector3(-dhdx, 1f, -dhdy));
            return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z);
        }

        private float GetCellWidthCm()
        {
            return _asset.SampleColumns > 1
                ? Math.Max(1f, (float)_asset.Bounds.Width / (_asset.SampleColumns - 1))
                : Math.Max(1f, _asset.Bounds.Width);
        }

        private float GetCellHeightCm()
        {
            return _asset.SampleRows > 1
                ? Math.Max(1f, (float)_asset.Bounds.Height / (_asset.SampleRows - 1))
                : Math.Max(1f, _asset.Bounds.Height);
        }

        private bool TryGetRayBoundsInterval(in ScreenRay ray, out float startT, out float endT)
        {
            startT = 0f;
            endT = float.PositiveInfinity;

            float originXCm = ray.Origin.X * MToCm;
            float originYCm = ray.Origin.Z * MToCm;
            float dirXCm = ray.Direction.X * MToCm;
            float dirYCm = ray.Direction.Z * MToCm;
            WorldAabbCm bounds = _asset.Bounds;

            return TryClipAxis(originXCm, dirXCm, bounds.Left, bounds.Right, ref startT, ref endT) &&
                   TryClipAxis(originYCm, dirYCm, bounds.Top, bounds.Bottom, ref startT, ref endT);
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
