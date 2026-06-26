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

            Assert.That(metadata.ChunkCount, Is.EqualTo(2));
            Assert.That(bytes.Length, Is.EqualTo(HeaderBytes + (metadata.ChunkCount * (ChunkRecordHeaderBytes + metadata.ChunkPayloadBytes))));
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
        private const int ChunkRecordHeaderBytes = 12;

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
    }
}
