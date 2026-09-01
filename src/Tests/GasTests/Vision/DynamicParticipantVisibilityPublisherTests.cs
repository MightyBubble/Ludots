using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Arch.Core.Utils;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.ParticipantVisibility;
using Ludots.Core.Registry;
using Ludots.Core.Systems;
using NUnit.Framework;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class DynamicParticipantVisibilityPublisherTests
    {
        private const string CollectionKey = "tests.dynamic.participants";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        [Test]
        public void ConfigDeserializesCombinedFlagsAndRegisteredComponentQueryNames()
        {
            const string json = """
            {
              "ViewerKind": "Player",
              "ViewerRef": "1",
              "CollectionKey": "tests.dynamic.participants",
              "CollectionRole": "Display",
              "Query": {
                "AllComponents": [ "MapEntity", "PlayerOwner", "CommandSourceSelectableTag" ],
                "NoneComponents": [ "PlayerIdentity" ]
              },
              "Flags": "RequireSelectable, ExcludePlayerIdentity, RequireMapMatch",
              "OwnerMatchPolicy": "Public",
              "Presence": "LiveVisible",
              "Position": "Live",
              "SourceRef": "viewer",
              "SourceKind": "Viewer"
            }
            """;

            DynamicParticipantQuerySpec spec = JsonSerializer.Deserialize<DynamicParticipantQuerySpec>(json, JsonOptions)
                ?? throw new InvalidOperationException("Dynamic participant query spec failed to deserialize.");

            Assert.That(spec.Flags, Is.EqualTo(
                DynamicParticipantQueryFlags.RequireSelectable |
                DynamicParticipantQueryFlags.ExcludePlayerIdentity |
                DynamicParticipantQueryFlags.RequireMapMatch));
            Assert.That(spec.OwnerMatchPolicy, Is.EqualTo(DynamicParticipantOwnerMatchPolicy.Public));
            Assert.That(spec.Query.AllComponents, Is.EqualTo(new[] { "MapEntity", "PlayerOwner", "CommandSourceSelectableTag" }));
            Assert.That(CoreComponentRegistry.TryGetComponentType("MapEntity", out _), Is.True);
            Assert.That(CoreComponentRegistry.TryGetComponentType("PlayerOwner", out _), Is.True);
            Assert.That(CoreComponentRegistry.TryGetComponentType("CommandSourceSelectableTag", out _), Is.True);
        }

        [Test]
        public void PublishAddsStaticMatchingEntitiesToCollectionAndKnowledge()
        {
            using var fixture = PublisherFixture.Create();
            Entity target = fixture.CreatePlayerOwnedParticipant(playerId: 1, teamId: 1);

            DynamicParticipantVisibilityPublishResult result = fixture.Publisher.Publish(10);

            Assert.That(result.ChangedCollections, Is.EqualTo(1));
            Assert.That(result.UpsertedKnowledgeRecords, Is.EqualTo(1));
            AssertCollection(fixture.Collections, fixture.Viewer, target);
            AssertLiveKnowledge(fixture.Knowledge, fixture.Viewer, target, expectedSource: fixture.Viewer, expectedTick: 10);
        }

        [Test]
        public void PublishDiscoversRuntimeEntityCreatedAfterWarmup()
        {
            using var fixture = PublisherFixture.Create();

            DynamicParticipantVisibilityPublishResult first = fixture.Publisher.Publish(1);
            Entity runtime = fixture.CreatePlayerOwnedParticipant(playerId: 1, teamId: 1);
            DynamicParticipantVisibilityPublishResult second = fixture.Publisher.Publish(2);

            Assert.That(first.ChangedCollections, Is.EqualTo(1));
            Assert.That(first.UpsertedKnowledgeRecords, Is.EqualTo(0));
            Assert.That(second.ChangedCollections, Is.EqualTo(1));
            Assert.That(second.UpsertedKnowledgeRecords, Is.EqualTo(1));
            AssertCollection(fixture.Collections, fixture.Viewer, runtime);
            AssertLiveKnowledge(fixture.Knowledge, fixture.Viewer, runtime, expectedSource: fixture.Viewer, expectedTick: 2);
        }

        [Test]
        public void PublishRefreshesKnowledgeWithoutReplacingCollectionWhenMembershipIsUnchanged()
        {
            using var fixture = PublisherFixture.Create();
            Entity target = fixture.CreatePlayerOwnedParticipant(playerId: 1, teamId: 1);
            fixture.Publisher.Publish(1);
            Assert.That(fixture.Collections.TryGet(fixture.Viewer, CollectionKey, out EntityCollectionHandle handle), Is.True);

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            DynamicParticipantVisibilityPublishResult result = fixture.Publisher.Publish(2);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(result.ChangedCollections, Is.EqualTo(0));
            Assert.That(result.UpsertedKnowledgeRecords, Is.EqualTo(1));
            Assert.That(result.RemovedKnowledgeRecords, Is.EqualTo(0));
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(fixture.Collections.TryGet(fixture.Viewer, CollectionKey, out EntityCollectionHandle after), Is.True);
            Assert.That(after.Revision, Is.EqualTo(handle.Revision));
            AssertCollection(fixture.Collections, fixture.Viewer, target);
            AssertLiveKnowledge(fixture.Knowledge, fixture.Viewer, target, expectedSource: fixture.Viewer, expectedTick: 2);
        }

        [Test]
        public void ConsecutivePublish_RestoresDisclosureMasksOverwrittenByFogProjection()
        {
            using var fixture = PublisherFixture.Create();
            Entity target = fixture.CreatePlayerOwnedParticipant(playerId: 1, teamId: 1);
            fixture.Publisher.Publish(1);
            fixture.Knowledge.Upsert(
                fixture.Viewer,
                target,
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    fixture.Viewer,
                    observedTick: 2,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 0));

            DynamicParticipantVisibilityPublishResult result = fixture.Publisher.Publish(2);

            Assert.That(result.ChangedCollections, Is.Zero);
            Assert.That(result.UpsertedKnowledgeRecords, Is.EqualTo(1));
            Assert.That(fixture.Knowledge.TryGet(fixture.Viewer, target, 2, out KnowledgeDisclosureRecord restored), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(restored.AttributeMask.ContainsId(1), Is.True);
                Assert.That(restored.RelationshipTypeMask.ContainsId(2), Is.True);
                Assert.That(restored.ObservedTick, Is.EqualTo(2));
            });
        }

        [Test]
        public void PublicOwnerMatchPolicy_IncludesNeutralEntityWithoutPlayerOwner()
        {
            using var fixture = PublisherFixture.CreatePublic();
            Entity neutral = fixture.CreateNeutralParticipant();

            DynamicParticipantVisibilityPublishResult result = fixture.Publisher.Publish(5);

            Assert.That(result.ChangedCollections, Is.EqualTo(1));
            Assert.That(result.UpsertedKnowledgeRecords, Is.EqualTo(1));
            AssertCollection(fixture.Collections, fixture.Viewer, neutral);
            AssertLiveKnowledge(fixture.Knowledge, fixture.Viewer, neutral, fixture.Viewer, expectedTick: 5);
        }

        [Test]
        public void Publish_PreservesZeroTickAndZeroConfidenceWithoutInventingDisclosureValues()
        {
            using var fixture = PublisherFixture.Create(confidencePermille: 0);
            Entity target = fixture.CreatePlayerOwnedParticipant(playerId: 1, teamId: 1);

            fixture.Publisher.Publish(0);

            Assert.That(fixture.Knowledge.TryGet(fixture.Viewer, target, currentTick: 0, out KnowledgeDisclosureRecord record), Is.True);
            Assert.That(record.ObservedTick, Is.Zero);
            Assert.That(record.ConfidencePermille, Is.Zero);
            Assert.That(() => fixture.Publisher.Publish(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(StaleMutation.Destroy)]
        [TestCase(StaleMutation.PlayerOwner)]
        [TestCase(StaleMutation.Team)]
        [TestCase(StaleMutation.Map)]
        [TestCase(StaleMutation.SelectableState)]
        public void PublishRemovesCollectionRowsAndKnowledgeWhenEntityStopsMatching(StaleMutation mutation)
        {
            using var fixture = mutation == StaleMutation.Team
                ? PublisherFixture.CreateForTeamViewer()
                : PublisherFixture.Create();
            Entity target = fixture.CreatePlayerOwnedParticipant(playerId: 1, teamId: 1);
            fixture.Publisher.Publish(1);
            AssertCollection(fixture.Collections, fixture.Viewer, target);
            AssertLiveKnowledge(fixture.Knowledge, fixture.Viewer, target, expectedSource: fixture.Viewer, expectedTick: 1);

            fixture.ApplyMutation(target, mutation);
            DynamicParticipantVisibilityPublishResult result = fixture.Publisher.Publish(2);

            Assert.That(result.ChangedCollections, Is.EqualTo(1));
            Assert.That(result.RemovedKnowledgeRecords, Is.EqualTo(1));
            AssertCollection(fixture.Collections, fixture.Viewer);
            Assert.That(fixture.Knowledge.TryGet(fixture.Viewer, target, currentTick: 2, out _), Is.False);
        }

        [Test]
        public void CompilerResolvesViewerSourceComponentsAndMasksFromConfig()
        {
            using var world = World.Create();
            var mapId = new MapId("test-map");
            Entity viewer = world.Create(new PlayerIdentity { PlayerId = 1 }, new MapEntity { MapId = mapId });
            Entity target = world.Create(
                new MapEntity { MapId = mapId },
                new PlayerOwner { PlayerId = 1 },
                new Team { Id = 1 },
                new CommandSourceSelectableTag());
            var session = new MapSession(mapId, new MapConfig { Id = mapId.Value })
            {
                PlayerEntityLookup = new PlayerEntityLookup(),
                EntityIndex = new MapLoadEntityIndex()
            };
            session.PlayerEntityLookup.Register(1, viewer);
            session.EntityIndex.Register(mapId.Value, "runtime-source", target);
            var relationships = new Ludots.Core.Gameplay.Relationships.RelationshipTypeRegistry();
            int participantType = relationships.Register("Participant");
            const int healthAttributeId = 7;

            DynamicParticipantVisibilityBinding[] bindings = DynamicParticipantVisibilityCompiler.Compile(
                session,
                new[]
                {
                    DynamicParticipantQuerySpec.Create(
                        DynamicParticipantViewerKind.Player,
                        "1",
                        CollectionKey,
                        DynamicParticipantQueryClause.Create(
                            new[] { "MapEntity", "PlayerOwner", "CommandSourceSelectableTag" },
                            new[] { "PlayerIdentity" }),
                        DynamicParticipantQueryFlags.RequireSelectable |
                        DynamicParticipantQueryFlags.ExcludePlayerIdentity |
                        DynamicParticipantQueryFlags.RequireMapMatch,
                        KnowledgePresence.LiveVisible,
                        KnowledgePositionAccess.Live,
                        sourceRef: "entity:runtime-source",
                        sourceKind: DynamicParticipantSourceKind.Entity,
                        attributeIds: new[] { healthAttributeId },
                        relationshipTypes: new[] { "Participant" },
                        ownerMatchPolicy: DynamicParticipantOwnerMatchPolicy.Public)
                },
                relationships);

            Assert.That(bindings, Has.Length.EqualTo(1));
            Assert.That(bindings[0].Viewer, Is.EqualTo(viewer));
            Assert.That(bindings[0].Source, Is.EqualTo(target));
            Assert.That(bindings[0].CollectionDescriptor.SourceKind, Is.EqualTo(EntityCollectionSourceKind.DynamicParticipant));
            Assert.That(bindings[0].OwnerMatchPolicy, Is.EqualTo(DynamicParticipantOwnerMatchPolicy.Public));
            Assert.That(bindings[0].AttributeMask.ContainsId(healthAttributeId), Is.True);
            Assert.That(bindings[0].RelationshipTypeMask.ContainsId(participantType), Is.True);
        }

        private static void AssertCollection(EntityCollectionStore collections, Entity viewer, params Entity[] expected)
        {
            Span<Entity> actual = stackalloc Entity[Math.Max(1, expected.Length)];
            int written = collections.CopyEntities(viewer, CollectionKey, actual);
            Assert.That(written, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]));
            }
        }

        private static void AssertLiveKnowledge(
            KnowledgeProjectionStore knowledge,
            Entity viewer,
            Entity target,
            Entity expectedSource,
            int expectedTick)
        {
            Assert.That(knowledge.TryGet(viewer, target, expectedTick, out KnowledgeDisclosureRecord record), Is.True);
            Assert.That(record.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(record.Position, Is.EqualTo(KnowledgePositionAccess.Live));
            Assert.That(record.Source, Is.EqualTo(expectedSource));
            Assert.That(record.ObservedTick, Is.EqualTo(expectedTick));
            Assert.That(record.ConfidencePermille, Is.EqualTo(1000));
        }

        public enum StaleMutation
        {
            Destroy,
            PlayerOwner,
            Team,
            Map,
            SelectableState,
        }

        private sealed class PublisherFixture : IDisposable
        {
            private readonly MapId _mapId = new("test-map");

            private PublisherFixture(World world, Entity viewer, DynamicParticipantVisibilityPublisher publisher, EntityCollectionStore collections, KnowledgeProjectionStore knowledge)
            {
                World = world;
                Viewer = viewer;
                Publisher = publisher;
                Collections = collections;
                Knowledge = knowledge;
            }

            public World World { get; }
            public Entity Viewer { get; }
            public DynamicParticipantVisibilityPublisher Publisher { get; }
            public EntityCollectionStore Collections { get; }
            public KnowledgeProjectionStore Knowledge { get; }

            public static PublisherFixture Create(int confidencePermille = 1000)
            {
                World world = World.Create();
                var mapId = new MapId("test-map");
                Entity viewer = world.Create(new PlayerIdentity { PlayerId = 1 }, new MapEntity { MapId = mapId });
                return Create(world, viewer, mapId, BuildPlayerBinding(viewer, mapId, confidencePermille));
            }

            public static PublisherFixture CreateForTeamViewer()
            {
                World world = World.Create();
                var mapId = new MapId("test-map");
                Entity viewer = world.Create(new TeamIdentity { TeamId = 1 }, new MapEntity { MapId = mapId });
                return Create(world, viewer, mapId, BuildTeamBinding(viewer, mapId));
            }

            public static PublisherFixture CreatePublic()
            {
                World world = World.Create();
                var mapId = new MapId("test-map");
                Entity viewer = world.Create(new PlayerIdentity { PlayerId = 1 }, new MapEntity { MapId = mapId });
                return Create(world, viewer, mapId, BuildPublicBinding(viewer, mapId));
            }

            public Entity CreatePlayerOwnedParticipant(int playerId, int teamId)
            {
                return World.Create(
                    new MapEntity { MapId = _mapId },
                    new PlayerOwner { PlayerId = playerId },
                    new Team { Id = teamId },
                    new CommandSourceSelectableTag());
            }

            public Entity CreateNeutralParticipant()
            {
                return World.Create(
                    new MapEntity { MapId = _mapId },
                    new CommandSourceSelectableTag());
            }

            public void ApplyMutation(Entity target, StaleMutation mutation)
            {
                switch (mutation)
                {
                    case StaleMutation.Destroy:
                        World.Destroy(target);
                        break;
                    case StaleMutation.PlayerOwner:
                        World.Set(target, new PlayerOwner { PlayerId = 2 });
                        break;
                    case StaleMutation.Team:
                        World.Set(target, new Team { Id = 2 });
                        break;
                    case StaleMutation.Map:
                        World.Set(target, new MapEntity { MapId = new MapId("other-map") });
                        break;
                    case StaleMutation.SelectableState:
                        World.Add(target, CommandSourceSelectableState.Disabled);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
                }
            }

            public void Dispose()
            {
                World.Dispose();
            }

            private static PublisherFixture Create(
                World world,
                Entity viewer,
                in MapId mapId,
                DynamicParticipantVisibilityBinding binding)
            {
                var collections = new EntityCollectionStore(
                    new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                    initialCollectionCapacity: 4,
                    initialRowCapacity: 8);
                var knowledge = new KnowledgeProjectionStore(initialCapacity: 8);
                var publisher = new DynamicParticipantVisibilityPublisher(
                    world,
                    collections,
                    knowledge,
                    new[] { binding });
                return new PublisherFixture(world, viewer, publisher, collections, knowledge);
            }

            private static DynamicParticipantVisibilityBinding BuildPlayerBinding(
                Entity viewer,
                in MapId mapId,
                int confidencePermille)
            {
                return BuildBinding(
                    viewer,
                    mapId,
                    new[]
                    {
                        Component<MapEntity>.ComponentType,
                        Component<PlayerOwner>.ComponentType,
                        Component<CommandSourceSelectableTag>.ComponentType
                    },
                    new[] { Component<PlayerIdentity>.ComponentType },
                    DynamicParticipantQueryFlags.RequireSelectable |
                    DynamicParticipantQueryFlags.ExcludePlayerIdentity |
                    DynamicParticipantQueryFlags.RequireMapMatch,
                    confidencePermille: confidencePermille);
            }

            private static DynamicParticipantVisibilityBinding BuildTeamBinding(Entity viewer, in MapId mapId)
            {
                return BuildBinding(
                    viewer,
                    mapId,
                    new[]
                    {
                        Component<MapEntity>.ComponentType,
                        Component<Team>.ComponentType,
                        Component<CommandSourceSelectableTag>.ComponentType
                    },
                    new[]
                    {
                        Component<TeamIdentity>.ComponentType,
                        Component<PlayerIdentity>.ComponentType
                    },
                    DynamicParticipantQueryFlags.RequireSelectable |
                    DynamicParticipantQueryFlags.ExcludePlayerIdentity |
                    DynamicParticipantQueryFlags.ExcludeTeamIdentity |
                    DynamicParticipantQueryFlags.RequireMapMatch);
            }

            private static DynamicParticipantVisibilityBinding BuildPublicBinding(Entity viewer, in MapId mapId)
            {
                return BuildBinding(
                    viewer,
                    mapId,
                    new[]
                    {
                        Component<MapEntity>.ComponentType,
                        Component<CommandSourceSelectableTag>.ComponentType
                    },
                    new[]
                    {
                        Component<TeamIdentity>.ComponentType,
                        Component<PlayerIdentity>.ComponentType
                    },
                    DynamicParticipantQueryFlags.RequireSelectable |
                    DynamicParticipantQueryFlags.ExcludePlayerIdentity |
                    DynamicParticipantQueryFlags.ExcludeTeamIdentity |
                    DynamicParticipantQueryFlags.RequireMapMatch,
                    DynamicParticipantOwnerMatchPolicy.Public);
            }

            private static DynamicParticipantVisibilityBinding BuildBinding(
                Entity viewer,
                in MapId mapId,
                ComponentType[] all,
                ComponentType[] none,
                DynamicParticipantQueryFlags flags,
                DynamicParticipantOwnerMatchPolicy ownerMatchPolicy = DynamicParticipantOwnerMatchPolicy.MatchViewer,
                int confidencePermille = 1000)
            {
                return DynamicParticipantVisibilityBinding.Create(
                    viewer,
                    viewer,
                    mapId,
                    EntityCollectionDescriptor.Create(
                        CollectionKey,
                        EntityCollectionSourceKind.DynamicParticipant,
                        EntityCollectionRoleKind.Display,
                        contextEntity: viewer,
                        primaryEntity: viewer),
                    all,
                    none,
                    flags,
                    DynamicParticipantSourceKind.Viewer,
                    requiredTagId: 0,
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    KnowledgeIdMask256.Empty.WithId(1),
                    KnowledgeIdMask256.Empty.WithId(2),
                    KnowledgeIdMask256.Empty,
                    confidencePermille: confidencePermille,
                    ownerMatchPolicy: ownerMatchPolicy);
            }
        }
    }
}
