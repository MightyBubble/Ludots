using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Knowledge;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class KnowledgeProjectionRelationGrantTests
    {
        [Test]
        public void ProjectOutgoing_AllyGrantProjectsOnlyAuthorizedSourceCollection()
        {
            using World world = World.Create();
            TestRuntime runtime = CreateRuntime(world);
            int allyTypeId = runtime.RelationshipTypes.Register("Ally");
            int scoutsKeyId = runtime.CollectionKeys.Register("collection.scouts");
            int secretsKeyId = runtime.CollectionKeys.Register("collection.secrets");
            Entity viewer = world.Create();
            Entity ally = world.Create();
            Entity scoutA = world.Create();
            Entity scoutB = world.Create();
            Entity secret = world.Create();
            KnowledgeDisclosureRecord allyProfile = CreateRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                ally,
                observedTick: 10,
                expiryTick: 30,
                confidencePermille: 950,
                attributeId: 1,
                relationshipTypeId: allyTypeId,
                tagId: 3);

            runtime.Relationships.EnsureLink(viewer, ally, allyTypeId);
            runtime.Collections.Replace(
                ally,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { scoutA, scoutB });
            runtime.Collections.Replace(
                ally,
                EntityCollectionDescriptor.Create("collection.secrets", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { secret });
            RelationshipCatalogRuntime catalogRuntime = CreateCatalogRuntime(
                runtime,
                "Ally",
                "collection.scouts",
                allyProfile,
                attributeId: 1,
                relationshipTypeId: allyTypeId,
                tagId: 3);
            var projection = new KnowledgeProjectionStore(initialCapacity: 4);
            var projector = new KnowledgeRelationCollectionProjector(runtime.Relationships, runtime.Collections, catalogRuntime, projection);

            Span<Entity> sources = stackalloc Entity[4];
            Span<Entity> targets = stackalloc Entity[4];
            int projected = projector.ProjectOutgoing(viewer, allyTypeId, currentTick: 12, sources, targets);

            Assert.That(projected, Is.EqualTo(2));
            Assert.That(projection.TryGet(viewer, scoutA, currentTick: 12, out KnowledgeDisclosureRecord first), Is.True);
            Assert.That(first.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(first.Position, Is.EqualTo(KnowledgePositionAccess.Live));
            Assert.That(first.Source, Is.EqualTo(ally));
            Assert.That(first.AttributeMask.ContainsId(1), Is.True);
            Assert.That(first.RelationshipTypeMask.ContainsId(allyTypeId), Is.True);
            Assert.That(projection.TryGet(viewer, scoutB, currentTick: 12, out _), Is.True);
            Assert.That(projection.TryGet(viewer, secret, currentTick: 12, out _), Is.False);
            Assert.That(runtime.Collections.CopyEntities(ally, secretsKeyId, targets), Is.EqualTo(1));
        }

        [Test]
        public void ProjectOutgoing_NpcRumorGrantUsesItsOwnDisclosureProfile()
        {
            using World world = World.Create();
            TestRuntime runtime = CreateRuntime(world);
            int rumorTypeId = runtime.RelationshipTypes.Register("Rumor");
            int rumorKeyId = runtime.CollectionKeys.Register("collection.rumors");
            Entity player = world.Create();
            Entity npc = world.Create();
            Entity hiddenCamp = world.Create();
            KnowledgeDisclosureRecord rumorProfile = CreateRecord(
                KnowledgePresence.HiddenWithSource,
                KnowledgePositionAccess.LastKnown,
                npc,
                observedTick: 40,
                expiryTick: 55,
                confidencePermille: 450,
                attributeId: 4,
                relationshipTypeId: rumorTypeId,
                tagId: 5);

            runtime.Relationships.EnsureLink(player, npc, rumorTypeId);
            runtime.Collections.Replace(
                npc,
                EntityCollectionDescriptor.Create("collection.rumors", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { hiddenCamp });
            RelationshipCatalogRuntime catalogRuntime = CreateCatalogRuntime(
                runtime,
                "Rumor",
                "collection.rumors",
                rumorProfile,
                attributeId: 4,
                relationshipTypeId: rumorTypeId,
                tagId: 5);
            var projection = new KnowledgeProjectionStore();
            var projector = new KnowledgeRelationCollectionProjector(runtime.Relationships, runtime.Collections, catalogRuntime, projection);

            Span<Entity> sources = stackalloc Entity[2];
            Span<Entity> targets = stackalloc Entity[2];
            Assert.That(projector.ProjectOutgoing(player, rumorTypeId, currentTick: 41, sources, targets), Is.EqualTo(1));

            Assert.That(projection.TryGet(player, hiddenCamp, currentTick: 41, out KnowledgeDisclosureRecord resolved), Is.True);
            Assert.That(resolved.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));
            Assert.That(resolved.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
            Assert.That(resolved.Source, Is.EqualTo(npc));
            Assert.That(resolved.ObservedTick, Is.EqualTo(40));
            Assert.That(resolved.ExpiryTick, Is.EqualTo(55));
            Assert.That(resolved.ConfidencePermille, Is.EqualTo(450));
        }

        [Test]
        public void ProjectOutgoing_DeniesCollectionWhenRelationshipTypeHasNoGrant()
        {
            using World world = World.Create();
            TestRuntime runtime = CreateRuntime(world);
            int allyTypeId = runtime.RelationshipTypes.Register("Ally");
            int strangerTypeId = runtime.RelationshipTypes.Register("Stranger");
            int scoutsKeyId = runtime.CollectionKeys.Register("collection.scouts");
            Entity viewer = world.Create();
            Entity stranger = world.Create();
            Entity scout = world.Create();

            runtime.Relationships.EnsureLink(viewer, stranger, strangerTypeId);
            runtime.Collections.Replace(
                stranger,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { scout });
            RelationshipCatalogRuntime catalogRuntime = CreateCatalogRuntime(
                runtime,
                "Ally",
                "collection.scouts",
                CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, stranger, 1, 0, 800, 1, allyTypeId, 2),
                attributeId: 1,
                relationshipTypeId: allyTypeId,
                tagId: 2);
            var projection = new KnowledgeProjectionStore();
            var projector = new KnowledgeRelationCollectionProjector(runtime.Relationships, runtime.Collections, catalogRuntime, projection);

            Span<Entity> sources = stackalloc Entity[2];
            Span<Entity> targets = stackalloc Entity[2];
            Assert.That(projector.ProjectOutgoing(viewer, allyTypeId, currentTick: 2, sources, targets), Is.EqualTo(0));
            Assert.That(projector.ProjectOutgoing(viewer, strangerTypeId, currentTick: 2, sources, targets), Is.EqualTo(0));
            Assert.That(projection.TryGet(viewer, scout, currentTick: 2, out _), Is.False);
        }

        [Test]
        public void ProjectOutgoing_DoesNotTraverseTransitiveRelationCollections()
        {
            using World world = World.Create();
            TestRuntime runtime = CreateRuntime(world);
            int allyTypeId = runtime.RelationshipTypes.Register("Ally");
            int scoutsKeyId = runtime.CollectionKeys.Register("collection.scouts");
            Entity viewer = world.Create();
            Entity ally = world.Create();
            Entity allyOfAlly = world.Create();
            Entity directScout = world.Create();
            Entity transitiveScout = world.Create();

            runtime.Relationships.EnsureLink(viewer, ally, allyTypeId);
            runtime.Relationships.EnsureLink(ally, allyOfAlly, allyTypeId);
            runtime.Relationships.EnsureLink(allyOfAlly, viewer, allyTypeId);
            runtime.Collections.Replace(
                ally,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { directScout });
            runtime.Collections.Replace(
                allyOfAlly,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { transitiveScout });
            RelationshipCatalogRuntime catalogRuntime = CreateCatalogRuntime(
                runtime,
                "Ally",
                "collection.scouts",
                CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, ally, 5, 0, 800, 1, allyTypeId, 2),
                attributeId: 1,
                relationshipTypeId: allyTypeId,
                tagId: 2);
            var projection = new KnowledgeProjectionStore();
            var projector = new KnowledgeRelationCollectionProjector(runtime.Relationships, runtime.Collections, catalogRuntime, projection);

            Span<Entity> sources = stackalloc Entity[4];
            Span<Entity> targets = stackalloc Entity[4];
            Assert.That(projector.ProjectOutgoing(viewer, allyTypeId, currentTick: 6, sources, targets), Is.EqualTo(1));

            Assert.That(projection.TryGet(viewer, directScout, currentTick: 6, out _), Is.True);
            Assert.That(projection.TryGet(viewer, allyOfAlly, currentTick: 6, out _), Is.False);
            Assert.That(projection.TryGet(viewer, transitiveScout, currentTick: 6, out _), Is.False);
        }

        [Test]
        public void ProjectOutgoing_UsesCallerBuffers_AndAllocatesZeroAfterWarmup()
        {
            using World world = World.Create();
            TestRuntime runtime = CreateRuntime(world);
            int allyTypeId = runtime.RelationshipTypes.Register("Ally");
            int scoutsKeyId = runtime.CollectionKeys.Register("collection.scouts");
            Entity viewer = world.Create();
            Entity allyA = world.Create();
            Entity allyB = world.Create();
            Entity scoutA = world.Create();
            Entity scoutB = world.Create();

            runtime.Relationships.EnsureLink(viewer, allyA, allyTypeId);
            runtime.Relationships.EnsureLink(viewer, allyB, allyTypeId);
            runtime.Collections.Replace(
                allyA,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { scoutA });
            runtime.Collections.Replace(
                allyB,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { scoutB });
            RelationshipCatalogRuntime catalogRuntime = CreateCatalogRuntime(
                runtime,
                "Ally",
                "collection.scouts",
                CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, allyA, 0, 0, 1000, 1, allyTypeId, 2),
                attributeId: 1,
                relationshipTypeId: allyTypeId,
                tagId: 2);
            var projection = new KnowledgeProjectionStore(initialCapacity: 4);
            var projector = new KnowledgeRelationCollectionProjector(runtime.Relationships, runtime.Collections, catalogRuntime, projection);
            var sources = new Entity[4];
            var targets = new Entity[4];

            Assert.That(projector.ProjectOutgoing(viewer, allyTypeId, currentTick: 20, sources, targets), Is.EqualTo(2));
            long allocated = MeasureProjectOutgoingAllocations(
                projector,
                viewer,
                allyTypeId,
                sources,
                targets,
                out int projected);
            Assert.That(projected, Is.EqualTo(20_000));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureProjectOutgoingAllocations(
            KnowledgeRelationCollectionProjector projector,
            Entity viewer,
            int allyTypeId,
            Entity[] sources,
            Entity[] targets,
            out int projected)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            projected = 0;
            for (int i = 0; i < 10_000; i++)
            {
                projected += projector.ProjectOutgoing(
                    viewer,
                    allyTypeId,
                    currentTick: 20,
                    sources,
                    targets);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static RelationshipCatalogRuntime CreateCatalogRuntime(
            TestRuntime runtime,
            string typeId,
            string collectionKey,
            in KnowledgeDisclosureRecord profile,
            int attributeId,
            int relationshipTypeId,
            int tagId)
        {
            return RelationshipCatalogRuntime.Compile(
                new RelationshipCatalogConfig
                {
                    KnowledgeGrants =
                    {
                        new RelationshipKnowledgeGrantConfig
                        {
                            Id = $"{typeId}.{collectionKey}",
                            TypeId = typeId,
                            CollectionKey = collectionKey,
                            Presence = profile.Presence,
                            Position = profile.Position,
                            AttributeIds = { attributeId },
                            RelationshipTypeIds = { relationshipTypeId },
                            TagIds = { tagId },
                            ObservedTick = profile.ObservedTick,
                            ExpiryTick = profile.ExpiryTick,
                            ConfidencePermille = profile.ConfidencePermille
                        }
                    }
                },
                runtime.RelationshipTypes,
                new RelationshipMetricRegistry(),
                runtime.Collections);
        }

        private static TestRuntime CreateRuntime(World world)
        {
            var relationshipTypes = new RelationshipTypeRegistry();
            var relationships = new RelationshipRuntime(
                world,
                relationshipTypes,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 4),
                new RelationshipReverseIndex(world));
            var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 8);
            return new TestRuntime(relationshipTypes, relationships, collectionKeys, collections);
        }

        private static KnowledgeDisclosureRecord CreateRecord(
            KnowledgePresence presence,
            KnowledgePositionAccess position,
            Entity source,
            int observedTick,
            int expiryTick,
            int confidencePermille,
            int attributeId,
            int relationshipTypeId,
            int tagId)
        {
            return new KnowledgeDisclosureRecord(
                presence,
                position,
                KnowledgeIdMask256.Empty.WithId(attributeId),
                KnowledgeIdMask256.Empty.WithId(relationshipTypeId),
                KnowledgeIdMask256.Empty.WithId(tagId),
                source,
                observedTick,
                expiryTick,
                confidencePermille,
                revision: 0);
        }

        private readonly record struct TestRuntime(
            RelationshipTypeRegistry RelationshipTypes,
            RelationshipRuntime Relationships,
            StringIntRegistry CollectionKeys,
            EntityCollectionStore Collections);
    }
}
