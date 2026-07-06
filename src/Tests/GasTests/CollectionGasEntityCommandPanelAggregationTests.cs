using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using EntityCommandPanelMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 PNL-4 — <c>CollectionGasEntityCommandPanelSource</c> consumes the
    /// <see cref="AbilityAggregationProfileRegistry"/> kernel. Covers the §6.1 M6 catalog cases
    /// (mixed marine/tank selection grouped by cast family, FormSet override recompute) and P3
    /// (runtime profile switch regroups without re-selection), plus activation routing to the
    /// group's first member. All family/ability names are test data, never Core concepts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CollectionGasEntityCommandPanelAggregationTests
    {
        private const string CollectionSourceId = "gas.collection-ability-slots";
        private const string AnyQueryId = "tests.command.aggregation-any";

        private const string StimFamilyTag = "castFamily.stimpack";
        private const string ChargeFamilyTag = "castFamily.charge_shot";

        private const int StimAbilityId = 101;
        private const int TankChargeAbilityId = 201;
        private const int EliteChargeAbilityId = 202;
        private const int FormVariantAbilityId = 203;
        private const int StrikeAbilityId = 301;

        private const string ByTemplateProfileId = "aggregation.by_template";
        private const string ByFamilyProfileId = "aggregation.by_family";

        [SetUp]
        public void SetUp()
        {
            // Family tags must exist before engine init so the by_family profile's catalog mask
            // (compiled at profile install) includes them; stim registers first => lower tag id.
            TagRegistry.Clear();
            TagRegistry.Register(StimFamilyTag);
            TagRegistry.Register(ChargeFamilyTag);
        }

        [TearDown]
        public void TearDown()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void DefaultByFamilyProfile_MixedSelection_OneCellPerFamilyGroup()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            var fixture = SelectionFixture.Create(engine);

            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(fixture.CollectionOwner, CollectionSourceId, AnyQueryId);
            var slots = new EntityCommandPanelSlotView[8];

            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);

            Assert.That(copied, Is.EqualTo(3), "by_family: stim family + charge family + untagged strike.");

            Assert.That(slots[0].AbilityId, Is.EqualTo(StimAbilityId));
            Assert.That(slots[0].DisplayLabel, Is.EqualTo("Stimpack"));
            Assert.That(slots[0].DetailLabel, Does.StartWith("3 owners | "), "badge count = group member count.");

            Assert.That(slots[1].AbilityId, Is.EqualTo(EliteChargeAbilityId),
                "charge family cell represents its first member (elite marine, lowest entity id).");
            Assert.That(slots[1].DisplayLabel, Is.EqualTo("Elite Charge"));
            Assert.That(slots[1].DetailLabel, Does.StartWith("3 owners | "), "elite + both tanks in one family cell.");

            Assert.That(slots[2].AbilityId, Is.EqualTo(StrikeAbilityId), "untagged ability falls back to its own group.");
            Assert.That(slots[2].DetailLabel, Does.StartWith("5 owners | "));

            Assert.That(EntityCommandPanelSourceDispatch.TryGetGroup(source, in context, 0, out EntityCommandPanelGroupView group), Is.True);
            Assert.That(group.SlotCount, Is.EqualTo(3));
        }

        [Test]
        public void SetAggregationProfile_SwitchRegroupsWithoutReselection_AndBumpsRevision()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            var fixture = SelectionFixture.Create(engine);

            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var collectionSource = (CollectionGasEntityCommandPanelSource)source;
            var context = new EntityCommandPanelSourceContext(fixture.CollectionOwner, CollectionSourceId, AnyQueryId);
            var slots = new EntityCommandPanelSlotView[8];

            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(3));
            Assert.That(EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint byFamilyRevision), Is.True);

            collectionSource.SetAggregationProfile(ByTemplateProfileId);

            Assert.That(EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint byTemplateRevision), Is.True);
            Assert.That(byTemplateRevision, Is.Not.EqualTo(byFamilyRevision),
                "P3: profile switch bumps the revision so open panels re-pull without re-selection.");

            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);
            Assert.That(copied, Is.EqualTo(4), "by_template splits the two charge-cannon templates.");
            Assert.That(slots[0].AbilityId, Is.EqualTo(StimAbilityId));
            Assert.That(slots[0].DetailLabel, Does.StartWith("3 owners | "));
            Assert.That(slots[1].AbilityId, Is.EqualTo(TankChargeAbilityId));
            Assert.That(slots[1].DetailLabel, Does.StartWith("2 owners | "));
            Assert.That(slots[2].AbilityId, Is.EqualTo(EliteChargeAbilityId));
            Assert.That(slots[2].DetailLabel, Does.Not.Contain("owners |"), "single-member cell keeps the plain detail.");
            Assert.That(slots[3].AbilityId, Is.EqualTo(StrikeAbilityId));
            Assert.That(slots[3].DetailLabel, Does.StartWith("5 owners | "));

            collectionSource.SetAggregationProfile(ByFamilyProfileId);
            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(3),
                "switching back restores the family grouping on the next build.");

            Assert.Throws<InvalidOperationException>(
                () => collectionSource.SetAggregationProfile("aggregation.not_installed"),
                "unknown profiles fail fast instead of silently keeping the old grouping.");
        }

        [Test]
        public void ActivateSlot_RoutesToGroupFirstMemberSlot()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            var fixture = SelectionFixture.Create(engine);

            var submitted = new List<Order>();
            InputOrderMappingSystem mapping = CreateMappingSystem(submitted);
            mapping.SetLocalPlayer(fixture.CollectionOwner, 7);
            mapping.SetActorProvider((out Entity actor) =>
            {
                actor = fixture.CollectionOwner;
                return true;
            });
            engine.SetService(CoreServiceKeys.ActiveInputOrderMapping, mapping);

            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(fixture.CollectionOwner, CollectionSourceId, AnyQueryId);
            var slots = new EntityCommandPanelSlotView[8];
            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(3));
            Assert.That(slots[1].AbilityId, Is.EqualTo(EliteChargeAbilityId), "displayed cell 1 is the charge family group.");

            bool activated = EntityCommandPanelSourceDispatch.ActivateSlot(source, in context, 0, 1);

            Assert.That(activated, Is.True);
            Assert.That(submitted.Count, Is.EqualTo(1));
            Assert.That(submitted[0].Args.I0, Is.EqualTo(2),
                "activation routes to the group's first member (elite marine, charge cannon on slot 2), not a tank's slot 1.");
        }

        [Test]
        public void FormSlotOverride_RecomputesGroupsAndKeepsProfilePreference()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            var fixture = SelectionFixture.Create(engine);

            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(fixture.CollectionOwner, CollectionSourceId, AnyQueryId);
            var slots = new EntityCommandPanelSlotView[8];

            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(3));
            Assert.That(slots[1].DetailLabel, Does.StartWith("3 owners | "), "pre-override: charge family has 3 members.");
            Assert.That(EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint beforeRevision), Is.True);

            // Form switch remaps the elite marine's slot 2 (charge cannon -> untagged form variant).
            ref var formSlots = ref engine.World.Get<AbilityFormSlotBuffer>(fixture.Elite);
            formSlots.SetOverride(2, FormVariantAbilityId);

            Assert.That(EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint afterRevision), Is.True);
            Assert.That(afterRevision, Is.Not.EqualTo(beforeRevision), "form override invalidates the panel revision.");

            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);
            Assert.That(copied, Is.EqualTo(4), "aggregation preference survives the form switch: still grouped by family.");
            Assert.That(slots[1].AbilityId, Is.EqualTo(TankChargeAbilityId),
                "charge family now only contains the tanks (kernel groups over AbilitySlotResolver results).");
            Assert.That(slots[1].DetailLabel, Does.StartWith("2 owners | "));
            Assert.That(slots[2].AbilityId, Is.EqualTo(FormVariantAbilityId));
            Assert.That(slots[2].DetailLabel, Does.Not.Contain("owners |"));

            formSlots.Clear(2);
            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(3),
                "clearing the override restores the original family grouping.");
        }

        /// <summary>M6 background selection: [marine1, marine2, eliteMarine, tank1, tank2].</summary>
        private readonly struct SelectionFixture
        {
            public SelectionFixture(Entity collectionOwner, Entity elite)
            {
                CollectionOwner = collectionOwner;
                Elite = elite;
            }

            public Entity CollectionOwner { get; }
            public Entity Elite { get; }

            public static SelectionFixture Create(GameEngine engine)
            {
                RegisterAbility(engine, StrikeAbilityId, "Strike", "Strike detail", catalogTag: null);
                RegisterAbility(engine, StimAbilityId, "Stimpack", "Stim detail", StimFamilyTag);
                RegisterAbility(engine, TankChargeAbilityId, "Tank Charge", "Tank charge detail", ChargeFamilyTag);
                RegisterAbility(engine, EliteChargeAbilityId, "Elite Charge", "Elite charge detail", ChargeFamilyTag);
                RegisterAbility(engine, FormVariantAbilityId, "Form Variant", "Form variant detail", catalogTag: null);

                Entity marine1 = CreateActor(engine.World, "Marine 1", StrikeAbilityId, StimAbilityId);
                Entity marine2 = CreateActor(engine.World, "Marine 2", StrikeAbilityId, StimAbilityId);
                // Elite marine slot layout = [0: strike, 1: stim, 2: charge cannon] (slots are panel-agnostic).
                Entity elite = CreateActor(engine.World, "Elite Marine", StrikeAbilityId, StimAbilityId, EliteChargeAbilityId);
                engine.World.Add(elite, new AbilityFormSlotBuffer());
                Entity tank1 = CreateActor(engine.World, "Tank 1", StrikeAbilityId, TankChargeAbilityId);
                Entity tank2 = CreateActor(engine.World, "Tank 2", StrikeAbilityId, TankChargeAbilityId);

                Entity collectionOwner = engine.World.Create(new Name { Value = "Aggregation Collection Owner" });
                ReplaceCommandCollection(engine, collectionOwner, new[] { marine1, marine2, elite, tank1, tank2 });
                RegisterQuery(engine, new EntityCommandPanelCollectionQueryConfig
                {
                    Id = AnyQueryId,
                    CollectionKey = EntityCollectionKeys.CommandSource,
                    Title = "Aggregation",
                    Filter = EntityCommandPanelCollectionFilter.Any,
                    Sort = EntityCommandPanelCollectionSortKind.SlotThenOwnerCountThenLabel
                });

                return new SelectionFixture(collectionOwner, elite);
            }
        }

        private static GameEngine CreateEngineWithCommandPanelMod()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            // EntityCommandPanelMod loads through the real ModLoader so its
            // assets/Configs/UI/ability_aggregation_profiles.json fragment (aggregation.by_family)
            // merges additively into the Core structural profiles at engine init (ArrayById).
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "EntityCommandPanelMod" }),
                Path.Combine(repoRoot, "assets"));
            InstallUiServices(engine);
            engine.TriggerManager.FireEvent(GameEvents.GameStart, engine.CreateContext());
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            return engine;
        }

        private static void InstallUiServices(GameEngine engine)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        }

        private static void RegisterAbility(GameEngine engine, int abilityId, string label, string detail, string? catalogTag)
        {
            var registry = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
                ?? throw new InvalidOperationException("AbilityDefinitionRegistry missing.");
            var definition = new AbilityDefinition
            {
                HasPresentation = true,
                Presentation = new AbilityPresentationConfig
                {
                    DisplayName = label,
                    HintText = detail
                }
            };
            if (catalogTag != null)
            {
                definition.HasCatalogTags = true;
                definition.CatalogTags.AddTag(TagRegistry.Register(catalogTag));
            }

            registry.Register(abilityId, in definition, "CollectionGasEntityCommandPanelAggregationTests");
        }

        private static Entity CreateActor(World world, string name, params int[] abilityIds)
        {
            Entity actor = world.Create(new Name { Value = name }, new AbilityStateBuffer());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            for (int i = 0; i < abilityIds.Length; i++)
            {
                abilities.AddAbility(abilityIds[i]);
            }

            return actor;
        }

        private static void ReplaceCommandCollection(GameEngine engine, Entity owner, Entity[] entities)
        {
            var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            store.Replace(
                owner,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.CommandSource,
                    EntityCollectionSourceKind.Explicit,
                    EntityCollectionRoleKind.CommandSource,
                    owner,
                    entities.Length == 0 ? Entity.Null : entities[0],
                    "Command Source",
                    $"{entities.Length} owners"),
                entities);
        }

        private static void RegisterQuery(GameEngine engine, EntityCommandPanelCollectionQueryConfig config)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelCollectionQueryConfigRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelCollectionQueryConfigRegistry missing.");
            registry.Register(config);
        }

        private static IEntityCommandPanelSource ResolveCollectionSource(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry missing.");
            Assert.That(registry.TryGet(CollectionSourceId, out IEntityCommandPanelSource source), Is.True);
            return source;
        }

        private static InputOrderMappingSystem CreateMappingSystem(List<Order> submitted)
        {
            var mapping = new InputOrderMappingSystem(new FrozenInputActionReader(), new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    CreateSkillMapping("SkillQ", 0),
                    CreateSkillMapping("SkillW", 1),
                    CreateSkillMapping("SkillE", 2)
                }
            });
            mapping.SetOrderTypeKeyResolver(key => string.Equals(key, "castAbility", StringComparison.Ordinal) ? 100 : 0);
            mapping.SetOrderSubmitHandler((in Order order) => submitted.Add(order));
            return mapping;
        }

        private static InputOrderMapping CreateSkillMapping(string actionId, int slotIndex)
        {
            return new InputOrderMapping
            {
                ActionId = actionId,
                Trigger = InputTriggerType.PressedThisFrame,
                OrderTypeKey = "castAbility",
                ArgsTemplate = new OrderArgsTemplate { I0 = slotIndex },
                RequireSelection = false,
                SelectionType = OrderSelectionType.None,
                IsSkillMapping = true
            };
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "README.md")) && Directory.Exists(Path.Combine(dir, "mods")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
