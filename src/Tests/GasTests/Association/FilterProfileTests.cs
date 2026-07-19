using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>RFC-0065 CTX-4: FilterProfile registry, association expansion cache and tag rules.</summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class FilterProfileTests
    {
        private const string DefaultProfileId = "filter.controllable.default";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void Evaluate_KeepsOnlyControlsReachableEntities_AndReexpandsAfterGrant()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallDefaultProfile();

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity m01 = world.Create();
            Entity m02 = world.Create();
            Entity m99 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);
            harness.Ownership.EnsureOwnership(p2Rep, m99);

            int profileId = harness.ProfileIds.GetId(DefaultProfileId);
            Span<Entity> filtered = stackalloc Entity[8];
            int count = harness.Filters.Evaluate(profileId, p1Rep, stackalloc Entity[] { m01, m02, m99 }, filtered);
            Assert.That(filtered[..count].ToArray(), Is.EqualTo(new[] { m01, m02 }), "m99 is outside P1's control domain.");

            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);
            count = harness.Filters.Evaluate(profileId, p1Rep, stackalloc Entity[] { m01, m02, m99 }, filtered);
            Assert.That(filtered[..count].ToArray(), Is.EqualTo(new[] { m01, m02, m99 }), "Grant must trigger re-expansion.");
        }

        [Test]
        public void Evaluate_ExcludeAnyTags_FiltersTaggedEntity()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            int deadTagId = TagRegistry.Register("state.dead");
            harness.InstallDefaultProfile();

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity m01 = world.Create();
            Entity m02 = world.Create(new GameplayTagContainer());
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);
            world.Get<GameplayTagContainer>(m02).AddTag(deadTagId);

            int profileId = harness.ProfileIds.GetId(DefaultProfileId);
            Span<Entity> filtered = stackalloc Entity[8];
            int count = harness.Filters.Evaluate(profileId, p1Rep, stackalloc Entity[] { m01, m02 }, filtered);
            Assert.That(filtered[..count].ToArray(), Is.EqualTo(new[] { m01 }));
        }

        [Test]
        public void Evaluate_IncludeAnyTags_RequiresAtLeastOneMatch()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            int selectableTagId = TagRegistry.Register("unit.selectable");
            harness.Filters.Install(Harness.Config(new FilterProfileDefinition
            {
                Id = "filter.selectable.only",
                AssociationQuery = new FilterProfileAssociationQuery { Anchor = "localPlayerRep", Expand = "controls" },
                Include = new FilterProfileTagRule { AnyTags = new List<string> { "unit.selectable" } },
            }));

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity tagged = world.Create(new GameplayTagContainer());
            Entity untagged = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, tagged);
            harness.Ownership.EnsureOwnership(p1Rep, untagged);
            world.Get<GameplayTagContainer>(tagged).AddTag(selectableTagId);

            int profileId = harness.ProfileIds.GetId("filter.selectable.only");
            Span<Entity> filtered = stackalloc Entity[8];
            int count = harness.Filters.Evaluate(profileId, p1Rep, stackalloc Entity[] { tagged, untagged }, filtered);
            Assert.That(filtered[..count].ToArray(), Is.EqualTo(new[] { tagged }));
        }

        [Test]
        public void Evaluate_ExpandNone_SkipsAssociationFilteringButKeepsTagRules()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            int deadTagId = TagRegistry.Register("state.dead");
            harness.Filters.Install(Harness.Config(new FilterProfileDefinition
            {
                Id = "filter.anything.alive",
                AssociationQuery = new FilterProfileAssociationQuery { Anchor = "localPlayerRep", Expand = "none" },
                Exclude = new FilterProfileTagRule { AnyTags = new List<string> { "state.dead" } },
            }));

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity m99 = world.Create();
            Entity dead = world.Create(new GameplayTagContainer());
            harness.Ownership.EnsureOwnership(p2Rep, m99);
            harness.Ownership.EnsureOwnership(p2Rep, dead);
            world.Get<GameplayTagContainer>(dead).AddTag(deadTagId);

            int profileId = harness.ProfileIds.GetId("filter.anything.alive");
            Span<Entity> filtered = stackalloc Entity[8];
            int count = harness.Filters.Evaluate(profileId, p1Rep, stackalloc Entity[] { m99, dead }, filtered);
            Assert.That(filtered[..count].ToArray(), Is.EqualTo(new[] { m99 }), "expand=none must not apply domain filtering.");
        }

        [Test]
        public void Evaluate_ReusesExpansionCache_ZeroAllocOnUnchangedTopology()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallDefaultProfile();

            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = world.Create(new PlayerIdentity { PlayerId = 2 });
            var raw = new Entity[16];
            for (int i = 0; i < 8; i++)
            {
                Entity own = world.Create();
                Entity foreign = world.Create();
                harness.Ownership.EnsureOwnership(p1Rep, own);
                harness.Ownership.EnsureOwnership(p2Rep, foreign);
                raw[i * 2] = own;
                raw[(i * 2) + 1] = foreign;
            }

            int profileId = harness.ProfileIds.GetId(DefaultProfileId);
            var filtered = new Entity[16];
            int warmupCount = harness.Filters.Evaluate(profileId, p1Rep, raw, filtered);
            Assert.That(warmupCount, Is.EqualTo(8));

            long allocated = MeasureEvaluateAllocations(harness, profileId, p1Rep, raw, filtered);
            allocated = Math.Min(allocated, MeasureEvaluateAllocations(harness, profileId, p1Rep, raw, filtered));
            Assert.That(allocated, Is.EqualTo(0), "Cache-valid Evaluate must be allocation free.");

            harness.Relationships.EnsureLink(p1Rep, p2Rep, harness.ControlsTypeId);
            int afterGrant = harness.Filters.Evaluate(profileId, p1Rep, raw, filtered);
            Assert.That(afterGrant, Is.EqualTo(16), "Topology change must invalidate the cached expansion.");
        }

        [Test]
        public void Install_UnknownExpandKind_Throws()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Assert.Throws<InvalidOperationException>(() => harness.Filters.Install(Harness.Config(new FilterProfileDefinition
            {
                Id = "filter.bad.expand",
                AssociationQuery = new FilterProfileAssociationQuery { Anchor = "localPlayerRep", Expand = "teleports" },
            })));
        }

        [Test]
        public void Install_UnknownAnchorKind_Throws()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Assert.Throws<InvalidOperationException>(() => harness.Filters.Install(Harness.Config(new FilterProfileDefinition
            {
                Id = "filter.bad.anchor",
                AssociationQuery = new FilterProfileAssociationQuery { Anchor = "somebodyElse", Expand = "controls" },
            })));
        }

        [Test]
        public void Install_UnknownTag_UnderFrozenTagRegistry_Throws()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            TagRegistry.Register("state.dead");
            TagRegistry.Freeze();
            try
            {
                Assert.Throws<InvalidOperationException>(() => harness.Filters.Install(Harness.Config(new FilterProfileDefinition
                {
                    Id = "filter.bad.tag",
                    AssociationQuery = new FilterProfileAssociationQuery { Anchor = "localPlayerRep", Expand = "controls" },
                    Exclude = new FilterProfileTagRule { AnyTags = new List<string> { "state.daed" } },
                })));
            }
            finally
            {
                TagRegistry.Clear();
            }
        }

        [Test]
        public void Install_DuplicateProfileId_Throws()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallDefaultProfile();

            Assert.Throws<InvalidOperationException>(() => harness.InstallDefaultProfile());
        }

        [Test]
        public void Evaluate_UninstalledProfileId_Throws()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            int danglingId = harness.ProfileIds.Register("filter.declared.but.never.installed");

            Assert.Throws<InvalidOperationException>(() =>
            {
                var filtered = new Entity[1];
                harness.Filters.Evaluate(danglingId, p1Rep, new[] { p1Rep }, filtered);
            });
        }

        private static long MeasureEvaluateAllocations(Harness harness, int profileId, Entity anchor, Entity[] raw, Entity[] filtered)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Filters.Evaluate(profileId, anchor, raw, filtered);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        internal sealed class Harness
        {
            public RelationshipRuntime Relationships = null!;
            public OwnershipResolver Ownership = null!;
            public FilterProfileRegistry Filters = null!;
            public StringIntRegistry ProfileIds = null!;
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
                var domains = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
                var profileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var filters = new FilterProfileRegistry(profileIds, world, new TagOps(new TagRuleRegistry(), new GasBudget()));
                filters.RegisterExpander(
                    FilterAssociationExpandKinds.Controls,
                    domains.CollectControlled,
                    () => domains.Revision);
                return new Harness
                {
                    Relationships = relationships,
                    Ownership = ownership,
                    Filters = filters,
                    ProfileIds = profileIds,
                    ControlsTypeId = controlsTypeId,
                };
            }

            public void InstallDefaultProfile()
            {
                Filters.Install(Config(new FilterProfileDefinition
                {
                    Id = DefaultProfileId,
                    AssociationQuery = new FilterProfileAssociationQuery { Anchor = "localPlayerRep", Expand = "controls" },
                    Exclude = new FilterProfileTagRule { AnyTags = new List<string> { "state.dead", "presentation.hidden" } },
                    Include = new FilterProfileTagRule { AnyTags = new List<string>() },
                }));
            }

            public static FilterProfilesConfig Config(params FilterProfileDefinition[] profiles)
            {
                return new FilterProfilesConfig { Profiles = new List<FilterProfileDefinition>(profiles) };
            }
        }
    }
}
