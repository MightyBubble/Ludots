using System;
using System.IO;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public static class LogicHeightmapQuadGridAdapter
    {
        public static LogicHeightmap FromSamples(
            int sampleColumns,
            int sampleRows,
            ReadOnlySpan<int> heightCm,
            int cellSizeXCm = 100,
            int cellSizeZCm = 100)
        {
            if (sampleColumns <= 0) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (heightCm.Length != checked(sampleColumns * sampleRows)) throw new ArgumentException(nameof(heightCm));

            int widthChunks = Math.Max(1, (sampleColumns + LogicHeightmapChunk.ChunkSize - 1) / LogicHeightmapChunk.ChunkSize);
            int heightChunks = Math.Max(1, (sampleRows + LogicHeightmapChunk.ChunkSize - 1) / LogicHeightmapChunk.ChunkSize);
            int widthSamples = checked(widthChunks * LogicHeightmapChunk.ChunkSize);
            int fullHeightSamples = checked(heightChunks * LogicHeightmapChunk.ChunkSize);

            var logic = new LogicHeightmap();
            logic.Initialize(widthChunks, heightChunks, LogicHeightmapGridKind.QuadGrid, cellSizeXCm, cellSizeZCm);

            for (int y = 0; y < fullHeightSamples; y++)
            {
                int srcY = Math.Min(y, sampleRows - 1);
                for (int x = 0; x < widthSamples; x++)
                {
                    int srcX = Math.Min(x, sampleColumns - 1);
                    int h = heightCm[srcY * sampleColumns + srcX];
                    logic.SetHeightCm(x, y, h);
                    logic.SetAreaId(x, y, ClassifyAreaId(h));
                }
            }

            return logic;
        }

        public static void WriteGenerated(
            Stream stream,
            int widthChunks,
            int heightChunks,
            Func<int, int, int> heightCmAt,
            int cellSizeXCm = 100,
            int cellSizeZCm = 100)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (widthChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthChunks));
            if (heightChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightChunks));
            if (heightCmAt == null) throw new ArgumentNullException(nameof(heightCmAt));
            if (cellSizeXCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeXCm));
            if (cellSizeZCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeZCm));

            LogicHeightmapBinary.WriteChunked(
                stream,
                widthChunks,
                heightChunks,
                LogicHeightmapGridKind.QuadGrid,
                cellSizeXCm,
                cellSizeZCm,
                (chunkX, chunkY) => BuildGeneratedChunk(chunkX, chunkY, heightCmAt));
        }

        private static LogicHeightmapChunk BuildGeneratedChunk(int chunkX, int chunkY, Func<int, int, int> heightCmAt)
        {
            var chunk = new LogicHeightmapChunk();
            int startX = chunkX * LogicHeightmapChunk.ChunkSize;
            int startY = chunkY * LogicHeightmapChunk.ChunkSize;
            for (int ly = 0; ly < LogicHeightmapChunk.ChunkSize; ly++)
            {
                for (int lx = 0; lx < LogicHeightmapChunk.ChunkSize; lx++)
                {
                    int h = heightCmAt(startX + lx, startY + ly);
                    chunk.SetHeightCm(lx, ly, h);
                    chunk.SetAreaId(lx, ly, ClassifyAreaId(h));
                }
            }

            return chunk;
        }

        private static byte ClassifyAreaId(int heightCm)
        {
            if (heightCm < 250) return 5;
            if (heightCm > 1200) return 2;
            if (heightCm > 800) return 3;
            return 0;
        }
    }
}
