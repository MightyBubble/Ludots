using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Fog;
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
            () => CrowdPairRulesProjection.Project(config, TestDefaults.DemoMap(), harness.Query));
        Assert.That(ex!.Message, Does.Contain("pairRules.sharesVisionWith.relation"));
        Assert.That(ex.Message, Does.Contain("NoSuch"));
        Assert.That(ex.Message, Does.Contain("不是已登记的关系类型"));
    }

    [Test]
    public void DeployedPlayerWithoutTeam_IsRejected()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        var map = TestDefaults.DemoMap();
        map.Players[0].TeamId = 0;
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdPairRulesProjection.Project(TestDefaults.DefaultConfig(), map, harness.Query));
        Assert.That(ex!.Message, Does.Contain("玩家 1"));
        Assert.That(ex.Message, Does.Contain("TeamId = 0"));
        Assert.That(ex.Message, Does.Contain("必须有队伍"));
    }

    [Test]
    public void SeedTeamEdges_MatchTheParityMatrix()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.Link(1, 1, harness.FriendlyTypeId);
        harness.Link(2, 2, harness.FriendlyTypeId);
        harness.Link(1, 2, harness.HostileTypeId);
        harness.Link(2, 1, harness.HostileTypeId);
        var map = TestDefaults.DemoMap();
        var projected = CrowdPairRulesProjection.Project(TestDefaults.DefaultConfig(), map, harness.Query);
        var parity = TestDefaults.ParityPairRules(map);
        Assert.That(projected.Rules.ResponseByPair, Is.EqualTo(parity.ResponseByPair));
        Assert.That(projected.Rules.SharesVisionByPair, Is.EqualTo(parity.SharesVisionByPair));
        Assert.That(projected.Rules.ResponseByPair[0 * 4 + 2], Is.EqualTo(CrowdPushResponse.Yield));
        Assert.That(projected.Rules.SharesVisionByPair[0 * 4 + 2], Is.True);
        Assert.That(projected.Rules.ResponseByPair[0 * 4 + 1], Is.EqualTo(CrowdPushResponse.Hold));
        Assert.That(projected.Rules.SharesVisionByPair[0 * 4 + 1], Is.False);
    }

    [Test]
    public void OneWayShare_DoesNotMergeGroups()
    {
        using var world = World.Create();
        var harness = TeamRelationTestHarness.Create(world);
        harness.Link(1, 1, harness.FriendlyTypeId);
        harness.Link(2, 2, harness.FriendlyTypeId);
        harness.Link(1, 2, harness.FriendlyTypeId);
        var projected = CrowdPairRulesProjection.Project(TestDefaults.DefaultConfig(), Players((1, 1), (2, 2)), harness.Query);
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
        var projected = CrowdPairRulesProjection.Project(
            TestDefaults.DefaultConfig(), Players((1, 1), (2, 2), (3, 3)), harness.Query);
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
        var projected = CrowdPairRulesProjection.Project(TestDefaults.DefaultConfig(), TestDefaults.DemoMap(), harness.Query);
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
