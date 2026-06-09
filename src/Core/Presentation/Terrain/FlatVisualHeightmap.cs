using System;
using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Explicit flat-ground heightmap for maps that author a flat visual terrain surface.
    /// Consumers still read a single height SSOT instead of inventing independent ground projection rules.
    /// </summary>
    public sealed class FlatVisualHeightmap : IVisualHeightmap
    {
        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
        {
            heightCm = 0f;
            return layerIndex == 0;
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                throw new ArgumentException("FlatVisualHeightmap batch spans must have identical lengths.");
            }

            if (layerIndex != 0)
            {
                return false;
            }

            outHeightCm.Clear();
            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
        {
            hit = default;
            if (layerIndex != 0)
            {
                return false;
            }

            float dirY = ray.Direction.Y;
            if (!float.IsFinite(dirY) || MathF.Abs(dirY) < 1e-6f)
            {
                return false;
            }

            float originY = ray.Origin.Y;
            if (!float.IsFinite(originY))
            {
                return false;
            }

            float t = -originY / dirY;
            if (!float.IsFinite(t) || t < 0f)
            {
                return false;
            }

            Vector3 point = ray.Origin + (ray.Direction * t);
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Z))
            {
                return false;
            }

            hit = new VisualGroundHit(
                worldXCm: point.X * 100f,
                worldYCm: point.Z * 100f,
                heightCm: 0f,
                layerIndex: 0,
                distanceMeters: t,
                normal: Vector3.UnitY);
            return true;
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
            if (layerIndex != 0)
            {
                outHitMask.Clear();
                return false;
            }

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
                throw new ArgumentException("FlatVisualHeightmap raycast batch spans must have identical lengths.");
            }

            for (int i = 0; i < count; i++)
            {
                float dirY = directionY[i];
                if (!float.IsFinite(dirY) || MathF.Abs(dirY) < 1e-6f)
                {
                    outHitMask[i] = 0;
                    continue;
                }

                float originY = originYMeters[i];
                if (!float.IsFinite(originY))
                {
                    outHitMask[i] = 0;
                    continue;
                }

                float t = -originY / dirY;
                if (!float.IsFinite(t) || t < 0f)
                {
                    outHitMask[i] = 0;
                    continue;
                }

                float x = originXMeters[i] + (directionX[i] * t);
                float z = originZMeters[i] + (directionZ[i] * t);
                if (!float.IsFinite(x) || !float.IsFinite(z))
                {
                    outHitMask[i] = 0;
                    continue;
                }

                outWorldXCm[i] = x * 100f;
                outWorldYCm[i] = z * 100f;
                outHeightCm[i] = 0f;
                outDistanceMeters[i] = t;
                outNormalX[i] = 0f;
                outNormalY[i] = 1f;
                outNormalZ[i] = 0f;
                outLayerIndex[i] = 0;
                outHitMask[i] = 1;
            }

            return true;
        }
    }
}
