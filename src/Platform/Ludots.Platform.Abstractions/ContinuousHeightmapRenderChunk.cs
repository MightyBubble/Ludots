using System;

namespace Ludots.Platform.Abstractions
{
    public readonly struct ContinuousHeightmapRenderChunk
    {
        public ContinuousHeightmapRenderChunk(
            int chunkX,
            int chunkY,
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            float sampleStepXCm,
            float sampleStepYCm,
            ReadOnlyMemory<short> heightSamplesCm,
            ReadOnlyMemory<ushort> heightSamplesRaw,
            ContinuousHeightSampleScale sampleScale,
            ContinuousHeightmapStorageLayout storageLayout,
            int sampleStride,
            int layerSampleOffset,
            int revision)
        {
            if (chunkX < 0) throw new ArgumentOutOfRangeException(nameof(chunkX));
            if (chunkY < 0) throw new ArgumentOutOfRangeException(nameof(chunkY));
            if (sampleColumns < 2) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows < 2) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (!float.IsFinite(sampleStepXCm) || sampleStepXCm <= 0f) throw new ArgumentOutOfRangeException(nameof(sampleStepXCm));
            if (!float.IsFinite(sampleStepYCm) || sampleStepYCm <= 0f) throw new ArgumentOutOfRangeException(nameof(sampleStepYCm));

            if (sampleStride < sampleColumns)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleStride));
            }

            int requiredLastIndex = checked(layerSampleOffset + ((sampleRows - 1) * sampleStride) + sampleColumns);
            bool raw = storageLayout == ContinuousHeightmapStorageLayout.RowMajorUInt16Scaled ||
                       storageLayout == ContinuousHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled;
            if (raw)
            {
                if (heightSamplesRaw.Length < requiredLastIndex)
                {
                    throw new ArgumentException("Visual heightmap render chunk raw sample payload is too small.", nameof(heightSamplesRaw));
                }
            }
            else if (heightSamplesCm.Length < requiredLastIndex)
            {
                throw new ArgumentException("Visual heightmap render chunk centimeter sample payload is too small.", nameof(heightSamplesCm));
            }

            ChunkX = chunkX;
            ChunkY = chunkY;
            Bounds = bounds;
            SampleColumns = sampleColumns;
            SampleRows = sampleRows;
            SampleStepXCm = sampleStepXCm;
            SampleStepYCm = sampleStepYCm;
            HeightSamplesCm = heightSamplesCm;
            HeightSamplesRaw = heightSamplesRaw;
            SampleScale = sampleScale;
            StorageLayout = storageLayout;
            SampleStride = sampleStride;
            LayerSampleOffset = layerSampleOffset;
            Revision = revision;
        }

        public int ChunkX { get; }

        public int ChunkY { get; }

        public WorldAabbCm Bounds { get; }

        public int SampleColumns { get; }

        public int SampleRows { get; }

        public float SampleStepXCm { get; }

        public float SampleStepYCm { get; }

        public ReadOnlyMemory<short> HeightSamplesCm { get; }

        public ReadOnlyMemory<ushort> HeightSamplesRaw { get; }

        public ContinuousHeightSampleScale SampleScale { get; }

        public ContinuousHeightmapStorageLayout StorageLayout { get; }

        public int SampleStride { get; }

        public int LayerSampleOffset { get; }

        public int Revision { get; }

        public bool TryReadHeightCm(int sampleX, int sampleY, out float heightCm)
        {
            heightCm = default;
            if ((uint)sampleX >= (uint)SampleColumns || (uint)sampleY >= (uint)SampleRows)
            {
                return false;
            }

            int index = LayerSampleOffset + (sampleY * SampleStride) + sampleX;
            switch (StorageLayout)
            {
                case ContinuousHeightmapStorageLayout.RowMajorInt16Centimeters:
                case ContinuousHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters:
                    heightCm = HeightSamplesCm.Span[index];
                    return true;

                case ContinuousHeightmapStorageLayout.RowMajorUInt16Scaled:
                case ContinuousHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled:
                    heightCm = SampleScale.Decode(HeightSamplesRaw.Span[index]);
                    return true;

                default:
                    return false;
            }
        }
    }
}
