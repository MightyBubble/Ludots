using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Global descriptor for a chunked visual heightmap.
    /// Adjacent chunks share border samples, so total sample resolution is derived as:
    /// chunkCount * (samplesPerChunk - 1) + 1.
    /// </summary>
    public sealed class ChunkedContinuousHeightmapDescriptor
    {
        public ChunkedContinuousHeightmapDescriptor(
            WorldAabbCm bounds,
            int chunkColumns,
            int chunkRows,
            int samplesPerChunkColumn,
            int samplesPerChunkRow,
            ContinuousHeightmapLayerDefinition[] layers,
            ContinuousHeightmapStorageLayout storageLayout = ContinuousHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters,
            int defaultLayerIndex = 0,
            ContinuousHeightmapInterpolationMode interpolationMode = ContinuousHeightmapInterpolationMode.BilinearHeightfield,
            ContinuousHeightSampleScale? sampleScale = null)
        {
            if (chunkColumns <= 0) throw new ArgumentOutOfRangeException(nameof(chunkColumns));
            if (chunkRows <= 0) throw new ArgumentOutOfRangeException(nameof(chunkRows));
            if (samplesPerChunkColumn < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChunkColumn));
            if (samplesPerChunkRow < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChunkRow));
            if (layers == null || layers.Length == 0) throw new ArgumentException("At least one visual heightmap layer is required.", nameof(layers));
            if (storageLayout == ContinuousHeightmapStorageLayout.None) throw new ArgumentOutOfRangeException(nameof(storageLayout));
            if ((uint)defaultLayerIndex >= (uint)layers.Length) throw new ArgumentOutOfRangeException(nameof(defaultLayerIndex));
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
            if (bounds.Width % chunkColumns != 0) throw new ArgumentException("Chunked visual heightmap bounds width must be divisible by chunk column count.", nameof(bounds));
            if (bounds.Height % chunkRows != 0) throw new ArgumentException("Chunked visual heightmap bounds height must be divisible by chunk row count.", nameof(bounds));
            ContinuousHeightSampleScale resolvedScale = sampleScale ?? ContinuousHeightSampleScale.IdentityCentimeters;
            resolvedScale.Validate();

            int samplesPerLayerPerChunk = checked(samplesPerChunkColumn * samplesPerChunkRow);
            for (int i = 0; i < layers.Length; i++)
            {
                ContinuousHeightmapLayerDefinition layer = layers[i];
                if (layer.SampleOffset < 0 ||
                    layer.SampleCount != samplesPerLayerPerChunk ||
                    layer.SampleOffset + layer.SampleCount > checked(samplesPerLayerPerChunk * layers.Length))
                {
                    throw new ArgumentException($"Layer {i} does not match the declared chunked visual heightmap layout.", nameof(layers));
                }
            }

            Bounds = bounds;
            ChunkColumns = chunkColumns;
            ChunkRows = chunkRows;
            SamplesPerChunkColumn = samplesPerChunkColumn;
            SamplesPerChunkRow = samplesPerChunkRow;
            Layers = layers;
            StorageLayout = storageLayout;
            DefaultLayerIndex = defaultLayerIndex;
            InterpolationMode = interpolationMode;
            SampleScale = resolvedScale;
        }

        public WorldAabbCm Bounds { get; }

        public int ChunkColumns { get; }

        public int ChunkRows { get; }

        public int SamplesPerChunkColumn { get; }

        public int SamplesPerChunkRow { get; }

        public ContinuousHeightmapLayerDefinition[] Layers { get; }

        public ContinuousHeightmapStorageLayout StorageLayout { get; }

        public int DefaultLayerIndex { get; }

        public ContinuousHeightmapInterpolationMode InterpolationMode { get; }

        public ContinuousHeightSampleScale SampleScale { get; }

        public int SamplesPerLayerPerChunk => checked(SamplesPerChunkColumn * SamplesPerChunkRow);

        public int SamplesPerChunk => checked(SamplesPerLayerPerChunk * Layers.Length);

        public int GlobalSampleColumns => checked(ChunkColumns * (SamplesPerChunkColumn - 1) + 1);

        public int GlobalSampleRows => checked(ChunkRows * (SamplesPerChunkRow - 1) + 1);

        public int ChunkWorldWidthCm => Bounds.Width / ChunkColumns;

        public int ChunkWorldHeightCm => Bounds.Height / ChunkRows;

        public static ChunkedContinuousHeightmapDescriptor CreateSingleLayer(
            WorldAabbCm bounds,
            int chunkColumns,
            int chunkRows,
            int samplesPerChunkColumn,
            int samplesPerChunkRow,
            string layerName = "base",
            ContinuousHeightmapInterpolationMode interpolationMode = ContinuousHeightmapInterpolationMode.BilinearHeightfield,
            ContinuousHeightmapStorageLayout storageLayout = ContinuousHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters,
            ContinuousHeightSampleScale? sampleScale = null)
        {
            int samplesPerLayer = checked(samplesPerChunkColumn * samplesPerChunkRow);
            return new ChunkedContinuousHeightmapDescriptor(
                bounds,
                chunkColumns,
                chunkRows,
                samplesPerChunkColumn,
                samplesPerChunkRow,
                new[]
                {
                    new ContinuousHeightmapLayerDefinition(
                        layerId: 0,
                        name: layerName,
                        sampleOffset: 0,
                        sampleCount: samplesPerLayer),
                },
                storageLayout: storageLayout,
                interpolationMode: interpolationMode,
                sampleScale: sampleScale);
        }
    }
}
