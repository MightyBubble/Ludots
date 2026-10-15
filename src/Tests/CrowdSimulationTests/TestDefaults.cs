using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;

namespace CrowdSimulationTests;

/// <summary>
/// S0 测试夹具：标准参数 Mod 的全量参数，叠上演示内容 Mod 的桥锚地形和结构模板，
/// 再叠无头演示图的地图事实（开局单位数、出生点、s7barrier）。通行矩阵是标准参数 Mod 与演示内容 Mod 两份
/// traversal.json 的合并，地图的导航格边长和地形顺序在 fixtures/demo-navmesh.json，单位模板来自演示内容 Mod。
/// </summary>
public static class TestDefaults
{
    public const int FixedHz = 30;
    public const int WorldCm = 16000 * 100;

    public static JsonObject DefaultConfigJson()
    {
        var merged = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "CrowdSimulationConfig.json")))!;
        ConfigPipeline.DeepMerge(merged, (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "crowddemo", "CrowdSimulationConfig.json")))!);
        var facts = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("fixtures", "demo-map-facts.json")))!;
        ConfigPipeline.DeepMerge(merged, facts);
        return merged;
    }

    public static CrowdSimulationConfig DefaultConfig()
        => CrowdSimulationConfig.Load(DefaultConfigJson());

    public static JsonObject StandardNavmeshJson()
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "navmesh.json")))!;

    public static JsonObject DefaultNavmeshJson()
    {
        var merged = StandardNavmeshJson();
        var facts = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("fixtures", "demo-navmesh.json")))!;
        ConfigPipeline.DeepMerge(merged, facts);
        return merged;
    }

    public static JsonObject DefaultTraversalJson()
    {
        var merged = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "traversal.json")))!;
        ConfigPipeline.DeepMerge(merged, (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "crowddemo", "CrowdSimulation", "traversal.json")))!);
        return merged;
    }

    public static NavMeshBakeConfig ParseNavmesh(JsonObject json, AgentProfileRegistry? profiles = null)
        => NavMeshBakeConfigLoader.Parse(json, profiles ?? DemoProfiles());

    public static CrowdTraversalConfig ParseTraversal(JsonObject json)
        => CrowdTraversalConfig.Parse(json);

    public static NavMeshBakeConfig DemoNavmesh()
        => ParseNavmesh(DefaultNavmeshJson());

    public static CrowdTraversalConfig DemoTraversal()
        => ParseTraversal(DefaultTraversalJson());

    public static List<CrowdUnitTemplateFacts> DemoTemplates()
    {
        var templates = JsonSerializer.Deserialize<List<EntityTemplate>>(
            File.ReadAllText(Path.Combine("assets", "crowddemo", "Entities", "templates.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return CrowdUnitTemplateFacts.FromTemplates(templates);
    }

    public static void SetNavCellSizeCm(JsonObject navmesh, int navCellSizeCm)
    {
        foreach (var node in navmesh["maps"]!.AsObject())
        {
            if (node.Value?["navCellSizeCm"] != null)
            {
                node.Value["navCellSizeCm"] = navCellSizeCm;
            }
        }
    }

    public static MapConfig DemoMap()
    {
        return new MapConfig
        {
            Id = "crowd_simulation_demo",
            ContinuousHeightmap = new Ludots.Core.Presentation.Terrain.ContinuousHeightmapBindingConfig
            {
                Asset = "assets/terrain/crowd_simulation_demo.height",
            },
            Boards = new List<BoardConfig>
            {
                new BoardConfig { WidthCm = WorldCm, HeightCm = WorldCm },
            },
            Teams = new List<TeamBindingData>
            {
                new TeamBindingData { TeamId = 1, RepresentativeInstanceId = "team_1" },
                new TeamBindingData { TeamId = 2, RepresentativeInstanceId = "team_2" },
            },
            Players = new List<PlayerBindingData>
            {
                new PlayerBindingData { PlayerId = 1, TeamId = 1, RepresentativeInstanceId = "player_1" },
                new PlayerBindingData { PlayerId = 2, TeamId = 2, RepresentativeInstanceId = "player_2" },
                new PlayerBindingData { PlayerId = 3, TeamId = 1, RepresentativeInstanceId = "player_3" },
                new PlayerBindingData { PlayerId = 4, TeamId = 2, RepresentativeInstanceId = "player_4" },
            },
        };
    }

    /// <summary>20 个体型：foot / hull / climber / amphib / leaper × R100–R400（读标准参数 Mod 的档案文件）。</summary>
    public static AgentProfileRegistry DemoProfiles()
    {
        var list = JsonSerializer.Deserialize<List<AgentProfileConfig>>(
            File.ReadAllText(Path.Combine("assets", "agent_profiles.json")),
            StrictJsonOptions.CreateCamelCase())!;
        return new AgentProfileRegistry(list);
    }

    /// <summary>
    /// 演示图 / 三张种子图的字面 4×4。表序是玩家 1..4，队伍 1/2/1/2。
    /// 不按 TeamId 推导。种子图投影要对上这张表，不能再写一套“同队即让路”。
    /// </summary>
    public static readonly CrowdPushResponse[] DemoResponse =
    {
        CrowdPushResponse.Yield, CrowdPushResponse.Hold, CrowdPushResponse.Yield, CrowdPushResponse.Hold,
        CrowdPushResponse.Hold, CrowdPushResponse.Yield, CrowdPushResponse.Hold, CrowdPushResponse.Yield,
        CrowdPushResponse.Yield, CrowdPushResponse.Hold, CrowdPushResponse.Yield, CrowdPushResponse.Hold,
        CrowdPushResponse.Hold, CrowdPushResponse.Yield, CrowdPushResponse.Hold, CrowdPushResponse.Yield,
    };

    public static readonly bool[] DemoShares =
    {
        true, false, true, false,
        false, true, false, true,
        true, false, true, false,
        false, true, false, true,
    };

    /// <summary>上表两个方向都共享之后的视野组。下标是玩家表序。</summary>
    public static readonly byte[] DemoVisionGroups = { 0, 1, 0, 1 };

    public static CrowdPairRules DemoPairRules()
    {
        var response = new CrowdPushResponse[DemoResponse.Length];
        var shares = new bool[DemoShares.Length];
        DemoResponse.CopyTo(response, 0);
        DemoShares.CopyTo(shares, 0);
        return CrowdPairRules.Matrices(4, response, shares);
    }

    public static string[] LinkKindIds(CrowdSimulationRuntimeConfig config)
        => config.LinkCandidateBakes.Select(b => b.KindId).ToArray();

    public static CrowdSimulationRuntimeConfig Assemble(
        CrowdSimulationConfig? config = null,
        MapConfig? map = null,
        AgentProfileRegistry? profiles = null,
        CrowdPairRules? pairRules = null,
        NavMeshBakeConfig? navmesh = null,
        CrowdTraversalConfig? traversal = null,
        IReadOnlyList<CrowdUnitTemplateFacts>? templates = null)
    {
        bool implicitDemoMap = map == null;
        map ??= DemoMap();
        if (pairRules == null)
        {
            if (!implicitDemoMap)
            {
                throw new InvalidOperationException(
                    "功能位矩阵必须显式传入。演示图的字面表只在不传地图时使用，其它地图不能按队伍号推导。");
            }

            pairRules = DemoPairRules();
        }

        profiles ??= DemoProfiles();
        return CrowdSimulationConfigLoader.Load(
            config ?? DefaultConfig(),
            map,
            profiles,
            navmesh ?? DemoNavmesh(),
            traversal ?? DemoTraversal(),
            templates ?? DemoTemplates(),
            FixedHz,
            pairRules);
    }
}
