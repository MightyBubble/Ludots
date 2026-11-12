using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation.MassNavigation;

[TestFixture]
public sealed class MassNavigationSolverWindowRebaseTests
{
    [Test]
    public void OrderFocus_DoesNotJumpWindowWhenMembersRemainInsidePlayArea()
    {
        using WindowHarness harness = WindowHarness.Create();
        Vector2 worldBefore = harness.Runtime.GetAgentWorldPositionCm(0);
        float centerX = harness.Runtime.SolverWindowCenterXCm;
        float centerY = harness.Runtime.SolverWindowCenterYCm;
        int movesBefore = harness.Runtime.SolverWindowMovesTotal;
        Fix64Vec2 publishedBefore = harness.World.Get<WorldPositionCm>(harness.Agent).Value;
        Fix64Vec2 previousBefore = harness.World.Get<PreviousWorldPositionCm>(harness.Agent).Value;

        harness.Runtime.FocusOrderTarget(new Vector2(worldBefore.X + 14_000f, worldBefore.Y), new[] { harness.Agent });

        Assert.That(harness.Runtime.SolverWindowCenterXCm, Is.EqualTo(centerX).Within(0.5f));
        Assert.That(harness.Runtime.SolverWindowCenterYCm, Is.EqualTo(centerY).Within(0.5f));
        Assert.That(harness.Runtime.SolverWindowMovesTotal, Is.EqualTo(movesBefore));
        Vector2 worldAfter = harness.Runtime.GetAgentWorldPositionCm(0);
        Assert.That(worldAfter.X, Is.EqualTo(worldBefore.X).Within(0.5f));
        Assert.That(worldAfter.Y, Is.EqualTo(worldBefore.Y).Within(0.5f));
        Assert.That(harness.World.Get<WorldPositionCm>(harness.Agent).Value.X.ToFloat(), Is.EqualTo(publishedBefore.X.ToFloat()).Within(1f));
        Assert.That(harness.World.Get<PreviousWorldPositionCm>(harness.Agent).Value.X.ToFloat(), Is.EqualTo(previousBefore.X.ToFloat()).Within(1f));
    }

    [Test]
    public void SolverWindowMove_KeepsPublishedWorldAndInterpolationSpan()
    {
        using WindowHarness harness = WindowHarness.Create();
        Vector2 worldBefore = harness.Runtime.GetAgentWorldPositionCm(0);
        float previousX = harness.World.Get<PreviousWorldPositionCm>(harness.Agent).Value.X.ToFloat();
        float currentX = harness.World.Get<WorldPositionCm>(harness.Agent).Value.X.ToFloat();
        float motionX = currentX - previousX;
        float centerBefore = harness.Runtime.SolverWindowCenterXCm;

        harness.Runtime.FocusSimulationWindow(new Vector2(centerBefore + 15_000f, harness.Runtime.SolverWindowCenterYCm));

        Vector2 worldAfter = harness.Runtime.GetAgentWorldPositionCm(0);
        float publishedX = harness.World.Get<WorldPositionCm>(harness.Agent).Value.X.ToFloat();
        float publishedPreviousX = harness.World.Get<PreviousWorldPositionCm>(harness.Agent).Value.X.ToFloat();
        Assert.That(harness.Runtime.SolverWindowCenterXCm - centerBefore, Is.GreaterThan(harness.Runtime.SolverWindowWidthCm));
        Assert.That(worldAfter.X, Is.EqualTo(worldBefore.X).Within(0.5f));
        Assert.That(worldAfter.Y, Is.EqualTo(worldBefore.Y).Within(0.5f));
        Assert.That(publishedX, Is.EqualTo(worldBefore.X).Within(1f));
        Assert.That(publishedX - publishedPreviousX, Is.EqualTo(motionX).Within(1f));
        Assert.That(MathF.Abs(publishedX - publishedPreviousX), Is.LessThan(100f));
    }

