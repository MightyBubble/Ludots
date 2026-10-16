using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh.Config;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>S0 验收：配置能加载，写错会报错（每条错误写明文件、字段路径、规则）。</summary>
[TestFixture]
public class CrowdSimulationConfigTests
{
    [Test]
    public void PlanTimeoutMs_Absent_FailsLoad()
    {
        JsonObject json = TestDefaults.DefaultConfigJson();
        json["worker"]!.AsObject().Remove("planTimeoutMs");
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfig.Load(json))!;
        Assert.That(ex.Message, Does.Contain("planTimeoutMs"));
    }

    [Test]
    public void PlanTimeoutMs_MustBePositive()
    {
        JsonObject json = TestDefaults.DefaultConfigJson();
        json["worker"]!["planTimeoutMs"] = 0;
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfig.Load(json))!;
        Assert.That(ex.Message, Does.Contain("worker.planTimeoutMs"));
        Assert.That(ex.Message, Does.Contain("> 0"));
    }

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
        Assert.That(runtime.Profiles.Where(p => p.AgentTypeIndex == foot.AgentTypeIndex).Select(p => p.SpeedCmPerSec),
            Is.All.EqualTo(Fix64.FromInt(1400)));
        // road 是合并后第 5 个区域；foot 对 road 代价 0.7。
        Assert.That(foot.CostByArea[5], Is.EqualTo(Fix64.FromDouble(0.7)));
        // deep 是第 4 个区域，foot 这一格写 false，代价为 0。
        Assert.That(foot.CostByArea[4], Is.EqualTo(Fix64.Zero));

        Assert.That(runtime.Fog.VisionCm, Is.EqualTo(Fix64.FromInt(80000)));
        Assert.That(runtime.Formation.MagicBoxMaxSpreadCm, Is.EqualTo(Fix64.FromInt(250000)));
        // 领队继承阈值与参考实现内联字面量同值(反转 -0.5 / 同向 0.94)。
        Assert.That(runtime.Formation.HeadingInheritDot, Is.EqualTo(Fix64.FromDouble(0.94)));
        Assert.That(runtime.Formation.MirrorFlipDot, Is.EqualTo(Fix64.FromDouble(-0.5)));
        Assert.That(runtime.Deploy.Bases[0].XCm, Is.EqualTo(480000));
        Assert.That(runtime.Deploy.Bases[0].YCm, Is.EqualTo(560000));

        var leaper = runtime.NavProfiles[4];
        Assert.That(leaper.Link, Is.Not.Null);
        Assert.That(leaper.Link!.UpCm, Is.EqualTo(2500));
        Assert.That(leaper.Link.DownCm, Is.EqualTo(20000));
        Assert.That(leaper.Link.RangeNavCells, Is.EqualTo(7));
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
    public void MixedBaseColors_AreRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        ((JsonObject)json["deploy"]!["bases"]![1]!).Remove("color");
        var config = CrowdSimulationConfig.Load(json);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(config));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationConfig.json"));
        Assert.That(ex.Message, Does.Contain("deploy.bases[1].color"));
    }

    [Test]
    public void BasesWithoutColors_StillAssemble()
    {
        var json = TestDefaults.DefaultConfigJson();
        foreach (var basis in json["deploy"]!["bases"]!.AsArray())
        {
            ((JsonObject)basis!).Remove("color");
        }

        var runtime = TestDefaults.Assemble(CrowdSimulationConfig.Load(json));
        Assert.That(runtime.Deploy.Bases.Count, Is.GreaterThan(1));
        foreach (var basis in runtime.Deploy.Bases)
        {
            Assert.That(basis.Color, Is.Null);
        }
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
        var traversalJson = TestDefaults.DefaultTraversalJson();
        traversalJson["agentTypes"]!["foot"]!["cells"]!["lava"] = JsonNode.Parse(
            """{"cost":1,"speedDivisor":1,"depart":[],"arrive":[]}""");
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.ParseTraversal(traversalJson));
        Assert.That(ex!.Message, Does.Contain("agentTypes.foot.cells.lava 不是 areas 里的 id"));
    }

    [Test]
    public void TemplateNavProfile_NotInTraversal_IsRejected()
    {
        var templates = TestDefaults.DemoTemplates();
        var first = templates[0];
        var profiles = TestDefaults.DemoProfiles();
        var list = new List<Ludots.Core.Navigation.AgentProfiles.AgentProfileConfig>(profiles.Count);
        for (int i = 0; i < profiles.Count; i++)
        {
            list.Add(profiles[i]);
        }

        list.Single(profile => profile.Id == first.ProfileId).NavProfileId = "ghost";
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(
            profiles: new Ludots.Core.Navigation.AgentProfiles.AgentProfileRegistry(list),
            templates: templates));
        Assert.That(ex!.Message, Does.Contain("navProfileId"));
        Assert.That(ex.Message, Does.Contain("ghost"));
        Assert.That(ex.Message, Does.Contain(first.TemplateId));
    }

    [Test]
    public void PathingAgentType_WithoutTemplate_IsNotBound()
    {
        var templates = TestDefaults.DemoTemplates();
        var profiles = TestDefaults.DemoProfiles();
        templates.RemoveAll(item => profiles.Require(item.ProfileId, item.TemplateId).NavProfileId == "foot");
        var runtime = TestDefaults.Assemble(templates: templates);
        Assert.That(runtime.AgentTypes.Any(agent => agent.Id == "foot"), Is.False);
        Assert.That(runtime.AgentTypes[0].Id, Is.EqualTo("hull"));
        Assert.That(runtime.Profiles.Count, Is.EqualTo(16));
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
    public void RetiredVersionKey_IsRejected()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["version"] = 6;
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain("已删除 version"));
        Assert.That(ex.Message, Does.Contain("配置不再带版本号，由严格 schema 与 mod.json semver 管理"));
    }

    [Test]
    public void TerrainAreas_UnknownArea_IsRejected()
    {
        var traversalJson = TestDefaults.DefaultTraversalJson();
        traversalJson["terrainAreas"]!["cliff"] = "lava";
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.ParseTraversal(traversalJson));
        Assert.That(ex!.Message, Does.Contain("terrainAreas.cliff = \"lava\" 不是 areas 里的区域"));
    }

    [Test]
    public void MapTerrainType_WithoutTerrainArea_IsRejected()
    {
        var navmesh = TestDefaults.DefaultNavmeshJson();
        navmesh["maps"]!["crowd_simulation_demo"]!["terrainTypes"]!.AsArray().Add("lava");
        var ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.Assemble(navmesh: TestDefaults.ParseNavmesh(navmesh)));
        Assert.That(ex!.Message, Does.Contain("terrainTypes[5] = \"lava\" 不在 CrowdSimulation/traversal.json 的 terrainAreas 里"));
    }

    [Test]
    public void JumpCost_BelowMinAreaCost_IsRejected()
    {
        var traversalJson = TestDefaults.DefaultTraversalJson();
        traversalJson["agentTypes"]!["leaper"]!["links"]!["jump"]!["costPerNavCell"] = 0.5;

        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.ParseTraversal(traversalJson));
        Assert.That(ex!.Message, Does.Contain("agentTypes.leaper.links.jump.costPerNavCell = 0.5，低于本行可通行格的最低 cost 0.8"));
    }

    [Test]
    public void PairRules_UnorderedPlayerIds_KeepTheFedMatrix()
    {
        // 装载不从队伍号生成矩阵，只把喂进来的矩阵按下标挂上。
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
        ConfigPipeline.DeepMerge(merged, JsonNode.Parse("""{ "formation": { "leaderSpeedRatio": 0.6 } }""")!.AsObject());

        var config = CrowdSimulationConfig.Load(merged);
        var runtime = TestDefaults.Assemble(config);

        Assert.That(runtime.Formation.LeaderSpeedRatio, Is.EqualTo(Fix64.FromDouble(0.6)));
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
            new[] { "bridge", "building", "fire", "flood", "road", "s7barrier", "timedBridge" }));
        var byId = templates.ToDictionary(t => t.Id);
        int Area(string id) => runtime.NavAreas.Select((a, i) => (a.Id, i)).Single(a => a.Id == id).i;

        var building = byId["building"];
        Assert.That(building.Layer, Is.EqualTo(CrowdStructureLayer.Base));
        Assert.That(building.Footprint, Is.EqualTo(CrowdStructureShape.Square));
        Assert.That(building.Effect, Is.EqualTo(CrowdStructureEffectKind.Block));
        Assert.That(building.BlockCoverage, Is.EqualTo(Fix64.FromDouble(0.5)));
        Assert.That(building.AreaIndex, Is.Null);
        Assert.That(building.Placement, Is.EqualTo(CrowdStructurePlacementKind.Unrestricted));
        Assert.That(building.LifetimeSec, Is.Null);

        foreach (string id in new[] { "bridge", "timedBridge" })
        {
            var bridge = byId[id];
            Assert.That(bridge.Layer, Is.EqualTo(CrowdStructureLayer.Upper), id);
            Assert.That(bridge.Footprint, Is.EqualTo(CrowdStructureShape.Path), id);
            Assert.That(bridge.Effect, Is.EqualTo(CrowdStructureEffectKind.Surface), id);
            Assert.That(bridge.AreaIndex, Is.EqualTo(Area("deck")), id);
            Assert.That(bridge.PortalReachNavCells, Is.EqualTo(4), id);
            Assert.That(bridge.Placement, Is.EqualTo(CrowdStructurePlacementKind.SpanEnds), id);
            Assert.That(Enumerable.Range(0, bridge.EndpointAreas.Length).Where(a => bridge.EndpointAreas[a]).ToArray(),
                Is.EquivalentTo(new[] { Area("shallow"), Area("open") }), id);
            Assert.That(Enumerable.Range(0, bridge.InteriorAreas.Length).Where(a => bridge.InteriorAreas[a]).ToArray(),
                Is.EqualTo(new[] { Area("deep") }), id);
        }

        Assert.That(byId["bridge"].LifetimeSec, Is.Null);
        Assert.That(byId["timedBridge"].LifetimeSecRaw, Is.EqualTo(4.0));
        Assert.That(byId["road"].Effect, Is.EqualTo(CrowdStructureEffectKind.SetArea));
        Assert.That(byId["road"].Footprint, Is.EqualTo(CrowdStructureShape.Path));
        Assert.That(byId["road"].AreaIndex, Is.EqualTo(Area("road")));
        Assert.That(byId["road"].Priority, Is.EqualTo(10));
        Assert.That(byId["flood"].Footprint, Is.EqualTo(CrowdStructureShape.Disc));
        Assert.That(byId["flood"].Priority, Is.EqualTo(20));
        Assert.That(byId["fire"].Priority, Is.EqualTo(40));
        Assert.That(byId["fire"].LifetimeSecRaw, Is.EqualTo(900.0));
        Assert.That(byId["s7barrier"].Blocks, Is.True);
        Assert.That(byId["s7barrier"].BlockCoverage, Is.EqualTo(Fix64.FromDouble(0.5)));
        Assert.That(byId["s7barrier"].LifetimeSec, Is.EqualTo(Fix64.FromDouble(4)));
        Assert.That(byId["s7barrier"].LifetimeSecRaw, Is.EqualTo(4.0));
    }

    [TestCase("blockCoverage", "0.5", "effect.coverage")]
    [TestCase("portalReachNavCells", "4", "effect.portalReachNavCells")]
    [TestCase("anchorTerrain", "[\"land\"]", "placement.endpointAreas")]
    [TestCase("spanTerrain", "[\"water\"]", "placement.interiorAreas")]
    public void StructureSection_RetiredKey_NamesItsReplacement(string key, string value, string replacement)
    {
        var json = TestDefaults.DefaultConfigJson();
        json["structures"]![key] = JsonNode.Parse(value);
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain($"已删除 structures.{key}"));
        Assert.That(ex.Message, Does.Contain(replacement));
    }

    [TestCase("name", "\"X\"")]
    [TestCase("blocker", "true")]
    [TestCase("area", "\"road\"")]
    [TestCase("priority", "1")]
    [TestCase("layered", "true")]
    public void StructureTemplate_RetiredKey_IsRejected(string key, string value)
    {
        var json = WithTemplate(Building);
        json["structures"]!["templates"]!["x"]![key] = JsonNode.Parse(value);
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain($"已删除 structures.templates.x.{key}"));
    }

    [Test]
    public void StructureTemplate_KindAfterOtherKeys_Loads()
    {
        var json = WithTemplate("""
            {"layer":"base","footprint":"square","effect":{"coverage":0.25,"kind":"block"},
             "placement":{"kind":"unrestricted"},"lifetimeSec":null}
            """);
        var runtime = TestDefaults.Assemble(CrowdSimulationConfig.Load(json));
        Assert.That(runtime.Structures.Templates.Single(t => t.Id == "x").BlockCoverage, Is.EqualTo(Fix64.FromDouble(0.25)));
    }

    [Test]
    public void StructureTemplate_LifetimeSecMissing_IsRejected()
    {
        var json = WithTemplate(Building);
        json["structures"]!["templates"]!["x"]!.AsObject().Remove("lifetimeSec");
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain("lifetimeSec"));
    }

    [TestCase("effect", """{"kind":"wall"}""", "effect.kind 需为 block / setArea / surface")]
    [TestCase("effect", """{"coverage":0.5}""", "effect.kind 需为 block / setArea / surface")]
    [TestCase("placement", """{"kind":"anywhere"}""", "placement.kind 需为 unrestricted / spanEnds")]
    public void StructureTemplate_UnknownKind_IsRejected(string key, string value, string message)
    {
        var json = WithTemplate(Building);
        json["structures"]!["templates"]!["x"]![key] = JsonNode.Parse(value);
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationConfig.Load(json));
        Assert.That(ex!.Message, Does.Contain($"structures.templates.x.{message}"));
    }

    private const string Building = """
        {"layer":"base","footprint":"square","effect":{"kind":"block","coverage":0.5},
         "placement":{"kind":"unrestricted"},"lifetimeSec":null}
        """;

    private const string SpanEnds = """{"kind":"spanEnds","endpointAreas":["shallow","open"],"interiorAreas":["deep"]}""";

    [TestCase("upper", "square", """{"kind":"surface","area":"deck","portalReachNavCells":4}""", SpanEnds)]
    [TestCase("base", "disc", """{"kind":"block","coverage":0.5}""", """{"kind":"unrestricted"}""")]
    [TestCase("base", "path", """{"kind":"block","coverage":0.5}""", """{"kind":"unrestricted"}""")]
    [TestCase("base", "path", """{"kind":"setArea","area":"road","priority":1}""", SpanEnds)]
    [TestCase("base", "path", """{"kind":"surface","area":"deck","portalReachNavCells":4}""", SpanEnds)]
    [TestCase("upper", "path", """{"kind":"setArea","area":"road","priority":1}""", SpanEnds)]
    [TestCase("upper", "path", """{"kind":"surface","area":"deck","portalReachNavCells":4}""", """{"kind":"unrestricted"}""")]
    public void StructureTemplate_CombinationOutsideTable_IsRejected(string layer, string footprint, string effect, string placement)
    {
        var json = WithTemplate($$"""
            {"layer":"{{layer}}","footprint":"{{footprint}}","effect":{{effect}},"placement":{{placement}},"lifetimeSec":null}
            """);
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("不是允许的组合"));
    }

    [TestCase("layer", "\"middle\"", "layer")]
    [TestCase("footprint", "\"rect\"", "footprint")]
    [TestCase("lifetimeSec", "0", "lifetimeSec")]
    [TestCase("effect", """{"kind":"block","coverage":0}""", "effect.coverage")]
    [TestCase("effect", """{"kind":"block","coverage":1.5}""", "effect.coverage")]
    public void StructureTemplate_FieldOutOfRange_IsRejected(string key, string value, string path)
    {
        var json = WithTemplate(Building);
        json["structures"]!["templates"]!["x"]![key] = JsonNode.Parse(value);
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain($"structures.templates.x.{path}"));
    }

    [Test]
    public void StructureTemplate_SurfaceAndSetAreaRanges_AreRejected()
    {
        var json = WithTemplate($$"""
            {"layer":"upper","footprint":"path","effect":{"kind":"surface","area":"deck","portalReachNavCells":0},
             "placement":{{SpanEnds}},"lifetimeSec":null}
            """);
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("effect.portalReachNavCells"));

        json = WithTemplate("""
            {"layer":"base","footprint":"disc","effect":{"kind":"setArea","area":"road","priority":-1},
             "placement":{"kind":"unrestricted"},"lifetimeSec":null}
            """);
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("effect.priority"));

        json = WithTemplate("""
            {"layer":"upper","footprint":"path","effect":{"kind":"surface","area":"deck","portalReachNavCells":4},
             "placement":{"kind":"spanEnds","endpointAreas":["open","open"],"interiorAreas":["deep"]},"lifetimeSec":null}
            """);
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("placement.endpointAreas"));
        Assert.That(ex.Message, Does.Contain("重复"));

        json = WithTemplate("""
            {"layer":"upper","footprint":"path","effect":{"kind":"surface","area":"deck","portalReachNavCells":4},
             "placement":{"kind":"spanEnds","endpointAreas":["open"],"interiorAreas":[]},"lifetimeSec":null}
            """);
        ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationConfigValidator.Validate(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain("placement.interiorAreas"));
    }

    [TestCase("""{"kind":"setArea","area":"nowhere","priority":1}""", """{"kind":"unrestricted"}""", "effect.area")]
    [TestCase("""{"kind":"surface","area":"deck","portalReachNavCells":4}""", """{"kind":"spanEnds","endpointAreas":["lava"],"interiorAreas":["deep"]}""", "placement.endpointAreas[0]")]
    [TestCase("""{"kind":"surface","area":"deck","portalReachNavCells":4}""", """{"kind":"spanEnds","endpointAreas":["open"],"interiorAreas":["lava"]}""", "placement.interiorAreas[0]")]
    public void StructureTemplate_UnknownArea_IsRejected(string effect, string placement, string path)
    {
        string layer = effect.Contains("surface") ? "upper" : "base";
        string footprint = effect.Contains("surface") ? "path" : "disc";
        var json = WithTemplate($$"""
            {"layer":"{{layer}}","footprint":"{{footprint}}","effect":{{effect}},"placement":{{placement}},"lifetimeSec":null}
            """);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(CrowdSimulationConfig.Load(json)));
        Assert.That(ex!.Message, Does.Contain($"structures.templates.x.{path}"));
        Assert.That(ex.Message, Does.Contain("未知导航区域"));
    }

    [Test]
    public void StructureTemplate_UpperArea_OnlyForSurface()
    {
        var setArea = WithTemplate("""
            {"layer":"base","footprint":"disc","effect":{"kind":"setArea","area":"deck","priority":1},
             "placement":{"kind":"unrestricted"},"lifetimeSec":null}
            """, keepDefaults: true);
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(CrowdSimulationConfig.Load(setArea)));
        Assert.That(ex!.Message, Does.Contain("structures.templates.x.effect.area \"deck\" 是上层区域"));

        var endpoint = WithTemplate("""
            {"layer":"upper","footprint":"path","effect":{"kind":"surface","area":"deck","portalReachNavCells":4},
             "placement":{"kind":"spanEnds","endpointAreas":["deck"],"interiorAreas":["deep"]},"lifetimeSec":null}
            """);
        ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(CrowdSimulationConfig.Load(endpoint)));
        Assert.That(ex!.Message, Does.Contain("placement.endpointAreas[0] = \"deck\" 是上层区域"));
    }

    private static JsonObject WithTemplate(string template, bool keepDefaults = false)
    {
        var json = TestDefaults.DefaultConfigJson();
        var templates = keepDefaults ? json["structures"]!["templates"]!.AsObject() : new JsonObject();
        templates["x"] = JsonNode.Parse(template);
        json["structures"]!["templates"] = templates;
        return json;
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

        const string templateKey = "structures.templates[].";
        if (oldPath.StartsWith(templateKey, StringComparison.Ordinal))
        {
            string key = oldPath[templateKey.Length..];
            root["structures"]!["templates"]!["building"]![key] = 1;
            return "structures.templates.building." + key;
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
        Assert.That(runtime.NavCellSizeCm, Is.EqualTo(navCellSizeCm));
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
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(below.Config, below.Map, pairRules: TestDefaults.DemoPairRules(), navmesh: below.Navmesh));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationConfig.json"));
        Assert.That(ex.Message, Does.Contain("fog.cellSizeCm"));
        Assert.That(ex.Message, Does.Contain("= 99"));
        Assert.That(ex.Message, Does.Contain("≥ 100"));

        var atBound = FogCellWorld(navCellSizeCm: 100, fogCellSizeCm: 100, visionCm: 100);
        var runtime = TestDefaults.Assemble(atBound.Config, atBound.Map, pairRules: TestDefaults.DemoPairRules(), navmesh: atBound.Navmesh);
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
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(over.Config, over.Map, pairRules: TestDefaults.DemoPairRules(), navmesh: over.Navmesh));
        Assert.That(ex!.Message, Does.Contain("fog.visionCm"));
        Assert.That(ex.Message, Does.Contain("2300"));
        Assert.That(ex.Message, Does.Contain("fog.cellSizeCm"));
        Assert.That(ex.Message, Does.Contain("向上取整 = 23"));
        Assert.That(ex.Message, Does.Contain("22"));

        var atDiagonal = FogCellWorld(navCellSizeCm: 100, fogCellSizeCm: 100, visionCm: 2200);
        var runtime = TestDefaults.Assemble(atDiagonal.Config, atDiagonal.Map, pairRules: TestDefaults.DemoPairRules(), navmesh: atDiagonal.Navmesh);
        Assert.That(runtime.Fog.VisionCm, Is.EqualTo(Fix64.FromInt(2200)));
        Assert.That(runtime.Fog.FogCellSizeCm, Is.EqualTo(100));
        Assert.That(runtime.NavCellSizeCm * runtime.Fog.NavCellsPerFogCell, Is.EqualTo(100));
    }

    private static (CrowdSimulationConfig Config, MapConfig Map, NavMeshBakeConfig Navmesh) FogCellWorld(int navCellSizeCm, int fogCellSizeCm, int visionCm)
    {
        var json = TestDefaults.DefaultConfigJson();
        json["fog"]!["cellSizeCm"] = fogCellSizeCm;
        json["fog"]!["visionCm"] = visionCm;
        // 16 个导航格恰好一块 tile,派生格数落在 4..128。
        json["navtile"]!["sizeCm"] = navCellSizeCm * 16;
        var navmesh = TestDefaults.DefaultNavmeshJson();
        TestDefaults.SetNavCellSizeCm(navmesh, navCellSizeCm);
        var map = TestDefaults.DemoMap();
        int widthCm = navCellSizeCm * 16;
        map.Boards[0].WidthCm = widthCm;
        map.Boards[0].HeightCm = widthCm;
        return (CrowdSimulationConfig.Load(json), map, TestDefaults.ParseNavmesh(navmesh));
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