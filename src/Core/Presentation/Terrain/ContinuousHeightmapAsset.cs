using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    public sealed class ContinuousHeightmapAsset
    {
        public ContinuousHeightmapAsset(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            short[] heightSamplesCm,
            ContinuousHeightmapLayerDefinition[] layers,
            ContinuousHeightmapStorageLayout storageLayout = ContinuousHeightmapStorageLayout.RowMajorInt16Centimeters,
            int defaultLayerIndex = 0,
            ContinuousHeightmapInterpolationMode interpolationMode = ContinuousHeightmapInterpolationMode.BilinearHeightfield)
        {
            if (sampleColumns <= 0) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));
            if (layers == null || layers.Length == 0) throw new ArgumentException("At least one visual heightmap layer is required.", nameof(layers));
            if (storageLayout == ContinuousHeightmapStorageLayout.None) throw new ArgumentOutOfRangeException(nameof(storageLayout));
            if ((uint)defaultLayerIndex >= (uint)layers.Length) throw new ArgumentOutOfRangeException(nameof(defaultLayerIndex));
            EnsureLayoutUsesInt16(storageLayout, nameof(storageLayout));

            int samplesPerLayer = checked(sampleColumns * sampleRows);
            for (int i = 0; i < layers.Length; i++)
            {
                ContinuousHeightmapLayerDefinition layer = layers[i];
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
            HeightSamplesRaw = Array.Empty<ushort>();
            Layers = layers;
            StorageLayout = storageLayout;
            DefaultLayerIndex = defaultLayerIndex;
            InterpolationMode = interpolationMode;
            SampleScale = ContinuousHeightSampleScale.IdentityCentimeters;
        }

        public ContinuousHeightmapAsset(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            ushort[] heightSamplesRaw,
            ContinuousHeightmapLayerDefinition[] layers,
            ContinuousHeightSampleScale sampleScale,
            ContinuousHeightmapStorageLayout storageLayout = ContinuousHeightmapStorageLayout.RowMajorUInt16Scaled,
            int defaultLayerIndex = 0,
            ContinuousHeightmapInterpolationMode interpolationMode = ContinuousHeightmapInterpolationMode.BilinearHeightfield)
        {
            if (sampleColumns <= 0) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (heightSamplesRaw == null) throw new ArgumentNullException(nameof(heightSamplesRaw));
            if (layers == null || layers.Length == 0) throw new ArgumentException("At least one visual heightmap layer is required.", nameof(layers));
            if (storageLayout == ContinuousHeightmapStorageLayout.None) throw new ArgumentOutOfRangeException(nameof(storageLayout));
            if ((uint)defaultLayerIndex >= (uint)layers.Length) throw new ArgumentOutOfRangeException(nameof(defaultLayerIndex));
            EnsureLayoutUsesUInt16(storageLayout, nameof(storageLayout));
            sampleScale.Validate();

            int samplesPerLayer = checked(sampleColumns * sampleRows);
            for (int i = 0; i < layers.Length; i++)
            {
                ContinuousHeightmapLayerDefinition layer = layers[i];
                if (layer.SampleOffset < 0 ||
                    layer.SampleCount != samplesPerLayer ||
                    layer.SampleOffset + layer.SampleCount > heightSamplesRaw.Length)
                {
                    throw new ArgumentException($"Layer {i} does not match the declared visual heightmap layout.", nameof(layers));
                }
            }

            Bounds = bounds;
            SampleColumns = sampleColumns;
            SampleRows = sampleRows;
            HeightSamplesCm = Array.Empty<short>();
            HeightSamplesRaw = heightSamplesRaw;
            Layers = layers;
            StorageLayout = storageLayout;
            DefaultLayerIndex = defaultLayerIndex;
            InterpolationMode = interpolationMode;
            SampleScale = sampleScale;
        }

        public WorldAabbCm Bounds { get; }

        public int SampleColumns { get; }

        public int SampleRows { get; }

        public short[] HeightSamplesCm { get; }

        public ushort[] HeightSamplesRaw { get; }

        public ContinuousHeightmapLayerDefinition[] Layers { get; }

        public ContinuousHeightmapStorageLayout StorageLayout { get; }

        public int DefaultLayerIndex { get; }

        public ContinuousHeightmapInterpolationMode InterpolationMode { get; }

        public ContinuousHeightSampleScale SampleScale { get; }

        public int SamplesPerLayer => checked(SampleColumns * SampleRows);

        public bool UsesRawUInt16Samples => HeightSamplesRaw.Length > 0;

        public static ContinuousHeightmapAsset CreateSingleLayer(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            short[] heightSamplesCm,
            string layerName = "base",
            ContinuousHeightmapInterpolationMode interpolationMode = ContinuousHeightmapInterpolationMode.BilinearHeightfield)
        {
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));

            return new ContinuousHeightmapAsset(
                bounds,
                sampleColumns,
                sampleRows,
                heightSamplesCm,
                new[]
                {
                    new ContinuousHeightmapLayerDefinition(
                        layerId: 0,
                        name: layerName,
                        sampleOffset: 0,
                        sampleCount: checked(sampleColumns * sampleRows)),
                },
                interpolationMode: interpolationMode);
        }

        public static ContinuousHeightmapAsset CreateSingleLayerFromRawUInt16(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            ushort[] heightSamplesRaw,
            ContinuousHeightSampleScale sampleScale,
            string layerName = "base",
            ContinuousHeightmapInterpolationMode interpolationMode = ContinuousHeightmapInterpolationMode.BilinearHeightfield)
        {
            if (heightSamplesRaw == null) throw new ArgumentNullException(nameof(heightSamplesRaw));

            return new ContinuousHeightmapAsset(
                bounds,
                sampleColumns,
                sampleRows,
                heightSamplesRaw,
                new[]
                {
                    new ContinuousHeightmapLayerDefinition(
                        layerId: 0,
                        name: layerName,
                        sampleOffset: 0,
                        sampleCount: checked(sampleColumns * sampleRows)),
                },
                sampleScale,
                interpolationMode: interpolationMode);
        }

        private static void EnsureLayoutUsesInt16(ContinuousHeightmapStorageLayout storageLayout, string paramName)
        {
            if (storageLayout != ContinuousHeightmapStorageLayout.RowMajorInt16Centimeters &&
                storageLayout != ContinuousHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters)
            {
                throw new ArgumentOutOfRangeException(paramName, "This visual heightmap constructor requires an int16 storage layout.");
            }
        }

        private static void EnsureLayoutUsesUInt16(ContinuousHeightmapStorageLayout storageLayout, string paramName)
        {
            if (storageLayout != ContinuousHeightmapStorageLayout.RowMajorUInt16Scaled &&
                storageLayout != ContinuousHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled)
            {
                throw new ArgumentOutOfRangeException(paramName, "This visual heightmap constructor requires a uint16 scaled storage layout.");
            }
        }
    }
}
