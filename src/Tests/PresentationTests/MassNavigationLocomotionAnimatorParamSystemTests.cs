using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using MassNavigationMod.Runtime;
using MassNavigationMod.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationLocomotionAnimatorParamSystemTests
    {
        [Test]
        public void Update_WritesMovingAndIdleAgentSpeedIntoPerformerFloatParams()
        {
            using World world = World.Create();
            MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            var layer = new MassNavigationAgentLayer(1u, 1u);
            simulation.MassFlow.Reset(
                new[] { 1 },
                unitsPerTeam: 2,
                config.World!.Obstacles,
                config.AgentProfiles,
                layer,
                config.Scenario.SpawnLayout);
            simulation.MassFlow.SetUnitTarget(0, simulation.MassFlow.GetPositionX(0) + 800f, simulation.MassFlow.GetPositionY(0));
            simulation.MassFlow.Step(
                1f,
                world,
                simulation.NavGroupRuntime,
                runHardResolve: false,
                hardResolveCandidateThresholdAgents: 1);

            Entity movingOwner = world.Create(new MassNavigationAgentIndex { Value = 0 }, new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity idleOwner = world.Create(new MassNavigationAgentIndex { Value = 1 }, new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity culledOwner = world.Create(new MassNavigationAgentIndex { Value = 0 }, new CullState { IsVisible = false, LOD = LODLevel.Culled });
            Entity movingPerformer = CreatePerformer(world, movingOwner);
            Entity idlePerformer = CreatePerformer(world, idleOwner);
            Entity culledPerformer = CreatePerformer(world, culledOwner, ownerCullVisible: false);
            int speedParamKey = MassNavigationSimulationRuntime.ResolveAgentLocomotionSpeedParamKey();

            var system = new MassNavigationLocomotionAnimatorParamSystem(world, simulation);
            system.Update(0.016f);

            Assert.That(world.Get<PerformerFloatParams>(movingPerformer).TryGet(speedParamKey, out float movingSpeed), Is.True);
            Assert.That(movingSpeed, Is.GreaterThan(0f));
            float expectedMovingSpeed = simulation.MassFlow.GetVelocityCmPerSecond(0).Length() /
                                        simulation.MassFlow.GetSpeedCmPerSecond(0);
            Assert.That(movingSpeed, Is.EqualTo(expectedMovingSpeed).Within(0.001f));
            Assert.That(world.Get<PerformerFloatParams>(idlePerformer).TryGet(speedParamKey, out float idleSpeed), Is.True);
            Assert.That(idleSpeed, Is.EqualTo(0f).Within(0.001f));
            Assert.That(world.Get<PerformerFloatParams>(culledPerformer).TryGet(speedParamKey, out _), Is.False);
        }

        private static Entity CreatePerformer(World world, Entity owner, bool ownerCullVisible = true)
        {
            return world.Create(
                new PerformerState
                {
                    OwnerEntity = owner,
                    AnchorKind = PresentationAnchorKind.Entity,
                },
                new PerformerFloatParams(),
                new PerformerCullState
                {
                    OwnerCullVisible = ownerCullVisible,
                    LOD = ownerCullVisible ? LODLevel.High : LODLevel.Culled,
                });
        }
    }
}
