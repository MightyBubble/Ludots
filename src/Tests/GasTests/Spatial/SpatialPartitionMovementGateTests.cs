using NUnit.Framework;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace GasTests
{
    [TestFixture]
    public sealed class SpatialPartitionMovementGateTests
    {
        private sealed class CountingPartitionWorld : ISpatialPartitionWorld
        {
            private readonly ISpatialPartitionWorld _inner;
            public int AddCount;
            public int RemoveCount;

            public CountingPartitionWorld(ISpatialPartitionWorld inner)
            {
                _inner = inner;
            }

            public void Add(Entity entity, int cellX, int cellY)
            {
                AddCount++;
                _inner.Add(entity, cellX, cellY);
            }

            public void Remove(Entity entity, int cellX, int cellY)
            {
                RemoveCount++;
                _inner.Remove(entity, cellX, cellY);
            }

            public int Query(in IntRect cellRect, Span<Entity> buffer, out int dropped)
            {
                return _inner.Query(in cellRect, buffer, out dropped);
            }

            public void Clear()
            {
                _inner.Clear();
            }
        }

        private World _world;
        private CountingPartitionWorld _partition;
        private WorldSizeSpec _spec;
        private SavePreviousWorldPositionSystem _savePrevious;
        private SpatialPartitionUpdateSystem _spatialUpdate;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _partition = new CountingPartitionWorld(new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64));
            _spec = new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), gridCellSizeCm: 100);
            _savePrevious = new SavePreviousWorldPositionSystem(_world);
            _spatialUpdate = new SpatialPartitionUpdateSystem(_world, _partition, _spec);
        }

        [TearDown]
        public void TearDown()
        {
            _spatialUpdate?.Dispose();
            _savePrevious?.Dispose();
            _world?.Dispose();
        }

        [Test]
        public void StaticEntityWithPreviousPosition_PerformsZeroPartitionOpsAcrossTicks()
        {
            Entity entity = _world.Create(
                new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) },
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) });

            for (int tick = 0; tick < 8; tick++)
            {
                _savePrevious.Update(0f);
                _spatialUpdate.Update(0f);
            }

            Assert.That(_partition.AddCount, Is.EqualTo(1), "static entity must only pay the initial membership Add");
            Assert.That(_partition.RemoveCount, Is.EqualTo(0));
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Active));
            Assert.That(PartitionContains(entity, 1, 2), Is.True);
        }

        [Test]
        public void MovingEntityWithPreviousPosition_CrossesCellThenStopsPayingWhenStatic()
        {
            Entity entity = _world.Create(
                new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) },
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) });

            _savePrevious.Update(0f);
            _spatialUpdate.Update(0f);
            Assert.That(_partition.AddCount, Is.EqualTo(1));

            _savePrevious.Update(0f);
            _world.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(450, 250) });
            _spatialUpdate.Update(0f);

            Assert.That(_partition.AddCount, Is.EqualTo(2), "crossing a cell boundary must still Add the new membership");
            Assert.That(_partition.RemoveCount, Is.EqualTo(1));
            Assert.That(PartitionContains(entity, 4, 2), Is.True);

            for (int tick = 0; tick < 5; tick++)
            {
                _savePrevious.Update(0f);
                _spatialUpdate.Update(0f);
            }

            Assert.That(_partition.AddCount, Is.EqualTo(2), "static ticks after the move must perform zero partition ops");
            Assert.That(_partition.RemoveCount, Is.EqualTo(1));
            Assert.That(_world.Get<SpatialCellRef>(entity).CellX, Is.EqualTo(4));
            Assert.That(_world.Get<SpatialCellRef>(entity).CellY, Is.EqualTo(2));
        }

        [Test]
        public void SuspendedThenResumedStaticEntity_StillReactivatesMembershipThroughGate()
        {
            Entity entity = _world.Create(
                new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) },
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) });

            _savePrevious.Update(0f);
            _spatialUpdate.Update(0f);
            Assert.That(_partition.AddCount, Is.EqualTo(1));

            _world.Add(entity, new SuspendedTag());
            _spatialUpdate.Update(0f);
            Assert.That(_partition.RemoveCount, Is.EqualTo(1));
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Uninitialized));

            _world.Remove<SuspendedTag>(entity);
            _savePrevious.Update(0f);
            _spatialUpdate.Update(0f);

            Assert.That(_partition.AddCount, Is.EqualTo(2), "resumed Uninitialized membership must reactivate even without movement");
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Active));
            Assert.That(PartitionContains(entity, 1, 2), Is.True);
        }

        [Test]
        public void EntityWithoutPreviousPosition_KeepsUngatedMoveBehavior()
        {
            Entity entity = _world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) });

            _spatialUpdate.Update(0f);
            Assert.That(_partition.AddCount, Is.EqualTo(1));

            _world.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(450, 250) });
            _spatialUpdate.Update(0f);

            Assert.That(_partition.AddCount, Is.EqualTo(2));
            Assert.That(_partition.RemoveCount, Is.EqualTo(1));
            Assert.That(PartitionContains(entity, 4, 2), Is.True);
        }

        [Test]
        public void MovingEntityWriterAfterPartition_RehomesAcrossCellsEveryTick()
        {
            Entity entity = _world.Create(
                new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) },
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(150, 250) });

            // 引擎真实调度：SavePrevious → SpatialPartition → 位置写者（massnav / GAS 位移）。
            // 门控若依赖 Previous==Current 快照，此序下判据恒真，格籍冻结在初始格。
            _savePrevious.Update(0f);
            _spatialUpdate.Update(0f);
            _world.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(450, 250) });

            _savePrevious.Update(0f);
            _spatialUpdate.Update(0f);
            _world.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(750, 250) });

            _savePrevious.Update(0f);
            _spatialUpdate.Update(0f);

            Assert.That(_world.Get<SpatialCellRef>(entity).CellX, Is.EqualTo(7),
                "writer-after-partition schedule must still re-home: membership cell tracks the current position");
            Assert.That(PartitionContains(entity, 7, 2), Is.True);
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
    }
}
