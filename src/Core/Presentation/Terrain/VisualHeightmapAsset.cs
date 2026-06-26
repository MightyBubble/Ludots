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
            int defaultLayerIndex = 0,
            VisualHeightmapInterpolationMode interpolationMode = VisualHeightmapInterpolationMode.BilinearHeightfield,
            VisualHeightmapMipLevel[]? mipLevels = null)
        {
            if (sampleColumns <= 0) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));
            if (layers == null || layers.Length == 0) throw new ArgumentException("At least one visual heightmap layer is required.", nameof(layers));
            if (storageLayout == VisualHeightmapStorageLayout.None) throw new ArgumentOutOfRangeException(nameof(storageLayout));
            if ((uint)defaultLayerIndex >= (uint)layers.Length) throw new ArgumentOutOfRangeException(nameof(defaultLayerIndex));
            EnsureLayoutUsesInt16(storageLayout, nameof(storageLayout));

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
            HeightSamplesRaw = Array.Empty<ushort>();
            Layers = layers;
            StorageLayout = storageLayout;
            DefaultLayerIndex = defaultLayerIndex;
            InterpolationMode = interpolationMode;
            SampleScale = VisualHeightSampleScale.IdentityCentimeters;
            MipLevels = ValidateMipLevels(mipLevels, layers.Length, usesRawUInt16Samples: false);
        }

        public VisualHeightmapAsset(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            ushort[] heightSamplesRaw,
            VisualHeightmapLayerDefinition[] layers,
            VisualHeightSampleScale sampleScale,
            VisualHeightmapStorageLayout storageLayout = VisualHeightmapStorageLayout.RowMajorUInt16Scaled,
            int defaultLayerIndex = 0,
            VisualHeightmapInterpolationMode interpolationMode = VisualHeightmapInterpolationMode.BilinearHeightfield,
            VisualHeightmapMipLevel[]? mipLevels = null)
        {
            if (sampleColumns <= 0) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (heightSamplesRaw == null) throw new ArgumentNullException(nameof(heightSamplesRaw));
            if (layers == null || layers.Length == 0) throw new ArgumentException("At least one visual heightmap layer is required.", nameof(layers));
            if (storageLayout == VisualHeightmapStorageLayout.None) throw new ArgumentOutOfRangeException(nameof(storageLayout));
            if ((uint)defaultLayerIndex >= (uint)layers.Length) throw new ArgumentOutOfRangeException(nameof(defaultLayerIndex));
            EnsureLayoutUsesUInt16(storageLayout, nameof(storageLayout));
            sampleScale.Validate();

            int samplesPerLayer = checked(sampleColumns * sampleRows);
            for (int i = 0; i < layers.Length; i++)
            {
                VisualHeightmapLayerDefinition layer = layers[i];
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
            MipLevels = ValidateMipLevels(mipLevels, layers.Length, usesRawUInt16Samples: true);
        }

        public WorldAabbCm Bounds { get; }

        public int SampleColumns { get; }

        public int SampleRows { get; }

        public short[] HeightSamplesCm { get; }

        public ushort[] HeightSamplesRaw { get; }

        public VisualHeightmapLayerDefinition[] Layers { get; }

        public VisualHeightmapStorageLayout StorageLayout { get; }

        public int DefaultLayerIndex { get; }

        public VisualHeightmapInterpolationMode InterpolationMode { get; }

        public VisualHeightSampleScale SampleScale { get; }

        public int SamplesPerLayer => checked(SampleColumns * SampleRows);

        public bool UsesRawUInt16Samples => HeightSamplesRaw.Length > 0;

        public VisualHeightmapMipLevel[] MipLevels { get; }

        public static VisualHeightmapAsset CreateSingleLayer(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            short[] heightSamplesCm,
            string layerName = "base",
            VisualHeightmapInterpolationMode interpolationMode = VisualHeightmapInterpolationMode.BilinearHeightfield)
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
                },
                interpolationMode: interpolationMode);
        }

        public static VisualHeightmapAsset CreateSingleLayerFromRawUInt16(
            WorldAabbCm bounds,
            int sampleColumns,
            int sampleRows,
            ushort[] heightSamplesRaw,
            VisualHeightSampleScale sampleScale,
            string layerName = "base",
            VisualHeightmapInterpolationMode interpolationMode = VisualHeightmapInterpolationMode.BilinearHeightfield)
        {
            if (heightSamplesRaw == null) throw new ArgumentNullException(nameof(heightSamplesRaw));

            return new VisualHeightmapAsset(
                bounds,
                sampleColumns,
                sampleRows,
                heightSamplesRaw,
                new[]
                {
                    new VisualHeightmapLayerDefinition(
                        layerId: 0,
                        name: layerName,
                        sampleOffset: 0,
                        sampleCount: checked(sampleColumns * sampleRows)),
                },
                sampleScale,
                interpolationMode: interpolationMode);
        }

        private static void EnsureLayoutUsesInt16(VisualHeightmapStorageLayout storageLayout, string paramName)
        {
            if (storageLayout != VisualHeightmapStorageLayout.RowMajorInt16Centimeters &&
                storageLayout != VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters)
            {
                throw new ArgumentOutOfRangeException(paramName, "This visual heightmap constructor requires an int16 storage layout.");
            }
        }

        private static void EnsureLayoutUsesUInt16(VisualHeightmapStorageLayout storageLayout, string paramName)
        {
            if (storageLayout != VisualHeightmapStorageLayout.RowMajorUInt16Scaled &&
                storageLayout != VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled)
            {
                throw new ArgumentOutOfRangeException(paramName, "This visual heightmap constructor requires a uint16 scaled storage layout.");
            }
        }

        private static VisualHeightmapMipLevel[] ValidateMipLevels(
            VisualHeightmapMipLevel[]? mipLevels,
            int layerCount,
            bool usesRawUInt16Samples)
        {
            if (mipLevels == null || mipLevels.Length == 0)
            {
                return Array.Empty<VisualHeightmapMipLevel>();
            }

            for (int i = 0; i < mipLevels.Length; i++)
            {
                VisualHeightmapMipLevel mip = mipLevels[i]
                    ?? throw new ArgumentException("Visual heightmap mip levels must not contain null entries.", nameof(mipLevels));
                if (mip.Level != i + 1)
                {
                    throw new ArgumentException("Visual heightmap mip levels must be contiguous from level 1.", nameof(mipLevels));
                }

                if (mip.UsesRawUInt16Samples != usesRawUInt16Samples)
                {
                    throw new ArgumentException("Visual heightmap mip encoding must match the base asset encoding.", nameof(mipLevels));
                }

                if (mip.Layers.Length != layerCount)
                {
                    throw new ArgumentException("Visual heightmap mip layer count must match the base asset layer count.", nameof(mipLevels));
                }
            }

            return mipLevels;
        }
    }
}
