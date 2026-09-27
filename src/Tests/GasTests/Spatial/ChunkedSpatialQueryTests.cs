using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ChunkedSpatialQueryTests
    {
        [Test]
        public void QueryAabb_SortsStable_And_Dedups_ForChunkedBackend()
        {
            var world = World.Create();
            try
            {
                var spec = new WorldSizeSpec(new WorldAabbCm(0, 0, 100000, 100000), gridCellSizeCm: 100);
                var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
                var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));

                var e2 = world.Create();
                var e1 = world.Create();
                var e3 = world.Create();

                partition.Add(e2, cellX: 0, cellY: 0);
                partition.Add(e1, cellX: 0, cellY: 0);
                partition.Add(e3, cellX: 0, cellY: 0);
                partition.Add(e3, cellX: 0, cellY: 0);

                Span<Entity> buffer = stackalloc Entity[256];
                var r = spatial.QueryAabb(new WorldAabbCm(0, 0, 100, 100), buffer);

                That(r.Dropped, Is.EqualTo(0));
                That(r.Count, Is.EqualTo(3));
                That(buffer[0].Id, Is.LessThan(buffer[1].Id));
                That(buffer[1].Id, Is.LessThan(buffer[2].Id));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void QueryAabb_CrossesChunkBoundary()
        {
            var world = World.Create();
            try
            {
                var spec = new WorldSizeSpec(new WorldAabbCm(-100000, -100000, 200000, 200000), gridCellSizeCm: 100);
                var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
                var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));

                var left = world.Create();
                var right = world.Create();

                partition.Add(left, cellX: 63, cellY: 0);
                partition.Add(right, cellX: 64, cellY: 0);

                Span<Entity> buffer = stackalloc Entity[8];
                var r = spatial.QueryAabb(new WorldAabbCm(6300, 0, 200, 100), buffer);

                That(r.Count, Is.EqualTo(2));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void Query_WorldSizedRect_EnumeratesStoredChunksInRowMajorOrder()
        {
            var world = World.Create();
            try
            {
                var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);

                // Scattered chunks across all four quadrants, one entity per occupied cell.
                // Row chunkY=0 mixes negative and non-negative chunkX: the sparse path's
                // sort must keep signed column order across the sign boundary.
                (int cellX, int cellY)[] cells =
                {
                    (-70, 66), (-5, 66), (0, -130), (100, -130), (-1, 0), (63, 0), (64, 0), (65, 1), (-200, 90),
                };
                var cellByEntity = new Dictionary<Entity, (int cellX, int cellY)>();
                foreach ((int cellX, int cellY) in cells)
                {
                    Entity entity = world.Create();
                    cellByEntity[entity] = (cellX, cellY);
                    partition.Add(entity, cellX, cellY);
                }

                // A world-sized rect covers billions of chunk addresses but stores nine
                // chunks; the query must enumerate stored chunks, in the dense path's order.
                var worldRect = new IntRect(-3_456_000, -3_456_000, 6_912_000, 6_912_000);
                Span<Entity> buffer = stackalloc Entity[cells.Length];
                int count = partition.Query(in worldRect, buffer, out int dropped);

                That(dropped, Is.EqualTo(0));
                That(count, Is.EqualTo(cellByEntity.Count));
                // Row-major chunk order (chunkY asc, chunkX asc), then cell order inside.
                (int cellX, int cellY)[] expectedOrder =
                {
                    (0, -130), (100, -130), (-1, 0), (63, 0), (64, 0), (65, 1), (-200, 90), (-70, 66), (-5, 66),
                };
                for (int i = 0; i < expectedOrder.Length; i++)
                {
                    That(cellByEntity[buffer[i]], Is.EqualTo(expectedOrder[i]),
                        $"result[{i}] must follow row-major chunk order");
                }
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void Query_WorldSizedRect_TruncatesWithDroppedCount_AndIsWarmAllocationFree()
        {
            var world = World.Create();
            try
            {
                var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
                for (int i = 0; i < 6; i++)
                {
                    Entity entity = world.Create();
                    partition.Add(entity, cellX: i, cellY: 0);
                }

                var worldRect = new IntRect(-1_000_000, -1_000_000, 2_000_000, 2_000_000);
                Span<Entity> small = stackalloc Entity[4];
                int count = partition.Query(in worldRect, small, out int dropped);

                That(count, Is.EqualTo(4));
                That(dropped, Is.EqualTo(2));

                Entity[] full = new Entity[6];
                partition.Query(in worldRect, full, out _);
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    partition.Query(in worldRect, full, out _);
                }
                That(GC.GetAllocatedBytesForCurrentThread() - bytes, Is.Zero,
                    "the sparse enumeration path must rent from the array pool, not allocate");
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}

