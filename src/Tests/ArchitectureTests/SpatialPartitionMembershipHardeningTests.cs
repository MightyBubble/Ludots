using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
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
        public void ValidateSynchronize_DefaultTarget_IsRejectedInsteadOfAssumingNoMembership()
        {
            SpatialMembershipTarget target = default;

            Assert.That(
                _membership.ValidateSynchronize(Entity.Null, in target),
                Is.EqualTo(SpatialMembershipValidationResult.InvalidTarget));
        }

        [Test]
        public void ValidateSynchronize_ProjectedPosition_IsBoundsCheckedEvenWhenEntityIsCurrentlyExcluded()
        {
            Entity entity = _world.Create(
                WorldPositionCm.FromCm(150, 150),
                new SpatialPartitionExcluded());
            var outOfBounds = new WorldCmInt2(60_000, 150);
            SpatialMembershipTarget target = SpatialMembershipTarget.At(in outOfBounds);

            Assert.That(
                _membership.ValidateSynchronize(entity, in target),
                Is.EqualTo(SpatialMembershipValidationResult.PositionOutOfBounds));
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
        public void BindExisting_OutOfBounds_RejectsBeforeAddingMirrorComponentsOrSlot()
        {
            Entity authored = _world.Create(WorldPositionCm.FromCm(60_000, 150));
            ClientWorldReplicationBridge bridge = CreatePositionBridge(
                entityCapacity: 1,
                out _,
                out _);
            var handle = new NetworkEntityHandle(0, 1);

            Assert.That(
                bridge.BindExisting(handle, authored),
                Is.EqualTo(ReplicationBridgeResult.SpatialApplyRejected));

            Assert.Multiple(() =>
            {
                Assert.That(_world.Has<ReplicationMirrorIdentity>(authored), Is.False);
                Assert.That(_world.Has<ReplicationMirrorState>(authored), Is.False);
                Assert.That(_world.Has<SpatialCellRef>(authored), Is.False);
                Assert.That(bridge.TryResolve(handle, out _), Is.False);
                Assert.That(bridge.LastSnapshotId, Is.Zero);
            });
        }

        [Test]
        public void Apply_OutOfBoundsCreate_RejectsWithoutEntitySlotKnowledgeOrBaselineMutation()
        {
            ClientWorldReplicationBridge bridge = CreatePositionBridge(
                entityCapacity: 1,
                out _,
                out _);
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(entityCapacity: 1);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { PositionState(handle, revision: 1, xCm: 60_000, yCm: 150) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };

            Assert.That(
                channel.BuildFull(7, 1, 1, states, visible, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.SpatialApplyRejected));

            var mirrorQuery = new QueryDescription().WithAll<ReplicationMirrorIdentity>();
            Assert.Multiple(() =>
            {
                Assert.That(_world.CountEntities(in mirrorQuery), Is.Zero);
                Assert.That(bridge.TryResolve(handle, out _), Is.False);
                Assert.That(bridge.LastSnapshotId, Is.Zero);
                Assert.That(bridge.HasPreparedBatch, Is.False);
            });
        }

        [Test]
        public void Apply_OutOfBoundsUpdate_PreservesEcsPartitionKnowledgeAndBaseline()
        {
            ClientWorldReplicationBridge bridge = CreatePositionBridge(
                entityCapacity: 1,
                out KnowledgeProjectionStore knowledge,
                out Entity viewer);
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(entityCapacity: 1);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { PositionState(handle, revision: 1, xCm: 150, yCm: 150) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };

            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity mirror), Is.True);
            Assert.That(PartitionContains(_partition, mirror, 1, 1), Is.True);

            states[0] = PositionState(handle, revision: 2, xCm: 60_000, yCm: 150);
            Assert.That(channel.BuildDelta(7, 2, 2, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.SpatialApplyRejected));

            Assert.Multiple(() =>
            {
                Assert.That(bridge.TryResolve(handle, out Entity unchanged), Is.True);
                Assert.That(unchanged, Is.EqualTo(mirror));
                Assert.That(_world.Get<WorldPositionCm>(mirror), Is.EqualTo(WorldPositionCm.FromCm(150, 150)));
                Assert.That(_world.Get<ReplicationMirrorState>(mirror).Revision, Is.EqualTo(1));
                Assert.That(PartitionContains(_partition, mirror, 1, 1), Is.True);
                Assert.That(PartitionContains(_partition, mirror, 600, 1), Is.False);
                Assert.That(knowledge.TryGet(viewer, mirror, currentTick: 2, out KnowledgeDisclosureRecord disclosure), Is.True);
                Assert.That(disclosure.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
                Assert.That(bridge.LastSnapshotId, Is.EqualTo(1));
                Assert.That(bridge.HasPreparedBatch, Is.False);
            });

            states[0] = PositionState(handle, revision: 3, xCm: 250, yCm: 150);
            Assert.That(channel.BuildDelta(7, 3, 3, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(_world.Get<WorldPositionCm>(mirror), Is.EqualTo(WorldPositionCm.FromCm(250, 150)));
            Assert.That(PartitionContains(_partition, mirror, 2, 1), Is.True);
        }

        [Test]
        public void Apply_LateOutOfBoundsCreate_RejectsWholeBatchBeforeEarlierRelease()
        {
            ClientWorldReplicationBridge bridge = CreatePositionBridge(
                entityCapacity: 2,
                out KnowledgeProjectionStore knowledge,
                out Entity viewer);
            var channel = Channel(capacity: 2);
            var packet = new ReplicationPacketBuffer(entityCapacity: 2);
            var first = new NetworkEntityHandle(0, 1);
            var second = new NetworkEntityHandle(1, 1);
            var states = new[] { PositionState(first, revision: 1, xCm: 150, yCm: 150) };
            var visible = new[] { new ReplicationDisclosureInput(first, KnowledgePresence.LiveVisible) };

            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(first, out Entity firstMirror), Is.True);

            states[0] = PositionState(second, revision: 1, xCm: 60_000, yCm: 150);
            visible[0] = new ReplicationDisclosureInput(second, KnowledgePresence.LiveVisible);
            Assert.That(channel.BuildFull(7, 2, 2, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.SpatialApplyRejected));

            Assert.Multiple(() =>
            {
                Assert.That(bridge.TryResolve(first, out Entity unchanged), Is.True);
                Assert.That(unchanged, Is.EqualTo(firstMirror));
                Assert.That(_world.IsAlive(firstMirror), Is.True);
                Assert.That(PartitionContains(_partition, firstMirror, 1, 1), Is.True);
                Assert.That(knowledge.TryGet(viewer, firstMirror, currentTick: 2, out _), Is.True);
                Assert.That(bridge.TryResolve(second, out _), Is.False);
                Assert.That(bridge.LastSnapshotId, Is.EqualTo(1));
            });
        }

        [Test]
        public void Apply_InvalidMembershipConceal_DoesNotConcealMirrorOrPoisonSlot()
        {
            Entity authored = _world.Create(WorldPositionCm.FromCm(150, 150));
            _membership.Update(0f);
            ClientWorldReplicationBridge bridge = CreatePositionBridge(
                entityCapacity: 1,
                out KnowledgeProjectionStore knowledge,
                out Entity viewer);
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(entityCapacity: 1);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { PositionState(handle, revision: 1, xCm: 150, yCm: 150) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };

            Assert.That(bridge.BindExisting(handle, authored), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity mirror), Is.True);
            Assert.That(mirror, Is.EqualTo(authored));
            _world.Get<SpatialCellRef>(mirror).State = (SpatialMembershipState)255;

            var known = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.Known) };
            Assert.That(channel.BuildDelta(7, 2, 2, 1, states, known, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.EcsStateMismatch));

            Assert.Multiple(() =>
            {
                Assert.That(bridge.TryResolve(handle, out Entity unchanged), Is.True);
                Assert.That(unchanged, Is.EqualTo(mirror));
                Assert.That(_world.IsAlive(mirror), Is.True);
                Assert.That(_world.Has<ReplicationMirrorIdentity, ReplicationMirrorState>(mirror), Is.True);
                Assert.That(_world.Get<WorldPositionCm>(mirror), Is.EqualTo(WorldPositionCm.FromCm(150, 150)));
                Assert.That(knowledge.TryGet(viewer, mirror, currentTick: 2, out KnowledgeDisclosureRecord disclosure), Is.True);
                Assert.That(disclosure.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
                Assert.That(bridge.LastSnapshotId, Is.EqualTo(1));
            });
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

            for (int i = 0; i < 64; i++)
            {
                ShiftAll(entities, dxCm: 500, dyCm: 0);
                _membership.Update(0f);
                ShiftAll(entities, dxCm: -500, dyCm: 0);
                _membership.Update(0f);
            }

            ShiftAll(entities, dxCm: 500, dyCm: 0);
            _ = MeasureUpdateAllocations(_membership);
            ShiftAll(entities, dxCm: -500, dyCm: 0);
            _ = MeasureUpdateAllocations(_membership);

            ShiftAll(entities, dxCm: 500, dyCm: 0);
            long forwardAllocated = MeasureUpdateAllocations(_membership);
            ShiftAll(entities, dxCm: -500, dyCm: 0);
            long backwardAllocated = MeasureUpdateAllocations(_membership);
            Assert.Multiple(() =>
            {
                Assert.That(forwardAllocated, Is.EqualTo(0));
                Assert.That(backwardAllocated, Is.EqualTo(0));
            });

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

        private ClientWorldReplicationBridge CreatePositionBridge(
            int entityCapacity,
            out KnowledgeProjectionStore knowledge,
            out Entity viewer)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            Assert.That(
                appliers.Register(1, new PositionReplicationApplier()),
                Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            knowledge = new KnowledgeProjectionStore(initialCapacity: entityCapacity);
            viewer = _world.Create();
            return new ClientWorldReplicationBridge(
                _world,
                entityCapacity,
                sessionEpoch: 7,
                appliers,
                _membership,
                knowledge,
                viewer);
        }

        private static AuthoritativeReplicationChannel Channel(int capacity) =>
            new(capacity, baselineCapacity: 2, new ReplicationDisclosureChangeLog(capacity * 4));

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MeasureUpdateAllocations(SpatialPartitionUpdateSystem membership)
        {
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            membership.Update(0f);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static ReplicatedEntityState PositionState(
            NetworkEntityHandle handle,
            uint revision,
            int xCm,
            int yCm) =>
            new(handle, schemaId: 1, revision, new ReplicationStateVector(xCm, yCm, 0, 0));

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

        private sealed class PositionReplicationApplier : IClientReplicationSchemaApplier
        {
            public bool CanCreate(World world, in ReplicatedEntityState state) =>
                world != null && TryDecode(in state, out _);

            public bool CanApply(World world, Entity entity, in ReplicatedEntityState state) =>
                world != null &&
                world.IsAlive(entity) &&
                world.Has<WorldPositionCm>(entity) &&
                TryDecode(in state, out _);

            public bool CanConceal(World world, Entity entity) =>
                world != null && world.IsAlive(entity) && world.Has<WorldPositionCm>(entity);

            public bool TryPreviewSpatialMembership(
                World world,
                Entity entity,
                in ReplicatedEntityState state,
                out SpatialMembershipTarget target)
            {
                target = default;
                if (world == null || !TryDecode(in state, out WorldPositionCm position))
                {
                    return false;
                }

                WorldCmInt2 positionCm = position.Value.ToWorldCmInt2();
                target = SpatialMembershipTarget.At(in positionCm);
                return true;
            }

            public Entity Create(
                World world,
                in ReplicationMirrorIdentity identity,
                in ReplicationMirrorState state)
            {
                ReplicationStateVector values = state.Values;
                var replicated = new ReplicatedEntityState(identity.Handle, state.SchemaId, state.Revision, in values);
                if (!TryDecode(in replicated, out WorldPositionCm position))
                {
                    throw new InvalidOperationException("Validated position replication create payload is invalid.");
                }

                return world.Create(in identity, in state, in position);
            }

            public void Apply(World world, Entity entity, in ReplicatedEntityState state)
            {
                if (!TryDecode(in state, out WorldPositionCm position))
                {
                    throw new InvalidOperationException("Validated position replication update payload is invalid.");
                }

                world.Set(entity, in position);
            }

            public void Conceal(World world, Entity entity)
            {
                WorldPositionCm concealed = WorldPositionCm.FromCm(0, 0);
                world.Set(entity, in concealed);
            }

            private static bool TryDecode(in ReplicatedEntityState state, out WorldPositionCm position)
            {
                if (state.SchemaId != 1 ||
                    state.Values.Value0 < int.MinValue ||
                    state.Values.Value0 > int.MaxValue ||
                    state.Values.Value1 < int.MinValue ||
                    state.Values.Value1 > int.MaxValue)
                {
                    position = default;
                    return false;
                }

                position = WorldPositionCm.FromCm((int)state.Values.Value0, (int)state.Values.Value1);
                return true;
            }
        }
    }
}
