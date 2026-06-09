using System;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public static class LogicHeightmapVisualHeightmapAdapter
    {
        public static LogicHeightmap FromVisualHeightmap(
            VisualHeightmapAsset asset,
            int layerIndex,
            int navChunkSamples)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if ((uint)layerIndex >= (uint)asset.Layers.Length) throw new ArgumentOutOfRangeException(nameof(layerIndex));
            if (navChunkSamples <= 0) throw new ArgumentOutOfRangeException(nameof(navChunkSamples));

            int widthChunks = Math.Max(1, (asset.SampleColumns + navChunkSamples - 1) / navChunkSamples);
            int heightChunks = Math.Max(1, (asset.SampleRows + navChunkSamples - 1) / navChunkSamples);
            int widthSamples = checked(widthChunks * LogicHeightmapChunk.ChunkSize);
            int heightSamples = checked(heightChunks * LogicHeightmapChunk.ChunkSize);
            int cellSizeXCm = Math.Max(1, asset.Bounds.Width / Math.Max(1, widthSamples));
            int cellSizeZCm = Math.Max(1, asset.Bounds.Height / Math.Max(1, heightSamples));

            var logic = new LogicHeightmap();
            logic.Initialize(widthChunks, heightChunks, LogicHeightmapGridKind.QuadGrid, cellSizeXCm, cellSizeZCm);

            var runtime = new VisualHeightmapRuntime(asset);
            for (int y = 0; y < heightSamples; y++)
            {
                float worldY = asset.Bounds.Top + (heightSamples == 1 ? 0f : y / (float)(heightSamples - 1) * asset.Bounds.Height);
                for (int x = 0; x < widthSamples; x++)
                {
                    float worldX = asset.Bounds.Left + (widthSamples == 1 ? 0f : x / (float)(widthSamples - 1) * asset.Bounds.Width);
                    if (!runtime.TrySampleHeightCm(worldX, worldY, out float heightCm, layerIndex))
                    {
                        heightCm = 0f;
                    }

                    int roundedHeightCm = (int)MathF.Round(heightCm);
                    logic.SetHeightCm(x, y, roundedHeightCm);
                    logic.SetWaterHeightCm(x, y, 0);
                    logic.SetAreaId(x, y, ClassifyAreaId(heightCm));
                    logic.SetBlocked(x, y, IsGeneratedNoFlyPeak(heightCm));
                }
            }

            return logic;
        }

        public static void WriteVisualHeightmap(
            Stream stream,
            VisualHeightmapAsset asset,
            int layerIndex,
            int navChunkSamples)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if ((uint)layerIndex >= (uint)asset.Layers.Length) throw new ArgumentOutOfRangeException(nameof(layerIndex));
            if (navChunkSamples <= 0) throw new ArgumentOutOfRangeException(nameof(navChunkSamples));

            int widthChunks = Math.Max(1, (asset.SampleColumns + navChunkSamples - 1) / navChunkSamples);
            int heightChunks = Math.Max(1, (asset.SampleRows + navChunkSamples - 1) / navChunkSamples);
            int widthSamples = checked(widthChunks * LogicHeightmapChunk.ChunkSize);
            int heightSamples = checked(heightChunks * LogicHeightmapChunk.ChunkSize);
            int cellSizeXCm = Math.Max(1, asset.Bounds.Width / Math.Max(1, widthSamples));
            int cellSizeZCm = Math.Max(1, asset.Bounds.Height / Math.Max(1, heightSamples));
            var runtime = new VisualHeightmapRuntime(asset);

            LogicHeightmapBinary.WriteChunked(
                stream,
                widthChunks,
                heightChunks,
                LogicHeightmapGridKind.QuadGrid,
                cellSizeXCm,
                cellSizeZCm,
                (chunkX, chunkY) => BuildVisualChunk(runtime, asset, layerIndex, widthSamples, heightSamples, chunkX, chunkY));
        }

        public static void WriteVisualHeightmap(
            Stream stream,
            Stream visualHeightmapStream,
            int layerIndex,
            int navChunkSamples)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (visualHeightmapStream == null) throw new ArgumentNullException(nameof(visualHeightmapStream));
            if (navChunkSamples <= 0) throw new ArgumentOutOfRangeException(nameof(navChunkSamples));

            using var reader = VisualHeightmapFileReader.Open(visualHeightmapStream);
            if ((uint)layerIndex >= (uint)reader.Layers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(layerIndex));
            }

            int widthChunks = Math.Max(1, (reader.SampleColumns + navChunkSamples - 1) / navChunkSamples);
            int heightChunks = Math.Max(1, (reader.SampleRows + navChunkSamples - 1) / navChunkSamples);
            int widthSamples = checked(widthChunks * LogicHeightmapChunk.ChunkSize);
            int heightSamples = checked(heightChunks * LogicHeightmapChunk.ChunkSize);
            int cellSizeXCm = Math.Max(1, reader.Bounds.Width / Math.Max(1, widthSamples));
            int cellSizeZCm = Math.Max(1, reader.Bounds.Height / Math.Max(1, heightSamples));

            LogicHeightmapBinary.WriteChunked(
                stream,
                widthChunks,
                heightChunks,
                LogicHeightmapGridKind.QuadGrid,
                cellSizeXCm,
                cellSizeZCm,
                (chunkX, chunkY) => BuildVisualChunk(reader, layerIndex, widthSamples, heightSamples, chunkX, chunkY));
        }

        private static LogicHeightmapChunk BuildVisualChunk(
            VisualHeightmapRuntime runtime,
            VisualHeightmapAsset asset,
            int layerIndex,
            int widthSamples,
            int heightSamples,
            int chunkX,
            int chunkY)
        {
            var chunk = new LogicHeightmapChunk();
            int startX = chunkX * LogicHeightmapChunk.ChunkSize;
            int startY = chunkY * LogicHeightmapChunk.ChunkSize;
            for (int ly = 0; ly < LogicHeightmapChunk.ChunkSize; ly++)
            {
                int y = startY + ly;
                float worldY = asset.Bounds.Top + (heightSamples == 1 ? 0f : y / (float)(heightSamples - 1) * asset.Bounds.Height);
                for (int lx = 0; lx < LogicHeightmapChunk.ChunkSize; lx++)
                {
                    int x = startX + lx;
                    float worldX = asset.Bounds.Left + (widthSamples == 1 ? 0f : x / (float)(widthSamples - 1) * asset.Bounds.Width);
                    if (!runtime.TrySampleHeightCm(worldX, worldY, out float heightCm, layerIndex))
                    {
                        heightCm = 0f;
                    }

                    int roundedHeightCm = (int)MathF.Round(heightCm);
                    chunk.SetHeightCm(lx, ly, roundedHeightCm);
                    chunk.SetWaterHeightCm(lx, ly, 0);
                    chunk.SetAreaId(lx, ly, ClassifyAreaId(heightCm));
                    chunk.SetBlocked(lx, ly, IsGeneratedNoFlyPeak(heightCm));
                }
            }

            return chunk;
        }

        private static LogicHeightmapChunk BuildVisualChunk(
            VisualHeightmapFileReader reader,
            int layerIndex,
            int widthSamples,
            int heightSamples,
            int chunkX,
            int chunkY)
        {
            var chunk = new LogicHeightmapChunk();
            int startX = chunkX * LogicHeightmapChunk.ChunkSize;
            int startY = chunkY * LogicHeightmapChunk.ChunkSize;
            for (int ly = 0; ly < LogicHeightmapChunk.ChunkSize; ly++)
            {
                int y = startY + ly;
                float worldY = reader.Bounds.Top + (heightSamples == 1 ? 0f : y / (float)(heightSamples - 1) * reader.Bounds.Height);
                for (int lx = 0; lx < LogicHeightmapChunk.ChunkSize; lx++)
                {
                    int x = startX + lx;
                    float worldX = reader.Bounds.Left + (widthSamples == 1 ? 0f : x / (float)(widthSamples - 1) * reader.Bounds.Width);
                    if (!reader.TrySampleHeightCm(worldX, worldY, out float heightCm, layerIndex))
                    {
                        heightCm = 0f;
                    }

                    int roundedHeightCm = (int)MathF.Round(heightCm);
                    chunk.SetHeightCm(lx, ly, roundedHeightCm);
                    chunk.SetWaterHeightCm(lx, ly, 0);
                    chunk.SetAreaId(lx, ly, ClassifyAreaId(heightCm));
                    chunk.SetBlocked(lx, ly, IsGeneratedNoFlyPeak(heightCm));
                }
            }

            return chunk;
        }

        private static byte ClassifyAreaId(float heightCm)
        {
            if (heightCm < 250f) return 5;
            if (heightCm > 1650f) return 6;
            if (heightCm > 1200f) return 2;
            if (heightCm > 800f) return 3;
            return 0;
        }

        private static bool IsGeneratedNoFlyPeak(float heightCm)
        {
            return heightCm > 1680f;
        }
    }
}
