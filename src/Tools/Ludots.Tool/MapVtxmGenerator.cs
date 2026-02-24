using System;
using System.IO;
using System.Text;

namespace Ludots.Tool
{
    public static class MapVtxmGenerator
    {
        public enum Preset
        {
            Bench,
            Flat,
            Stripes,
            Cliffs,
            Lake
        }

        public static void GenerateV2(string outFile, int widthChunks, int heightChunks, int chunkSize, Preset preset, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(outFile)) throw new ArgumentException("Output file is required.", nameof(outFile));
            if (widthChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthChunks));
            if (heightChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightChunks));
            if (chunkSize <= 0 || (chunkSize & (chunkSize - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunkSize must be power-of-two.");

            outFile = Path.GetFullPath(outFile);
            var outDir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            if (File.Exists(outFile) && !overwrite)
            {
                throw new IOException($"File already exists: {outFile} (pass --overwrite to replace)");
            }

            using var fs = File.Create(outFile);
            using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

            bw.Write(Encoding.ASCII.GetBytes("VTXM"));
            bw.Write(2);
            bw.Write(widthChunks);
            bw.Write(heightChunks);
            bw.Write(chunkSize);
            bw.Write(0);

            var packed = new byte[chunkSize * chunkSize];
            var layer2 = new byte[chunkSize * chunkSize];
            var flagsZeros = new byte[(chunkSize * chunkSize / 64) * sizeof(ulong)];
            var rampsBytes = new byte[flagsZeros.Length];
            var factions = new byte[chunkSize * chunkSize];
            var extraFlags0 = new byte[flagsZeros.Length];
            var extraFlags1 = new byte[flagsZeros.Length];
            var extraFlags2 = new byte[flagsZeros.Length];
            var extraBytes0 = new byte[chunkSize * chunkSize];
            var cliffStraighten = new byte[(chunkSize * chunkSize * 3) / 8];

            var rampsU = new ulong[chunkSize * chunkSize / 64];
            var ef0U = new ulong[chunkSize * chunkSize / 64];
            var ef1U = new ulong[chunkSize * chunkSize / 64];
            var ef2U = new ulong[chunkSize * chunkSize / 64];

            int mapWidth = widthChunks * chunkSize;
            int mapHeight = heightChunks * chunkSize;

            for (int cy = 0; cy < heightChunks; cy++)
            {
                for (int cx = 0; cx < widthChunks; cx++)
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

                    for (int ly = 0; ly < chunkSize; ly++)
                    {
                        for (int lx = 0; lx < chunkSize; lx++)
                        {
                            int globalC = cx * chunkSize + lx;
                            int globalR = cy * chunkSize + ly;
                            int cell = (ly * chunkSize) + lx;

                            byte h = HeightAt(preset, mapWidth, mapHeight, globalC, globalR);
                            byte w = WaterAt(preset, mapWidth, mapHeight, globalC, globalR, h);
                            byte biome = BiomeAt(preset, globalC, globalR, h, w);

                            packed[cell] = (byte)((biome << 4) | (h & 0x0F));
                            layer2[cell] = (byte)(w & 0x0F);

                            int ulongIndex = cell >> 6;
                            int bitIndex = cell & 0x3F;
                            ulong mask = 1UL << bitIndex;

                            bool flag0 = h >= 10;
                            bool flag1 = h <= 2;
                            bool flag2 = w > h;
                            if (flag0) ef0U[ulongIndex] |= mask;
                            if (flag1) ef1U[ulongIndex] |= mask;
                            if (flag2) ef2U[ulongIndex] |= mask;

                            extraBytes0[cell] = (byte)(1 + ((globalC / 64) + (globalR / 64) * 17));

                            bool isOdd = (globalR & 1) == 1;

                            int n0c = globalC + 1;
                            int n0r = globalR;
                            if (ShouldStraightenEdge(preset, mapWidth, mapHeight, globalC, globalR, n0c, n0r))
                            {
                                int bit = cell * 3 + 0;
                                cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
                            }

                            int n1c = isOdd ? globalC + 1 : globalC;
                            int n1r = globalR + 1;
                            if (ShouldStraightenEdge(preset, mapWidth, mapHeight, globalC, globalR, n1c, n1r))
                            {
                                int bit = cell * 3 + 1;
                                cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
                            }

                            int n2c = isOdd ? globalC : globalC - 1;
                            int n2r = globalR + 1;
                            if (ShouldStraightenEdge(preset, mapWidth, mapHeight, globalC, globalR, n2c, n2r))
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
        }

        private static byte HeightAt(Preset preset, int mapW, int mapH, int c, int r)
        {
            return preset switch
            {
                Preset.Flat => 6,
                Preset.Stripes => (byte)(((c / 8) % 12) & 0x0F),
                Preset.Cliffs => (byte)((((c / 16) & 1) == 0 ? 2 : 12) & 0x0F),
                Preset.Lake => (byte)((6 + ((r / 32) % 4)) & 0x0F),
                _ => (byte)((((c / 16) % 12) + (((r / 128) & 1) * 3)) & 0x0F)
            };
        }

        private static byte WaterAt(Preset preset, int mapW, int mapH, int c, int r, byte h)
        {
            if (preset == Preset.Lake || preset == Preset.Bench)
            {
                int centerC = mapW / 2;
                int centerR = mapH / 2;
                int d = c - centerC;
                int e = r - centerR;
                int dist2 = d * d + e * e;
                if (dist2 < 220 * 220) return 8;
                if (dist2 < 300 * 300) return 4;
            }
            return 0;
        }

        private static byte BiomeAt(Preset preset, int c, int r, byte h, byte w)
        {
            if (w > h) return 5;
            if (preset == Preset.Cliffs) return 2;
            if (h <= 2) return 1;
            if (h <= 5) return 0;
            if (h <= 8) return 3;
            return 2;
        }

        private static byte HeightAtBounded(Preset preset, int w, int h, int c, int r)
        {
            if ((uint)c >= (uint)w || (uint)r >= (uint)h) return 0;
            return HeightAt(preset, w, h, c, r);
        }

        private static bool ShouldStraightenEdge(Preset preset, int w, int h, int cA, int rA, int cB, int rB)
        {
            if ((uint)cA >= (uint)w || (uint)rA >= (uint)h) return false;
            if ((uint)cB >= (uint)w || (uint)rB >= (uint)h) return false;

            byte hA = HeightAt(preset, w, h, cA, rA);
            byte hB = HeightAt(preset, w, h, cB, rB);
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

            byte hLeft = HeightAtBounded(preset, w, h, highC - 1, highR);
            byte hRight = HeightAtBounded(preset, w, h, highC + 1, highR);
            byte lLeft = HeightAtBounded(preset, w, h, lowC - 1, lowR);
            byte lRight = HeightAtBounded(preset, w, h, lowC + 1, lowR);

            bool isVerticalCliffCandidate = (hLeft != highH || hRight != highH) || (lLeft != lowH || lRight != lowH);
            if (!isVerticalCliffCandidate) return false;

            byte hUp = HeightAtBounded(preset, w, h, highC, highR - 1);
            byte hDown = HeightAtBounded(preset, w, h, highC, highR + 1);
            bool highIsContinuous = hUp == highH && hDown == highH;

            byte lUp = HeightAtBounded(preset, w, h, lowC, lowR - 1);
            byte lDown = HeightAtBounded(preset, w, h, lowC, lowR + 1);
            bool lowIsContinuous = lUp == lowH && lDown == lowH;

            return highIsContinuous && lowIsContinuous;
        }
    }
}

