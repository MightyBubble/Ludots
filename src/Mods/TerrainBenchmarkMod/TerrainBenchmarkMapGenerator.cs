using System;
using System.IO;
using System.Text;
using Ludots.Core.Modding;

namespace TerrainBenchmarkMod
{
    public static class TerrainBenchmarkMapGenerator
    {
        private const string FileName = "terrain_bench.vtxm";
        private const int Version = 2;
        private const int ChunkSize = 64;
        private const int WidthChunks = 64;
        private const int HeightChunks = 64;

        public static void EnsureGenerated(IModContext context)
        {
            string uri = $"{context.ModId}:assets/Data/Maps/{FileName}";
            if (!context.VFS.TryResolveFullPath(uri, out var fullPath))
            {
                throw new InvalidOperationException($"Failed to resolve path: {uri}");
            }

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(fullPath))
            {
                var info = new FileInfo(fullPath);
                if (info.Length > 0) return;
            }

            using var fs = File.Create(fullPath);
            using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

            bw.Write(Encoding.ASCII.GetBytes("VTXM"));
            bw.Write(Version);
            bw.Write(WidthChunks);
            bw.Write(HeightChunks);
            bw.Write(ChunkSize);
            bw.Write(0);

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

            var rampsU = new ulong[ChunkSize * ChunkSize / 64];
            var ef0U = new ulong[ChunkSize * ChunkSize / 64];
            var ef1U = new ulong[ChunkSize * ChunkSize / 64];
            var ef2U = new ulong[ChunkSize * ChunkSize / 64];

            int mapWidth = WidthChunks * ChunkSize;
            int mapHeight = HeightChunks * ChunkSize;

            for (int cy = 0; cy < HeightChunks; cy++)
            {
                for (int cx = 0; cx < WidthChunks; cx++)
                {
                    Array.Clear(packed, 0, packed.Length);
                    Array.Clear(layer2, 0, layer2.Length);
                    Array.Clear(rampsU, 0, rampsU.Length);
                    Array.Clear(factions, 0, factions.Length);
                    Array.Clear(ef0U, 0, ef0U.Length);
                    Array.Clear(ef1U, 0, ef1U.Length);
                    Array.Clear(ef2U, 0, ef2U.Length);
                    Array.Clear(extraBytes0, 0, extraBytes0.Length);
                    Array.Clear(cliffStraighten, 0, cliffStraighten.Length);

                    for (int ly = 0; ly < ChunkSize; ly++)
                    {
                        for (int lx = 0; lx < ChunkSize; lx++)
                        {
                            int globalC = cx * ChunkSize + lx;
                            int globalR = cy * ChunkSize + ly;
                            int cell = (ly * ChunkSize) + lx;

                            byte h = HeightAt(globalC, globalR);
                            byte w = WaterAt(globalC, globalR);
                            byte biome = BiomeAt(globalC, globalR, h, w);
                            byte veg = 0;

                            packed[cell] = (byte)((biome << 4) | (h & 0x0F));
                            layer2[cell] = (byte)((veg << 4) | (w & 0x0F));

                            int ulongIndex = cell >> 6;
                            int bitIndex = cell & 0x3F;
                            ulong mask = 1UL << bitIndex;

                            bool flag0 = h >= 10;
                            bool flag1 = h <= 2;
                            bool flag2 = w > h;
                            if (flag0) ef0U[ulongIndex] |= mask;
                            if (flag1) ef1U[ulongIndex] |= mask;
                            if (flag2) ef2U[ulongIndex] |= mask;

                            extraBytes0[cell] = TerritoryAt(globalC, globalR);

                            bool isOdd = (globalR & 1) == 1;

                            int n0c = globalC + 1;
                            int n0r = globalR;
                            if (ShouldStraightenEdge(mapWidth, mapHeight, globalC, globalR, n0c, n0r))
                            {
                                int bit = cell * 3 + 0;
                                cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
                            }

                            int n1c = isOdd ? globalC + 1 : globalC;
                            int n1r = globalR + 1;
                            if (ShouldStraightenEdge(mapWidth, mapHeight, globalC, globalR, n1c, n1r))
                            {
                                int bit = cell * 3 + 1;
                                cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
                            }

                            int n2c = isOdd ? globalC : globalC - 1;
                            int n2r = globalR + 1;
                            if (ShouldStraightenEdge(mapWidth, mapHeight, globalC, globalR, n2c, n2r))
                            {
                                int bit = cell * 3 + 2;
                                cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
                            }
                        }
                    }

                    Buffer.BlockCopy(rampsU, 0, rampsBytes, 0, rampsBytes.Length);
                    Buffer.BlockCopy(ef0U, 0, extraFlags0, 0, extraFlags0.Length);
                    Buffer.BlockCopy(ef1U, 0, extraFlags1, 0, extraFlags1.Length);
                    Buffer.BlockCopy(ef2U, 0, extraFlags2, 0, extraFlags2.Length);

                    bw.Write(packed);
                    bw.Write(layer2);
                    bw.Write(flagsZeros);
                    bw.Write(rampsBytes);
                    bw.Write(factions);
                    bw.Write(extraFlags0);
                    bw.Write(extraFlags1);
                    bw.Write(extraFlags2);
                    bw.Write(extraBytes0);
                    bw.Write(cliffStraighten);
                }
            }

