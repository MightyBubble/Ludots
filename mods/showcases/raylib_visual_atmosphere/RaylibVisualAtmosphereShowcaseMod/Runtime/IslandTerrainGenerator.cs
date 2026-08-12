using System;
using System.IO;
using System.Text;
using Ludots.Core.Modding;

namespace RaylibVisualAtmosphereShowcaseMod.Runtime;

internal static class IslandTerrainGenerator
{
    private const string FileName = "tropical_island_v3.vtxm";
    private const int Version = 2;
    private const int ChunkSize = 64;
    private const int WidthChunks = 4;
    private const int HeightChunks = 4;

    public static void EnsureGenerated(IModContext context)
    {
        string uri = $"{context.ModId}:assets/Data/Maps/{FileName}";
        if (!context.VFS.TryResolveFullPath(uri, out string? fullPath))
        {
            throw new InvalidOperationException($"Failed to resolve path: {uri}");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Always regenerate so island remakes are not stuck on an old sparse mesh.
        using FileStream stream = File.Create(fullPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("VTXM"));
        writer.Write(Version);
        writer.Write(WidthChunks);
        writer.Write(HeightChunks);
        writer.Write(ChunkSize);
        writer.Write(0);

        var packed = new byte[ChunkSize * ChunkSize];
        var layer2 = new byte[ChunkSize * ChunkSize];
        var flagsZeros = new byte[(ChunkSize * ChunkSize / 64) * sizeof(ulong)];
        var rampsBytes = new byte[flagsZeros.Length];
        var factions = new byte[ChunkSize * ChunkSize];
        var extraFlags0 = new byte[flagsZeros.Length];
        var extraFlags1 = new byte[flagsZeros.Length];
        var extraFlags2 = new byte[flagsZeros.Length];
        var extraBytes0 = new byte[ChunkSize * ChunkSize];
        var cliffStraighten = new byte[(ChunkSize * ChunkSize * 3) / 8];

        var extraFlag0 = new ulong[ChunkSize * ChunkSize / 64];
        var extraFlag1 = new ulong[ChunkSize * ChunkSize / 64];
        var extraFlag2 = new ulong[ChunkSize * ChunkSize / 64];

        int mapWidth = WidthChunks * ChunkSize;
        int mapHeight = HeightChunks * ChunkSize;

        for (int chunkY = 0; chunkY < HeightChunks; chunkY++)
        {
            for (int chunkX = 0; chunkX < WidthChunks; chunkX++)
            {
                Array.Clear(packed, 0, packed.Length);
                Array.Clear(layer2, 0, layer2.Length);
                Array.Clear(extraFlag0, 0, extraFlag0.Length);
                Array.Clear(extraFlag1, 0, extraFlag1.Length);
                Array.Clear(extraFlag2, 0, extraFlag2.Length);
                Array.Clear(extraBytes0, 0, extraBytes0.Length);
                Array.Clear(cliffStraighten, 0, cliffStraighten.Length);

                for (int localY = 0; localY < ChunkSize; localY++)
                {
                    for (int localX = 0; localX < ChunkSize; localX++)
                    {
                        int globalX = chunkX * ChunkSize + localX;
                        int globalY = chunkY * ChunkSize + localY;
                        int cell = (localY * ChunkSize) + localX;

                        byte height = HeightAt(mapWidth, mapHeight, globalX, globalY);
                        byte water = WaterAt(mapWidth, mapHeight, globalX, globalY, height);
                        byte biome = BiomeAt(height, water);

                        packed[cell] = (byte)((biome << 4) | (height & 0x0F));
                        layer2[cell] = (byte)(water & 0x0F);
                        factions[cell] = 0;
                        extraBytes0[cell] = 0;

                        int ulongIndex = cell >> 6;
                        int bitIndex = cell & 0x3F;
                        ulong bit = 1UL << bitIndex;
                        if (height >= 8)
                        {
                            extraFlag0[ulongIndex] |= bit;
                        }

                        if (water > height)
                        {
                            extraFlag1[ulongIndex] |= bit;
                        }

                        bool oddRow = (globalY & 1) == 1;
                        SetStraightenBitIfNeeded(
                            cliffStraighten, mapWidth, mapHeight, cell, globalX, globalY,
                            globalX + 1, globalY, edgeIndex: 0);
                        SetStraightenBitIfNeeded(
                            cliffStraighten, mapWidth, mapHeight, cell, globalX, globalY,
                            oddRow ? globalX + 1 : globalX, globalY + 1, edgeIndex: 1);
                        SetStraightenBitIfNeeded(
                            cliffStraighten, mapWidth, mapHeight, cell, globalX, globalY,
                            oddRow ? globalX : globalX - 1, globalY + 1, edgeIndex: 2);
                    }
                }

                Buffer.BlockCopy(extraFlag0, 0, extraFlags0, 0, extraFlags0.Length);
                Buffer.BlockCopy(extraFlag1, 0, extraFlags1, 0, extraFlags1.Length);
                Buffer.BlockCopy(extraFlag2, 0, extraFlags2, 0, extraFlags2.Length);

                writer.Write(packed);
                writer.Write(layer2);
                writer.Write(flagsZeros);
                writer.Write(rampsBytes);
                writer.Write(factions);
                writer.Write(extraFlags0);
                writer.Write(extraFlags1);
                writer.Write(extraFlags2);
                writer.Write(extraBytes0);
                writer.Write(cliffStraighten);
            }
        }

        writer.Flush();
        context.Log($"[RaylibVisualAtmosphereShowcaseMod] Generated island terrain {fullPath} ({new FileInfo(fullPath).Length} bytes)");
    }

    private static byte HeightAt(int mapW, int mapH, int x, int y)
    {
        float cx = mapW * 0.5f;
        float cy = mapH * 0.5f;
        float dx = (x - cx) / cx;
        float dy = (y - cy) / cy;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        // Soft radial island with irregular coastline.
        float coast = 0.42f + 0.06f * MathF.Sin(MathF.Atan2(dy, dx) * 5f);
        if (dist > coast + 0.12f)
        {
            return 1;
        }

        float n =
            0.55f * ValueNoise(x * 0.07f, y * 0.07f) +
            0.30f * ValueNoise(x * 0.15f + 20f, y * 0.15f - 11f) +
            0.15f * ValueNoise(x * 0.35f - 7f, y * 0.35f + 3f);

        float inland = Math.Clamp(1f - (dist / coast), 0f, 1f);
        float beach = Math.Clamp((coast + 0.05f - dist) / 0.08f, 0f, 1f);
        float peak = inland * inland * (4.5f + n * 7.5f);
        float height = 2.2f + ((peak - 2.2f) * beach);
        return (byte)Math.Clamp((int)MathF.Round(height), 1, 12);
    }

    private static byte WaterAt(int mapW, int mapH, int x, int y, byte height)
    {
        float cx = mapW * 0.5f;
        float cy = mapH * 0.5f;
        float dx = (x - cx) / cx;
        float dy = (y - cy) / cy;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float coast = 0.42f + 0.06f * MathF.Sin(MathF.Atan2(dy, dx) * 5f);

        if (dist > coast + 0.02f)
        {
            return 6;
        }

        if (dist > coast - 0.04f && height <= 3)
        {
            return 4;
        }

        return 0;
    }

    private static byte BiomeAt(byte height, byte water)
    {
        if (water > height)
        {
            return 5;
        }

        if (height <= 3)
        {
            return 1; // sand
        }

        if (height <= 6)
        {
            return 0; // grass
        }

        if (height <= 9)
        {
            return 3; // dirt/rock mix
        }

        return 2; // rock
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
        int n = x * 374761393 + y * 668265263;
        n = (n ^ (n >> 13)) * 1274126177;
        n ^= n >> 16;
        return (n & 0x7FFFFFFF) / (float)int.MaxValue;
    }

    private static void SetStraightenBitIfNeeded(
        byte[] cliffStraighten,
        int mapW,
        int mapH,
        int cell,
        int ax,
        int ay,
        int bx,
        int by,
        int edgeIndex)
    {
        if ((uint)bx >= (uint)mapW || (uint)by >= (uint)mapH)
        {
            return;
        }

        byte ha = HeightAt(mapW, mapH, ax, ay);
        byte hb = HeightAt(mapW, mapH, bx, by);
        if (ha == hb || Math.Abs(ha - hb) < 3)
        {
            return;
        }

        int bit = cell * 3 + edgeIndex;
        cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
    }
}
