using System;
using System.Linq;
using System.Reflection;
using Ludots.Core.Map.Fields;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LogicTerrainSoAStoreTests
    {
        [Test]
        public void SparseGrid_DoesNotInstantiateDefaultChunks_AndTracksDirtyPerChunk()
        {
            var terrain = new SparseGridLogicTerrainField(4096, 4096);

            Assert.That(terrain.ResidentChunkCount, Is.EqualTo(0));
            Assert.That(terrain.GetCell(2048, 2048), Is.EqualTo(LogicTerrainCell.Default));

            terrain.SetCell(64, 64, LogicTerrainCell.Default);
            Assert.That(terrain.ResidentChunkCount, Is.EqualTo(0));

            terrain.SetCell(0, 0, new LogicTerrainCell(3, 0, LogicTerrainSurfaceFlags.Blocked, areaId: 7));
            terrain.SetCell(4095, 4095, new LogicTerrainCell(9, 1, LogicTerrainSurfaceFlags.Ramp, areaId: 2));

            Assert.That(terrain.ResidentChunkCount, Is.EqualTo(2));
            Assert.That(terrain.IsChunkResident(0, 0), Is.True);
            Assert.That(terrain.IsChunkResident(63, 63), Is.True);
            Assert.That(terrain.IsChunkDirty(0, 0), Is.True);

            terrain.ClearChunkDirty(0, 0);

            Assert.That(terrain.IsChunkDirty(0, 0), Is.False);
            Assert.That(terrain.IsChunkDirty(63, 63), Is.True);
        }

        [Test]
        public void SparseGrid_RandomReadWrite_MatchesDenseReference()
        {
            const int width = 257;
            const int height = 131;
            var terrain = new SparseGridLogicTerrainField(width, height);
            var dense = new LogicTerrainCell[width * height];
            Array.Fill(dense, LogicTerrainCell.Default);
            var rng = new Random(406);

            for (int i = 0; i < 700; i++)
            {
                int col = rng.Next(width);
                int row = rng.Next(height);
                var flags = LogicTerrainSurfaceFlags.None;
                if ((rng.Next() & 1) != 0) flags |= LogicTerrainSurfaceFlags.Ramp;
                if ((rng.Next() & 2) != 0) flags |= LogicTerrainSurfaceFlags.Blocked;
                byte heightLevel = (byte)rng.Next(SpatialScaleDefaults.LogicTerrainHeightLevels);
                byte waterLevel = (byte)rng.Next(SpatialScaleDefaults.LogicTerrainHeightLevels);
                if (waterLevel > heightLevel) flags |= LogicTerrainSurfaceFlags.Water;
                var value = new LogicTerrainCell(
                    heightLevel,
                    waterLevel,
                    flags,
                    areaId: (byte)rng.Next(byte.MaxValue + 1),
                    cost: 0.5f + (float)rng.NextDouble() * 8f);

                terrain.SetCell(col, row, value);
                dense[row * width + col] = value;
            }

            for (int i = 0; i < 1000; i++)
            {
                int col = rng.Next(width);
                int row = rng.Next(height);
                Assert.That(terrain.GetCell(col, row), Is.EqualTo(dense[row * width + col]));
            }
        }

        [Test]
        public void LogicTerrainTypes_LiveInMapDomain_AndChunkStorageIsNotAoS()
        {
            Assert.That(typeof(LogicTerrainField).Namespace, Is.EqualTo("Ludots.Core.Map.Fields"));
            Assert.That(typeof(SparseGridLogicTerrainField).Namespace, Is.EqualTo("Ludots.Core.Map.Fields"));

            FieldInfo[] sparseFields = typeof(SparseGridLogicTerrainField)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Type cellArrayType = typeof(LogicTerrainCell).MakeArrayType();
            Assert.That(sparseFields.Any(f => f.FieldType == cellArrayType), Is.False);

            Type? chunkType = typeof(LogicTerrainField).Assembly.GetType("Ludots.Core.Map.Fields.LogicTerrainChunk");
            Assert.That(chunkType, Is.Not.Null);
            FieldInfo[] chunkFields = chunkType!.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(chunkFields.Any(f => f.FieldType == cellArrayType), Is.False);
            Assert.That(chunkFields.Any(f => f.Name.Contains("height", StringComparison.OrdinalIgnoreCase) && f.FieldType == typeof(byte[])), Is.True);
            Assert.That(chunkFields.Any(f => f.Name.Contains("area", StringComparison.OrdinalIgnoreCase) && f.FieldType == typeof(byte[])), Is.True);
            Assert.That(chunkFields.Any(f => f.Name.Contains("flag", StringComparison.OrdinalIgnoreCase) && f.FieldType == typeof(ulong[])), Is.True);
        }

        [Test]
        public void ResidentHotPaths_GetSetAndSampleEquivalentOperations_AreAllocationFree()
        {
            var terrain = new SparseGridLogicTerrainField(128, 128);
            var value = new LogicTerrainCell(7, 0, LogicTerrainSurfaceFlags.Ramp, areaId: 9, cost: 3f);
            terrain.SetCell(5, 5, value);
            terrain.ClearDirty();

            for (int i = 0; i < 64; i++)
            {
                terrain.SetCell(5, 5, value);
                _ = terrain.GetCell(5, 5);
                terrain.GetWorldPositionMeters(5, 5, out _, out _);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                terrain.SetCell(5, 5, value);
                _ = terrain.GetCell(5, 5);
                terrain.GetWorldPositionMeters(5, 5, out _, out _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
