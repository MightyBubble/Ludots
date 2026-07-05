using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>RFC-0065 M3: domain-routed collection writes + composite control-plane view (CTRL-4c/4d).</summary>
    [TestFixture]
    public sealed class DomainRoutedCollectionTests
    {
        [Test]
        public void ReplaceRouted_SplitsWriteByControlDomainAndComposesView()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity m01 = world.Create();
            Entity m02 = world.Create();
            Entity m99 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);
            harness.Ownership.EnsureOwnership(p2Rep, m99);
            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);

            harness.Writer.ReplaceRouted(
                p1Rep,
                harness.CommandSourceKeyId,
                stackalloc Entity[] { m01, m99 },
                EntityCollectionSourceKind.UiAcquisition);

            Span<Entity> rows = stackalloc Entity[8];
            Span<Entity> writers = stackalloc Entity[8];

            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p1Handle), Is.True);
            Assert.That(harness.Store.CopyEntities(p1Handle, 0, rows), Is.EqualTo(1));
            Assert.That(rows[0], Is.EqualTo(m01));
            Assert.That(harness.Store.CopyWriterDomains(p1Handle, 0, writers), Is.EqualTo(1));
            Assert.That(writers[0], Is.EqualTo(p1Rep));

            Assert.That(harness.Store.TryGet(p2Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p2Handle), Is.True);
            Assert.That(harness.Store.CopyEntities(p2Handle, 0, rows), Is.EqualTo(1));
            Assert.That(rows[0], Is.EqualTo(m99));
            Assert.That(harness.Store.TryGetWriterDomainAt(p2Handle, 0, out Entity rowWriter), Is.True);
            Assert.That(rowWriter, Is.EqualTo(p1Rep));

            Span<Entity> members = stackalloc Entity[8];
            Span<Entity> domains = stackalloc Entity[8];
            int count = harness.View.CopyMembersWithDomain(p1Rep, harness.CommandSourceKeyId, members, domains);
            Assert.That(members[..count].ToArray(), Is.EqualTo(new[] { m01, m99 }));
            Assert.That(domains[..count].ToArray(), Is.EqualTo(new[] { p1Rep, p2Rep }));
        }

        [Test]
        public void ControlsRevoke_ShrinksViewWhileForeignDomainKeepsItsRows()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity m01 = world.Create();
            Entity m99 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p2Rep, m99);
            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);

            harness.Writer.ReplaceRouted(
                p1Rep,
                harness.CommandSourceKeyId,
                stackalloc Entity[] { m01, m99 },
                EntityCollectionSourceKind.UiAcquisition);

            harness.Relationships.RemoveLink(p1Rep, p2Rep, harness.ControlsTypeId);

            Span<Entity> members = stackalloc Entity[8];
            int count = harness.View.CopyMembers(p1Rep, harness.CommandSourceKeyId, members);
            Assert.That(members[..count].ToArray(), Is.EqualTo(new[] { m01 }), "View must shrink to the anchor's own domain.");

            Assert.That(harness.Store.TryGet(p2Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p2Handle), Is.True);
            Span<Entity> rows = stackalloc Entity[4];
            Assert.That(harness.Store.CopyEntities(p2Handle, 0, rows), Is.EqualTo(1), "Handback is a zero-op: the foreign domain keeps its latest state.");
            Assert.That(rows[0], Is.EqualTo(m99));
        }

        [Test]
        public void ReplaceRouted_ClearsDomainsDroppedByTheNewBatch()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity m01 = world.Create();
            Entity m99 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p2Rep, m99);
            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);

            harness.Writer.ReplaceRouted(
                p1Rep,
                harness.CommandSourceKeyId,
                stackalloc Entity[] { m01, m99 },
                EntityCollectionSourceKind.UiAcquisition);
            harness.Writer.ReplaceRouted(
                p1Rep,
                harness.CommandSourceKeyId,
                stackalloc Entity[] { m01 },
                EntityCollectionSourceKind.UiAcquisition);

            Assert.That(harness.Store.TryGet(p2Rep, harness.CommandSourceKeyId, out EntityCollectionHandle p2Handle), Is.True);
            Span<Entity> rows = stackalloc Entity[4];
            Assert.That(harness.Store.CopyEntities(p2Handle, 0, rows), Is.EqualTo(0), "Re-routing must clear the key in domains no longer covered.");

            Span<Entity> members = stackalloc Entity[8];
            int count = harness.View.CopyMembers(p1Rep, harness.CommandSourceKeyId, members);
            Assert.That(members[..count].ToArray(), Is.EqualTo(new[] { m01 }));
        }

        [Test]
        public void ReplaceRouted_RoutesDomainlessEntityToTheWriterDomain()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity stray = world.Create();

            harness.Writer.ReplaceRouted(
                p1Rep,
                harness.CommandSourceKeyId,
                stackalloc Entity[] { stray },
                EntityCollectionSourceKind.UiAcquisition);

            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out EntityCollectionHandle handle), Is.True);
            Span<Entity> rows = stackalloc Entity[4];
            Assert.That(harness.Store.CopyEntities(handle, 0, rows), Is.EqualTo(1));
            Assert.That(rows[0], Is.EqualTo(stray));
            Assert.That(harness.Store.TryGetWriterDomainAt(handle, 0, out Entity writer), Is.True);
            Assert.That(writer, Is.EqualTo(p1Rep));
        }

        [Test]
        public void ComputeRevision_ChangesOnContentChangeAndOnTopologyChange()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity m01 = world.Create();
            Entity m02 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);

            harness.Writer.ReplaceRouted(
                p1Rep,
                harness.CommandSourceKeyId,
                stackalloc Entity[] { m01 },
                EntityCollectionSourceKind.UiAcquisition);
            uint initial = harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId);
            Assert.That(harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId), Is.EqualTo(initial), "Stable state must yield a stable revision.");

            harness.Writer.ReplaceRouted(
                p1Rep,
                harness.CommandSourceKeyId,
                stackalloc Entity[] { m01, m02 },
                EntityCollectionSourceKind.UiAcquisition);
            uint afterContentChange = harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId);
            Assert.That(afterContentChange, Is.Not.EqualTo(initial), "Content change in any domain must move the composite revision.");

            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);
            Assert.That(
                harness.View.ComputeRevision(p1Rep, harness.CommandSourceKeyId),
                Is.Not.EqualTo(afterContentChange),
                "Topology change must move the composite revision.");
        }

        [Test]
        public void RoutedWriteAndViewRead_AllocateZeroAfterWarmup()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            var selection = new Entity[16];
            for (int i = 0; i < 8; i++)
            {
                Entity own = world.Create();
                Entity proxy = world.Create();
                harness.Ownership.EnsureOwnership(p1Rep, own);
                harness.Ownership.EnsureOwnership(p2Rep, proxy);
                selection[i * 2] = own;
                selection[(i * 2) + 1] = proxy;
            }

            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);

            var members = new Entity[32];
            var domains = new Entity[32];
            harness.Writer.ReplaceRouted(p1Rep, harness.CommandSourceKeyId, selection, EntityCollectionSourceKind.UiAcquisition);
            harness.View.CopyMembersWithDomain(p1Rep, harness.CommandSourceKeyId, members, domains);

            long allocated = MeasureSteadyStateAllocations(harness, p1Rep, selection, members, domains);
            allocated = Math.Min(allocated, MeasureSteadyStateAllocations(harness, p1Rep, selection, members, domains));
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static long MeasureSteadyStateAllocations(
            Harness harness,
            Entity writerDomain,
            Entity[] selection,
            Entity[] members,
            Entity[] domains)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Writer.ReplaceRouted(writerDomain, harness.CommandSourceKeyId, selection, EntityCollectionSourceKind.UiAcquisition);
                harness.View.CopyMembersWithDomain(writerDomain, harness.CommandSourceKeyId, members, domains);
                harness.View.ComputeRevision(writerDomain, harness.CommandSourceKeyId);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class Harness
        {
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public EntityCollectionStore Store = null!;
            public DomainRoutedCollectionWriter Writer = null!;
            public ControlPlaneView View = null!;
            public int ControlsTypeId;
            public int CommandSourceKeyId;

            public static Harness Create(World world)
            {
                var types = new RelationshipTypeRegistry();
                var relationships = new RelationshipRuntime(
                    world,
                    types,
                    new RelationshipMetricRegistry(),
                    new RelationshipFlagRegistry(),
                    new RelationshipBandRegistry(),
                    new RelationshipChangeBuffer(capacity: 4),
                    new RelationshipReverseIndex(world));
                int ownsTypeId = types.Register("Owns");
                int controlsTypeId = types.Register("Controls");
                var ownership = new OwnershipResolver(relationships, ownsTypeId);
                var query = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
                var keyRegistry = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var store = new EntityCollectionStore(keyRegistry, initialCollectionCapacity: 16, initialRowCapacity: 128);
                return new Harness
                {
                    Relationships = relationships,
                    Ownership = ownership,
                    Store = store,
                    Writer = new DomainRoutedCollectionWriter(store, query),
                    View = new ControlPlaneView(store, query),
                    ControlsTypeId = controlsTypeId,
                    CommandSourceKeyId = keyRegistry.Register(EntityCollectionKeys.CommandSource),
                };
            }
        }
    }
}
