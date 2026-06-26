using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ludots.Core.Map.Fields;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LogicTerrainBinaryTests
    {
        [Test]
        public void Write_PersistsOnlyDirtySparseChunks()
        {
            var terrain = new SparseGridLogicTerrainField(4096, 4096);
            terrain.SetCell(10, 10, new LogicTerrainCell(3, 0, LogicTerrainSurfaceFlags.Blocked, areaId: 7));
            terrain.SetCell(
                (3 * SpatialScaleDefaults.TerrainChunkCells) + 1,
                (2 * SpatialScaleDefaults.TerrainChunkCells) + 1,
                new LogicTerrainCell(9, 1, LogicTerrainSurfaceFlags.Ramp | LogicTerrainSurfaceFlags.Water, areaId: 4));
            terrain.SetCell(
                5 * SpatialScaleDefaults.TerrainChunkCells,
                5 * SpatialScaleDefaults.TerrainChunkCells,
                LogicTerrainCell.Default);

            byte[] bytes = WriteToBytes(terrain);
            LogicTerrainBinaryMetadata metadata = LogicTerrainBinary.ReadMetadata(new MemoryStream(bytes));
            List<ChunkRecordInfo> records = ReadChunkRecords(bytes);

            Assert.That(metadata.ChunkCount, Is.EqualTo(2));
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(bytes.Length, Is.LessThan(HeaderBytes + (metadata.ChunkCount * (ChunkRecordHeaderBytes + metadata.ChunkPayloadBytes))));
        }

        [Test]
        public void Roundtrip_PreservesCellsAndSparseDefaults()
        {
            const int width = 130;
            const int height = 129;
            var terrain = new SparseGridLogicTerrainField(width, height);
            var expectedOverrides = new Dictionary<int, LogicTerrainCell>();

            WriteCell(terrain, expectedOverrides, width, 0, 0, new LogicTerrainCell(1, 0, LogicTerrainSurfaceFlags.None, areaId: 1));
            WriteCell(terrain, expectedOverrides, width, 65, 0, new LogicTerrainCell(2, 0, LogicTerrainSurfaceFlags.Ramp, areaId: 5));
            WriteCell(terrain, expectedOverrides, width, 129, 128, new LogicTerrainCell(7, 9, LogicTerrainSurfaceFlags.Water | LogicTerrainSurfaceFlags.Blocked, areaId: 11));

            byte[] bytes = WriteToBytes(terrain);
            SparseGridLogicTerrainField loaded = LogicTerrainBinary.Read(new MemoryStream(bytes));

            Assert.That(loaded.ResidentChunkCount, Is.EqualTo(3));
            Assert.That(loaded.IsChunkDirty(0, 0), Is.False);
            Assert.That(loaded.WidthCells, Is.EqualTo(width));
            Assert.That(loaded.HeightCells, Is.EqualTo(height));

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    LogicTerrainCell expected = expectedOverrides.TryGetValue(row * width + col, out LogicTerrainCell overrideCell)
                        ? overrideCell
                        : LogicTerrainCell.Default;
                    Assert.That(loaded.GetCell(col, row), Is.EqualTo(expected));
                }
            }
        }

        [Test]
        public void Read_FailsFast_OnBadMagicBadVersionAndTruncation()
        {
            var terrain = new SparseGridLogicTerrainField(64, 64);
            terrain.SetCell(0, 0, new LogicTerrainCell(1, 0, LogicTerrainSurfaceFlags.None));
            byte[] bytes = WriteToBytes(terrain);

            byte[] badMagic = (byte[])bytes.Clone();
            badMagic[0] ^= 0xFF;
            Assert.That(
                () => LogicTerrainBinary.Read(new MemoryStream(badMagic)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("magic"));

            byte[] badVersion = (byte[])bytes.Clone();
            badVersion[4] = 0xFF;
            Assert.That(
                () => LogicTerrainBinary.Read(new MemoryStream(badVersion)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("version"));

            byte[] truncated = new byte[bytes.Length - 1];
            Array.Copy(bytes, truncated, truncated.Length);
            Assert.That(
                () => LogicTerrainBinary.Read(new MemoryStream(truncated)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("checksum"));
        }

        [Test]
        public void Roundtrip_SelectsRawRlePaletteAndDeltaCompression()
        {
            AssertCompressionRoundtrip(CreateRawTerrain(), LogicTerrainChunkCompressionMode.Raw);
            AssertCompressionRoundtrip(CreateRleTerrain(), LogicTerrainChunkCompressionMode.Rle);
            AssertCompressionRoundtrip(CreatePaletteTerrain(), LogicTerrainChunkCompressionMode.Palette);
            AssertCompressionRoundtrip(CreateDeltaTerrain(), LogicTerrainChunkCompressionMode.Delta);
        }

        [Test]
        public void Write_CompressesUniformChunkSignificantlyBelowRaw()
        {
            byte[] bytes = WriteToBytes(CreateRleTerrain());
            LogicTerrainBinaryMetadata metadata = LogicTerrainBinary.ReadMetadata(new MemoryStream(bytes));
            List<ChunkRecordInfo> records = ReadChunkRecords(bytes);

            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].CompressionMode, Is.EqualTo(LogicTerrainChunkCompressionMode.Rle));
            Assert.That(records[0].EncodedBytes, Is.LessThan(metadata.ChunkPayloadBytes / 4));
        }

        [Test]
        public void Read_FailsFast_OnUnknownCompressionMode()
        {
            byte[] bytes = WriteToBytes(CreateRleTerrain());
            bytes[HeaderBytes + sizeof(long)] = 0x99;
            RewriteChecksum(bytes);

            Assert.That(
                () => LogicTerrainBinary.Read(new MemoryStream(bytes)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("unknown chunk compression"));
        }

        [Test]
        public void Metadata_ExposesDenseEquivalentMath_WithoutFieldSemanticStrings()
        {
            var terrain = new SparseGridLogicTerrainField(128, 96);
            terrain.SetCell(1, 1, new LogicTerrainCell(4, 0, LogicTerrainSurfaceFlags.Blocked, areaId: 8));

            byte[] bytes = WriteToBytes(terrain);
            LogicTerrainBinaryMetadata metadata = LogicTerrainBinary.ReadMetadata(new MemoryStream(bytes));

            Assert.That(metadata.DenseEquivalentBytesPerCell, Is.EqualTo(LogicTerrainBinary.DenseEquivalentBytesPerCell));
            Assert.That(metadata.DenseEquivalentBytes, Is.EqualTo(128L * 96 * 4));
            Assert.That(LogicTerrainBinary.GetDenseEquivalentBytes(128, 96), Is.EqualTo(metadata.DenseEquivalentBytes));

            string ascii = Encoding.ASCII.GetString(bytes);
            Assert.That(ascii, Does.Not.Contain("cost"));
            Assert.That(ascii, Does.Not.Contain("areaId"));
            Assert.That(ascii, Does.Not.Contain("blocked"));
        }

        private const int HeaderBytes = 52;
        private const int ChunkRecordHeaderBytes = 13;
        private const int ChecksumOffset = 4 + 2 + 2;
        private const int ChecksumLength = 8;

        private static byte[] WriteToBytes(SparseGridLogicTerrainField terrain)
        {
            using var stream = new MemoryStream();
            LogicTerrainBinary.Write(stream, terrain);
            return stream.ToArray();
        }

        private static void WriteCell(
            SparseGridLogicTerrainField terrain,
            Dictionary<int, LogicTerrainCell> expectedOverrides,
            int width,
            int col,
            int row,
            LogicTerrainCell cell)
        {
            terrain.SetCell(col, row, cell);
            expectedOverrides[row * width + col] = cell;
        }

        private static void AssertCompressionRoundtrip(
            SparseGridLogicTerrainField terrain,
            LogicTerrainChunkCompressionMode expectedCompression)
        {
            byte[] bytes = WriteToBytes(terrain);
            List<ChunkRecordInfo> records = ReadChunkRecords(bytes);
            SparseGridLogicTerrainField loaded = LogicTerrainBinary.Read(new MemoryStream(bytes));

            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].CompressionMode, Is.EqualTo(expectedCompression));
            Assert.That(loaded.WidthCells, Is.EqualTo(terrain.WidthCells));
            Assert.That(loaded.HeightCells, Is.EqualTo(terrain.HeightCells));

            for (int row = 0; row < terrain.HeightCells; row++)
            {
                for (int col = 0; col < terrain.WidthCells; col++)
                {
                    Assert.That(loaded.GetCell(col, row), Is.EqualTo(terrain.GetCell(col, row)));
                }
            }
        }

        private static SparseGridLogicTerrainField CreateRawTerrain()
        {
            const int size = 8;
            var terrain = new SparseGridLogicTerrainField(size, size, chunkSizeCells: size);
            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++)
                {
                    int index = (row * size) + col;
                    byte height = (byte)((index & 1) == 0 ? 0 : 15);
                    byte water = height;
                    byte areaId = (byte)(((index * 37) + 11) & 0xFF);
                    var flags = (index & 2) == 0 ? LogicTerrainSurfaceFlags.Ramp : LogicTerrainSurfaceFlags.Blocked;
                    terrain.SetCell(col, row, new LogicTerrainCell(height, water, flags, areaId));
                }
            }

            return terrain;
        }

        private static SparseGridLogicTerrainField CreateRleTerrain()
        {
            const int size = 8;
            var terrain = new SparseGridLogicTerrainField(size, size, chunkSizeCells: size);
            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++)
                {
                    terrain.SetCell(col, row, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None, areaId: 7));
                }
            }

            return terrain;
        }

        private static SparseGridLogicTerrainField CreatePaletteTerrain()
        {
            const int size = 8;
            var terrain = new SparseGridLogicTerrainField(size, size, chunkSizeCells: size);
            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++)
                {
                    byte areaId = (byte)(((row + col) & 1) == 0 ? 1 : 2);
                    terrain.SetCell(col, row, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None, areaId));
                }
            }

            return terrain;
        }

        private static SparseGridLogicTerrainField CreateDeltaTerrain()
        {
            const int size = 8;
            var terrain = new SparseGridLogicTerrainField(size, size, chunkSizeCells: size);
            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++)
                {
                    byte areaId = (byte)((row * size) + col);
                    terrain.SetCell(col, row, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None, areaId));
                }
            }

            return terrain;
        }

        private static List<ChunkRecordInfo> ReadChunkRecords(byte[] bytes)
        {
            LogicTerrainBinaryMetadata metadata = LogicTerrainBinary.ReadMetadata(new MemoryStream(bytes));
            var records = new List<ChunkRecordInfo>(metadata.ChunkCount);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            reader.BaseStream.Position = HeaderBytes;
            for (int i = 0; i < metadata.ChunkCount; i++)
            {
                long chunkKey = reader.ReadInt64();
                var compressionMode = (LogicTerrainChunkCompressionMode)reader.ReadByte();
                int encodedBytes = reader.ReadInt32();
                records.Add(new ChunkRecordInfo(chunkKey, compressionMode, encodedBytes));
                reader.BaseStream.Position += encodedBytes;
            }

            Assert.That(reader.BaseStream.Position, Is.EqualTo(reader.BaseStream.Length));
            return records;
        }

        private static void RewriteChecksum(byte[] bytes)
        {
            for (int i = 0; i < ChecksumLength; i++)
            {
                bytes[ChecksumOffset + i] = 0;
            }

            ulong checksum = Fnv1a64(bytes);
            for (int i = 0; i < ChecksumLength; i++)
            {
                bytes[ChecksumOffset + i] = (byte)((checksum >> (8 * i)) & 0xFF);
            }
        }

        private static ulong Fnv1a64(byte[] data)
        {
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < data.Length; i++)
            {
                if (i >= ChecksumOffset && i < ChecksumOffset + ChecksumLength)
                {
                    continue;
                }

                hash ^= data[i];
                hash *= 1099511628211UL;
            }

            return hash;
        }

        private readonly struct ChunkRecordInfo
        {
            public ChunkRecordInfo(
                long chunkKey,
                LogicTerrainChunkCompressionMode compressionMode,
                int encodedBytes)
            {
                ChunkKey = chunkKey;
                CompressionMode = compressionMode;
                EncodedBytes = encodedBytes;
            }

            public long ChunkKey { get; }

            public LogicTerrainChunkCompressionMode CompressionMode { get; }

            public int EncodedBytes { get; }
        }
    }
}
