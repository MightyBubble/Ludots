using System;
using System.IO;
using System.Text;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.Terrain
{
    /// <summary>
    /// React web editor grid map_data.bin: chunks of TerrainChunkCells squared, dense stride-4
    /// cells or the sparse 0x84 chunk-indexed layout. Shared by the runtime board loader and the
    /// bake tooling so grid boards keep one authoritative reader.
    /// <see cref="WriteAreaGrid"/> writes the dense layout with AreaId in byte 3 and every other channel zero.
    /// </summary>
    public static class ReactGridTerrainBinary
    {
        public const byte SparseFormatVersion = 0x84;
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
            var terrain = new SparseGridLogicTerrainField(widthCells, heightCells, cellSizeCm, ChunkSize);
            byte[] reactChunk = new byte[ChunkBytes];

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

                    LoadChunk(terrain, cx, cy, reactChunk);
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

                    LoadChunk(terrain, cx, cy, reactChunk);
                }
            }

            return terrain;
        }

        public static void WriteAreaGrid(Stream output, int cellsX, int cellsY, byte[] areaIds)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (areaIds == null) throw new ArgumentNullException(nameof(areaIds));
            if (cellsX <= 0 || cellsY <= 0)
            {
                throw new InvalidOperationException($"React terrain area grid {cellsX}×{cellsY} 无效（需为 > 0）。");
            }

            if (cellsX % ChunkSize != 0 || cellsY % ChunkSize != 0)
            {
                throw new InvalidOperationException(
                    $"React terrain area grid {cellsX}×{cellsY} 必须能被区块边长 {ChunkSize} 整除。");
            }

            int cellCount = checked(cellsX * cellsY);
            if (areaIds.Length != cellCount)
            {
                throw new InvalidOperationException(
                    $"React terrain area grid 需要 {cellCount} 个格值，实得 {areaIds.Length}。");
            }

            int widthChunks = cellsX / ChunkSize;
            int heightChunks = cellsY / ChunkSize;
            var chunk = new byte[ChunkBytes];
            using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
            writer.Write(widthChunks);
            writer.Write(heightChunks);
            writer.Write(DenseCellStride);

            for (int chunkY = 0; chunkY < heightChunks; chunkY++)
            {
                for (int chunkX = 0; chunkX < widthChunks; chunkX++)
                {
                    Array.Clear(chunk, 0, chunk.Length);
                    int originCol = chunkX * ChunkSize;
                    int originRow = chunkY * ChunkSize;
                    for (int localY = 0; localY < ChunkSize; localY++)
                    {
                        int row = originRow + localY;
                        int rowIndex = row * cellsX;
                        int localRow = localY * ChunkSize;
                        for (int localX = 0; localX < ChunkSize; localX++)
                        {
                            chunk[(localRow + localX) * DenseCellStride + 3] = areaIds[rowIndex + originCol + localX];
                        }
                    }

                    writer.Write(chunk);
                }
            }

            writer.Flush();
        }

        private static void ValidateHeader(string inputName, int widthChunks, int heightChunks, byte strideOrVersion)
        {
            if (widthChunks <= 0 || heightChunks <= 0)
            {
                throw new InvalidDataException($"Invalid chunk dimensions in '{inputName}': {widthChunks}x{heightChunks}");
            }

            if (strideOrVersion != DenseCellStride && strideOrVersion != SparseFormatVersion)
            {
                throw new InvalidDataException(
                    $"React terrain format mismatch in '{inputName}'. Expected stride={DenseCellStride} or sparse version={SparseFormatVersion}, actual={strideOrVersion}.");
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
