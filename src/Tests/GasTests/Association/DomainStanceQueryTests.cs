using System;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class DomainStanceQueryTests
    {
        [Test]
        public void GetStance_DirectRepEdgeWinsOverTeamEdge()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create();
            Entity repB = world.Create();
            Entity teamA = world.Create();
            Entity teamB = world.Create();
            harness.Relationships.EnsureLink(repA, teamA, harness.MemberOfTypeId);
            harness.Relationships.EnsureLink(repB, teamB, harness.MemberOfTypeId);
            harness.Relationships.EnsureLink(teamA, teamB, harness.SecondStanceId);
            harness.Relationships.EnsureLink(repA, repB, harness.FirstStanceId);

            Assert.That(harness.Query.GetStance(repA, repB), Is.EqualTo(harness.FirstStanceId));
            Assert.That(harness.Query.HasStance(repA, repB, harness.FirstStanceId), Is.True);
            Assert.That(harness.Query.HasStance(repA, repB, harness.SecondStanceId), Is.False);
        }

        [Test]
        public void GetStance_ResolvesTeamEdgeWhenNoDirectEdgeExists()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create();
            Entity repB = world.Create();
            Entity teamA = world.Create();
            Entity teamB = world.Create();
            harness.Relationships.EnsureLink(repA, teamA, harness.MemberOfTypeId);
            harness.Relationships.EnsureLink(repB, teamB, harness.MemberOfTypeId);
            harness.Relationships.EnsureLink(teamA, teamB, harness.FirstStanceId);

            Assert.That(harness.Query.GetStance(repA, repB), Is.EqualTo(harness.FirstStanceId));
        }

        [Test]
        public void GetStance_SameDomainAndSameTeamUseConfiguredStances()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create();
            Entity repB = world.Create();
            Entity team = world.Create();
            harness.Relationships.EnsureLink(repA, team, harness.MemberOfTypeId);
            harness.Relationships.EnsureLink(repB, team, harness.MemberOfTypeId);

            Assert.That(harness.Query.GetStance(repA, repA), Is.EqualTo(harness.SameDomainStanceId));
            Assert.That(harness.Query.GetStance(repA, repB), Is.EqualTo(harness.SameTeamStanceId));
        }

        [Test]
        public void GetStance_ReturnsConfiguredDefaultWhenNoEdgesExist()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create();
            Entity repB = world.Create();

            Assert.That(harness.Query.GetStance(repA, repB), Is.EqualTo(harness.DefaultStanceId));
        }

        [Test]
        public void GetStance_CachedResultIsRecomputedAfterEdgeMutations()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create();
            Entity repB = world.Create();

            Assert.That(harness.Query.GetStance(repA, repB), Is.EqualTo(harness.DefaultStanceId));
            Assert.That(harness.Query.GetStance(repA, repB), Is.EqualTo(harness.DefaultStanceId), "cache hit");

            uint beforeLink = harness.Query.Revision;
            harness.Relationships.EnsureLink(repA, repB, harness.FirstStanceId);
            Assert.That(harness.Query.Revision, Is.GreaterThan(beforeLink));
            Assert.That(harness.Query.GetStance(repA, repB), Is.EqualTo(harness.FirstStanceId));

            harness.Relationships.RemoveLink(repA, repB, harness.FirstStanceId);
            Assert.That(harness.Query.GetStance(repA, repB), Is.EqualTo(harness.DefaultStanceId));
        }

        [Test]
        public void GetStance_AllocatesZeroOnCacheHitsAfterWarmup()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create();
            Entity repB = world.Create();
            Entity teamA = world.Create();
            Entity teamB = world.Create();
            harness.Relationships.EnsureLink(repA, teamA, harness.MemberOfTypeId);
            harness.Relationships.EnsureLink(repB, teamB, harness.MemberOfTypeId);
            harness.Relationships.EnsureLink(teamA, teamB, harness.FirstStanceId);

            harness.Query.GetStance(repA, repB);
            harness.Query.GetStance(repB, repA);

            long allocated = MeasureGetStanceAllocations(harness.Query, repA, repB);
            allocated = Math.Min(allocated, MeasureGetStanceAllocations(harness.Query, repA, repB));
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static long MeasureGetStanceAllocations(DomainStanceQuery query, Entity repA, Entity repB)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                query.GetStance(repA, repB);
                query.GetStance(repB, repA);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class Harness
        {
            public RelationshipRuntime Relationships = null!;
            public DomainStanceQuery Query = null!;
            public int MemberOfTypeId;
            public int FirstStanceId;
            public int SecondStanceId;
            public int SameDomainStanceId;
            public int SameTeamStanceId;
            public int DefaultStanceId;

            public static Harness Create(World world)
            {
                var types = new RelationshipTypeRegistry();
                var metrics = new RelationshipMetricRegistry();
                var relationships = new RelationshipRuntime(
                    world,
                    types,
                    metrics,
                    new RelationshipFlagRegistry(),
                    new RelationshipBandRegistry(),
                    new RelationshipChangeBuffer(capacity: 4),
                    new RelationshipReverseIndex(world));

                // Stance names live only in this catalog construction; every assertion goes through resolved ids.
                var catalog = new RelationshipCatalogConfig
                {
                    Types = { new RelationshipTypeConfig { Id = "MemberOf" } },
                    Stance = new DomainStanceConfig
                    {
                        StanceTypes = { "Stance.First", "Stance.Second", "Stance.Fallthrough" },
                        SameDomainStance = "Stance.Second",
                        SameTeamStance = "Stance.Second",
                        DefaultStance = "Stance.Fallthrough",
                    },
                };
                RelationshipCatalogInstaller.RegisterCatalog(
                    catalog,
                    types,
                    metrics,
                    new RelationshipFlagRegistry(),
                    new RelationshipBandRegistry(),
                    new RelationshipReasonRegistry());

                int memberOfTypeId = types.GetId(catalog.Types[0].Id);
                return new Harness
                {
                    Relationships = relationships,
                    Query = DomainStanceQuery.Create(relationships, memberOfTypeId, catalog.Stance),
                    MemberOfTypeId = memberOfTypeId,
                    FirstStanceId = types.GetId(catalog.Stance.StanceTypes[0]),
                    SecondStanceId = types.GetId(catalog.Stance.StanceTypes[1]),
                    SameDomainStanceId = types.GetId(catalog.Stance.SameDomainStance),
                    SameTeamStanceId = types.GetId(catalog.Stance.SameTeamStance),
                    DefaultStanceId = types.GetId(catalog.Stance.DefaultStance),
                };
            }
        }
    }
}
