using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Layers;
using Ludots.Core.Input.EntityView;
using Ludots.Core.Input.Selection;
using Ludots.Core.MassNavigation;
using Ludots.Core.Input.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationMoveOrderSubmitterTests
    {
        private const int LocalPlayerId = 1;
        private const int LocalTeamId = 1;
        private const int EnemyPlayerId = 2;
        private const int EnemyTeamId = 2;
        private const int MoveOrderTypeId = 37;
        private const int BlockingOrderTypeId = 38;
        private const string RejectedMoveToOrderKey = "moveTo";

        [Test]
        public void RightClickMove_NoSelection_RejectsWithoutTeamMoveOrGroupTarget()
        {
            using TestContextScope context = CreateContext();
            Vector2 target = new(1200f, 1400f);

            context.SubmitMoveCommand(target);

            Assert.That(context.Simulation.CommandRejectsTotal, Is.EqualTo(1));
            Assert.That(context.Simulation.CommandCountFrame, Is.EqualTo(0));
            Assert.That(context.Simulation.NavGroupRuntime.ActiveGroupCount, Is.EqualTo(0));
            Assert.That(context.Simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.EqualTo(0));
            Assert.That(context.Simulation.LastCommandSelectionCount, Is.EqualTo(0));

            Assert.That(context.World.Get<OrderBuffer>(context.Agent).HasActive, Is.False);
        }

        [Test]
        public void RightClickMove_WithSelection_SubmitsSharedOrder()
        {
            using TestContextScope context = CreateContext();
            context.Select(context.Agent);
            Vector2 target = new(1600f, 1800f);

            context.SubmitMoveCommand(target);

            ref OrderBuffer orders = ref context.World.Get<OrderBuffer>(context.Agent);
            Assert.That(orders.HasActive, Is.True);
            Assert.That(orders.ActiveOrder.Order.OrderTypeId, Is.EqualTo(MoveOrderTypeId));
            Assert.That(orders.ActiveOrder.Order.OrderId, Is.GreaterThan(0));
            Assert.That(orders.ActiveOrder.Order.PlayerId, Is.EqualTo(1));
            Assert.That(orders.ActiveOrder.Order.Args.Spatial.WorldCm.X, Is.EqualTo(target.X));
            Assert.That(orders.ActiveOrder.Order.Args.Spatial.WorldCm.Z, Is.EqualTo(target.Y));
            Assert.That(orders.ActiveOrder.Order.Args.Selection.HasCollection, Is.True);
            Assert.That(context.Simulation.CommandCountFrame, Is.EqualTo(1));
            Assert.That(context.Simulation.CommandRejectsTotal, Is.EqualTo(0));
            Assert.That(context.Simulation.LastCommandSelectionCount, Is.EqualTo(1));
        }

        [Test]
        public void RightClickMove_OrderSubmitBlocked_DoesNotCountAsAcceptedCommand()
        {
            using TestContextScope context = CreateContext(blockMoveOrderWithActiveOrder: true);
            context.Select(context.Agent);
            context.SetActiveOrder(context.Agent, BlockingOrderTypeId);
            Vector2 target = new(1600f, 1800f);

            context.SubmitMoveCommand(target);

            ref OrderBuffer orders = ref context.World.Get<OrderBuffer>(context.Agent);
            Assert.That(orders.HasActive, Is.True);
            Assert.That(orders.ActiveOrder.Order.OrderTypeId, Is.EqualTo(BlockingOrderTypeId));
            Assert.That(context.Simulation.CommandCountFrame, Is.EqualTo(0));
            Assert.That(context.Simulation.CommandRejectsTotal, Is.EqualTo(1));
            Assert.That(context.Simulation.HasCommandFocus, Is.False);
        }

        [Test]
        public void RightClickMove_OrderSubmitIgnored_DoesNotCountAsAcceptedCommand()
        {
            using TestContextScope context = CreateContext(moveSameTypePolicy: SameTypePolicy.Ignore);
            context.Select(context.Agent);
            context.SetActiveOrder(context.Agent, MoveOrderTypeId);
            Vector2 target = new(1600f, 1800f);

            context.SubmitMoveCommand(target);

            Assert.That(context.Simulation.CommandCountFrame, Is.EqualTo(0));
            Assert.That(context.Simulation.CommandRejectsTotal, Is.EqualTo(1));
            Assert.That(context.Simulation.HasCommandFocus, Is.False);
        }

        [Test]
        public void RightClickMove_OrderSubmitQueueFull_DoesNotCountAsAcceptedCommand()
        {
            using TestContextScope context = CreateContext(
                moveSameTypePolicy: SameTypePolicy.Queue,
                moveQueueFullPolicy: QueueFullPolicy.RejectNew,
                moveMaxQueueSize: 0);
            context.Select(context.Agent);
            context.SetActiveOrder(context.Agent, MoveOrderTypeId);
            Vector2 target = new(1600f, 1800f);

            context.SubmitMoveCommand(target);

            Assert.That(context.Simulation.CommandCountFrame, Is.EqualTo(0));
            Assert.That(context.Simulation.CommandRejectsTotal, Is.EqualTo(1));
            Assert.That(context.Simulation.HasCommandFocus, Is.False);
        }

        [Test]
        public void RightClickMove_EnemyTeamSelection_RejectsWithoutMoveOrder()
        {
            using TestContextScope context = CreateContext();
            context.Select(context.EnemyAgent);
            Vector2 target = new(1600f, 1800f);

            context.SubmitMoveCommand(target);

            Assert.That(context.Simulation.CommandRejectsTotal, Is.EqualTo(1));
            Assert.That(context.Simulation.CommandCountFrame, Is.EqualTo(0));
            Assert.That(context.Simulation.LastCommandSelectionCount, Is.EqualTo(0));
            Assert.That(context.World.Get<OrderBuffer>(context.EnemyAgent).HasActive, Is.False);
            Assert.That(context.World.Get<OrderBuffer>(context.Agent).HasActive, Is.False);
        }

        [Test]
        public void RightClickMove_NonLocalPlayerOwnerSelection_RejectsEvenWhenTeamMatches()
        {
            using TestContextScope context = CreateContext();
            context.World.Set(context.Agent, new PlayerOwner { PlayerId = EnemyPlayerId });
            context.Select(context.Agent);
            Vector2 target = new(1600f, 1800f);

            context.SubmitMoveCommand(target);

            Assert.That(context.Simulation.CommandRejectsTotal, Is.EqualTo(1));
            Assert.That(context.Simulation.CommandCountFrame, Is.EqualTo(0));
            Assert.That(context.Simulation.LastCommandSelectionCount, Is.EqualTo(0));
            Assert.That(context.World.Get<OrderBuffer>(context.Agent).HasActive, Is.False);
        }

        [Test]
        public void RightClickMove_RequiresFormalMassNavigationOrderKeyStrictly()
        {
            using TestContextScope context = CreateContext(registerFormalMoveOrder: false, registerRejectedMoveToOrder: true);
            context.Select(context.Agent);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => context.SubmitMoveCommand(new Vector2(1600f, 1800f)))!;

            Assert.That(ex.Message, Does.Contain(MassNavigationOrderKeys.Move));
            Assert.That(ex.Message, Does.Not.Contain(RejectedMoveToOrderKey));
            Assert.That(context.World.Get<OrderBuffer>(context.Agent).HasActive, Is.False);
        }

        [Test]
        public void RightClickMove_WithSelectionRequiresCurrentCommandSourceCollection()
        {
            using TestContextScope context = CreateContext();
            context.Select(context.Agent);
            context.Engine.GlobalContext.Remove(CoreServiceKeys.EntityViewKey.Name);
            EntityViewProfileEntry profile = context.EntityViewConfig.RequireProfile(context.EntityViewConfig.DefaultViewKey);
            context.Collections.Replace(
                context.LocalPlayer,
                EntityCollectionDescriptor.Create(
                    profile.CommandSourceCollectionKey,
                    EntityCollectionSourceKind.SelectionView,
                    EntityCollectionRoleKind.CommandSource),
                ReadOnlySpan<Entity>.Empty);

            MassNavigationMoveCommandResult result = context.SubmitMoveCommand(new Vector2(1600f, 1800f));

            Assert.That(result, Is.EqualTo(MassNavigationMoveCommandResult.EmptySelection));
            Assert.That(context.World.Get<OrderBuffer>(context.Agent).HasActive, Is.False);
        }

        [Test]
        public void BindBoardWorld_RejectsActiveHotZoneOutsideBoardCenterRange()
        {
            MassNavigationConfig config = MassNavigationTestConfigFactory.CreateConfigForTests();
            config.World!.HotZones[0].CenterXCm = 1_000;
            var simulation = new MassNavigationSimulationRuntime(config);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => simulation.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100)))!;

            Assert.That(ex.Message, Does.Contain("active hot zone"));
            Assert.That(ex.Message, Does.Contain("center x"));
            Assert.That(ex.Message, Does.Contain("center range"));
        }

        [Test]
        public void SetFormationMode_RejectsUndefinedEnumValue()
        {
            var simulation = new MassNavigationSimulationRuntime(MassNavigationTestConfigFactory.CreateConfigForTests());

            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => simulation.SetFormationMode((MassNavigationFormationMode)999))!;

            Assert.That(ex.Message, Does.Contain("formation mode"));
        }

        [Test]
        public void SelectionScratchAndSnapshot_OverflowFailFastWithoutArrayResize()
        {
            MassNavigationConfig config = MassNavigationTestConfigFactory.CreateConfigForTests();
            config.ScenarioRuntime.InitialSelectionScratchCapacity = 1;
            config.ScenarioRuntime.InitialSelectedEntityCapacity = 1;
            var simulation = new MassNavigationSimulationRuntime(config);

            InvalidOperationException scratchEx = Assert.Throws<InvalidOperationException>(
                () => simulation.EnsureSelectionScratch(2))!;
            Assert.That(scratchEx.Message, Does.Contain("scenarioRuntime.initialSelectionScratchCapacity"));

            InvalidOperationException selectedEx = Assert.Throws<InvalidOperationException>(
                () => simulation.EnsureSelectionScratch(2))!;
            Assert.That(selectedEx.Message, Does.Contain("scenarioRuntime.initialSelectionScratchCapacity"));
        }

        private static TestContextScope CreateContext(
            bool registerFormalMoveOrder = true,
            bool registerRejectedMoveToOrder = false,
            SameTypePolicy moveSameTypePolicy = SameTypePolicy.Replace,
            QueueFullPolicy moveQueueFullPolicy = QueueFullPolicy.DropOldest,
            int moveMaxQueueSize = 3,
            bool blockMoveOrderWithActiveOrder = false)
        {
            var engine = new GameEngine();
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                Path.Combine(repoRoot, "assets"));
            World world = engine.World;
            Entity localPlayer = world.Create(new PlayerOwner { PlayerId = LocalPlayerId });
            Entity agent = world.Create(
                new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.Register("light") },
                new Team { Id = LocalTeamId },
                new PlayerOwner { PlayerId = LocalPlayerId },
                OrderBuffer.CreateEmpty());
            Entity enemyAgent = world.Create(
                new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.GetId("light") },
                new Team { Id = EnemyTeamId },
                OrderBuffer.CreateEmpty());

            var config = MassNavigationTestConfigFactory.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            simulation.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100));
            int layerIndex = LayerRegistry.Register(MassNavigationLayerNames.Agent);
            var agentLayer = new MassNavigationAgentLayer(1u << layerIndex, 1u << layerIndex);
            simulation.RebuildFromAuthoredAgents(
                world,
                new[] { agent, enemyAgent },
                new[]
                {
                    new MassNavigationAgentSeed(
                        teamId: LocalTeamId,
                        localPositionXCm: 100f,
                        localPositionYCm: 100f,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        agentLayer),
                    new MassNavigationAgentSeed(
                        teamId: EnemyTeamId,
                        localPositionXCm: 300f,
                        localPositionYCm: 100f,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        agentLayer),
                },
                new[] { true, true });

            var selectionRegistry = new StringIntRegistry(32, 1, 0, StringComparer.Ordinal);
            var selection = new SelectionRuntime(
                world,
                new SelectionRuntimeConfig
                {
                    TargetFilter = new SelectionTargetFilterConfig { RelationFilter = "All" },
                },
                selectionRegistry);
            EntityViewRuntimeConfig entityViewConfig = engine.GetService(CoreServiceKeys.EntityViewConfig)
                ?? throw new InvalidOperationException("MassNavigation command tests require EntityViewConfig.");
            EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("MassNavigation command tests require EntityCollectionStore.");
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, localPlayer);
            engine.SetService(CoreServiceKeys.SelectionRuntime, selection);
            engine.SetService(CoreServiceKeys.EntityViewConfig, entityViewConfig);
            engine.GlobalContext[CoreServiceKeys.EntityViewViewerEntity.Name] = localPlayer;
            engine.GlobalContext[CoreServiceKeys.EntityViewKey.Name] = entityViewConfig.DefaultViewKey;
            engine.GlobalContext[CoreServiceKeys.EntityCollectionStore.Name] = collections;

            var orderTypes = new OrderTypeRegistry();
            if (registerFormalMoveOrder)
            {
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = MassNavigationOrderKeys.Move,
                    OrderTypeId = MoveOrderTypeId,
                    SameTypePolicy = moveSameTypePolicy,
                    QueueFullPolicy = moveQueueFullPolicy,
                    MaxQueueSize = moveMaxQueueSize,
                    ClearQueueOnActivate = true,
                    AllowQueuedMode = true,
                });

                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "test.blockingOrder",
                    OrderTypeId = BlockingOrderTypeId,
                    SameTypePolicy = SameTypePolicy.Replace,
                    ClearQueueOnActivate = true,
                    AllowQueuedMode = true,
                });
            }

            if (registerRejectedMoveToOrder)
            {
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = RejectedMoveToOrderKey,
                    OrderTypeId = MoveOrderTypeId,
                    SameTypePolicy = SameTypePolicy.Replace,
                    ClearQueueOnActivate = true,
                    AllowQueuedMode = true,
                });
            }

            var orderRules = new OrderRuleRegistry();
            if (blockMoveOrderWithActiveOrder)
            {
                RegisterMoveBlockedByBlockingOrder(orderRules);
            }

            var orderQueue = new OrderQueue();
            var orderBuffer = new OrderBufferSystem(world, new DiscreteClock(), orderTypes, orderRules, orderQueue);
            engine.SetService(CoreServiceKeys.OrderQueue, orderQueue);
            engine.SetService(CoreServiceKeys.OrderTypeRegistry, orderTypes);
            engine.SetService(CoreServiceKeys.OrderRuleRegistry, orderRules);
            engine.SetService(CoreServiceKeys.OrderBufferSystem, orderBuffer);

            return new TestContextScope(engine, world, localPlayer, agent, enemyAgent, selection, simulation, orderQueue, orderBuffer, orderTypes, entityViewConfig, collections);
        }

        private static unsafe void RegisterMoveBlockedByBlockingOrder(OrderRuleRegistry orderRules)
        {
            var ruleSet = new OrderRuleSet
            {
                BlockedActiveCount = 1,
            };
            ruleSet.BlockedActiveOrderTypeIds[0] = BlockingOrderTypeId;
            orderRules.Register(MoveOrderTypeId, in ruleSet);
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private sealed class TestContextScope : IDisposable
        {
            public TestContextScope(
                GameEngine engine,
                World world,
                Entity localPlayer,
                Entity agent,
                Entity enemyAgent,
                SelectionRuntime selection,
                MassNavigationSimulationRuntime simulation,
                OrderQueue orderQueue,
                OrderBufferSystem orderBufferSystem,
                OrderTypeRegistry orderTypeRegistry,
                EntityViewRuntimeConfig entityViewConfig,
                EntityCollectionStore collections)
            {
                Engine = engine;
                World = world;
                LocalPlayer = localPlayer;
                Agent = agent;
                EnemyAgent = enemyAgent;
                Selection = selection;
                Simulation = simulation;
                OrderQueue = orderQueue;
                OrderBufferSystem = orderBufferSystem;
                OrderTypeRegistry = orderTypeRegistry;
                EntityViewConfig = entityViewConfig;
                Collections = collections;
            }

            public GameEngine Engine { get; }
            public World World { get; }
            public Entity LocalPlayer { get; }
            public Entity Agent { get; }
            public Entity EnemyAgent { get; }
            public SelectionRuntime Selection { get; }
            public MassNavigationSimulationRuntime Simulation { get; }
            public OrderQueue OrderQueue { get; }
            public OrderBufferSystem OrderBufferSystem { get; }
            public OrderTypeRegistry OrderTypeRegistry { get; }
            public EntityViewRuntimeConfig EntityViewConfig { get; }
            public EntityCollectionStore Collections { get; }

            public MassNavigationMoveCommandResult SubmitMoveCommand(Vector2 centerCm)
            {
                MassNavigationMoveCommandResult result = MassNavigationMoveOrderSubmitter.SubmitViaOrderQueue(
                    Simulation,
                    World,
                    Engine.GlobalContext,
                    OrderQueue,
                    OrderTypeRegistry,
                    centerCm,
                    LocalPlayerId);
                OrderBufferSystem.Update(0f);
                Simulation.ReconcilePendingMoveOrderAcceptance(World, OrderTypeRegistry);
                return result;
            }

            public void Select(Entity entity)
            {
                EntityViewProfileEntry profile = EntityViewConfig.RequireProfile(EntityViewConfig.DefaultViewKey);
                EntityViewRuntime.PromoteCommandSource(
                    Collections,
                    LocalPlayer,
                    in profile,
                    new[] { entity },
                    "test select");
                EntityViewRuntime.PromoteDisplayCollection(
                    Collections,
                    LocalPlayer,
                    in profile,
                    new[] { entity },
                    "test select");
            }

            public void SetActiveOrder(Entity entity, int orderTypeId)
            {
                ref OrderBuffer orders = ref World.Get<OrderBuffer>(entity);
                var order = new Order
                {
                    OrderId = 9001,
                    OrderTypeId = orderTypeId,
                    PlayerId = LocalPlayerId,
                    Actor = entity,
                    SubmitMode = OrderSubmitMode.Immediate,
                };
                orders.SetActiveDirect(in order, priority: 100);
            }

            public void Dispose()
            {
                Engine.Dispose();
            }
        }
    }
}
