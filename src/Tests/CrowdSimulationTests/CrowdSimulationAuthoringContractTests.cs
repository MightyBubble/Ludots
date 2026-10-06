using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>S0 验收的模板级契约：阻挡物形状、双求解器组件互斥、部署清单组件要求。</summary>
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

    private static IEnumerable<EntityTemplate> DeployTemplates()
    {
        var config = TestDefaults.DefaultConfig();
        foreach (var id in config.Deploy.Templates)
        {
            yield return Template(id, "CrowdSimulationAgent");
        }
    }

    [Test]
    public void SquareBoxBlocker_Passes()
    {
        var templates = new[] { Template("blocker_140m", "ManifestationObstacleIntent2D") }.Concat(DeployTemplates()).ToArray();
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
        var templates = new[] { t }.Concat(DeployTemplates()).ToArray();
        Assert.DoesNotThrow(() =>
            CrowdSimulationAuthoringContract.Validate(templates, TestDefaults.DefaultConfig(), map, TestDefaults.DemoProfiles()));
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
    public void DeployTemplate_WithoutCrowdSimulationAgent_IsRejected()
    {
        var t = Template("crowd_simulation_infantry_r100"); // 名字在清单里，但没带组件
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdSimulationAuthoringContract.Validate(new[] { t }, TestDefaults.DefaultConfig(), TestDefaults.DemoMap(), TestDefaults.DemoProfiles()));
        Assert.That(ex!.Message, Does.Contain("必须带 CrowdSimulationAgent 组件"));
    }

    [Test]
    public void DeployTemplate_ReferencingUnknownProfile_IsRejected()
    {
        var t = Template("crowd_simulation_infantry_r100", "CrowdSimulationAgent");
        t.Components["CrowdSimulationAgent"] = JsonNode.Parse("""{ "profileId": "ghost_r999" }""")!;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdSimulationAuthoringContract.Validate(new[] { t }, TestDefaults.DefaultConfig(), TestDefaults.DemoMap(), TestDefaults.DemoProfiles()));
        Assert.That(ex!.Message, Does.Contain("ghost_r999"));
    }
}
