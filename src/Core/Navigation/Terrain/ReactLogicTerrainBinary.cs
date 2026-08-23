using System;
using System.IO;
using System.Text;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.Terrain
{
    public static class ReactLogicTerrainBinary
    {
        public const byte SparseFormatVersion = 0x84;

        private const int ReactCellStride = 4;
        private const int ChunkSize = SpatialScaleDefaults.TerrainChunkCells;
        private const int CellsPerChunk = ChunkSize * ChunkSize;
        private const int ReactChunkBytes = CellsPerChunk * ReactCellStride;

        public static LogicTerrainField ReadGridLogicTerrainField(
            string inputPath,
            int cellSizeCm = SpatialScaleDefaults.CellCm)
        {
            using var input = File.OpenRead(inputPath);
            return ReadGridLogicTerrainField(inputPath, input, cellSizeCm);
        }

        public static LogicTerrainField ReadGridLogicTerrainField(
            string inputName,
            Stream input,
            int cellSizeCm = SpatialScaleDefaults.CellCm)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));

            using var br = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            int widthChunks = br.ReadInt32();
            int heightChunks = br.ReadInt32();
            byte strideOrVersion = br.ReadByte();

            ValidateReactHeader(inputName, input, widthChunks, heightChunks, strideOrVersion);

            int widthCells = checked(widthChunks * ChunkSize);
            int heightCells = checked(heightChunks * ChunkSize);
            var terrain = new SparseGridLogicTerrainField(widthCells, heightCells, cellSizeCm, ChunkSize);
            byte[] reactChunk = new byte[ReactChunkBytes];

            if (strideOrVersion == SparseFormatVersion)
            {
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

                    int read = input.Read(reactChunk, 0, reactChunk.Length);
                    if (read != reactChunk.Length)
                    {
                        throw new EndOfStreamException($"Unexpected EOF when reading sparse chunk ({cx},{cy}) from '{inputName}'.");
                    }

                    LoadReactChunkIntoSparseGrid(terrain, cx, cy, reactChunk);
                }

                return terrain;
            }

            if (input.Length - input.Position == 0)
            {
                return terrain;
            }

            for (int cy = 0; cy < heightChunks; cy++)
            {
                for (int cx = 0; cx < widthChunks; cx++)
                {
                    int read = input.Read(reactChunk, 0, reactChunk.Length);
                    if (read != reactChunk.Length)
                    {
                        throw new EndOfStreamException($"Unexpected EOF when reading chunk ({cx},{cy}) from '{inputName}'.");
                    }

                    LoadReactChunkIntoSparseGrid(terrain, cx, cy, reactChunk);
                }
            }

            return terrain;
        }

        public static void ValidateReactHeader(
            string inputName,
            Stream input,
            int widthChunks,
            int heightChunks,
            byte strideOrVersion)
        {
            if (widthChunks <= 0 || heightChunks <= 0)
            {
                throw new InvalidDataException($"React terrain '{inputName}' has invalid dimensions {widthChunks}x{heightChunks}.");
            }

            if (strideOrVersion != ReactCellStride && strideOrVersion != SparseFormatVersion)
            {
                throw new InvalidDataException($"React terrain '{inputName}' has unsupported stride/version {strideOrVersion}.");
            }

            if (strideOrVersion == SparseFormatVersion)
            {
                return;
            }

            long expected = sizeof(int) + sizeof(int) + sizeof(byte) + ((long)widthChunks * heightChunks * ReactChunkBytes);
            if (input.Length != expected)
            {
                throw new InvalidDataException($"React terrain '{inputName}' has {input.Length} bytes; expected {expected}.");
            }
        }

        private static void LoadReactChunkIntoSparseGrid(
            SparseGridLogicTerrainField terrain,
            int chunkX,
            int chunkY,
            byte[] reactChunk)
        {
            var cells = new LogicTerrainCell[CellsPerChunk];
            bool hasAuthoredCell = false;

            for (int ly = 0; ly < ChunkSize; ly++)
            {
                for (int lx = 0; lx < ChunkSize; lx++)
                {
                    int cell = (ly * ChunkSize) + lx;
                    int i = cell * ReactCellStride;

                    byte b0 = reactChunk[i];
                    byte b2 = reactChunk[i + 2];
                    byte areaId = reactChunk[i + 3];
                    byte height = (byte)((b0 >> 4) & 0x0F);
                    byte water = (byte)(b0 & 0x0F);

                    var flags = LogicTerrainSurfaceFlags.None;
                    if ((b2 & 0b1000_0000) != 0) flags |= LogicTerrainSurfaceFlags.Ramp;
                    if ((b2 & 0b0000_1000) != 0) flags |= LogicTerrainSurfaceFlags.Blocked;
                    if (water > height) flags |= LogicTerrainSurfaceFlags.Water;

                    var logicCell = new LogicTerrainCell(height, water, flags, areaId);
                    cells[cell] = logicCell;
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
