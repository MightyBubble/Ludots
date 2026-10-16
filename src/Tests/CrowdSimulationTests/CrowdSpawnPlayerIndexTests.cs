using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.Gameplay.Components;
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
        TestDefaults.EnableFog(session);

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

    [Test]
    public void Spawn_GroupPlayer_FollowsBasePlayerId_WhenBaseOrderDiffersFromPlayers()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var bases = session.Config.Deploy.Bases;
        Assert.That(bases.Count, Is.GreaterThan(1));
        int players = session.Config.PairRules.PlayerCount;
        int firstPlayer = bases[0].PlayerId;
        var order = new List<PlayerBindingData>();
        for (int id = 1; id <= players; id++)
        {
            if (id == firstPlayer) continue;
            order.Add(new PlayerBindingData { PlayerId = id, TeamId = 1, RepresentativeInstanceId = "p" + id });
        }

        order.Add(new PlayerBindingData { PlayerId = firstPlayer, TeamId = 1, RepresentativeInstanceId = "p" + firstPlayer });
        session.Config.PairRules.BindPlayerIndex(new MapConfig { Id = "player_perm", Players = order });
        int table = session.Config.PairRules.IndexByPlayerId[firstPlayer];
        Assert.That(table, Is.Not.EqualTo(0));

        int made = CrowdDeployment.Spawn(session, bases.Count);
        Assert.That(made, Is.EqualTo(bases.Count));
        bool sawFirst = false;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var entity = session.Units.EntityAt(i);
            int owner = session.World.Get<PlayerOwner>(entity).PlayerId;
            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            var group = session.Groups.Groups[state.GroupId];
            Assert.That(group, Is.Not.Null);
            Assert.That(group!.Player, Is.EqualTo(session.Config.PairRules.IndexByPlayerId[owner]));
            if (owner == firstPlayer) sawFirst = true;
        }

        Assert.That(sawFirst, Is.True);
        int at = session.Units.Count;
        Assert.That(
            CrowdDeployment.SpawnAt(session, Fix64.FromInt(bases[0].XCm), Fix64.FromInt(bases[0].YCm), 1, firstPlayer, 0, 0),
            Is.EqualTo(1));
        var pointed = session.Units.EntityAt(at);
        Assert.That(session.World.Get<PlayerOwner>(pointed).PlayerId, Is.EqualTo(firstPlayer));
        var pointedState = session.World.Get<CrowdSimulationUnitState>(pointed);
        Assert.That(session.Groups.Groups[pointedState.GroupId]!.Player, Is.EqualTo(table));
    }
}
