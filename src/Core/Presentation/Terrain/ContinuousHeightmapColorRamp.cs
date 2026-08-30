using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Terrain
{
    public static class ContinuousHeightmapColorRamp
    {
        public static float ResolveHeightBandContrast(float heightBand, float colorContrast)
        {
            if (!float.IsFinite(heightBand)) throw new ArgumentOutOfRangeException(nameof(heightBand));
            if (!float.IsFinite(colorContrast) || colorContrast <= 0f) throw new ArgumentOutOfRangeException(nameof(colorContrast));

            float clampedBand = Math.Clamp(heightBand, 0f, 1f);
            float clampedContrast = Math.Clamp(colorContrast, 0.01f, 16f);
            return Math.Clamp(((clampedBand - 0.5f) * clampedContrast) + 0.5f, 0f, 1f);
        }

        public static Vector3 ResolveColorRanged(
            float heightCm,
            float slope,
            float minHeightCm,
            float maxHeightCm,
            float seaLevelCm,
            float colorContrast)
        {
            if (!float.IsFinite(heightCm)) throw new ArgumentOutOfRangeException(nameof(heightCm));
            if (!float.IsFinite(slope)) throw new ArgumentOutOfRangeException(nameof(slope));
            if (!float.IsFinite(minHeightCm)) throw new ArgumentOutOfRangeException(nameof(minHeightCm));
            if (!float.IsFinite(maxHeightCm)) throw new ArgumentOutOfRangeException(nameof(maxHeightCm));
            if (!float.IsFinite(seaLevelCm)) throw new ArgumentOutOfRangeException(nameof(seaLevelCm));
            if (maxHeightCm < minHeightCm) throw new ArgumentOutOfRangeException(nameof(maxHeightCm));

            bool water = heightCm <= seaLevelCm;
            float band = water
                ? ResolveWaterBand(heightCm, minHeightCm, maxHeightCm, seaLevelCm)
                : ResolveLandBand(heightCm, minHeightCm, maxHeightCm, seaLevelCm);
            band = ResolveHeightBandContrast(band, colorContrast);
            return ResolveSplitBandColor(water, band, slope);
        }

        public static Vector3 ResolveGrayscale(float heightBand)
        {
            if (!float.IsFinite(heightBand)) throw new ArgumentOutOfRangeException(nameof(heightBand));

            float value = Math.Clamp(heightBand, 0f, 1f);
            return new Vector3(value, value, value);
        }

        private static float ResolveWaterBand(float heightCm, float minHeightCm, float maxHeightCm, float seaLevelCm)
        {
            float deepCm = MathF.Min(minHeightCm, seaLevelCm);
            float shallowCm = MathF.Min(maxHeightCm, seaLevelCm);
            float span = MathF.Max(1f, shallowCm - deepCm);
            return Math.Clamp((heightCm - deepCm) / span, 0f, 1f);
        }

        private static float ResolveLandBand(float heightCm, float minHeightCm, float maxHeightCm, float seaLevelCm)
        {
            float lowlandCm = MathF.Max(minHeightCm, seaLevelCm);
            float highlandCm = MathF.Max(maxHeightCm, lowlandCm + 1f);
            float span = MathF.Max(1f, highlandCm - lowlandCm);
            return Math.Clamp((heightCm - lowlandCm) / span, 0f, 1f);
        }

        private static Vector3 ResolveSplitBandColor(bool water, float band, float slope)
        {
            band = Math.Clamp(band, 0f, 1f);
            if (water)
            {
                Vector3 deep = new(12f / 255f, 34f / 255f, 66f / 255f);
                Vector3 shallow = new(48f / 255f, 104f / 255f, 154f / 255f);
                return Vector3.Lerp(deep, shallow, band);
            }

            Vector3 lowland = new(58f / 255f, 120f / 255f, 62f / 255f);
            Vector3 mid = new(120f / 255f, 150f / 255f, 74f / 255f);
            Vector3 high = new(176f / 255f, 148f / 255f, 96f / 255f);
            Vector3 peak = new(232f / 255f, 232f / 255f, 224f / 255f);
            Vector3 color = band < 0.45f
                ? Vector3.Lerp(lowland, mid, band / 0.45f)
                : band < 0.80f
                    ? Vector3.Lerp(mid, high, (band - 0.45f) / 0.35f)
                    : Vector3.Lerp(high, peak, (band - 0.80f) / 0.20f);
            float shade = 1f - Math.Clamp(Math.Clamp(slope, 0f, 1f) * 0.42f, 0f, 0.42f);
            return Vector3.Clamp(color * shade, Vector3.Zero, Vector3.One);
        }
    }
}
