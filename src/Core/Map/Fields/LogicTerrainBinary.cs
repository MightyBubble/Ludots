using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Fields
{
    public enum LogicTerrainChunkCompressionMode : byte
    {
        Raw = 0,
        Rle = 1,
        Palette = 2,
        Delta = 3,
        Zstd = 16,
        Lz4 = 17
    }

    public readonly struct LogicTerrainBinaryMetadata
    {
        public LogicTerrainBinaryMetadata(
            int widthCells,
            int heightCells,
            int cellSizeCm,
            int chunkSizeCells,
            LogicTerrainCell defaultCell,
            int chunkCount,
            int chunkPayloadBytes,
            int denseEquivalentBytesPerCell)
        {
            WidthCells = widthCells;
            HeightCells = heightCells;
            CellSizeCm = cellSizeCm;
            ChunkSizeCells = chunkSizeCells;
            DefaultCell = defaultCell;
            ChunkCount = chunkCount;
            ChunkPayloadBytes = chunkPayloadBytes;
            DenseEquivalentBytesPerCell = denseEquivalentBytesPerCell;
        }

        public int WidthCells { get; }

        public int HeightCells { get; }

        public int CellSizeCm { get; }

        public int ChunkSizeCells { get; }

        public LogicTerrainCell DefaultCell { get; }

        public int ChunkCount { get; }

        public int ChunkPayloadBytes { get; }

        public int DenseEquivalentBytesPerCell { get; }

        public long DenseEquivalentBytes
            => checked((long)WidthCells * HeightCells * DenseEquivalentBytesPerCell);
    }

    public static class LogicTerrainBinary
    {
        private const uint Magic = 0x4E52544C;
        public const ushort FormatVersion = 2;
        private const int ChecksumOffset = 4 + 2 + 2;
        private const int ChecksumLength = 8;
        private const byte LayerLayoutHeightWaterAreaFlagsV1 = 1;
        private const byte FileCompressionNone = 0;

        public static void Write(Stream stream, SparseGridLogicTerrainField terrain)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));

            BoardFieldStore<LogicTerrainCell> store = terrain.Store;
            int payloadBytes = PortablePayloadByteLength(store.ChunkSizeCells);
            int chunkCount = store.DirtyChunkCount;

            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write((ushort)0);
                writer.Write(0UL);
                writer.Write(store.WidthCells);
                writer.Write(store.HeightCells);
                writer.Write(store.CellSizeCm);
                writer.Write(store.ChunkSizeCells);
                WritePortableDefaultCell(writer, store.DefaultValue);
                writer.Write(LayerLayoutHeightWaterAreaFlagsV1);
                writer.Write(FileCompressionNone);
                writer.Write((ushort)0);
                writer.Write(SpatialScaleDefaults.LogicDenseEquivalentBytesPerCell);
                writer.Write(payloadBytes);
                writer.Write(chunkCount);

                foreach (KeyValuePair<long, BoardFieldChunk<LogicTerrainCell>> pair in store.ResidentChunks)
                {
                    if (!pair.Value.Dirty)
                    {
                        continue;
                    }

                    if (pair.Value is not LogicTerrainChunk chunk)
                    {
                        throw new InvalidDataException(
                            $"LogicTerrainBinary requires {nameof(LogicTerrainChunk)} payloads.");
                    }

                    if (chunk.PortablePayloadByteLength != payloadBytes)
                    {
                        throw new InvalidDataException(
                            $"LogicTerrain chunk payload size {chunk.PortablePayloadByteLength} does not match header {payloadBytes}.");
                    }

                    byte[] rawPayload = WriteRawPayload(chunk, payloadBytes);
                    EncodedChunkPayload encoded = SelectSmallestPayload(rawPayload);
                    writer.Write(pair.Key);
                    writer.Write((byte)encoded.Mode);
                    writer.Write(encoded.Bytes.Length);
                    writer.Write(encoded.Bytes);
                }
            }

            byte[] data = ms.ToArray();
            ulong checksum = Fnv1a64(data, ChecksumOffset, ChecksumLength);
            WriteUInt64LE(data, ChecksumOffset, checksum);
            stream.Write(data, 0, data.Length);
        }

        public static SparseGridLogicTerrainField Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            byte[] data = ReadAllBytes(stream);
            using var reader = OpenValidatedReader(data);
            LogicTerrainBinaryMetadata metadata = ReadHeader(reader);

            var terrain = new SparseGridLogicTerrainField(
                metadata.WidthCells,
                metadata.HeightCells,
                metadata.CellSizeCm,
                metadata.ChunkSizeCells,
                metadata.DefaultCell);

            int expectedPayloadBytes = PortablePayloadByteLength(metadata.ChunkSizeCells);
            if (metadata.ChunkPayloadBytes != expectedPayloadBytes)
            {
                throw new InvalidDataException(
                    $"LogicTerrainBinary payload size {metadata.ChunkPayloadBytes} does not match expected {expectedPayloadBytes}.");
            }

            int cellCount = checked(metadata.ChunkSizeCells * metadata.ChunkSizeCells);
            for (int i = 0; i < metadata.ChunkCount; i++)
            {
                long chunkKey = reader.ReadInt64();
                var compressionMode = (LogicTerrainChunkCompressionMode)reader.ReadByte();
                int encodedBytes = reader.ReadInt32();
                if (encodedBytes <= 0)
                {
                    throw new InvalidDataException("LogicTerrainBinary chunk encoded byte length must be > 0.");
                }

                byte[] encodedPayload = reader.ReadBytes(encodedBytes);
                if (encodedPayload.Length != encodedBytes)
                {
                    throw new EndOfStreamException("LogicTerrainBinary chunk payload truncated.");
                }

                byte[] rawPayload = DecodePayload(compressionMode, encodedPayload, expectedPayloadBytes);
                var chunk = new LogicTerrainChunk(cellCount);
                using (var payloadReader = new BinaryReader(new MemoryStream(rawPayload)))
                {
                    chunk.ReadPortablePayload(payloadReader);
                    if (payloadReader.BaseStream.Position != payloadReader.BaseStream.Length)
                    {
                        throw new InvalidDataException("LogicTerrainBinary chunk payload has trailing bytes.");
                    }
                }

                terrain.Store.SetResidentChunk(chunkKey, chunk, dirty: false);
            }

            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("LogicTerrainBinary has trailing bytes.");
            }

            return terrain;
        }

        public static LogicTerrainBinaryMetadata ReadMetadata(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            byte[] data = ReadAllBytes(stream);
            using var reader = OpenValidatedReader(data);
            return ReadHeader(reader);
        }

        public static long GetDenseEquivalentBytes(int widthCells, int heightCells)
            => checked((long)widthCells * heightCells * SpatialScaleDefaults.LogicDenseEquivalentBytesPerCell);

        private static LogicTerrainBinaryMetadata ReadHeader(BinaryReader reader)
        {
            int widthCells = reader.ReadInt32();
            int heightCells = reader.ReadInt32();
            int cellSizeCm = reader.ReadInt32();
            int chunkSizeCells = reader.ReadInt32();
            if (widthCells <= 0) throw new InvalidDataException("LogicTerrainBinary width must be > 0.");
            if (heightCells <= 0) throw new InvalidDataException("LogicTerrainBinary height must be > 0.");
            if (cellSizeCm <= 0) throw new InvalidDataException("LogicTerrainBinary cell size must be > 0.");
            if (chunkSizeCells <= 0) throw new InvalidDataException("LogicTerrainBinary chunk size must be > 0.");

            LogicTerrainCell defaultCell = ReadPortableDefaultCell(reader);
            byte layout = reader.ReadByte();
            if (layout != LayerLayoutHeightWaterAreaFlagsV1)
            {
                throw new InvalidDataException($"LogicTerrainBinary layer layout mismatch: {layout}.");
            }

            byte compression = reader.ReadByte();
            if (compression != FileCompressionNone)
            {
                throw new InvalidDataException($"LogicTerrainBinary file compression mode mismatch: {compression}.");
            }

            _ = reader.ReadUInt16();
            int denseEquivalentBytesPerCell = reader.ReadInt32();
            if (denseEquivalentBytesPerCell != SpatialScaleDefaults.LogicDenseEquivalentBytesPerCell)
            {
                throw new InvalidDataException(
                    $"LogicTerrainBinary dense-equivalent bytes/cell mismatch: {denseEquivalentBytesPerCell}.");
            }

            int chunkPayloadBytes = reader.ReadInt32();
            if (chunkPayloadBytes <= 0)
            {
                throw new InvalidDataException("LogicTerrainBinary chunk payload bytes must be > 0.");
            }

            int chunkCount = reader.ReadInt32();
            if (chunkCount < 0)
            {
                throw new InvalidDataException("LogicTerrainBinary chunk count must be >= 0.");
            }

            return new LogicTerrainBinaryMetadata(
                widthCells,
                heightCells,
                cellSizeCm,
                chunkSizeCells,
                defaultCell,
                chunkCount,
                chunkPayloadBytes,
                denseEquivalentBytesPerCell);
        }

        private static BinaryReader OpenValidatedReader(byte[] data)
        {
            if (data.Length < ChecksumOffset + ChecksumLength)
            {
                throw new InvalidDataException("LogicTerrainBinary too small.");
            }

            var reader = new BinaryReader(new MemoryStream(data));
            uint magic = reader.ReadUInt32();
            if (magic != Magic)
            {
                throw new InvalidDataException("LogicTerrainBinary magic mismatch.");
            }

            ushort version = reader.ReadUInt16();
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"LogicTerrainBinary version mismatch: {version}.");
            }

            _ = reader.ReadUInt16();
            ulong checksum = reader.ReadUInt64();
            ulong computed = Fnv1a64(data, ChecksumOffset, ChecksumLength);
            if (checksum != computed)
            {
                throw new InvalidDataException("LogicTerrainBinary checksum mismatch.");
            }

            return reader;
        }

        private static byte[] WriteRawPayload(LogicTerrainChunk chunk, int payloadBytes)
        {
            byte[] rawPayload = new byte[payloadBytes];
            using var stream = new MemoryStream(rawPayload, writable: true);
            using var writer = new BinaryWriter(stream);
            chunk.WritePortablePayload(writer);
            if (stream.Position != payloadBytes)
            {
                throw new InvalidDataException(
                    $"LogicTerrain chunk payload wrote {stream.Position} bytes, expected {payloadBytes}.");
            }

            return rawPayload;
        }

        private static EncodedChunkPayload SelectSmallestPayload(byte[] rawPayload)
        {
            var best = new EncodedChunkPayload(LogicTerrainChunkCompressionMode.Raw, rawPayload);
            UseIfSmaller(ref best, LogicTerrainChunkCompressionMode.Rle, EncodeRle(rawPayload));
            UseIfSmaller(ref best, LogicTerrainChunkCompressionMode.Palette, EncodePalette(rawPayload));
            UseIfSmaller(ref best, LogicTerrainChunkCompressionMode.Delta, EncodeDelta(rawPayload));
            return best;
        }

        private static void UseIfSmaller(
            ref EncodedChunkPayload best,
            LogicTerrainChunkCompressionMode mode,
            byte[]? candidate)
        {
            if (candidate != null && candidate.Length < best.Bytes.Length)
            {
                best = new EncodedChunkPayload(mode, candidate);
            }
        }

        private static byte[] EncodeRle(byte[] raw)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            int index = 0;
            while (index < raw.Length)
            {
                byte value = raw[index];
                int runLength = 1;
                while (index + runLength < raw.Length &&
                       raw[index + runLength] == value &&
                       runLength < ushort.MaxValue)
                {
                    runLength++;
                }

                writer.Write(value);
                writer.Write((ushort)runLength);
                index += runLength;
            }

            return ms.ToArray();
        }

        private static byte[]? EncodePalette(byte[] raw)
        {
            byte[] palette = new byte[16];
            int paletteCount = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (FindPaletteIndex(palette, paletteCount, raw[i]) >= 0)
                {
                    continue;
                }

                if (paletteCount == palette.Length)
                {
                    return null;
                }

                palette[paletteCount++] = raw[i];
            }

            int indexBytes = (raw.Length + 1) >> 1;
            byte[] encoded = new byte[checked(1 + paletteCount + indexBytes)];
            encoded[0] = (byte)paletteCount;
            Array.Copy(palette, 0, encoded, 1, paletteCount);

            int packedOffset = 1 + paletteCount;
            for (int i = 0; i < raw.Length; i++)
            {
                int paletteIndex = FindPaletteIndex(palette, paletteCount, raw[i]);
                int target = packedOffset + (i >> 1);
                if ((i & 1) == 0)
                {
                    encoded[target] = (byte)paletteIndex;
                }
                else
                {
                    encoded[target] |= (byte)(paletteIndex << 4);
                }
            }

            return encoded;
        }

        private static int FindPaletteIndex(byte[] palette, int paletteCount, byte value)
        {
            for (int i = 0; i < paletteCount; i++)
            {
                if (palette[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static byte[]? EncodeDelta(byte[] raw)
        {
            if (raw.Length == 0)
            {
                return Array.Empty<byte>();
            }

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(raw[0]);

            int index = 1;
            while (index < raw.Length)
            {
                int delta = raw[index] - raw[index - 1];
                if (delta < sbyte.MinValue || delta > sbyte.MaxValue)
                {
                    return null;
                }

                int runLength = 1;
                byte previous = raw[index];
                while (index + runLength < raw.Length && runLength < ushort.MaxValue)
                {
                    int nextDelta = raw[index + runLength] - previous;
                    if (nextDelta != delta)
                    {
                        break;
                    }

                    previous = raw[index + runLength];
                    runLength++;
                }

                writer.Write(unchecked((byte)(sbyte)delta));
                writer.Write((ushort)runLength);
                index += runLength;
            }

            return ms.ToArray();
        }

        private static byte[] DecodePayload(
            LogicTerrainChunkCompressionMode mode,
            byte[] encodedPayload,
            int expectedPayloadBytes)
        {
            return mode switch
            {
                LogicTerrainChunkCompressionMode.Raw => DecodeRaw(encodedPayload, expectedPayloadBytes),
                LogicTerrainChunkCompressionMode.Rle => DecodeRle(encodedPayload, expectedPayloadBytes),
                LogicTerrainChunkCompressionMode.Palette => DecodePalette(encodedPayload, expectedPayloadBytes),
                LogicTerrainChunkCompressionMode.Delta => DecodeDelta(encodedPayload, expectedPayloadBytes),
                LogicTerrainChunkCompressionMode.Zstd => throw new InvalidDataException("LogicTerrainBinary zstd compression is reserved and not implemented."),
                LogicTerrainChunkCompressionMode.Lz4 => throw new InvalidDataException("LogicTerrainBinary lz4 compression is reserved and not implemented."),
                _ => throw new InvalidDataException($"LogicTerrainBinary unknown chunk compression mode: {(byte)mode}.")
            };
        }

        private static byte[] DecodeRaw(byte[] encodedPayload, int expectedPayloadBytes)
        {
            if (encodedPayload.Length != expectedPayloadBytes)
            {
                throw new InvalidDataException(
                    $"LogicTerrainBinary raw chunk payload size {encodedPayload.Length} does not match expected {expectedPayloadBytes}.");
            }

            return encodedPayload;
        }

        private static byte[] DecodeRle(byte[] encodedPayload, int expectedPayloadBytes)
        {
            if (encodedPayload.Length % 3 != 0)
            {
                throw new InvalidDataException("LogicTerrainBinary RLE payload has an incomplete run.");
            }

            byte[] raw = new byte[expectedPayloadBytes];
            int source = 0;
            int target = 0;
            while (source < encodedPayload.Length)
            {
                byte value = encodedPayload[source++];
                int runLength = encodedPayload[source++] | (encodedPayload[source++] << 8);
                if (runLength <= 0)
                {
                    throw new InvalidDataException("LogicTerrainBinary RLE payload has a zero-length run.");
                }

                if (target + runLength > raw.Length)
                {
                    throw new InvalidDataException("LogicTerrainBinary RLE payload expands past chunk size.");
                }

                Array.Fill(raw, value, target, runLength);
                target += runLength;
            }

            if (target != raw.Length)
            {
                throw new InvalidDataException("LogicTerrainBinary RLE payload ended before chunk size.");
            }

            return raw;
        }

        private static byte[] DecodePalette(byte[] encodedPayload, int expectedPayloadBytes)
        {
            if (encodedPayload.Length < 2)
            {
                throw new InvalidDataException("LogicTerrainBinary palette payload is too small.");
            }

            int paletteCount = encodedPayload[0];
            if (paletteCount <= 0 || paletteCount > 16)
            {
                throw new InvalidDataException($"LogicTerrainBinary palette size is invalid: {paletteCount}.");
            }

            int expectedLength = checked(1 + paletteCount + ((expectedPayloadBytes + 1) >> 1));
            if (encodedPayload.Length != expectedLength)
            {
                throw new InvalidDataException(
                    $"LogicTerrainBinary palette payload size {encodedPayload.Length} does not match expected {expectedLength}.");
            }

            byte[] raw = new byte[expectedPayloadBytes];
            int packedOffset = 1 + paletteCount;
            for (int i = 0; i < raw.Length; i++)
            {
                byte packed = encodedPayload[packedOffset + (i >> 1)];
                int paletteIndex = (i & 1) == 0 ? packed & 0x0F : (packed >> 4) & 0x0F;
                if (paletteIndex >= paletteCount)
                {
                    throw new InvalidDataException("LogicTerrainBinary palette payload references an invalid palette index.");
                }

                raw[i] = encodedPayload[1 + paletteIndex];
            }

            return raw;
        }

        private static byte[] DecodeDelta(byte[] encodedPayload, int expectedPayloadBytes)
        {
            if (encodedPayload.Length < 1)
            {
                throw new InvalidDataException("LogicTerrainBinary delta payload is too small.");
            }

            byte[] raw = new byte[expectedPayloadBytes];
            raw[0] = encodedPayload[0];
            int source = 1;
            int target = 1;
            while (source < encodedPayload.Length)
            {
                if (source + 3 > encodedPayload.Length)
                {
                    throw new InvalidDataException("LogicTerrainBinary delta payload has an incomplete run.");
                }

                int delta = unchecked((sbyte)encodedPayload[source++]);
                int runLength = encodedPayload[source++] | (encodedPayload[source++] << 8);
                if (runLength <= 0)
                {
                    throw new InvalidDataException("LogicTerrainBinary delta payload has a zero-length run.");
                }

                if (target + runLength > raw.Length)
                {
                    throw new InvalidDataException("LogicTerrainBinary delta payload expands past chunk size.");
                }

                for (int i = 0; i < runLength; i++)
                {
                    raw[target] = unchecked((byte)(raw[target - 1] + delta));
                    target++;
                }
            }

            if (target != raw.Length)
            {
                throw new InvalidDataException("LogicTerrainBinary delta payload ended before chunk size.");
            }

            return raw;
        }

        private static void WritePortableDefaultCell(BinaryWriter writer, LogicTerrainCell cell)
        {
            writer.Write(cell.HeightLevel);
            writer.Write(cell.WaterHeightLevel);
            writer.Write((byte)cell.SurfaceFlags);
            writer.Write(cell.AreaId);
        }

        private static LogicTerrainCell ReadPortableDefaultCell(BinaryReader reader)
        {
            byte height = reader.ReadByte();
            byte water = reader.ReadByte();
            var flags = (LogicTerrainSurfaceFlags)reader.ReadByte();
            byte areaId = reader.ReadByte();
            return new LogicTerrainCell(height, water, flags, areaId);
        }

        private static int PortablePayloadByteLength(int chunkSizeCells)
        {
            int cellCount = checked(chunkSizeCells * chunkSizeCells);
            int flagWords = (cellCount + 63) >> 6;
            return checked(cellCount + cellCount + (flagWords * 3 * sizeof(ulong)));
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static ulong Fnv1a64(byte[] data, int checksumOffset, int checksumLength)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < data.Length; i++)
            {
                if (i >= checksumOffset && i < checksumOffset + checksumLength)
                {
                    continue;
                }

                h ^= data[i];
                h *= 1099511628211UL;
            }

            return h;
        }

        private static void WriteUInt64LE(byte[] data, int checksumOffset, ulong value)
        {
            data[checksumOffset + 0] = (byte)(value & 0xFF);
            data[checksumOffset + 1] = (byte)((value >> 8) & 0xFF);
            data[checksumOffset + 2] = (byte)((value >> 16) & 0xFF);
            data[checksumOffset + 3] = (byte)((value >> 24) & 0xFF);
            data[checksumOffset + 4] = (byte)((value >> 32) & 0xFF);
            data[checksumOffset + 5] = (byte)((value >> 40) & 0xFF);
            data[checksumOffset + 6] = (byte)((value >> 48) & 0xFF);
            data[checksumOffset + 7] = (byte)((value >> 56) & 0xFF);
        }

        private readonly struct EncodedChunkPayload
        {
            public EncodedChunkPayload(LogicTerrainChunkCompressionMode mode, byte[] bytes)
            {
                Mode = mode;
                Bytes = bytes;
            }

            public LogicTerrainChunkCompressionMode Mode { get; }

            public byte[] Bytes { get; }
        }
    }
}
