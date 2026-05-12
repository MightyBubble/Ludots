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
            int applied = context.Simulation.Commands.ApplyPending(context.World, context.Simulation);

            Assert.That(applied, Is.EqualTo(0));
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
        }

        [Test]
        public void RightClickMove_WithSelectionRequiresCurrentSelectionContainer()
        {
            using TestContextScope context = CreateContext();
            context.Select(context.Agent);
            context.Engine.GlobalContext.Remove(CoreServiceKeys.SelectionViewKey.Name);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => context.Bridge.SubmitMoveCommandForTests(new Vector2(1600f, 1800f)))!;

            Assert.That(ex.Message, Does.Contain("current selection container"));
            Assert.That(context.World.Get<OrderBuffer>(context.Agent).HasActive, Is.False);
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
            int layerIndex = LayerRegistry.Register(MassNavigationLayerNames.Agent);
            var agentLayer = new MassNavigationAgentLayer(1u << layerIndex, 1u << layerIndex);
            simulation.MassFlow.Reset(new[] { 1, 2 }, unitsPerTeam: 1, config.World!.Obstacles, config.AgentProfiles, agentLayer);
            simulation.AgentState.RegisterAgentAtIndex(agent, agentIndex: 0, controllable: true);

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
                AgentProfiles = new MassNavigationAgentProfileSetConfig
                {
                    DefaultProfileId = "light",
                    Profiles = new[]
                    {
                        new MassNavigationAgentProfileConfig
                        {
                            Id = "light",
                            Heavy = false,
                            NavMass = 1f,
                            VisualScale = 0.22f,
                            BodyRadiusCm = 20f,
                            SpeedCmPerSecond = 800f,
                            EveryNth = 0,
                            NthOffset = 0,
                        },
                    },
                },
            };
            config.World.Validate();
            config.Scenario.Validate();
            config.AgentProfiles.Validate();
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
