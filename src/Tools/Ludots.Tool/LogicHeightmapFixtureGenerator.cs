using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;

namespace Ludots.Tool
{
    public static class LogicHeightmapFixtureGenerator
    {
        public static void GenerateQuadGrid(string outFile, int widthChunks, int heightChunks, MapVtxmGenerator.Preset preset, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(outFile)) throw new ArgumentException("Output file is required.", nameof(outFile));
            if (widthChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthChunks));
            if (heightChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightChunks));

            outFile = Path.GetFullPath(outFile);
            string? outDir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            if (File.Exists(outFile) && !overwrite)
            {
                throw new IOException($"File already exists: {outFile} (pass --overwrite to replace)");
            }

            int sampleColumns = checked(widthChunks * LogicHeightmapChunk.ChunkSize);
            int sampleRows = checked(heightChunks * LogicHeightmapChunk.ChunkSize);
            using var fs = File.Create(outFile);
            LogicHeightmapBinary.WriteChunked(
                fs,
                widthChunks,
                heightChunks,
                LogicHeightmapGridKind.QuadGrid,
                cellSizeXCm: 100,
                cellSizeZCm: 100,
                (chunkX, chunkY) => BuildGeneratedChunk(chunkX, chunkY, sampleColumns, sampleRows, preset));
        }

        public static void GenerateQuadGridSubset(
            string outFile,
            int widthChunks,
            int heightChunks,
            MapVtxmGenerator.Preset preset,
            int chunkMinX,
            int chunkMinY,
            int chunkMaxX,
            int chunkMaxY,
            bool includeNeighbors,
            bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(outFile)) throw new ArgumentException("Output file is required.", nameof(outFile));
            if (widthChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthChunks));
            if (heightChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightChunks));
            if (chunkMinX > chunkMaxX) throw new ArgumentException("chunkMinX must be <= chunkMaxX.");
            if (chunkMinY > chunkMaxY) throw new ArgumentException("chunkMinY must be <= chunkMaxY.");

            int firstX = Math.Max(0, includeNeighbors ? chunkMinX - 1 : chunkMinX);
            int firstY = Math.Max(0, includeNeighbors ? chunkMinY - 1 : chunkMinY);
            int lastX = Math.Min(widthChunks - 1, includeNeighbors ? chunkMaxX + 1 : chunkMaxX);
            int lastY = Math.Min(heightChunks - 1, includeNeighbors ? chunkMaxY + 1 : chunkMaxY);
            if (firstX > lastX || firstY > lastY)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkMinX), "Requested chunk subset is outside the LogicHeightmap bounds.");
            }

            outFile = Path.GetFullPath(outFile);
            string? outDir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            if (File.Exists(outFile) && !overwrite)
            {
                throw new IOException($"File already exists: {outFile} (pass --overwrite to replace)");
            }

            int sampleColumns = checked(widthChunks * LogicHeightmapChunk.ChunkSize);
            int sampleRows = checked(heightChunks * LogicHeightmapChunk.ChunkSize);
            var chunks = new List<(int ChunkX, int ChunkY)>((lastX - firstX + 1) * (lastY - firstY + 1));
            for (int cy = firstY; cy <= lastY; cy++)
            {
                for (int cx = firstX; cx <= lastX; cx++)
                {
                    chunks.Add((cx, cy));
                }
            }

            using var fs = File.Create(outFile);
            LogicHeightmapBinary.WriteChunkedSubset(
                fs,
                widthChunks,
                heightChunks,
                LogicHeightmapGridKind.QuadGrid,
                cellSizeXCm: 100,
                cellSizeZCm: 100,
                chunks,
                (chunkX, chunkY) => BuildGeneratedChunk(chunkX, chunkY, sampleColumns, sampleRows, preset));
        }

        private static LogicHeightmapChunk BuildGeneratedChunk(int chunkX, int chunkY, int sampleColumns, int sampleRows, MapVtxmGenerator.Preset preset)
        {
            var chunk = new LogicHeightmapChunk();
            int startX = chunkX * LogicHeightmapChunk.ChunkSize;
            int startY = chunkY * LogicHeightmapChunk.ChunkSize;
            for (int ly = 0; ly < LogicHeightmapChunk.ChunkSize; ly++)
            {
                for (int lx = 0; lx < LogicHeightmapChunk.ChunkSize; lx++)
                {
                    int sampleX = startX + lx;
                    int sampleY = startY + ly;
                    MountainRiverFixtureSample? mountainRiver = preset == MapVtxmGenerator.Preset.MountainRiver
                        ? MountainRiverFixtureTerrain.Sample(sampleColumns, sampleRows, sampleX, sampleY)
                        : null;
                    int h = mountainRiver?.HeightCm ?? HeightCmAt(preset, sampleColumns, sampleRows, sampleX, sampleY);
                    byte areaId = mountainRiver?.AreaId ?? ClassifyAreaId(h);
                    chunk.SetHeightCm(lx, ly, h);
                    chunk.SetWaterHeightCm(lx, ly, mountainRiver?.WaterHeightCm ?? 0);
                    chunk.SetAreaId(lx, ly, areaId);
                    chunk.SetBlocked(lx, ly, mountainRiver?.Blocked ?? IsBlockedMask(preset, sampleColumns, sampleRows, sampleX, sampleY, h));
                }
            }

            return chunk;
        }

        private static int HeightCmAt(MapVtxmGenerator.Preset preset, int width, int height, int xCell, int yCell)
        {
            return preset switch
            {
                MapVtxmGenerator.Preset.Flat => 600,
                MapVtxmGenerator.Preset.Lake => 600 + ((yCell / 32) % 4) * 120,
                MapVtxmGenerator.Preset.Cliffs => ((xCell / 16) & 1) == 0 ? 200 : 1200,
                MapVtxmGenerator.Preset.MountainRiver => MountainRiverFixtureTerrain.Sample(width, height, xCell, yCell).HeightCm,
                _ => 450 + (((xCell / 16) % 12) + (((yCell / 128) & 1) * 3)) * 80
            };
        }

        private static byte ClassifyAreaId(int heightCm)
        {
            if (heightCm < 250) return 5;
            if (heightCm > 1650) return 6;
            if (heightCm > 1200) return 2;
            if (heightCm > 800) return 3;
            return 0;
        }

        private static bool IsBlockedMask(MapVtxmGenerator.Preset preset, int width, int height, int xCell, int yCell, int heightCm)
        {
            return preset == MapVtxmGenerator.Preset.MountainRiver &&
                MountainRiverFixtureTerrain.Sample(width, height, xCell, yCell).Blocked;
        }
    }
}
