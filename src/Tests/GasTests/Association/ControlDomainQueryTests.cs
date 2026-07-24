using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ControlDomainQueryTests
    {
        [Test]
        public void CollectControlled_ReturnsTransitiveOwnsSubtree()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity city = world.Create();
            Entity unit = world.Create();
            harness.Ownership.EnsureOwnership(rep, city);
            harness.Ownership.EnsureOwnership(city, unit);

            Span<Entity> buffer = stackalloc Entity[8];
            int count = harness.Query.CollectControlled(rep, buffer);
            Assert.That(buffer[..count].ToArray(), Is.EquivalentTo(new[] { city, unit }));
            Assert.That(harness.Query.IsControllableBy(rep, unit), Is.True);
            Assert.That(harness.Query.IsControllableBy(rep, city), Is.True);
        }

        [Test]
        public void CollectControlled_ExpandsGrantToDomainRepWithoutIncludingTheRep()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repB = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity ownUnit = world.Create();
            Entity proxyUnit = world.Create();
            harness.Ownership.EnsureOwnership(repA, ownUnit);
            harness.Ownership.EnsureOwnership(repB, proxyUnit);
            harness.Relationships.EnsureLink(repA, repB, harness.ControlsTypeId);

            Span<Entity> buffer = stackalloc Entity[8];
            int count = harness.Query.CollectControlled(repA, buffer);
            Assert.That(buffer[..count].ToArray(), Is.EquivalentTo(new[] { ownUnit, proxyUnit }));
            Assert.That(harness.Query.IsControllableBy(repA, proxyUnit), Is.True);
            Assert.That(harness.Query.IsControllableBy(repB, ownUnit), Is.False);
        }

        [Test]
        public void CollectControlled_IncludesDirectlyGrantedPlainUnit()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity strayUnit = world.Create();
            harness.Relationships.EnsureLink(rep, strayUnit, harness.ControlsTypeId);

            Span<Entity> buffer = stackalloc Entity[4];
            int count = harness.Query.CollectControlled(rep, buffer);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(strayUnit));
            Assert.That(harness.Query.IsControllableBy(rep, strayUnit), Is.True);
        }

        [Test]
        public void CollectControlled_DeduplicatesOwnedEntityAlsoGrantedDirectly()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity unit = world.Create();
            harness.Ownership.EnsureOwnership(rep, unit);
            harness.Relationships.EnsureLink(rep, unit, harness.ControlsTypeId);

            Span<Entity> buffer = stackalloc Entity[4];
            int count = harness.Query.CollectControlled(rep, buffer);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(unit));
        }

        [Test]
        public void CollectControlled_ShrinksAfterGrantRevokedAndRevisionAdvances()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repB = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity proxyUnit = world.Create();
            harness.Ownership.EnsureOwnership(repB, proxyUnit);
            harness.Relationships.EnsureLink(repA, repB, harness.ControlsTypeId);

            Span<Entity> buffer = stackalloc Entity[4];
            Assert.That(harness.Query.CollectControlled(repA, buffer), Is.EqualTo(1));
            uint beforeRevoke = harness.Query.Revision;

            harness.Relationships.RemoveLink(repA, repB, harness.ControlsTypeId);
            Assert.That(harness.Query.Revision, Is.GreaterThan(beforeRevoke));
            Assert.That(harness.Query.CollectControlled(repA, buffer), Is.EqualTo(0));
            Assert.That(harness.Query.IsControllableBy(repA, proxyUnit), Is.False);
        }

        [Test]
        public void CollectControlledDomains_SingleSpanReturnsOnlyFullyControlledDomains()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repB = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity repC = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity enemyUnit = world.Create();
            harness.Ownership.EnsureOwnership(repC, enemyUnit);
            harness.Relationships.EnsureLink(repA, repB, harness.ControlsTypeId);
            harness.Relationships.EnsureLink(repA, enemyUnit, harness.ControlsTypeId);

            Span<Entity> buffer = stackalloc Entity[8];
            int count = harness.Query.CollectControlledDomains(repA, buffer);
            Assert.That(buffer[..count].ToArray(), Is.EqualTo(new[] { repA, repB }),
                "Single-span overload must report only fully controlled domains.");
        }

        [Test]
        public void CollectControlledDomains_DualSpanMarksUnitGrantDomainAsPartial()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repC = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity enemy1 = world.Create();
            Entity enemy2 = world.Create();
            harness.Ownership.EnsureOwnership(repC, enemy1);
            harness.Ownership.EnsureOwnership(repC, enemy2);
            harness.Relationships.EnsureLink(repA, enemy1, harness.ControlsTypeId);

            Span<Entity> domains = stackalloc Entity[8];
            Span<bool> fullyControlled = stackalloc bool[8];
            int count = harness.Query.CollectControlledDomains(repA, domains, fullyControlled);
            Assert.That(domains[..count].ToArray(), Is.EqualTo(new[] { repA, repC }));
            Assert.That(fullyControlled[..count].ToArray(), Is.EqualTo(new[] { true, false }),
                "A domain reached only through a unit grant is partially controlled.");

            harness.Relationships.RemoveLink(repA, enemy1, harness.ControlsTypeId);
            count = harness.Query.CollectControlledDomains(repA, domains, fullyControlled);
            Assert.That(domains[..count].ToArray(), Is.EqualTo(new[] { repA }),
                "Revoking the unit grant must drop the partial domain entry.");
        }

        [Test]
        public void CollectControlledDomains_DomainlessUnitGrantProducesNoEntry()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity strayUnit = world.Create();
            harness.Relationships.EnsureLink(rep, strayUnit, harness.ControlsTypeId);

            Span<Entity> domains = stackalloc Entity[4];
            Span<bool> fullyControlled = stackalloc bool[4];
            int count = harness.Query.CollectControlledDomains(rep, domains, fullyControlled);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(domains[0], Is.EqualTo(rep));
            Assert.That(fullyControlled[0], Is.True);
        }

        [Test]
        public void CollectControlledDomains_FullGrantWinsOverUnitGrantIntoSameDomain()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repC = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity enemyUnit = world.Create();
            harness.Ownership.EnsureOwnership(repC, enemyUnit);
            harness.Relationships.EnsureLink(repA, enemyUnit, harness.ControlsTypeId);
            harness.Relationships.EnsureLink(repA, repC, harness.ControlsTypeId);

            Span<Entity> domains = stackalloc Entity[4];
            Span<bool> fullyControlled = stackalloc bool[4];
            int count = harness.Query.CollectControlledDomains(repA, domains, fullyControlled);
            Assert.That(domains[..count].ToArray(), Is.EqualTo(new[] { repA, repC }));
            Assert.That(fullyControlled[..count].ToArray(), Is.EqualTo(new[] { true, true }),
                "A domain that is both fully granted and unit-granted must report as fully controlled.");
        }

        [Test]
        public void CollectControlledDomains_DualSpanThrowsWhenFlagSpanIsShorter()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });

            Assert.That(() =>
            {
                Span<Entity> domains = stackalloc Entity[4];
                Span<bool> fullyControlled = stackalloc bool[2];
                harness.Query.CollectControlledDomains(rep, domains, fullyControlled);
            }, Throws.ArgumentException);
        }

        [Test]
        public void CollectControlledDomains_DualSpanAllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repB = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity repC = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity enemyUnit = world.Create();
            harness.Ownership.EnsureOwnership(repC, enemyUnit);
            harness.Relationships.EnsureLink(repA, repB, harness.ControlsTypeId);
            harness.Relationships.EnsureLink(repA, enemyUnit, harness.ControlsTypeId);

            var domains = new Entity[8];
            var fullyControlled = new bool[8];
            harness.Query.CollectControlledDomains(repA, domains, fullyControlled);

            long allocated = MeasureCollectControlledDomainsAllocations(harness.Query, repA, domains, fullyControlled);
            allocated = Math.Min(allocated, MeasureCollectControlledDomainsAllocations(harness.Query, repA, domains, fullyControlled));
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static long MeasureCollectControlledDomainsAllocations(
            ControlDomainQuery query,
            Entity controllerRep,
            Entity[] domains,
            bool[] fullyControlled)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                query.CollectControlledDomains(controllerRep, domains, fullyControlled);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [Test]
        public void CollectDirectUnitGrants_ReturnsOnlyPlainUnitsResolvingToTheRequestedDomain()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repB = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity repC = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity enemyC1 = world.Create();
            Entity enemyC2 = world.Create();
            Entity enemyC3 = world.Create();
            Entity unitB = world.Create();
            Entity strayUnit = world.Create();
            harness.Ownership.EnsureOwnership(repC, enemyC1);
            harness.Ownership.EnsureOwnership(repC, enemyC2);
            harness.Ownership.EnsureOwnership(repC, enemyC3);
            harness.Ownership.EnsureOwnership(repB, unitB);
            harness.Relationships.EnsureLink(repA, enemyC1, harness.ControlsTypeId);
            harness.Relationships.EnsureLink(repA, enemyC2, harness.ControlsTypeId);
            harness.Relationships.EnsureLink(repA, unitB, harness.ControlsTypeId);
            harness.Relationships.EnsureLink(repA, repB, harness.ControlsTypeId);
            harness.Relationships.EnsureLink(repA, strayUnit, harness.ControlsTypeId);

            Span<Entity> buffer = stackalloc Entity[8];
            int countC = harness.Query.CollectDirectUnitGrants(repA, repC, buffer);
            Assert.That(buffer[..countC].ToArray(), Is.EquivalentTo(new[] { enemyC1, enemyC2 }),
                "Only granted plain units resolving to the requested domain qualify; ungranted siblings never appear.");

            int countB = harness.Query.CollectDirectUnitGrants(repA, repB, buffer);
            Assert.That(buffer[..countB].ToArray(), Is.EquivalentTo(new[] { unitB }),
                "A grant to the domain rep itself is a full grant, not a unit grant.");
        }

        [Test]
        public void CollectDirectUnitGrants_ShrinksAfterRevokeAndTruncatesToBuffer()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repC = world.Create(new PlayerIdentity { PlayerId = 3 });
            Entity enemy1 = world.Create();
            Entity enemy2 = world.Create();
            harness.Ownership.EnsureOwnership(repC, enemy1);
            harness.Ownership.EnsureOwnership(repC, enemy2);
            harness.Relationships.EnsureLink(repA, enemy1, harness.ControlsTypeId);
            harness.Relationships.EnsureLink(repA, enemy2, harness.ControlsTypeId);

            Span<Entity> tiny = stackalloc Entity[1];
            Assert.That(harness.Query.CollectDirectUnitGrants(repA, repC, tiny), Is.EqualTo(1));

            Span<Entity> buffer = stackalloc Entity[4];
            harness.Relationships.RemoveLink(repA, enemy1, harness.ControlsTypeId);
            int count = harness.Query.CollectDirectUnitGrants(repA, repC, buffer);
            Assert.That(buffer[..count].ToArray(), Is.EquivalentTo(new[] { enemy2 }));

            harness.Relationships.RemoveLink(repA, enemy2, harness.ControlsTypeId);
            Assert.That(harness.Query.CollectDirectUnitGrants(repA, repC, buffer), Is.EqualTo(0));
        }

        [Test]
        public void TryResolveControlDomain_WalksMultiLevelOwnsChainToPlayerIdentityRep()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity city = world.Create();
            Entity building = world.Create();
            Entity unit = world.Create();
            harness.Ownership.EnsureOwnership(rep, city);
            harness.Ownership.EnsureOwnership(city, building);
            harness.Ownership.EnsureOwnership(building, unit);

            Assert.That(harness.Query.TryResolveControlDomain(unit, out Entity domainRep), Is.True);
            Assert.That(domainRep, Is.EqualTo(rep));
            Assert.That(harness.Query.TryResolveControlDomain(rep, out Entity selfRep), Is.True);
            Assert.That(selfRep, Is.EqualTo(rep));
        }

        [Test]
        public void TryResolveControlDomain_ReturnsFalseWhenNoDomainExists()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity orphanOwner = world.Create();
            Entity orphanUnit = world.Create();
            harness.Ownership.EnsureOwnership(orphanOwner, orphanUnit);

            Assert.That(harness.Query.TryResolveControlDomain(orphanUnit, out Entity domainRep), Is.False);
            Assert.That(domainRep, Is.EqualTo(Entity.Null));
        }

        [Test]
        public void CollectControlled_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Entity repA = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity repB = world.Create(new PlayerIdentity { PlayerId = 2 });
            for (int i = 0; i < 8; i++)
            {
                harness.Ownership.EnsureOwnership(repA, world.Create());
                harness.Ownership.EnsureOwnership(repB, world.Create());
            }

            harness.Relationships.EnsureLink(repA, repB, harness.ControlsTypeId);

            var buffer = new Entity[32];
            harness.Query.CollectControlled(repA, buffer);

            long allocated = MeasureCollectControlledAllocations(harness.Query, repA, buffer);
            allocated = Math.Min(allocated, MeasureCollectControlledAllocations(harness.Query, repA, buffer));
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static long MeasureCollectControlledAllocations(ControlDomainQuery query, Entity controllerRep, Entity[] buffer)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                query.CollectControlled(controllerRep, buffer);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class Harness
        {
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public ControlDomainQuery Query = null!;
            public int OwnsTypeId;
            public int ControlsTypeId;

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
                return new Harness
                {
                    Relationships = relationships,
                    Ownership = ownership,
                    Query = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId),
                    OwnsTypeId = ownsTypeId,
                    ControlsTypeId = controlsTypeId,
                };
            }
        }
    }
}
