using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>点名生成的组下标走玩家表序，不按玩家号减一。</summary>
public sealed class CrowdSpawnPlayerIndexTests
{
    [Test]
    public void SpawnAt_GroupPlayer_MatchesIndexByPlayerId()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));

        var map = new MapConfig
        {
            Id = "player_perm",
            Players = new List<PlayerBindingData>
            {
                new() { PlayerId = 3, TeamId = 1, RepresentativeInstanceId = "p3" },
                new() { PlayerId = 1, TeamId = 1, RepresentativeInstanceId = "p1" },
                new() { PlayerId = 4, TeamId = 2, RepresentativeInstanceId = "p4" },
                new() { PlayerId = 2, TeamId = 2, RepresentativeInstanceId = "p2" },
            },
        };
        session.Config.PairRules.BindPlayerIndex(map);
        const int player = 3;
        int tableIndex = session.Config.PairRules.IndexByPlayerId[player];
        Assert.That(tableIndex, Is.EqualTo(0));

        Assert.That(
            CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 1, player, 0, 0),
            Is.EqualTo(1));
        var state = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(0));
        var group = session.Groups.Groups[state.GroupId];
        Assert.That(group, Is.Not.Null);
        Assert.That(group!.Player, Is.EqualTo(tableIndex));
    }
}
