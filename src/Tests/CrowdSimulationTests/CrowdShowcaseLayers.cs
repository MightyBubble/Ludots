using System;
using System.IO;

namespace CrowdSimulationTests;

/// <summary>
/// 群体演示按玩家能分清的层分开放。测试要用某个 mod 的目录时走这里，找不到就停下。
/// </summary>
internal static class CrowdShowcaseLayers
{
    public static string ModDirectory(string repoRoot, string modId)
        => Path.Combine(repoRoot, "mods", "showcases", LayerOf(modId), modId);

    public static string LayerOf(string modId) => modId switch
    {
        "CrowdSimulationMarchShowcaseMod" or
        "CrowdSimulationReversalShowcaseMod" or
        "CrowdSimulationRepeatClickShowcaseMod" => "crowd_march",
        "CrowdSimulationFogExploreShowcaseMod" or
        "CrowdSimulationFogRerouteShowcaseMod" or
        "CrowdSimulationFogAllyVisionShowcaseMod" => "crowd_fog",
        "CrowdSimulationS3PathMod" => "crowd_path",
        "CrowdSimulationS4DeployMod" => "crowd_placement",
        "CrowdSimulationMinimalMod" => "crowd_wall",
        "CrowdSimulationS1Terrain1337Mod" or
        "CrowdSimulationS1Terrain2024Mod" or
        "CrowdSimulationS1Terrain7Mod" => "crowd_map",
        "CrowdSimulationStress50kShowcaseMod" => "crowd_scale",
        "CrowdSimulationDemoContentMod" or
        "CrowdSimulationShowcaseStageMod" => "crowd_simulation",
        _ => throw new InvalidOperationException($"没有登记群体演示层：{modId}"),
    };
}
