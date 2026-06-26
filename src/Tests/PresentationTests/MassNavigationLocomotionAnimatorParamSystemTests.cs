using Arch.Core;
using Ludots.Core.MassCrowd.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationLocomotionAnimatorParamSystemTests
    {
        [Test]
        public void Runtime_ResolvesMovingAndIdleAgentLocomotionSpeed()
        {
            using World world = World.Create();
            MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            var layer = new MassNavigationAgentLayer(1u, 1u);

            Entity movingAgent = world.Create();
            Entity idleAgent = world.Create();
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
    }
}
