using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Arch.Core;
using EntityCommandPanelMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.Core.Registry;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 PNL-4 - <c>CollectionGasEntityCommandPanelSource</c> consumes the
    /// <see cref="AbilityAggregationProfileRegistry"/> kernel. Covers the M6 catalog cases
    /// (mixed marine/tank selection grouped by cast family, FormSet override recompute) and P3
    /// (runtime profile switch regroups without re-selection), plus activation fan-out to every
    /// surviving group member. All family/ability names are test data, never Core concepts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CollectionGasEntityCommandPanelAggregationTests
    {
        private const string CollectionSourceId = "gas.collection-ability-slots";
        private const string AnyQueryId = "tests.command.aggregation-any";

        private const string StimFamilyCategory = "castFamily.stimpack";
        private const string ChargeFamilyCategory = "castFamily.charge_shot";

        private const int StimAbilityId = 101;
        private const int TankChargeAbilityId = 201;
        private const int EliteChargeAbilityId = 202;
        private const int FormVariantAbilityId = 203;
        private const int StrikeAbilityId = 301;
        private const int MarineTemplateKeyId = 11;
        private const int EliteTemplateKeyId = 12;
        private const int TankTemplateKeyId = 21;

        private const string ByTemplateProfileId = "aggregation.by_template";
        private const string ByFamilyProfileId = "aggregation.tests.by_family";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
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
            Assert.That(slots[1].DisplayLabel, Is.EqualTo("Charge shot"),
                "family profile display names come from the configured catalog tag, not from a representative ability.");
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
            var context = new EntityCommandPanelSourceContext(fixture.CollectionOwner, CollectionSourceId, AnyQueryId);
            var slots = new EntityCommandPanelSlotView[8];

            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(3));
            Assert.That(EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint byFamilyRevision), Is.True);

            SetAggregationProfile(source, ByTemplateProfileId);

            Assert.That(EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint byTemplateRevision), Is.True);
            Assert.That(byTemplateRevision, Is.Not.EqualTo(byFamilyRevision),
                "P3: profile switch bumps the revision so open panels re-pull without re-selection.");

            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);
            Assert.That(copied, Is.EqualTo(7), "by_template preserves unit-template command rows and slot positions.");
            Assert.That(CountAbility(slots, copied, StrikeAbilityId), Is.EqualTo(3));
            Assert.That(CountAbility(slots, copied, StimAbilityId), Is.EqualTo(2));
            Assert.That(CountAbility(slots, copied, TankChargeAbilityId), Is.EqualTo(1));
            Assert.That(CountAbility(slots, copied, EliteChargeAbilityId), Is.EqualTo(1));
            Assert.That(FirstDetail(slots, copied, StrikeAbilityId), Does.StartWith("2 owners | "));
            Assert.That(FirstDetail(slots, copied, EliteChargeAbilityId), Does.Not.Contain("owners |"),
                "single-member unit-template cell keeps the plain detail.");

            SetAggregationProfile(source, ByFamilyProfileId);
            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(3),
                "switching back restores the family grouping on the next build.");

            Assert.Throws<InvalidOperationException>(
                () => SetAggregationProfile(source, "aggregation.not_installed"),
                "unknown profiles fail fast instead of silently keeping the old grouping.");
        }

        [Test]
        public void ActivateSlot_SubmitsEveryGroupMemberSlot()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            var fixture = SelectionFixture.Create(engine);

            var submitted = new List<Order>();
            InputOrderMappingSystem mapping = CreateMappingSystem(submitted);
            mapping.SetSolePossessedActor(fixture.CollectionOwner, 7);
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

            InputOrderActivationResult activated = EntityCommandPanelSourceDispatch.ActivateSlot(source, in context, 0, 1);

            Assert.That(activated.State, Is.EqualTo(InputOrderActivationState.Submitted));
            Assert.That(activated.Actor, Is.EqualTo(fixture.Elite));
            Assert.That(activated.OrderId, Is.GreaterThan(0));
            Assert.That(submitted.Count, Is.EqualTo(3));
            Assert.That(submitted[0].Args.I0, Is.EqualTo(2),
                "activation starts from the representative member (elite marine, charge cannon on slot 2).");
            Assert.That(submitted[0].Actor, Is.EqualTo(fixture.Elite));
            Assert.That(submitted[1].Args.I0, Is.EqualTo(1));
            Assert.That(submitted[1].Actor, Is.EqualTo(fixture.Tank1));
            Assert.That(submitted[2].Args.I0, Is.EqualTo(1));
            Assert.That(submitted[2].Actor, Is.EqualTo(fixture.Tank2));
        }

        [Test]
        public void ActivateSlot_MultiMemberSmartCast_SubmitsEveryMember()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            var fixture = SelectionFixture.Create(engine);

            var submitted = new List<Order>();
            InputOrderMappingSystem mapping = CreateMappingSystem(submitted, CastModeType.SmartCast);
            mapping.SetSolePossessedActor(fixture.CollectionOwner, 7);
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
            Assert.That(slots[1].AbilityId, Is.EqualTo(EliteChargeAbilityId));

            InputOrderActivationResult activated = EntityCommandPanelSourceDispatch.ActivateSlot(source, in context, 0, 1);

            Assert.That(activated.State, Is.EqualTo(InputOrderActivationState.Submitted));
            Assert.That(submitted.Count, Is.EqualTo(3));
            Assert.That(mapping.IsAiming, Is.False);
        }

        [Test]
        public void ActivateSlot_MemberFailureReturnsTypedRejectionWithoutDroppingOtherMembers()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            var fixture = SelectionFixture.Create(engine);

            var submitted = new List<Order>();
            InputOrderMappingSystem mapping = CreateMappingSystem(submitted);
            mapping.SetSolePossessedActor(fixture.CollectionOwner, 7);
            mapping.SetActorProvider((out Entity actor) =>
            {
                actor = fixture.CollectionOwner;
                return true;
            });
            mapping.SetOrderSubmitHandler((in Order order) =>
            {
                submitted.Add(order);
                return order.Actor == fixture.Tank1
                    ? OrderSubmitResult.RejectedQueueFull
                    : OrderSubmitResult.Queued;
            });
            engine.SetService(CoreServiceKeys.ActiveInputOrderMapping, mapping);

            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(fixture.CollectionOwner, CollectionSourceId, AnyQueryId);
            var slots = new EntityCommandPanelSlotView[8];
            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(3));
            Assert.That(slots[1].AbilityId, Is.EqualTo(EliteChargeAbilityId));

            InputOrderActivationResult activated = EntityCommandPanelSourceDispatch.ActivateSlot(source, in context, 0, 1);

            Assert.That(activated.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(activated.Actor, Is.EqualTo(fixture.Tank1));
            Assert.That(activated.Rejection, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(activated.OrderId, Is.GreaterThan(0));
            Assert.That(submitted.Count, Is.EqualTo(3),
                "one member failing must not silently collapse the aggregate command back to the representative unit.");
        }

        [Test]
        public void ActivateSlot_MultiMemberAiming_ReturnsTypedRejectionWithoutOpeningSingleActorAiming()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            var fixture = SelectionFixture.Create(engine);

            var submitted = new List<Order>();
            InputOrderMappingSystem mapping = CreateMappingSystem(submitted, CastModeType.AimCast);
            mapping.SetSolePossessedActor(fixture.CollectionOwner, 7);
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
            Assert.That(slots[1].AbilityId, Is.EqualTo(EliteChargeAbilityId));

            InputOrderActivationResult activated = EntityCommandPanelSourceDispatch.ActivateSlot(source, in context, 0, 1);

            Assert.That(activated.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(activated.Rejection, Is.EqualTo(OrderSubmitResult.RejectedByRule));
            Assert.That(submitted.Count, Is.Zero);
            Assert.That(mapping.IsAiming, Is.False);
            Assert.That(mapping.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(mapping.LastActivationResult.Actor, Is.EqualTo(activated.Actor));
            Assert.That(mapping.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedByRule));
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
            public SelectionFixture(Entity collectionOwner, Entity elite, Entity tank1, Entity tank2)
            {
                CollectionOwner = collectionOwner;
                Elite = elite;
                Tank1 = tank1;
                Tank2 = tank2;
            }

            public Entity CollectionOwner { get; }
            public Entity Elite { get; }
            public Entity Tank1 { get; }
            public Entity Tank2 { get; }

            public static SelectionFixture Create(GameEngine engine)
            {
                RegisterAbility(engine, StrikeAbilityId, "Strike", "Strike detail", categoryName: null);
                RegisterAbility(engine, StimAbilityId, "Stimpack", "Stim detail", StimFamilyCategory);
                RegisterAbility(engine, TankChargeAbilityId, "Tank Charge", "Tank charge detail", ChargeFamilyCategory);
                RegisterAbility(engine, EliteChargeAbilityId, "Elite Charge", "Elite charge detail", ChargeFamilyCategory);
                RegisterAbility(engine, FormVariantAbilityId, "Form Variant", "Form variant detail", categoryName: null);

                Entity marine1 = CreateActor(engine.World, "Marine 1", MarineTemplateKeyId, StrikeAbilityId, StimAbilityId);
                Entity marine2 = CreateActor(engine.World, "Marine 2", MarineTemplateKeyId, StrikeAbilityId, StimAbilityId);
                // Elite marine slot layout = [0: strike, 1: stim, 2: charge cannon] (slots are panel-agnostic).
                Entity elite = CreateActor(engine.World, "Elite Marine", EliteTemplateKeyId, StrikeAbilityId, StimAbilityId, EliteChargeAbilityId);
                engine.World.Add(elite, new AbilityFormSlotBuffer());
                Entity tank1 = CreateActor(engine.World, "Tank 1", TankTemplateKeyId, StrikeAbilityId, TankChargeAbilityId);
                Entity tank2 = CreateActor(engine.World, "Tank 2", TankTemplateKeyId, StrikeAbilityId, TankChargeAbilityId);

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

                return new SelectionFixture(collectionOwner, elite, tank1, tank2);
            }
        }

        private static int CountAbility(EntityCommandPanelSlotView[] slots, int count, int abilityId)
        {
            int result = 0;
            for (int i = 0; i < count; i++)
            {
                if (slots[i].AbilityId == abilityId)
                {
                    result++;
                }
            }

            return result;
        }

        private static string FirstDetail(EntityCommandPanelSlotView[] slots, int count, int abilityId)
        {
            for (int i = 0; i < count; i++)
            {
                if (slots[i].AbilityId == abilityId)
                {
                    return slots[i].DetailLabel;
                }
            }

            Assert.Fail($"Ability {abilityId} was not copied.");
            return string.Empty;
        }

        private static GameEngine CreateEngineWithCommandPanelMod()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            // EntityCommandPanelMod loads through the real ModLoader so its
            // assets/UI/ability_aggregation_profiles.json fragment (aggregation.by_family)
            // merges additively into the Core structural profiles at engine init (ArrayById).
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "EntityCommandPanelMod" }),
                Path.Combine(repoRoot, "assets"));
            InstallUiServices(engine);
            InstallTestAggregationProfiles(engine);
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

        private static void InstallTestAggregationProfiles(GameEngine engine)
        {
            AbilityCategoryRegistry.Register(StimFamilyCategory);
            AbilityCategoryRegistry.Register(ChargeFamilyCategory);

            var profileIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var registry = new AbilityAggregationProfileRegistry(profileIds);
            registry.Install(new AbilityAggregationProfilesConfig
            {
                Profiles =
                {
                    new AbilityAggregationProfileDefinition
                    {
                        Id = ByFamilyProfileId,
                        GroupBy = "catalog.castFamily",
                        Overflow = "nextPanelSlot",
                    },
                    new AbilityAggregationProfileDefinition
                    {
                        Id = ByTemplateProfileId,
                        GroupBy = "template.id",
                        Overflow = "nextPanelSlot",
                    },
                }
            });

            engine.SetService(CoreServiceKeys.AbilityAggregationProfileRegistry, registry);
        }

        private static void RegisterAbility(GameEngine engine, int abilityId, string label, string detail, string? categoryName)
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
            if (categoryName != null)
            {
                definition.HasCategories = true;
                definition.Categories.AddTag(AbilityCategoryRegistry.Register(categoryName));
            }

            registry.Register(abilityId, in definition, "CollectionGasEntityCommandPanelAggregationTests");
        }

        private static Entity CreateActor(World world, string name, int templateKeyId, params int[] abilityIds)
        {
            Entity actor = world.Create(
                new Name { Value = name },
                new EntityTemplateKeyRef { TemplateKeyId = templateKeyId },
                new AbilityStateBuffer());
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
            SetAggregationProfile(source, ByFamilyProfileId);
            return source;
        }

        private static void SetAggregationProfile(IEntityCommandPanelSource source, string profileId)
        {
            MethodInfo method = source.GetType().GetMethod("SetAggregationProfile", new[] { typeof(string) })
                ?? throw new InvalidOperationException("Collection command panel source must expose SetAggregationProfile.");
            try
            {
                method.Invoke(source, new object[] { profileId });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static InputOrderMappingSystem CreateMappingSystem(
            List<Order> submitted,
            CastModeType interactionMode = CastModeType.TargetFirst)
        {
            var mapping = new InputOrderMappingSystem(new FrozenInputActionReader(), new InputOrderMappingConfig
            {
                InteractionMode = interactionMode,
                Mappings = new List<InputOrderMapping>
                {
                    CreateSkillMapping("SkillQ", 0),
                    CreateSkillMapping("SkillW", 1),
                    CreateSkillMapping("SkillE", 2)
                }
            });
            mapping.SetOrderTypeKeyResolver(key => string.Equals(key, "castAbility", StringComparison.Ordinal) ? 100 : 0);
            mapping.SetActivationActorValidator((actor, _) => actor != Entity.Null);
            int nextOrderId = 1;
            mapping.SetOrderIdentityAssigner((ref Order order) => order.OrderId = nextOrderId++);
            mapping.SetOrderSubmitHandler((in Order order) => { submitted.Add(order); return OrderSubmitResult.Queued; });
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
                RequireTarget = false,
                TargetType = OrderTargetType.None,
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
