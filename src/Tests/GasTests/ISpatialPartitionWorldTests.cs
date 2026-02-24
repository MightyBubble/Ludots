using System;
using NUnit.Framework;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace GasTests
{
    [TestFixture]
    public class ISpatialPartitionWorldTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        private static ISpatialPartitionWorld[] CreateBothImplementations()
        {
            return new ISpatialPartitionWorld[]
            {
                new GridSpatialPartitionWorld(cellSize: 1, initialCellCapacity: 64),
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4, initialChunkCapacity: 16),
            };
        }

        [Test]
        public void Add_And_Query_ReturnsSameEntity()
        {
            foreach (var partition in CreateBothImplementations())
            {
                var entity = _world.Create();
                partition.Add(entity, 5, 5);

                Span<Entity> buffer = stackalloc Entity[16];
                int count = partition.Query(new IntRect(5, 5, 1, 1), buffer, out int dropped);

                Assert.That(count, Is.EqualTo(1), $"Failed for {partition.GetType().Name}");
                Assert.That(buffer[0], Is.EqualTo(entity), $"Failed for {partition.GetType().Name}");
                Assert.That(dropped, Is.EqualTo(0));
            }
        }

        [Test]
        public void Remove_EntityNoLongerReturned()
        {
            foreach (var partition in CreateBothImplementations())
            {
                var entity = _world.Create();
                partition.Add(entity, 3, 3);
                partition.Remove(entity, 3, 3);

                Span<Entity> buffer = stackalloc Entity[16];
                int count = partition.Query(new IntRect(3, 3, 1, 1), buffer, out _);

                Assert.That(count, Is.EqualTo(0), $"Failed for {partition.GetType().Name}");
            }
        }

        [Test]
        public void Move_EntityBetweenCells()
        {
            foreach (var partition in CreateBothImplementations())
            {
                var entity = _world.Create();
                partition.Add(entity, 1, 1);
                partition.Remove(entity, 1, 1);
                partition.Add(entity, 10, 10);

                Span<Entity> buffer = stackalloc Entity[16];
                int oldCount = partition.Query(new IntRect(1, 1, 1, 1), buffer, out _);
                Assert.That(oldCount, Is.EqualTo(0), $"Old cell not empty for {partition.GetType().Name}");

                int newCount = partition.Query(new IntRect(10, 10, 1, 1), buffer, out _);
                Assert.That(newCount, Is.EqualTo(1), $"New cell empty for {partition.GetType().Name}");
                Assert.That(buffer[0], Is.EqualTo(entity));
            }
        }

        [Test]
        public void Query_CrossChunkBoundary_ReturnsAll()
        {
            // ChunkedGrid with chunkSize=4: entities at (3,3) and (4,4) are in different chunks
            var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4);
            var e1 = _world.Create();
            var e2 = _world.Create();
            partition.Add(e1, 3, 3);
            partition.Add(e2, 4, 4);

            Span<Entity> buffer = stackalloc Entity[16];
            int count = partition.Query(new IntRect(3, 3, 2, 2), buffer, out _);

            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void Query_EmptyRegion_ReturnsZero()
        {
            foreach (var partition in CreateBothImplementations())
            {
                Span<Entity> buffer = stackalloc Entity[16];
                int count = partition.Query(new IntRect(100, 100, 5, 5), buffer, out int dropped);

                Assert.That(count, Is.EqualTo(0), $"Failed for {partition.GetType().Name}");
                Assert.That(dropped, Is.EqualTo(0));
            }
        }

        [Test]
        public void Query_BufferOverflow_ReportsDropped()
        {
            foreach (var partition in CreateBothImplementations())
            {
                // Add 5 entities at same cell
                for (int i = 0; i < 5; i++)
                {
                    var e = _world.Create();
                    partition.Add(e, 0, 0);
                }

                // Query with buffer of 3
                Span<Entity> buffer = stackalloc Entity[3];
                int count = partition.Query(new IntRect(0, 0, 1, 1), buffer, out int dropped);

                Assert.That(count, Is.EqualTo(3), $"Failed for {partition.GetType().Name}");
                Assert.That(dropped, Is.EqualTo(2), $"Dropped mismatch for {partition.GetType().Name}");
            }
        }
    }
}