    [Test]
    public void OrderCoverage_PullsWindowBackOnlyUntilMembersReenterPlayArea()
    {
        using WindowHarness harness = WindowHarness.Create();
        Vector2 worldBefore = harness.Runtime.GetAgentWorldPositionCm(0);
        harness.Runtime.FocusOrderTarget(new Vector2(worldBefore.X + 14_000f, worldBefore.Y), new[] { harness.Agent });
        float nudgedCenterX = harness.Runtime.SolverWindowCenterXCm + 4_960f;
        harness.Runtime.FocusSimulationWindow(new Vector2(nudgedCenterX, harness.Runtime.SolverWindowCenterYCm));
        Assert.That(harness.Runtime.MassNavigationFlow.GetPositionX(0), Is.LessThan(harness.Runtime.MassNavigationFlow.PlayAreaMinXCm));

        harness.Runtime.MaintainOrderCoverageWindow();

        float localX = harness.Runtime.MassNavigationFlow.GetPositionX(0);
        Vector2 worldAfter = harness.Runtime.GetAgentWorldPositionCm(0);
        Assert.That(localX, Is.GreaterThanOrEqualTo(harness.Runtime.MassNavigationFlow.PlayAreaMinXCm - 0.5f));
        Assert.That(localX, Is.LessThanOrEqualTo(harness.Runtime.MassNavigationFlow.PlayAreaMaxXCm + 0.5f));
        Assert.That(worldAfter.X, Is.EqualTo(worldBefore.X).Within(0.5f));
        Assert.That(harness.World.Get<WorldPositionCm>(harness.Agent).Value.X.ToFloat(), Is.EqualTo(worldBefore.X).Within(1f));
        Assert.That(harness.Runtime.SolverWindowCenterXCm, Is.LessThan(nudgedCenterX - 1f));
        Assert.That(harness.Runtime.SolverWindowDriver, Is.EqualTo("order members"));
    }

    private sealed class WindowHarness : IDisposable
    {
        public MassNavigationSimulationRuntime Runtime { get; }
        public World World { get; }
        public Entity Agent { get; }

        private WindowHarness(MassNavigationSimulationRuntime runtime, World world, Entity agent)
        {
            Runtime = runtime;
            World = world;
            Agent = agent;
        }

        public static WindowHarness Create()
        {
            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            config.Streaming.RetainSeconds = 0f;
            config.Streaming.RadiusCm = 1_000;
            config.ScenarioRuntime.RuntimeCapacity.LoadedChunkCapacity = 128;
            var runtime = new MassNavigationSimulationRuntime(config);
            var world = World.Create();
            runtime.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(0, 0, 40_000, 40_000), 100),
                MassNavigationOrderChainTests.CreateLoadedChunksForTests(runtime));
            int profileId = MassNavigationProfileRegistry.Register("light");
            var agent = world.Create(
                new MassNavigationAgent { ProfileId = profileId },
                new WorldPositionCm(),
                new PreviousWorldPositionCm());
            var seed = new MassNavigationAgentSeed(
                relationshipDomainId: MassNavigationOrderChainTests.LocalTeamId,
                localPositionXCm: 5_000f,
                localPositionYCm: 5_000f,
                heavy: false,
                navMass: 1f,
                visualScale: 1f,
                bodyRadiusCm: 20f,
                speedCmPerSecond: 800f,
                new MassNavigationAgentLayer(1u, 1u));
            runtime.RebuildFromAuthoredAgents(world, new[] { agent }, new[] { seed }, new[] { true });
            Vector2 solverWorld = runtime.GetAgentWorldPositionCm(0);
            world.Set(agent, WorldPositionCm.FromCmFloat(solverWorld.X, solverWorld.Y));
            world.Set(agent, new PreviousWorldPositionCm
            {
                Value = Fix64Vec2.FromFloat(solverWorld.X - 30f, solverWorld.Y),
            });
            return new WindowHarness(runtime, world, agent);
        }

        public void Dispose()
        {
            World.Dispose();
        }
    }
}
