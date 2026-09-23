using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationLocomotionBlackboardSyncSystemTests
    {
        [Test]
        public void MassNavigationSimulationRuntime_MovingAndIdleAgents_ResolvesLocomotionSpeed()
        {
            using World world = World.Create();
            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            var layer = new MassNavigationAgentLayer(1u, 1u);
            int profileId = MassNavigationProfileRegistry.Register("light");

            Entity movingAgent = world.Create(new MassNavigationAgent { ProfileId = profileId });
            Entity idleAgent = world.Create(new MassNavigationAgent { ProfileId = profileId });
            simulation.RebuildFromAuthoredAgents(
                world,
                new[] { movingAgent, idleAgent },
                new[]
                {
                    new MassNavigationAgentSeed(
                        teamId: 1,
                        localPositionXCm: 100f,
                        localPositionYCm: 100f,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        layer),
                    new MassNavigationAgentSeed(
                        teamId: 1,
                        localPositionXCm: 300f,
                        localPositionYCm: 100f,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        layer),
                },
                new[] { true, true });
            simulation.SetAgentNavigationTargetLocalCm(0, simulation.GetAgentLocalPositionCm(0).X + 800f, simulation.GetAgentLocalPositionCm(0).Y);
            simulation.StepNavigationForTests(world, 1f);

            Assert.That(simulation.TryGetAgentLocomotionSpeedNormalized(0, out float movingSpeed), Is.True);
            Assert.That(movingSpeed, Is.GreaterThan(0f));
            Assert.That(simulation.TryGetAgentLocomotionSpeedNormalized(1, out float idleSpeed), Is.True);
            Assert.That(idleSpeed, Is.EqualTo(0f).Within(0.001f));
            Assert.That(simulation.TryGetAgentLocomotionSpeedNormalized(2, out _), Is.False);
        }

        [Test]
        public void AgentBinding_InitializesLocomotionSpeedBlackboardFact()
        {
            using World world = World.Create();
            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            var layer = new MassNavigationAgentLayer(1u, 1u);
            int profileId = MassNavigationProfileRegistry.Register("light");

            Entity agent = world.Create(new MassNavigationAgent { ProfileId = profileId });
            simulation.RebuildFromAuthoredAgents(
                world,
                new[] { agent },
                new[]
                {
                    new MassNavigationAgentSeed(
                        teamId: 1,
                        localPositionXCm: 100f,
                        localPositionYCm: 100f,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        layer),
                },
                new[] { true });

            Assert.That(world.Has<BlackboardFloatBuffer>(agent), Is.True);
            Assert.That(
                world.Get<BlackboardFloatBuffer>(agent).TryGet(MassNavigationBlackboardKeys.AgentLocomotionSpeed, out float speed),
                Is.True);
            Assert.That(speed, Is.EqualTo(0f));
        }

        [Test]
        public void MassNavigationLocomotionBlackboardSyncSystem_WritesOwnerBlackboardSpeed()
        {
            using var engine = new GameEngine();
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                Path.Combine(repoRoot, "assets"));
            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            var mapId = new MapId(config.MapId);
            engine.SetCurrentMapSessionForTests(new MapSession(mapId, new MapConfig { Id = config.MapId }));
            var binding = new MassNavigationRuntimeBinding();
            binding.Activate(mapId, simulation);
            binding.MarkPrepared(mapId, simulation);
            engine.SetService(MassNavigationKeys.RuntimeBinding, binding);
            World world = engine.World;
            var layer = new MassNavigationAgentLayer(1u, 1u);
            int profileId = MassNavigationProfileRegistry.Register("light");

            Entity movingAgent = world.Create(new MassNavigationAgent { ProfileId = profileId });
            Entity idleAgent = world.Create(new MassNavigationAgent { ProfileId = profileId });
            simulation.RebuildFromAuthoredAgents(
                world,
                new[] { movingAgent, idleAgent },
                new[]
                {
                    new MassNavigationAgentSeed(
                        teamId: 1,
                        localPositionXCm: 100f,
                        localPositionYCm: 100f,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        layer),
                    new MassNavigationAgentSeed(
                        teamId: 1,
                        localPositionXCm: 300f,
                        localPositionYCm: 100f,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        layer),
                },
                new[] { true, true });
            simulation.SetAgentNavigationTargetLocalCm(0, simulation.GetAgentLocalPositionCm(0).X + 800f, simulation.GetAgentLocalPositionCm(0).Y);
            simulation.StepNavigationForTests(world, 1f);

            var system = new MassNavigationLocomotionBlackboardSyncSystem(engine);
            system.Update(0f);

            ref BlackboardFloatBuffer movingBlackboard = ref world.Get<BlackboardFloatBuffer>(movingAgent);
            ref BlackboardFloatBuffer idleBlackboard = ref world.Get<BlackboardFloatBuffer>(idleAgent);
            Assert.That(movingBlackboard.TryGet(MassNavigationBlackboardKeys.AgentLocomotionSpeed, out float movingSpeed), Is.True);
            Assert.That(movingSpeed, Is.GreaterThan(0f));
            Assert.That(idleBlackboard.TryGet(MassNavigationBlackboardKeys.AgentLocomotionSpeed, out float idleSpeed), Is.True);
            Assert.That(idleSpeed, Is.EqualTo(0f).Within(0.001f));
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}

