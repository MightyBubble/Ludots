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
        Assert.That(s4.Deploy!.Palette.Select(p => p.Entry),
            Is.EqualTo(new[] { "building", "s7barrier", "roadFixed", "flood", "roadTwoPoint", "bridge" }));
        Assert.That(s4Debug.Tools!.PlaceEntry, Is.EqualTo("building"));
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
        ["spawnTemplate"] = "crowd_simulation.unit.infantry.r100",
        ["placeEntry"] = "building",
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
    public void S4Palette_EveryEntryResolvesAgainstSessionTemplates()
    {
        var entries = CrowdStructurePalette.Load(ModConfigChain.Open("CrowdSimulationS4DeployMod").Merged(CrowdStructurePalette.RelativePath));
        var (config, _) = Parity.S4DeployTruthTests.BuildSession("s7");
        var s4 = ParseMod("CrowdSimulationS4DeployMod");
        foreach (CrowdPaletteSlotDto slot in s4.Deploy!.Palette)
        {
            Assert.That(entries.TryGetValue(slot.Entry, out CrowdPaletteEntry? entry), Is.True, slot.Entry);
            Assert.DoesNotThrow(() => CrowdStructurePalette.RequireTemplate(slot.Entry, entry!, config.Structures), slot.Entry);
        }

        Assert.That(entries["s7barrier"].Gesture, Is.EqualTo(CrowdPlacementGesture.Point));
        Assert.That(entries["roadFixed"].Gesture, Is.EqualTo(CrowdPlacementGesture.FixedPath));
        Assert.That((entries["roadFixed"].ToOffsetXCm, entries["roadFixed"].ToOffsetYCm), Is.EqualTo((25000, 0)));
        Assert.That(entries["roadTwoPoint"].TemplateId, Is.EqualTo(entries["roadFixed"].TemplateId));
    }

    [Test]
    public void PaletteGesture_MustMatchTemplateFootprint()
    {
        var (config, _) = Parity.S4DeployTruthTests.BuildSession("s7");
        var entries = CrowdStructurePalette.Load(ModConfigChain.Open("CrowdSimulationS4DeployMod").Merged(CrowdStructurePalette.RelativePath));
        CrowdPaletteEntry point = entries["building"];
        CrowdPaletteEntry path = entries["roadTwoPoint"];

        var pathAsPoint = path with { Gesture = CrowdPlacementGesture.Point };
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdStructurePalette.RequireTemplate("x", pathAsPoint, config.Structures));
        Assert.That(ex!.Message, Does.Contain(CrowdStructurePalette.GestureTwoPoint));

        var pointAsPath = point with { Gesture = CrowdPlacementGesture.TwoPoint };
        ex = Assert.Throws<InvalidOperationException>(() => CrowdStructurePalette.RequireTemplate("x", pointAsPath, config.Structures));
        Assert.That(ex!.Message, Does.Contain(CrowdStructurePalette.GesturePoint));

        var missing = point with { TemplateId = "absent" };
        ex = Assert.Throws<InvalidOperationException>(() => CrowdStructurePalette.RequireTemplate("x", missing, config.Structures));
        Assert.That(ex!.Message, Does.Contain("absent"));
    }

    [TestCase("""{"kind":"fixedPath"}""", "toOffsetCm")]
    [TestCase("""{"kind":"fixedPath","toOffsetCm":{"xCm":0,"yCm":0}}""", "toOffsetCm")]
    [TestCase("""{"kind":"point","toOffsetCm":{"xCm":1,"yCm":0}}""", "toOffsetCm")]
    [TestCase("""{"kind":"twoPoint","toOffsetCm":{"xCm":1,"yCm":0}}""", "toOffsetCm")]
    [TestCase("""{"kind":"lasso"}""", "gesture.kind")]
    public void PaletteGesture_BadShape_IsRejected(string gesture, string expected)
    {
        var root = JsonNode.Parse("""{"e":{"template":"t","label":"l","sizeCm":100,"gesture":""" + gesture + "}}")!.AsObject();
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdStructurePalette.Load(root));
        Assert.That(ex!.Message, Does.Contain(expected));
    }

    [Test]
    public void PaletteEntry_UnknownKeyAndNonPositiveSize_AreRejected()
    {
        var unknown = JsonNode.Parse("""{"e":{"template":"t","label":"l","gesture":{"kind":"point"},"sizeCm":100,"roadLengthCm":1}}""")!.AsObject();
        Assert.Throws<InvalidOperationException>(() => CrowdStructurePalette.Load(unknown));
        var size = JsonNode.Parse("""{"e":{"template":"t","label":"l","gesture":{"kind":"point"},"sizeCm":0}}""")!.AsObject();
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdStructurePalette.Load(size));
        Assert.That(ex!.Message, Does.Contain("sizeCm"));
    }

    [Test]
    public void SpawnTemplate_UnknownKeyAndMissingCatalogId_AreRejected()
    {
        var unknown = MinimalTools();
        unknown["spawnUnitType"] = 0;
        var key = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(new JsonObject { ["tools"] = unknown }));
        Assert.That(key!.Message, Does.Contain("spawnUnitType"));

        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        var tools = new CrowdDebugTools
        {
            BoomRadiusCm = 12500,
            RingScale = 2,
            Impulse = 4,
            SpawnCount = 24,
            SpawnTemplate = "missing.unit",
            PlaceEntry = "building",
            SpeedStepsPermille = new[] { 500, 1000, 2000, 4000 },
        };
        var missing = Assert.Throws<InvalidOperationException>(() => tools.ResolveSpawn(session.Config));
        Assert.That(missing!.Message, Does.Contain("missing.unit"));
    }

    [Test]
    public void ToolsPlaceEntry_MustNameAPaletteSlot()
    {
        var debug = JsonNode.Parse(File.ReadAllText(DebugPath("CrowdSimulationS4DeployMod")))!.AsObject();
        debug["tools"]!["placeEntry"] = "absent";
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugConfig.Load(debug));
        Assert.That(ex!.Message, Does.Contain("absent"));
    }

    [Test]
    public void PaletteSlot_DuplicateSelectAction_IsRejected()
    {
        var debug = JsonNode.Parse(File.ReadAllText(DebugPath("CrowdSimulationS4DeployMod")))!.AsObject();
        var palette = debug["presentation"]!["deploy"]!["palette"]!.AsArray();
        palette[1]!["selectAction"] = palette[0]!["selectAction"]!.GetValue<string>();
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
