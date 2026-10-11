using System;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

[TestFixture]
public sealed class CrowdPresentationConfigTests
{
    [Test]
    public void ShowcaseDebugFiles_Parse()
    {
        var s3 = ParseMod("CrowdSimulationS3PathMod");
        Assert.That(s3.Kind, Is.EqualTo(CrowdPresentationConfig.KindPath));
        Assert.That(s3.ViewLabels, Has.Length.EqualTo(3));
        Assert.That(s3.Path!.TruthAsset, Does.Contain("s3c-query-truth.json"));
        Assert.That(s3.Deploy, Is.Null);

        var s4 = ParseMod("CrowdSimulationS4DeployMod");
        Assert.That(s4.Kind, Is.EqualTo(CrowdPresentationConfig.KindDeploy));
        Assert.That(s4.Deploy!.Structures, Has.Length.EqualTo(3));
        Assert.That(s4.Deploy.Structures[0].Shape, Is.EqualTo(S4DeployDemoRuntime.ShapePoint));
        Assert.That(s4.Deploy.Structures[0].RoadLengthCm, Is.Null);
        Assert.That(s4.Deploy.Structures[2].Shape, Is.EqualTo(S4DeployDemoRuntime.ShapeRoad));
        Assert.That(s4.Deploy.Structures[2].RoadLengthCm, Is.EqualTo(25000));
    }

    [Test]
    public void PointWithRoadLength_IsRejected()
    {
        string json = File.ReadAllText(DebugPath("CrowdSimulationS4DeployMod"));
        json = json.Replace(
            "\"shape\": \"point\",\n          \"sizeCm\": 14000,",
            "\"shape\": \"point\",\n          \"sizeCm\": 14000,\n          \"roadLengthCm\": 1,",
            StringComparison.Ordinal);
        var debug = JsonNode.Parse(json)!.AsObject();
        Assert.Throws<InvalidOperationException>(() => CrowdPresentationConfig.ParseOptional(debug));
    }

    private static CrowdPresentationConfig ParseMod(string mod)
    {
        var debug = JsonNode.Parse(File.ReadAllText(DebugPath(mod)))!.AsObject();
        return CrowdPresentationConfig.ParseOptional(debug)!;
    }

    private static string DebugPath(string mod)
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods", "showcases", "crowd_simulation", mod, "assets", "CrowdSimulationDebug.json");
        Assert.That(File.Exists(path), Is.True, path);
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
