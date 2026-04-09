using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Global descriptor for a chunked visual heightmap.
    /// Adjacent chunks share border samples, so total sample resolution is derived as:
    /// chunkCount * (samplesPerChunk - 1) + 1.
    /// </summary>
    public sealed class ChunkedVisualHeightmapDescriptor
    {
        public ChunkedVisualHeightmapDescriptor(
            WorldAabbCm bounds,
            int chunkColumns,
            int chunkRows,
            int samplesPerChunkColumn,
            int samplesPerChunkRow,
            VisualHeightmapLayerDefinition[] layers,
            VisualHeightmapStorageLayout storageLayout = VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters,
            int defaultLayerIndex = 0,
            VisualHeightmapInterpolationMode interpolationMode = VisualHeightmapInterpolationMode.BilinearHeightfield,
            VisualHeightSampleScale? sampleScale = null)
        {
            if (chunkColumns <= 0) throw new ArgumentOutOfRangeException(nameof(chunkColumns));
            if (chunkRows <= 0) throw new ArgumentOutOfRangeException(nameof(chunkRows));
            if (samplesPerChunkColumn < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChunkColumn));
            if (samplesPerChunkRow < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerChunkRow));
            if (layers == null || layers.Length == 0) throw new ArgumentException("At least one visual heightmap layer is required.", nameof(layers));
            if (storageLayout == VisualHeightmapStorageLayout.None) throw new ArgumentOutOfRangeException(nameof(storageLayout));
            if ((uint)defaultLayerIndex >= (uint)layers.Length) throw new ArgumentOutOfRangeException(nameof(defaultLayerIndex));
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
            if (bounds.Width % chunkColumns != 0) throw new ArgumentException("Chunked visual heightmap bounds width must be divisible by chunk column count.", nameof(bounds));
            if (bounds.Height % chunkRows != 0) throw new ArgumentException("Chunked visual heightmap bounds height must be divisible by chunk row count.", nameof(bounds));
            VisualHeightSampleScale resolvedScale = sampleScale ?? VisualHeightSampleScale.IdentityCentimeters;
            resolvedScale.Validate();

            int samplesPerLayerPerChunk = checked(samplesPerChunkColumn * samplesPerChunkRow);
            for (int i = 0; i < layers.Length; i++)
            {
                VisualHeightmapLayerDefinition layer = layers[i];
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

        public VisualHeightmapLayerDefinition[] Layers { get; }

        public VisualHeightmapStorageLayout StorageLayout { get; }

        public int DefaultLayerIndex { get; }

        public VisualHeightmapInterpolationMode InterpolationMode { get; }

        public VisualHeightSampleScale SampleScale { get; }

        public int SamplesPerLayerPerChunk => checked(SamplesPerChunkColumn * SamplesPerChunkRow);

        public int SamplesPerChunk => checked(SamplesPerLayerPerChunk * Layers.Length);

        public int GlobalSampleColumns => checked(ChunkColumns * (SamplesPerChunkColumn - 1) + 1);

        public int GlobalSampleRows => checked(ChunkRows * (SamplesPerChunkRow - 1) + 1);

        public int ChunkWorldWidthCm => Bounds.Width / ChunkColumns;

        public int ChunkWorldHeightCm => Bounds.Height / ChunkRows;

        public static ChunkedVisualHeightmapDescriptor CreateSingleLayer(
            WorldAabbCm bounds,
            int chunkColumns,
            int chunkRows,
            int samplesPerChunkColumn,
            int samplesPerChunkRow,
            string layerName = "base",
            VisualHeightmapInterpolationMode interpolationMode = VisualHeightmapInterpolationMode.BilinearHeightfield,
            VisualHeightmapStorageLayout storageLayout = VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters,
            VisualHeightSampleScale? sampleScale = null)
        {
            int samplesPerLayer = checked(samplesPerChunkColumn * samplesPerChunkRow);
            return new ChunkedVisualHeightmapDescriptor(
                bounds,
                chunkColumns,
                chunkRows,
                samplesPerChunkColumn,
                samplesPerChunkRow,
                new[]
                {
                    new VisualHeightmapLayerDefinition(
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
