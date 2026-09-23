using Arch.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using MassNavigationPresentationAdapter;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
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
        public void MassNavigationPresentationAdapter_VisibleOwnedPresenter_WritesNormalizedSpeed()
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

            Entity movingPresenter = world.Create(
                new PresenterState { OwnerEntity = movingAgent, Version = 10 },
                new PresenterFloatParams(),
                new PresenterCullState { OwnerCullVisible = true });
            Entity idlePresenter = world.Create(
                new PresenterState { OwnerEntity = idleAgent, Version = 20 },
                new PresenterFloatParams(),
                new PresenterCullState { OwnerCullVisible = true });
            Entity culledPresenter = world.Create(
                new PresenterState { OwnerEntity = movingAgent, Version = 30 },
                new PresenterFloatParams(),
                new PresenterCullState { OwnerCullVisible = false });

            var system = new MassNavigationLocomotionAnimatorParamSystem(engine);
            system.Update(0f);

            int speedParamKey = MassNavigationPresentationAdapterIds.AgentLocomotionSpeedParam;
            ref PresenterFloatParams movingParams = ref world.Get<PresenterFloatParams>(movingPresenter);
            ref PresenterFloatParams idleParams = ref world.Get<PresenterFloatParams>(idlePresenter);
            ref PresenterFloatParams culledParams = ref world.Get<PresenterFloatParams>(culledPresenter);
            Assert.That(movingParams.TryGet(speedParamKey, out float movingSpeed), Is.True);
            Assert.That(movingSpeed, Is.GreaterThan(0f));
            Assert.That(idleParams.TryGet(speedParamKey, out float idleSpeed), Is.True);
            Assert.That(idleSpeed, Is.EqualTo(0f).Within(0.001f));
            Assert.That(culledParams.TryGet(speedParamKey, out _), Is.False);
            Assert.That(world.Get<PresenterState>(movingPresenter).Version, Is.EqualTo(11));
            Assert.That(world.Get<PresenterState>(idlePresenter).Version, Is.EqualTo(21));
            Assert.That(world.Get<PresenterState>(culledPresenter).Version, Is.EqualTo(30));

            system.Update(0f);

            Assert.That(world.Get<PresenterState>(movingPresenter).Version, Is.EqualTo(11));
            Assert.That(world.Get<PresenterState>(idlePresenter).Version, Is.EqualTo(21));
        }

        [Test]
        public void MassNavigationPresentationAdapter_Installer_IsIdempotentAndRunsBeforeAnimator()
        {
            using var engine = new GameEngine();
            string repoRoot = FindRepoRoot();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
                Path.Combine(repoRoot, "assets"));

            MassNavigationPresentationAdapterInstaller.EnsureLocomotionAnimatorParams(engine);
            MassNavigationPresentationAdapterInstaller.EnsureLocomotionAnimatorParams(engine);

            IReadOnlyList<ISystem<float>> systems = GetPresentationSystems(engine);
            int adapterCount = 0;
            int adapterIndex = -1;
            int animatorIndex = -1;
            for (int i = 0; i < systems.Count; i++)
            {
                if (systems[i] is MassNavigationLocomotionAnimatorParamSystem)
                {
                    adapterCount++;
                    adapterIndex = i;
                }

                if (systems[i] is AnimatorRuntimeSystem)
                {
                    animatorIndex = i;
                }
            }

            Assert.That(adapterCount, Is.EqualTo(1));
            Assert.That(adapterIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(animatorIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(adapterIndex, Is.LessThan(animatorIndex));
        }

        private static IReadOnlyList<ISystem<float>> GetPresentationSystems(GameEngine engine)
        {
            FieldInfo field = typeof(GameEngine).GetField(
                "_presentationSystems",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GameEngine._presentationSystems field missing.");
            return (IReadOnlyList<ISystem<float>>)field.GetValue(engine)!;
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
