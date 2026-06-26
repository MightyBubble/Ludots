using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Layers;
using Ludots.Core.Input.Selection;
using Ludots.Core.MassNavigation;
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
    public sealed class MassNavigationLocalCommandInputSystemTests
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
            Assert.That(orders.ActiveOrder.Order.Args.Selection.Container, Is.Not.EqualTo(Entity.Null));
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
        public void RightClickMove_WithSelectionRequiresCurrentSelectionContainer()
        {
            using TestContextScope context = CreateContext();
            context.Select(context.Agent);
            context.Engine.GlobalContext.Remove(CoreServiceKeys.SelectionViewKey.Name);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => context.SubmitMoveCommand(new Vector2(1600f, 1800f)))!;

            Assert.That(ex.Message, Does.Contain("current selection container"));
            Assert.That(context.World.Get<OrderBuffer>(context.Agent).HasActive, Is.False);
        }

        [Test]
        public void BindBoardWorld_RejectsActiveHotZoneOutsideBoardCenterRange()
        {
            MassNavigationConfig config = CreateConfigForTests();
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
            var simulation = new MassNavigationSimulationRuntime(CreateConfigForTests());

            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => simulation.SetFormationMode((MassNavigationFormationMode)999))!;

            Assert.That(ex.Message, Does.Contain("formation mode"));
        }

        [Test]
        public void SelectionScratchAndSnapshot_OverflowFailFastWithoutArrayResize()
        {
            MassNavigationConfig config = CreateConfigForTests();
            config.ScenarioRuntime.InitialSelectionScratchCapacity = 1;
            config.ScenarioRuntime.InitialSelectedEntityCapacity = 1;
            var simulation = new MassNavigationSimulationRuntime(config);

            InvalidOperationException scratchEx = Assert.Throws<InvalidOperationException>(
                () => simulation.EnsureSelectionScratch(2))!;
            Assert.That(scratchEx.Message, Does.Contain("scenarioRuntime.initialSelectionScratchCapacity"));

            InvalidOperationException selectedEx = Assert.Throws<InvalidOperationException>(
                () => simulation.SetSelection(new[] { Entity.Null, Entity.Null }, revision: 1))!;
            Assert.That(selectedEx.Message, Does.Contain("scenarioRuntime.initialSelectedEntityCapacity"));

            string source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "src",
                "Core",
                "MassNavigation",
                "Runtime",
                "MassNavigationSimulationRuntime.cs"));
            Assert.That(source, Does.Not.Contain("Array.Resize(ref _selectionScratch"));
            Assert.That(source, Does.Not.Contain("Array.Resize(ref _selectedEntities"));
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

            var config = CreateConfigForTests();
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
            selection.TryBindView(localPlayer, SelectionViewKeys.Primary, localPlayer, SelectionSetKeys.LivePrimary);
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, localPlayer);
            engine.SetService(CoreServiceKeys.SelectionRuntime, selection);
            engine.SetService(CoreServiceKeys.SelectionViewViewerEntity, localPlayer);
            engine.SetService(CoreServiceKeys.SelectionViewKey, SelectionViewKeys.Primary);

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
            var orderBuffer = new OrderBufferSystem(world, new DiscreteClock(), orderTypes, orderRules);
            engine.SetService(CoreServiceKeys.OrderTypeRegistry, orderTypes);
            engine.SetService(CoreServiceKeys.OrderRuleRegistry, orderRules);
            engine.SetService(CoreServiceKeys.OrderBufferSystem, orderBuffer);

            MassNavigationSelectionSync.SyncIfChanged(world, engine.GlobalContext, selection, simulation);
            return new TestContextScope(engine, world, localPlayer, agent, enemyAgent, selection, simulation, orderBuffer, orderTypes);
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

        internal static MassNavigationConfig CreateConfigForTests()
        {
            MassNavigationConfig baseConfig = LoadBaseMassNavigationConfig();
            MassNavigationFlowSolverConfig solver = CreateTestSolverConfig();
            var config = new MassNavigationConfig
            {
                MapId = "mass_navigation",
                Solver = solver,
                World = new MassNavigationWorldConfig
                {
                    SolverWindowWidthCm = solver.FieldWidthCm,
                    SolverWindowHeightCm = solver.FieldHeightCm,
                    StreamingChunkSizeCm = 500,
                    StreamingRadiusCm = 1000,
                    CommandFocusHoldTicks = 3,
                    WorkAreaPaddingCm = 100,
                    WorkAreaMaxWidthCm = solver.FieldWidthCm,
                    WorkAreaMaxHeightCm = solver.FieldHeightCm,
                    ActiveHotZoneId = "center",
                    HotZones = new[]
                    {
                        new MassNavigationHotZoneConfig
                        {
                            Id = "center",
                            Label = "Center",
                            CenterXCm = 5000,
                            CenterYCm = 5000,
                            WidthCm = 1000,
                            HeightCm = 1000,
                        },
                    },
                },
                Streaming = new MassNavigationStreamingConfig
                {
                    RetainSeconds = 6f,
                    RadiusCm = 1000,
                },
                Scenario = new MassNavigationScenarioConfig
                {
                    AgentsPerTeam = 1,
                    InitialSelectedTeamId = LocalTeamId,
                    Teams = new[]
                    {
                        new MassNavigationScenarioTeamConfig { Id = LocalTeamId, Name = "Team 1" },
                        new MassNavigationScenarioTeamConfig { Id = EnemyTeamId, Name = "Team 2" },
                    },
                    SpawnLayout = new MassNavigationScenarioSpawnLayoutConfig
                    {
                        Kind = "OrbitOpposedTargets",
                        OrbitRadiusCm = 3000f,
                    },
                },
                ScenarioRuntime = new MassNavigationScenarioRuntimeConfig
                {
                    AutoSpawnConfiguredScenario = true,
                    InitialSelectionScratchCapacity = 8,
                    InitialSelectedEntityCapacity = 8,
                    RuntimeCapacity = new MassNavigationRuntimeCapacityConfig
                    {
                        NavigationGroupCapacity = 8,
                        GroupMembershipAgentCapacity = 16,
                        SelectionMemberScratchCapacity = 8,
                        GroupMemberCapacity = 8,
                        OrderIngestionTokenCapacity = 8,
                        OrderIngestionMemberCapacity = 8,
                        LoadedChunkCapacity = 32,
                        MetadataTeamCapacity = 2,
                    },
                },
                AgentProfiles = new MassNavigationAgentProfileSetConfig
                {
                    DefaultProfileId = "light",
                    Profiles = new[]
                    {
                        new MassNavigationAgentProfileConfig
                        {
                            Id = "light",
                            Heavy = false,
                            VisualScale = 0.22f,
                            SpeedCmPerSecond = 800f,
                            EveryNth = 0,
                            NthOffset = 0,
                        },
                    },
                },
                Cadence = baseConfig.Cadence,
                Presentation = baseConfig.Presentation,
                TeamRelationships = baseConfig.TeamRelationships,
                Flow = baseConfig.Flow,
                Arrival = baseConfig.Arrival,
                Avoidance = baseConfig.Avoidance,
                Semantics = baseConfig.Semantics,
            };
            config.Solver.Validate();
            config.World.Validate(config.Solver);
            config.Streaming.Validate();
            config.ScenarioRuntime.Validate();
            config.Scenario.Validate(config.ScenarioRuntime);
            config.AgentProfiles.Validate();
            config.AgentProfiles.BindAgentProfiles(CreateAgentProfilesForTests());
            return config;
        }

        private static MassNavigationConfig LoadBaseMassNavigationConfig()
        {
            string path = Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "assets",
                "MassNavigationConfig.json");
            using FileStream stream = File.OpenRead(path);
            return MassNavigationConfig.Load(stream);
        }

        private static AgentProfileRegistry CreateAgentProfilesForTests()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "light",
                    RadiusCm = 20,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }

        private static MassNavigationFlowSolverConfig CreateTestSolverConfig()
        {
            return new MassNavigationFlowSolverConfig
            {
                FieldWidthCm = 10_000,
                FieldHeightCm = 10_000,
                FlowCellSizeCm = 100,
                MaxObstacleCount = 64,
                ParallelWorkerCount = 1,
                SeparationHashCellSizeCm = 100,
                SeparationHashMinSearchRadiusCells = 2,
                HardResolveHashCellSizeCm = 50,
                HardResolveHashMinSearchRadiusCells = 1,
                PlayAreaMinXCm = 50f,
                PlayAreaMaxXCm = 9_950f,
                PlayAreaMinYCm = 50f,
                PlayAreaMaxYCm = 9_950f,
            };
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
                OrderBufferSystem orderBufferSystem,
                OrderTypeRegistry orderTypeRegistry)
            {
                Engine = engine;
                World = world;
                LocalPlayer = localPlayer;
                Agent = agent;
                EnemyAgent = enemyAgent;
                Selection = selection;
                Simulation = simulation;
                OrderBufferSystem = orderBufferSystem;
                OrderTypeRegistry = orderTypeRegistry;
            }

            public GameEngine Engine { get; }
            public World World { get; }
            public Entity LocalPlayer { get; }
            public Entity Agent { get; }
            public Entity EnemyAgent { get; }
            public SelectionRuntime Selection { get; }
            public MassNavigationSimulationRuntime Simulation { get; }
            public OrderBufferSystem OrderBufferSystem { get; }
            public OrderTypeRegistry OrderTypeRegistry { get; }

            public MassNavigationMoveCommandResult SubmitMoveCommand(Vector2 centerCm)
            {
                return Simulation.SubmitMoveCommand(
                    World,
                    Engine.GlobalContext,
                    OrderBufferSystem,
                    OrderTypeRegistry,
                    centerCm,
                    LocalPlayerId);
            }

            public void Select(Entity entity)
            {
                if (!Selection.ReplaceSelection(LocalPlayer, SelectionSetKeys.LivePrimary, new[] { entity }))
                {
                    throw new InvalidOperationException("Failed to write test selection.");
                }

                MassNavigationSelectionSync.SyncIfChanged(World, Engine.GlobalContext, Selection, Simulation);
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
