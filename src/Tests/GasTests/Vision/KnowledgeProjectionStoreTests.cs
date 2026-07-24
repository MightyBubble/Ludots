using Arch.Core;
using Ludots.Core.Knowledge;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class KnowledgeProjectionStoreTests
    {
        [Test]
        public void UpsertAndTryGet_KeyByViewerEntityAndTargetEntity()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity otherViewer = world.Create();
            Entity target = world.Create();
            Entity source = world.Create();
            var store = new KnowledgeProjectionStore(initialCapacity: 2);
            KnowledgeDisclosureRecord record = new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                KnowledgeIdMask256.Empty.WithId(1),
                KnowledgeIdMask256.Empty.WithId(2),
                KnowledgeIdMask256.Empty.WithId(3),
                source,
                observedTick: 100,
                expiryTick: 140,
                confidencePermille: 900,
                revision: 7);

            Assert.That(store.Upsert(viewer, target, record), Is.EqualTo(1u));

            Assert.That(store.TryGet(viewer, target, currentTick: 120, out KnowledgeDisclosureRecord resolved), Is.True);
            Assert.That(resolved.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(resolved.Position, Is.EqualTo(KnowledgePositionAccess.Live));
            Assert.That(resolved.AttributeMask.ContainsId(1), Is.True);
            Assert.That(resolved.RelationshipTypeMask.ContainsId(2), Is.True);
            Assert.That(resolved.TagMask.ContainsId(3), Is.True);
            Assert.That(resolved.Source, Is.EqualTo(source));
            Assert.That(resolved.ObservedTick, Is.EqualTo(100));
            Assert.That(resolved.ExpiryTick, Is.EqualTo(140));
            Assert.That(resolved.ConfidencePermille, Is.EqualTo(900));
            Assert.That(resolved.Revision, Is.EqualTo(1u));
            Assert.That(store.TryGet(otherViewer, target, currentTick: 120, out _), Is.False);
        }

        [Test]
        public void Store_TreatsTeamAndNpcViewersAsEntities_AndExpiresRecords()
        {
            using World world = World.Create();
            Entity teamViewer = world.Create();
            Entity npcViewer = world.Create();
            Entity target = world.Create();
            Entity teamSource = world.Create();
            Entity npcSource = world.Create();
            var store = new KnowledgeProjectionStore(initialCapacity: 2);

            uint teamRevision = store.Upsert(
                teamViewer,
                target,
                CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, teamSource, observedTick: 5, expiryTick: 10, confidencePermille: 700));
            uint npcRevision = store.Upsert(
                npcViewer,
                target,
                CreateRecord(KnowledgePresence.HiddenWithSource, KnowledgePositionAccess.None, npcSource, observedTick: 6, expiryTick: 0, confidencePermille: 500));

            Assert.That(teamRevision, Is.EqualTo(1u));
            Assert.That(npcRevision, Is.EqualTo(1u));
            Assert.That(store.RecordCount, Is.EqualTo(2));
            Assert.That(store.TryGet(teamViewer, target, currentTick: 9, out KnowledgeDisclosureRecord teamRecord), Is.True);
            Assert.That(teamRecord.Source, Is.EqualTo(teamSource));
            Assert.That(teamRecord.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
            Assert.That(store.TryGet(teamViewer, target, currentTick: 10, out _), Is.False);
            Assert.That(store.TryGet(npcViewer, target, currentTick: 10, out KnowledgeDisclosureRecord npcRecord), Is.True);
            Assert.That(npcRecord.Source, Is.EqualTo(npcSource));
            Assert.That(npcRecord.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));

            Assert.That(store.Expire(currentTick: 10), Is.EqualTo(1));
            Assert.That(store.RecordCount, Is.EqualTo(1));
            Assert.That(store.TryGet(teamViewer, target, currentTick: 11, out _), Is.False);
            Assert.That(store.TryGet(npcViewer, target, currentTick: 11, out _), Is.True);
        }

        [Test]
        public void Upsert_PreservesRevisionForSameContent_AndBumpsRevisionForChanges()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity target = world.Create();
            Entity source = world.Create();
            var store = new KnowledgeProjectionStore();
            KnowledgeDisclosureRecord first = CreateRecord(
                KnowledgePresence.Known,
                KnowledgePositionAccess.LastKnown,
                source,
                observedTick: 1,
                expiryTick: 20,
                confidencePermille: 600);
            KnowledgeDisclosureRecord changed = CreateRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                source,
                observedTick: 2,
                expiryTick: 30,
                confidencePermille: 900);

            uint firstRevision = store.Upsert(viewer, target, first);
            uint sameRevision = store.Upsert(viewer, target, first);
            uint changedRevision = store.Upsert(viewer, target, changed);

            Assert.That(firstRevision, Is.EqualTo(1u));
            Assert.That(sameRevision, Is.EqualTo(firstRevision));
            Assert.That(changedRevision, Is.GreaterThan(sameRevision));
            Assert.That(store.TryGet(viewer, target, currentTick: 2, out KnowledgeDisclosureRecord resolved), Is.True);
            Assert.That(resolved.Revision, Is.EqualTo(changedRevision));
            Assert.That(resolved.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(resolved.ObservedTick, Is.EqualTo(2));
        }

        [Test]
        public void CopyRecords_UsesCallerBuffers_AndAllocatesZeroAfterWarmup()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity targetA = world.Create();
            Entity targetB = world.Create();
            Entity source = world.Create();
            var store = new KnowledgeProjectionStore(initialCapacity: 2);
            store.Upsert(viewer, targetA, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, source, 1, 100, 800));
            store.Upsert(viewer, targetB, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, source, 2, 0, 1000));

            Span<Entity> targets = stackalloc Entity[4];
            Span<KnowledgeDisclosureRecord> records = stackalloc KnowledgeDisclosureRecord[4];
            Assert.That(store.CopyRecords(viewer, currentTick: 3, targets, records), Is.EqualTo(2));
            Assert.That(targets[0], Is.EqualTo(targetA));
            Assert.That(targets[1], Is.EqualTo(targetB));
            Assert.That(records[0].Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
            Assert.That(records[1].Position, Is.EqualTo(KnowledgePositionAccess.Live));

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                store.TryGet(viewer, targetA, currentTick: 4, out _);
                store.CopyRecords(viewer, currentTick: 4, targets, records);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void RemoveAndCopyTargets_UseViewerScopedProjectionRows()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity otherViewer = world.Create();
            Entity targetA = world.Create();
            Entity targetB = world.Create();
            Entity source = world.Create();
            var store = new KnowledgeProjectionStore(initialCapacity: 2);
            store.Upsert(viewer, targetA, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, source, 1, 100, 800));
            store.Upsert(viewer, targetB, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, source, 2, 100, 900));
            store.Upsert(otherViewer, targetB, CreateRecord(KnowledgePresence.HiddenWithSource, KnowledgePositionAccess.None, source, 3, 100, 500));

            Span<Entity> targets = stackalloc Entity[4];
            Assert.That(store.CopyTargets(viewer, currentTick: 4, targets), Is.EqualTo(2));
            Assert.That(targets[0], Is.EqualTo(targetA));
            Assert.That(targets[1], Is.EqualTo(targetB));
            Assert.That(store.Remove(viewer, targetA), Is.True);
            Assert.That(store.Remove(viewer, targetA), Is.False);
            Assert.That(store.TryGet(viewer, targetA, currentTick: 4, out _), Is.False);
            Assert.That(store.TryGet(otherViewer, targetB, currentTick: 4, out _), Is.True);

            targets.Clear();
            Assert.That(store.CopyTargets(viewer, currentTick: 4, targets), Is.EqualTo(1));
            Assert.That(targets[0], Is.EqualTo(targetB));
            Assert.That(store.RecordCount, Is.EqualTo(2));
        }

        [Test]
        public void MaintenancePolicy_ExpiresAndCompactsChurnedViewerTargetsWithinConfiguredBounds()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity source = world.Create();
            Entity persistentTarget = world.Create();
            var policy = new KnowledgeProjectionMaintenancePolicy(
                expirePeriodTicks: 1,
                compactPeriodTicks: 1,
                compactInactivePermilleThreshold: 250,
                compactInactiveCountThreshold: 32);
            var store = new KnowledgeProjectionStore(initialCapacity: 4, policy);
            int totalExpired = 0;
            int totalCompacted = 0;

            for (int wave = 0; wave < 12; wave++)
            {
                for (int targetIndex = 0; targetIndex < 96; targetIndex++)
                {
                    Entity target = world.Create();
                    store.Upsert(
                        viewer,
                        target,
                        CreateRecord(
                            KnowledgePresence.Known,
                            KnowledgePositionAccess.LastKnown,
                            source,
                            observedTick: wave,
                            expiryTick: wave + 1,
                            confidencePermille: 600));
                }

                store.Upsert(
                    viewer,
                    persistentTarget,
                    CreateRecord(
                        KnowledgePresence.LiveVisible,
                        KnowledgePositionAccess.Live,
                        source,
                        observedTick: wave,
                        expiryTick: 0,
                        confidencePermille: 1000));

                KnowledgeProjectionMaintenanceResult result = store.RunMaintenance(currentTick: wave);
                totalExpired += result.ExpiredCount;
                totalCompacted += result.CompactedCount;
            }

            KnowledgeProjectionMaintenanceResult final = store.RunMaintenance(currentTick: 12);
            totalExpired += final.ExpiredCount;
            totalCompacted += final.CompactedCount;

            Assert.That(totalExpired, Is.EqualTo(12 * 96));
            Assert.That(totalCompacted, Is.GreaterThanOrEqualTo(12 * 96));
            Assert.That(store.RecordCount, Is.EqualTo(1));
            Assert.That(store.PhysicalRecordCount, Is.EqualTo(1));
            Assert.That(store.RecordCapacity, Is.LessThanOrEqualTo(4));
            Assert.That(store.TryGet(viewer, persistentTarget, currentTick: 12, out KnowledgeDisclosureRecord resolved), Is.True);
            Assert.That(resolved.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));

            Span<Entity> targets = stackalloc Entity[4];
            Span<KnowledgeDisclosureRecord> records = stackalloc KnowledgeDisclosureRecord[4];
            Assert.That(store.CopyRecords(viewer, currentTick: 12, targets, records), Is.EqualTo(1));
            Assert.That(targets[0], Is.EqualTo(persistentTarget));

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                store.TryGet(viewer, persistentTarget, currentTick: 12, out _);
                store.CopyRecords(viewer, currentTick: 12, targets, records);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void ClearViewer_RemovesOneViewerPartitionWithoutDroppingOtherViewers()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity otherViewer = world.Create();
            Entity source = world.Create();
            var store = new KnowledgeProjectionStore(initialCapacity: 4, KnowledgeProjectionMaintenancePolicy.Manual);

            for (int i = 0; i < 64; i++)
            {
                store.Upsert(
                    viewer,
                    world.Create(),
                    CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, source, i, 0, 700));
            }

            for (int i = 0; i < 16; i++)
            {
                store.Upsert(
                    otherViewer,
                    world.Create(),
                    CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, source, i, 0, 1000));
            }

            Assert.That(store.ClearViewer(viewer), Is.EqualTo(64));
            Assert.That(store.RecordCount, Is.EqualTo(16));
            Assert.That(store.Compact(), Is.EqualTo(64));
            Assert.That(store.PhysicalRecordCount, Is.EqualTo(16));

            Span<Entity> targets = stackalloc Entity[80];
            Span<KnowledgeDisclosureRecord> records = stackalloc KnowledgeDisclosureRecord[80];
            Assert.That(store.CopyRecords(viewer, currentTick: 65, targets, records), Is.EqualTo(0));
            Assert.That(store.CopyRecords(otherViewer, currentTick: 65, targets, records), Is.EqualTo(16));
            for (int i = 0; i < 16; i++)
            {
                Assert.That(records[i].Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            }
        }

        private static KnowledgeDisclosureRecord CreateRecord(
            KnowledgePresence presence,
            KnowledgePositionAccess position,
            Entity source,
            int observedTick,
            int expiryTick,
            int confidencePermille)
        {
            return new KnowledgeDisclosureRecord(
                presence,
                position,
                KnowledgeIdMask256.Empty.WithId(1),
                KnowledgeIdMask256.Empty.WithId(2),
                KnowledgeIdMask256.Empty.WithId(3),
                source,
                observedTick,
                expiryTick,
                confidencePermille,
                revision: 0);
        }
    }
}
