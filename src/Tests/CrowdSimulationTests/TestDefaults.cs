using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.AgentProfiles;

namespace CrowdSimulationTests;

/// <summary>
/// S0 测试夹具：标准参数 Mod 的全量参数，叠上无头演示图的地图事实
/// （mapId、地表、出生点、s7barrier）。体型档案仍在能力 Mod。
/// </summary>
public static class TestDefaults
{
    public const int FixedHz = 30;
    public const int WorldCm = 16000 * 100;

    public static JsonObject DefaultConfigJson()
    {
        var merged = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "CrowdSimulationConfig.json")))!;
        var facts = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("fixtures", "demo-map-facts.json")))!;
        ConfigPipeline.DeepMerge(merged, facts);
        return merged;
    }

    public static CrowdSimulationConfig DefaultConfig()
        => CrowdSimulationConfig.Load(DefaultConfigJson());

    public static MapConfig DemoMap()
    {
        return new MapConfig
        {
            Id = "crowd_simulation_demo",
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

    /// <summary>20 个体型：foot / hull / climber / amphib / leaper × R100–R400（读能力 Mod 的档案文件）。</summary>
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

    public static CrowdSimulationRuntimeConfig Assemble(
        CrowdSimulationConfig? config = null,
        MapConfig? map = null,
        AgentProfileRegistry? profiles = null,
        CrowdPairRules? pairRules = null)
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

        return CrowdSimulationConfigLoader.Load(
            config ?? DefaultConfig(),
            map,
            profiles ?? DemoProfiles(),
            FixedHz,
            pairRules);
    }
}
