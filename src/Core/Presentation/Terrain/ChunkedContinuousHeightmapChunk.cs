using System;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Loaded chunk payload for a chunked visual heightmap.
    /// Arrays are owned by the caller/streaming layer and reused across runtime systems.
    /// </summary>
    public sealed class ChunkedContinuousHeightmapChunk
    {
        public ChunkedContinuousHeightmapChunk(int chunkX, int chunkY, short[] heightSamplesCm, int generation = 0)
        {
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));

            ChunkX = chunkX;
            ChunkY = chunkY;
            HeightSamplesCm = heightSamplesCm;
            HeightSamplesRaw = Array.Empty<ushort>();
            UsesRawUInt16Samples = false;
            Generation = generation;
        }

        public ChunkedContinuousHeightmapChunk(int chunkX, int chunkY, ushort[] heightSamplesRaw, int generation = 0)
        {
            if (heightSamplesRaw == null) throw new ArgumentNullException(nameof(heightSamplesRaw));

            ChunkX = chunkX;
            ChunkY = chunkY;
            HeightSamplesCm = Array.Empty<short>();
            HeightSamplesRaw = heightSamplesRaw;
            UsesRawUInt16Samples = true;
            Generation = generation;
        }

        public int ChunkX { get; }

        public int ChunkY { get; }

        public short[] HeightSamplesCm { get; }

        public ushort[] HeightSamplesRaw { get; }

        public bool UsesRawUInt16Samples { get; }

        public int Generation { get; }

        public int SampleCount => UsesRawUInt16Samples ? HeightSamplesRaw.Length : HeightSamplesCm.Length;
    }
}
