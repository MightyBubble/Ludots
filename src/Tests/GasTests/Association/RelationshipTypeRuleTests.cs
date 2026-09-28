using System;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class RelationshipTypeRuleTests
    {
        [Test]
        public void TypeWithoutRules_AcceptsAnyNumberOfLinks()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRuntime(world, new RelationshipTypeConfig { Id = "Admires" });
            int admires = relationships.TypeRegistry.GetId("Admires");
            Entity hero = world.Create();
            Entity fanA = world.Create();
            Entity fanB = world.Create();

            relationships.EnsureLink(fanA, hero, admires);
            relationships.EnsureLink(fanB, hero, admires);

            Assert.That(relationships.CollectIncoming(hero, admires, stackalloc Entity[4]), Is.EqualTo(2));
        }

        [Test]
        public void MaxOutgoingReplace_MovesTheSourceToItsNewTarget()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRuntime(world, new RelationshipTypeConfig
            {
                Id = "SwornTo",
                Rules = new RelationshipTypeRulesConfig { MaxOutgoing = 1, OnFull = RelationshipCapacityPolicy.Replace },
            });
            int swornTo = relationships.TypeRegistry.GetId("SwornTo");
            Entity knight = world.Create();
            Entity oldLord = world.Create();
            Entity newLord = world.Create();
            relationships.EnsureLink(knight, oldLord, swornTo);

            relationships.EnsureLink(knight, newLord, swornTo);

            Assert.That(relationships.HasLink(knight, oldLord, swornTo), Is.False);
            Assert.That(relationships.HasLink(knight, newLord, swornTo), Is.True);
        }

        [Test]
        public void MaxOutgoingReject_FailsLoudlyAndKeepsTheExistingLink()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRuntime(world, new RelationshipTypeConfig
            {
                Id = "SwornTo",
                Rules = new RelationshipTypeRulesConfig { MaxOutgoing = 1, OnFull = RelationshipCapacityPolicy.Reject },
            });
            int swornTo = relationships.TypeRegistry.GetId("SwornTo");
            Entity knight = world.Create();
            Entity oldLord = world.Create();
            Entity newLord = world.Create();
            relationships.EnsureLink(knight, oldLord, swornTo);

            var ex = Assert.Throws<InvalidOperationException>(() => relationships.EnsureLink(knight, newLord, swornTo));

            Assert.That(ex!.Message, Does.Contain("'SwornTo'").And.Contain("maxOutgoing"));
            Assert.That(relationships.HasLink(knight, oldLord, swornTo), Is.True);
            Assert.That(relationships.HasLink(knight, newLord, swornTo), Is.False);
        }

        [Test]
        public void MaxIncomingTwoReject_AllowsTwoAndRejectsTheThird()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRuntime(world, new RelationshipTypeConfig
            {
                Id = "Guards",
                Rules = new RelationshipTypeRulesConfig { MaxIncoming = 2, OnFull = RelationshipCapacityPolicy.Reject },
            });
            int guards = relationships.TypeRegistry.GetId("Guards");
            Entity gate = world.Create();

            relationships.EnsureLink(world.Create(), gate, guards);
            relationships.EnsureLink(world.Create(), gate, guards);

            Assert.Throws<InvalidOperationException>(() => relationships.EnsureLink(world.Create(), gate, guards));
            Assert.That(relationships.CollectIncoming(gate, guards, stackalloc Entity[4]), Is.EqualTo(2));
        }

        [Test]
        public void Acyclic_RejectsSelfLinksAndLinksThatCloseACycle()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRuntime(world, new RelationshipTypeConfig
            {
                Id = "Owns",
                Rules = new RelationshipTypeRulesConfig { MaxIncoming = 1, OnFull = RelationshipCapacityPolicy.Replace, Acyclic = true },
            });
            int owns = relationships.TypeRegistry.GetId("Owns");
            Entity kingdom = world.Create();
            Entity castle = world.Create();
            Entity garrison = world.Create();
            relationships.EnsureLink(kingdom, castle, owns);
            relationships.EnsureLink(castle, garrison, owns);

            Assert.Throws<InvalidOperationException>(() => relationships.EnsureLink(garrison, garrison, owns));
            var ex = Assert.Throws<InvalidOperationException>(() => relationships.EnsureLink(garrison, castle, owns));

            Assert.That(ex!.Message, Does.Contain("cycle"));
            Assert.That(relationships.HasLink(kingdom, castle, owns), Is.True, "A rejected link must not replace the existing owner.");
            Assert.That(relationships.HasLink(garrison, castle, owns), Is.False);
        }

        [Test]
        public void BlockedAny_RejectsTheLinkWhileThePairHasTheBlockingType()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRuntime(
                world,
                new RelationshipTypeConfig { Id = "AtWar" },
                new RelationshipTypeConfig { Id = "Trades", Rules = new RelationshipTypeRulesConfig { BlockedAny = { "AtWar" } } });
            int atWar = relationships.TypeRegistry.GetId("AtWar");
            int trades = relationships.TypeRegistry.GetId("Trades");
            Entity red = world.Create();
            Entity blue = world.Create();
            relationships.EnsureLink(red, blue, atWar);

            var ex = Assert.Throws<InvalidOperationException>(() => relationships.EnsureLink(red, blue, trades));
            Assert.That(ex!.Message, Does.Contain("'AtWar'").And.Contain("blockedAny"));

            relationships.RemoveLink(red, blue, atWar);
            relationships.EnsureLink(red, blue, trades);
            Assert.That(relationships.HasLink(red, blue, trades), Is.True);
        }

        [Test]
        public void Removed_ReplacesTheExclusiveTypeBetweenTheSamePair()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRuntime(
                world,
                new RelationshipTypeConfig { Id = "Hostile", Rules = new RelationshipTypeRulesConfig { Removed = { "Allied" } } },
                new RelationshipTypeConfig { Id = "Allied", Rules = new RelationshipTypeRulesConfig { Removed = { "Hostile" } } });
            int hostile = relationships.TypeRegistry.GetId("Hostile");
            int allied = relationships.TypeRegistry.GetId("Allied");
            Entity red = world.Create();
            Entity blue = world.Create();
            Entity green = world.Create();
            relationships.EnsureLink(red, blue, hostile);
            relationships.EnsureLink(red, green, hostile);

            relationships.EnsureLink(red, blue, allied);

            Assert.That(relationships.HasLink(red, blue, hostile), Is.False);
            Assert.That(relationships.HasLink(red, blue, allied), Is.True);
            Assert.That(relationships.HasLink(red, green, hostile), Is.True, "Exclusion only touches the same source → target pair.");
        }

        [TestCase(1, 0, RelationshipCapacityPolicy.None, "onFull must say")]
        [TestCase(0, 0, RelationshipCapacityPolicy.Replace, "no maxIncoming")]
        [TestCase(2, 0, RelationshipCapacityPolicy.Replace, "maximum of 1")]
        [TestCase(-1, 0, RelationshipCapacityPolicy.Reject, "must not be negative")]
        public void InstallFromCatalog_RejectsInconsistentCapacity(int maxIncoming, int maxOutgoing, RelationshipCapacityPolicy onFull, string expected)
        {
            var catalog = Catalog(new RelationshipTypeConfig
            {
                Id = "Owns",
                Rules = new RelationshipTypeRulesConfig { MaxIncoming = maxIncoming, MaxOutgoing = maxOutgoing, OnFull = onFull },
            });

            var ex = Assert.Throws<InvalidOperationException>(() => Install(catalog));
            Assert.That(ex!.Message, Does.Contain("'Owns'").And.Contain(expected));
        }

        [Test]
        public void InstallFromCatalog_RejectsDirectionalRulesOnSymmetricTypes()
        {
            var catalog = Catalog(new RelationshipTypeConfig
            {
                Id = "Allied",
                IsSymmetric = true,
                Rules = new RelationshipTypeRulesConfig { Acyclic = true },
            });

            var ex = Assert.Throws<InvalidOperationException>(() => Install(catalog));
            Assert.That(ex!.Message, Does.Contain("'Allied'").And.Contain("symmetric"));
        }

        [Test]
        public void InstallFromCatalog_RejectsUndeclaredSelfAndContradictoryTypeReferences()
        {
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => Install(Catalog(
                    new RelationshipTypeConfig { Id = "Trades", Rules = new RelationshipTypeRulesConfig { BlockedAny = { "AtWar" } } })))!.Message,
                Does.Contain("undeclared relationship type 'AtWar'"));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => Install(Catalog(
                    new RelationshipTypeConfig { Id = "Trades", Rules = new RelationshipTypeRulesConfig { Removed = { "Trades" } } })))!.Message,
                Does.Contain("must not name the type itself"));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => Install(Catalog(
                    new RelationshipTypeConfig { Id = "AtWar" },
                    new RelationshipTypeConfig { Id = "Trades", Rules = new RelationshipTypeRulesConfig { BlockedAny = { "AtWar" }, Removed = { "AtWar" } } })))!.Message,
                Does.Contain("both blockedAny and removed"));
        }

        [Test]
        public void DefaultCatalog_DeclaresSingleOwnerReplaceAndNoCyclesOnOwns()
        {
            using World world = World.Create();
            var types = new RelationshipTypeRegistry();
            int owns = types.Register("Owns");
            RelationshipRuntime relationships = NewRuntime(world, types);

            DefaultRelationshipRules.Install(relationships);

            Assert.That(relationships.Rules.TryGet(owns, out RelationshipTypeRule rule), Is.True);
            Assert.That(rule.MaxIncoming, Is.EqualTo(1));
            Assert.That(rule.OnFull, Is.EqualTo(RelationshipCapacityPolicy.Replace));
            Assert.That(rule.Acyclic, Is.True);
        }

        private static RelationshipCatalogConfig Catalog(params RelationshipTypeConfig[] types)
        {
            var catalog = new RelationshipCatalogConfig();
            catalog.Types.AddRange(types);
            return catalog;
        }

        private static void Install(RelationshipCatalogConfig catalog)
        {
            using World world = World.Create();
            CreateRuntime(world, catalog);
        }

        private static RelationshipRuntime CreateRuntime(World world, params RelationshipTypeConfig[] types)
        {
            return CreateRuntime(world, Catalog(types));
        }

        private static RelationshipRuntime CreateRuntime(World world, RelationshipCatalogConfig catalog)
        {
            var types = new RelationshipTypeRegistry();
            var metrics = new RelationshipMetricRegistry();
            var flags = new RelationshipFlagRegistry();
            var bands = new RelationshipBandRegistry();
            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, metrics, flags, bands);
            RelationshipRuntime relationships = NewRuntime(world, types, metrics, flags, bands);
            relationships.Rules.InstallFromCatalog(catalog);
            return relationships;
        }

        private static RelationshipRuntime NewRuntime(
            World world,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry? metrics = null,
            RelationshipFlagRegistry? flags = null,
            RelationshipBandRegistry? bands = null)
        {
            return new RelationshipRuntime(
                world,
                types,
                metrics ?? new RelationshipMetricRegistry(),
                flags ?? new RelationshipFlagRegistry(),
                bands ?? new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 32),
                new RelationshipReverseIndex(world));
        }
    }
}
