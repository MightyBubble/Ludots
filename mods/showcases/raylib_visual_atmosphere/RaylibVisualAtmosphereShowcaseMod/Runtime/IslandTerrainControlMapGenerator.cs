using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Ludots.Core.Modding;

namespace RaylibVisualAtmosphereShowcaseMod.Runtime;

/// <summary>
/// Bakes RGBA8 control weights for the showcase island.
/// Channels: R=sand, G=grass, B=dirt, A=rock (normalized per pixel).
/// UV: world XZ → (worldXcm - Bounds.X) / Bounds.Width, (worldYcm - Bounds.Y) / Bounds.Height
/// matching VisualHeightmap sample UV / shader uControlBounds meters.
/// </summary>
internal static class IslandTerrainControlMapGenerator
{
    public const string RelativeAssetPath = "assets/Textures/terrain_control_weights.png";

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

        int columns = IslandTerrainGenerator.SampleColumns;
        int rows = IslandTerrainGenerator.SampleRows;
        var heights = new short[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                float u = col / (float)(columns - 1);
                float v = row / (float)(rows - 1);
                float worldXcm = IslandTerrainGenerator.BoundsCm.X + (u * IslandTerrainGenerator.BoundsCm.Width);
                float worldYcm = IslandTerrainGenerator.BoundsCm.Y + (v * IslandTerrainGenerator.BoundsCm.Height);
                heights[(row * columns) + col] = IslandTerrainGenerator.HeightCmAt(worldXcm, worldYcm);
            }
        }

        float stepXcm = IslandTerrainGenerator.BoundsCm.Width / (float)(columns - 1);
        float stepYcm = IslandTerrainGenerator.BoundsCm.Height / (float)(rows - 1);
        float sea = IslandTerrainGenerator.SeaLevelCm;
        float peakSpan = IslandTerrainGenerator.AbsoluteColorPeakSpanCm;
        var rgba = new byte[columns * rows * 4];
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int idx = (row * columns) + col;
                float heightCm = heights[idx];
                float heightBand = MathF.Min(1f, (heightCm - sea) / peakSpan);
                float slope = EstimateSlope01(heights, columns, rows, col, row, stepXcm, stepYcm);
                float noise =
                    0.55f * ValueNoise(col * 0.035f, row * 0.035f) +
                    0.45f * ValueNoise(col * 0.11f + 9f, row * 0.11f - 4f);

                ResolveWeights(heightBand, slope, noise, out float sand, out float grass, out float dirt, out float rock);
                float sum = MathF.Max(sand + grass + dirt + rock, 1e-5f);
                sand /= sum;
                grass /= sum;
                dirt /= sum;
                rock /= sum;

                int o = idx * 4;
                rgba[o + 0] = ToByte01(sand);
                rgba[o + 1] = ToByte01(grass);
                rgba[o + 2] = ToByte01(dirt);
                rgba[o + 3] = ToByte01(rock);
            }
        }

        WriteRgba8Png(fullPath, columns, rows, rgba);
        context.Log(
            $"[RaylibVisualAtmosphereShowcaseMod] Generated terrain control map {fullPath} ({columns}x{rows}, {new FileInfo(fullPath).Length} bytes)");
    }

    private static void ResolveWeights(
        float heightBand,
        float slope,
        float noise,
        out float sand,
        out float grass,
        out float dirt,
        out float rock)
    {
        // Beach ring just above sea; mid grass; dirt mid-high; rock peaks + steep slopes.
        float land = Math.Clamp(heightBand, 0f, 1f);
        float submerged = Math.Clamp((-heightBand) / 0.04f, 0f, 1f);

        sand =
            SmoothRange(land, 0f, 0.02f, 0.08f, 0.14f) * (1f - slope * 0.65f) +
            (1f - submerged) * 0.15f * (heightBand <= 0f ? 1f : 0f);
        grass =
            SmoothRange(land, 0.03f, 0.10f, 0.28f, 0.40f) *
            (1f - Math.Clamp(slope * 1.1f, 0f, 0.85f)) *
            (0.75f + 0.25f * noise);
        dirt =
            SmoothRange(land, 0.22f, 0.34f, 0.52f, 0.66f) *
            (0.70f + 0.30f * (1f - noise));
        rock =
            SmoothRange(land, 0.48f, 0.62f, 1.1f, 1.2f) +
            MathF.Pow(Math.Clamp(slope, 0f, 1f), 1.35f) * SmoothRange(land, 0.08f, 0.18f, 1.1f, 1.2f);

        if (heightBand < -0.002f)
        {
            sand = 0.85f;
            grass = 0.05f;
            dirt = 0.05f;
            rock = 0.05f;
        }
    }

    private static float SmoothRange(float x, float in0, float in1, float out0, float out1)
    {
        float rise = SmoothStep(in0, in1, x);
        float fall = 1f - SmoothStep(out0, out1, x);
        return Math.Clamp(rise * fall, 0f, 1f);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / MathF.Max(edge1 - edge0, 1e-5f), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float EstimateSlope01(
        short[] heights,
        int columns,
        int rows,
        int col,
        int row,
        float stepXcm,
        float stepYcm)
    {
        int left = Math.Max(0, col - 1);
        int right = Math.Min(columns - 1, col + 1);
        int top = Math.Max(0, row - 1);
        int bottom = Math.Min(rows - 1, row + 1);
        float hLeft = heights[(row * columns) + left];
        float hRight = heights[(row * columns) + right];
        float hTop = heights[(top * columns) + col];
        float hBottom = heights[(bottom * columns) + col];
        float dx = MathF.Max(1f, (right - left) * stepXcm);
        float dz = MathF.Max(1f, (bottom - top) * stepYcm);
        float nx = -(hRight - hLeft) / dx;
        float nz = -(hBottom - hTop) / dz;
        float ny = 1f;
        float len = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        float normalY = len > 1e-5f ? ny / len : 1f;
        return Math.Clamp(1f - normalY, 0f, 1f);
    }

    private static byte ToByte01(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

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

    private static void WriteRgba8Png(string path, int width, int height, byte[] rgba)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new InvalidOperationException("RGBA buffer size does not match width*height*4.");
        }

        int rawStride = 1 + (width * 4);
        var raw = new byte[rawStride * height];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * rawStride;
            raw[rowStart] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, rowStart + 1, width * 4);
        }

        byte[] compressed = CompressZlib(raw);
        using var stream = File.Create(path);
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        stream.Write(signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(stream, "IHDR"u8, ihdr);
        WriteChunk(stream, "IDAT"u8, compressed);
        WriteChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static byte[] CompressZlib(byte[] raw)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x01);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        uint adler = Adler32(raw);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, adler);
        output.Write(checksum);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        stream.Write(len);
        stream.Write(type);
        stream.Write(data);

        uint crc = Crc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint ModAdler = 65521;
        uint a = 1;
        uint b = 0;
        for (int i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % ModAdler;
            b = (b + a) % ModAdler;
        }

        return (b << 16) | a;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < type.Length; i++)
        {
            crc = Crc32Update(crc, type[i]);
        }

        for (int i = 0; i < data.Length; i++)
        {
            crc = Crc32Update(crc, data[i]);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint Crc32Update(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
        {
            uint mask = (uint)-(int)(crc & 1);
            crc = (crc >> 1) ^ (0xEDB88320u & mask);
        }

        return crc;
    }
}
