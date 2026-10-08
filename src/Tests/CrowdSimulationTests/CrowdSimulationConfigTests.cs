using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>S0 验收：配置能加载，写错会报错（每条错误写明文件、字段路径、规则）。</summary>
[TestFixture]
public class CrowdSimulationConfigTests
{
    [Test]
    public void DefaultConfig_Assembles_AgainstDemoMap()
    {
        var runtime = TestDefaults.Assemble();

        Assert.That(runtime.Version, Is.EqualTo(7));
        Assert.That(runtime.NavCellCount, Is.EqualTo(256));
        Assert.That(runtime.NavCellSizeCm, Is.EqualTo(6250));
        Assert.That(runtime.FixedHz, Is.EqualTo(30));
        Assert.That(runtime.TerrainTypeIds, Is.EqualTo(new[] { "water", "shore", "land", "mountain", "cliff" }));
        Assert.That(runtime.NavAreas.Count, Is.EqualTo(9));
        Assert.That(runtime.TerrainToArea.ToArray(), Is.EqualTo(new[] { 4, 1, 0, 2, 3 }));
        Assert.That(runtime.AgentTypes.Count, Is.EqualTo(5));
        Assert.That(runtime.Profiles.Count, Is.EqualTo(20));
    }

    [Test]
    public void DefaultConfig_ConvertsToFixedPointCentimeters()
    {
        var runtime = TestDefaults.Assemble();

        var foot = runtime.AgentTypes[0];
        Assert.That(foot.SpeedCmPerSecond, Is.EqualTo(Fix64.FromInt(1400)));
        // road 在 navAreas 下标 5；foot 对 road 代价 0.7。
        Assert.That(foot.CostByArea[5], Is.EqualTo(Fix64.FromDouble(0.7)));
        // deep 下标 4 未写 = 不可通行（0）。
        Assert.That(foot.CostByArea[4], Is.EqualTo(Fix64.Zero));

        Assert.That(runtime.Fog.VisionCm, Is.EqualTo(Fix64.FromInt(80000)));
        Assert.That(runtime.Formation.MagicBoxMaxSpreadCm, Is.EqualTo(Fix64.FromInt(250000)));
        // 领队继承阈值与参考实现内联字面量同值(D61 反转 -0.5 / D62 同向 0.94)。
        Assert.That(runtime.Formation.HeadingInheritDot, Is.EqualTo(Fix64.FromDouble(0.94)));
        Assert.That(runtime.Formation.MirrorFlipDot, Is.EqualTo(Fix64.FromDouble(-0.5)));
        Assert.That(runtime.Deploy.Bases[0].XCm, Is.EqualTo(480000));
        Assert.That(runtime.Deploy.Bases[0].YCm, Is.EqualTo(560000));

        var leaper = runtime.NavProfiles[4];
        Assert.That(leaper.Jump, Is.Not.Null);
        Assert.That(leaper.Jump!.UpCm, Is.EqualTo(2500));
        Assert.That(leaper.Jump.DownCm, Is.EqualTo(20000));
        Assert.That(leaper.Jump.RangeCells, Is.EqualTo(7));
    }

    [Test]
    public void DefaultConfig_DerivesClearanceFromGeometry()
    {
        var runtime = TestDefaults.Assemble();

        // 默认 6250 cm 格下 R100–R400 的个人半径（600–2400 cm）都不足半格，共享净空 1。
        foreach (var p in runtime.Profiles)
        {
            Assert.That(p.ClearanceCells, Is.EqualTo(1), p.Id);
        }

        var leaperR400 = runtime.Profiles.Single(p => p.Id == "leaper_r400");
        Assert.That(leaperR400.NavContextId, Is.EqualTo(4 * 256 + 1));
        Assert.That(leaperR400.PersonalRadiusCm, Is.EqualTo(Fix64.FromInt(2400)));
        Assert.That(leaperR400.PushPriority, Is.EqualTo(Fix64.FromInt(2 + 3)));
    }

