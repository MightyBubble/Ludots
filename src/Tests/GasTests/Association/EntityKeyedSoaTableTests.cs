using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Knowledge;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class EntityKeyedSoaTableTests
    {
        [Test]
        public void UpsertTryGetAndRemove_KeyByVersionAwareEntityPair()
        {
            using var world = World.Create();
            Entity primary = world.Create();
            Entity secondary = world.Create();
            var table = new EntityKeyedSoaTable<TestPayload>(initialCapacity: 2);
            var key = EntityKeyedSoaKey.ForPair(primary, secondary);

            uint revision = table.Upsert(key, new TestPayload(10, 100), expiryTick: 0, payloadChanged: true, out int slot);

            Assert.That(revision, Is.EqualTo(1u));
            Assert.That(table.ActiveCount, Is.EqualTo(1));
            Assert.That(table.TryGet(key, currentTick: 1, out TestPayload payload, out uint resolvedRevision, out int resolvedSlot), Is.True);
            Assert.That(payload.Amount, Is.EqualTo(10));
            Assert.That(resolvedRevision, Is.EqualTo(1u));
            Assert.That(resolvedSlot, Is.EqualTo(slot));

            uint sameRevision = table.Upsert(key, new TestPayload(10, 100), expiryTick: 0, payloadChanged: false, out _);
            uint changedRevision = table.Upsert(key, new TestPayload(11, 100), expiryTick: 0, payloadChanged: true, out _);

            Assert.That(sameRevision, Is.EqualTo(revision));
            Assert.That(changedRevision, Is.GreaterThan(sameRevision));
            Assert.That(table.Remove(key), Is.True);
            Assert.That(table.Remove(key), Is.False);
            Assert.That(table.ActiveCount, Is.EqualTo(0));
            Assert.That(table.TryGet(key, currentTick: 1, out _, out _, out _), Is.False);
        }

        [Test]
        public void CopyByPrimary_UsesCallerBufferAndAllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            Entity primary = world.Create();
            Entity otherPrimary = world.Create();
            Entity first = world.Create();
            Entity second = world.Create();
            Entity third = world.Create();
            var table = new EntityKeyedSoaTable<TestPayload>(initialCapacity: 2);
            table.Upsert(EntityKeyedSoaKey.ForPair(primary, first), new TestPayload(1, 10), expiryTick: 0, payloadChanged: true, out _);
            table.Upsert(EntityKeyedSoaKey.ForPair(primary, second), new TestPayload(2, 20), expiryTick: 0, payloadChanged: true, out _);
            table.Upsert(EntityKeyedSoaKey.ForPair(otherPrimary, third), new TestPayload(3, 30), expiryTick: 0, payloadChanged: true, out _);

            var rows = new EntityKeyedSoaRow<TestPayload>[4];
            Assert.That(table.CopyByPrimary(primary, currentTick: 1, rows), Is.EqualTo(2));
            Assert.That(rows[0].Key.Secondary, Is.EqualTo(first));
            Assert.That(rows[1].Key.Secondary, Is.EqualTo(second));

            long allocated = MeasureCopyByPrimaryAllocations(table, primary, first, rows);
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void ExpireAndCompact_ReclaimsInactiveSlotsAndBoundsCapacity()
        {
            using var world = World.Create();
            Entity primary = world.Create();
            var table = new EntityKeyedSoaTable<TestPayload>(initialCapacity: 4);
            for (int i = 0; i < 128; i++)
            {
                Entity secondary = world.Create();
                table.Upsert(EntityKeyedSoaKey.ForPair(primary, secondary), new TestPayload(i, i), expiryTick: i < 64 ? 5 : 0, payloadChanged: true, out _);
            }

            int grownCapacity = table.SlotCapacity;
            Assert.That(grownCapacity, Is.GreaterThanOrEqualTo(128));
            Assert.That(table.Expire(currentTick: 5), Is.EqualTo(64));
            Assert.That(table.ActiveCount, Is.EqualTo(64));

            int moved = table.Compact();

            Assert.That(moved, Is.EqualTo(64));
            Assert.That(table.PhysicalSlotCount, Is.EqualTo(64));
            Assert.That(table.SlotCapacity, Is.LessThan(grownCapacity));
            Span<EntityKeyedSoaRow<TestPayload>> rows = stackalloc EntityKeyedSoaRow<TestPayload>[80];
            Assert.That(table.CopyByPrimary(primary, currentTick: 6, rows), Is.EqualTo(64));
        }

        [Test]
        public void EntityAndDiscriminatorKey_SupportsCollectionStyleOwnerRows()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var table = new EntityKeyedSoaTable<TestPayload>(initialCapacity: 2);
            var first = EntityKeyedSoaKey.ForEntityAndDiscriminator(owner, discriminator: 7);
            var second = EntityKeyedSoaKey.ForEntityAndDiscriminator(owner, discriminator: 8);

            table.Upsert(first, new TestPayload(70, 0), expiryTick: 0, payloadChanged: true, out _);
            table.Upsert(second, new TestPayload(80, 0), expiryTick: 0, payloadChanged: true, out _);

            Assert.That(table.TryGet(first, currentTick: 0, out TestPayload payload, out _, out _), Is.True);
            Assert.That(payload.Amount, Is.EqualTo(70));
            Span<EntityKeyedSoaRow<TestPayload>> rows = stackalloc EntityKeyedSoaRow<TestPayload>[4];
            Assert.That(table.CopyByPrimary(owner, currentTick: 0, rows), Is.EqualTo(2));
            Assert.That(rows[0].Key.Discriminator, Is.EqualTo(7));
            Assert.That(rows[1].Key.Discriminator, Is.EqualTo(8));
        }

        [Test]
        public void KnowledgeProjectionStore_CopyRecords_UsesCallerBuffersWithoutStackallocSizedByRequest()
        {
            using var world = World.Create();
            Entity viewer = world.Create();
            var store = new KnowledgeProjectionStore(initialCapacity: 4);
            var emptyMask = default(KnowledgeIdMask256);

            for (int i = 0; i < 256; i++)
            {
                Entity target = world.Create();
                store.Upsert(
                    viewer,
                    target,
                    new KnowledgeDisclosureRecord(
                        KnowledgePresence.Known,
                        KnowledgePositionAccess.LastKnown,
                        emptyMask,
                        emptyMask,
                        emptyMask,
                        source: target,
                        observedTick: i,
                        expiryTick: 0,
                        confidencePermille: 1000,
                        revision: 0));
            }

            var targets = new Entity[256];
            var records = new KnowledgeDisclosureRecord[256];

            Assert.That(store.CopyRecords(viewer, currentTick: 1, targets, records), Is.EqualTo(256));

            long allocated = MeasureProjectionCopyAllocations(
                store,
                viewer,
                targets,
                records,
                out int copied);
            Assert.That(copied, Is.EqualTo(256));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureCopyByPrimaryAllocations(
            EntityKeyedSoaTable<TestPayload> table,
            Entity primary,
            Entity first,
            EntityKeyedSoaRow<TestPayload>[] rows)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                table.TryGet(EntityKeyedSoaKey.ForPair(primary, first), currentTick: 1, out _, out _, out _);
                table.CopyByPrimary(primary, currentTick: 1, rows);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureProjectionCopyAllocations(
            KnowledgeProjectionStore store,
            Entity viewer,
            Entity[] targets,
            KnowledgeDisclosureRecord[] records,
            out int copied)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            copied = 0;
            for (int i = 0; i < 200; i++)
            {
                copied = store.CopyRecords(viewer, currentTick: 1, targets, records);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private readonly struct TestPayload
        {
            public TestPayload(int amount, int marker)
            {
                Amount = amount;
                Marker = marker;
            }

            public readonly int Amount;
            public readonly int Marker;
        }
    }
}
