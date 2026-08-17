using System;

namespace Ludots.Platform.Abstractions
{
    public interface IVisualHeightmap
    {
        bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1);

        bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1);

        bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1);

        bool RaycastGroundBatch(
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
            int layerIndex = -1);
    }
}
