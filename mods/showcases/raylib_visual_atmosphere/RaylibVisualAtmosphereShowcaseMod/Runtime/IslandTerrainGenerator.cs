using System;
using System.IO;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Terrain;

namespace RaylibVisualAtmosphereShowcaseMod.Runtime;

/// <summary>
/// Builds a continuous VisualHeightmap island (.vhtm).
/// Land must rise above <see cref="SeaLevelCm"/>; water plane + refraction read shallow/deep from the submerged floor.
/// </summary>
internal static class IslandTerrainGenerator
{
    public const string RelativeAssetPath = "assets/terrain/tropical_island.vhtm";
    public const int SampleColumns = 513;
    public const int SampleRows = 513;

    public static readonly WorldAabbCm BoundsCm = new(-64000, -64000, 128000, 128000);

    /// <summary>Sea plane height in centimeters (Host waterPlaneY = this / 100).</summary>
    public const short SeaLevelCm = 200;

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

        VisualHeightmapAsset asset = VisualHeightmapAsset.CreateSingleLayer(
            BoundsCm,
            SampleColumns,
            SampleRows,
            samples,
            layerName: "tropical_island",
            interpolationMode: VisualHeightmapInterpolationMode.BilinearHeightfield);

        using FileStream stream = File.Create(fullPath);
        VisualHeightmapBinary.Write(stream, asset);
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
            0.36f +
            0.045f * MathF.Sin(angle * 5f) +
            0.028f * MathF.Sin(angle * 9f + 1.2f) +
            0.022f * MathF.Cos(angle * 3f - 0.7f);
        // Soft lagoon indentation (north) without swallowing the whole island.
        if (angle > 1.0f && angle < 1.6f)
        {
            coast -= 0.03f;
        }

        float ridge =
            0.50f * RidgeNoise(worldXcm * 0.00032f, worldYcm * 0.00032f) +
            0.32f * RidgeNoise(worldXcm * 0.00085f + 13f, worldYcm * 0.00085f - 9f) +
            0.18f * ValueNoise(worldXcm * 0.0020f - 4f, worldYcm * 0.0020f + 6f);

        // Far ocean floor.
        if (dist > coast + 0.22f)
        {
            float ix = dx + 0.50f;
            float iy = dy + 0.44f;
            float id = MathF.Sqrt((ix * ix) + (iy * iy));
            if (id < 0.065f)
            {
                float islet = SeaLevelCm + 120f + ((1f - (id / 0.065f)) * 1100f);
                return ClampCm(islet);
            }

            float abyss = Math.Clamp((dist - coast - 0.22f) / 0.55f, 0f, 1f);
            return ClampCm(SeaLevelCm - 220f - (abyss * 520f));
        }

        // Shallow shelf under water (turquoise via refraction of colored floor).
        if (dist > coast)
        {
            float shelfT = Math.Clamp((dist - coast) / 0.22f, 0f, 1f);
            return ClampCm(SeaLevelCm - 25f - (shelfT * 160f));
        }

        float inland = Math.Clamp(1f - (dist / Math.Max(coast, 0.08f)), 0f, 1f);
        // Beach just above sea → grass slopes → rocky peaks.
        float beachHeight = SeaLevelCm + 40f + (inland * 180f);
        float mountain = SeaLevelCm + 280f + (inland * inland * (1600f + ridge * 3200f));
        float blend = Math.Clamp((inland - 0.12f) / 0.88f, 0f, 1f);
        float height = beachHeight + ((mountain - beachHeight) * blend);

        // Northern lagoon pocket: keep a shallow submerged floor, not a land ring.
        if (angle > 1.05f && angle < 1.55f && dist > coast - 0.10f && dist < coast - 0.02f && inland < 0.35f)
        {
            height = SeaLevelCm - 35f;
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
