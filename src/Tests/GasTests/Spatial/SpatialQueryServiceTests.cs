using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class SpatialQueryServiceTests
    {
        [Test]
        public void QueryAabb_SortsStable_And_Dedups()
        {
            var world = World.Create();
            try
            {
                var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
                var grid = new GridSpatialPartitionWorld(cellSize: 1);
                var spatial = new SpatialQueryService(new GridSpatialPartitionBackend(grid, coords));

                var e2 = world.Create();
                var e1 = world.Create();
                var e3 = world.Create();

                grid.Add(e2, new IntRect(0, 0, 2, 2));
                grid.Add(e1, new IntRect(1, 0, 2, 2));
                grid.Add(e3, new IntRect(0, 1, 2, 2));

                Span<Entity> buffer = stackalloc Entity[256];
                var r = spatial.QueryAabb(new WorldAabbCm(0, 0, 300, 300), buffer);

                That(r.Dropped, Is.EqualTo(0));
                That(r.Count, Is.EqualTo(3));
                That(buffer[0].Id, Is.LessThan(buffer[1].Id));
                That(buffer[1].Id, Is.LessThan(buffer[2].Id));
                That(buffer[0], Is.EqualTo(e2));
                That(buffer[1], Is.EqualTo(e1));
                That(buffer[2], Is.EqualTo(e3));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void QueryAabb_WhenBufferFull_DropsExtraResults_AndIsObservable()
        {
            var world = World.Create();
            try
            {
                var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
                var grid = new GridSpatialPartitionWorld(cellSize: 64);
                var spatial = new SpatialQueryService(new GridSpatialPartitionBackend(grid, coords));

                const int total = 10;
                for (int i = 0; i < total; i++)
                {
                    var e = world.Create();
                    grid.Add(e, new IntRect(0, 0, 0, 0));
                }

                Span<Entity> buffer = stackalloc Entity[4];
                var r = spatial.QueryAabb(new WorldAabbCm(0, 0, 100, 100), buffer);

                That(r.Count, Is.EqualTo(4));
                That(r.Dropped, Is.EqualTo(6));
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
