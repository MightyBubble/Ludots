using System;
using System.IO;
using System.Text;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    public static class VisualHeightmapBinary
    {
        private const string Magic = "VHTM";
        public const int FormatVersion = 4;
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
                VisualHeightmapMipLevel[] mipLevels = ReadMipLevels(reader, usesRawUInt16Samples: true, layers.Length);
                return new VisualHeightmapAsset(
                    bounds,
                    sampleColumns,
                    sampleRows,
                    heightSamplesRaw,
                    layers,
                    sampleScale,
                    storageLayout,
                    defaultLayerIndex,
                    interpolationMode,
                    mipLevels);
            }

            if (payloadKind != SamplePayloadKind.ChunkedInt16Centimeters)
            {
                throw new InvalidDataException("Visual heightmap payload kind does not match int16-centimeter storage layout.");
            }

            short[] heightSamplesCm = ReadChunkedInt16Payload(reader, sampleColumns, sampleRows, layers, sampleCount);
            VisualHeightmapMipLevel[] int16MipLevels = ReadMipLevels(reader, usesRawUInt16Samples: false, layers.Length);
            return new VisualHeightmapAsset(
                bounds,
                sampleColumns,
                sampleRows,
                heightSamplesCm,
                layers,
                storageLayout,
                defaultLayerIndex,
                interpolationMode,
                int16MipLevels);
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
            }
            else
            {
                writer.Write((int)SamplePayloadKind.ChunkedInt16Centimeters);
                writer.Write(asset.HeightSamplesCm.Length);
                WriteChunkedInt16Payload(writer, asset.HeightSamplesCm, asset.SampleColumns, asset.SampleRows, asset.Layers);
            }

            WriteMipLevels(writer, asset);
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

        private static VisualHeightmapMipLevel[] ReadMipLevels(BinaryReader reader, bool usesRawUInt16Samples, int baseLayerCount)
        {
            int mipLevelCount = reader.ReadInt32();
            if (mipLevelCount < 0 || mipLevelCount > 16)
            {
                throw new InvalidDataException("Visual heightmap mip level count is outside the supported range.");
            }

            if (mipLevelCount == 0)
            {
                return Array.Empty<VisualHeightmapMipLevel>();
            }

            var mipLevels = new VisualHeightmapMipLevel[mipLevelCount];
            for (int i = 0; i < mipLevelCount; i++)
            {
                int level = reader.ReadInt32();
                if (level != i + 1)
                {
                    throw new InvalidDataException("Visual heightmap mip levels must be contiguous from level 1.");
                }

                int sampleColumns = reader.ReadInt32();
                int sampleRows = reader.ReadInt32();
                if (sampleColumns < 2 || sampleRows < 2)
                {
                    throw new InvalidDataException("Visual heightmap mip dimensions must be at least 2x2.");
                }

                int layerCount = reader.ReadInt32();
                if (layerCount != baseLayerCount)
                {
                    throw new InvalidDataException("Visual heightmap mip layer count does not match level 0.");
                }

                var layers = new VisualHeightmapLayerDefinition[layerCount];
                for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                {
                    layers[layerIndex] = new VisualHeightmapLayerDefinition(
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
                    throw new InvalidDataException($"Visual heightmap mip sample count mismatch. Expected {expectedSampleCount}, actual {sampleCount}.");
                }

                if (usesRawUInt16Samples)
                {
                    if (payloadKind != SamplePayloadKind.ChunkedUInt16Scaled)
                    {
                        throw new InvalidDataException("Visual heightmap mip payload kind does not match uint16-scaled storage layout.");
                    }

                    ushort[] raw = ReadChunkedUInt16Payload(reader, sampleColumns, sampleRows, layers, sampleCount);
                    mipLevels[i] = new VisualHeightmapMipLevel(level, sampleColumns, sampleRows, raw, layers);
                }
                else
                {
                    if (payloadKind != SamplePayloadKind.ChunkedInt16Centimeters)
                    {
                        throw new InvalidDataException("Visual heightmap mip payload kind does not match int16-centimeter storage layout.");
                    }

                    short[] cm = ReadChunkedInt16Payload(reader, sampleColumns, sampleRows, layers, sampleCount);
                    mipLevels[i] = new VisualHeightmapMipLevel(level, sampleColumns, sampleRows, cm, layers);
                }
            }

            return mipLevels;
        }

        private static void WriteMipLevels(BinaryWriter writer, VisualHeightmapAsset asset)
        {
            VisualHeightmapMipLevel[] mipLevels = asset.MipLevels.Length == 0
                ? GenerateMipLevels(asset)
                : asset.MipLevels;

            writer.Write(mipLevels.Length);
            for (int i = 0; i < mipLevels.Length; i++)
            {
                VisualHeightmapMipLevel mip = mipLevels[i];
                writer.Write(mip.Level);
                writer.Write(mip.SampleColumns);
                writer.Write(mip.SampleRows);
                writer.Write(mip.Layers.Length);
                for (int layerIndex = 0; layerIndex < mip.Layers.Length; layerIndex++)
                {
                    VisualHeightmapLayerDefinition layer = mip.Layers[layerIndex];
                    writer.Write(layer.LayerId);
                    writer.Write(layer.Name ?? string.Empty);
                    writer.Write(layer.SampleOffset);
                    writer.Write(layer.SampleCount);
                }

                if (asset.UsesRawUInt16Samples)
                {
                    writer.Write((int)SamplePayloadKind.ChunkedUInt16Scaled);
                    writer.Write(mip.HeightSamplesRaw.Length);
                    WriteChunkedUInt16Payload(writer, mip.HeightSamplesRaw, mip.SampleColumns, mip.SampleRows, mip.Layers);
                }
                else
                {
                    writer.Write((int)SamplePayloadKind.ChunkedInt16Centimeters);
                    writer.Write(mip.HeightSamplesCm.Length);
                    WriteChunkedInt16Payload(writer, mip.HeightSamplesCm, mip.SampleColumns, mip.SampleRows, mip.Layers);
                }
            }
        }

        private static VisualHeightmapMipLevel[] GenerateMipLevels(VisualHeightmapAsset asset)
        {
            int levelCount = CountGeneratedMipLevels(asset.SampleColumns, asset.SampleRows);
            if (levelCount == 0)
            {
                return Array.Empty<VisualHeightmapMipLevel>();
            }

            var mipLevels = new VisualHeightmapMipLevel[levelCount];
            int sourceColumns = asset.SampleColumns;
            int sourceRows = asset.SampleRows;
            VisualHeightmapLayerDefinition[] sourceLayers = asset.Layers;
            if (asset.UsesRawUInt16Samples)
            {
                ushort[] sourceRaw = asset.HeightSamplesRaw;
                for (int level = 1; level <= levelCount; level++)
                {
                    int mipColumns = Math.Max(2, DivideRoundUp(sourceColumns, 2));
                    int mipRows = Math.Max(2, DivideRoundUp(sourceRows, 2));
                    VisualHeightmapLayerDefinition[] layers = CreateMipLayers(sourceLayers, mipColumns, mipRows);
                    ushort[] mipSamples = DownsampleUInt16(sourceRaw, sourceColumns, sourceRows, sourceLayers, mipColumns, mipRows, layers);
                    mipLevels[level - 1] = new VisualHeightmapMipLevel(level, mipColumns, mipRows, mipSamples, layers);
                    sourceRaw = mipSamples;
                    sourceColumns = mipColumns;
                    sourceRows = mipRows;
                    sourceLayers = layers;
                }
            }
            else
            {
                short[] sourceCm = asset.HeightSamplesCm;
                for (int level = 1; level <= levelCount; level++)
                {
                    int mipColumns = Math.Max(2, DivideRoundUp(sourceColumns, 2));
                    int mipRows = Math.Max(2, DivideRoundUp(sourceRows, 2));
                    VisualHeightmapLayerDefinition[] layers = CreateMipLayers(sourceLayers, mipColumns, mipRows);
                    short[] mipSamples = DownsampleInt16(sourceCm, sourceColumns, sourceRows, sourceLayers, mipColumns, mipRows, layers);
                    mipLevels[level - 1] = new VisualHeightmapMipLevel(level, mipColumns, mipRows, mipSamples, layers);
                    sourceCm = mipSamples;
                    sourceColumns = mipColumns;
                    sourceRows = mipRows;
                    sourceLayers = layers;
                }
            }

            return mipLevels;
        }

        private static int CountGeneratedMipLevels(int sampleColumns, int sampleRows)
        {
            int count = 0;
            while (sampleColumns > 2 || sampleRows > 2)
            {
                int nextColumns = Math.Max(2, DivideRoundUp(sampleColumns, 2));
                int nextRows = Math.Max(2, DivideRoundUp(sampleRows, 2));
                if (nextColumns == sampleColumns && nextRows == sampleRows)
                {
                    break;
                }

                count++;
                sampleColumns = nextColumns;
                sampleRows = nextRows;
            }

            return count;
        }

        private static VisualHeightmapLayerDefinition[] CreateMipLayers(VisualHeightmapLayerDefinition[] sourceLayers, int sampleColumns, int sampleRows)
        {
            int samplesPerLayer = checked(sampleColumns * sampleRows);
            var layers = new VisualHeightmapLayerDefinition[sourceLayers.Length];
            for (int i = 0; i < sourceLayers.Length; i++)
            {
                VisualHeightmapLayerDefinition sourceLayer = sourceLayers[i];
                layers[i] = new VisualHeightmapLayerDefinition(
                    sourceLayer.LayerId,
                    sourceLayer.Name,
                    checked(i * samplesPerLayer),
                    samplesPerLayer);
            }

            return layers;
        }

        private static short[] DownsampleInt16(
            short[] source,
            int sourceColumns,
            int sourceRows,
            VisualHeightmapLayerDefinition[] sourceLayers,
            int mipColumns,
            int mipRows,
            VisualHeightmapLayerDefinition[] mipLayers)
        {
            var mip = new short[checked(mipColumns * mipRows * mipLayers.Length)];
            for (int layerIndex = 0; layerIndex < mipLayers.Length; layerIndex++)
            {
                int sourceLayerOffset = sourceLayers[layerIndex].SampleOffset;
                int mipLayerOffset = mipLayers[layerIndex].SampleOffset;
                for (int y = 0; y < mipRows; y++)
                {
                    int sourceY0 = Math.Min(sourceRows - 1, y * 2);
                    int sourceY1 = Math.Min(sourceRows - 1, sourceY0 + 1);
                    for (int x = 0; x < mipColumns; x++)
                    {
                        int sourceX0 = Math.Min(sourceColumns - 1, x * 2);
                        int sourceX1 = Math.Min(sourceColumns - 1, sourceX0 + 1);
                        int sum =
                            source[sourceLayerOffset + (sourceY0 * sourceColumns) + sourceX0] +
                            source[sourceLayerOffset + (sourceY0 * sourceColumns) + sourceX1] +
                            source[sourceLayerOffset + (sourceY1 * sourceColumns) + sourceX0] +
                            source[sourceLayerOffset + (sourceY1 * sourceColumns) + sourceX1];
                        mip[mipLayerOffset + (y * mipColumns) + x] = checked((short)Math.Clamp((int)MathF.Round(sum / 4f), short.MinValue, short.MaxValue));
                    }
                }
            }

            return mip;
        }

        private static ushort[] DownsampleUInt16(
            ushort[] source,
            int sourceColumns,
            int sourceRows,
            VisualHeightmapLayerDefinition[] sourceLayers,
            int mipColumns,
            int mipRows,
            VisualHeightmapLayerDefinition[] mipLayers)
        {
            var mip = new ushort[checked(mipColumns * mipRows * mipLayers.Length)];
            for (int layerIndex = 0; layerIndex < mipLayers.Length; layerIndex++)
            {
                int sourceLayerOffset = sourceLayers[layerIndex].SampleOffset;
                int mipLayerOffset = mipLayers[layerIndex].SampleOffset;
                for (int y = 0; y < mipRows; y++)
                {
                    int sourceY0 = Math.Min(sourceRows - 1, y * 2);
                    int sourceY1 = Math.Min(sourceRows - 1, sourceY0 + 1);
                    for (int x = 0; x < mipColumns; x++)
                    {
                        int sourceX0 = Math.Min(sourceColumns - 1, x * 2);
                        int sourceX1 = Math.Min(sourceColumns - 1, sourceX0 + 1);
                        int sum =
                            source[sourceLayerOffset + (sourceY0 * sourceColumns) + sourceX0] +
                            source[sourceLayerOffset + (sourceY0 * sourceColumns) + sourceX1] +
                            source[sourceLayerOffset + (sourceY1 * sourceColumns) + sourceX0] +
                            source[sourceLayerOffset + (sourceY1 * sourceColumns) + sourceX1];
                        mip[mipLayerOffset + (y * mipColumns) + x] = checked((ushort)Math.Clamp((int)MathF.Round(sum / 4f), ushort.MinValue, ushort.MaxValue));
                    }
                }
            }

            return mip;
        }

        private static short[] ReadChunkedInt16Payload(BinaryReader reader, int sampleColumns, int sampleRows, VisualHeightmapLayerDefinition[] layers, int sampleCount)
        {
            ReadChunkHeader(reader, sampleColumns, sampleRows, out int chunkColumns, out int chunkRows, out int defaultValue, out int recordCount);
            var samples = new short[sampleCount];
            Array.Fill(samples, checked((short)defaultValue));

            for (int i = 0; i < recordCount; i++)
            {
                ReadChunkRecordHeader(reader, layers.Length, chunkColumns, chunkRows, out int layerIndex, out int chunkX, out int chunkY, out ChunkPayloadKind recordKind, out VisualHeightmapChunkCompression compression);
                GetChunkDimensions(sampleColumns, sampleRows, chunkX, chunkY, out int width, out int height);
                int layerOffset = layers[layerIndex].SampleOffset;

                if (recordKind == ChunkPayloadKind.Flat)
                {
                    RequireUncompressedFlatChunk(compression);
                    short value = reader.ReadInt16();
                    FillChunk(samples, sampleColumns, layerOffset, chunkX, chunkY, width, height, value);
                    continue;
                }

                if (recordKind != ChunkPayloadKind.Raw)
                {
                    throw new InvalidDataException($"Unsupported visual heightmap chunk payload kind: {recordKind}.");
                }

                ReadRawChunk(samples, reader, sampleColumns, layerOffset, chunkX, chunkY, width, height, compression);
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
                ReadChunkRecordHeader(reader, layers.Length, chunkColumns, chunkRows, out int layerIndex, out int chunkX, out int chunkY, out ChunkPayloadKind recordKind, out VisualHeightmapChunkCompression compression);
                GetChunkDimensions(sampleColumns, sampleRows, chunkX, chunkY, out int width, out int height);
                int layerOffset = layers[layerIndex].SampleOffset;

                if (recordKind == ChunkPayloadKind.Flat)
                {
                    RequireUncompressedFlatChunk(compression);
                    ushort value = reader.ReadUInt16();
                    FillChunk(samples, sampleColumns, layerOffset, chunkX, chunkY, width, height, value);
                    continue;
                }

                if (recordKind != ChunkPayloadKind.Raw)
                {
                    throw new InvalidDataException($"Unsupported visual heightmap chunk payload kind: {recordKind}.");
                }

                ReadRawChunk(samples, reader, sampleColumns, layerOffset, chunkX, chunkY, width, height, compression);
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
            out ChunkPayloadKind recordKind,
            out VisualHeightmapChunkCompression compression)
        {
            layerIndex = reader.ReadInt32();
            chunkX = reader.ReadInt32();
            chunkY = reader.ReadInt32();
            recordKind = (ChunkPayloadKind)reader.ReadByte();
            compression = (VisualHeightmapChunkCompression)reader.ReadByte();

            if ((uint)layerIndex >= (uint)layerCount ||
                (uint)chunkX >= (uint)chunkColumns ||
                (uint)chunkY >= (uint)chunkRows)
            {
                throw new InvalidDataException("Visual heightmap chunk record is outside the declared layout.");
            }

            if (compression != VisualHeightmapChunkCompression.None &&
                compression != VisualHeightmapChunkCompression.RunLength)
            {
                throw new InvalidDataException($"Unsupported visual heightmap chunk compression: {compression}.");
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
                            writer.Write((byte)VisualHeightmapChunkCompression.None);
                            WriteValue(writer, value);
                            continue;
                        }

                        writer.Write((byte)ChunkPayloadKind.Raw);
                        VisualHeightmapChunkCompression compression = ShouldUseRunLengthCompression(samples, sampleColumns, layerOffset, chunkX, chunkY, width, height)
                            ? VisualHeightmapChunkCompression.RunLength
                            : VisualHeightmapChunkCompression.None;
                        writer.Write((byte)compression);
                        WriteRawChunk(writer, samples, sampleColumns, layerOffset, chunkX, chunkY, width, height, compression);
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

        private static void RequireUncompressedFlatChunk(VisualHeightmapChunkCompression compression)
        {
            if (compression != VisualHeightmapChunkCompression.None)
            {
                throw new InvalidDataException("Visual heightmap flat chunks must not declare compression.");
            }
        }

        private static void ReadRawChunk(
            short[] samples,
            BinaryReader reader,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int width,
            int height,
            VisualHeightmapChunkCompression compression)
        {
            if (compression == VisualHeightmapChunkCompression.None)
            {
                ReadUncompressedRawChunk(samples, reader, sampleColumns, layerOffset, chunkX, chunkY, width, height);
                return;
            }

            if (compression != VisualHeightmapChunkCompression.RunLength)
            {
                throw new InvalidDataException($"Unsupported visual heightmap chunk compression: {compression}.");
            }

            ReadRunLengthChunk(samples, reader, sampleColumns, layerOffset, chunkX, chunkY, width, height);
        }

        private static void ReadRawChunk(
            ushort[] samples,
            BinaryReader reader,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int width,
            int height,
            VisualHeightmapChunkCompression compression)
        {
            if (compression == VisualHeightmapChunkCompression.None)
            {
                ReadUncompressedRawChunk(samples, reader, sampleColumns, layerOffset, chunkX, chunkY, width, height);
                return;
            }

            if (compression != VisualHeightmapChunkCompression.RunLength)
            {
                throw new InvalidDataException($"Unsupported visual heightmap chunk compression: {compression}.");
            }

            ReadRunLengthChunk(samples, reader, sampleColumns, layerOffset, chunkX, chunkY, width, height);
        }

        private static void ReadUncompressedRawChunk(short[] samples, BinaryReader reader, int sampleColumns, int layerOffset, int chunkX, int chunkY, int width, int height)
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

        private static void ReadUncompressedRawChunk(ushort[] samples, BinaryReader reader, int sampleColumns, int layerOffset, int chunkX, int chunkY, int width, int height)
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

        private static void ReadRunLengthChunk(short[] samples, BinaryReader reader, int sampleColumns, int layerOffset, int chunkX, int chunkY, int width, int height)
        {
            int total = checked(width * height);
            int written = 0;
            while (written < total)
            {
                int runLength = reader.ReadInt32();
                if (runLength <= 0 || runLength > total - written)
                {
                    throw new InvalidDataException("Visual heightmap RLE chunk has an invalid run length.");
                }

                short value = reader.ReadInt16();
                FillRun(samples, sampleColumns, layerOffset, chunkX, chunkY, width, written, runLength, value);
                written += runLength;
            }
        }

        private static void ReadRunLengthChunk(ushort[] samples, BinaryReader reader, int sampleColumns, int layerOffset, int chunkX, int chunkY, int width, int height)
        {
            int total = checked(width * height);
            int written = 0;
            while (written < total)
            {
                int runLength = reader.ReadInt32();
                if (runLength <= 0 || runLength > total - written)
                {
                    throw new InvalidDataException("Visual heightmap RLE chunk has an invalid run length.");
                }

                ushort value = reader.ReadUInt16();
                FillRun(samples, sampleColumns, layerOffset, chunkX, chunkY, width, written, runLength, value);
                written += runLength;
            }
        }

        private static void FillRun<T>(
            T[] samples,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int chunkWidth,
            int runStart,
            int runLength,
            T value)
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            for (int i = 0; i < runLength; i++)
            {
                int localIndex = runStart + i;
                int x = localIndex % chunkWidth;
                int y = localIndex / chunkWidth;
                int sampleIndex = layerOffset + ((startY + y) * sampleColumns) + startX + x;
                samples[sampleIndex] = value;
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
            int height,
            VisualHeightmapChunkCompression compression)
            where T : unmanaged, IEquatable<T>
        {
            if (compression == VisualHeightmapChunkCompression.RunLength)
            {
                WriteRunLengthChunk(writer, samples, sampleColumns, layerOffset, chunkX, chunkY, width, height);
                return;
            }

            if (compression != VisualHeightmapChunkCompression.None)
            {
                throw new InvalidOperationException($"Unsupported visual heightmap chunk compression: {compression}.");
            }

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

        private static bool ShouldUseRunLengthCompression<T>(
            T[] samples,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int width,
            int height)
            where T : unmanaged, IEquatable<T>
        {
            int runCount = CountRuns(samples, sampleColumns, layerOffset, chunkX, chunkY, width, height);
            int sampleSizeBytes = typeof(T) == typeof(short) || typeof(T) == typeof(ushort)
                ? 2
                : throw new InvalidOperationException($"Unsupported visual heightmap sample type: {typeof(T).Name}.");
            int rawBytes = checked(width * height * sampleSizeBytes);
            int rleBytes = checked(runCount * (sizeof(int) + sampleSizeBytes));
            return rleBytes < rawBytes;
        }

        private static int CountRuns<T>(
            T[] samples,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int width,
            int height)
            where T : unmanaged, IEquatable<T>
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            int runCount = 0;
            bool hasPrevious = false;
            T previous = default;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = layerOffset + ((startY + y) * sampleColumns) + startX;
                for (int x = 0; x < width; x++)
                {
                    T current = samples[rowOffset + x];
                    if (!hasPrevious || !current.Equals(previous))
                    {
                        runCount++;
                        previous = current;
                        hasPrevious = true;
                    }
                }
            }

            return runCount;
        }

        private static void WriteRunLengthChunk<T>(
            BinaryWriter writer,
            T[] samples,
            int sampleColumns,
            int layerOffset,
            int chunkX,
            int chunkY,
            int width,
            int height)
            where T : unmanaged, IEquatable<T>
        {
            int startX = chunkX * ChunkSampleColumns;
            int startY = chunkY * ChunkSampleRows;
            bool hasRun = false;
            T runValue = default;
            int runLength = 0;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = layerOffset + ((startY + y) * sampleColumns) + startX;
                for (int x = 0; x < width; x++)
                {
                    T current = samples[rowOffset + x];
                    if (!hasRun)
                    {
                        runValue = current;
                        runLength = 1;
                        hasRun = true;
                        continue;
                    }

                    if (current.Equals(runValue))
                    {
                        runLength++;
                        continue;
                    }

                    writer.Write(runLength);
                    WriteValue(writer, runValue);
                    runValue = current;
                    runLength = 1;
                }
            }

            if (hasRun)
            {
                writer.Write(runLength);
                WriteValue(writer, runValue);
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
