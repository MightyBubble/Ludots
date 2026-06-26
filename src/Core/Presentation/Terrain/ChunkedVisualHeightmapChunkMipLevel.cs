using System;

namespace Ludots.Core.Presentation.Terrain
{
    public sealed class ChunkedVisualHeightmapChunkMipLevel
    {
        public ChunkedVisualHeightmapChunkMipLevel(
            int level,
            int samplesPerChunkColumn,
            int samplesPerChunkRow,
            short[] heightSamplesCm)
        {
            if (level <= 0) throw new ArgumentOutOfRangeException(nameof(level));
            if (samplesPerChunkColumn < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChunkColumn));
            if (samplesPerChunkRow < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChunkRow));
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));

            Level = level;
            SamplesPerChunkColumn = samplesPerChunkColumn;
            SamplesPerChunkRow = samplesPerChunkRow;
            HeightSamplesCm = heightSamplesCm;
            HeightSamplesRaw = Array.Empty<ushort>();
        }

        public ChunkedVisualHeightmapChunkMipLevel(
            int level,
            int samplesPerChunkColumn,
            int samplesPerChunkRow,
            ushort[] heightSamplesRaw)
        {
            if (level <= 0) throw new ArgumentOutOfRangeException(nameof(level));
            if (samplesPerChunkColumn < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChunkColumn));
            if (samplesPerChunkRow < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChunkRow));
            if (heightSamplesRaw == null) throw new ArgumentNullException(nameof(heightSamplesRaw));

            Level = level;
            SamplesPerChunkColumn = samplesPerChunkColumn;
            SamplesPerChunkRow = samplesPerChunkRow;
            HeightSamplesCm = Array.Empty<short>();
            HeightSamplesRaw = heightSamplesRaw;
        }

        public int Level { get; }

        public int SamplesPerChunkColumn { get; }

        public int SamplesPerChunkRow { get; }

        public short[] HeightSamplesCm { get; }

        public ushort[] HeightSamplesRaw { get; }

        public bool UsesRawUInt16Samples => HeightSamplesRaw.Length > 0;

        public int SamplesPerLayerPerChunk => checked(SamplesPerChunkColumn * SamplesPerChunkRow);
    }
}
