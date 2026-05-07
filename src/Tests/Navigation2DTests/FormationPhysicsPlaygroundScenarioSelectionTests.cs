extern alias formationplayground;

using Arch.Core;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Config;
using NUnit.Framework;
using FormationScenarioSpawner = formationplayground::Navigation2DPlaygroundMod.Systems.Navigation2DPlaygroundScenarioSpawner;
using FormationControllable = formationplayground::Navigation2DPlaygroundMod.Systems.NavPlaygroundControllable;
using FormationBlocker = formationplayground::Navigation2DPlaygroundMod.Systems.NavPlaygroundBlocker;
using FormationTeam = formationplayground::Navigation2DPlaygroundMod.Systems.NavPlaygroundTeam;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    public sealed class FormationPhysicsPlaygroundScenarioSelectionTests
    {
        private static readonly QueryDescription FriendlyFormationQuery = new QueryDescription()
            .WithAll<FormationControllable, FormationTeam, SelectionSelectableTag, SelectionSelectableState>()
            .WithNone<FormationBlocker>();

        private static readonly QueryDescription HostileFormationQuery = new QueryDescription()
            .WithAll<FormationTeam>()
            .WithNone<FormationControllable, FormationBlocker>();

        [Test]
        public void SpawnScenario_MarksControllableFormationAgentsAsSelectable()
        {
            var playgroundConfig = FormationScenarioSpawner.GetPlaygroundConfig(gameConfig: null);
            var scenario = playgroundConfig.Scenarios.Find(static entry => entry.Kind == Navigation2DPlaygroundScenarioKind.GoalQueue);

            Assert.That(scenario, Is.Not.Null, "FormationPhysicsPlaygroundMod should ship a controllable scenario.");

            using var world = World.Create();
            var summary = FormationScenarioSpawner.SpawnScenario(world, scenario!, agentsPerTeam: 9);

            int friendlySelectable = 0;
            world.Query(in FriendlyFormationQuery, (Entity entity, ref FormationControllable controllable, ref FormationTeam team, ref SelectionSelectableTag selectable, ref SelectionSelectableState state) =>
            {
                Assert.That(team.Id, Is.EqualTo(0), "Only controllable team-0 formations should advertise formal selection.");
                Assert.That(state.Enabled, Is.True, "Controllable formations should be selectable by default for box drag.");
                friendlySelectable++;
            });

            int hostileNonSelectable = 0;
            world.Query(in HostileFormationQuery, (Entity entity, ref FormationTeam team) =>
            {
                if (team.Id != 0)
                {
                    Assert.That(world.Has<SelectionSelectableTag>(entity), Is.False, "Hostile formations should not enter the local-player selection set.");
                    hostileNonSelectable++;
                }
            });

            Assert.That(summary.DynamicAgents, Is.GreaterThan(0));
            Assert.That(friendlySelectable, Is.EqualTo(summary.DynamicAgents), "Every spawned controllable formation agent should satisfy the box-selection contract.");
            Assert.That(hostileNonSelectable, Is.EqualTo(0), "GoalQueue only spawns player-controlled formation agents in this acceptance lane.");
        }
    }
}