            bw.Flush();
            context.Log($"[TerrainBenchmarkMod] Generated {fullPath} ({new FileInfo(fullPath).Length} bytes)");
        }

        private static byte HeightAt(int c, int r)
        {
            int band = (c / 16) % 12;
            int ridge = (r / 128) & 1;
            int h = band + ridge * 3;
            return (byte)(h & 0x0F);
        }

        private static byte WaterAt(int c, int r)
        {
            int d = (c - 2048);
            int e = (r - 2048);
            int dist2 = d * d + e * e;
            if (dist2 < 300 * 300) return 6;
            if (dist2 < 380 * 380) return 3;
            return 0;
        }

        private static byte BiomeAt(int c, int r, byte h, byte w)
        {
            if (w > h) return 5;
            if (h <= 2) return 1;
            if (h <= 5) return 0;
            if (h <= 8) return 3;
            return 2;
        }

        private static byte TerritoryAt(int c, int r)
        {
            int tx = c / 256;
            int ty = r / 256;
            int id = 1 + (tx + ty * 17);
            return (byte)(id & 0xFF);
        }

        private static byte HeightAtBounded(int w, int h, int c, int r)
        {
            if ((uint)c >= (uint)w || (uint)r >= (uint)h) return 0;
            return HeightAt(c, r);
        }

        private static bool ShouldStraightenEdge(int w, int h, int cA, int rA, int cB, int rB)
        {
            if ((uint)cA >= (uint)w || (uint)rA >= (uint)h) return false;
            if ((uint)cB >= (uint)w || (uint)rB >= (uint)h) return false;

            byte hA = HeightAt(cA, rA);
            byte hB = HeightAt(cB, rB);
            if (hA == hB) return false;

            int highC, highR;
            byte highH;
            int lowC, lowR;
            byte lowH;

            if (hA > hB)
            {
                highC = cA; highR = rA; highH = hA;
                lowC = cB; lowR = rB; lowH = hB;
            }
            else
            {
                highC = cB; highR = rB; highH = hB;
                lowC = cA; lowR = rA; lowH = hA;
            }

            byte hLeft = HeightAtBounded(w, h, highC - 1, highR);
            byte hRight = HeightAtBounded(w, h, highC + 1, highR);
            byte lLeft = HeightAtBounded(w, h, lowC - 1, lowR);
            byte lRight = HeightAtBounded(w, h, lowC + 1, lowR);

            bool isVerticalCliffCandidate = (hLeft != highH || hRight != highH) || (lLeft != lowH || lRight != lowH);
            if (!isVerticalCliffCandidate) return false;

            byte hUp = HeightAtBounded(w, h, highC, highR - 1);
            byte hDown = HeightAtBounded(w, h, highC, highR + 1);
            bool highIsContinuous = hUp == highH && hDown == highH;

            byte lUp = HeightAtBounded(w, h, lowC, lowR - 1);
            byte lDown = HeightAtBounded(w, h, lowC, lowR + 1);
            bool lowIsContinuous = lUp == lowH && lDown == lowH;

            return highIsContinuous && lowIsContinuous;
        }
    }
}

