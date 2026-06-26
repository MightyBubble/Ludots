using System;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Loaded chunk payload for a chunked visual heightmap.
    /// Arrays are owned by the caller/streaming layer and reused across runtime systems.
    /// </summary>
    public sealed class ChunkedVisualHeightmapChunk
    {
        public ChunkedVisualHeightmapChunk(
            int chunkX,
            int chunkY,
            short[] heightSamplesCm,
            int generation = 0,
            ChunkedVisualHeightmapChunkMipLevel[]? mipLevels = null)
        {
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));

            ChunkX = chunkX;
            ChunkY = chunkY;
            HeightSamplesCm = heightSamplesCm;
            HeightSamplesRaw = Array.Empty<ushort>();
            UsesRawUInt16Samples = false;
            Generation = generation;
            MipLevels = ValidateMipLevels(mipLevels, usesRawUInt16Samples: false);
        }

        public ChunkedVisualHeightmapChunk(
            int chunkX,
            int chunkY,
            ushort[] heightSamplesRaw,
            int generation = 0,
            ChunkedVisualHeightmapChunkMipLevel[]? mipLevels = null)
        {
            if (heightSamplesRaw == null) throw new ArgumentNullException(nameof(heightSamplesRaw));

            ChunkX = chunkX;
            ChunkY = chunkY;
            HeightSamplesCm = Array.Empty<short>();
            HeightSamplesRaw = heightSamplesRaw;
            UsesRawUInt16Samples = true;
            Generation = generation;
            MipLevels = ValidateMipLevels(mipLevels, usesRawUInt16Samples: true);
        }

        public int ChunkX { get; }

        public int ChunkY { get; }

        public short[] HeightSamplesCm { get; }

        public ushort[] HeightSamplesRaw { get; }

        public bool UsesRawUInt16Samples { get; }

        public int Generation { get; }

        public int SampleCount => UsesRawUInt16Samples ? HeightSamplesRaw.Length : HeightSamplesCm.Length;

        public ChunkedVisualHeightmapChunkMipLevel[] MipLevels { get; }

        public bool TryGetMipLevel(int mipLevel, out ChunkedVisualHeightmapChunkMipLevel level)
        {
            if (mipLevel <= 0 || mipLevel > MipLevels.Length)
            {
                level = null!;
                return false;
            }

            level = MipLevels[mipLevel - 1];
            return true;
        }

        private static ChunkedVisualHeightmapChunkMipLevel[] ValidateMipLevels(
            ChunkedVisualHeightmapChunkMipLevel[]? mipLevels,
            bool usesRawUInt16Samples)
        {
            if (mipLevels == null || mipLevels.Length == 0)
            {
                return Array.Empty<ChunkedVisualHeightmapChunkMipLevel>();
            }

            for (int i = 0; i < mipLevels.Length; i++)
            {
                ChunkedVisualHeightmapChunkMipLevel mip = mipLevels[i]
                    ?? throw new ArgumentException("Chunked visual heightmap mip levels must not contain null entries.", nameof(mipLevels));
                if (mip.Level != i + 1)
                {
                    throw new ArgumentException("Chunked visual heightmap mip levels must be contiguous from level 1.", nameof(mipLevels));
                }

                if (mip.UsesRawUInt16Samples != usesRawUInt16Samples)
                {
                    throw new ArgumentException("Chunked visual heightmap mip encoding must match the base chunk encoding.", nameof(mipLevels));
                }
            }

            return mipLevels;
        }
    }
}
