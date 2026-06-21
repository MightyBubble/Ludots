using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class KnowledgeProjectionResolverTests
    {
        [Test]
        public void TryResolve_DirectPlayerViewerProjectsFiniteAspectsAndRevision()
        {
            using World world = World.Create();
            Entity player = world.Create();
            Entity target = world.Create();
            Entity source = world.Create();
            var store = new KnowledgeProjectionStore();
            uint revision = store.Upsert(
                player,
                target,
                CreateRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    source,
                    observedTick: 10,
                    expiryTick: 30,
                    confidencePermille: 900,
                    attributeMask: KnowledgeIdMask256.Empty.WithId(2),
                    relationshipMask: KnowledgeIdMask256.Empty.WithId(4),
                    tagMask: KnowledgeIdMask256.Empty.WithId(6)));
            var resolver = new KnowledgeProjectionResolver(store);

            Assert.That(resolver.TryResolve(player, target, currentTick: 11, out KnowledgeProjection projection), Is.True);
            Assert.That(projection.Viewer, Is.EqualTo(player));
            Assert.That(projection.Target, Is.EqualTo(target));
            Assert.That(projection.Source, Is.EqualTo(source));
            Assert.That(projection.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(projection.Position, Is.EqualTo(KnowledgePositionAccess.Live));
            Assert.That(projection.Revision, Is.EqualTo(revision));
            Assert.That(projection.CanKnowEntity, Is.True);
            Assert.That(projection.CanReadPosition(KnowledgePositionAccess.LastKnown), Is.True);
            Assert.That(projection.CanReadPosition(KnowledgePositionAccess.Live), Is.True);
            Assert.That(projection.CanReadAttribute(2), Is.True);
            Assert.That(projection.CanReadRelationship(4), Is.True);
            Assert.That(projection.CanReadTag(6), Is.True);
            Assert.That(projection.CanReadAttribute(3), Is.False);
            Assert.That(projection.CanReadRelationship(5), Is.False);
            Assert.That(projection.CanReadTag(7), Is.False);
        }

        [Test]
        public void TryResolve_WithNamedScopeCombinesViewerAndScopeMemberRecordsDeterministically()
        {
            using World world = World.Create();
            Entity player = world.Create();
            Entity team = world.Create();
            Entity teamScopeHost = world.Create(new ScopeMembershipRevision());
            Entity target = world.Create();
            Entity playerSource = world.Create();
            Entity teamSource = world.Create();
            int teamScopeId = 7;
            AddScopeRef(world, player, teamScopeId, teamScopeHost);
            AddScopeRef(world, team, teamScopeId, teamScopeHost);
            var store = new KnowledgeProjectionStore();
            uint playerRevision = store.Upsert(
                player,
                target,
                CreateRecord(
                    KnowledgePresence.Known,
                    KnowledgePositionAccess.LastKnown,
                    playerSource,
                    1,
                    0,
                    650,
                    KnowledgeIdMask256.Empty.WithId(1),
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty.WithId(3)));
            uint teamRevision = store.Upsert(
                team,
                target,
                CreateRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    teamSource,
                    2,
                    0,
                    800,
                    KnowledgeIdMask256.Empty.WithId(2),
                    KnowledgeIdMask256.Empty.WithId(4),
                    KnowledgeIdMask256.Empty));
            var scopeResolver = new ScopeResolver(world);
            var resolver = new KnowledgeProjectionResolver(store, scopeResolver);
            ScopeKey viewerScope = ScopeKey.Named(teamScopeId);
            var roleContext = new RoleResolverContext(
                actor: player,
                subject: player,
                viewer: player);
            Span<Entity> scopeMembers = stackalloc Entity[4];

            Assert.That(
                resolver.TryResolve(player, target, currentTick: 3, in viewerScope, in roleContext, scopeMembers, out KnowledgeProjection projection),
                Is.True);
            Assert.That(projection.Viewer, Is.EqualTo(player));
            Assert.That(projection.Target, Is.EqualTo(target));
            Assert.That(projection.Source, Is.EqualTo(teamSource));
            Assert.That(projection.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(projection.Position, Is.EqualTo(KnowledgePositionAccess.Live));
            Assert.That(projection.Revision, Is.EqualTo(playerRevision ^ teamRevision));
            Assert.That(projection.CanReadAttribute(1), Is.True);
            Assert.That(projection.CanReadAttribute(2), Is.True);
            Assert.That(projection.CanReadRelationship(4), Is.True);
            Assert.That(projection.CanReadTag(3), Is.True);
        }

        [Test]
        public void TryResolve_WithRelationGrantsProjectsAlliedCollectionIntoResolverPath()
        {
            using World world = World.Create();
            TestRuntime runtime = CreateRuntime(world);
            int allyTypeId = runtime.RelationshipTypes.Register("Ally");
            int scoutKeyId = runtime.CollectionKeys.Register("collection.scouts");
            Entity viewer = world.Create();
            Entity ally = world.Create();
            Entity scout = world.Create();
            runtime.Relationships.EnsureLink(viewer, ally, allyTypeId);
            runtime.Collections.Replace(
                ally,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { scout });
            var grants = new KnowledgeRelationCollectionGrantStore();
            grants.Upsert(new KnowledgeRelationCollectionGrant(
                allyTypeId,
                scoutKeyId,
                CreateRecord(
                    KnowledgePresence.Known,
                    KnowledgePositionAccess.LastKnown,
                    ally,
                    observedTick: 5,
                    expiryTick: 0,
                    confidencePermille: 500,
                    attributeMask: KnowledgeIdMask256.Empty.WithId(8),
                    relationshipMask: KnowledgeIdMask256.Empty.WithId(allyTypeId),
                    tagMask: KnowledgeIdMask256.Empty.WithId(9))));
            var store = new KnowledgeProjectionStore(initialCapacity: 4);
            var projector = new KnowledgeRelationCollectionProjector(runtime.Relationships, runtime.Collections, grants, store);
            var resolver = new KnowledgeProjectionResolver(store, projector);
            ScopeKey viewerScope = ScopeKey.Self;
            var roleContext = new RoleResolverContext(
                actor: viewer,
                subject: viewer,
                viewer: viewer);
            Span<Entity> scopeMembers = stackalloc Entity[1];
            Span<Entity> relationSources = stackalloc Entity[2];
            Span<Entity> relationTargets = stackalloc Entity[2];

            Assert.That(
                resolver.TryResolve(
                    viewer,
                    scout,
                    currentTick: 6,
                    in viewerScope,
                    in roleContext,
                    scopeMembers,
                    allyTypeId,
                    relationSources,
                    relationTargets,
                    out KnowledgeProjection projection),
                Is.True);
            Assert.That(projection.CanKnowEntity, Is.True);
            Assert.That(projection.Source, Is.EqualTo(ally));
            Assert.That(projection.CanReadPosition(KnowledgePositionAccess.LastKnown), Is.True);
            Assert.That(projection.CanReadAttribute(8), Is.True);
            Assert.That(projection.CanReadRelationship(allyTypeId), Is.True);
            Assert.That(projection.CanReadTag(9), Is.True);
        }

        [Test]
        public void TryResolve_NeutralNpcDisclosureAllowsExistenceButDeniesUnmaskedAspects()
        {
            using World world = World.Create();
            Entity player = world.Create();
            Entity rumorTarget = world.Create();
            Entity npc = world.Create();
            var store = new KnowledgeProjectionStore();
            store.Upsert(
                player,
                rumorTarget,
                CreateRecord(
                    KnowledgePresence.HiddenWithSource,
                    KnowledgePositionAccess.LastKnown,
                    npc,
                    20,
                    40,
                    400,
                    KnowledgeIdMask256.Empty.WithId(11),
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty));
            var resolver = new KnowledgeProjectionResolver(store);

            Assert.That(resolver.TryResolve(player, rumorTarget, currentTick: 21, out KnowledgeProjection projection), Is.True);
            Assert.That(projection.CanKnowEntity, Is.True);
            Assert.That(projection.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));
            Assert.That(projection.CanReadPosition(KnowledgePositionAccess.LastKnown), Is.True);
            Assert.That(projection.CanReadPosition(KnowledgePositionAccess.Live), Is.False);
            Assert.That(projection.CanReadAttribute(11), Is.True);
            Assert.That(projection.CanReadRelationship(1), Is.False);
            Assert.That(projection.CanReadTag(1), Is.False);
        }

        [Test]
        public void TryResolve_DirectLiveProjectionWinsOverFiniteDisclosureWhenMerged()
        {
            using World world = World.Create();
            Entity player = world.Create();
            Entity allyIntelSource = world.Create();
            Entity allyScopeHost = world.Create(new ScopeMembershipRevision());
            Entity target = world.Create();
            int allyScopeId = 9;
            AddScopeRef(world, player, allyScopeId, allyScopeHost);
            AddScopeRef(world, allyIntelSource, allyScopeId, allyScopeHost);
            var store = new KnowledgeProjectionStore();
            store.Upsert(
                player,
                target,
                CreateRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    player,
                    observedTick: 10,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    attributeMask: KnowledgeIdMask256.Empty.WithId(1),
                    relationshipMask: KnowledgeIdMask256.Empty,
                    tagMask: KnowledgeIdMask256.Empty));
            store.Upsert(
                allyIntelSource,
                target,
                CreateRecord(
                    KnowledgePresence.HiddenWithSource,
                    KnowledgePositionAccess.LastKnown,
                    allyIntelSource,
                    observedTick: 11,
                    expiryTick: 0,
                    confidencePermille: 600,
                    attributeMask: KnowledgeIdMask256.Empty,
                    relationshipMask: KnowledgeIdMask256.Empty.WithId(2),
                    tagMask: KnowledgeIdMask256.Empty));
            var scopeResolver = new ScopeResolver(world);
            var resolver = new KnowledgeProjectionResolver(store, scopeResolver);
            ScopeKey viewerScope = ScopeKey.Named(allyScopeId);
            var roleContext = new RoleResolverContext(
                actor: player,
                subject: player,
                viewer: player);
            Span<Entity> scopeMembers = stackalloc Entity[4];

            Assert.That(
                resolver.TryResolve(player, target, currentTick: 12, in viewerScope, in roleContext, scopeMembers, out KnowledgeProjection projection),
                Is.True);
            Assert.That(projection.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(projection.Position, Is.EqualTo(KnowledgePositionAccess.Live));
            Assert.That(projection.Source, Is.EqualTo(player));
            Assert.That(projection.CanReadAttribute(1), Is.True);
            Assert.That(projection.CanReadRelationship(2), Is.True);
        }

        [Test]
        public void Issue200_TryResolveWithRelationGrants_AllocatesZeroAfterWarmup()
        {
            using World world = World.Create();
            TestRuntime runtime = CreateRuntime(world);
            int allyTypeId = runtime.RelationshipTypes.Register("Ally");
            int scoutKeyId = runtime.CollectionKeys.Register("collection.scouts");
            Entity player = world.Create();
            Entity team = world.Create();
            Entity teamScopeHost = world.Create(new ScopeMembershipRevision());
            Entity ally = world.Create();
            Entity target = world.Create();
            int teamScopeId = 11;
            AddScopeRef(world, player, teamScopeId, teamScopeHost);
            AddScopeRef(world, team, teamScopeId, teamScopeHost);
            runtime.Relationships.EnsureLink(player, ally, allyTypeId);
            runtime.Collections.Replace(
                ally,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { target });
            var store = new KnowledgeProjectionStore(initialCapacity: 4);
            store.Upsert(
                team,
                target,
                CreateRecord(
                    KnowledgePresence.Known,
                    KnowledgePositionAccess.LastKnown,
                    team,
                    1,
                    0,
                    700,
                    KnowledgeIdMask256.Empty.WithId(1),
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty));
            var grants = new KnowledgeRelationCollectionGrantStore();
            grants.Upsert(new KnowledgeRelationCollectionGrant(
                allyTypeId,
                scoutKeyId,
                CreateRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    ally,
                    0,
                    0,
                    900,
                    KnowledgeIdMask256.Empty.WithId(2),
                    KnowledgeIdMask256.Empty.WithId(allyTypeId),
                    KnowledgeIdMask256.Empty.WithId(3))));
            var projector = new KnowledgeRelationCollectionProjector(runtime.Relationships, runtime.Collections, grants, store);
            var scopeResolver = new ScopeResolver(world);
            var resolver = new KnowledgeProjectionResolver(store, projector, scopeResolver);
            ScopeKey viewerScope = ScopeKey.Named(teamScopeId);
            var roleContext = new RoleResolverContext(
                actor: player,
                subject: player,
                viewer: player);
            Span<Entity> scopeMembers = stackalloc Entity[4];
            Span<Entity> relationSources = stackalloc Entity[4];
            Span<Entity> relationTargets = stackalloc Entity[4];
            Assert.That(
                resolver.TryResolve(player, target, 2, in viewerScope, in roleContext, scopeMembers, allyTypeId, relationSources, relationTargets, out _),
                Is.True);

            for (int i = 0; i < 64; i++)
            {
                resolver.TryResolve(player, target, 2, in viewerScope, in roleContext, scopeMembers, allyTypeId, relationSources, relationTargets, out _);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                resolver.TryResolve(player, target, 2, in viewerScope, in roleContext, scopeMembers, allyTypeId, relationSources, relationTargets, out _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void Issue200_KnowledgeProjectionConsumer_TryResolve_AllocatesZeroAfterWarmup()
        {
            using World world = World.Create();
            TestRuntime runtime = CreateRuntime(world);
            int allyTypeId = runtime.RelationshipTypes.Register("Ally");
            int scoutKeyId = runtime.CollectionKeys.Register("collection.scouts");
            Entity viewer = world.Create();
            Entity ally = world.Create();
            Entity scout = world.Create();
            runtime.Relationships.EnsureLink(viewer, ally, allyTypeId);
            runtime.Collections.Replace(
                ally,
                EntityCollectionDescriptor.Create("collection.scouts", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
                new[] { scout });
            var store = new KnowledgeProjectionStore(initialCapacity: 4);
            var grants = new KnowledgeRelationCollectionGrantStore();
            grants.Upsert(new KnowledgeRelationCollectionGrant(
                allyTypeId,
                scoutKeyId,
                CreateRecord(
                    KnowledgePresence.Known,
                    KnowledgePositionAccess.LastKnown,
                    ally,
                    observedTick: 1,
                    expiryTick: 0,
                    confidencePermille: 700,
                    attributeMask: KnowledgeIdMask256.Empty.WithId(2),
                    relationshipMask: KnowledgeIdMask256.Empty.WithId(allyTypeId),
                    tagMask: KnowledgeIdMask256.Empty)));
            var projector = new KnowledgeRelationCollectionProjector(runtime.Relationships, runtime.Collections, grants, store);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store, projector),
                [CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer,
            };

            Assert.That(KnowledgeProjectionConsumer.TryResolve(world, globals, Entity.Null, scout, out _), Is.True);

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                KnowledgeProjectionConsumer.TryResolve(world, globals, Entity.Null, scout, out _);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
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
                new RelationshipChangeBuffer(capacity: 4));
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
            KnowledgeIdMask256 attributeMask,
            KnowledgeIdMask256 relationshipMask,
            KnowledgeIdMask256 tagMask)
        {
            return new KnowledgeDisclosureRecord(
                presence,
                position,
                attributeMask,
                relationshipMask,
                tagMask,
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

        private static void AddScopeRef(World world, Entity entity, int scopeKeyId, Entity scopeHost)
        {
            var refs = new ScopeRefBuffer();
            Assert.That(refs.TryAdd(scopeKeyId, scopeHost), Is.True);
            world.Add(entity, refs);
        }
    }
}
