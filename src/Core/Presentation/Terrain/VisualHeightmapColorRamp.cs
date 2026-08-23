using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Terrain
{
    public static class VisualHeightmapColorRamp
    {
        public static float ResolveHeightBandContrast(float heightBand, float colorContrast)
        {
            if (!float.IsFinite(heightBand)) throw new ArgumentOutOfRangeException(nameof(heightBand));
            if (!float.IsFinite(colorContrast) || colorContrast <= 0f) throw new ArgumentOutOfRangeException(nameof(colorContrast));

            float clampedBand = Math.Clamp(heightBand, 0f, 1f);
            float clampedContrast = Math.Clamp(colorContrast, 0.01f, 16f);
            return Math.Clamp(((clampedBand - 0.5f) * clampedContrast) + 0.5f, 0f, 1f);
        }

        public static Vector3 ResolveColor(float heightBand, float slope)
        {
            return ResolveColor(heightBand, slope, heightCm: 1f, seaLevelCm: 0f);
        }

        /// <summary>
        /// Terrain color ramp that separates water from land by absolute height so that
        /// low-lying plains do not get painted with a sea-like tone. Below <paramref name="seaLevelCm"/>
        /// renders as a depth-shaded blue water band; land uses a green -> tan -> peak ramp.
        /// This overload derives its own normalized band from the global <paramref name="heightBand"/>
        /// and only knows whether the sample is water or land, so land and sea share the global
        /// height range. Prefer the range-aware overload for independent land/sea normalization.
        /// </summary>
        public static Vector3 ResolveColor(float heightBand, float slope, float heightCm, float seaLevelCm)
        {
            if (!float.IsFinite(heightCm)) throw new ArgumentOutOfRangeException(nameof(heightCm));
            if (!float.IsFinite(seaLevelCm)) throw new ArgumentOutOfRangeException(nameof(seaLevelCm));
            bool water = heightCm <= seaLevelCm;
            return ResolveSplitBandColor(water, Math.Clamp(heightBand, 0f, 1f), slope);
        }

        /// <summary>
        /// Range-aware terrain ramp. Water (heightCm &lt;= seaLevelCm) is normalized within
        /// [minHeightCm, seaLevelCm] and shaded deep-&gt;shallow blue; land is normalized within
        /// [seaLevelCm, maxHeightCm] and shaded green-&gt;tan-&gt;peak. Independent normalization keeps
        /// land relief readable even when the sea floor spans a much larger vertical range.
        /// <paramref name="colorContrast"/> is applied to each side's local band.
        /// </summary>
        public static Vector3 ResolveColorRanged(
            float heightCm,
            float slope,
            float minHeightCm,
            float maxHeightCm,
            float seaLevelCm,
            float colorContrast)
        {
            if (!float.IsFinite(heightCm)) throw new ArgumentOutOfRangeException(nameof(heightCm));
            if (!float.IsFinite(minHeightCm)) throw new ArgumentOutOfRangeException(nameof(minHeightCm));
            if (!float.IsFinite(maxHeightCm)) throw new ArgumentOutOfRangeException(nameof(maxHeightCm));
            if (!float.IsFinite(seaLevelCm)) throw new ArgumentOutOfRangeException(nameof(seaLevelCm));

            bool water = heightCm <= seaLevelCm;
            float band;
            if (water)
            {
                float span = MathF.Max(1f, seaLevelCm - minHeightCm);
                // 1 at coast (shallow), 0 at deepest.
                band = Math.Clamp((heightCm - minHeightCm) / span, 0f, 1f);
            }
            else
            {
                float span = MathF.Max(1f, maxHeightCm - seaLevelCm);
                band = Math.Clamp((heightCm - seaLevelCm) / span, 0f, 1f);
            }

            band = ResolveHeightBandContrast(band, colorContrast);
            return ResolveSplitBandColor(water, band, slope);
        }

        private static Vector3 ResolveSplitBandColor(bool water, float band, float slope)
        {
            if (!float.IsFinite(band)) throw new ArgumentOutOfRangeException(nameof(band));
            if (!float.IsFinite(slope)) throw new ArgumentOutOfRangeException(nameof(slope));
            band = Math.Clamp(band, 0f, 1f);

            if (water)
            {
                // band: 1 = shallow coast, 0 = deep abyss.
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

        public static Vector3 ResolveGrayscale(float heightBand)
        {
            if (!float.IsFinite(heightBand)) throw new ArgumentOutOfRangeException(nameof(heightBand));

            float value = Math.Clamp(heightBand, 0f, 1f);
            return new Vector3(value, value, value);
        }
    }
}
