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
/// S0 测试夹具：CrowdSimulationMod 自带的全量默认配置（单一事实源 = Mod 资产文件）、
/// 16 km 演示地图与 5 移动类型 × 4 半径级的代理体型注册表。
/// </summary>
public static class TestDefaults
{
    public const int FixedHz = 30;
    public const int WorldCm = 16000 * 100;

    public static JsonObject DefaultConfigJson()
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "CrowdSimulationConfig.json")))!;

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

    /// <summary>20 个体型：foot / hull / climber / amphib / leaper × R100–R400（读 CrowdSimulationMod 的默认文件）。</summary>
    public static AgentProfileRegistry DemoProfiles()
    {
        var list = JsonSerializer.Deserialize<List<AgentProfileConfig>>(
            File.ReadAllText(Path.Combine("assets", "agent_profiles.json")),
            StrictJsonOptions.CreateCamelCase())!;
        return new AgentProfileRegistry(list);
    }

    public static CrowdSimulationRuntimeConfig Assemble(
        CrowdSimulationConfig? config = null,
        MapConfig? map = null,
        AgentProfileRegistry? profiles = null)
        => CrowdSimulationConfigLoader.Load(
            config ?? DefaultConfig(),
            map ?? DemoMap(),
            profiles ?? DemoProfiles(),
            FixedHz);
}
