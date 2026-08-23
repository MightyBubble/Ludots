using System;
using System.IO;
using System.Text;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    public static class VisualHeightmapBinary
    {
        private const string Magic = "VHTM";
        private const int Version = 2;

        public static VisualHeightmapAsset Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            string magic = Encoding.ASCII.GetString(ReadExact(reader, 4));
            if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid visual heightmap binary magic.");
            }

            int version = reader.ReadInt32();
            if (version != 1 && version != Version)
            {
                throw new InvalidDataException($"Unsupported visual heightmap binary version: {version}.");
            }

            var bounds = new WorldAabbCm(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32());

            int sampleColumns = reader.ReadInt32();
            int sampleRows = reader.ReadInt32();
            var storageLayout = (VisualHeightmapStorageLayout)reader.ReadInt32();
            int defaultLayerIndex = reader.ReadInt32();
            VisualHeightmapInterpolationMode interpolationMode = version >= 2
                ? (VisualHeightmapInterpolationMode)reader.ReadInt32()
                : VisualHeightmapInterpolationMode.BilinearHeightfield;
            VisualHeightSampleScale sampleScale = version >= 2
                ? new VisualHeightSampleScale(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32())
                : VisualHeightSampleScale.IdentityCentimeters;

            int layerCount = reader.ReadInt32();
            if (layerCount <= 0)
            {
                throw new InvalidDataException("Visual heightmap binary must contain at least one layer.");
            }

            var layers = new VisualHeightmapLayerDefinition[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                layers[i] = new VisualHeightmapLayerDefinition(
                    reader.ReadInt32(),
                    reader.ReadString(),
                    reader.ReadInt32(),
                    reader.ReadInt32());
            }

            int sampleCount = reader.ReadInt32();
            if (sampleCount < 0)
            {
                throw new InvalidDataException("Visual heightmap binary sample count cannot be negative.");
            }

            bool usesRawUInt16 = storageLayout == VisualHeightmapStorageLayout.RowMajorUInt16Scaled ||
                                 storageLayout == VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled;
            if (usesRawUInt16)
            {
                byte[] rawSamples = ReadExact(reader, checked(sampleCount * sizeof(ushort)));
                var heightSamplesRaw = new ushort[sampleCount];
                Buffer.BlockCopy(rawSamples, 0, heightSamplesRaw, 0, rawSamples.Length);

                return new VisualHeightmapAsset(
                    bounds,
                    sampleColumns,
                    sampleRows,
                    heightSamplesRaw,
                    layers,
                    sampleScale,
                    storageLayout,
                    defaultLayerIndex,
                    interpolationMode);
            }

            byte[] rawCmSamples = ReadExact(reader, checked(sampleCount * sizeof(short)));
            var heightSamplesCm = new short[sampleCount];
            Buffer.BlockCopy(rawCmSamples, 0, heightSamplesCm, 0, rawCmSamples.Length);

            return new VisualHeightmapAsset(
                bounds,
                sampleColumns,
                sampleRows,
                heightSamplesCm,
                layers,
                storageLayout,
                defaultLayerIndex,
                interpolationMode);
        }

        public static void Write(Stream stream, VisualHeightmapAsset asset)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (asset == null) throw new ArgumentNullException(nameof(asset));

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes(Magic));
            writer.Write(Version);
            writer.Write(asset.Bounds.X);
            writer.Write(asset.Bounds.Y);
            writer.Write(asset.Bounds.Width);
            writer.Write(asset.Bounds.Height);
            writer.Write(asset.SampleColumns);
            writer.Write(asset.SampleRows);
            writer.Write((int)asset.StorageLayout);
            writer.Write(asset.DefaultLayerIndex);
            writer.Write((int)asset.InterpolationMode);
            writer.Write(asset.SampleScale.OffsetCm);
            writer.Write(asset.SampleScale.UnitsPerSampleNumeratorCm);
            writer.Write(asset.SampleScale.UnitsPerSampleDenominator);
            writer.Write(asset.Layers.Length);

            for (int i = 0; i < asset.Layers.Length; i++)
            {
                VisualHeightmapLayerDefinition layer = asset.Layers[i];
                writer.Write(layer.LayerId);
                writer.Write(layer.Name ?? string.Empty);
                writer.Write(layer.SampleOffset);
                writer.Write(layer.SampleCount);
            }

            if (asset.UsesRawUInt16Samples)
            {
                writer.Write(asset.HeightSamplesRaw.Length);
                byte[] rawSamples = new byte[checked(asset.HeightSamplesRaw.Length * sizeof(ushort))];
                Buffer.BlockCopy(asset.HeightSamplesRaw, 0, rawSamples, 0, rawSamples.Length);
                writer.Write(rawSamples);
            }
            else
            {
                writer.Write(asset.HeightSamplesCm.Length);
                byte[] rawSamples = new byte[checked(asset.HeightSamplesCm.Length * sizeof(short))];
                Buffer.BlockCopy(asset.HeightSamplesCm, 0, rawSamples, 0, rawSamples.Length);
                writer.Write(rawSamples);
            }
        }

        private static byte[] ReadExact(BinaryReader reader, int byteCount)
        {
            byte[] buffer = reader.ReadBytes(byteCount);
            if (buffer.Length != byteCount)
            {
                throw new EndOfStreamException();
            }

            return buffer;
        }
    }
}
