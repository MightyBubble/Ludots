using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Tests;
using NUnit.Framework;

namespace CrowdSimulationTests;

public class CrowdPairRulesProjectionTests
{
    [Test]
    public void StandardPairRules_NameTheConfiguredRelationId()
    {
        var config = TestDefaults.DefaultConfig();
        Assert.That(config.PairRules.YieldsTo.Relation, Is.EqualTo("Friendly"));
        Assert.That(config.PairRules.SharesVisionWith.Relation, Is.EqualTo("Friendly"));
    }

    [Test]
    public void BlankRelationId_IsRejectedAtValidation()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["pairRules"]!["yieldsTo"]!["relation"] = " ";
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("pairRules.yieldsTo.relation"));
    }

    [Test]
    public void MissingRelationType_IsRejected()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        var json = TestDefaults.DefaultConfigJson();
        json["pairRules"]!["sharesVisionWith"]!["relation"] = "NoSuch";
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(
            () => Project(config, TestDefaults.DemoMap(), harness, world));
        Assert.That(ex!.Message, Does.Contain("pairRules.sharesVisionWith.relation"));
        Assert.That(ex.Message, Does.Contain("NoSuch"));
        Assert.That(ex.Message, Does.Contain("不是已登记的关系类型"));
    }

    [Test]
    public void PlayerWithoutTeam_IsRejected_EvenWhenNotInBases()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        var map = TestDefaults.DemoMap();
        map.Players.Add(new PlayerBindingData
        {
            PlayerId = 5,
            TeamId = 0,
            RepresentativeInstanceId = "player_5",
        });
        var ex = Assert.Throws<InvalidOperationException>(
            () => Project(TestDefaults.DefaultConfig(), map, harness, world));
        Assert.That(ex!.Message, Does.Contain("玩家 5"));
        Assert.That(ex.Message, Does.Contain("TeamId = 0"));
        Assert.That(ex.Message, Does.Contain("必须有队伍"));
    }

    [Test]
    public void UnknownDeployPlayer_IsRejected()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        var json = TestDefaults.DefaultConfigJson();
        json["deploy"]!["bases"]![0]!["playerId"] = 99;
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(
            () => Project(config, TestDefaults.DemoMap(), harness, world));
        Assert.That(ex!.Message, Does.Contain("deploy.bases"));
        Assert.That(ex.Message, Does.Contain("玩家 99"));
        Assert.That(ex.Message, Does.Contain("没有这个玩家"));
    }

    [Test]
    public void SeedTeamEdges_MatchTheLiteralMatrix()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.Link(1, 1, harness.FriendlyTypeId);
        harness.Link(2, 2, harness.FriendlyTypeId);
        harness.Link(1, 2, harness.HostileTypeId);
        harness.Link(2, 1, harness.HostileTypeId);
        var map = TestDefaults.DemoMap();
        var projected = Project(TestDefaults.DefaultConfig(), map, harness, world);
        var literal = TestDefaults.DemoPairRules();
        Assert.That(projected.Rules.ResponseByPair, Is.EqualTo(literal.ResponseByPair));
        Assert.That(projected.Rules.SharesVisionByPair, Is.EqualTo(literal.SharesVisionByPair));
        var groups = CrowdFog.BuildVisionGroups(projected.Rules.SharesVisionByPair, 4, out int count);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(groups, Is.EqualTo(TestDefaults.DemoVisionGroups));
    }

    [Test]
    public void SameTeamWithoutNamedEdge_IsHold()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.EnsureTeam(1);
        var map = Players((1, 1), (2, 1));
        var projected = Project(ConfigFor(map), map, harness, world);
        Assert.That(projected.Rules.ResponseByPair, Is.EqualTo(new[]
        {
            CrowdPushResponse.Hold, CrowdPushResponse.Hold,
            CrowdPushResponse.Hold, CrowdPushResponse.Hold,
        }));
        Assert.That(projected.Rules.SharesVisionByPair, Is.EqualTo(new[] { false, false, false, false }));
    }

    [Test]
    public void OneWayShare_DoesNotMergeGroups()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.Link(1, 1, harness.FriendlyTypeId);
        harness.Link(2, 2, harness.FriendlyTypeId);
        harness.Link(1, 2, harness.FriendlyTypeId);
        var map = Players((1, 1), (2, 2));
        var projected = Project(ConfigFor(map), map, harness, world);
        Assert.That(projected.Rules.SharesVisionByPair[0 * 2 + 1], Is.True);
        Assert.That(projected.Rules.SharesVisionByPair[1 * 2 + 0], Is.False);
        var groups = CrowdFog.BuildVisionGroups(projected.Rules.SharesVisionByPair, 2, out int count);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(groups[0], Is.Not.EqualTo(groups[1]));
    }

    [Test]
    public void Share_MergesTransitively()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.LinkSymmetric(1, 1, harness.FriendlyTypeId);
        harness.LinkSymmetric(2, 2, harness.FriendlyTypeId);
        harness.LinkSymmetric(3, 3, harness.FriendlyTypeId);
        harness.LinkSymmetric(1, 2, harness.FriendlyTypeId);
        harness.LinkSymmetric(2, 3, harness.FriendlyTypeId);
        var map = Players((1, 1), (2, 2), (3, 3));
        var projected = Project(ConfigFor(map), map, harness, world);
        Assert.That(projected.Rules.SharesVisionByPair[0 * 3 + 2], Is.False);
        var groups = CrowdFog.BuildVisionGroups(projected.Rules.SharesVisionByPair, 3, out int count);
        Assert.That(count, Is.EqualTo(1));
        Assert.That(groups[0], Is.EqualTo(groups[1]));
        Assert.That(groups[1], Is.EqualTo(groups[2]));
    }

    [Test]
    public void RevisionChange_SameMatrix_DoesNotFail_DifferentMatrix_Fails()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.Link(1, 1, harness.FriendlyTypeId);
        harness.Link(2, 2, harness.FriendlyTypeId);
        var projected = Project(TestDefaults.DefaultConfig(), TestDefaults.DemoMap(), harness, world);
        uint before = projected.Guard.Revision;
        projected.Guard.CheckAtTickBoundary();
        Assert.That(projected.Guard.Revision, Is.EqualTo(before));

        harness.Link(1, 2, harness.HostileTypeId);
        Assert.That(harness.Query.Revision, Is.GreaterThan(before));
        Assert.DoesNotThrow(() => projected.Guard.CheckAtTickBoundary());
        Assert.That(projected.Guard.Revision, Is.EqualTo(harness.Query.Revision));
        Assert.DoesNotThrow(() => projected.Guard.CheckAtTickBoundary());

        harness.Link(1, 2, harness.FriendlyTypeId);
        var ex = Assert.Throws<InvalidOperationException>(() => projected.Guard.CheckAtTickBoundary());
        Assert.That(ex!.Message, Does.Contain("只在会话开始投影"));
        Assert.That(projected.Rules.ResponseByPair[0 * 4 + 1], Is.EqualTo(CrowdPushResponse.Hold));
    }

    [Test]
    public void MemberOfTeamChange_IsRejected()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.Link(1, 1, harness.FriendlyTypeId);
        harness.Link(2, 2, harness.FriendlyTypeId);
        int owns = harness.Relationships.TypeRegistry.Register("Owns");
        int memberOf = harness.Relationships.TypeRegistry.Register("MemberOf");
        harness.Relationships.BindParticipantIdentityProjection(
            new OwnershipResolver(harness.Relationships, owns), owns, memberOf);
        MarkTeamIdentity(world, harness, 1);
        MarkTeamIdentity(world, harness, 2);

        var map = TestDefaults.DemoMap();
        var players = new PlayerEntityLookup();
        var reps = new Entity[map.Players.Count];
        for (int i = 0; i < map.Players.Count; i++)
        {
            PlayerBindingData binding = map.Players[i];
            Entity player = world.Create(new PlayerIdentity { PlayerId = binding.PlayerId });
            players.Register(binding.PlayerId, player);
            reps[i] = player;
            harness.Relationships.EnsureLink(player, harness.EnsureTeam(binding.TeamId), memberOf);
        }

        var projected = CrowdPairRulesProjection.Project(
            TestDefaults.DefaultConfig(), map, harness.Query, players, world);
        uint revision = projected.Guard.Revision;
        Assert.DoesNotThrow(() => projected.Guard.CheckAtTickBoundary());

        harness.Relationships.RemoveLink(reps[0], harness.EnsureTeam(1), memberOf);
        harness.Relationships.EnsureLink(reps[0], harness.EnsureTeam(2), memberOf);
        Assert.That(world.Get<Team>(reps[0]).Id, Is.EqualTo(2));
        var ex = Assert.Throws<InvalidOperationException>(() => projected.Guard.CheckAtTickBoundary());
        Assert.That(ex!.Message, Does.Contain("玩家 1"));
        Assert.That(ex.Message, Does.Contain("队伍从 1 变成 2"));
        Assert.That(projected.Rules.ResponseByPair[0 * 4 + 1], Is.EqualTo(CrowdPushResponse.Hold));
        Assert.That(projected.Guard.Revision, Is.EqualTo(revision));
    }

    [Test]
    public void TeamComponentOnlyChange_IsRejected()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.Link(1, 1, harness.FriendlyTypeId);
        harness.Link(2, 2, harness.FriendlyTypeId);
        var map = TestDefaults.DemoMap();
        var players = BindPlayers(world, harness, map);
        var projected = CrowdPairRulesProjection.Project(
            TestDefaults.DefaultConfig(), map, harness.Query, players, world);
        uint revision = harness.Query.Revision;
        Assert.That(players.TryGet(1, out Entity player), Is.True);
        world.Set(player, new Team { Id = 2 });
        var ex = Assert.Throws<InvalidOperationException>(() => projected.Guard.CheckAtTickBoundary());
        Assert.That(ex!.Message, Does.Contain("玩家 1"));
        Assert.That(ex.Message, Does.Contain("队伍从 1 变成 2"));
        Assert.That(harness.Query.Revision, Is.EqualTo(revision));
        Assert.That(projected.Rules.ResponseByPair[0 * 4 + 1], Is.EqualTo(CrowdPushResponse.Hold));
    }

    private static CrowdSimulationConfig ConfigFor(MapConfig map)
    {
        var json = TestDefaults.DefaultConfigJson();
        var bases = new JsonArray();
        foreach (var player in map.Players)
        {
            bases.Add(JsonNode.Parse($"{{\"playerId\":{player.PlayerId},\"xCm\":1,\"yCm\":1}}"));
        }

        json["deploy"]!["bases"] = bases;
        return CrowdSimulationConfig.Load(json);
    }

    private static CrowdPairProjection Project(
        CrowdSimulationConfig config, MapConfig map, TeamRelationTestHarness harness, World world)
    {
        return CrowdPairRulesProjection.Project(config, map, harness.Query, BindPlayers(world, harness, map), world);
    }

    private static PlayerEntityLookup BindPlayers(World world, TeamRelationTestHarness harness, MapConfig map)
    {
        var players = new PlayerEntityLookup();
        foreach (var binding in map.Players)
        {
            if (binding.TeamId <= 0)
            {
                continue;
            }

            harness.EnsureTeam(binding.TeamId);
            players.Register(
                binding.PlayerId,
                world.Create(new PlayerIdentity { PlayerId = binding.PlayerId }, new Team { Id = binding.TeamId }));
        }

        return players;
    }

    private static void MarkTeamIdentity(World world, TeamRelationTestHarness harness, int teamId)
    {
        Entity team = harness.EnsureTeam(teamId);
        world.Add(team, new TeamIdentity { TeamId = teamId });
    }

    private static MapConfig Players(params (int PlayerId, int TeamId)[] players)
    {
        var map = new MapConfig
        {
            Id = "pair-rules",
            Players = new System.Collections.Generic.List<PlayerBindingData>(),
        };
        foreach (var (playerId, teamId) in players)
        {
            map.Players.Add(new PlayerBindingData
            {
                PlayerId = playerId,
                TeamId = teamId,
                RepresentativeInstanceId = "player_" + playerId,
            });
        }

        return map;
    }
}
