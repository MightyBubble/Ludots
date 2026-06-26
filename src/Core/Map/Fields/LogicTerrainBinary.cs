using System.IO;

namespace Ludots.Core.Map.Fields
{
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
        public const ushort FormatVersion = 1;
        public const int DenseEquivalentBytesPerCell = 4;

        private const int ChecksumOffset = 4 + 2 + 2;
        private const int ChecksumLength = 8;
        private const byte LayerLayoutHeightWaterAreaFlagsV1 = 1;
        private const byte CompressionNone = 0;

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
                writer.Write(CompressionNone);
                writer.Write((ushort)0);
                writer.Write(DenseEquivalentBytesPerCell);
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

                    writer.Write(pair.Key);
                    writer.Write(payloadBytes);
                    chunk.WritePortablePayload(writer);
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
                int payloadBytes = reader.ReadInt32();
                if (payloadBytes != expectedPayloadBytes)
                {
                    throw new InvalidDataException(
                        $"LogicTerrainBinary chunk payload size {payloadBytes} does not match header {expectedPayloadBytes}.");
                }

                var chunk = new LogicTerrainChunk(cellCount);
                chunk.ReadPortablePayload(reader);
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
            => checked((long)widthCells * heightCells * DenseEquivalentBytesPerCell);

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
            if (compression != CompressionNone)
            {
                throw new InvalidDataException($"LogicTerrainBinary compression mode mismatch: {compression}.");
            }

            _ = reader.ReadUInt16();
            int denseEquivalentBytesPerCell = reader.ReadInt32();
            if (denseEquivalentBytesPerCell != DenseEquivalentBytesPerCell)
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
    }
}
