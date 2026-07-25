using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class SpatialPartitionMembershipHardeningTests
    {
        private World _world = null!;
        private ChunkedGridSpatialPartitionWorld _partition = null!;
        private WorldSizeSpec _spec;
        private SpatialPartitionUpdateSystem _membership = null!;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            _spec = new WorldSizeSpec(new WorldAabbCm(-50_000, -50_000, 100_000, 100_000), gridCellSizeCm: 100);
            _membership = new SpatialPartitionUpdateSystem(_world, _partition, _spec);
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        [Test]
        public void SetPartition_SameCellSize_ClearsOldPartitionAndFullyRebuildsNew()
        {
            Entity a = _world.Create(WorldPositionCm.FromCm(150, 250));
            Entity b = _world.Create(WorldPositionCm.FromCm(1250, 50));
            _membership.Update(0f);

            Assert.That(PartitionContains(_partition, a, 1, 2), Is.True);
            Assert.That(PartitionContains(_partition, b, 12, 0), Is.True);

            var oldPartition = _partition;
            var newPartition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            Entity stale = _world.Create();
            newPartition.Add(stale, 9, 9);
            var sameSpec = new WorldSizeSpec(new WorldAabbCm(-50_000, -50_000, 100_000, 100_000), gridCellSizeCm: 100);

            _membership.SetPartition(newPartition, sameSpec);
            _partition = newPartition;
            _spec = sameSpec;

            Assert.That(CountInCell(oldPartition, 1, 2), Is.EqualTo(0));
            Assert.That(CountInCell(oldPartition, 12, 0), Is.EqualTo(0));
            Assert.That(PartitionContains(newPartition, a, 1, 2), Is.True);
            Assert.That(PartitionContains(newPartition, b, 12, 0), Is.True);
            Assert.That(PartitionContains(newPartition, stale, 9, 9), Is.False);
            Assert.That(_world.Get<SpatialCellRef>(a).State, Is.EqualTo(SpatialMembershipState.Active));
            Assert.That(_world.Get<SpatialCellRef>(b).State, Is.EqualTo(SpatialMembershipState.Active));
        }

        [Test]
        public void SetPartition_DifferentCellSize_ClearsOldPartitionAndFullyRebuildsNewCells()
        {
            Entity a = _world.Create(WorldPositionCm.FromCm(150, 250));
            Entity b = _world.Create(WorldPositionCm.FromCm(1250, 50));
            _membership.Update(0f);

            Assert.That(PartitionContains(_partition, a, 1, 2), Is.True);
            Assert.That(PartitionContains(_partition, b, 12, 0), Is.True);

            var oldPartition = _partition;
            var newPartition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            var newSpec = new WorldSizeSpec(new WorldAabbCm(-50_000, -50_000, 100_000, 100_000), gridCellSizeCm: 250);

            _membership.SetPartition(newPartition, newSpec);
            _partition = newPartition;
            _spec = newSpec;

            Assert.That(CountInCell(oldPartition, 1, 2), Is.EqualTo(0));
            Assert.That(CountInCell(oldPartition, 12, 0), Is.EqualTo(0));
            Assert.That(PartitionContains(newPartition, a, 0, 1), Is.True);
            Assert.That(PartitionContains(newPartition, b, 5, 0), Is.True);
            Assert.That(PartitionContains(newPartition, a, 1, 2), Is.False);
            Assert.That(PartitionContains(newPartition, b, 12, 0), Is.False);
        }

        [Test]
        public void SetPartition_SuspendedEntities_AreNotRebuiltIntoNewPartition()
        {
            Entity active = _world.Create(WorldPositionCm.FromCm(150, 150));
            Entity suspended = _world.Create(WorldPositionCm.FromCm(350, 150));
            _membership.Update(0f);
            _world.Add(suspended, new SuspendedTag());

            var oldPartition = _partition;
            var newPartition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            _membership.SetPartition(newPartition, _spec);
            _partition = newPartition;

            Assert.That(CountInCell(oldPartition, 1, 1), Is.EqualTo(0));
            Assert.That(CountInCell(oldPartition, 3, 1), Is.EqualTo(0));
            Assert.That(PartitionContains(newPartition, active, 1, 1), Is.True);
            Assert.That(PartitionContains(newPartition, suspended, 3, 1), Is.False);
            Assert.That(_world.Get<SpatialCellRef>(suspended).State, Is.EqualTo(SpatialMembershipState.Uninitialized));

            _world.Remove<SuspendedTag>(suspended);
            _membership.Update(0f);

            Assert.That(PartitionContains(newPartition, suspended, 3, 1), Is.True);
            Assert.That(_world.Get<SpatialCellRef>(suspended).State, Is.EqualTo(SpatialMembershipState.Active));
        }

        [Test]
        public void Update_SuspendedActiveEntityLeavesPartitionAndReturnsAfterResume()
        {
            Entity entity = _world.Create(WorldPositionCm.FromCm(150, 150));
            _membership.Update(0f);
            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.True);

            _world.Add(entity, new SuspendedTag());
            _membership.Update(0f);

            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.False);
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Uninitialized));

            _world.Remove<SuspendedTag>(entity);
            _membership.Update(0f);

            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.True);
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Active));
        }

        [Test]
        public void SetPartition_InvalidEligiblePosition_DoesNotMutateEitherPartitionOrMembership()
        {
            Entity inside = _world.Create(WorldPositionCm.FromCm(150, 150));
            Entity outsideNewBounds = _world.Create(WorldPositionCm.FromCm(5_000, 150));
            _membership.Update(0f);
            var target = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            Entity targetSentinel = _world.Create();
            target.Add(targetSentinel, 9, 9);
            var smallerSpec = new WorldSizeSpec(new WorldAabbCm(-1_000, -1_000, 2_000, 2_000), gridCellSizeCm: 200);

            Assert.That(
                () => _membership.SetPartition(target, smallerSpec),
                Throws.InvalidOperationException.With.Message.Contains("SPATIAL.ERR.WorldPositionOutOfBounds"));

            Assert.That(PartitionContains(_partition, inside, 1, 1), Is.True);
            Assert.That(PartitionContains(_partition, outsideNewBounds, 50, 1), Is.True);
            Assert.That(PartitionContains(target, targetSentinel, 9, 9), Is.True);
            Assert.That(_world.Get<SpatialCellRef>(inside).State, Is.EqualTo(SpatialMembershipState.Active));
            Assert.That(_world.Get<SpatialCellRef>(outsideNewBounds).State, Is.EqualTo(SpatialMembershipState.Active));
        }

        [Test]
        public void Deactivate_DoesNotReactivateOnUpdate_AndSynchronizeRestoresActive()
        {
            Entity entity = _world.Create(WorldPositionCm.FromCm(150, 150));
            _membership.Update(0f);
            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.True);

            _membership.Deactivate(entity);
            Assert.That(_world.Has<SpatialCellRef>(entity), Is.True);
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Deactivated));
            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.False);
            Assert.That(_world.Has<SpatialPartitionExcluded>(entity), Is.False);

            _world.Set(entity, WorldPositionCm.FromCm(550, 150));
            _membership.Update(0f);
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Deactivated));
            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.False);
            Assert.That(PartitionContains(_partition, entity, 5, 1), Is.False);

            var replacement = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            _membership.SetPartition(replacement, _spec);
            _partition = replacement;
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Deactivated));
            Assert.That(PartitionContains(replacement, entity, 5, 1), Is.False);

            _membership.Synchronize(entity);
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Active));
            Assert.That(PartitionContains(_partition, entity, 5, 1), Is.True);
        }

        [Test]
        public void ConcealedEntity_DoesNotReappearUnderRealCameraCulling_UntilSynchronize()
        {
            var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(_partition, _spec));
            spatial.SetPositionProvider(e => _world.Get<WorldPositionCm>(e).Value.ToWorldCmInt2());
            spatial.SetCoordinateConverter(coords);

            Entity entity = _world.Create(
                WorldPositionCm.FromCm(100, 100),
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new VisualTransform
                {
                    Position = new Vector3(1f, 0f, 1f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f)));
            _membership.Update(0f);
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            appliers.Freeze();
            Entity viewer = _world.Create();
            var bridge = new ClientWorldReplicationBridge(
                _world,
                entityCapacity: 1,
                sessionEpoch: 7,
                appliers,
                _membership,
                new KnowledgeProjectionStore(initialCapacity: 1),
                viewer);
            var handle = new NetworkEntityHandle(0, 1);
            Assert.That(bridge.BindExisting(handle, entity), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.True);

            var camera = new CameraManager();
            camera.State.TargetCm = Vector2.Zero;
            camera.State.DistanceCm = 30000f;
            camera.State.Pitch = 45f;
            camera.State.FovYDeg = 60f;
            var view = new StubViewController();
            using var culling = new CameraCullingSystem(
                _world,
                camera,
                spatial,
                view,
                cullingConfig: new CameraCullingRuntimeConfig
                {
                    HighLodDistanceCm = 4000f,
                    MediumLodDistanceCm = 10000f,
                    LowLodDistanceCm = 20000f,
                });

            culling.Update(0.016f);
            Assert.That(_world.Get<CullState>(entity).IsVisible, Is.True);

            Assert.That(bridge.Clear(), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(_world.Has<SpatialPartitionExcluded>(entity), Is.False);
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Deactivated));

            _membership.Update(0.016f);
            culling.Update(0.016f);
            Assert.That(_world.Get<CullState>(entity).IsVisible, Is.False);
            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.False);

            Assert.That(
                bridge.BindExisting(new NetworkEntityHandle(0, 2), entity),
                Is.EqualTo(ReplicationBridgeResult.Success));
            _membership.Update(0.016f);
            culling.Update(0.016f);
            Assert.That(_world.Get<SpatialCellRef>(entity).State, Is.EqualTo(SpatialMembershipState.Active));
            Assert.That(PartitionContains(_partition, entity, 1, 1), Is.True);
            Assert.That(_world.Get<CullState>(entity).IsVisible, Is.True);
        }

        [Test]
        public void WarmedTenThousandCrossCellCopiedPositionUpdate_AllocatesZeroAndHasNoDuplicatesOrStaleCells()
        {
            const int count = 10_000;
            var entities = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                int x = (i % 100) * 100 + 50;
                int y = (i / 100) * 100 + 50;
                entities[i] = _world.Create(WorldPositionCm.FromCm(x, y));
            }

            _membership.Update(0f);

            ShiftAll(entities, dxCm: 500, dyCm: 0);
            _membership.Update(0f); // warm destination cells / chunk tables
            ShiftAll(entities, dxCm: -500, dyCm: 0);
            _membership.Update(0f); // warm return path
            ShiftAll(entities, dxCm: 500, dyCm: 0);

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            _membership.Update(0f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));

            var seen = new HashSet<Entity>();
            Entity[] scanBuffer = new Entity[count + 64];
            int n = _partition.Query(new IntRect(0, 0, 120, 120), scanBuffer, out int dropped);
            Assert.That(dropped, Is.EqualTo(0));
            for (int i = 0; i < n; i++)
            {
                Assert.That(seen.Add(scanBuffer[i]), Is.True, "Duplicate spatial membership entry.");
            }

            Assert.That(seen.Count, Is.EqualTo(count));
            for (int i = 0; i < count; i++)
            {
                Entity entity = entities[i];
                WorldCmInt2 worldCm = _world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2();
                (int cx, int cy) = (MathUtil.FloorDiv(worldCm.X, _spec.GridCellSizeCm), MathUtil.FloorDiv(worldCm.Y, _spec.GridCellSizeCm));
                Assert.That(PartitionContains(_partition, entity, cx, cy), Is.True);
                Assert.That(PartitionContains(_partition, entity, cx - 5, cy), Is.False);
            }
        }

        private void ShiftAll(Entity[] entities, int dxCm, int dyCm)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                ref WorldPositionCm pos = ref _world.Get<WorldPositionCm>(entities[i]);
                WorldCmInt2 worldCm = pos.Value.ToWorldCmInt2();
                pos = WorldPositionCm.FromCm(worldCm.X + dxCm, worldCm.Y + dyCm);
            }
        }

        private static bool PartitionContains(ISpatialPartitionWorld partition, Entity entity, int cellX, int cellY)
        {
            Span<Entity> buffer = stackalloc Entity[64];
            int count = partition.Query(new IntRect(cellX, cellY, 1, 1), buffer, out _);
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] == entity)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountInCell(ISpatialPartitionWorld partition, int cellX, int cellY)
        {
            Span<Entity> buffer = stackalloc Entity[64];
            return partition.Query(new IntRect(cellX, cellY, 1, 1), buffer, out _);
        }

        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution { get; set; } = new Vector2(1920, 1080);
            public float Fov { get; set; } = 60f;
            public float AspectRatio { get; set; } = 16f / 9f;
        }
    }
}
