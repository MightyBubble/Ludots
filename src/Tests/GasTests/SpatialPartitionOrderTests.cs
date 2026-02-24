using NUnit.Framework;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;

namespace GasTests
{
    [TestFixture]
    public sealed class SpatialPartitionOrderTests
    {
        private World _world;
        private ChunkedGridSpatialPartitionWorld _partition;
        private WorldSizeSpec _spec;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            _spec = new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), gridCellSizeCm: 100);
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        [Test]
        public void PhysicsSyncThenSpatialUpdate_PartitionMatchesNewWorldPositionSameTick()
        {
            var start = Fix64Vec2.FromInt(0, 0);
            var entity = _world.Create(
                new WorldPositionCm { Value = start },
                new Position2D { Value = start }
            );

            var physicsSync = new Physics2DToWorldPositionSyncSystem(_world);
            var spatialUpdate = new SpatialPartitionUpdateSystem(_world, _partition, _spec);

            var moved = Fix64Vec2.FromInt(500, 0);
            _world.Set(entity, new Position2D { Value = moved });

            physicsSync.Update(0.016f);
            spatialUpdate.Update(0.016f);

            (int cx, int cy) = WorldToCell(500, 0, _spec.GridCellSizeCm);
            Assert.That(PartitionContains(entity, cx, cy), Is.True);
        }

        [Test]
        public void SpatialUpdateBeforePhysicsSync_PartitionLagsAndDoesNotMatchNewPosition()
        {
            var start = Fix64Vec2.FromInt(0, 0);
            var entity = _world.Create(
                new WorldPositionCm { Value = start },
                new Position2D { Value = start }
            );

            var physicsSync = new Physics2DToWorldPositionSyncSystem(_world);
            var spatialUpdate = new SpatialPartitionUpdateSystem(_world, _partition, _spec);

            var moved = Fix64Vec2.FromInt(500, 0);
            _world.Set(entity, new Position2D { Value = moved });

            spatialUpdate.Update(0.016f);
            physicsSync.Update(0.016f);

            (int cxNew, int cyNew) = WorldToCell(500, 0, _spec.GridCellSizeCm);
            Assert.That(PartitionContains(entity, cxNew, cyNew), Is.False);
        }

        private bool PartitionContains(Entity e, int cellX, int cellY)
        {
            Span<Entity> buffer = stackalloc Entity[16];
            int count = _partition.Query(new IntRect(cellX, cellY, 1, 1), buffer, out _);
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] == e) return true;
            }
            return false;
        }

        private static (int x, int y) WorldToCell(int xcm, int ycm, int cellSizeCm)
        {
            return (MathUtil.FloorDiv(xcm, cellSizeCm), MathUtil.FloorDiv(ycm, cellSizeCm));
        }
    }
}
