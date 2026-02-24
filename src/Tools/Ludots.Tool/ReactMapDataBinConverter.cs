using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ludots.Tool
{
    public sealed class ReactMapConversionSummary
    {
        public string InputPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public int WidthChunks { get; set; }
        public int HeightChunks { get; set; }
        public int ChunkSize { get; set; }
        public int CellsPerChunk { get; set; }
        public long InputBytes { get; set; }
        public long OutputBytes { get; set; }
        public byte StrideOrVersion { get; set; }
        public byte MinHeight { get; set; }
        public byte MaxHeight { get; set; }
        public byte MinWater { get; set; }
        public byte MaxWater { get; set; }
        public Dictionary<int, int> BiomeCounts { get; set; } = new Dictionary<int, int>();
        public Dictionary<int, int> VegetationCounts { get; set; } = new Dictionary<int, int>();
    }

    public static class ReactMapDataBinConverter
    {
        private const string Magic = "VTXM";
        private const int Version = 2;
        private const int ChunkSize = 64;
        private const int CellsPerChunk = ChunkSize * ChunkSize;
        private const int ReactCellStride = 4;
        private const int ReactChunkBytes = CellsPerChunk * ReactCellStride;

        private const int PackedBytesPerChunk = CellsPerChunk;
        private const int Layer2BytesPerChunk = CellsPerChunk;
        private const int FlagsUlongsPerChunk = CellsPerChunk / 64;
        private const int FlagsBytesPerChunk = FlagsUlongsPerChunk * 8;
        private const int RampBytesPerChunk = FlagsBytesPerChunk;
        private const int FactionsBytesPerChunk = CellsPerChunk;
        private const int TerritoryBytesPerChunk = CellsPerChunk;
        private const int CliffStraightenBytesPerChunk = (CellsPerChunk * 3) / 8;

        public static ReactMapConversionSummary ConvertToVertexMapBinary(string inputPath, string outputPath)
        {
            using var input = File.OpenRead(inputPath);
            using var output = File.Create(outputPath);
            return ConvertToVertexMapBinary(inputPath, outputPath, input, output);
        }

        public static ReactMapConversionSummary ConvertToVertexMapBinary(string inputPath, Stream output)
        {
            using var input = File.OpenRead(inputPath);
            return ConvertToVertexMapBinary(inputPath, outputPath: string.Empty, input, output);
        }

        private static ReactMapConversionSummary ConvertToVertexMapBinary(string inputPath, string outputPath, Stream input, Stream output)
        {
            using var br = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);

            int widthChunks = br.ReadInt32();
            int heightChunks = br.ReadInt32();
            byte strideOrVersion = br.ReadByte();

            if (widthChunks <= 0 || heightChunks <= 0)
            {
                throw new InvalidDataException($"Invalid chunk dimensions: {widthChunks}x{heightChunks}");
            }

            long expectedBody = (long)widthChunks * heightChunks * ReactChunkBytes;
            long remaining = input.Length - input.Position;
            if (remaining < expectedBody)
            {
                throw new InvalidDataException($"Input too small. Expected body bytes={expectedBody}, actual remaining={remaining}.");
            }

            var summary = new ReactMapConversionSummary
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                WidthChunks = widthChunks,
                HeightChunks = heightChunks,
                ChunkSize = ChunkSize,
                CellsPerChunk = CellsPerChunk,
                InputBytes = input.Length,
                StrideOrVersion = strideOrVersion,
                MinHeight = 255,
                MaxHeight = 0,
                MinWater = 255,
                MaxWater = 0
            };

            byte[] reactChunk = new byte[ReactChunkBytes];
            int mapWidth = widthChunks * ChunkSize;
            int mapHeight = heightChunks * ChunkSize;
            int mapCells = mapWidth * mapHeight;

            var heights = new byte[mapCells];
            var waters = new byte[mapCells];
            var biomes = new byte[mapCells];
            var vegs = new byte[mapCells];
            var ramp = new bool[mapCells];
            var snow = new bool[mapCells];
            var mud = new bool[mapCells];
            var ice = new bool[mapCells];
            var territory = new byte[mapCells];

            for (int cy = 0; cy < heightChunks; cy++)
            {
                for (int cx = 0; cx < widthChunks; cx++)
                {
                    int read = input.Read(reactChunk, 0, reactChunk.Length);
                    if (read != reactChunk.Length)
                    {
                        throw new EndOfStreamException($"Unexpected EOF when reading chunk ({cx},{cy}).");
                    }

                    for (int ly = 0; ly < ChunkSize; ly++)
                    {
                        for (int lx = 0; lx < ChunkSize; lx++)
                        {
                            int cell = (ly * ChunkSize) + lx;
                            int i = cell * ReactCellStride;

                            byte b0 = reactChunk[i];
                            byte b1 = reactChunk[i + 1];
                            byte b2 = reactChunk[i + 2];
                            byte b3 = reactChunk[i + 3];

                            byte height = (byte)((b0 >> 4) & 0x0F);
                            byte water = (byte)(b0 & 0x0F);
                            byte biome = (byte)((b1 >> 4) & 0x0F);
                            byte veg = (byte)(b1 & 0x0F);

                            bool isRamp = (b2 & 0b1000_0000) != 0;
                            bool isSnow = (b2 & 0b0100_0000) != 0;
                            bool isMud = (b2 & 0b0010_0000) != 0;
                            bool isIce = (b2 & 0b0001_0000) != 0;

                            int globalC = cx * ChunkSize + lx;
                            int globalR = cy * ChunkSize + ly;
                            int globalIndex = globalR * mapWidth + globalC;

                            heights[globalIndex] = height;
                            waters[globalIndex] = water;
                            biomes[globalIndex] = biome;
                            vegs[globalIndex] = veg;
                            ramp[globalIndex] = isRamp;
                            snow[globalIndex] = isSnow;
                            mud[globalIndex] = isMud;
                            ice[globalIndex] = isIce;
                            territory[globalIndex] = b3;

                            if (height < summary.MinHeight) summary.MinHeight = height;
                            if (height > summary.MaxHeight) summary.MaxHeight = height;
                            if (water < summary.MinWater) summary.MinWater = water;
                            if (water > summary.MaxWater) summary.MaxWater = water;

                            if (!summary.BiomeCounts.TryGetValue(biome, out int bc)) bc = 0;
                            summary.BiomeCounts[biome] = bc + 1;

                            if (!summary.VegetationCounts.TryGetValue(veg, out int vc)) vc = 0;
                            summary.VegetationCounts[veg] = vc + 1;
                        }
                    }
                }
            }

            static byte HeightAt(byte[] data, int w, int h, int c, int r)
            {
                if ((uint)c >= (uint)w || (uint)r >= (uint)h) return 0;
                return data[r * w + c];
            }

            static bool BoolAt(bool[] data, int w, int h, int c, int r)
            {
                if ((uint)c >= (uint)w || (uint)r >= (uint)h) return false;
                return data[r * w + c];
            }

            /// <summary>
            /// Determines whether a cliff edge between two cells should be straightened.
            ///
            /// A cliff edge is straightened when both sides of the cliff are part of a
            /// continuous plateau that extends in the direction PERPENDICULAR to the edge.
            /// This ensures that only genuine straight cliff faces are marked, avoiding
            /// false positives at irregular cliff corners.
            ///
            /// The check is direction-aware: for each of the 3 edge directions per cell,
            /// the perpendicular continuity is checked along the correct axis.
            ///
            /// edgeDir:
            ///   0 = horizontal right (c,r)↔(c+1,r)     → check vertical (up/down) continuity
            ///   1 = diagonal          (c,r)↔(n1c, r+1)  → check perpendicular continuity
            ///   2 = diagonal          (c,r)↔(n2c, r+1)  → check perpendicular continuity
            /// </summary>
            static bool ShouldStraightenEdge(byte[] heightData, bool[] rampData, int w, int h,
                int cA, int rA, int cB, int rB, int edgeDir)
            {
                if ((uint)cA >= (uint)w || (uint)rA >= (uint)h) return false;
                if ((uint)cB >= (uint)w || (uint)rB >= (uint)h) return false;

                int idxA = rA * w + cA;
                int idxB = rB * w + cB;
                byte hA = heightData[idxA];
                byte hB = heightData[idxB];

                // No cliff if same height or either side is a ramp
                if (hA == hB) return false;
                if (rampData[idxA] || rampData[idxB]) return false;

                // Identify high and low sides
                int highC, highR, lowC, lowR;
                byte highH, lowH;
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

                // Check continuity PERPENDICULAR to the cliff edge direction.
                // A cliff should only be straightened if both the high and low plateaus
                // extend continuously along the cliff face.
                bool highContinuous, lowContinuous;

                if (edgeDir == 0)
                {
                    // Edge is horizontal (east-west): cliff face runs north-south.
                    // Check vertical continuity (up and down of each cell).
                    highContinuous =
                        HeightAt(heightData, w, h, highC, highR - 1) == highH &&
                        HeightAt(heightData, w, h, highC, highR + 1) == highH;
                    lowContinuous =
                        HeightAt(heightData, w, h, lowC, lowR - 1) == lowH &&
                        HeightAt(heightData, w, h, lowC, lowR + 1) == lowH;
                }
                else
                {
                    // Edges 1 and 2 are diagonal (connecting to row below).
                    // The cliff face runs roughly horizontally (east-west).
                    // Check horizontal continuity (left and right of each cell).
                    highContinuous =
                        HeightAt(heightData, w, h, highC - 1, highR) == highH &&
                        HeightAt(heightData, w, h, highC + 1, highR) == highH;
                    lowContinuous =
                        HeightAt(heightData, w, h, lowC - 1, lowR) == lowH &&
                        HeightAt(heightData, w, h, lowC + 1, lowR) == lowH;
                }

                return highContinuous && lowContinuous;
            }

            using var bw = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

            bw.Write(Encoding.ASCII.GetBytes(Magic));
            bw.Write(Version);
            bw.Write(widthChunks);
            bw.Write(heightChunks);
            bw.Write(ChunkSize);
            bw.Write(0);

            var packed = new byte[PackedBytesPerChunk];
            var layer2 = new byte[Layer2BytesPerChunk];
            var flagsZeros = new byte[FlagsBytesPerChunk];
            var factionsZeros = new byte[FactionsBytesPerChunk];

            var rampU = new ulong[FlagsUlongsPerChunk];
            var snowU = new ulong[FlagsUlongsPerChunk];
            var mudU = new ulong[FlagsUlongsPerChunk];
            var iceU = new ulong[FlagsUlongsPerChunk];

            var rampBytes = new byte[RampBytesPerChunk];
            var snowBytes = new byte[FlagsBytesPerChunk];
            var mudBytes = new byte[FlagsBytesPerChunk];
            var iceBytes = new byte[FlagsBytesPerChunk];

            var chunkTerritory = new byte[TerritoryBytesPerChunk];
            var cliffStraighten = new byte[CliffStraightenBytesPerChunk];

            for (int cy = 0; cy < heightChunks; cy++)
            {
                for (int cx = 0; cx < widthChunks; cx++)
                {
                    Array.Clear(packed, 0, packed.Length);
                    Array.Clear(layer2, 0, layer2.Length);
                    Array.Clear(rampU, 0, rampU.Length);
                    Array.Clear(snowU, 0, snowU.Length);
                    Array.Clear(mudU, 0, mudU.Length);
                    Array.Clear(iceU, 0, iceU.Length);
                    Array.Clear(chunkTerritory, 0, chunkTerritory.Length);
                    Array.Clear(cliffStraighten, 0, cliffStraighten.Length);

                    for (int ly = 0; ly < ChunkSize; ly++)
                    {
                        for (int lx = 0; lx < ChunkSize; lx++)
                        {
                            int globalC = cx * ChunkSize + lx;
                            int globalR = cy * ChunkSize + ly;
                            int globalIndex = globalR * mapWidth + globalC;

                            int cell = (ly * ChunkSize) + lx;
                            packed[cell] = (byte)((biomes[globalIndex] << 4) | (heights[globalIndex] & 0x0F));
                            layer2[cell] = (byte)((vegs[globalIndex] << 4) | (waters[globalIndex] & 0x0F));

                            int ulongIndex = cell >> 6;
                            int bitIndex = cell & 0x3F;
                            ulong mask = 1UL << bitIndex;
                            if (ramp[globalIndex]) rampU[ulongIndex] |= mask;
                            if (snow[globalIndex]) snowU[ulongIndex] |= mask;
                            if (mud[globalIndex]) mudU[ulongIndex] |= mask;
                            if (ice[globalIndex]) iceU[ulongIndex] |= mask;

                            chunkTerritory[cell] = territory[globalIndex];

                            bool isOdd = (globalR & 1) == 1;

                            // Edge 0: horizontal right (c,r)↔(c+1,r)
                            int n0c = globalC + 1;
                            int n0r = globalR;
                            if (ShouldStraightenEdge(heights, ramp, mapWidth, mapHeight, globalC, globalR, n0c, n0r, edgeDir: 0))
                            {
                                int bit = cell * 3 + 0;
                                cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
                            }

                            // Edge 1: diagonal bottom (c,r)↔(n1c, r+1)
                            int n1c = isOdd ? globalC + 1 : globalC;
                            int n1r = globalR + 1;
                            if (ShouldStraightenEdge(heights, ramp, mapWidth, mapHeight, globalC, globalR, n1c, n1r, edgeDir: 1))
                            {
                                int bit = cell * 3 + 1;
                                cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
                            }

                            // Edge 2: diagonal bottom (c,r)↔(n2c, r+1)
                            int n2c = isOdd ? globalC : globalC - 1;
                            int n2r = globalR + 1;
                            if (ShouldStraightenEdge(heights, ramp, mapWidth, mapHeight, globalC, globalR, n2c, n2r, edgeDir: 2))
                            {
                                int bit = cell * 3 + 2;
                                cliffStraighten[bit >> 3] |= (byte)(1 << (bit & 7));
                            }
                        }
                    }

                    Buffer.BlockCopy(rampU, 0, rampBytes, 0, rampBytes.Length);
                    Buffer.BlockCopy(snowU, 0, snowBytes, 0, snowBytes.Length);
                    Buffer.BlockCopy(mudU, 0, mudBytes, 0, mudBytes.Length);
                    Buffer.BlockCopy(iceU, 0, iceBytes, 0, iceBytes.Length);

                    bw.Write(packed);
                    bw.Write(layer2);
                    bw.Write(flagsZeros);
                    bw.Write(rampBytes);
                    bw.Write(factionsZeros);
                    bw.Write(snowBytes);
                    bw.Write(mudBytes);
                    bw.Write(iceBytes);
                    bw.Write(chunkTerritory);
                    bw.Write(cliffStraighten);
                }
            }

            bw.Flush();
            summary.OutputBytes = output.Length;
            return summary;
        }
    }
}
