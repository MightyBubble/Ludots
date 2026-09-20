using System;
using System.IO;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace RaylibVisualAtmosphereShowcaseMod.Runtime;

/// <summary>
/// Builds a continuous ContinuousHeightmap island (.height).
/// Land must rise above <see cref="SeaLevelCm"/>; water plane + refraction read shallow/deep from the submerged floor.
/// </summary>
internal static class IslandTerrainGenerator
{
    public const string RelativeAssetPath = "assets/terrain/tropical_island.height";
    public const int SampleColumns = 513;
    public const int SampleRows = 513;

    public static readonly WorldAabbCm BoundsCm = new(-64000, -64000, 128000, 128000);

    /// <summary>Sea plane height in centimeters (Host waterPlaneY = this / 100).</summary>
    public const short SeaLevelCm = 200;

    /// <summary>Peak span above sea used by absolute island vertex colors.</summary>
    public const float AbsoluteColorPeakSpanCm = 14000f;

    public static void EnsureGenerated(IModContext context)
    {
        string uri = $"{context.ModId}:{RelativeAssetPath}";
        if (!context.VFS.TryResolveFullPath(uri, out string? fullPath))
        {
            throw new InvalidOperationException($"Failed to resolve path: {uri}");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var samples = new short[SampleColumns * SampleRows];
        for (int row = 0; row < SampleRows; row++)
        {
            for (int col = 0; col < SampleColumns; col++)
            {
                float u = col / (float)(SampleColumns - 1);
                float v = row / (float)(SampleRows - 1);
                float worldXcm = BoundsCm.X + (u * BoundsCm.Width);
                float worldYcm = BoundsCm.Y + (v * BoundsCm.Height);
                samples[(row * SampleColumns) + col] = HeightCmAt(worldXcm, worldYcm);
            }
        }

        ContinuousHeightmapAsset asset = ContinuousHeightmapAsset.CreateSingleLayer(
            BoundsCm,
            SampleColumns,
            SampleRows,
            samples,
            layerName: "tropical_island",
            interpolationMode: ContinuousHeightmapInterpolationMode.BilinearHeightfield);

        using FileStream stream = File.Create(fullPath);
        ContinuousHeightmapBinary.Write(stream, asset);
        context.Log(
            $"[RaylibVisualAtmosphereShowcaseMod] Generated continuous island heightmap {fullPath} ({new FileInfo(fullPath).Length} bytes)");
    }

    public static short HeightCmAt(float worldXcm, float worldYcm)
    {
        float cx = BoundsCm.X + (BoundsCm.Width * 0.5f);
        float cy = BoundsCm.Y + (BoundsCm.Height * 0.5f);
        float dx = (worldXcm - cx) / (BoundsCm.Width * 0.5f);
        float dy = (worldYcm - cy) / (BoundsCm.Height * 0.5f);
        float dist = MathF.Sqrt((dx * dx) + (dy * dy));
        float angle = MathF.Atan2(dy, dx);

        float coast =
            0.34f +
            0.055f * MathF.Sin(angle * 5f) +
            0.032f * MathF.Sin(angle * 9f + 1.2f) +
            0.024f * MathF.Cos(angle * 3f - 0.7f) +
            0.018f * MathF.Sin(angle * 2f + 0.4f);
        // Soft lagoon indentation (north) without swallowing the whole island.
        if (angle > 1.0f && angle < 1.65f)
        {
            coast -= 0.045f;
        }

        float ridge =
            0.42f * RidgeNoise(worldXcm * 0.00028f, worldYcm * 0.00028f) +
            0.34f * RidgeNoise(worldXcm * 0.00078f + 13f, worldYcm * 0.00078f - 9f) +
            0.24f * ValueNoise(worldXcm * 0.0018f - 4f, worldYcm * 0.0018f + 6f);

        // Erosion gullies: carve valleys along angular sectors for rugged silhouette.
        float gully =
            MathF.Pow(MathF.Abs(MathF.Sin(angle * 7f + ridge * 2.2f)), 2.4f) *
            MathF.Pow(Math.Clamp(1f - dist / MathF.Max(coast, 0.08f), 0f, 1f), 1.35f);

        // Far ocean floor + satellite islet.
        if (dist > coast + 0.28f)
        {
            float ix = dx + 0.52f;
            float iy = dy + 0.46f;
            float id = MathF.Sqrt((ix * ix) + (iy * iy));
            if (id < 0.07f)
            {
                float isletInland = 1f - (id / 0.07f);
                float islet = SeaLevelCm + 80f + (isletInland * 900f) + (ridge * 400f * isletInland);
                return ClampCm(islet);
            }

            float abyss = Math.Clamp((dist - coast - 0.28f) / 0.50f, 0f, 1f);
            return ClampCm(SeaLevelCm - 280f - (abyss * 620f));
        }

        // Shallow shelf under water (turquoise via refraction of colored floor).
        if (dist > coast)
        {
            float shelfT = Math.Clamp((dist - coast) / 0.28f, 0f, 1f);
            // Near shore: almost awash sand shelf; outer: deeper cyan floor.
            float shelfFloor = SeaLevelCm - 18f - (shelfT * shelfT * 210f);
            return ClampCm(shelfFloor);
        }

        float inland = Math.Clamp(1f - (dist / MathF.Max(coast, 0.08f)), 0f, 1f);

        // Thin bright beach ring just above sea.
        float beach = SeaLevelCm + 35f + (inland * 220f);

        // Rapid rise: foothills → steep eroded peaks (readable from aerial).
        float mountainCore = MathF.Pow(Math.Clamp((inland - 0.08f) / 0.92f, 0f, 1f), 1.15f);
        float peakBoost = MathF.Pow(mountainCore, 1.55f);
        float mountain =
            SeaLevelCm +
            420f +
            (mountainCore * 4200f) +
            (peakBoost * (5200f + ridge * 6800f)) -
            (gully * 2200f);

        // Keep a short beach band before mountain blend dominates.
        float blend = Math.Clamp((inland - 0.05f) / 0.22f, 0f, 1f);
        blend = blend * blend * (3f - (2f * blend));
        float height = beach + ((mountain - beach) * blend);

        // Northern lagoon pocket: shallow submerged floor inside the coast line.
        if (angle > 1.05f && angle < 1.55f && dist > coast - 0.11f && dist < coast - 0.015f && inland < 0.38f)
        {
            height = SeaLevelCm - 42f;
        }

        return ClampCm(height);
    }

    private static short ClampCm(float value) =>
        (short)Math.Clamp((int)MathF.Round(value), short.MinValue, short.MaxValue);

    private static float RidgeNoise(float x, float y)
    {
        float n = ValueNoise(x, y);
        return 1f - MathF.Abs((n * 2f) - 1f);
    }

    private static float ValueNoise(float x, float y)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        float v00 = Hash01(x0, y0);
        float v10 = Hash01(x0 + 1, y0);
        float v01 = Hash01(x0, y0 + 1);
        float v11 = Hash01(x0 + 1, y0 + 1);
        float ix0 = v00 + ((v10 - v00) * Smooth(fx));
        float ix1 = v01 + ((v11 - v01) * Smooth(fx));
        return ix0 + ((ix1 - ix0) * Smooth(fy));
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Hash01(int x, int y)
    {
        int n = (x * 374761393) + (y * 668265263);
        n = (n ^ (n >> 13)) * 1274126177;
        n ^= n >> 16;
        return (n & 0x7FFFFFFF) / (float)int.MaxValue;
    }
}
