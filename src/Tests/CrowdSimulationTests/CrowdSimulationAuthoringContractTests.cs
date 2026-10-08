using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>S0 验收的模板级契约：阻挡物形状、双求解器组件互斥、模板 profileId 引用闭包。
/// 契约本体在 CrowdSimulationRuntime 进图激活时执行（见 ActivationPath_InvokesTemplateContract）。</summary>
[TestFixture]
public class CrowdSimulationAuthoringContractTests
{
    private static EntityTemplate Template(string id, params string[] componentNames)
    {
        var components = new Dictionary<string, JsonNode>();
        foreach (var name in componentNames)
        {
            components[name] = name switch
            {
                "ManifestationObstacleIntent2D" => JsonNode.Parse("""{ "shape": "Box", "sinkNavigationObstacle": true, "halfWidthCm": 7000, "halfHeightCm": 7000 }""")!,
                "CrowdSimulationAgent" => JsonNode.Parse("""{ "profileId": "foot_r200" }""")!,
                "MassNavigationAgent" => JsonNode.Parse("""{ "profileId": "foot_r200" }""")!,
                _ => JsonNode.Parse("{}")!,
            };
        }

        return new EntityTemplate { Id = id, Components = components };
    }

    [Test]
    public void SquareBoxBlocker_Passes()
    {
        var templates = new[] { Template("blocker_140m", "ManifestationObstacleIntent2D") };
        var map = TestDefaults.DemoMap();
        map.Entities.Add(new EntitySpawnData { InstanceId = "b1", Template = "blocker_140m" });
        Assert.DoesNotThrow(() =>
            CrowdSimulationAuthoringContract.Validate(templates, TestDefaults.DefaultConfig(), map, TestDefaults.DemoProfiles()));
    }

    [Test]
    public void CircleBlocker_IsRejected()
    {
        var t = Template("round_blocker", "ManifestationObstacleIntent2D");
        t.Components["ManifestationObstacleIntent2D"] = JsonNode.Parse("""{ "shape": "Circle", "sinkNavigationObstacle": true, "radiusCm": 7000 }""")!;
        var map = TestDefaults.DemoMap();
        map.Entities.Add(new EntitySpawnData { InstanceId = "b1", Template = "round_blocker" });
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdSimulationAuthoringContract.Validate(new[] { t }, TestDefaults.DefaultConfig(), map, TestDefaults.DemoProfiles()));
        Assert.That(ex!.Message, Does.Contain("阻挡物只能是 Box"));
    }

    [Test]
    public void NonSquareBoxBlocker_IsRejected()
    {
        var t = Template("rect_blocker", "ManifestationObstacleIntent2D");
        t.Components["ManifestationObstacleIntent2D"] = JsonNode.Parse("""{ "shape": "Box", "sinkNavigationObstacle": true, "halfWidthCm": 7000, "halfHeightCm": 3500 }""")!;
        var map = TestDefaults.DemoMap();
        map.Entities.Add(new EntitySpawnData { InstanceId = "b1", Template = "rect_blocker" });
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdSimulationAuthoringContract.Validate(new[] { t }, TestDefaults.DefaultConfig(), map, TestDefaults.DemoProfiles()));
        Assert.That(ex!.Message, Does.Contain("必须是正方形"));
    }

    [Test]
    public void CircleNonBlockingArea_IsAllowed()
    {
        // 不禁止圆形非阻挡区域实体（火场 / 泛洪的足迹语义）。
        var t = Template("fire_zone", "ManifestationObstacleIntent2D");
        t.Components["ManifestationObstacleIntent2D"] = JsonNode.Parse("""{ "shape": "Circle", "sinkNavigationObstacle": false, "radiusCm": 7000 }""")!;
        var map = TestDefaults.DemoMap();
        map.Entities.Add(new EntitySpawnData { InstanceId = "f1", Template = "fire_zone" });
        Assert.DoesNotThrow(() =>
            CrowdSimulationAuthoringContract.Validate(new[] { t }, TestDefaults.DefaultConfig(), map, TestDefaults.DemoProfiles()));
    }

    [Test]
    public void Template_WithBothAgentComponents_IsRejected()
    {
        var t = Template("dual_unit", "CrowdSimulationAgent", "MassNavigationAgent");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdSimulationAuthoringContract.Validate(new[] { t }, TestDefaults.DefaultConfig(), TestDefaults.DemoMap(), TestDefaults.DemoProfiles()));
        Assert.That(ex!.Message, Does.Contain("同时带 CrowdSimulationAgent 与 MassNavigationAgent"));
    }

    [Test]
    public void Template_ReferencingUnknownProfile_IsRejected()
    {
        var t = Template("ghost_unit", "CrowdSimulationAgent");
        t.Components["CrowdSimulationAgent"] = JsonNode.Parse("""{ "profileId": "ghost_r999" }""")!;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdSimulationAuthoringContract.Validate(new[] { t }, TestDefaults.DefaultConfig(), TestDefaults.DemoMap(), TestDefaults.DemoProfiles()));
        Assert.That(ex!.Message, Does.Contain("ghost_r999"));
    }

    [Test]
    public void ActivationPath_InvokesTemplateContract()
    {
        // 激活路径要完整 Mod 宿主（地图聚焦 → 会话激活），单元测试不可达；端到端由真机
        // 冒烟（crowd_simulation_s4_deploy_1337）覆盖。这里钉住 Runtime 激活代码里的契约
        // 调用，防止活接线被静默拆除（契约曾是只有测试调用的死代码）。
        string repoRoot = FindRepoRoot();
        string runtimeSource = File.ReadAllText(
            Path.Combine(repoRoot, "src", "Core", "CrowdSimulation", "Runtime", "CrowdSimulationRuntime.cs"));
        Assert.That(runtimeSource, Does.Contain("CrowdSimulationAuthoringContract.Validate("));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根（src/Core/Ludots.Core.csproj）。");
    }
}
