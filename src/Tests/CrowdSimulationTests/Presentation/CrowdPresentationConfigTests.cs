using System;
using System.IO;
using System.Linq;
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
        Assert.That(s4.Deploy!.Actions.Validate, Is.EqualTo("CrowdSimulation.Validate"));
        Assert.That(s4.Deploy.Actions.Fuzz, Is.EqualTo("CrowdSimulation.S4.Fuzz"));
        var s4Debug = CrowdSimulationDebugConfig.Load(JsonNode.Parse(File.ReadAllText(DebugPath("CrowdSimulationS4DeployMod")))!.AsObject());
        Assert.That(s4Debug.Fuzz!.SliceMs, Is.EqualTo(8));
        Assert.That(s4Debug.Fuzz.ToPlan(s4Debug.Tools!).BoomRadiusCm, Is.EqualTo(s4Debug.Tools!.BoomRadiusCm));
        Assert.That(s4.Deploy!.Structures, Has.Length.EqualTo(6));
        Assert.That(s4.Deploy.Structures[0].Shape, Is.EqualTo(S4DeployDemoRuntime.ShapePoint));
        Assert.That(s4.Deploy.Structures[0].RoadLengthCm, Is.Null);
        Assert.That(s4.Deploy.Structures[2].Shape, Is.EqualTo(S4DeployDemoRuntime.ShapeRoad));
        Assert.That(s4.Deploy.Structures[2].RoadLengthCm, Is.EqualTo(25000));
        Assert.That(s4.Deploy.Structures[3].Shape, Is.EqualTo(S4DeployDemoRuntime.ShapeDisc));
        Assert.That(s4.Deploy.Structures[3].SizeCm, Is.EqualTo(8000));
        Assert.That(s4.Deploy.Structures[4].Shape, Is.EqualTo(S4DeployDemoRuntime.ShapeSpan));
        Assert.That(s4.Deploy.Structures[4].SizeCm, Is.EqualTo(3000));
        Assert.That(s4.Deploy.Structures[5].Id, Is.EqualTo("bridge"));
        Assert.That(s4.Deploy.Structures[5].Shape, Is.EqualTo(S4DeployDemoRuntime.ShapeSpan));
        Assert.That(s4.Deploy.Structures[5].SizeCm, Is.EqualTo(3000));
        Assert.That(s4.Deploy.Actions.ToolBoom, Is.EqualTo("CrowdSimulation.S4.ToolBoom"));
        Assert.That(s4.Deploy.Actions.Stress, Is.EqualTo("CrowdSimulation.S4.Stress"));
    }

    [Test]
    public void DeadKey_IsRejected()
    {
        var debug = JsonNode.Parse(File.ReadAllText(DebugPath("CrowdSimulationS3PathMod")))!.AsObject();
        debug["clockOverride"] = 1;
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(debug));
        Assert.That(ex!.Message, Does.Contain("clockOverride"));
    }

    [Test]
    public void Tools_MissingFieldUnknownKeyAndRange_AreRejected()
    {
        var missing = MinimalTools();
        missing.Remove("boomRadiusCm");
        Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(new JsonObject { ["tools"] = missing }));

        var unknown = MinimalTools();
        unknown["blast"] = 1;
        Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(new JsonObject { ["tools"] = unknown }));

        var range = MinimalTools();
        range["ringScale"] = 1;
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(new JsonObject { ["tools"] = range }));
        Assert.That(ex!.Message, Does.Contain("ringScale"));

        var rates = new JsonObject
        {
            ["tickHzMin"] = 10,
            ["tickHzMax"] = 60,
            ["avoidHzMin"] = 0,
            ["avoidHzMax"] = 30,
            ["avoidStep"] = 1,
        };
        Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(new JsonObject { ["rates"] = rates }));
    }

    private static JsonObject MinimalTools() => new()
    {
        ["boomRadiusCm"] = 12500,
        ["ringScale"] = 2,
        ["impulse"] = 4,
        ["spawnCount"] = 24,
        ["spawnUnitType"] = 0,
        ["spawnRadiusClass"] = 0,
        ["placeTemplate"] = "building",
        ["rectSizeCm"] = 14000,
        ["discRadiusCm"] = 8000,
        ["pathWidthCm"] = 3000,
        ["speedStepsPermille"] = new JsonArray(500, 1000, 2000, 4000),
    };

    [Test]
    public void EveryDebugFile_LoadsStrictly()
    {
        var mods = ModConfigChain.ModsShipping("CrowdSimulationDebug.json");
        Assert.That(mods, Is.Not.Empty);
        foreach (string modId in mods)
        {
            Assert.DoesNotThrow(
                () => CrowdSimulationDebugConfig.Load(ModConfigChain.Open(modId).Merged("CrowdSimulationDebug.json")),
                modId);
        }
    }

    [Test]
    public void SessionScript_AssetRowsComeBeforeInlineRows()
    {
        var debug = CrowdSimulationDebugConfig.Load(JsonNode.Parse("""
            {"session":{"scriptAsset":"assets/opening.json","script":[{"tick":5,"cmd":{"type":"spawn","count":2}}]}}
            """)!.AsObject());
        string? opened = null;
        var rows = CrowdSimulationDebugConfig.SessionScript(debug.Session, rel =>
        {
            opened = rel;
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"units":1,"script":[{"tick":0,"cmd":{"type":"spawn","count":1}}]}"""));
        })!;

        Assert.That(opened, Is.EqualTo("assets/opening.json"));
        Assert.That(rows.Select(r => r.Tick), Is.EqualTo(new[] { 0, 5 }));
        Assert.That(rows.Select(r => r.Cmd["count"]!.GetValue<int>()), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void SessionScriptAsset_Blank_IsRejected()
    {
        var debug = JsonNode.Parse("""{"session":{"scriptAsset":" "}}""")!.AsObject();
        Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(debug));
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
        return CrowdSimulationDebugConfig.Load(debug).Presentation!;
    }

    private static string DebugPath(string mod)
    {
        string path = Path.Combine(
            ModConfigChain.RepoRoot(),
            "mods", "showcases", "crowd_simulation", mod, "assets", "CrowdSimulationDebug.json");
        Assert.That(File.Exists(path), Is.True, path);
        return path;
    }
}
