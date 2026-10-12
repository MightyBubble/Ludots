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

        Assert.That(runtime.Sim.SimSecondsPerTickSecond, Is.EqualTo(Fix64.FromInt(10)));
        Assert.That(runtime.NavCellsPerSide, Is.EqualTo(256));
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
        // 领队继承阈值与参考实现内联字面量同值(反转 -0.5 / 同向 0.94)。
        Assert.That(runtime.Formation.HeadingInheritDot, Is.EqualTo(Fix64.FromDouble(0.94)));
        Assert.That(runtime.Formation.MirrorFlipDot, Is.EqualTo(Fix64.FromDouble(-0.5)));
        Assert.That(runtime.Deploy.Bases[0].XCm, Is.EqualTo(480000));
        Assert.That(runtime.Deploy.Bases[0].YCm, Is.EqualTo(560000));

        var leaper = runtime.NavProfiles[4];
        Assert.That(leaper.Jump, Is.Not.Null);
        Assert.That(leaper.Jump!.UpCm, Is.EqualTo(2500));
        Assert.That(leaper.Jump.DownCm, Is.EqualTo(20000));
        Assert.That(leaper.Jump.RangeNavCells, Is.EqualTo(7));
    }

    [Test]
    public void DefaultConfig_DerivesClearanceFromGeometry()
    {
        var runtime = TestDefaults.Assemble();

        // 默认 6250 cm 格下 R100–R400 的个人半径（600–2400 cm）都不足半格，共享净空 1。
        foreach (var p in runtime.Profiles)
        {
            Assert.That(p.ClearanceNavCells, Is.EqualTo(1), p.Id);
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
    public void MissingParallelWorkerCount_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        ((JsonObject)json["sim"]!).Remove("parallelWorkerCount");
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain("parallelWorkerCount"));
    }

    [Test]
    public void ZeroParallelWorkerCount_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["sim"]!["parallelWorkerCount"] = 0;
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("sim.parallelWorkerCount"));
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
        // 6250 cm × 2 格，派生格数低于 4..128。
        json["navtile"]!["sizeCm"] = 12500;
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("navtile.sizeCm"));
        Assert.That(ex.Message, Does.Contain("= 2"));
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
                    NavProfileId = p.NavProfileId,
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
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(map: map, pairRules: TestDefaults.DemoPairRules()));
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
    public void RetiredVersionKey_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["version"] = 6;
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain("已删除 version"));
        Assert.That(ex.Message, Does.Contain("配置不再带版本号，由严格 schema 与 mod.json semver 管理"));
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
    public void PairRules_UnorderedPlayerIds_KeepTheFedMatrix()
    {
        // 玩家对级 overrides 已删除。装载不再从队伍号生成矩阵，只把喂进来的矩阵按下标挂上。
        // 玩家号仍要能换成表序，乱序玩家号查的是同一张下标表。
        var map = TestDefaults.DemoMap();
        map.Players.Clear();
        map.Players.Add(new PlayerBindingData { PlayerId = 3, TeamId = 1, RepresentativeInstanceId = "player_3" });
        map.Players.Add(new PlayerBindingData { PlayerId = 1, TeamId = 2, RepresentativeInstanceId = "player_1" });
        map.Players.Add(new PlayerBindingData { PlayerId = 2, TeamId = 1, RepresentativeInstanceId = "player_2" });
        map.Players.Add(new PlayerBindingData { PlayerId = 4, TeamId = 2, RepresentativeInstanceId = "player_4" });

        var response = new CrowdPushResponse[16];
        var shares = new bool[16];
        response[0 * 4 + 1] = CrowdPushResponse.Yield;
        response[1 * 4 + 0] = CrowdPushResponse.Hold;
        shares[0 * 4 + 2] = true;
        shares[2 * 4 + 0] = false;
        var fed = CrowdPairRules.Matrices(4, response, shares);
        var runtime = TestDefaults.Assemble(map: map, pairRules: fed);

        var pairRules = runtime.PairRules;
        Assert.That(pairRules, Is.SameAs(fed));
        Assert.That(pairRules.IndexByPlayerId[3], Is.EqualTo(0));
        Assert.That(pairRules.IndexByPlayerId[1], Is.EqualTo(1));
        Assert.That(pairRules.IndexByPlayerId[2], Is.EqualTo(2));
        Assert.That(pairRules.IndexByPlayerId[4], Is.EqualTo(3));
        Assert.That(pairRules.ResponseByPair[0 * 4 + 1], Is.EqualTo(CrowdPushResponse.Yield));
        Assert.That(pairRules.ResponseByPair[1 * 4 + 0], Is.EqualTo(CrowdPushResponse.Hold));
        Assert.That(pairRules.SharesVisionByPair[0 * 4 + 2], Is.True);
        Assert.That(pairRules.SharesVisionByPair[2 * 4 + 0], Is.False);
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
        Assert.That(templates.Select(t => t.Id).ToArray(), Is.EqualTo(
            new[] { "bridge", "building", "fire", "flood", "road", "s7barrier" }));
        var byId = templates.ToDictionary(t => t.Id);
        Assert.That(byId["building"].Blocker, Is.True);
        Assert.That(byId["building"].AreaIndex, Is.Null);
        Assert.That(byId["bridge"].Layered, Is.True);
        Assert.That(byId["bridge"].AreaIndex, Is.Not.Null);
        Assert.That(byId["road"].Priority, Is.EqualTo(10));
        Assert.That(byId["fire"].LifetimeSec, Is.Not.Null);
        Assert.That(byId["s7barrier"].Blocker, Is.True);
        Assert.That(byId["s7barrier"].LifetimeSec, Is.EqualTo(Fix64.FromDouble(4)));
        Assert.That(byId["s7barrier"].LifetimeSecRaw, Is.EqualTo(4.0));
    }

    [Test]
    public void StructureTemplates_InvalidRules_AreRejected()
    {
        // blocker 只支持 rect
        var json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonObject
        {
            ["x"] = JsonNode.Parse("""{"name":"X","footprint":"disc","blocker":true}""")!,
        };
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("blocker 只支持 rect"));

        // area 与 priority 同进同出
        json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonObject
        {
            ["x"] = JsonNode.Parse("""{"name":"X","footprint":"rect","area":"road"}""")!,
        };
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("area 与 priority 需同时给出"));

        // 未知区域
        json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonObject
        {
            ["x"] = JsonNode.Parse("""{"name":"X","footprint":"rect","area":"nowhere","priority":1}""")!,
        };
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("未知导航区域"));

        // 非阻挡且无 area = 无效实体
        json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonObject
        {
            ["x"] = JsonNode.Parse("""{"name":"X","footprint":"rect"}""")!,
        };
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("无效实体"));

        // lifetimeSec 需 > 0
        json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"] = new JsonObject
        {
            ["x"] = JsonNode.Parse("""{"name":"X","footprint":"rect","blocker":true,"lifetimeSec":0}""")!,
        };
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("lifetimeSec"));
    }

    [Test]
    public void PlayerIds_MustBePermutation_ForPairIndex()
    {
        // 功能位矩阵按玩家号直查(IndexByPlayerId 长度 P+1),玩家号必须恰为 1..P 的排列。
        // 这是下标合同,不是关系规则。跳号/重复/0/超界全部进图即拒。
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

            int n = ids.Length;
            var rules = CrowdPairRules.Matrices(n, new CrowdPushResponse[n * n], new bool[n * n]);
            var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(map: map, pairRules: rules),
                $"玩家号 [{string.Join(',', ids)}] 应被拒绝");
            Assert.That(ex!.Message, Does.Contain(expected), $"[{string.Join(',', ids)}] 的报错应写明规则");
        }
    }

    [Test]
    public void RetiredSpatialKeys_NameTheReplacement()
    {
        foreach (var (oldPath, newKey) in CrowdSimulationConfig.RetiredSpatialKeys)
        {
            var json = TestDefaults.DefaultConfigJson();
            string concrete = PlaceRetiredKey(json, oldPath);
            var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json), oldPath);
            Assert.That(ex!.Message, Does.Contain(concrete), oldPath);
            Assert.That(ex.Message, Does.Contain(newKey), oldPath);
            Assert.That(ex.Message, Does.Contain("已删除"), oldPath);
        }
    }

    [Test]
    public void RetiredSpatialKeys_AreNotEmptied()
    {
        var old = new HashSet<string>(CrowdSimulationConfig.RetiredSpatialKeys.Select(entry => entry.OldPath));
        Assert.That(old, Does.Contain("formation.settleTime"));
        Assert.That(old, Does.Contain("movement.slotTimeConstant"));
        Assert.That(old, Does.Contain("sim.timeScale"));
        Assert.That(old, Does.Contain("formations[].spacing"));
        Assert.That(old, Does.Contain("fog.cellCells"));
        Assert.That(old, Does.Contain("movement.blendRate"));
        Assert.That(old, Does.Contain("movement.unitTurnRate"));
        Assert.That(old, Does.Contain("movement.stallDecay"));
        Assert.That(old, Does.Contain("movement.slotCheckInterval"));
        Assert.That(old, Does.Contain("formation.leaderTurnRate"));
        Assert.That(old, Does.Contain("avoidance.acceleration"));
        Assert.That(old, Does.Contain("relations"));
        Assert.That(old, Does.Contain("push.modes"));
    }

    private static string PlaceRetiredKey(JsonObject root, string oldPath)
    {
        if (oldPath == "formations[].spacing")
        {
            root["formations"]!["box"]!["spacing"] = 1;
            return "formations.box.spacing";
        }

        if (oldPath == "navProfiles[].jump.rangeCells")
        {
            var profiles = root["navProfiles"]!.AsArray();
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i]!["jump"] is JsonObject jump)
                {
                    jump["rangeCells"] = 7;
                    return $"navProfiles[{i}].jump.rangeCells";
                }
            }

            throw new InvalidOperationException("标准配置没有 jump profile。");
        }

        var parts = oldPath.Split('.');
        JsonObject obj = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            obj = obj[parts[i]]!.AsObject();
        }

        obj[parts[^1]] = 1;
        return oldPath;
    }

    [Test]
    public void BoardAnchor_OffOrigin_IsRejected()
    {
        var map = TestDefaults.DemoMap();
        map.Boards[0].Anchor.WorldXCm = 1;
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(map: map, pairRules: TestDefaults.DemoPairRules()));
        Assert.That(ex!.Message, Does.Contain("Anchor"));
        Assert.That(ex.Message, Does.Contain("必须为 0"));
    }

    [Test]
    public void WorldDistanceCentimeters_MatchLegacyCellCountFix64Raw()
    {
        const int navCellSizeCm = 6250;
        var config = TestDefaults.DefaultConfig();
        var runtime = TestDefaults.Assemble();
        Assert.That(config.World.NavCellSizeCm, Is.EqualTo(navCellSizeCm));
        Assert.That(runtime.Hpa.NavCellsPerTile, Is.EqualTo(16));
        Assert.That(runtime.Fog.FogCellSizeCm, Is.EqualTo(25000));
        Assert.That(runtime.Fog.NavCellsPerFogCell, Is.EqualTo(4));

        Equal(runtime.Formation.SlotSightNavCells, config.Formation.SlotSightCm, 16, navCellSizeCm);
        Equal(runtime.Formation.LeaderLookAheadNavCells, config.Formation.LeaderLookAheadCm, 6, navCellSizeCm);
        Equal(runtime.Formation.SlotArriveNavCells, config.Formation.SlotArriveCm, 0.35, navCellSizeCm);
        Equal(runtime.Formation.GoalArriveNavCells, config.Formation.GoalArriveCm, 1, navCellSizeCm);
        Equal(runtime.Formation.SettleRadiusNavCells, config.Formation.SettleRadiusCm, 2, navCellSizeCm);
        Equal(runtime.Formation.MinClumpNavCells, config.Formation.MinClumpCm, 1.5, navCellSizeCm);
        Equal(runtime.Movement.WakeDistanceNavCells, config.Movement.WakeDistanceCm, 2, navCellSizeCm);
        Equal(runtime.Deploy.SpreadNavCells, config.Deploy.SpreadCm, 30, navCellSizeCm);
        Equal(runtime.Deploy.BaseJitterNavCells, config.Deploy.BaseJitterCm, 20, navCellSizeCm);

        long legacyPad = (Fix64.FromDouble(1) * navCellSizeCm).RawValue;
        Assert.That(runtime.Formation.MagicBoxPadCm.RawValue, Is.EqualTo(legacyPad));
        Assert.That(runtime.Formation.MagicBoxPadCm.RawValue, Is.EqualTo(Fix64.FromDouble(config.Formation.MagicBoxPadCm).RawValue));
        Assert.That(config.Formation.MagicBoxPadCm, Is.EqualTo(navCellSizeCm));

        Assert.That(runtime.Movement.MaxStepNavCells.RawValue, Is.EqualTo(Fix64.FromDouble(0.45).RawValue));
        Assert.That(runtime.Navmesh.PortalInsetNavCells.RawValue, Is.EqualTo(Fix64.FromDouble(0.5).RawValue));
        Assert.That(runtime.Spawn.NavCellInsetFraction.RawValue, Is.EqualTo(Fix64.FromDouble(0.02).RawValue));
    }

    private static void Equal(Fix64 actual, double authoredCm, double legacyCells, int navCellSizeCm)
    {
        long legacy = Fix64.FromDouble(legacyCells).RawValue;
        long converted = Fix64.FromDouble(authoredCm / (double)navCellSizeCm).RawValue;
        Assert.That(converted, Is.EqualTo(legacy), $"厘米 {authoredCm} / {navCellSizeCm} 与旧格数 {legacyCells} 的 Fix64 原始值不一致");
        Assert.That(actual.RawValue, Is.EqualTo(legacy));
    }

    [Test]
    public void FogCellSize_BelowSquareBound_IsRejected_AtBound_Passes()
    {
        // 99 cm 低于下限 100;导航边长取 16,tile 边长与板宽对齐。
        var below = FogCellWorld(navCellSizeCm: 99, fogCellSizeCm: 99, visionCm: 99);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(below.Config, below.Map, pairRules: TestDefaults.DemoPairRules()));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationConfig.json"));
        Assert.That(ex.Message, Does.Contain("fog.cellSizeCm"));
        Assert.That(ex.Message, Does.Contain("= 99"));
        Assert.That(ex.Message, Does.Contain("≥ 100"));

        var atBound = FogCellWorld(navCellSizeCm: 100, fogCellSizeCm: 100, visionCm: 100);
        var runtime = TestDefaults.Assemble(atBound.Config, atBound.Map, pairRules: TestDefaults.DemoPairRules());
        Assert.That(runtime.Fog.FogCellSizeCm, Is.EqualTo(100));
        Assert.That(runtime.NavCellSizeCm * runtime.Fog.NavCellsPerFogCell, Is.EqualTo(100));
    }

    [Test]
    public void FogCellMultiplier_Above32_IsRejected()
    {
        // 格长改由厘米作者面给出,不再做 navCellSizeCm × cellCells 的 int 直乘。
        // 超过 int 的厘米值在反序列化拒绝;倍数 33 仍落在 int 内,由 1..32 拒绝。
        var overflow = TestDefaults.DefaultConfigJson();
        overflow["fog"]!["cellSizeCm"] = 4294967264L;
        var overflowEx = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(overflow));
        Assert.That(overflowEx!.Message, Does.Contain("cellSizeCm"));

        const int navCellSizeCm = 6250;
        var json = TestDefaults.DefaultConfigJson();
        json["world"]!["navCellSizeCm"] = navCellSizeCm;
        json["fog"]!["cellSizeCm"] = navCellSizeCm * 33;
        var map = TestDefaults.DemoMap();
        int widthCm = navCellSizeCm * 16;
        map.Boards[0].WidthCm = widthCm;
        map.Boards[0].HeightCm = widthCm;
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(CrowdSimulationConfig.Load(json), map, pairRules: TestDefaults.DemoPairRules()));
        Assert.That(ex!.Message, Does.Contain("fog.cellSizeCm"));
        Assert.That(ex.Message, Does.Contain("= 33"));
        Assert.That(ex.Message, Does.Contain("≤ 32"));
    }

    [Test]
    public void VisionRadiusCells_BeyondDiagonal_IsRejected_AtDiagonal_Passes()
    {
        // 现网:visionCm 80000、迷雾格 25000 cm、每边迷雾格 256/4=64,半径 4 格。
        var live = TestDefaults.Assemble();
        Assert.That(live.Fog.VisionCm, Is.EqualTo(Fix64.FromInt(80000)));
        Assert.That(live.Fog.FogCellSizeCm, Is.EqualTo(25000));
        Assert.That(live.NavCellSizeCm * live.Fog.NavCellsPerFogCell, Is.EqualTo(25000));
        Assert.That((live.NavCellsPerSide + live.Fog.NavCellsPerFogCell - 1) / live.Fog.NavCellsPerFogCell, Is.EqualTo(64));

        // 导航 16、迷雾格 = 导航格 → 每边迷雾格 16。对角 (F-1)*√2:21²=441 < 2×15²=450 ≤ 22²=484,上限 22。
        // 格长 100 cm;vision 2200 → ceil=22,2300 → ceil=23。
        var over = FogCellWorld(navCellSizeCm: 100, fogCellSizeCm: 100, visionCm: 2300);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(over.Config, over.Map, pairRules: TestDefaults.DemoPairRules()));
        Assert.That(ex!.Message, Does.Contain("fog.visionCm"));
        Assert.That(ex.Message, Does.Contain("2300"));
        Assert.That(ex.Message, Does.Contain("fog.cellSizeCm"));
        Assert.That(ex.Message, Does.Contain("向上取整 = 23"));
        Assert.That(ex.Message, Does.Contain("22"));

        var atDiagonal = FogCellWorld(navCellSizeCm: 100, fogCellSizeCm: 100, visionCm: 2200);
        var runtime = TestDefaults.Assemble(atDiagonal.Config, atDiagonal.Map, pairRules: TestDefaults.DemoPairRules());
        Assert.That(runtime.Fog.VisionCm, Is.EqualTo(Fix64.FromInt(2200)));
        Assert.That(runtime.Fog.FogCellSizeCm, Is.EqualTo(100));
        Assert.That(runtime.NavCellSizeCm * runtime.Fog.NavCellsPerFogCell, Is.EqualTo(100));
    }

    private static (CrowdSimulationConfig Config, MapConfig Map) FogCellWorld(int navCellSizeCm, int fogCellSizeCm, int visionCm)
    {
        var json = TestDefaults.DefaultConfigJson();
        json["world"]!["navCellSizeCm"] = navCellSizeCm;
        json["fog"]!["cellSizeCm"] = fogCellSizeCm;
        json["fog"]!["visionCm"] = visionCm;
        // 16 个导航格恰好一块 tile,派生格数落在 4..128。
        json["navtile"]!["sizeCm"] = navCellSizeCm * 16;
        var map = TestDefaults.DemoMap();
        int widthCm = navCellSizeCm * 16;
        map.Boards[0].WidthCm = widthCm;
        map.Boards[0].HeightCm = widthCm;
        return (CrowdSimulationConfig.Load(json), map);
    }

    [Test]
    public void IdKeyedSections_RejectArrayAndNull()
    {
        var asArray = TestDefaults.DefaultConfigJson();
        asArray["formations"] = new JsonArray
        {
            JsonNode.Parse("""{"id":"box","label":"方阵","spacingDiameterScale":1,"aspect":1,"wedge":false}""")!,
        };
        var arrayEx = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(asArray));
        Assert.That(arrayEx!.Message, Does.Contain("以 id 为键的对象，不再使用数组"));

        var nulled = TestDefaults.DefaultConfigJson();
        nulled["formations"]!["box"] = null;
        var nullEx = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(nulled));
        Assert.That(nullEx!.Message, Does.Contain("formations.box"));
        Assert.That(nullEx.Message, Does.Contain("为 null"));

        var sectionNull = TestDefaults.DefaultConfigJson();
        sectionNull["formations"] = null;
        var sectionEx = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(sectionNull));
        Assert.That(sectionEx!.Message, Does.Contain("formations"));
        Assert.That(sectionEx.Message, Does.Contain("不接受 null"));
    }

    [Test]
    public void FormationOverride_MergesOneKey()
    {
        var merged = TestDefaults.DefaultConfigJson();
        ConfigPipeline.DeepMerge(merged, JsonNode.Parse("""{ "formations": { "wedge": { "aspect": 3.5 } } }""")!.AsObject());
        var runtime = TestDefaults.Assemble(CrowdSimulationConfig.Load(merged));
        var wedge = runtime.Formations.Single(f => f.Id == "wedge");
        Assert.That(wedge.Aspect, Is.EqualTo(Fix64.FromDouble(3.5)));
        Assert.That(runtime.Formations[0].Id, Is.EqualTo("box"));
    }
}