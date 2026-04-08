using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    public sealed class VisualHeightmapAsset
    {
        public VisualHeightmapAsset(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            short[] heightSamplesCm,
            VisualHeightmapLayerDefinition[] layers,
            VisualHeightmapStorageLayout storageLayout = VisualHeightmapStorageLayout.RowMajorInt16Centimeters,
            int defaultLayerIndex = 0)
        {
            if (sampleColumns <= 0) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));
            if (layers == null || layers.Length == 0) throw new ArgumentException("At least one visual heightmap layer is required.", nameof(layers));
            if (storageLayout == VisualHeightmapStorageLayout.None) throw new ArgumentOutOfRangeException(nameof(storageLayout));
            if ((uint)defaultLayerIndex >= (uint)layers.Length) throw new ArgumentOutOfRangeException(nameof(defaultLayerIndex));

            int samplesPerLayer = checked(sampleColumns * sampleRows);
            for (int i = 0; i < layers.Length; i++)
            {
                VisualHeightmapLayerDefinition layer = layers[i];
                if (layer.SampleOffset < 0 ||
                    layer.SampleCount != samplesPerLayer ||
                    layer.SampleOffset + layer.SampleCount > heightSamplesCm.Length)
                {
                    throw new ArgumentException($"Layer {i} does not match the declared visual heightmap layout.", nameof(layers));
                }
            }

            Bounds = bounds;
            SampleColumns = sampleColumns;
            SampleRows = sampleRows;
            HeightSamplesCm = heightSamplesCm;
            Layers = layers;
            StorageLayout = storageLayout;
            DefaultLayerIndex = defaultLayerIndex;
        }

        public WorldAabbCm Bounds { get; }

        public int SampleColumns { get; }

        public int SampleRows { get; }

        public short[] HeightSamplesCm { get; }

        public VisualHeightmapLayerDefinition[] Layers { get; }

        public VisualHeightmapStorageLayout StorageLayout { get; }

        public int DefaultLayerIndex { get; }

        public int SamplesPerLayer => checked(SampleColumns * SampleRows);

        public static VisualHeightmapAsset CreateSingleLayer(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            short[] heightSamplesCm,
            string layerName = "base")
        {
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));

            return new VisualHeightmapAsset(
                bounds,
                sampleColumns,
                sampleRows,
                heightSamplesCm,
                new[]
                {
                    new VisualHeightmapLayerDefinition(
                        layerId: 0,
                        name: layerName,
                        sampleOffset: 0,
                        sampleCount: checked(sampleColumns * sampleRows)),
                });
        }
    }
}
