using System;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class TeamRelationQueryTests
    {
        [Test]
        public void Has_IsTrueOnlyForTheAuthoredTypeAndDirection()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, teamIds: new[] { 1, 2 });
            harness.Relationships.EnsureLink(harness.Rep(1), harness.Rep(2), harness.FirstTypeId);

            Assert.That(harness.Query.Has(1, 2, harness.FirstTypeId), Is.True);
            Assert.That(harness.Query.Has(1, 2, harness.SecondTypeId), Is.False);
            Assert.That(harness.Query.Has(2, 1, harness.FirstTypeId), Is.False, "edges are directed; symmetry is authored as two edges");
        }

        [Test]
        public void Has_CarriesNoImplicitSameTeamRelation()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, teamIds: new[] { 1 });

            Assert.That(harness.Query.Has(1, 1, harness.FirstTypeId), Is.False);

            harness.Relationships.EnsureLink(harness.Rep(1), harness.Rep(1), harness.FirstTypeId);
            Assert.That(harness.Query.Has(1, 1, harness.FirstTypeId), Is.True);
        }

        [Test]
        public void Has_NoTeamCarriesNoRelation()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, teamIds: new[] { 1 });
            harness.Relationships.EnsureLink(harness.Rep(1), harness.Rep(1), harness.FirstTypeId);

            Assert.That(harness.Query.Has(0, 1, harness.FirstTypeId), Is.False);
            Assert.That(harness.Query.Has(1, 0, harness.FirstTypeId), Is.False);
        }

        [Test]
        public void Has_ThrowsWhenAPositiveTeamHasNoRepresentative()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, teamIds: new[] { 1 });

            Assert.Throws<InvalidOperationException>(() => harness.Query.Has(1, 7, harness.FirstTypeId));
        }

        [Test]
        public void Has_EntityOverloadResolvesTeamComponents()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, teamIds: new[] { 1, 2 });
            harness.Relationships.EnsureLink(harness.Rep(1), harness.Rep(2), harness.FirstTypeId);
            Entity soldier = world.Create(new Team { Id = 1 });
            Entity enemy = world.Create(new Team { Id = 2 });
            Entity neutralProp = world.Create();

            Assert.That(harness.Query.Has(soldier, enemy, harness.FirstTypeId), Is.True);
            Assert.That(harness.Query.Has(enemy, soldier, harness.FirstTypeId), Is.False);
            Assert.That(harness.Query.Has(soldier, neutralProp, harness.FirstTypeId), Is.False);
        }

        [Test]
        public void Revision_AdvancesWhenEdgesChange()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, teamIds: new[] { 1, 2 });

            uint before = harness.Query.Revision;
            harness.Relationships.EnsureLink(harness.Rep(1), harness.Rep(2), harness.FirstTypeId);
            Assert.That(harness.Query.Revision, Is.GreaterThan(before));
            Assert.That(harness.Query.Has(1, 2, harness.FirstTypeId), Is.True);

            harness.Relationships.RemoveLink(harness.Rep(1), harness.Rep(2), harness.FirstTypeId);
            Assert.That(harness.Query.Has(1, 2, harness.FirstTypeId), Is.False);
        }

        [Test]
        public void ParseFilter_AcceptsAllOrARegisteredTypeAndRejectsEverythingElse()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, teamIds: new[] { 1, 2 });
            harness.Relationships.EnsureLink(harness.Rep(1), harness.Rep(2), harness.FirstTypeId);

            RelationFilter all = harness.Query.ParseFilter(RelationFilter.AllKeyword);
            RelationFilter first = harness.Query.ParseFilter(Harness.FirstTypeName);

            Assert.That(all.IsAll, Is.True);
            Assert.That(harness.Query.Passes(in all, 2, 1), Is.True);
            Assert.That(first.RelationTypeId, Is.EqualTo(harness.FirstTypeId));
            Assert.That(harness.Query.Passes(in first, 1, 2), Is.True);
            Assert.That(harness.Query.Passes(in first, 2, 1), Is.False);
            Assert.Throws<InvalidOperationException>(() => harness.Query.ParseFilter("Relation.Undeclared"));
            Assert.Throws<InvalidOperationException>(() => harness.Query.ParseFilter(" " + Harness.FirstTypeName));
            Assert.Throws<ArgumentException>(() => harness.Query.ParseFilter(string.Empty));
        }

        [Test]
        public void Has_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, teamIds: new[] { 1, 2 });
            harness.Relationships.EnsureLink(harness.Rep(1), harness.Rep(2), harness.FirstTypeId);
            harness.Query.Has(1, 2, harness.FirstTypeId);
            harness.Query.Has(2, 1, harness.FirstTypeId);

            long allocated = MeasureAllocations(harness);
            allocated = Math.Min(allocated, MeasureAllocations(harness));
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static long MeasureAllocations(Harness harness)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Query.Has(1, 2, harness.FirstTypeId);
                harness.Query.Has(2, 1, harness.FirstTypeId);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class Harness
        {
            public const string FirstTypeName = "Relation.First";
            public const string SecondTypeName = "Relation.Second";

            public RelationshipRuntime Relationships = null!;
            public TeamEntityLookup Teams = null!;
            public TeamRelationQuery Query = null!;
            public int FirstTypeId;
            public int SecondTypeId;

            public Entity Rep(int teamId) => Teams.Get(teamId);

            public static Harness Create(World world, int[] teamIds)
            {
                var types = new RelationshipTypeRegistry();
                int firstTypeId = types.Register(FirstTypeName);
                int secondTypeId = types.Register(SecondTypeName);
                var relationships = new RelationshipRuntime(
                    world,
                    types,
                    new RelationshipMetricRegistry(),
                    new RelationshipFlagRegistry(),
                    new RelationshipBandRegistry(),
                    new RelationshipChangeBuffer(capacity: 4),
                    new RelationshipReverseIndex(world));
                var teams = new TeamEntityLookup();
                foreach (int teamId in teamIds)
                {
                    teams.Register(teamId, world.Create(new Team { Id = teamId }));
                }

                return new Harness
                {
                    Relationships = relationships,
                    Teams = teams,
                    Query = new TeamRelationQuery(relationships, teams),
                    FirstTypeId = firstTypeId,
                    SecondTypeId = secondTypeId,
                };
            }
        }
    }
}
