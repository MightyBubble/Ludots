using Arch.Core;
using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Presentation.Performers;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationLocomotionAnimatorParamSystemTests
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
        public void MassNavigationLocomotionAnimatorParamSystem_VisibleOwnedPerformer_WritesNormalizedSpeed()
        {
            using var engine = new GameEngine();
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                Path.Combine(repoRoot, "assets"));
            World world = engine.World;
            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            var mapId = new MapId(config.MapId);
            engine.SetCurrentMapSessionForTests(new MapSession(mapId, new MapConfig { Id = config.MapId }));
            var binding = new MassNavigationRuntimeBinding();
            binding.Activate(mapId, simulation);
            binding.MarkPrepared(mapId, simulation);
            engine.SetService(MassNavigationKeys.RuntimeBinding, binding);
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

            Entity movingPerformer = world.Create(
                new PerformerState { OwnerEntity = movingAgent, Version = 10 },
                new PerformerFloatParams(),
                new PerformerCullState { OwnerCullVisible = true });
            Entity idlePerformer = world.Create(
                new PerformerState { OwnerEntity = idleAgent, Version = 20 },
                new PerformerFloatParams(),
                new PerformerCullState { OwnerCullVisible = true });
            Entity culledPerformer = world.Create(
                new PerformerState { OwnerEntity = movingAgent, Version = 30 },
                new PerformerFloatParams(),
                new PerformerCullState { OwnerCullVisible = false });

            var system = new MassNavigationLocomotionAnimatorParamSystem(engine);
            system.Update(0f);

            int speedParamKey = MassNavigationSimulationRuntime.ResolveAgentLocomotionSpeedParamKey();
            ref PerformerFloatParams movingParams = ref world.Get<PerformerFloatParams>(movingPerformer);
            ref PerformerFloatParams idleParams = ref world.Get<PerformerFloatParams>(idlePerformer);
            ref PerformerFloatParams culledParams = ref world.Get<PerformerFloatParams>(culledPerformer);
            Assert.That(movingParams.TryGet(speedParamKey, out float movingSpeed), Is.True);
            Assert.That(movingSpeed, Is.GreaterThan(0f));
            Assert.That(idleParams.TryGet(speedParamKey, out float idleSpeed), Is.True);
            Assert.That(idleSpeed, Is.EqualTo(0f).Within(0.001f));
            Assert.That(culledParams.TryGet(speedParamKey, out _), Is.False);
            Assert.That(world.Get<PerformerState>(movingPerformer).Version, Is.EqualTo(11));
            Assert.That(world.Get<PerformerState>(idlePerformer).Version, Is.EqualTo(21));
            Assert.That(world.Get<PerformerState>(culledPerformer).Version, Is.EqualTo(30));

            system.Update(0f);

            Assert.That(world.Get<PerformerState>(movingPerformer).Version, Is.EqualTo(11));
            Assert.That(world.Get<PerformerState>(idlePerformer).Version, Is.EqualTo(21));
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
