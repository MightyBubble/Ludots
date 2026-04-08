using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    public interface IVisualHeightmap
    {
        bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0);

        bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0);

        bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0);

        bool RaycastGroundBatch(
            ReadOnlySpan<float> originXMeters,
            ReadOnlySpan<float> originYMeters,
            ReadOnlySpan<float> originZMeters,
            ReadOnlySpan<float> directionX,
            ReadOnlySpan<float> directionY,
            ReadOnlySpan<float> directionZ,
            Span<VisualGroundHit> outHits,
            Span<byte> outHitMask,
            int layerIndex = 0);
    }
}
