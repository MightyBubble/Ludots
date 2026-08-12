using System;
using System.IO;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Terrain;

namespace RaylibVisualAtmosphereShowcaseMod.Runtime;

/// <summary>
/// Builds a continuous VisualHeightmap island (.vhtm). VertexMap is intentionally not used —
/// reflective water and terrain coloring ride the VisualHeightmap host path.
/// </summary>
internal static class IslandTerrainGenerator
{
    public const string RelativeAssetPath = "assets/terrain/tropical_island.vhtm";
    public const int SampleColumns = 513;
    public const int SampleRows = 513;

    /// <summary>World AABB in cm, centered at origin (matches Grid board + virtual cameras).</summary>
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
            0.38f +
            0.05f * MathF.Sin(angle * 5f) +
            0.03f * MathF.Sin(angle * 9f + 1.2f) +
            0.025f * MathF.Cos(angle * 3f - 0.7f);
        if (angle > 0.9f && angle < 1.7f)
        {
            coast -= 0.05f; // northern lagoon bite
        }

        // Deep ocean floor far from shore.
        if (dist > coast + 0.18f)
        {
            // Tiny satellite islet SW.
            float ix = dx + 0.52f;
            float iy = dy + 0.46f;
            float id = MathF.Sqrt((ix * ix) + (iy * iy));
            if (id < 0.07f)
            {
                float islet = SeaLevelCm + 80f + ((1f - (id / 0.07f)) * 900f);
                return (short)Math.Clamp((int)MathF.Round(islet), short.MinValue, short.MaxValue);
            }

            float abyss = Math.Clamp((dist - coast - 0.18f) / 0.5f, 0f, 1f);
            float floor = SeaLevelCm - 180f - (abyss * 420f);
            return (short)Math.Clamp((int)MathF.Round(floor), short.MinValue, short.MaxValue);
        }

        float ridge =
            0.55f * RidgeNoise(worldXcm * 0.00035f, worldYcm * 0.00035f) +
            0.30f * RidgeNoise(worldXcm * 0.0009f + 13f, worldYcm * 0.0009f - 9f) +
            0.15f * ValueNoise(worldXcm * 0.0022f - 4f, worldYcm * 0.0022f + 6f);

        float inland = Math.Clamp(1f - (dist / Math.Max(coast, 0.08f)), 0f, 1f);
        float beach = Math.Clamp((coast + 0.04f - dist) / 0.08f, 0f, 1f);

        // Shelf just under water → sand → grass slopes → rock peaks.
        float shelf = SeaLevelCm - 40f;
        float peak = SeaLevelCm + 200f + (inland * inland * (1200f + ridge * 2800f));
        float height = shelf + ((peak - shelf) * beach);

        if (angle > 0.95f && angle < 1.65f && dist > coast - 0.09f && dist < coast + 0.02f)
        {
            height = Math.Min(height, SeaLevelCm - 20f); // lagoon floor
        }

        return (short)Math.Clamp((int)MathF.Round(height), short.MinValue, short.MaxValue);
    }

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
