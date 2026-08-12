using System;
using System.IO;
using System.Text;
using Ludots.Core.Modding;

namespace RaylibVisualAtmosphereShowcaseMod.Runtime;

internal static class IslandTerrainGenerator
{
    private const string FileName = "tropical_island.vtxm";
    private const int Version = 2;
    private const int ChunkSize = 64;
    private const int WidthChunks = 3;
    private const int HeightChunks = 3;

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

        if (File.Exists(fullPath) && new FileInfo(fullPath).Length > 0)
        {
            return;
        }

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
        float dx = x - cx;
        float dy = y - cy;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        float islandRadius = 48f;
        float beachRadius = 56f;
        if (dist > beachRadius)
        {
            return 1;
        }

        float t = 1f - (dist / islandRadius);
        if (t < 0f)
        {
            return 3;
        }

        float ridge = 0.55f + 0.45f * MathF.Sin(dx * 0.18f) * MathF.Cos(dy * 0.16f);
        float peak = 3f + t * t * 9f * ridge;
        int height = (int)MathF.Round(peak);
        return (byte)Math.Clamp(height, 3, 12);
    }

    private static byte WaterAt(int mapW, int mapH, int x, int y, byte height)
    {
        float cx = mapW * 0.5f;
        float cy = mapH * 0.5f;
        float dx = x - cx;
        float dy = y - cy;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > 54f)
        {
            return 6;
        }

        if (dist > 48f && height <= 3)
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
            return 1;
        }

        if (height <= 6)
        {
            return 0;
        }

        if (height <= 9)
        {
            return 3;
        }

        return 2;
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
