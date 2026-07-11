using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
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
            Assert.That(context.Simulation.LastCommandActorCount, Is.EqualTo(0));

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
            Assert.That(orders.ActiveOrder.Order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            Assert.That(orders.ActiveOrder.Order.Args.Spatial.WorldCm.X, Is.EqualTo(target.X));
            Assert.That(orders.ActiveOrder.Order.Args.Spatial.WorldCm.Z, Is.EqualTo(target.Y));
            Assert.That(context.Simulation.CommandCountFrame, Is.EqualTo(1));
            Assert.That(context.Simulation.CommandRejectsTotal, Is.EqualTo(0));
            Assert.That(context.Simulation.LastCommandActorCount, Is.EqualTo(1));
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
            Assert.That(context.Simulation.LastCommandActorCount, Is.EqualTo(0));
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
            Assert.That(context.Simulation.LastCommandActorCount, Is.EqualTo(0));
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
        public void RightClickMove_WithCommandSourceDoesNotRequireLegacySelectionContainer()
        {
            using TestContextScope context = CreateContext();
            context.Select(context.Agent);

            MassNavigationMoveCommandResult result = context.SubmitMoveCommand(new Vector2(1600f, 1800f));

            Assert.That(result, Is.EqualTo(MassNavigationMoveCommandResult.Submitted));
            Assert.That(context.World.Get<OrderBuffer>(context.Agent).ActiveOrder.Order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
        }

        [Test]
        public void BindBoardWorld_RejectsActiveHotZoneOutsideBoardCenterRange()
        {
            MassNavigationConfig config = CreateConfigForTests();
            config.World!.HotZones[0].CenterXCm = 1_000;
            var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
            var loadedChunks = new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(config.World.StreamingChunkSizeCm);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => simulation.BindBoardWorld(
                    new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
                    loadedChunks))!;

            Assert.That(ex.Message, Does.Contain("active hot zone"));
            Assert.That(ex.Message, Does.Contain("center x"));
            Assert.That(ex.Message, Does.Contain("center range"));
        }

        [Test]
        public void SetFormationMode_RejectsUndefinedEnumValue()
        {
            var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), CreateConfigForTests());

            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => simulation.SetFormationMode((MassNavigationFormationMode)999))!;

            Assert.That(ex.Message, Does.Contain("formation mode"));
        }

        [Test]
        public void SelectionScratchAndSnapshot_OverflowFailFastWithoutArrayResize()
        {
            MassNavigationConfig config = CreateConfigForTests();
            config.Capacity.InitialCommandActorScratchCapacity = 1;
            config.Capacity.InitialCommandActorSnapshotCapacity = 1;
            var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);

            InvalidOperationException scratchEx = Assert.Throws<InvalidOperationException>(
                () => simulation.EnsureCommandActorScratch(2))!;
            Assert.That(scratchEx.Message, Does.Contain("runtime.capacity.initialCommandActorScratchCapacity"));

            InvalidOperationException selectedEx = Assert.Throws<InvalidOperationException>(
                () => simulation.SetCommandActorSnapshot(new[] { Entity.Null, Entity.Null }, revision: 1))!;
            Assert.That(selectedEx.Message, Does.Contain("runtime.capacity.initialCommandActorSnapshotCapacity"));
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
            var simulation = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
            simulation.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
                new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(config.World!.StreamingChunkSizeCm));
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

            engine.SetService(CoreServiceKeys.LocalPlayerEntity, localPlayer);

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

            return new TestContextScope(engine, world, localPlayer, agent, enemyAgent, simulation, orderBuffer, orderTypes);
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
                Solver = solver,
                World = new MassNavigationWorldConfig
                {
                    StreamingChunkSizeCm = 500,
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
                        },
                    },
                },
                Streaming = new MassNavigationStreamingConfig
                {
                    RetainSeconds = 6f,
                    RadiusCm = 1000,
                },
                Capacity = new MassNavigationCapacityConfig
                {
                    InitialCommandActorScratchCapacity = 8,
                    InitialCommandActorSnapshotCapacity = 8,
                    NavigationGroupCapacity = 8,
                    GroupMembershipAgentCapacity = 16,
                    CommandActorScratchCapacity = 8,
                    GroupMemberCapacity = 8,
                    OrderIngestionTokenCapacity = 8,
                    OrderIngestionMemberCapacity = 8,
                    LoadedChunkCapacity = 32,
                    MetadataTeamCapacity = 2,
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
                Flow = baseConfig.Flow,
                Arrival = baseConfig.Arrival,
                Avoidance = baseConfig.Avoidance,
                Semantics = baseConfig.Semantics,
            };
            config.Solver.Validate();
            config.World.Validate(config.Solver);
            config.Streaming.Validate();
            config.Capacity.Validate();
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
            JsonArray profiles = JsonNode.Parse(File.ReadAllText(path))?.AsArray()
                ?? throw new InvalidOperationException("MassNavigationConfig.json must contain a profile array.");
            JsonObject profile = (JsonObject)(profiles[0]?.DeepClone()
                ?? throw new InvalidOperationException("MassNavigationConfig.json must contain the base profile."));
            profile.Remove("id");
            profile.Remove("extends");
            return MassNavigationCapabilityProfile.Load(profile).Runtime;
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
                MassNavigationSimulationRuntime simulation,
                OrderBufferSystem orderBufferSystem,
                OrderTypeRegistry orderTypeRegistry)
            {
                Engine = engine;
                World = world;
                LocalPlayer = localPlayer;
                Agent = agent;
                EnemyAgent = enemyAgent;
                Simulation = simulation;
                OrderBufferSystem = orderBufferSystem;
                OrderTypeRegistry = orderTypeRegistry;
            }

            public GameEngine Engine { get; }
            public World World { get; }
            public Entity LocalPlayer { get; }
            public Entity Agent { get; }
            public Entity EnemyAgent { get; }
            public MassNavigationSimulationRuntime Simulation { get; }
            public OrderBufferSystem OrderBufferSystem { get; }
            public OrderTypeRegistry OrderTypeRegistry { get; }

            public MassNavigationMoveCommandResult SubmitMoveCommand(Vector2 centerCm)
            {
                if (!Simulation.ContainsWorldPoint(centerCm.X, centerCm.Y))
                {
                    Simulation.RejectCommandOutsideWorld(centerCm.X, centerCm.Y);
                    return MassNavigationMoveCommandResult.OutsideWorld;
                }

                ReadOnlySpan<Entity> actors = Simulation.CommandActors;
                if (actors.Length <= 0)
                {
                    Simulation.RejectCommandWithoutCommandActors(centerCm.X, centerCm.Y);
                    return MassNavigationMoveCommandResult.EmptyCommandActors;
                }

                if (!CanLocalPlayerCommand(World, actors))
                {
                    Simulation.RejectCommandUnauthorizedCommandActors(centerCm.X, centerCm.Y);
                    return MassNavigationMoveCommandResult.UnauthorizedCommandActors;
                }

                if (!OrderTypeRegistry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
                {
                    throw new InvalidOperationException($"MassNavigation runtime requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
                }

                int sharedOrderId = Simulation.AllocateSharedOrderId();
                int submitted = 0;
                for (int i = 0; i < actors.Length; i++)
                {
                    Entity actor = actors[i];
                    if (!World.IsAlive(actor))
                    {
                        continue;
                    }

                    var order = new Order
                    {
                        OrderId = sharedOrderId,
                        OrderTypeId = moveOrderTypeId,
                        PlayerId = LocalPlayerId,
                        Actor = actor,
                        SubmitMode = OrderSubmitMode.Immediate,
                        Args = MassNavigationMoveOrderArgs.Encode(
                            centerCm,
                            Simulation.FormationMode,
                            Simulation.NavGroupRuntime.CommandActorRotationRadians)
                    };

                    OrderSubmitResult result = OrderBufferSystem.SubmitOrder(actor, in order);
                    if (result == OrderSubmitResult.Activated || result == OrderSubmitResult.Queued)
                    {
                        submitted++;
                    }
                }

                if (submitted <= 0)
                {
                    Simulation.RejectCommandOrderSubmit(centerCm.X, centerCm.Y);
                    return MassNavigationMoveCommandResult.OrderSubmitRejected;
                }

                Simulation.FocusCommandTarget(centerCm, actors);
                Simulation.MarkCommandApply();
                return MassNavigationMoveCommandResult.Submitted;
            }

            public void Select(Entity entity)
            {
                Simulation.SetCommandActorSnapshot(new[] { entity }, revision: Simulation.CommandActorSnapshotRevision + 1);
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

            private static bool CanLocalPlayerCommand(World world, ReadOnlySpan<Entity> actors)
            {
                int liveActors = 0;
                for (int i = 0; i < actors.Length; i++)
                {
                    Entity actor = actors[i];
                    if (!world.IsAlive(actor))
                    {
                        continue;
                    }

                    if (!world.TryGet(actor, out PlayerOwner owner) ||
                        owner.PlayerId != LocalPlayerId)
                    {
                        return false;
                    }

                    liveActors++;
                }

                return liveActors > 0;
            }
        }
    }
}
