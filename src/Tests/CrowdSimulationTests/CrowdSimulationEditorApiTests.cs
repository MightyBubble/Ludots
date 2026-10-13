using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public class CrowdSimulationEditorApiTests
{
    private const string S4 = "CrowdSimulationS4DeployMod";

    [Test]
    public void Load_S4_ExposesLayersOwnersAndBundleVersion()
    {
        JsonObject snapshot = CrowdSimulationEditorApi.Load(FindRepoRoot(), S4);
        Assert.That(Flag(snapshot, "ok"), Is.True, snapshot.ToJsonString());
        Assert.That(Int(snapshot, "bundleVersion"), Is.EqualTo(CrowdSimulationAuthoringBundle.SchemaVersion));
        Assert.That(Text(snapshot, "mapId"), Is.EqualTo("crowd_simulation_s1337"));
        Assert.That(snapshot["scenario"], Is.InstanceOf<JsonObject>());
        Assert.That(snapshot["globals"], Is.InstanceOf<JsonObject>());
        Assert.That(snapshot["templates"], Is.InstanceOf<JsonObject>());
        Assert.That(snapshot["debug"], Is.InstanceOf<JsonObject>());
        Assert.That(snapshot["debugView"], Is.InstanceOf<JsonObject>());
        Assert.That(snapshot["costMatrix"], Is.InstanceOf<JsonObject>());
        var owners = (JsonArray)snapshot["owners"]!;
        Assert.That(owners.Count, Is.GreaterThan(0));
        var first = (JsonObject)owners[0]!;
        Assert.That(Text(first, "sourceFile"), Does.Contain(":"));
        Assert.That(Text(first, "sourceMod"), Is.Not.Empty);
    }

    [Test]
    public void Validate_BadMaxUnits_NamesSourceAndDoesNotWrite()
    {
        string copy = CopyS4();
        string configPath = Path.Combine(copy, "assets", "CrowdSimulationConfig.json");
        try
        {
            JsonObject snapshot = CrowdSimulationEditorApi.Load(FindRepoRoot(), S4, copy);
            JsonObject globals = (JsonObject)snapshot["globals"]!.DeepClone()!;
            ((JsonObject)globals["sim"]!)["maxUnits"] = "nope";
            JsonObject result = CrowdSimulationEditorApi.Validate(FindRepoRoot(), S4, "globals", globals, copy);
            Assert.That(Flag(result, "ok"), Is.False, result.ToJsonString());
            JsonObject issue = (JsonObject)((JsonArray)result["issues"]!)[0]!;
            Assert.That(Text(issue, "path"), Is.EqualTo("sim.maxUnits"));
            Assert.That(Text(issue, "sourceMod"), Is.EqualTo(S4));
            Assert.That(Text(issue, "sourceFile"), Does.Contain("CrowdSimulationConfig.json"));
            Assert.That(File.Exists(configPath), Is.False);
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void Apply_Globals_WritesOnlyTheSelectedMod()
    {
        string repo = FindRepoRoot();
        string copy = CopyS4();
        string standard = Path.Combine(
            repo,
            "mods",
            "capabilities",
            "navigation",
            "CrowdSimulationStandardMod",
            "assets",
            "CrowdSimulationConfig.json");
        string before = File.ReadAllText(standard);
        try
        {
            JsonObject snapshot = CrowdSimulationEditorApi.Load(repo, S4, copy);
            JsonObject globals = (JsonObject)snapshot["globals"]!.DeepClone()!;
            ((JsonObject)globals["sim"]!)["maxUnits"] = 40000;
            JsonObject result = CrowdSimulationEditorApi.Apply(repo, S4, "globals", globals, copy);
            Assert.That(Flag(result, "ok"), Is.True, result.ToJsonString());
            string written = string.Join(",", ((JsonArray)result["written"]!).Select(node => node!.ToString()));
            Assert.That(written, Does.Contain(S4 + "/assets/CrowdSimulationConfig.json"));
            Assert.That(written, Does.Not.Contain("CrowdSimulationStandardMod"));
            JsonObject file = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(copy, "assets", "CrowdSimulationConfig.json")))!;
            Assert.That(((JsonObject)file["sim"]!)["maxUnits"]!.GetValue<int>(), Is.EqualTo(40000));
            Assert.That(File.ReadAllText(standard), Is.EqualTo(before));

            JsonObject again = CrowdSimulationEditorApi.Load(repo, S4, copy);
            Assert.That(((JsonObject)((JsonObject)again["globals"]!)["sim"]!)["maxUnits"]!.GetValue<int>(), Is.EqualTo(40000));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
            Assert.That(File.ReadAllText(standard), Is.EqualTo(before));
        }
    }

    [Test]
    public void Apply_Globals_RefusesTargetFileWhoseRootIsNotObject()
    {
        string repo = FindRepoRoot();
        string copy = CopyS4();
        try
        {
            JsonObject snapshot = CrowdSimulationEditorApi.Load(repo, S4, copy);
            JsonObject globals = (JsonObject)snapshot["globals"]!.DeepClone()!;
            string configPath = Path.Combine(copy, "assets", "CrowdSimulationConfig.json");
            File.WriteAllText(configPath, "[]");
            JsonObject result = CrowdSimulationEditorApi.Apply(repo, S4, "globals", globals, copy);
            Assert.That(Flag(result, "ok"), Is.False, result.ToJsonString());
            Assert.That(File.ReadAllText(configPath), Is.EqualTo("[]"));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void Apply_MapOnNonOwner_Refuses()
    {
        string copy = CopyS4();
        try
        {
            JsonObject snapshot = CrowdSimulationEditorApi.Load(FindRepoRoot(), S4, copy);
            JsonObject scenario = (JsonObject)snapshot["scenario"]!.DeepClone()!;
            scenario["map"] = new JsonObject { ["id"] = "not-the-map" };
            JsonObject result = CrowdSimulationEditorApi.Apply(FindRepoRoot(), S4, "scenario", scenario, copy);
            Assert.That(Flag(result, "ok"), Is.False, result.ToJsonString());
            string messages = result["issues"]!.ToJsonString();
            Assert.That(messages, Does.Contain("Maps/"));
            Assert.That(Directory.Exists(Path.Combine(copy, "assets", "Maps")), Is.False);
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void Import_RejectsMissingOrOtherVersion_BeforeValidation()
    {
        string repo = FindRepoRoot();
        string copy = CopyS4();
        try
        {
            JsonObject bundle = CrowdSimulationEditorApi.ExportBundle(repo, S4, copy);
            Assert.That(Int(bundle, "version"), Is.EqualTo(CrowdSimulationAuthoringBundle.SchemaVersion));
            bundle["version"] = 99;
            JsonObject wrong = CrowdSimulationEditorApi.ImportBundle(repo, S4, bundle, write: true, copy);
            Assert.That(Flag(wrong, "ok"), Is.False, wrong.ToJsonString());
            Assert.That(wrong["issues"]!.ToJsonString(), Does.Contain("version"));
            Assert.That(File.Exists(Path.Combine(copy, "assets", "CrowdSimulationConfig.json")), Is.False);

            bundle.Remove("version");
            JsonObject missing = CrowdSimulationEditorApi.ImportBundle(repo, S4, bundle, write: false, copy);
            Assert.That(Flag(missing, "ok"), Is.False);
            Assert.That(missing["issues"]!.ToJsonString(), Does.Contain("version"));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void CostMatrix_RoundTrip_WritesPassableFalseForEmptyCell()
    {
        string repo = FindRepoRoot();
        string copy = CopyS4();
        try
        {
            JsonObject snapshot = CrowdSimulationEditorApi.Load(repo, S4, copy);
            JsonObject matrix = (JsonObject)snapshot["costMatrix"]!.DeepClone()!;
            JsonObject cells = (JsonObject)matrix["cells"]!;
            JsonObject foot = (JsonObject)cells["foot"]!;
            foot["0"] = 7;
            foot["8"] = null;
            JsonObject applied = CrowdSimulationEditorApi.Apply(repo, S4, "costMatrix", matrix, copy);
            Assert.That(Flag(applied, "ok"), Is.True, applied.ToJsonString());
            JsonObject again = CrowdSimulationEditorApi.Load(repo, S4, copy);
            JsonObject footAgain = (JsonObject)((JsonObject)((JsonObject)again["costMatrix"]!)["cells"]!)["foot"]!;
            Assert.That(footAgain["0"]!.GetValue<int>(), Is.EqualTo(7));
            Assert.That(footAgain["8"], Is.Null);
            string pathing = File.ReadAllText(Path.Combine(copy, "assets", "Navigation", "pathing.json"));
            Assert.That(pathing, Does.Contain("\"passable\": false").Or.Contain("\"passable\":false"));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void LiveApply_InvalidDebug_DoesNotCommit()
    {
        bool committed = false;
        CrowdSimulationEditorResult result = CrowdSimulationLiveApply.ApplyDebug(
            new JsonObject { ["worker"] = new JsonObject() },
            () => committed = true);
        Assert.That(result.Ok, Is.False);
        Assert.That(committed, Is.False);
    }

    [Test]
    public void LiveApply_EmptyDebug_Commits()
    {
        bool committed = false;
        CrowdSimulationEditorResult result = CrowdSimulationLiveApply.ApplyDebug(new JsonObject(), () => committed = true);
        Assert.That(result.Ok, Is.True);
        Assert.That(committed, Is.True);
    }

    [Test]
    public void LiveApply_DebugViewWrongVersion_DoesNotCommit()
    {
        bool committed = false;
        CrowdSimulationEditorResult result = CrowdSimulationLiveApply.ApplyDebugView(
            new JsonObject { ["version"] = 2 },
            () => committed = true);
        Assert.That(result.Ok, Is.False);
        Assert.That(committed, Is.False);
    }

    [Test]
    public void LiveApply_Rebuild_SkipsWhenValidationReturnsIssues()
    {
        bool rebuilt = false;
        CrowdSimulationEditorResult result = CrowdSimulationLiveApply.ApplyRebuild(
            () => CrowdSimulationAuthoringIssues.One("sim.maxUnits", "bad"),
            () => rebuilt = true);
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Rebuilt, Is.False);
        Assert.That(rebuilt, Is.False);
    }

    [Test]
    public void LiveApply_Rebuild_CommitsWhenValidationIsEmpty()
    {
        bool rebuilt = false;
        CrowdSimulationEditorResult result = CrowdSimulationLiveApply.ApplyRebuild(
            () => Array.Empty<CrowdSimulationAuthoringIssue>(),
            () => rebuilt = true);
        Assert.That(result.Ok, Is.True);
        Assert.That(result.Rebuilt, Is.True);
        JsonObject json = result.ToJson();
        Assert.That(json["lost"]!.ToJsonString(), Does.Contain("commands"));
        Assert.That(rebuilt, Is.True);
    }

    private static string CopyS4()
    {
        string source = Path.Combine(FindRepoRoot(), "mods", "showcases", "crowd_simulation", S4);
        string copy = Path.Combine(Path.GetTempPath(), "ludots-s4-edit-" + Guid.NewGuid().ToString("N"));
        CopyTree(source, copy);
        return copy;
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
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

    private static bool Flag(JsonObject node, string key) => node[key]!.GetValue<bool>();

    private static int Int(JsonObject node, string key) => node[key]!.GetValue<int>();

    private static string Text(JsonObject node, string key) => node[key]!.GetValue<string>();
}
