using System;

namespace Ludots.Core.Presentation.Terrain
{
    public sealed class VisualHeightmapMipLevel
    {
        public VisualHeightmapMipLevel(
            int level,
            int sampleColumns,
            int sampleRows,
            short[] heightSamplesCm,
            VisualHeightmapLayerDefinition[] layers)
        {
            if (level <= 0) throw new ArgumentOutOfRangeException(nameof(level));
            if (sampleColumns < 2) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows < 2) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (heightSamplesCm == null) throw new ArgumentNullException(nameof(heightSamplesCm));
            ValidateLayers(sampleColumns, sampleRows, layers, heightSamplesCm.Length, nameof(layers));

            Level = level;
            SampleColumns = sampleColumns;
            SampleRows = sampleRows;
            HeightSamplesCm = heightSamplesCm;
            HeightSamplesRaw = Array.Empty<ushort>();
            Layers = layers;
        }

        public VisualHeightmapMipLevel(
            int level,
            int sampleColumns,
            int sampleRows,
            ushort[] heightSamplesRaw,
            VisualHeightmapLayerDefinition[] layers)
        {
            if (level <= 0) throw new ArgumentOutOfRangeException(nameof(level));
            if (sampleColumns < 2) throw new ArgumentOutOfRangeException(nameof(sampleColumns));
            if (sampleRows < 2) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            if (heightSamplesRaw == null) throw new ArgumentNullException(nameof(heightSamplesRaw));
            ValidateLayers(sampleColumns, sampleRows, layers, heightSamplesRaw.Length, nameof(layers));

            Level = level;
            SampleColumns = sampleColumns;
            SampleRows = sampleRows;
            HeightSamplesCm = Array.Empty<short>();
            HeightSamplesRaw = heightSamplesRaw;
            Layers = layers;
        }

        public int Level { get; }

        public int SampleColumns { get; }

        public int SampleRows { get; }

        public short[] HeightSamplesCm { get; }

        public ushort[] HeightSamplesRaw { get; }

        public VisualHeightmapLayerDefinition[] Layers { get; }

        public int SamplesPerLayer => checked(SampleColumns * SampleRows);

        public bool UsesRawUInt16Samples => HeightSamplesRaw.Length > 0;

        private static void ValidateLayers(
            int sampleColumns,
            int sampleRows,
            VisualHeightmapLayerDefinition[] layers,
            int samplePayloadLength,
            string paramName)
        {
            if (layers == null || layers.Length == 0)
            {
                throw new ArgumentException("At least one visual heightmap mip layer is required.", paramName);
            }

            int samplesPerLayer = checked(sampleColumns * sampleRows);
            for (int i = 0; i < layers.Length; i++)
            {
                VisualHeightmapLayerDefinition layer = layers[i];
                if (layer.SampleOffset < 0 ||
                    layer.SampleCount != samplesPerLayer ||
                    layer.SampleOffset + layer.SampleCount > samplePayloadLength)
                {
                    throw new ArgumentException($"Mip layer {i} does not match the declared visual heightmap mip layout.", paramName);
                }
            }
        }
    }
}
