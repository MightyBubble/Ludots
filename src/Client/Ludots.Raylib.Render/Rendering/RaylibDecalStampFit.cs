using System;
using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// Shared yawed-stamp fit for receiver-mesh projectors: samples the bound receiver heightmap across a
    /// 7x7 grid over the stamp footprint and centers the projector between sampled min/max so the decal
    /// volume hugs the receiver surface. A footprint that cannot be fully sampled throws instead of
    /// leaving the authored Y.
    /// </summary>
    internal static class RaylibDecalStampFit
    {
        public const int HeightSampleSegments = 6;

        public static Vector3 FitCenter(
            in Vector3 stampCenter,
            float yawRad,
            in Vector2 stampSizeMeters,
            int stableId,
            IVisualHeightmap heightmap,
            string receiverName)
        {
            if (!float.IsFinite(stampSizeMeters.X) || !float.IsFinite(stampSizeMeters.Y) ||
                stampSizeMeters.X <= 0f || stampSizeMeters.Y <= 0f)
            {
                throw new InvalidOperationException(
                    $"{receiverName} Decal stableId={stableId} stamp size must be finite and positive, got {stampSizeMeters}.");
            }

            float cos = MathF.Cos(yawRad);
            float sin = MathF.Sin(yawRad);
            float minHeightM = float.PositiveInfinity;
            float maxHeightM = float.NegativeInfinity;
            int samples = HeightSampleSegments;
            for (int y = 0; y <= samples; y++)
            {
                float v = (y / (float)samples) - 0.5f;
                float localZ = v * stampSizeMeters.Y;
                for (int x = 0; x <= samples; x++)
                {
                    float u = (x / (float)samples) - 0.5f;
                    float localX = u * stampSizeMeters.X;
                    float worldX = stampCenter.X + (localX * cos) - (localZ * sin);
                    float worldZ = stampCenter.Z + (localX * sin) + (localZ * cos);
                    float worldXCm = worldX * WorldUnits.CmPerMeter;
                    float worldYCm = worldZ * WorldUnits.CmPerMeter;
                    if (!heightmap.TrySampleHeightCm(worldXCm, worldYCm, out float heightCm))
                    {
                        throw new InvalidOperationException(
                            $"{receiverName} Decal stableId={stableId} stamp does not overlap sampleable receiver height at ({worldXCm:F1},{worldYCm:F1}).");
                    }

                    float heightM = WorldUnits.CmToM(heightCm);
                    minHeightM = MathF.Min(minHeightM, heightM);
                    maxHeightM = MathF.Max(maxHeightM, heightM);
                }
            }

            return new Vector3(stampCenter.X, (minHeightM + maxHeightM) * 0.5f, stampCenter.Z);
        }
    }
}
