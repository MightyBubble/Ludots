using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;
using Ludots.Core.UI.EntityCommandPanels;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 PNL-1/2/3 (DEC-10) — AbilityAggregationProfileRegistry against the §6.1 M6 catalog
    /// cases: two marines + one elite marine + two tanks, a shared stim ability in one cast family,
    /// and two charge-cannon templates sharing another. All family/ability names are test data,
    /// never Core concepts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class AbilityAggregationProfileTests
    {
        private const string FamilyTagPrefix = "castFamily";
        private const string StimFamilyTag = "castFamily.stimpack";
        private const string ChargeFamilyTag = "castFamily.charge_shot";

        private const int StimAbilityId = 101;
        private const int TankChargeAbilityId = 201;
        private const int EliteChargeAbilityId = 202;
        private const int FormVariantAbilityId = 203;
        private const int AttackAbilityId = 301;

        private const string ByFamilyProfileId = "aggregation.by_family";
        private const string ByTemplateProfileId = "aggregation.by_template";
        private const string ByAbilityIdProfileId = "aggregation.by_ability_id";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void ByFamily_GroupsAcrossTemplates_UntaggedAbilityFormsItsOwnGroup()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Selection selection = harness.CreateM6Selection();

            var result = new AbilityAggregationResult();
            int groupCount = harness.Registry.BuildGroups(
                harness.ProfileId(ByFamilyProfileId), selection.Members, world, harness.Abilities, ref result);

            Assert.That(groupCount, Is.EqualTo(3));

            // Catalog-tag groups (kind 1) sort before ability-identity groups (kind 2), tag id ascending.
            Assert.That(result.GetGroupKey(0), Is.EqualTo(CatalogKey(StimFamilyTag)));
            Assert.That(result.GroupEntities(0).ToArray(), Is.EqualTo(new[] { selection.Marine1, selection.Marine2, selection.Elite }),
                "stim family: both marines plus the elite marine.");

            Assert.That(result.GetGroupKey(1), Is.EqualTo(CatalogKey(ChargeFamilyTag)));
            Assert.That(result.GroupEntities(1).ToArray(), Is.EqualTo(new[] { selection.Elite, selection.Tank1, selection.Tank2 }),
                "charge family: one group of 3 across the two templates.");

            Assert.That(result.GetGroupKey(2), Is.EqualTo(IdentityKey(AttackAbilityId)));
            Assert.That(result.GroupEntities(2).Length, Is.EqualTo(5), "untagged attack forms its own per-ability group.");
        }

        [Test]
        public void ByTemplate_SplitsChargeCannonTemplates()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Selection selection = harness.CreateM6Selection();

            var result = new AbilityAggregationResult();
            int groupCount = harness.Registry.BuildGroups(
                harness.ProfileId(ByTemplateProfileId), selection.Members, world, harness.Abilities, ref result);

            Assert.That(groupCount, Is.EqualTo(4));
            AssertGroup(result, 0, IdentityKey(StimAbilityId), 3);
            AssertGroup(result, 1, IdentityKey(TankChargeAbilityId), 2);
            AssertGroup(result, 2, IdentityKey(EliteChargeAbilityId), 1);
            AssertGroup(result, 3, IdentityKey(AttackAbilityId), 5);
            Assert.That(result.GroupEntities(2).ToArray(), Is.EqualTo(new[] { selection.Elite }));
        }

        [Test]
        public void ByAbilityId_EachDistinctAbilityIdOwnGroup_SameKeysAsTemplate()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Selection selection = harness.CreateM6Selection();

            var byAbility = new AbilityAggregationResult();
            var byTemplate = new AbilityAggregationResult();
            int abilityGroups = harness.Registry.BuildGroups(
                harness.ProfileId(ByAbilityIdProfileId), selection.Members, world, harness.Abilities, ref byAbility);
            int templateGroups = harness.Registry.BuildGroups(
                harness.ProfileId(ByTemplateProfileId), selection.Members, world, harness.Abilities, ref byTemplate);

            Assert.That(abilityGroups, Is.EqualTo(4), "every distinct ability id is its own group.");
            // Ability ids are definition ids in this repo, so template.id and ability.id are the same key.
            Assert.That(templateGroups, Is.EqualTo(abilityGroups));
            for (int i = 0; i < abilityGroups; i++)
            {
                Assert.That(byAbility.GetGroupKey(i), Is.EqualTo(byTemplate.GetGroupKey(i)));
                Assert.That(byAbility.GroupEntities(i).ToArray(), Is.EqualTo(byTemplate.GroupEntities(i).ToArray()));
            }
        }

        [Test]
        public void EmptySelection_ZeroGroups_AndMembersWithoutSlotsAreSkipped()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            var result = new AbilityAggregationResult();
            int emptyCount = harness.Registry.BuildGroups(
                harness.ProfileId(ByFamilyProfileId), ReadOnlySpan<Entity>.Empty, world, harness.Abilities, ref result);
            Assert.That(emptyCount, Is.EqualTo(0));
            Assert.That(result.MemberCount, Is.EqualTo(0));

            Entity slotless = world.Create();
            Span<Entity> members = stackalloc Entity[] { slotless };
            int skippedCount = harness.Registry.BuildGroups(
                harness.ProfileId(ByFamilyProfileId), members, world, harness.Abilities, ref result);
            Assert.That(skippedCount, Is.EqualTo(0), "a member without an AbilityStateBuffer contributes no slots.");
        }

        [Test]
        public void FormSetOverride_RecomputesGroupsFromEffectiveAbilities()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Selection selection = harness.CreateM6Selection();
            int profileId = harness.ProfileId(ByFamilyProfileId);

            var result = new AbilityAggregationResult();
            harness.Registry.BuildGroups(profileId, selection.Members, world, harness.Abilities, ref result);
            Assert.That(result.GroupEntities(1).Length, Is.EqualTo(3), "pre-override: charge family has 3 members.");

            // Form switch remaps the elite marine's slot 2 (charge cannon -> untagged form variant).
            ref var formSlots = ref world.Get<AbilityFormSlotBuffer>(selection.Elite);
            formSlots.SetOverride(2, FormVariantAbilityId);

            int groupCount = harness.Registry.BuildGroups(profileId, selection.Members, world, harness.Abilities, ref result);
            Assert.That(groupCount, Is.EqualTo(4));
            Assert.That(result.GetGroupKey(1), Is.EqualTo(CatalogKey(ChargeFamilyTag)));
            Assert.That(result.GroupEntities(1).ToArray(), Is.EqualTo(new[] { selection.Tank1, selection.Tank2 }),
                "aggregation follows the AbilitySlotResolver result, not the base slot.");
            AssertGroup(result, 2, IdentityKey(FormVariantAbilityId), 1);

            formSlots.Clear(2);
            harness.Registry.BuildGroups(profileId, selection.Members, world, harness.Abilities, ref result);
            Assert.That(result.GroupEntities(1).Length, Is.EqualTo(3), "clearing the override restores the base grouping.");
        }

        [Test]
        public void BuildGroups_DeterministicOrdering_KeyAscThenEntityIdAscThenSlotAsc()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Selection selection = harness.CreateM6Selection();
            int profileId = harness.ProfileId(ByFamilyProfileId);

            // Reversed member order must not change the output.
            Entity[] reversed = selection.Members.ToArray();
            Array.Reverse(reversed);

            var forward = new AbilityAggregationResult();
            var backward = new AbilityAggregationResult();
            int forwardCount = harness.Registry.BuildGroups(profileId, selection.Members, world, harness.Abilities, ref forward);
            int backwardCount = harness.Registry.BuildGroups(profileId, reversed, world, harness.Abilities, ref backward);

            Assert.That(backwardCount, Is.EqualTo(forwardCount));
            for (int g = 0; g < forwardCount; g++)
            {
                Assert.That(backward.GetGroupKey(g), Is.EqualTo(forward.GetGroupKey(g)));
                Assert.That(backward.GroupEntities(g).ToArray(), Is.EqualTo(forward.GroupEntities(g).ToArray()));
                Assert.That(backward.GroupSlotIndices(g).ToArray(), Is.EqualTo(forward.GroupSlotIndices(g).ToArray()));

                Assert.That(forward.GetGroupKey(g), g == 0 ? Is.GreaterThan(0L) : Is.GreaterThan(forward.GetGroupKey(g - 1)),
                    "group keys are strictly ascending.");
                ReadOnlySpan<Entity> entities = forward.GroupEntities(g);
                ReadOnlySpan<int> slots = forward.GroupSlotIndices(g);
                for (int m = 1; m < entities.Length; m++)
                {
                    bool ordered = entities[m].Id > entities[m - 1].Id ||
                                   (entities[m].Id == entities[m - 1].Id && slots[m] > slots[m - 1]);
                    Assert.That(ordered, Is.True, "members are ordered by entity id asc then slot asc.");
                }
            }
        }

        [Test]
        public void Install_UnknownGroupByPrefix_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Assert.Throws<InvalidOperationException>(() => harness.Registry.Install(Harness.Config(
                new AbilityAggregationProfileDefinition { Id = "aggregation.bad.prefix", GroupBy = "bogus.field" })));
            Assert.Throws<InvalidOperationException>(() => harness.Registry.Install(Harness.Config(
                new AbilityAggregationProfileDefinition { Id = "aggregation.bad.empty", GroupBy = "catalog." })));
            Assert.Throws<InvalidOperationException>(() => harness.Registry.Install(Harness.Config(
                new AbilityAggregationProfileDefinition { Id = "aggregation.bad.field", GroupBy = "ability.name" })));
        }

        [Test]
        public void BuildGroups_SteadyState_IsAllocationFree()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Selection selection = harness.CreateM6Selection();
            int profileId = harness.ProfileId(ByFamilyProfileId);
            Entity[] members = selection.Members.ToArray();

            var result = new AbilityAggregationResult();
            int warmup = harness.Registry.BuildGroups(profileId, members, world, harness.Abilities, ref result);
            Assert.That(warmup, Is.EqualTo(3));

            long allocated = MeasureBuildGroupsAllocations(harness, profileId, members, world, ref result);
            allocated = Math.Min(allocated, MeasureBuildGroupsAllocations(harness, profileId, members, world, ref result));
            Assert.That(allocated, Is.EqualTo(0), "Steady-state BuildGroups must be allocation free.");
        }

        [Test]
        public void DefaultConfigFile_ValidatesAndInstalls()
        {
            string path = Path.Combine(FindRepoRoot(), "assets", "Configs", "UI", "ability_aggregation_profiles.json");
            Assert.That(File.Exists(path), Is.True, $"Missing {path}");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true };
            var config = JsonSerializer.Deserialize<AbilityAggregationProfilesConfig>(File.ReadAllText(path), options);
            AbilityAggregationProfileConfigLoader.Validate(config, path);

            TagRegistry.Register(StimFamilyTag);
            var registry = new AbilityAggregationProfileRegistry(
                new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            registry.Install(config);

            Assert.That(registry.IsInstalled(registry.ProfileIdRegistry.GetId(ByFamilyProfileId)), Is.True);
            Assert.That(registry.GetOverflow(registry.ProfileIdRegistry.GetId(ByFamilyProfileId)), Is.Not.Empty);
            Assert.That(registry.IsInstalled(registry.ProfileIdRegistry.GetId(ByTemplateProfileId)), Is.True);
            Assert.That(registry.IsInstalled(registry.ProfileIdRegistry.GetId(ByAbilityIdProfileId)), Is.True);
        }

        private static long MeasureBuildGroupsAllocations(
            Harness harness,
            int profileId,
            Entity[] members,
            World world,
            ref AbilityAggregationResult result)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Registry.BuildGroups(profileId, members, world, harness.Abilities, ref result);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static void AssertGroup(AbilityAggregationResult result, int groupIndex, long expectedKey, int expectedMembers)
        {
            Assert.That(result.GetGroupKey(groupIndex), Is.EqualTo(expectedKey));
            Assert.That(result.GroupEntities(groupIndex).Length, Is.EqualTo(expectedMembers));
        }

        private static long CatalogKey(string tagName)
        {
            return AbilityAggregationKeyKinds.MakeKey(AbilityAggregationKeyKinds.CatalogTag, TagRegistry.GetId(tagName));
        }

        private static long IdentityKey(int abilityId)
        {
            return AbilityAggregationKeyKinds.MakeKey(AbilityAggregationKeyKinds.AbilityId, abilityId);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "assets")) &&
                    File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing assets/ and src/Core/Ludots.Core.csproj");
        }

        private readonly struct Selection
        {
            public Selection(Entity marine1, Entity marine2, Entity elite, Entity tank1, Entity tank2, Entity[] members)
            {
                Marine1 = marine1;
                Marine2 = marine2;
                Elite = elite;
                Tank1 = tank1;
                Tank2 = tank2;
                _members = members;
            }

            public Entity Marine1 { get; }
            public Entity Marine2 { get; }
            public Entity Elite { get; }
            public Entity Tank1 { get; }
            public Entity Tank2 { get; }
            private readonly Entity[] _members;
            public ReadOnlySpan<Entity> Members => _members;
        }

        private sealed class Harness
        {
            public World World = null!;
            public AbilityDefinitionRegistry Abilities = null!;
            public AbilityAggregationProfileRegistry Registry = null!;
            public StringIntRegistry ProfileIds = null!;

            public static Harness Create(World world)
            {
                // Tag ids: stim family < charge family (registration order fixes the group order).
                TagRegistry.Register(StimFamilyTag);
                TagRegistry.Register(ChargeFamilyTag);

                var abilities = new AbilityDefinitionRegistry();
                RegisterAbility(abilities, StimAbilityId, StimFamilyTag);
                RegisterAbility(abilities, TankChargeAbilityId, ChargeFamilyTag);
                RegisterAbility(abilities, EliteChargeAbilityId, ChargeFamilyTag);
                RegisterAbility(abilities, FormVariantAbilityId, catalogTag: null);
                RegisterAbility(abilities, AttackAbilityId, catalogTag: null);

                var profileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var registry = new AbilityAggregationProfileRegistry(profileIds);
                registry.Install(Config(
                    new AbilityAggregationProfileDefinition
                    {
                        Id = ByFamilyProfileId,
                        GroupBy = "catalog." + FamilyTagPrefix,
                        Overflow = "nextPanelSlot",
                    },
                    new AbilityAggregationProfileDefinition { Id = ByTemplateProfileId, GroupBy = "template.id" },
                    new AbilityAggregationProfileDefinition { Id = ByAbilityIdProfileId, GroupBy = "ability.id" }));

                return new Harness
                {
                    World = world,
                    Abilities = abilities,
                    Registry = registry,
                    ProfileIds = profileIds,
                };
            }

            public int ProfileId(string name) => ProfileIds.GetId(name);

            /// <summary>M6 background: [marine1, marine2, eliteMarine, tank1, tank2].</summary>
            public Selection CreateM6Selection()
            {
                Entity marine1 = CreateActor(AttackAbilityId, StimAbilityId);
                Entity marine2 = CreateActor(AttackAbilityId, StimAbilityId);
                // Elite marine slot layout = [0: attack, 1: stim, 2: charge cannon] (slots are panel-agnostic).
                Entity elite = CreateActor(AttackAbilityId, StimAbilityId, EliteChargeAbilityId);
                World.Add(elite, new AbilityFormSlotBuffer());
                Entity tank1 = CreateActor(AttackAbilityId, TankChargeAbilityId);
                Entity tank2 = CreateActor(AttackAbilityId, TankChargeAbilityId);
                return new Selection(marine1, marine2, elite, tank1, tank2, new[] { marine1, marine2, elite, tank1, tank2 });
            }

            public static AbilityAggregationProfilesConfig Config(params AbilityAggregationProfileDefinition[] profiles)
            {
                return new AbilityAggregationProfilesConfig { Profiles = new List<AbilityAggregationProfileDefinition>(profiles) };
            }

            private Entity CreateActor(params int[] abilityIds)
            {
                Entity actor = World.Create(new AbilityStateBuffer());
                ref AbilityStateBuffer slots = ref World.Get<AbilityStateBuffer>(actor);
                for (int i = 0; i < abilityIds.Length; i++)
                {
                    slots.AddAbility(abilityIds[i]);
                }

                return actor;
            }

            private static void RegisterAbility(AbilityDefinitionRegistry registry, int abilityId, string catalogTag)
            {
                var def = new AbilityDefinition();
                if (catalogTag != null)
                {
                    def.HasCatalogTags = true;
                    def.CatalogTags.AddTag(TagRegistry.Register(catalogTag));
                }

                registry.Register(abilityId, in def);
            }
        }
    }
}
