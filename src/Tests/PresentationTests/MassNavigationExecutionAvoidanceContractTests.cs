using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.Avoidance;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationExecutionAvoidanceContractTests
    {
        [Test]
        public void AvoidanceKernels_LiveInCoreNamespace()
        {
            Assert.That(typeof(OrcaSolver2D).Namespace, Is.EqualTo("Ludots.Core.Navigation.Avoidance"));
            Assert.That(typeof(SonarSolver2D).Namespace, Is.EqualTo("Ludots.Core.Navigation.Avoidance"));
            Assert.That(typeof(SonarSolver2D).GetNestedType("SolveConfig")!.GetField("FallbackToPreferredVelocity"), Is.Null);
        }

        [Test]
        public void AvoidanceKernelSources_DoNotExposeFallbackWording()
        {
            string root = FindRepoRoot();
            string sonarPath = Path.Combine(root, "src", "Core", "Navigation", "Avoidance", "SonarSolver2D.cs");
            string sonarSource = File.ReadAllText(sonarPath);

            Assert.That(sonarSource, Does.Not.Contain("FallbackToPreferredVelocity"));
            Assert.That(sonarSource, Does.Not.Contain("fallbackToPreferredVelocity"));
            Assert.That(sonarSource, Does.Contain("UsePreferredVelocityWhenBlocked"));
        }

        [Test]
        public void Runtime_PerAgentWorldTargetProducesArrivalEvent()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntime(world);
            Entity agent = runtime.AgentState.AllAgents[0];
            Vector2 start = runtime.GetAgentWorldPositionCm(0);
            Vector2 target = start + new Vector2(80f, 0f);

            Assert.That(runtime.SetAgentNavigationTargetWorldCm(agent, target, resetRecovery: true), Is.True);
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float targetX, out float targetY), Is.True);
            Assert.That(targetX, Is.EqualTo(target.X).Within(0.001f));
            Assert.That(targetY, Is.EqualTo(target.Y).Within(0.001f));

            Span<MassNavigationArrivalEvent> events = stackalloc MassNavigationArrivalEvent[4];
            int drained = StepUntilArrival(runtime, world, events);

            Assert.That(drained, Is.EqualTo(1));
            Assert.That(events[0].AgentIndex, Is.EqualTo(0));
            Assert.That(events[0].Agent, Is.EqualTo(agent));
            Assert.That(Vector2.Distance(new Vector2(events[0].WorldXCm, events[0].WorldYCm), target), Is.LessThan(125f));
        }

        [Test]
        public void Runtime_RuntimeObstacleStampRebuildsAndBlocksTargetProjection()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntime(world);
            Vector2 start = runtime.GetAgentWorldPositionCm(0);
            var obstacle = new MassNavigationObstacleSnapshot(start.X + 300f, start.Y, radiusCm: 180f);

            runtime.RebuildRuntimeObstacles(new[] { obstacle });
            Assert.That(runtime.NavigationObstacleCount, Is.EqualTo(1));

            Vector2 blockedLocal = new Vector2(runtime.ToLocalXCm(obstacle.WorldXCm), runtime.ToLocalYCm(obstacle.WorldYCm));
            Vector2 resolvedLocal = runtime.GetFlowSolverForTests().ResolveUnitNavigableTarget(
                0,
                blockedLocal.X,
                blockedLocal.Y,
                hintX: 1f,
                hintY: 0f,
                minimumClearanceCm: 0f);

            Assert.That(Vector2.Distance(resolvedLocal, blockedLocal), Is.GreaterThan(175f));
        }

        [TestCase("Orca")]
        [TestCase("Sonar")]
        public void Runtime_ConfiguredHighQualityAvoidanceModesStepWithoutLegacyDependency(string mode)
        {
            using var world = World.Create();
            MassNavigationConfig config = CreateConfig(mode);
            var runtime = new MassNavigationSimulationRuntime(config);
            runtime.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100));

            MassNavigationAgentSeed[] seeds =
            {
                new(
                    teamId: 1,
                    localPositionXCm: 4_900,
                    localPositionYCm: 5_000,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 40f,
                    speedCmPerSecond: 600f,
                    new MassNavigationAgentLayer(1u, 1u)),
                new(
                    teamId: 1,
                    localPositionXCm: 5_100,
                    localPositionYCm: 5_000,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 40f,
                    speedCmPerSecond: 600f,
                    new MassNavigationAgentLayer(1u, 1u)),
            };
            runtime.RebuildFromAuthoredAgents(world, CreateAgentEntities(world, seeds.Length), seeds, new[] { true, true });

            runtime.SetAgentNavigationTargetWorldCm(0, 5_900f, 5_000f, resetRecovery: true);
            runtime.SetAgentNavigationTargetWorldCm(1, 4_100f, 5_000f, resetRecovery: true);
            runtime.StepNavigationForTests(world, 0.05f, runHardResolve: true);

            Vector2 velocity0 = runtime.GetFlowSolverForTests().GetVelocityCmPerSecond(0);
            Vector2 velocity1 = runtime.GetFlowSolverForTests().GetVelocityCmPerSecond(1);
            Assert.That(velocity0.Length(), Is.GreaterThan(0f));
            Assert.That(velocity1.Length(), Is.GreaterThan(0f));
        }

        private static int StepUntilArrival(MassNavigationSimulationRuntime runtime, World world, Span<MassNavigationArrivalEvent> events)
        {
            for (int i = 0; i < 240; i++)
            {
                runtime.StepNavigationForTests(world, 0.05f, runHardResolve: true);
                int drained = runtime.DrainArrivalEvents(events);
                if (drained > 0)
                {
                    return drained;
                }
            }

            return 0;
        }

        private static MassNavigationSimulationRuntime CreateRuntime(World world)
        {
            MassNavigationConfig config = CreateConfig("Separation");
            var runtime = new MassNavigationSimulationRuntime(config);
            runtime.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100));
            MassNavigationAgentSeed[] seeds =
            {
                new(
                    teamId: 1,
                    localPositionXCm: 5_000,
                    localPositionYCm: 5_000,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 900f,
                    new MassNavigationAgentLayer(1u, 1u)),
            };
            runtime.RebuildFromAuthoredAgents(world, CreateAgentEntities(world, seeds.Length), seeds, new[] { true });
            return runtime;
        }

        private static Entity[] CreateAgentEntities(World world, int count)
        {
            var entities = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                entities[i] = world.Create();
            }

            return entities;
        }

        private static MassNavigationConfig CreateConfig(string avoidanceMode)
        {
            MassNavigationFlowSolverConfig solver = new()
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

            var config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
            config.Solver = solver;
            config.World!.SolverWindowWidthCm = solver.FieldWidthCm;
            config.World.SolverWindowHeightCm = solver.FieldHeightCm;
            config.Avoidance.Mode = avoidanceMode;
            config.Avoidance.Validate();
            config.Solver.Validate();
            config.World.Validate(config.Solver);
            config.AgentProfiles.BindAgentProfiles(new AgentProfileRegistry(new[]
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
            }));
            return config;
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
    }

}
