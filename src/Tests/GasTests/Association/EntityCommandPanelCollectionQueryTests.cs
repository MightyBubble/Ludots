using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
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
    [TestFixture]
    [NonParallelizable]
    public sealed class EntityCommandPanelCollectionQueryTests
    {
        private const string CollectionSourceId = "gas.collection-ability-slots";
        private const string SharedActionQueryId = "tests.command.shared-action";
        private const string OwnerCountQueryId = "tests.command.owner-count";

        [Test]
        public void ModInstall_RegistersCollectionCommandSourceAndDefaultQueryConfig()
        {
            using var engine = CreateEngineWithCommandPanelMod();

            var sources = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry missing.");
            var queries = engine.GetService(CoreServiceKeys.EntityCommandPanelCollectionQueryConfigRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelCollectionQueryConfigRegistry missing.");

            Assert.That(sources.TryGet(CollectionSourceId, out _), Is.True);
            Assert.That(queries.TryGet(EntityCollectionKeys.CommandSource, out EntityCommandPanelCollectionQueryConfig config), Is.True);
            Assert.That(config.Id, Is.EqualTo(EntityCollectionKeys.CommandSource));
            Assert.That(config.CollectionKey, Is.EqualTo(EntityCollectionKeys.CommandSource));
            Assert.That(config.Sort, Is.EqualTo(EntityCommandPanelCollectionSortKind.SlotThenOwnerCountThenLabel));
        }

        [Test]
        public void CopySlots_UnregisteredQueryId_FailsExplicitly()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            Entity owner = engine.World.Create();
            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(owner, CollectionSourceId, "tests.command.missing-query");

            var slots = new EntityCommandPanelSlotView[8];

            var ex = Assert.Throws<InvalidOperationException>(
                () => EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots));
            Assert.That(ex!.Message, Does.Contain("tests.command.missing-query"));
        }

        [Test]
        public void CopySlots_ConfiguredCollectionQuery_AggregatesFiltersAndSortsDeterministically()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            RegisterAbility(engine, 1001, "Arc Bolt", "Q detail");
            RegisterAbility(engine, 1002, "Ward", "W detail");
            RegisterAbility(engine, 1003, "Pulse", "E detail");

            Entity collectionOwner = engine.World.Create(new Name { Value = "Command Collection Owner" });
            Entity first = CreateActor(engine.World, "Alpha", 1001, 1002);
            Entity second = CreateActor(engine.World, "Beta", 1001, 1003);

            ReplaceCommandCollection(engine, collectionOwner, new[] { first, second });
            RegisterQuery(engine, new EntityCommandPanelCollectionQueryConfig
            {
                Id = SharedActionQueryId,
                CollectionKey = EntityCollectionKeys.CommandSource,
                Title = "Shared Q",
                Filter = new EntityCommandPanelCollectionFilter(
                    EntityCommandPanelCollectionFilterKind.ActionId,
                    ActionId: "SkillQ"),
                Sort = EntityCommandPanelCollectionSortKind.OwnerCountThenSlotThenLabel
            });

            engine.SetService(CoreServiceKeys.ActiveInputOrderMapping, CreateMappingSystem(new List<Order>()));
            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(collectionOwner, CollectionSourceId, SharedActionQueryId);
            var slots = new EntityCommandPanelSlotView[8];

            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);

            Assert.That(copied, Is.EqualTo(1));
            Assert.That(slots[0].SlotIndex, Is.EqualTo(0), "Displayed slot index should be dense after aggregation.");
            Assert.That(slots[0].AbilityId, Is.EqualTo(1001));
            Assert.That(slots[0].ActionId, Is.EqualTo("SkillQ"));
            Assert.That(slots[0].DisplayLabel, Is.EqualTo("Arc Bolt"));
            Assert.That(slots[0].DetailLabel, Is.EqualTo("2 owners | Q detail"));

            RegisterQuery(engine, new EntityCommandPanelCollectionQueryConfig
            {
                Id = OwnerCountQueryId,
                CollectionKey = EntityCollectionKeys.CommandSource,
                Title = "Owner Count",
                Filter = EntityCommandPanelCollectionFilter.Any,
                Sort = EntityCommandPanelCollectionSortKind.OwnerCountThenSlotThenLabel
            });

            context = new EntityCommandPanelSourceContext(collectionOwner, CollectionSourceId, OwnerCountQueryId);
            copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);

            Assert.That(copied, Is.EqualTo(3));
            Assert.That(slots[0].AbilityId, Is.EqualTo(1001), "Two-owner Q should sort ahead of one-owner W/E.");
            Assert.That(slots[1].AbilityId, Is.EqualTo(1002), "Slot order remains deterministic after owner-count tie.");
            Assert.That(slots[2].AbilityId, Is.EqualTo(1003));
            Assert.That(slots[0].SlotIndex, Is.EqualTo(0));
            Assert.That(slots[1].SlotIndex, Is.EqualTo(1));
            Assert.That(slots[2].SlotIndex, Is.EqualTo(2));
        }

        [Test]
        public void ActivateSlot_UsesDisplayedSlotMapAndExistingActionSource()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            RegisterAbility(engine, 1001, "Arc Bolt", "Q detail");
            RegisterAbility(engine, 1002, "Ward", "W detail");
            RegisterAbility(engine, 1003, "Pulse", "E detail");

            Entity collectionOwner = engine.World.Create();
            Entity first = CreateActor(engine.World, "Alpha", 1001);
            Entity second = CreateActor(engine.World, "Beta", 1002, 1003);
            ReplaceCommandCollection(engine, collectionOwner, new[] { first, second });

            RegisterQuery(engine, new EntityCommandPanelCollectionQueryConfig
            {
                Id = OwnerCountQueryId,
                CollectionKey = EntityCollectionKeys.CommandSource,
                Filter = EntityCommandPanelCollectionFilter.Any,
                Sort = EntityCommandPanelCollectionSortKind.AbilityIdThenSlot
            });

            var submitted = new List<Order>();
            var mapping = CreateMappingSystem(submitted);
            mapping.SetLocalPlayer(collectionOwner, 7);
            mapping.SetActorProvider((out Entity actor) =>
            {
                actor = collectionOwner;
                return true;
            });
            engine.SetService(CoreServiceKeys.ActiveInputOrderMapping, mapping);

            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(collectionOwner, CollectionSourceId, OwnerCountQueryId);
            var slots = new EntityCommandPanelSlotView[8];
            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);

            Assert.That(copied, Is.EqualTo(3));
            Assert.That(slots[2].AbilityId, Is.EqualTo(1003));
            Assert.That(slots[2].SlotIndex, Is.EqualTo(2), "UI click should address dense displayed slot 2 after sorting.");

            InputOrderActivationResult activated = EntityCommandPanelSourceDispatch.ActivateSlot(source, in context, 0, 2);

            Assert.That(activated.State, Is.EqualTo(InputOrderActivationState.Submitted));
            Assert.That(activated.Actor, Is.EqualTo(second));
            Assert.That(activated.OrderId, Is.GreaterThan(0));
            Assert.That(submitted.Count, Is.EqualTo(1));
            Assert.That(submitted[0].Args.I0, Is.EqualTo(1), "Displayed slot 2 must route to the original owner slot 1/action SkillW.");
            Assert.That(submitted[0].Actor, Is.EqualTo(second),
                "Collection panel activation must preserve the member that owns the displayed ability.");
            Assert.That(submitted[0].OrderTypeId, Is.EqualTo(100));
        }

        [Test]
        public void ActivateSlot_PropagatesTypedQueueFullRejection()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            RegisterAbility(engine, 1001, "Arc Bolt", "Q detail");

            Entity collectionOwner = engine.World.Create();
            Entity first = CreateActor(engine.World, "Alpha", 1001);
            ReplaceCommandCollection(engine, collectionOwner, new[] { first });

            RegisterQuery(engine, new EntityCommandPanelCollectionQueryConfig
            {
                Id = OwnerCountQueryId,
                CollectionKey = EntityCollectionKeys.CommandSource,
                Filter = EntityCommandPanelCollectionFilter.Any,
                Sort = EntityCommandPanelCollectionSortKind.AbilityIdThenSlot
            });

            var mapping = CreateMappingSystem(new List<Order>());
            mapping.SetLocalPlayer(collectionOwner, 7);
            mapping.SetActorProvider((out Entity actor) =>
            {
                actor = collectionOwner;
                return true;
            });
            mapping.SetOrderSubmitHandler((in Order _) => OrderSubmitResult.RejectedQueueFull);
            engine.SetService(CoreServiceKeys.ActiveInputOrderMapping, mapping);

            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(collectionOwner, CollectionSourceId, OwnerCountQueryId);
            var slots = new EntityCommandPanelSlotView[8];
            Assert.That(EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots), Is.EqualTo(1));

            InputOrderActivationResult activated = EntityCommandPanelSourceDispatch.ActivateSlot(source, in context, 0, 0);

            Assert.That(activated.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(activated.Actor, Is.EqualTo(first));
            Assert.That(activated.Rejection, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(activated.OrderId, Is.GreaterThan(0));
        }

        private static GameEngine CreateEngineWithCommandPanelMod()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            // EntityCommandPanelMod loads through the real ModLoader so its
            // assets/Configs/UI/ability_aggregation_profiles.json fragment (aggregation.by_family,
            // the mod's default profile) is merged before the mod installs at GameStart.
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

        private static void RegisterAbility(GameEngine engine, int abilityId, string label, string detail)
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
            registry.Register(abilityId, in definition, "EntityCommandPanelCollectionQueryTests");
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
            var input = new FrozenInputActionReader();
            var mapping = new InputOrderMappingSystem(input, new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                        RequireTarget = false,
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true
                    },
                    new()
                    {
                        ActionId = "SkillW",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 1 },
                        RequireTarget = false,
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true
                    },
                    new()
                    {
                        ActionId = "SkillE",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 2 },
                        RequireTarget = false,
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true
                    },
                }
            });
            mapping.SetOrderTypeKeyResolver(key => string.Equals(key, "castAbility", StringComparison.Ordinal) ? 100 : 0);
            mapping.SetActivationActorValidator((actor, _) => actor != Entity.Null);
            int nextOrderId = 1;
            mapping.SetOrderIdentityAssigner((ref Order order) => order.OrderId = nextOrderId++);
            mapping.SetOrderSubmitHandler((in Order order) => { submitted.Add(order); return OrderSubmitResult.Queued; });
            return mapping;
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
