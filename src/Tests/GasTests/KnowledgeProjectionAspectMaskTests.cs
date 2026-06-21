using System;
using Arch.Core;
using Ludots.Core.Knowledge;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class KnowledgeProjectionAspectMaskTests
    {
        [Test]
        public void IdMask_ComposesRegistryIds_AndRejectsOutOfRangeIds()
        {
            Assert.That(KnowledgeIdMask256.Empty.IsEmpty, Is.True);

            KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty
                .WithId(0)
                .WithId(63)
                .WithId(64)
                .WithId(127)
                .WithId(128)
                .WithId(255);

            Assert.That(mask.ContainsId(0), Is.True);
            Assert.That(mask.ContainsId(63), Is.True);
            Assert.That(mask.ContainsId(64), Is.True);
            Assert.That(mask.ContainsId(127), Is.True);
            Assert.That(mask.ContainsId(128), Is.True);
            Assert.That(mask.ContainsId(255), Is.True);
            Assert.That(mask.ContainsId(42), Is.False);
            Assert.That(mask.IsEmpty, Is.False);

            Assert.That(() => KnowledgeIdMask256.Empty.WithId(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => KnowledgeIdMask256.Empty.WithId(256), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => mask.ContainsId(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => mask.ContainsId(256), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void IdMask_UnionsAndChecksComposedAspectSets()
        {
            KnowledgeIdMask256 combat = KnowledgeIdMask256.Empty.WithId(1).WithId(3);
            KnowledgeIdMask256 economy = KnowledgeIdMask256.Empty.WithId(64).WithId(129);
            KnowledgeIdMask256 composed = combat.Union(economy);

            Assert.That(KnowledgeIdMask256.Empty.ContainsAll(KnowledgeIdMask256.Empty), Is.True);
            Assert.That(composed.ContainsAll(combat), Is.True);
            Assert.That(composed.ContainsAll(economy), Is.True);
            Assert.That(combat.ContainsAll(composed), Is.False);
            Assert.That(combat.Intersects(economy), Is.False);
            Assert.That(composed.Intersects(economy), Is.True);
        }

        [Test]
        public void DisclosureRecord_ExpressesFiniteProjectionAsValueContract()
        {
            using World world = World.Create();
            Entity source = world.Create();
            KnowledgeDisclosureRecord record = new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.LastKnown,
                KnowledgeIdMask256.Empty.WithId(2),
                KnowledgeIdMask256.Empty.WithId(4),
                KnowledgeIdMask256.Empty.WithId(6),
                source,
                observedTick: 120,
                expiryTick: 180,
                confidencePermille: 750,
                revision: 9);

            Assert.That(record.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(record.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
            Assert.That(record.AttributeMask.ContainsId(2), Is.True);
            Assert.That(record.RelationshipTypeMask.ContainsId(4), Is.True);
            Assert.That(record.TagMask.ContainsId(6), Is.True);
            Assert.That(record.Source, Is.EqualTo(source));
            Assert.That(record.ObservedTick, Is.EqualTo(120));
            Assert.That(record.ExpiryTick, Is.EqualTo(180));
            Assert.That(record.ConfidencePermille, Is.EqualTo(750));
            Assert.That(record.Revision, Is.EqualTo(9u));
            Assert.That(record.IsExpired(currentTick: 179), Is.False);
            Assert.That(record.IsExpired(currentTick: 180), Is.True);
        }

        [Test]
        public void DisclosureRecord_CanRepresentUnknownAndHiddenWithSourceWithoutParticipantFields()
        {
            using World world = World.Create();
            Entity source = world.Create();

            KnowledgeDisclosureRecord unknown = new KnowledgeDisclosureRecord(
                KnowledgePresence.Unknown,
                KnowledgePositionAccess.None,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                default,
                observedTick: 0,
                expiryTick: 0,
                confidencePermille: 0,
                revision: 0);
            KnowledgeDisclosureRecord hiddenWithSource = new KnowledgeDisclosureRecord(
                KnowledgePresence.HiddenWithSource,
                KnowledgePositionAccess.None,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty.WithId(11),
                KnowledgeIdMask256.Empty,
                source,
                observedTick: 200,
                expiryTick: 240,
                confidencePermille: 400,
                revision: 2);

            Assert.That(unknown.Presence, Is.EqualTo(KnowledgePresence.Unknown));
            Assert.That(unknown.Position, Is.EqualTo(KnowledgePositionAccess.None));
            Assert.That(unknown.AttributeMask.IsEmpty, Is.True);
            Assert.That(unknown.RelationshipTypeMask.IsEmpty, Is.True);
            Assert.That(unknown.TagMask.IsEmpty, Is.True);
            Assert.That(hiddenWithSource.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));
            Assert.That(hiddenWithSource.RelationshipTypeMask.ContainsId(11), Is.True);
            Assert.That(hiddenWithSource.Source, Is.EqualTo(source));
        }

        [Test]
        public void HotPathMaskAndRecordChecks_AllocateZeroAfterWarmup()
        {
            using World world = World.Create();
            Entity source = world.Create();
            KnowledgeIdMask256 required = KnowledgeIdMask256.Empty.WithId(2).WithId(5);
            KnowledgeDisclosureRecord record = new KnowledgeDisclosureRecord(
                KnowledgePresence.Known,
                KnowledgePositionAccess.Live,
                required.Union(KnowledgeIdMask256.Empty.WithId(9)),
                KnowledgeIdMask256.Empty.WithId(17),
                KnowledgeIdMask256.Empty.WithId(19),
                source,
                observedTick: 10,
                expiryTick: 1000,
                confidencePermille: 1000,
                revision: 1);

            int sink = 0;
            for (int i = 0; i < 128; i++)
            {
                if (record.AttributeMask.ContainsAll(required))
                {
                    sink++;
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                if (record.AttributeMask.ContainsId(2))
                {
                    sink++;
                }

                if (record.AttributeMask.ContainsAll(required))
                {
                    sink++;
                }

                if (record.RelationshipTypeMask.Intersects(record.TagMask))
                {
                    sink++;
                }

                if (!record.IsExpired(i & 255))
                {
                    sink++;
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(sink, Is.GreaterThan(0));
            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
