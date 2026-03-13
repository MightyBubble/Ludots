using System;
using System.IO;
using System.Text;
using Ludots.Core.Modding;

namespace CameraAcceptanceMod.Runtime
{
    internal static class CameraAcceptanceHotpathTerrainGenerator
    {
        private const string FileName = "camera_acceptance_hotpath.vtxm";
        private const int Version = 2;
        private const int ChunkSize = 64;
        private const int WidthChunks = 12;
        private const int HeightChunks = 10;

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

                            byte height = HeightAt(globalX, globalY);
                            byte water = WaterAt(globalX, globalY);
                            byte biome = BiomeAt(height, water);

                            packed[cell] = (byte)((biome << 4) | (height & 0x0F));
                            layer2[cell] = water;
                            factions[cell] = 0;
                            extraBytes0[cell] = TerritoryAt(globalX, globalY);

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

                            if (((globalX / 48) + (globalY / 48)) % 2 == 0)
                            {
                                extraFlag2[ulongIndex] |= bit;
                            }

                            bool oddRow = (globalY & 1) == 1;
                            SetStraightenBitIfNeeded(cliffStraighten, mapWidth, mapHeight, cell, globalX, globalY, globalX + 1, globalY, edgeIndex: 0);
                            SetStraightenBitIfNeeded(cliffStraighten, mapWidth, mapHeight, cell, globalX, globalY, oddRow ? globalX + 1 : globalX, globalY + 1, edgeIndex: 1);
                            SetStraightenBitIfNeeded(cliffStraighten, mapWidth, mapHeight, cell, globalX, globalY, oddRow ? globalX : globalX - 1, globalY + 1, edgeIndex: 2);
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
            context.Log($"[CameraAcceptanceMod] Generated hotpath terrain {fullPath} ({new FileInfo(fullPath).Length} bytes)");
        }

        private static byte HeightAt(int x, int y)
        {
            int ridge = Math.Abs((x % 192) - 96) < 18 ? 3 : 0;
            int band = ((x / 96) + (y / 128)) % 4;
            int swell = ((x / 28) + (y / 24)) % 3;
            int height = 2 + band + swell + ridge;
            return (byte)Math.Clamp(height, 0, 15);
        }

        private static byte WaterAt(int x, int y)
        {
            int centerX = (WidthChunks * ChunkSize) / 2;
            int centerY = (HeightChunks * ChunkSize) / 2;
            int dx = x - centerX;
            int dy = y - centerY;
            int distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < 55 * 55)
            {
                return 6;
            }

            if (distanceSquared < 76 * 76)
            {
                return 3;
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

        private static byte TerritoryAt(int x, int y)
        {
            int territory = 1 + ((x / 96) + ((y / 96) * 11));
            return (byte)(territory & 0xFF);
        }

        private static void SetStraightenBitIfNeeded(byte[] cliffStraighten, int mapWidth, int mapHeight, int cell, int ax, int ay, int bx, int by, int edgeIndex)
        {
            if (!ShouldStraightenEdge(mapWidth, mapHeight, ax, ay, bx, by))
            {
                return;
            }

            int bit = (cell * 3) + edgeIndex;
            cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
        }

        private static bool ShouldStraightenEdge(int width, int height, int ax, int ay, int bx, int by)
        {
            if ((uint)ax >= (uint)width || (uint)ay >= (uint)height)
            {
                return false;
            }

            if ((uint)bx >= (uint)width || (uint)by >= (uint)height)
            {
                return false;
            }

            byte aHeight = HeightAt(ax, ay);
            byte bHeight = HeightAt(bx, by);
            if (aHeight == bHeight)
            {
                return false;
            }

            int highX;
            int highY;
            byte highHeight;
            int lowX;
            int lowY;
            byte lowHeight;
            if (aHeight > bHeight)
            {
                highX = ax;
                highY = ay;
                highHeight = aHeight;
                lowX = bx;
                lowY = by;
                lowHeight = bHeight;
            }
            else
            {
                highX = bx;
                highY = by;
                highHeight = bHeight;
                lowX = ax;
                lowY = ay;
                lowHeight = aHeight;
            }

            bool highContinuous = HeightAtBounded(width, height, highX, highY - 1) == highHeight &&
                                  HeightAtBounded(width, height, highX, highY + 1) == highHeight;
            bool lowContinuous = HeightAtBounded(width, height, lowX, lowY - 1) == lowHeight &&
                                 HeightAtBounded(width, height, lowX, lowY + 1) == lowHeight;

            return highContinuous && lowContinuous;
        }

        private static byte HeightAtBounded(int width, int height, int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                return 0;
            }

            return HeightAt(x, y);
        }
    }
}
