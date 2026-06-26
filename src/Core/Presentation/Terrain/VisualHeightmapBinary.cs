using System;
using System.IO;
using System.Text;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    public static class VisualHeightmapBinary
    {
        private const string Magic = "VHTM";
        public const int FormatVersion = 3;
        private const int ChunkSampleColumns = 64;
        private const int ChunkSampleRows = 64;

        private enum SamplePayloadKind : int
        {
            ChunkedInt16Centimeters = 1,
            ChunkedUInt16Scaled = 2
        }

        private enum ChunkPayloadKind : byte
        {
            Flat = 0,
            Raw = 1
        }

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
            if (version != FormatVersion)
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
            var interpolationMode = (VisualHeightmapInterpolationMode)reader.ReadInt32();
            var sampleScale = new VisualHeightSampleScale(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32());

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

            var payloadKind = (SamplePayloadKind)reader.ReadInt32();
            int sampleCount = reader.ReadInt32();
            int samplesPerLayer = checked(sampleColumns * sampleRows);
            int expectedSampleCount = ComputeExpectedSampleCount(layers, samplesPerLayer);
            if (sampleCount != expectedSampleCount)
            {
                throw new InvalidDataException($"Visual heightmap sample count mismatch. Expected {expectedSampleCount}, actual {sampleCount}.");
            }

            bool usesRawUInt16 = storageLayout == VisualHeightmapStorageLayout.RowMajorUInt16Scaled ||
                                 storageLayout == VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled;
            if (usesRawUInt16)
            {
                if (payloadKind != SamplePayloadKind.ChunkedUInt16Scaled)
                {
                    throw new InvalidDataException("Visual heightmap payload kind does not match uint16-scaled storage layout.");
                }

                ushort[] heightSamplesRaw = ReadChunkedUInt16Payload(reader, sampleColumns, sampleRows, layers, sampleCount);
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

            if (payloadKind != SamplePayloadKind.ChunkedInt16Centimeters)
            {
                throw new InvalidDataException("Visual heightmap payload kind does not match int16-centimeter storage layout.");
            }

            short[] heightSamplesCm = ReadChunkedInt16Payload(reader, sampleColumns, sampleRows, layers, sampleCount);
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
            writer.Write(FormatVersion);
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
                writer.Write((int)SamplePayloadKind.ChunkedUInt16Scaled);
                writer.Write(asset.HeightSamplesRaw.Length);
                WriteChunkedUInt16Payload(writer, asset.HeightSamplesRaw, asset.SampleColumns, asset.SampleRows, asset.Layers);
                return;
            }

            writer.Write((int)SamplePayloadKind.ChunkedInt16Centimeters);
            writer.Write(asset.HeightSamplesCm.Length);
            WriteChunkedInt16Payload(writer, asset.HeightSamplesCm, asset.SampleColumns, asset.SampleRows, asset.Layers);
        }

        public static bool TryGetFlatHeightCm(VisualHeightmapAsset asset, out float heightCm)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));

            int sampleCount = checked(asset.SampleColumns * asset.SampleRows * asset.Layers.Length);
            if (asset.UsesRawUInt16Samples)
            {
                if (asset.HeightSamplesRaw.Length != sampleCount || sampleCount == 0)
                {
                    heightCm = default;
                    return false;
                }

                ushort first = asset.HeightSamplesRaw[0];
                for (int i = 1; i < asset.HeightSamplesRaw.Length; i++)
                {
                    if (asset.HeightSamplesRaw[i] != first)
                    {
                        heightCm = default;
                        return false;
                    }
                }

                heightCm = asset.SampleScale.Decode(first);
                return true;
            }

            if (asset.HeightSamplesCm.Length != sampleCount || sampleCount == 0)
            {
                heightCm = default;
                return false;
            }

            short firstCm = asset.HeightSamplesCm[0];
            for (int i = 1; i < asset.HeightSamplesCm.Length; i++)
            {
                if (asset.HeightSamplesCm[i] != firstCm)
                {
                    heightCm = default;
                    return false;
                }
            }

            heightCm = firstCm;
            return true;
        }

        private static short[] ReadChunkedInt16Payload(BinaryReader reader, int sampleColumns, int sampleRows, VisualHeightmapLayerDefinition[] layers, int sampleCount)
        {
            ReadChunkHeader(reader, sampleColumns, sampleRows, out int chunkColumns, out int chunkRows, out int defaultValue, out int recordCount);
            var samples = new short[sampleCount];
            Array.Fill(samples, checked((short)defaultValue));

            for (int i = 0; i < recordCount; i++)
            {
                ReadChunkRecordHeader(reader, layers.Length, chunkColumns, chunkRows, out int layerIndex, out int chunkX, out int chunkY, out ChunkPayloadKind recordKind);
                GetChunkDimensions(sampleColumns, sampleRows, chunkX, chunkY, out int width, out int height);
                int layerOffset = layers[layerIndex].SampleOffset;

                if (recordKind == ChunkPayloadKind.Flat)
                {
                    short value = reader.ReadInt16();
                    FillChunk(samples, sampleColumns, layerOffset, chunkX, chunkY, width, height, value);
                    continue;
                }

                if (recordKind != ChunkPayloadKind.Raw)
                {
                    throw new InvalidDataException($"Unsupported visual heightmap chunk payload kind: {recordKind}.");
                }

                ReadRawChunk(samples, reader, sampleColumns, layerOffset, chunkX, chunkY, width, height);
            }

            return samples;
        }

        private static ushort[] ReadChunkedUInt16Payload(BinaryReader reader, int sampleColumns, int sampleRows, VisualHeightmapLayerDefinition[] layers, int sampleCount)
        {
            ReadChunkHeader(reader, sampleColumns, sampleRows, out int chunkColumns, out int chunkRows, out int defaultValue, out int recordCount);
            var samples = new ushort[sampleCount];
            Array.Fill(samples, checked((ushort)defaultValue));

            for (int i = 0; i < recordCount; i++)
            {
                ReadChunkRecordHeader(reader, layers.Length, chunkColumns, chunkRows, out int layerIndex, out int chunkX, out int chunkY, out ChunkPayloadKind recordKind);
                GetChunkDimensions(sampleColumns, sampleRows, chunkX, chunkY, out int width, out int height);
                int layerOffset = layers[layerIndex].SampleOffset;

                if (recordKind == ChunkPayloadKind.Flat)
                {
                    ushort value = reader.ReadUInt16();
                    FillChunk(samples, sampleColumns, layerOffset, chunkX, chunkY, width, height, value);
                    continue;
                }

                if (recordKind != ChunkPayloadKind.Raw)
                {
                    throw new InvalidDataException($"Unsupported visual heightmap chunk payload kind: {recordKind}.");
                }

                ReadRawChunk(samples, reader, sampleColumns, layerOffset, chunkX, chunkY, width, height);
            }

            return samples;
        }

        private static void WriteChunkedInt16Payload(BinaryWriter writer, short[] samples, int sampleColumns, int sampleRows, VisualHeightmapLayerDefinition[] layers)
        {
            short defaultValue = ResolveDefaultValue(samples);
            int recordCount = CountChunkRecords(samples, sampleColumns, sampleRows, layers, defaultValue);
            WriteChunkHeader(writer, sampleColumns, sampleRows, defaultValue, recordCount);
            WriteChunkRecords(writer, samples, sampleColumns, sampleRows, layers, defaultValue);
        }

        private static void WriteChunkedUInt16Payload(BinaryWriter writer, ushort[] samples, int sampleColumns, int sampleRows, VisualHeightmapLayerDefinition[] layers)
        {
            ushort defaultValue = ResolveDefaultValue(samples);
            int recordCount = CountChunkRecords(samples, sampleColumns, sampleRows, layers, defaultValue);
            WriteChunkHeader(writer, sampleColumns, sampleRows, defaultValue, recordCount);
            WriteChunkRecords(writer, samples, sampleColumns, sampleRows, layers, defaultValue);
        }

        private static void ReadChunkHeader(
            BinaryReader reader,
            int sampleColumns,
            int sampleRows,
            out int chunkColumns,
            out int chunkRows,
            out int defaultValue,
            out int recordCount)
        {
            int chunkSampleColumns = reader.ReadInt32();
            int chunkSampleRows = reader.ReadInt32();
            if (chunkSampleColumns != ChunkSampleColumns || chunkSampleRows != ChunkSampleRows)
            {
                throw new InvalidDataException("Visual heightmap chunk dimensions do not match the current format.");
            }

            chunkColumns = reader.ReadInt32();
            chunkRows = reader.ReadInt32();
            int expectedChunkColumns = DivideRoundUp(sampleColumns, ChunkSampleColumns);
            int expectedChunkRows = DivideRoundUp(sampleRows, ChunkSampleRows);
            if (chunkColumns != expectedChunkColumns || chunkRows != expectedChunkRows)
            {
                throw new InvalidDataException("Visual heightmap chunk grid does not match sample dimensions.");
            }

            defaultValue = reader.ReadInt32();
            recordCount = reader.ReadInt32();
            if (recordCount < 0)
            {
                throw new InvalidDataException("Visual heightmap chunk record count cannot be negative.");
            }
        }

        private static void WriteChunkHeader(BinaryWriter writer, int sampleColumns, int sampleRows, int defaultValue, int recordCount)
        {
            writer.Write(ChunkSampleColumns);
            writer.Write(ChunkSampleRows);
            writer.Write(DivideRoundUp(sampleColumns, ChunkSampleColumns));
            writer.Write(DivideRoundUp(sampleRows, ChunkSampleRows));
            writer.Write(defaultValue);
            writer.Write(recordCount);
        }

        private static void ReadChunkRecordHeader(
            BinaryReader reader,
            int layerCount,
            int chunkColumns,
            int chunkRows,
            out int layerIndex,
            out int chunkX,
            out int chunkY,
            out ChunkPayloadKind recordKind)
        {
            layerIndex = reader.ReadInt32();
            chunkX = reader.ReadInt32();
            chunkY = reader.ReadInt32();
            recordKind = (ChunkPayloadKind)reader.ReadByte();

            if ((uint)layerIndex >= (uint)layerCount ||
                (uint)chunkX >= (uint)chunkColumns ||
                (uint)chunkY >= (uint)chunkRows)
            {
                throw new InvalidDataException("Visual heightmap chunk record is outside the declared layout.");
            }
        }

        private static int CountChunkRecords<T>(T[] samples, int sampleColumns, int sampleRows, VisualHeightmapLayerDefinition[] layers, T defaultValue)
            where T : unmanaged, IEquatable<T>
        {
            int recordCount = 0;
            int chunkColumns = DivideRoundUp(sampleColumns, ChunkSampleColumns);
            int chunkRows = DivideRoundUp(sampleRows, ChunkSampleRows);
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                int layerOffset = layers[layerIndex].SampleOffset;
                for (int chunkY = 0; chunkY < chunkRows; chunkY++)
                {
                    for (int chunkX = 0; chunkX < chunkColumns; chunkX++)
                    {
                        GetChunkDimensions(sampleColumns, sampleRows, chunkX, chunkY, out int width, out int height);
                        if (IsChunkFlat(samples, sampleColumns, layerOffset, chunkX, chunkY, width, height, out T value) &&
                            value.Equals(defaultValue))
                        {
                            continue;
                        }

                        recordCount++;
                    }
                }
            }

            return recordCount;
        }

        private static void WriteChunkRecords<T>(BinaryWriter writer, T[] samples, int sampleColumns, int sampleRows, VisualHeightmapLayerDefinition[] layers, T defaultValue)
            where T : unmanaged, IEquatable<T>
        {
            int chunkColumns = DivideRoundUp(sampleColumns, ChunkSampleColumns);
            int chunkRows = DivideRoundUp(sampleRows, ChunkSampleRows);
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                int layerOffset = layers[layerIndex].SampleOffset;
                for (int chunkY = 0; chunkY < chunkRows; chunkY++)
                {
                    for (int chunkX = 0; chunkX < chunkColumns; chunkX++)
                    {
                        GetChunkDimensions(sampleColumns, sampleRows, chunkX, chunkY, out int width, out int height);
                        bool flat = IsChunkFlat(samples, sampleColumns, layerOffset, chunkX, chunkY, width, height, out T value);
                        if (flat && value.Equals(defaultValue))
                        {
                            continue;
                        }

                        writer.Write(layerIndex);
                        writer.Write(chunkX);
                        writer.Write(chunkY);
                        if (flat)
                        {
                            writer.Write((byte)ChunkPayloadKind.Flat);
                            WriteValue(writer, value);
                            continue;
                        }

                        writer.Write((byte)ChunkPayloadKind.Raw);
                        WriteRawChunk(writer, samples, sampleColumns, layerOffset, chunkX, chunkY, width, height);
                    }
                }
            }
        }

        private static bool IsChunkFlat<T>(
            T[] samples,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int width,
            int height,
            out T value)
            where T : unmanaged, IEquatable<T>
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            value = samples[layerOffset + (startY * sampleColumns) + startX];
            for (int y = 0; y < height; y++)
            {
                int rowOffset = layerOffset + ((startY + y) * sampleColumns) + startX;
                for (int x = 0; x < width; x++)
                {
                    if (!samples[rowOffset + x].Equals(value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void GetChunkDimensions(int sampleColumns, int sampleRows, int chunkX, int chunkY, out int width, out int height)
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            width = Math.Min(ChunkSampleColumns, sampleColumns - startX);
            height = Math.Min(ChunkSampleRows, sampleRows - startY);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("Visual heightmap chunk dimensions are invalid.");
            }
        }

        private static void FillChunk<T>(
            T[] samples,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int width,
            int height,
            T value)
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = layerOffset + ((startY + y) * sampleColumns) + startX;
                for (int x = 0; x < width; x++)
                {
                    samples[rowOffset + x] = value;
                }
            }
        }

        private static void ReadRawChunk(short[] samples, BinaryReader reader, int sampleColumns, int layerOffset, int chunkX, int chunkY, int width, int height)
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = layerOffset + ((startY + y) * sampleColumns) + startX;
                for (int x = 0; x < width; x++)
                {
                    samples[rowOffset + x] = reader.ReadInt16();
                }
            }
        }

        private static void ReadRawChunk(ushort[] samples, BinaryReader reader, int sampleColumns, int layerOffset, int chunkX, int chunkY, int width, int height)
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = layerOffset + ((startY + y) * sampleColumns) + startX;
                for (int x = 0; x < width; x++)
                {
                    samples[rowOffset + x] = reader.ReadUInt16();
                }
            }
        }

        private static void WriteRawChunk<T>(
            BinaryWriter writer,
            T[] samples,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int width,
            int height)
            where T : unmanaged
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = layerOffset + ((startY + y) * sampleColumns) + startX;
                for (int x = 0; x < width; x++)
                {
                    WriteValue(writer, samples[rowOffset + x]);
                }
            }
        }

        private static T ResolveDefaultValue<T>(T[] samples)
            where T : unmanaged, IEquatable<T>
        {
            if (samples.Length == 0)
            {
                return default;
            }

            T first = samples[0];
            for (int i = 1; i < samples.Length; i++)
            {
                if (!samples[i].Equals(first))
                {
                    return default;
                }
            }

            return first;
        }

        private static int ComputeExpectedSampleCount(VisualHeightmapLayerDefinition[] layers, int samplesPerLayer)
        {
            int expected = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                VisualHeightmapLayerDefinition layer = layers[i];
                if (layer.SampleOffset < 0 || layer.SampleCount != samplesPerLayer)
                {
                    throw new InvalidDataException($"Visual heightmap layer {i} does not match the declared sample grid.");
                }

                expected = Math.Max(expected, checked(layer.SampleOffset + layer.SampleCount));
            }

            return expected;
        }

        private static void WriteValue<T>(BinaryWriter writer, T value)
            where T : unmanaged
        {
            if (typeof(T) == typeof(short))
            {
                writer.Write((short)(object)value);
                return;
            }

            if (typeof(T) == typeof(ushort))
            {
                writer.Write((ushort)(object)value);
                return;
            }

            throw new InvalidOperationException($"Unsupported visual heightmap sample type: {typeof(T).Name}.");
        }

        private static int DivideRoundUp(int value, int divisor)
            => checked((value + divisor - 1) / divisor);

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
