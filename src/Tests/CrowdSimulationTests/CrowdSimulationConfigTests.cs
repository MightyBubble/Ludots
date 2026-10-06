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
}
