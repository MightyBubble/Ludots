using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.Terrain
{
    /// <summary>
    /// Reads and writes the React web editor's grid map_data.bin terrain: chunks of TerrainChunkCells squared,
    /// dense stride-4 cells or the sparse 0x84/0x85 chunk-indexed layout. Shared by the runtime board
    /// loader and the bake tooling so grid boards keep one authoritative reader.
    /// </summary>
    public static class ReactGridTerrainBinary
    {
        public const byte SparseFormatVersion = 0x84;
        public const byte SparseFormatVersionWithOrigin = 0x85;

        private const byte DenseCellStride = 4;
        private const int ChunkSize = SpatialScaleDefaults.TerrainChunkCells;
        private const int CellsPerChunk = ChunkSize * ChunkSize;
        private const int ChunkBytes = CellsPerChunk * DenseCellStride;

        public static LogicTerrainField Read(string inputPath, int cellSizeCm = SpatialScaleDefaults.CellCm)
        {
            using var input = File.OpenRead(inputPath);
            return Read(inputPath, input, cellSizeCm);
        }

        public static LogicTerrainField Read(string inputName, Stream input, int cellSizeCm)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));

            using var br = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            int widthChunks = br.ReadInt32();
            int heightChunks = br.ReadInt32();
            byte strideOrVersion = br.ReadByte();

            ValidateHeader(inputName, widthChunks, heightChunks, strideOrVersion);

            int widthCells = checked(widthChunks * ChunkSize);
            int heightCells = checked(heightChunks * ChunkSize);
            int originXcm = 0;
            int originZcm = 0;
            if (strideOrVersion == SparseFormatVersionWithOrigin)
            {
                originXcm = br.ReadInt32();
                originZcm = br.ReadInt32();
            }

            var terrain = new SparseGridLogicTerrainField(
                widthCells,
                heightCells,
                cellSizeCm,
                ChunkSize,
                default,
                originXcm,
                originZcm);
            byte[] reactChunk = new byte[ChunkBytes];

            if (strideOrVersion == DenseCellStride)
            {
                for (int cy = 0; cy < heightChunks; cy++)
                {
                    for (int cx = 0; cx < widthChunks; cx++)
                    {
                        ReadExact(input, reactChunk, 0, reactChunk.Length, $"Unexpected EOF when reading chunk ({cx},{cy}) from '{inputName}'.");
                        LoadChunk(terrain, cx, cy, reactChunk);
                    }
                }

                return terrain;
            }

            int chunkCount = br.ReadInt32();
            if (chunkCount < 0)
            {
                throw new InvalidDataException($"Sparse React terrain '{inputName}' has negative chunk count {chunkCount}.");
            }

            for (int i = 0; i < chunkCount; i++)
            {
                int cx = br.ReadInt32();
                int cy = br.ReadInt32();
                if ((uint)cx >= (uint)widthChunks || (uint)cy >= (uint)heightChunks)
                {
                    throw new InvalidDataException($"Sparse React terrain '{inputName}' has chunk ({cx},{cy}) outside {widthChunks}x{heightChunks}.");
                }

                ReadExact(input, reactChunk, 0, reactChunk.Length, $"Unexpected EOF when reading sparse chunk ({cx},{cy}) from '{inputName}'.");
                LoadChunk(terrain, cx, cy, reactChunk);
            }

            return terrain;
        }

        public static void Write(string outputPath, LogicTerrainField terrain)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentNullException(nameof(outputPath));
            }

            using var output = File.Create(outputPath);
            Write(output, terrain);
        }

        public static void Write(Stream output, LogicTerrainField terrain)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (terrain.Topology != LogicTerrainTopology.Grid)
            {
                throw new NotSupportedException($"React grid terrain write only supports grid logic terrain, got {terrain.Topology}.");
            }

            using var bw = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
            bw.Write(terrain.WidthChunks);
            bw.Write(terrain.HeightChunks);

            int originXcm = ResolveOriginXcm(terrain);
            int originZcm = ResolveOriginZcm(terrain);
            bool hasOrigin = originXcm != 0 || originZcm != 0;
            bw.Write(hasOrigin ? SparseFormatVersionWithOrigin : SparseFormatVersion);
            if (hasOrigin)
            {
                bw.Write(originXcm);
                bw.Write(originZcm);
            }

            var chunks = new List<(int ChunkX, int ChunkY, byte[] Bytes)>();
            for (int chunkY = 0; chunkY < terrain.HeightChunks; chunkY++)
            {
                int rowStart = chunkY * ChunkSize;
                int rowsInChunk = terrain.TileHeightCells(chunkY);
                for (int chunkX = 0; chunkX < terrain.WidthChunks; chunkX++)
                {
                    int colStart = chunkX * ChunkSize;
                    int colsInChunk = terrain.TileWidthCells(chunkX);
                    byte[] bytes = new byte[ChunkBytes];
                    bool authored = false;

                    for (int localY = 0; localY < ChunkSize; localY++)
                    {
                        int globalRow = rowStart + localY;
                        bool rowInBounds = localY < rowsInChunk;
                        for (int localX = 0; localX < ChunkSize; localX++)
                        {
                            int cell = (localY * ChunkSize) + localX;
                            int i = cell * DenseCellStride;
                            bool inBounds = rowInBounds && localX < colsInChunk;
                            LogicTerrainCell terrainCell = inBounds
                                ? terrain.GetCell(colStart + localX, globalRow)
                                : default;

                            bytes[i] = (byte)(((terrainCell.HeightLevel & 0x0F) << 4) | (terrainCell.WaterHeightLevel & 0x0F));
                            bytes[i + 1] = 0;
                            bytes[i + 2] = 0;
                            if (terrainCell.IsRamp)
                            {
                                bytes[i + 2] |= 0b1000_0000;
                            }
                            if (terrainCell.IsBlocked)
                            {
                                bytes[i + 2] |= 0b0000_1000;
                            }
                            bytes[i + 3] = terrainCell.AreaId;
                            authored |= terrainCell.HeightLevel != 0 ||
                                terrainCell.WaterHeightLevel != 0 ||
                                terrainCell.SurfaceFlags != LogicTerrainSurfaceFlags.None ||
                                terrainCell.AreaId != 0;
                        }
                    }

                    if (authored)
                    {
                        chunks.Add((chunkX, chunkY, bytes));
                    }
                }
            }

            bw.Write(chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                bw.Write(chunks[i].ChunkX);
                bw.Write(chunks[i].ChunkY);
                bw.Write(chunks[i].Bytes);
            }
        }

        private static int ResolveOriginXcm(LogicTerrainField terrain)
            => terrain switch
            {
                MutableGridLogicTerrainField mutable => mutable.OriginXcm,
                FlatGridLogicTerrainField flat => flat.OriginXcm,
                SparseGridLogicTerrainField sparse => sparse.OriginXcm,
                _ => 0
            };

        private static int ResolveOriginZcm(LogicTerrainField terrain)
            => terrain switch
            {
                MutableGridLogicTerrainField mutable => mutable.OriginZcm,
                FlatGridLogicTerrainField flat => flat.OriginZcm,
                SparseGridLogicTerrainField sparse => sparse.OriginZcm,
                _ => 0
            };

        private static void ValidateHeader(string inputName, int widthChunks, int heightChunks, byte strideOrVersion)
        {
            if (widthChunks <= 0 || heightChunks <= 0)
            {
                throw new InvalidDataException($"Invalid chunk dimensions in '{inputName}': {widthChunks}x{heightChunks}");
            }

            if (strideOrVersion != DenseCellStride &&
                strideOrVersion != SparseFormatVersion &&
                strideOrVersion != SparseFormatVersionWithOrigin)
            {
                throw new InvalidDataException(
                    $"React terrain format mismatch in '{inputName}'. Expected stride={DenseCellStride} or sparse versions={SparseFormatVersion}/{SparseFormatVersionWithOrigin}, actual={strideOrVersion}.");
            }
        }

        private static void ReadExact(Stream input, byte[] buffer, int offset, int count, string errorMessage)
        {
            int readTotal = 0;
            while (readTotal < count)
            {
                int read = input.Read(buffer, offset + readTotal, count - readTotal);
                if (read <= 0)
                {
                    throw new EndOfStreamException(errorMessage);
                }

                readTotal += read;
            }
        }

        private static void LoadChunk(SparseGridLogicTerrainField terrain, int chunkX, int chunkY, byte[] reactChunk)
        {
            var cells = new LogicTerrainCell[CellsPerChunk];
            bool hasAuthoredCell = false;

            for (int ly = 0; ly < ChunkSize; ly++)
            {
                for (int lx = 0; lx < ChunkSize; lx++)
                {
                    int cell = (ly * ChunkSize) + lx;
                    int i = cell * DenseCellStride;

                    byte b0 = reactChunk[i];
                    byte b2 = reactChunk[i + 2];
                    byte areaId = reactChunk[i + 3];
                    byte height = (byte)((b0 >> 4) & 0x0F);
                    byte water = (byte)(b0 & 0x0F);

                    var flags = LogicTerrainSurfaceFlags.None;
                    if ((b2 & 0b1000_0000) != 0) flags |= LogicTerrainSurfaceFlags.Ramp;
                    if ((b2 & 0b0000_1000) != 0) flags |= LogicTerrainSurfaceFlags.Blocked;
                    if (water > height) flags |= LogicTerrainSurfaceFlags.Water;

                    cells[cell] = new LogicTerrainCell(height, water, flags, areaId);
                    hasAuthoredCell |= height != 0 ||
                        water != 0 ||
                        flags != LogicTerrainSurfaceFlags.None ||
                        areaId != 0;
                }
            }

            if (hasAuthoredCell)
            {
                terrain.SetChunk(chunkX, chunkY, cells);
            }
        }
    }
}
