using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ParticipantIdentityProjectionTests
    {
        [Test]
        public void EnsureOwnership_ProjectsPlayerOwnerFromPlayerIdentity_AndFollowsLaterTransfers()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity playerA = world.Create(new PlayerIdentity { PlayerId = 4 });
            Entity playerB = world.Create(new PlayerIdentity { PlayerId = 9 });
            Entity unit = world.Create();

            harness.Ownership.EnsureOwnership(playerA, unit);

            Assert.That(world.TryGet(unit, out PlayerOwner owner), Is.True);
            Assert.That(owner.PlayerId, Is.EqualTo(4));

            harness.Ownership.EnsureOwnership(playerB, unit);

            Assert.That(world.Get<PlayerOwner>(unit).PlayerId, Is.EqualTo(9));
            Assert.That(harness.Relationships.HasLink(playerA, unit, harness.OwnsTypeId), Is.False);
            Assert.That(harness.Relationships.HasLink(playerB, unit, harness.OwnsTypeId), Is.True);

            harness.Ownership.ClearOwnership(unit);

            Assert.That(world.Has<PlayerOwner>(unit), Is.False);
            Assert.That(harness.Ownership.TryGetDirectOwner(unit, out _), Is.False);
        }

        [Test]
        public void RuntimeSpawn_WithPlayerOwnerOverride_ProjectsComponentFromRepresentative()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 3 }, new PlayerOwner { PlayerId = 3 });
            harness.Players.Register(3, rep);

            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                WorldPositionCm = Fix64Vec2.FromInt(1, 2),
                HasWorldPosition = 1,
                PlayerOwnerIdOverride = 3,
            }), Is.True);
            harness.System.Update(0f);

            Entity spawned = FindSpawnedUnit(world, rep);
            Assert.That(world.Get<PlayerOwner>(spawned).PlayerId, Is.EqualTo(3));
            Assert.That(harness.Relationships.HasLink(rep, spawned, harness.OwnsTypeId), Is.True);
        }

        [Test]
        public void RuntimeSpawn_WithMembershipTarget_ProjectsTeamFromTeamIdentity()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity team = world.Create(new TeamIdentity { TeamId = 12 }, new Team { Id = 12 });

            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                WorldPositionCm = Fix64Vec2.FromInt(4, 5),
                HasWorldPosition = 1,
                MembershipTarget = team,
                HasMembershipTarget = 1,
            }), Is.True);
            harness.System.Update(0f);

            Entity spawned = FindSpawnedUnit(world, team);
            Assert.That(world.Get<Team>(spawned).Id, Is.EqualTo(12));
            Assert.That(harness.Relationships.HasLink(spawned, team, harness.MemberOfTypeId), Is.True);
        }

        [Test]
        public void RuntimeSpawn_WithoutBoundRep_KeepsAuthoredPlayerIdUntilAnEdgeExists()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Assert.That(harness.Requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                WorldPositionCm = Fix64Vec2.FromInt(1, 2),
                HasWorldPosition = 1,
                PlayerOwnerIdOverride = 42,
            }), Is.True);
            harness.System.Update(0f);

            Entity spawned = FindSpawnedUnit(world, Entity.Null);
            Assert.That(world.Get<PlayerOwner>(spawned).PlayerId, Is.EqualTo(42));
            Assert.That(harness.Ownership.TryGetDirectOwner(spawned, out _), Is.False);
        }

        private static Entity FindSpawnedUnit(World world, Entity exclude)
        {
            Entity spawned = Entity.Null;
            var query = new QueryDescription().WithAll<WorldPositionCm>().WithNone<PlayerIdentity, TeamIdentity>();
            world.Query(in query, (Entity entity) =>
            {
                if (entity != exclude)
                {
                    spawned = entity;
                }
            });
            return spawned;
        }

        private sealed class Harness
        {
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public PlayerEntityLookup Players = null!;
            public RuntimeEntitySpawnQueue Requests = null!;
            public RuntimeEntitySpawnSystem System = null!;
            public int OwnsTypeId;
            public int MemberOfTypeId;

            public static Harness Create(World world)
            {
                var types = new RelationshipTypeRegistry();
                var relationships = new RelationshipRuntime(
                    world,
                    types,
                    new RelationshipMetricRegistry(),
                    new RelationshipFlagRegistry(),
                    new RelationshipBandRegistry(),
                    new RelationshipChangeBuffer(capacity: 8),
                    new RelationshipReverseIndex(world));
                int ownsTypeId = types.Register("Owns");
                int memberOfTypeId = types.Register("MemberOf");
                var ownership = new OwnershipResolver(relationships, ownsTypeId);
                ownership.BindIdentityProjection(world);
                var players = new PlayerEntityLookup();
                var requests = new RuntimeEntitySpawnQueue(capacity: 8);
                var system = new RuntimeEntitySpawnSystem(
                    world,
                    requests,
                    new DataRegistry<EntityTemplate>(null!),
                    new EntityTemplateKeyRegistry(),
                    new PresentationStableIdAllocator(),
                    ownership: ownership,
                    playerLookup: players,
                    relationships: relationships,
                    memberOfTypeId: memberOfTypeId,
                    ownsTypeId: ownsTypeId);
                return new Harness
                {
                    Relationships = relationships,
                    Ownership = ownership,
                    Players = players,
                    Requests = requests,
                    System = system,
                    OwnsTypeId = ownsTypeId,
                    MemberOfTypeId = memberOfTypeId,
                };
            }
        }
    }
}
