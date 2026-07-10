using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>RFC-0065 CTRL-2: runtime spawns join the owns topology exactly like map-load binding.</summary>
    [TestFixture]
    public sealed class OwnershipEdgeBuilderTests
    {
        [Test]
        public void RuntimeSpawn_WithPlayerOwnerOverride_LinksOwnsEdgeAndResolvesControlDomain()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 3 }, new PlayerOwner { PlayerId = 3 });
            harness.Players.Register(3, rep);

            Entity spawned = SpawnAssembly(harness, playerOwnerIdOverride: 3);

            Assert.That(harness.Relationships.HasLink(rep, spawned, harness.OwnsTypeId), Is.True);
            Assert.That(harness.ControlDomains.TryResolveControlDomain(spawned, out Entity domain), Is.True);
            Assert.That(domain, Is.EqualTo(rep));
        }

        [Test]
        public void RuntimeSpawn_CopyingPlayerOwnerFromSource_LinksOwnsEdge()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 5 }, new PlayerOwner { PlayerId = 5 });
            harness.Players.Register(5, rep);
            Entity source = world.Create(new PlayerOwner { PlayerId = 5 });

            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                Source = source,
                WorldPositionCm = Fix64Vec2.FromInt(10, 20),
                HasWorldPosition = 1,
                CopySourcePlayerOwner = 1,
            }), Is.True);
            harness.System.Update(0f);

            Entity spawned = FindSpawnedWithOwner(world, playerId: 5, exclude1: rep, exclude2: source);
            Assert.That(spawned, Is.Not.EqualTo(Entity.Null));
            Assert.That(harness.Relationships.HasLink(rep, spawned, harness.OwnsTypeId), Is.True);
        }

        [Test]
        public void RuntimeSpawn_WithoutBoundRep_CreatesNoEdge()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity spawned = SpawnAssembly(harness, playerOwnerIdOverride: 42);

            Assert.That(harness.Ownership.TryGetDirectOwner(spawned, out _), Is.False,
                "A PlayerOwner id with no bound rep has no control domain, so no edge exists.");
        }

        private static Entity SpawnAssembly(Harness harness, int playerOwnerIdOverride)
        {
            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                WorldPositionCm = Fix64Vec2.FromInt(1, 2),
                HasWorldPosition = 1,
                PlayerOwnerIdOverride = playerOwnerIdOverride,
            }), Is.True);
            harness.System.Update(0f);
            return FindSpawnedWithOwner(harness.World, playerOwnerIdOverride, Entity.Null, Entity.Null);
        }

        private static Entity FindSpawnedWithOwner(World world, int playerId, Entity exclude1, Entity exclude2)
        {
            Entity spawned = Entity.Null;
            var query = new QueryDescription().WithAll<PlayerOwner>().WithNone<PlayerIdentity>();
            world.Query(in query, (Entity entity, ref PlayerOwner owner) =>
            {
                if (owner.PlayerId == playerId && entity != exclude1 && entity != exclude2)
                {
                    spawned = entity;
                }
            });
            return spawned;
        }

        private sealed class Harness
        {
            public World World = null!;
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public ControlDomainQuery ControlDomains = null!;
            public PlayerEntityLookup Players = null!;
            public RuntimeEntitySpawnQueue Requests = null!;
            public RuntimeEntitySpawnSystem System = null!;
            public int OwnsTypeId;

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
                var players = new PlayerEntityLookup();
                var requests = new RuntimeEntitySpawnQueue(capacity: 8);
                var system = new RuntimeEntitySpawnSystem(
                    world,
                    requests,
                    new DataRegistry<EntityTemplate>(null!),
                    new EntityTemplateKeyRegistry(),
                    new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                    ownership: ownership,
                    playerLookup: players);
                return new Harness
                {
                    World = world,
                    Relationships = relationships,
                    Ownership = ownership,
                    ControlDomains = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId),
                    Players = players,
                    Requests = requests,
                    System = system,
                    OwnsTypeId = ownsTypeId,
                };
            }
        }
    }
}