    [Test]
    public void MissingRequiredField_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        json.Remove("hpa");
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationConfig.json"));
    }

    [Test]
    public void UnknownField_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["unknownTuning"] = 1;
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationConfig.json"));
    }

    [Test]
    public void OutOfRangeValue_ReportsFileFieldAndRule()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["hpa"]!["clusterSize"] = 2;
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("hpa.clusterSize = 2"));
        Assert.That(ex.Message, Does.Contain("≥ 4"));
        Assert.That(ex.Message, Does.Contain("≤ 128"));

        var dotJson = TestDefaults.DefaultConfigJson();
        dotJson["formation"]!["mirrorFlipDot"] = 0.5;
        var dotConfig = CrowdSimulationConfig.Load(dotJson);
        var dotEx = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(dotConfig));
        Assert.That(dotEx!.Message, Does.Contain("formation.mirrorFlipDot = 0.5"));
        Assert.That(dotEx.Message, Does.Contain("≥ -1"));
        Assert.That(dotEx.Message, Does.Contain("≤ 0"));
    }

    [Test]
    public void AreaCost_ReferencingUnknownArea_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        ((JsonObject)json["agentTypes"]![0]!)["areaCost"]!["lava"] = 1.0;
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("agentTypes.foot.areaCost.lava"));
        Assert.That(ex.Message, Does.Contain("未知导航区域"));
    }

    [Test]
    public void ProfileLayer_WithoutAgentType_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        var profiles = TestDefaults.DemoProfiles();
        var withBadLayer = new AgentProfileRegistry(
            Enumerable.Range(0, profiles.Count).Select(i =>
            {
                var p = profiles[i];
                return new AgentProfileConfig
                {
                    Id = p.Id,
                    RadiusCm = p.RadiusCm,
                    HeightCm = p.HeightCm,
                    ClearanceCm = p.ClearanceCm,
                    DraftCm = p.DraftCm,
                    BeamCm = p.BeamCm,
                    Mass = p.Mass,
                    Layer = i == 0 ? 99 : p.Layer,
                };
            }).ToList());
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(profiles: withBadLayer));
        Assert.That(ex!.Message, Does.Contain("layer = 99"));
    }

    [Test]
    public void NonSquareMap_IsRejected()
    {
        var map = TestDefaults.DemoMap();
        map.Boards[0].HeightCm = TestDefaults.WorldCm / 2;
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(map: map));
        Assert.That(ex!.Message, Does.Contain("必须是正方形"));
    }

    [Test]
    public void DualSolver_SameMap_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigLoader.EnsureSolverExclusive("crowd_simulation_demo", "crowd_simulation_demo"));
        Assert.That(ex!.Message, Does.Contain("一张地图只能使用其中一个"));
        Assert.DoesNotThrow(() => CrowdSimulationConfigLoader.EnsureSolverExclusive("other_map", "crowd_simulation_demo"));
        Assert.DoesNotThrow(() => CrowdSimulationConfigLoader.EnsureSolverExclusive(null, "crowd_simulation_demo"));
    }

    [Test]
    public void VersionMismatch_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["version"] = 6;
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("无迁移"));
    }

    [Test]
    public void TerrainAreas_MissingType_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        ((JsonObject)json["terrainAreas"]!).Remove("cliff");
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("缺少地形类型 \"cliff\" 的映射"));
    }

    [Test]
    public void JumpCost_BelowMinAreaCost_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        ((JsonObject)json["navProfiles"]![4]!)["jump"]!["cost"] = 0.5;
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("jump.cost 不能低于该类型最低区域代价"));
    }

    [Test]
    public void Relations_Matrix_AlignsUnorderedPlayerIds()
    {
        // Players 不按 1..P 顺排(表序 [3,1,2])时,推挤矩阵按下标对齐:同队 ally、
        // 跨队 enemy、overrides 翻转指定对(3↔1)——求解器的玩家号→下标换算吃同一张表。
        var map = TestDefaults.DemoMap();
        map.Players.Clear();
        map.Players.Add(new PlayerBindingData { PlayerId = 3, TeamId = 1, RepresentativeInstanceId = "player_3" });
        map.Players.Add(new PlayerBindingData { PlayerId = 1, TeamId = 2, RepresentativeInstanceId = "player_1" });
        map.Players.Add(new PlayerBindingData { PlayerId = 2, TeamId = 1, RepresentativeInstanceId = "player_2" });
        map.Players.Add(new PlayerBindingData { PlayerId = 4, TeamId = 2, RepresentativeInstanceId = "player_4" });

        var json = TestDefaults.DefaultConfigJson();
        json["relations"]!["overrides"] = new JsonArray
        {
            new JsonObject { ["a"] = 1, ["b"] = 3, ["kind"] = "ally" },
        };
        var runtime = TestDefaults.Assemble(CrowdSimulationConfig.Load(json), map);

        var relations = runtime.Relations;
        Assert.That(relations.PlayerCount, Is.EqualTo(4));
        Assert.That(relations.IndexByPlayerId[3], Is.EqualTo(0));
        Assert.That(relations.IndexByPlayerId[1], Is.EqualTo(1));
        Assert.That(relations.IndexByPlayerId[2], Is.EqualTo(2));
        Assert.That(relations.IndexByPlayerId[4], Is.EqualTo(3));

        // 表序 [3,1,2,4]:(3,2) 同队 A → sameTeam=ally → priority;(1,2)/(3,4) 跨队 →
        // default=enemy → rigid;override 翻转 (1,3) → ally → priority,且对称。
        Assert.That(relations.PushModeByPair[0 * 4 + 2], Is.EqualTo(CrowdSimulationPushMode.Priority));
        Assert.That(relations.PushModeByPair[1 * 4 + 2], Is.EqualTo(CrowdSimulationPushMode.Rigid));
        Assert.That(relations.PushModeByPair[0 * 4 + 3], Is.EqualTo(CrowdSimulationPushMode.Rigid));
        Assert.That(relations.PushModeByPair[0 * 4 + 1], Is.EqualTo(CrowdSimulationPushMode.Priority));
        Assert.That(relations.PushModeByPair[1 * 4 + 0], Is.EqualTo(CrowdSimulationPushMode.Priority));
    }

    [Test]
    public void PartialOverride_MergesOverDefaults()
    {
        var merged = TestDefaults.DefaultConfigJson();
        ConfigPipeline.DeepMerge(merged, JsonNode.Parse("""{ "formation": { "leaderSpeed": 0.6 } }""")!.AsObject());

        var config = CrowdSimulationConfig.Load(merged);
        var runtime = TestDefaults.Assemble(config);

        Assert.That(runtime.Formation.LeaderSpeed, Is.EqualTo(Fix64.FromDouble(0.6)));
        Assert.That(runtime.Formation.SpacingScale, Is.EqualTo(Fix64.FromDouble(1.1)));
        Assert.That(runtime.NavCellSizeCm, Is.EqualTo(6250));
    }

    [Test]
    public void DeployBase_ReferencingUnknownPlayer_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["deploy"]!["bases"]![0]!["playerId"] = 9;
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("playerId = 9"));
    }

    [Test]
    public void StructureTemplates_ParseFromModDefaults()
    {
        var runtime = TestDefaults.Assemble();
        var templates = runtime.Structures.Templates;
        // 与参考端默认表同序同 id(S7 真值脚本按 id 引用,追加模板在表尾)
        Assert.That(templates.Select(t => t.Id).ToArray(), Is.EqualTo(
            new[] { "building", "bridge", "road", "flood", "fire", "s7barrier" }));
        Assert.That(templates[0].Blocker, Is.True);
        Assert.That(templates[0].AreaIndex, Is.Null);
        Assert.That(templates[1].Layered, Is.True);
        Assert.That(templates[1].AreaIndex, Is.Not.Null);
        Assert.That(templates[2].Priority, Is.EqualTo(10));
        Assert.That(templates[4].LifetimeSec, Is.Not.Null);
        Assert.That(templates[5].Blocker, Is.True);
        Assert.That(templates[5].LifetimeSec, Is.EqualTo(Fix64.FromDouble(4)));
        Assert.That(templates[5].LifetimeSecRaw, Is.EqualTo(4.0));
    }

    [Test]
    public void StructureTemplates_InvalidRules_AreRejected()
    {
        // blocker 只支持 rect
        var json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonArray
        {
            JsonNode.Parse("""{"id":"x","name":"X","footprint":"disc","blocker":true}""")!,
        };
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("blocker 只支持 rect"));

        // area 与 priority 同进同出
        json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonArray
        {
            JsonNode.Parse("""{"id":"x","name":"X","footprint":"rect","area":"road"}""")!,
        };
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("area 与 priority 需同时给出"));

        // 未知区域
        json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonArray
        {
            JsonNode.Parse("""{"id":"x","name":"X","footprint":"rect","area":"nowhere","priority":1}""")!,
        };
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("未知导航区域"));

        // 非阻挡且无 area = 无效实体
        json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonArray
        {
            JsonNode.Parse("""{"id":"x","name":"X","footprint":"rect"}""")!,
        };
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("无效实体"));

        // lifetimeSec 需 > 0
        json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonArray
        {
            JsonNode.Parse("""{"id":"x","name":"X","footprint":"rect","blocker":true,"lifetimeSec":0}""")!,
        };
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("lifetimeSec"));
    }

    [Test]
    public void Relations_NonPermutationPlayerIds_AreRejected()
    {
        // L16:关系矩阵按玩家号直查(IndexByPlayerId 长度 P+1),玩家号必须恰为 1..P 的排列;
        // 跳号/重复/0/超界全部进图即拒。
        foreach (var (ids, expected) in new[]
                 {
                     (new[] { 1, 3 }, "缺失 2"),
                     (new[] { 1, 1 }, "playerId 重复"),
                     (new[] { 0, 1 }, "缺失 2"),
                     (new[] { 1, 5 }, "缺失 2"),
                 })
        {
            var map = TestDefaults.DemoMap();
            map.Players.Clear();
            for (int i = 0; i < ids.Length; i++)
            {
                map.Players.Add(new PlayerBindingData { PlayerId = ids[i], TeamId = 1, RepresentativeInstanceId = "player_" + i });
            }

            var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(map: map),
                $"玩家号 [{string.Join(',', ids)}] 应被拒绝");
            Assert.That(ex!.Message, Does.Contain(expected), $"[{string.Join(',', ids)}] 的报错应写明规则");
        }
    }
}