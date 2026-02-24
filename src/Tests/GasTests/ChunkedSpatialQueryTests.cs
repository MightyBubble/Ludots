using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

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
    }
}

