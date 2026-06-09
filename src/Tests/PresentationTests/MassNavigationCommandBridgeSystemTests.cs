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
using Ludots.Core.Input.Selection;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using MassNavigationMod;
using MassNavigationMod.Runtime;
using MassNavigationMod.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationCommandBridgeSystemTests
    {
        private const int MoveOrderTypeId = 37;

        [Test]
        public void RightClickMove_NoSelection_RejectsWithoutTeamMoveOrGroupTarget()
        {
            using TestContextScope context = CreateContext();
            Vector2 target = new(1200f, 1400f);

            context.Bridge.SubmitMoveCommandForTests(target);

            Assert.That(context.Simulation.PendingCommandCount, Is.EqualTo(0));
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

            context.Bridge.SubmitMoveCommandForTests(target);

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
            Assert.That(context.Simulation.PendingCommandCount, Is.EqualTo(0));
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.HasOrder, Is.True);
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.FanoutCount, Is.EqualTo(1));
            Assert.That(context.Simulation.AcceptanceDiagnostics.TargetAllocation.HasAllocation, Is.True);
            Assert.That(context.Simulation.AcceptanceDiagnostics.TargetAllocation.SlotCount, Is.EqualTo(1));
        }

        [Test]
        public void RightClickMove_SameAndNearTarget_ReusesNormalizedRouteBucket()
        {
            using TestContextScope context = CreateContext();
            context.Select(context.Agent);
            Vector2 target = new(1600f, 1800f);

            context.Bridge.SubmitMoveCommandForTests(target);
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.CacheHit, Is.False);

            context.Bridge.SubmitMoveCommandForTests(target);
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.CacheHit, Is.True);
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.SamePointReuseCount, Is.EqualTo(1));
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.ReuseScope, Is.EqualTo("same_point_order_bucket"));

            context.Bridge.SubmitMoveCommandForTests(target + new Vector2(250f, 120f));
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.CacheHit, Is.True);
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.NearPointReuseCount, Is.EqualTo(1));
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.RouteCacheSize, Is.EqualTo(1));
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.ReuseScope, Is.EqualTo("near_point_order_bucket"));
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.PathRouteSignature, Is.EqualTo("not_available"));
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.MeshRouteSignature, Is.EqualTo("not_available"));
            Assert.That(context.Simulation.AcceptanceDiagnostics.OrderReuse.ProductionGap, Does.Contain("normalized_bucket_route_reuse_passed"));
        }

        [Test]
        public void SelectionMove_TenThousandAgents_AllocatesOneSlotPerUnitAndKeepsRouteReusable()
        {
            using var world = World.Create();
            var simulation = new MassNavigationSimulationRuntime(CreateConfig());
            simulation.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100));
            simulation.MassFlow.Reset(new[] { 1 }, unitsPerTeam: 10_000, simulation.Config.World!.Obstacles);

            Entity[] selected = new Entity[10_000];
            for (int i = 0; i < selected.Length; i++)
            {
                Entity agent = world.Create(
                    default(MassNavigationAgentTag),
                    new MassNavigationAgentIndex { Value = i },
                    new Team { Id = 1 },
                    OrderBuffer.CreateEmpty());
                simulation.AgentState.RegisterAgent(agent, controllable: true);
                selected[i] = agent;
            }

            var orderTypes = new OrderTypeRegistry();
            RegisterMassMoveOrderType(orderTypes);
            var orderRules = new OrderRuleRegistry();
            using GameEngine engine = CreateOrderBridgeEngine(world, orderTypes, orderRules);
            var orderBridge = new MassNavigationOrderBridgeSystem(engine, simulation);

            int firstOrderId = simulation.AllocateSharedOrderId();
            SubmitSharedOrder(world, orderTypes, orderRules, selected, new Vector2(3_820f, 1_540f), firstOrderId);
            DriveOrderBridgeUntilSubmitted(orderBridge, simulation, firstOrderId);

            Assert.That(simulation.NavGroupRuntime.ActiveGroupCount, Is.EqualTo(1));
            Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.EqualTo(1));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.HasAllocation, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SelectedCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SlotCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ReachableSlotCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ReachabilityFanoutCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.UnitSlotCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.BlockedSlotCount, Is.EqualTo(0));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.FallbackSlotCount, Is.EqualTo(0));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ReachabilityProbeStatus, Is.EqualTo("ProjectedByFormation"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ReachabilitySource, Does.Contain("formation_slot_projection"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ReachabilitySource, Does.Contain("shared_order_fanout"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.AllocationRouteId, Is.GreaterThan(0));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.AllocationRouteReuseKey, Does.Contain("goalBucket"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.BlockedReasonSummary, Is.EqualTo("none"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.FallbackReasonSummary, Is.EqualTo("none"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ProductionGap, Does.Contain("reachability_probe_missing"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.FormationMode, Is.EqualTo(nameof(MassNavigationFormationMode.Square)));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.GoalFootprintRadiusCm, Is.GreaterThanOrEqualTo(4_000f));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.FanoutCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.CacheHit, Is.False);
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.ReuseScope, Is.EqualTo("cold_order_bucket"));
            Assert.That(simulation.NavGroupRuntime.TargetRefreshBudget, Is.GreaterThan(0));
            Assert.That(simulation.NavGroupRuntime.AppliedTargetRefreshCountFrame, Is.LessThan(10_000),
                "Large selection target projection must be budgeted instead of resolving every slot in one command frame.");
            Assert.That(simulation.NavGroupRuntime.PendingTargetRefreshCount, Is.GreaterThan(0));
            DrainTargetRefresh(simulation, selected);
            Assert.That(simulation.MassFlow.CountUnitsWithTargets(), Is.EqualTo(10_000));
            Assert.That(simulation.NavGroupRuntime.PendingTargetRefreshCount, Is.EqualTo(0));

            int secondOrderId = simulation.AllocateSharedOrderId();
            SubmitSharedOrder(world, orderTypes, orderRules, selected, new Vector2(3_880f, 1_580f), secondOrderId);
            DriveOrderBridgeUntilSubmitted(orderBridge, simulation, secondOrderId);

            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.CacheHit, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.NearPointReuseCount, Is.EqualTo(1));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.RouteCacheSize, Is.EqualTo(1));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.ReuseScope, Is.EqualTo("near_point_order_bucket"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SlotCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ReachableSlotCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.AllocationRouteId, Is.EqualTo(simulation.AcceptanceDiagnostics.OrderReuse.ReusedRouteId));
            DrainTargetRefresh(simulation, selected);
            Assert.That(simulation.MassFlow.CountUnitsWithTargets(), Is.EqualTo(10_000));
        }

        private static void DriveOrderBridgeUntilSubmitted(
            MassNavigationOrderBridgeSystem orderBridge,
            MassNavigationSimulationRuntime simulation,
            int orderId)
        {
            for (int frame = 0; frame < 32 && simulation.AcceptanceDiagnostics.OrderReuse.LastOrderId != orderId; frame++)
            {
                simulation.BeginFrame(0.016f);
                if (frame == 0)
                {
                    simulation.MarkCommandApply();
                }

                orderBridge.Update(0.016f);
            }
        }

        private static void DrainTargetRefresh(MassNavigationSimulationRuntime simulation, Entity[] selected)
        {
            int maxRefreshFrames = (int)Math.Ceiling(10_000d / simulation.NavGroupRuntime.TargetRefreshBudget) * 4;
            for (int frame = 1; frame <= maxRefreshFrames && simulation.NavGroupRuntime.PendingTargetRefreshCount > 0; frame += 2)
            {
                simulation.NavGroupRuntime.UpdateTargets(
                    simulation.MassFlow,
                    simulation.AgentState,
                    selected,
                    frame);
            }
        }

        private static TestContextScope CreateContext()
        {
            var engine = new GameEngine();
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                Path.Combine(repoRoot, "assets"));
            World world = engine.World;
            Entity localPlayer = world.Create(new PlayerOwner { PlayerId = 1 });
            Entity agent = world.Create(
                default(MassNavigationAgentTag),
                new MassNavigationAgentIndex { Value = 0 },
                new Team { Id = 1 },
                OrderBuffer.CreateEmpty());

            var config = CreateConfig();
            var simulation = new MassNavigationSimulationRuntime(config);
            simulation.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100));
            simulation.MassFlow.Reset(new[] { 1, 2 }, unitsPerTeam: 1, config.World!.Obstacles);
            simulation.AgentState.RegisterAgent(agent, controllable: true);

            var selectionRegistry = new StringIntRegistry(32, 1, 0, StringComparer.Ordinal);
            var selection = new SelectionRuntime(world, new SelectionRuntimeConfig(), selectionRegistry);
            selection.TryBindView(localPlayer, SelectionViewKeys.Primary, localPlayer, SelectionSetKeys.LivePrimary);
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, localPlayer);
            engine.SetService(CoreServiceKeys.SelectionRuntime, selection);
            engine.SetService(CoreServiceKeys.SelectionViewViewerEntity, localPlayer);
            engine.SetService(CoreServiceKeys.SelectionViewKey, SelectionViewKeys.Primary);

            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = MassNavigationOrderKeys.Move,
                OrderTypeId = MoveOrderTypeId,
                SameTypePolicy = SameTypePolicy.Replace,
                ClearQueueOnActivate = true,
                AllowQueuedMode = true,
            });
            var orderRules = new OrderRuleRegistry();
            var orderBuffer = new OrderBufferSystem(world, new DiscreteClock(), orderTypes, orderRules);
            engine.SetService(CoreServiceKeys.OrderTypeRegistry, orderTypes);
            engine.SetService(CoreServiceKeys.OrderRuleRegistry, orderRules);
            engine.SetService(CoreServiceKeys.OrderBufferSystem, orderBuffer);

            MassNavigationSelectionSync.SyncIfChanged(world, engine.GlobalContext, selection, simulation);
            var bridge = new MassNavigationCommandBridgeSystem(engine, simulation);
            return new TestContextScope(engine, world, localPlayer, agent, selection, simulation, bridge);
        }

        private static void RegisterMassMoveOrderType(OrderTypeRegistry orderTypes)
        {
            orderTypes.Register(new OrderTypeConfig
            {
                Key = MassNavigationOrderKeys.Move,
                OrderTypeId = MoveOrderTypeId,
                SameTypePolicy = SameTypePolicy.Replace,
                ClearQueueOnActivate = true,
                CanInterruptSelf = true,
                BufferWindowMs = 0,
                AllowQueuedMode = true,
            });
        }

        private static GameEngine CreateOrderBridgeEngine(
            World world,
            OrderTypeRegistry orderTypes,
            OrderRuleRegistry orderRules)
        {
            var engine = new GameEngine();
            typeof(GameEngine)
                .GetProperty(nameof(GameEngine.World))!
                .SetValue(engine, world);
            var mapId = new MapId(MassNavigationIds.MapId);
            var session = new MapSession(mapId, new MapConfig { Id = MassNavigationIds.MapId });
            typeof(GameEngine)
                .GetProperty(nameof(GameEngine.CurrentMapSession))!
                .SetValue(engine, session);
            engine.SetService(CoreServiceKeys.MapId, mapId);
            engine.SetService(CoreServiceKeys.MapSession, session);
            engine.SetService(CoreServiceKeys.OrderTypeRegistry, orderTypes);
            engine.SetService(CoreServiceKeys.OrderRuleRegistry, orderRules);
            engine.SetService(CoreServiceKeys.OrderBufferSystem, new OrderBufferSystem(world, new DiscreteClock(), orderTypes, orderRules));
            return engine;
        }

        private static void SubmitSharedOrder(
            World world,
            OrderTypeRegistry orderTypes,
            OrderRuleRegistry orderRules,
            Entity[] selected,
            Vector2 destination,
            int orderId)
        {
            var orderBuffer = new OrderBufferSystem(world, new DiscreteClock(), orderTypes, orderRules);
            for (int i = 0; i < selected.Length; i++)
            {
                var order = new Order
                {
                    OrderId = orderId,
                    OrderTypeId = MoveOrderTypeId,
                    PlayerId = 1,
                    Actor = selected[i],
                    SubmitMode = OrderSubmitMode.Immediate,
                    Args = new OrderArgs
                    {
                        I0 = (int)MassNavigationFormationMode.Square,
                        Spatial = new OrderSpatial
                        {
                            Kind = OrderSpatialKind.WorldCm,
                            Mode = OrderCollectionMode.Single,
                            WorldCm = new Vector3(destination.X, 0f, destination.Y),
                        },
                    }
                };

                Assert.That(orderBuffer.SubmitOrder(selected[i], in order), Is.Not.EqualTo(OrderSubmitResult.InvalidEntity));
            }
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

        private static MassNavigationConfig CreateConfig()
        {
            var config = new MassNavigationConfig
            {
                MapId = "mass_navigation",
                World = new MassNavigationWorldConfig
                {
                    SolverWindowWidthCm = MassFlowSimulationState.FieldWidthCm,
                    SolverWindowHeightCm = MassFlowSimulationState.FieldHeightCm,
                    StreamingChunkSizeCm = 500,
                    StreamingRadiusCm = 1000,
                    CameraFocusShiftThresholdCm = 100,
                    CommandFocusHoldTicks = 3,
                    WorkAreaPaddingCm = 100,
                    WorkAreaMaxWidthCm = MassFlowSimulationState.FieldWidthCm,
                    WorkAreaMaxHeightCm = MassFlowSimulationState.FieldHeightCm,
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
                    Obstacles = new[]
                    {
                        new MassNavigationObstacleConfig
                        {
                            Id = "blocker",
                            LocalXCm = 5000f,
                            LocalYCm = 5000f,
                            RadiusCm = 100f,
                        },
                    },
                },
                Scenario = new MassNavigationScenarioConfig
                {
                    AgentsPerTeam = 1,
                    InitialSelectedTeamId = 1,
                    Teams = new[]
                    {
                        new MassNavigationScenarioTeamConfig { Id = 1, Name = "Team 1" },
                        new MassNavigationScenarioTeamConfig { Id = 2, Name = "Team 2" },
                    },
                },
            };
            config.World.Validate();
            config.Scenario.Validate();
            return config;
        }

        private sealed class TestContextScope : IDisposable
        {
            public TestContextScope(
                GameEngine engine,
                World world,
                Entity localPlayer,
                Entity agent,
                SelectionRuntime selection,
                MassNavigationSimulationRuntime simulation,
                MassNavigationCommandBridgeSystem bridge)
            {
                Engine = engine;
                World = world;
                LocalPlayer = localPlayer;
                Agent = agent;
                Selection = selection;
                Simulation = simulation;
                Bridge = bridge;
            }

            public GameEngine Engine { get; }
            public World World { get; }
            public Entity LocalPlayer { get; }
            public Entity Agent { get; }
            public SelectionRuntime Selection { get; }
            public MassNavigationSimulationRuntime Simulation { get; }
            public MassNavigationCommandBridgeSystem Bridge { get; }

            public void Select(Entity entity)
            {
                if (!Selection.ReplaceSelection(LocalPlayer, SelectionSetKeys.LivePrimary, new[] { entity }))
                {
                    throw new InvalidOperationException("Failed to write test selection.");
                }

                MassNavigationSelectionSync.SyncIfChanged(World, Engine.GlobalContext, Selection, Simulation);
            }

            public void Dispose()
            {
                Engine.Dispose();
            }
        }
    }
}
