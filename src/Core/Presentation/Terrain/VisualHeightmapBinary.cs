using System;
using System.IO;
using System.Text;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    public static class VisualHeightmapBinary
    {
        private const string Magic = "VHTM";
        private const int Version = 1;

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
            if (version != Version)
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

            byte[] rawSamples = ReadExact(reader, checked(sampleCount * sizeof(short)));
            var heightSamplesCm = new short[sampleCount];
            Buffer.BlockCopy(rawSamples, 0, heightSamplesCm, 0, rawSamples.Length);

            return new VisualHeightmapAsset(
                bounds,
                sampleColumns,
                sampleRows,
                heightSamplesCm,
                layers,
                storageLayout,
                defaultLayerIndex);
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
            writer.Write(asset.Layers.Length);

            for (int i = 0; i < asset.Layers.Length; i++)
            {
                VisualHeightmapLayerDefinition layer = asset.Layers[i];
                writer.Write(layer.LayerId);
                writer.Write(layer.Name ?? string.Empty);
                writer.Write(layer.SampleOffset);
                writer.Write(layer.SampleCount);
            }

            writer.Write(asset.HeightSamplesCm.Length);
            byte[] rawSamples = new byte[checked(asset.HeightSamplesCm.Length * sizeof(short))];
            Buffer.BlockCopy(asset.HeightSamplesCm, 0, rawSamples, 0, rawSamples.Length);
            writer.Write(rawSamples);
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
