using Arch.Core;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class FogKnowledgeAndSnapshotTests
    {
        [Test]
        public void FogKnowledgeProjector_ProjectsLiveKnownHiddenAndAspectMasks()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity source = world.Create();
            Entity liveTarget = world.Create();
            Entity exploredTarget = world.Create();
            Entity hiddenTarget = world.Create();
            var knowledge = new KnowledgeProjectionStore();
            var projector = new FogKnowledgeProjector(knowledge);
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerDefinition layer = registry.Get(layerId);
            uint layerMask = registry.ToMask(layerId);
            var field = new FogField(1, in layer);
            field.SetVisible(new FogCell(0, 0));
            field.SetExplored(new FogCell(1, 0));
            field.SetDenied(new FogCell(2, 0));
            KnowledgeIdMask256 attributes = KnowledgeIdMask256.Empty.WithId(7);
            var policy = new FogProjectionPolicy(
                new FogDisclosurePolicy(attributes, KnowledgeIdMask256.Empty, KnowledgeIdMask256.Empty, ttlTicks: 0, trueSightRevealsConcealment: true),
                memoryTtlTicks: 5);
            FogOccupant[] occupants =
            {
                new(liveTarget, new WorldCmInt2(50, 50), layerMask),
                new(exploredTarget, new WorldCmInt2(150, 50), layerMask),
                new(hiddenTarget, new WorldCmInt2(250, 50), layerMask)
            };

            Assert.That(projector.Project(viewer, source, new WorldCmInt2(0, 0), field, occupants, policy, currentTick: 10), Is.EqualTo(3));

            Assert.That(knowledge.TryGet(viewer, liveTarget, currentTick: 10, out KnowledgeDisclosureRecord live), Is.True);
            Assert.That(knowledge.TryGet(viewer, exploredTarget, currentTick: 10, out KnowledgeDisclosureRecord explored), Is.True);
            Assert.That(knowledge.TryGet(viewer, hiddenTarget, currentTick: 10, out KnowledgeDisclosureRecord hidden), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(live.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
                Assert.That(live.Position, Is.EqualTo(KnowledgePositionAccess.Live));
                Assert.That(live.AttributeMask.ContainsId(7), Is.True);
                Assert.That(explored.Presence, Is.EqualTo(KnowledgePresence.Known));
                Assert.That(explored.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
                Assert.That(explored.ExpiryTick, Is.EqualTo(15));
                Assert.That(explored.AttributeMask.IsEmpty, Is.True);
                Assert.That(hidden.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));
                Assert.That(hidden.Position, Is.EqualTo(KnowledgePositionAccess.None));
                Assert.That(knowledge.TryGet(viewer, exploredTarget, currentTick: 15, out _), Is.False);
            });
        }

        [Test]
        public void FogKnowledgeProjector_AppliesConcealmentAndTrueSightDetection()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity source = world.Create();
            Entity concealedTarget = world.Create();
            Entity stealthTarget = world.Create();
            var knowledge = new KnowledgeProjectionStore();
            var map = new FogCellMap();
            map.SetConcealed(new FogCell(2, 0), true);
            var projector = new FogKnowledgeProjector(knowledge, map);
            var registry = new FogLayerRegistry();
            FogLayerId detectionId = registry.Register("detection", cellSizeCm: 100, updateHz: 10);
            FogLayerDefinition detection = registry.Get(detectionId);
            uint detectionMask = registry.ToMask(detectionId);
            var field = new FogField(1, in detection);
            field.SetVisible(new FogCell(2, 0));
            field.SetVisible(new FogCell(3, 0));
            var policy = new FogProjectionPolicy(
                new FogDisclosurePolicy(KnowledgeIdMask256.Empty.WithId(1), KnowledgeIdMask256.Empty, KnowledgeIdMask256.Empty, ttlTicks: 0, trueSightRevealsConcealment: true),
                memoryTtlTicks: 0);
            FogOccupant[] occupants =
            {
                new(concealedTarget, new WorldCmInt2(250, 50), detectionMask),
                new(stealthTarget, new WorldCmInt2(350, 50), detectionMask, stealthLevel: 2)
            };

            projector.Project(viewer, source, new WorldCmInt2(0, 0), field, occupants, policy, currentTick: 1, detectionStrength: 1);
            Assert.That(knowledge.TryGet(viewer, concealedTarget, 1, out KnowledgeDisclosureRecord concealedWithoutStrength), Is.True);
            Assert.That(knowledge.TryGet(viewer, stealthTarget, 1, out KnowledgeDisclosureRecord stealthWithoutStrength), Is.True);
            Assert.That(concealedWithoutStrength.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(stealthWithoutStrength.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));

            projector.Project(viewer, source, new WorldCmInt2(0, 0), field, occupants, policy, currentTick: 2, detectionStrength: 2);
            Assert.That(knowledge.TryGet(viewer, stealthTarget, 2, out KnowledgeDisclosureRecord stealthWithStrength), Is.True);
            Assert.That(stealthWithStrength.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
        }

        [Test]
        public void FogKnowledgeProjector_HidesConcealedOccupantWithoutTrueSightOrAdjacency()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity source = world.Create();
            Entity target = world.Create();
            var knowledge = new KnowledgeProjectionStore();
            var map = new FogCellMap();
            map.SetConcealed(new FogCell(2, 0), true);
            var projector = new FogKnowledgeProjector(knowledge, map);
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerDefinition layer = registry.Get(layerId);
            uint mask = registry.ToMask(layerId);
            var field = new FogField(1, in layer);
            field.SetVisible(new FogCell(2, 0));
            var policy = new FogProjectionPolicy(FogDisclosurePolicy.None, memoryTtlTicks: 0, trueSightRevealsConcealment: false);
            FogOccupant[] occupants = { new(target, new WorldCmInt2(250, 50), mask) };

            projector.Project(viewer, source, new WorldCmInt2(0, 0), field, occupants, policy, currentTick: 1, detectionStrength: 0);
            Assert.That(knowledge.TryGet(viewer, target, 1, out KnowledgeDisclosureRecord hidden), Is.True);
            Assert.That(hidden.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));

            projector.Project(viewer, source, new WorldCmInt2(150, 50), field, occupants, policy, currentTick: 2, detectionStrength: 0);
            Assert.That(knowledge.TryGet(viewer, target, 2, out KnowledgeDisclosureRecord adjacent), Is.True);
            Assert.That(adjacent.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
        }

        [Test]
        public void FogSnapshotStore_CapturesRestoresDiffsAndMergesExplored()
        {
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerDefinition layer = registry.Get(layerId);
            var field = new FogField(1, in layer);
            field.SetVisible(new FogCell(0, 0));
            field.SetExplored(new FogCell(1, 0));
            var store = new FogSnapshotStore();
            FogSnapshotHandle first = store.Capture(field, tick: 1);

            field.SetDenied(new FogCell(2, 0));
            FogSnapshotHandle second = store.Capture(field, tick: 2);
            Span<FogCell> changed = stackalloc FogCell[8];
            int diffCount = store.Diff(first, second, changed);

            var restored = new FogField(1, in layer);
            Assert.That(store.TryRestore(first, restored), Is.True);
            Assert.That(restored.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Visible));
            Assert.That(restored.GetVisibility(new FogCell(2, 0)), Is.EqualTo(CellVisibility.Unseen));
            Assert.That(diffCount, Is.EqualTo(1));
            Assert.That(changed[0], Is.EqualTo(new FogCell(2, 0)));

            var merged = new FogField(1, in layer);
            Assert.That(store.TryMergeExplored(first, second, merged), Is.True);
            Assert.That(merged.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Visible));
            Assert.That(merged.GetVisibility(new FogCell(1, 0)), Is.EqualTo(CellVisibility.Explored));
        }

        [Test]
        public void FogSnapshotStore_SharedExploredMergeIsRelationshipGatedAndDynamic()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out RelationshipTypeRegistry types);
            int sharedVisionTypeId = types.Register("SharedVision");
            Entity sourceHost = world.Create();
            Entity sharedHost = world.Create();
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerDefinition layer = registry.Get(layerId);
            var sourceField = new FogField(1, in layer);
            var sharedField = new FogField(1, in layer);
            sourceField.SetExplored(new FogCell(0, 0));
            sharedField.SetVisible(new FogCell(3, 0));
            var store = new FogSnapshotStore(relationships: relationships);
            FogSnapshotHandle sourceSnapshot = store.Capture(sourceField, tick: 1);
            FogSnapshotHandle sharedSnapshot = store.Capture(sharedField, tick: 1);
            var target = new FogField(1, in layer);

            Assert.That(
                store.TryMergeSharedExplored(
                    sourceSnapshot,
                    sharedSnapshot,
                    target,
                    sourceHost,
                    sharedHost,
                    sharedVisionTypeId),
                Is.False);
            Assert.That(target.GetVisibility(new FogCell(3, 0)), Is.EqualTo(CellVisibility.Unseen));

            relationships.EnsureLink(sourceHost, sharedHost, sharedVisionTypeId);
            Assert.That(
                store.TryMergeSharedExplored(
                    sourceSnapshot,
                    sharedSnapshot,
                    target,
                    sourceHost,
                    sharedHost,
                    sharedVisionTypeId),
                Is.True);
            Assert.That(target.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Explored));
            Assert.That(target.GetVisibility(new FogCell(3, 0)), Is.EqualTo(CellVisibility.Explored));

            relationships.RemoveLink(sourceHost, sharedHost, sharedVisionTypeId);
            var afterBreak = new FogField(1, in layer);
            Assert.That(
                store.TryMergeSharedExplored(
                    sourceSnapshot,
                    sharedSnapshot,
                    afterBreak,
                    sourceHost,
                    sharedHost,
                    sharedVisionTypeId),
                Is.False);
            Assert.That(afterBreak.GetVisibility(new FogCell(3, 0)), Is.EqualTo(CellVisibility.Unseen));
        }

        [Test]
        public void FogLayerRegistry_ReturnsStableLayerIdsAndMasks()
        {
            var registry = new FogLayerRegistry();
            FogLayerId ground = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerId sameGround = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerId air = registry.Register("air", cellSizeCm: 200, updateHz: 5);

            Assert.That(sameGround, Is.EqualTo(ground));
            Assert.That(registry.GetId("ground"), Is.EqualTo(ground));
            Assert.That(registry.ToMask(ground), Is.EqualTo(1u));
            Assert.That(registry.ToMask(air), Is.EqualTo(2u));
        }

        [Test]
        public void FogProjectionOccupants_OnlyExposeThroughMatchingLayers()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity source = world.Create();
            Entity ordinary = world.Create();
            Entity detectedOnly = world.Create();
            var knowledge = new KnowledgeProjectionStore();
            var projector = new FogKnowledgeProjector(knowledge);
            var registry = new FogLayerRegistry();
            FogLayerId groundId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerId detectionId = registry.Register("detection", cellSizeCm: 100, updateHz: 10);
            uint groundMask = registry.ToMask(groundId);
            uint detectionMask = registry.ToMask(detectionId);
            var groundField = new FogField(1, registry.Get(groundId));
            groundField.SetVisible(new FogCell(0, 0));
            FogOccupant[] occupants =
            {
                new(ordinary, new WorldCmInt2(50, 50), groundMask),
                new(detectedOnly, new WorldCmInt2(50, 50), detectionMask, stealthLevel: 1)
            };

            projector.Project(viewer, source, WorldCmInt2.Zero, groundField, occupants, FogProjectionPolicy.Default, currentTick: 1);

            Assert.That(knowledge.TryGet(viewer, ordinary, 1, out _), Is.True);
            Assert.That(knowledge.TryGet(viewer, detectedOnly, 1, out _), Is.False);
        }

        private static RelationshipRuntime CreateRelationshipRuntime(World world, out RelationshipTypeRegistry types)
        {
            types = new RelationshipTypeRegistry();
            return new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 4),
                new RelationshipReverseIndex(world));
        }
    }
}
