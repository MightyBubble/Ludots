using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Layers;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationAuthoredAgentBindingIncrementalTests
    {
        private const int TeamId = 1;

        [Test]
        public void AppendAuthoredAgents_PreservesActiveMoveGroupState()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime simulation = CreateSimulation(
                world,
                out Entity agent0,
                out Entity agent1,
                out MassNavigationAgentLayer layer);
            simulation.SetSelection(new[] { agent0, agent1 }, revision: 1);
            Vector2 moveDestination = new(2000f, 2000f);
            int movedCount = simulation.NavGroupRuntime.IssueSelectionMoveCommand(
                simulation.MassNavigationFlow,
                world,
                simulation.AgentState,
                simulation.SelectedEntities,
                moveDestination,
                MassNavigationFormationMode.Square);
            Assert.That(movedCount, Is.EqualTo(2));
            Assert.That(simulation.NavGroupRuntime.ActiveGroupCount, Is.EqualTo(1));
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float orderTargetX, out float orderTargetY), Is.True);
            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(0, out float target0X, out float target0Y), Is.True);

            Entity newAgent = CreateAuthoredAgentEntity(world, localX: 1500f, localY: 1500f, layer);
            var newSeed = CreateSeed(localX: 1500f, localY: 1500f, layer);
            simulation.AppendAuthoredAgents(world, new[] { newAgent }, new[] { newSeed }, new[] { true });

            Assert.That(simulation.NavGroupRuntime.ActiveGroupCount, Is.EqualTo(1));
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float preservedOrderX, out float preservedOrderY), Is.True);
            Assert.That(preservedOrderX, Is.EqualTo(orderTargetX).Within(0.001f));
            Assert.That(preservedOrderY, Is.EqualTo(orderTargetY).Within(0.001f));
            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(0, out float preservedTarget0X, out float preservedTarget0Y), Is.True);
            Assert.That(preservedTarget0X, Is.EqualTo(target0X).Within(0.001f));
            Assert.That(preservedTarget0Y, Is.EqualTo(target0Y).Within(0.001f));
            Assert.That(simulation.AgentState.TotalAgents, Is.EqualTo(3));
            Assert.That(world.Has<MassNavigationAgentIndex>(newAgent), Is.True);
        }

        [Test]
        public void AuthoredAgentBindingSystem_IncrementalInsert_PreservesActiveMoveGroupState()
        {
            using BindingHarness harness = CreateBindingHarness(membershipCapacity: 8);
            harness.BindingSystem.Update(0f);
            Assert.That(harness.Simulation.AgentState.TotalAgents, Is.EqualTo(2));

            harness.Simulation.SetSelection(new[] { harness.Agent0, harness.Agent1 }, revision: 1);
            Vector2 moveDestination = new(2000f, 2000f);
            int movedCount = harness.Simulation.NavGroupRuntime.IssueSelectionMoveCommand(
                harness.Simulation.MassNavigationFlow,
                harness.Engine.World,
                harness.Simulation.AgentState,
                harness.Simulation.SelectedEntities,
                moveDestination,
                MassNavigationFormationMode.Square);
            Assert.That(movedCount, Is.EqualTo(2));
            Assert.That(harness.Simulation.NavGroupRuntime.ActiveGroupCount, Is.EqualTo(1));
            Assert.That(harness.Simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float orderTargetX, out float orderTargetY), Is.True);

            Entity newAgent = CreateAuthoredAgentEntity(harness.Engine.World, localX: 1500f, localY: 1500f, harness.Layer);
            harness.BindingSystem.Update(0f);

            Assert.That(harness.Simulation.NavGroupRuntime.ActiveGroupCount, Is.EqualTo(1));
            Assert.That(harness.Simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float preservedOrderX, out float preservedOrderY), Is.True);
            Assert.That(preservedOrderX, Is.EqualTo(orderTargetX).Within(0.001f));
            Assert.That(preservedOrderY, Is.EqualTo(orderTargetY).Within(0.001f));
            Assert.That(harness.Simulation.AgentState.TotalAgents, Is.EqualTo(3));
            Assert.That(harness.Engine.World.Has<MassNavigationAgentIndex>(newAgent), Is.True);
        }

        [Test]
        public void AppendAuthoredAgents_DoesNotBumpAuthoredRuntimeBindingRevision()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime simulation = CreateSimulation(
                world,
                out _,
                out _,
                out MassNavigationAgentLayer layer);
            int revisionBeforeAppend = simulation.AuthoredRuntimeBindingRevision;

            Entity newAgent = CreateAuthoredAgentEntity(world, localX: 1500f, localY: 1500f, layer);
            var newSeed = CreateSeed(localX: 1500f, localY: 1500f, layer);
            simulation.AppendAuthoredAgents(world, new[] { newAgent }, new[] { newSeed }, new[] { true });

            Assert.That(simulation.AuthoredRuntimeBindingRevision, Is.EqualTo(revisionBeforeAppend));
            Assert.That(simulation.StructuralChangeRevision, Is.GreaterThan(0));
        }

        [Test]
        public void AuthoredAgentBindingSystem_IncrementalInsert_DoesNotBumpAuthoredRuntimeBindingRevision()
        {
            using BindingHarness harness = CreateBindingHarness(membershipCapacity: 8);
            harness.BindingSystem.Update(0f);
            int revisionBeforeInsert = harness.Simulation.AuthoredRuntimeBindingRevision;

            CreateAuthoredAgentEntity(harness.Engine.World, localX: 1500f, localY: 1500f, harness.Layer);
            harness.BindingSystem.Update(0f);

            Assert.That(harness.Simulation.AuthoredRuntimeBindingRevision, Is.EqualTo(revisionBeforeInsert));
            Assert.That(harness.Simulation.AgentState.TotalAgents, Is.EqualTo(3));
        }

        [Test]
        public void AppendAuthoredAgents_ExceedsMembershipCapacity_Throws()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime simulation = CreateSimulation(
                world,
                out _,
                out _,
                out MassNavigationAgentLayer layer,
                membershipCapacity: 2);
            Entity newAgent = CreateAuthoredAgentEntity(world, localX: 900f, localY: 900f, layer);
            var seed = CreateSeed(localX: 900f, localY: 900f, layer);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                simulation.AppendAuthoredAgents(world, new[] { newAgent }, new[] { seed }, new[] { true }));
            Assert.That(ex!.Message, Does.Contain("groupMembershipAgentCapacity"));
        }

        private static MassNavigationSimulationRuntime CreateSimulation(
            World world,
            out Entity agent0,
            out Entity agent1,
            out MassNavigationAgentLayer layer,
            int membershipCapacity = 16)
        {
            MassNavigationSimulationRuntime simulation = CreateConfiguredSimulation(CreateTestConfig(membershipCapacity));
            layer = CreateAgentLayer();
            MassNavigationAgentSeed[] seeds =
            {
                CreateSeed(localX: 1000f, localY: 1000f, layer),
                CreateSeed(localX: 1200f, localY: 1000f, layer),
            };
            agent0 = CreateAuthoredAgentEntity(world, localX: 1000f, localY: 1000f, layer);
            agent1 = CreateAuthoredAgentEntity(world, localX: 1200f, localY: 1000f, layer);
            simulation.RebuildFromAuthoredAgents(world, new[] { agent0, agent1 }, seeds, new[] { true, true });
            return simulation;
        }

        private static BindingHarness CreateBindingHarness(int membershipCapacity)
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(FindRepoRoot(), "mods", "LudotsCoreMod") },
                Path.Combine(FindRepoRoot(), "assets"));

            MassNavigationConfig config = CreateTestConfig(membershipCapacity);
            var simulation = CreateConfiguredSimulation(config);
            simulation.SetWorldOperationsReady(true);
            engine.SetService(MassNavigationKeys.SimulationRuntime, simulation);
            engine.SetCurrentMapSessionForTests(new MapSession(new MapId(config.MapId), new MapConfig { Id = config.MapId }));

            MassNavigationAgentLayer layer = CreateAgentLayer();
            Entity agent0 = CreateAuthoredAgentEntity(engine.World, localX: 1000f, localY: 1000f, layer);
            Entity agent1 = CreateAuthoredAgentEntity(engine.World, localX: 1200f, localY: 1000f, layer);
            var bindingSystem = new MassNavigationAuthoredAgentBindingSystem(engine, simulation);
            return new BindingHarness(engine, simulation, bindingSystem, layer, agent0, agent1);
        }

        private static MassNavigationConfig CreateTestConfig(int membershipCapacity)
        {
            MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
            config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = membershipCapacity;
            config.ScenarioRuntime.RuntimeCapacity.SelectionMemberScratchCapacity = membershipCapacity;
            config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity = membershipCapacity;
            config.ScenarioRuntime.RuntimeCapacity.OrderIngestionMemberCapacity = membershipCapacity;
            return config;
        }

        private static MassNavigationSimulationRuntime CreateConfiguredSimulation(MassNavigationConfig config)
        {
            var simulation = new MassNavigationSimulationRuntime(config);
            simulation.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100));
            return simulation;
        }

        private static MassNavigationAgentLayer CreateAgentLayer()
        {
            int layerIndex = LayerRegistry.Register(MassNavigationLayerNames.Agent);
            uint mask = 1u << layerIndex;
            return new MassNavigationAgentLayer(mask, mask);
        }

        private static MassNavigationAgentSeed CreateSeed(float localX, float localY, MassNavigationAgentLayer layer)
        {
            return new MassNavigationAgentSeed(
                teamId: TeamId,
                localPositionXCm: localX,
                localPositionYCm: localY,
                heavy: false,
                navMass: 1f,
                visualScale: 1f,
                bodyRadiusCm: 20f,
                speedCmPerSecond: 800f,
                layer);
        }

        private static Entity CreateAuthoredAgentEntity(World world, float localX, float localY, MassNavigationAgentLayer layer, bool controllable = true)
        {
            int profileId = MassNavigationProfileRegistry.Register("light");
            if (controllable)
            {
                return world.Create(
                    new MassNavigationAgent { ProfileId = profileId },
                    new Team { Id = TeamId },
                    WorldPositionCm.FromCmFloat(localX, localY),
                    new EntityLayer(layer.CategoryMask, layer.InteractionMask),
                    new FacingDirection { AngleRad = 0f },
                    OrderBuffer.CreateEmpty());
            }

            return world.Create(
                new MassNavigationAgent { ProfileId = profileId },
                new Team { Id = TeamId },
                WorldPositionCm.FromCmFloat(localX, localY),
                new EntityLayer(layer.CategoryMask, layer.InteractionMask),
                new FacingDirection { AngleRad = 0f });
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

        private sealed class BindingHarness : IDisposable
        {
            public BindingHarness(
                GameEngine engine,
                MassNavigationSimulationRuntime simulation,
                MassNavigationAuthoredAgentBindingSystem bindingSystem,
                MassNavigationAgentLayer layer,
                Entity agent0,
                Entity agent1)
            {
                Engine = engine;
                Simulation = simulation;
                BindingSystem = bindingSystem;
                Layer = layer;
                Agent0 = agent0;
                Agent1 = agent1;
            }

            public GameEngine Engine { get; }
            public MassNavigationSimulationRuntime Simulation { get; }
            public MassNavigationAuthoredAgentBindingSystem BindingSystem { get; }
            public MassNavigationAgentLayer Layer { get; }
            public Entity Agent0 { get; }
            public Entity Agent1 { get; }

            public void Dispose()
            {
                BindingSystem.Dispose();
                Engine.Dispose();
            }
        }
    }
}
