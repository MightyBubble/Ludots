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
        string before = File.ReadAllText(configPath);
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
            Assert.That(File.ReadAllText(configPath), Is.EqualTo(before));
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
            Assert.That(((JsonObject)file["deploy"]!)["initialUnits"]!.GetValue<int>(), Is.EqualTo(12000));
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
        string configPath = Path.Combine(copy, "assets", "CrowdSimulationConfig.json");
        string before = File.ReadAllText(configPath);
        try
        {
            JsonObject bundle = CrowdSimulationEditorApi.ExportBundle(repo, S4, copy);
            Assert.That(Int(bundle, "version"), Is.EqualTo(CrowdSimulationAuthoringBundle.SchemaVersion));
            bundle["version"] = 99;
            JsonObject wrong = CrowdSimulationEditorApi.ImportBundle(repo, S4, bundle, write: true, copy);
            Assert.That(Flag(wrong, "ok"), Is.False, wrong.ToJsonString());
            Assert.That(wrong["issues"]!.ToJsonString(), Does.Contain("version"));
            Assert.That(File.ReadAllText(configPath), Is.EqualTo(before));

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

    [Test]
    public void UnitTemplates_Minimal_ListsSchemaFieldSourcesProfileAndPresenter()
    {
        JsonObject view = UnitTemplates(CrowdSimulationEditorApi.Load(FindRepoRoot(), Minimal));
        string[] schema = ((JsonArray)view["fields"]!).Select(field => Text((JsonObject)field!, "name")).ToArray();
        Assert.That(schema, Is.EqualTo(CrowdUnitTemplateFacts.AuthoredFields.Select(field => field.Name).ToArray()));
        Assert.That(view["agentTypes"]!.ToJsonString(), Does.Contain("\"foot\""));

        JsonObject unit = Item(view, MinimalUnit);
        Assert.That(Flag(unit, "ownedBySelection"), Is.True);
        Assert.That(unit["agent"]!["speedCmPerSecond"]!.GetValue<int>(), Is.EqualTo(1400));
        foreach (CrowdUnitTemplateField field in CrowdUnitTemplateFacts.AuthoredFields)
        {
            JsonObject source = (JsonObject)unit["fields"]![field.Name]!;
            Assert.That(Text(source, "sourceMod"), Is.EqualTo(Minimal), field.Name);
            Assert.That(Text(source, "sourceFile"), Does.EndWith("Entities/templates.json"), field.Name);
        }

        JsonObject profile = (JsonObject)unit["profile"]!;
        Assert.That(Text(profile, "id"), Is.EqualTo("crowd_minimal_unit"));
        Assert.That(profile["values"]!["radiusCm"]!.GetValue<int>(), Is.EqualTo(150));
        Assert.That(Text((JsonObject)profile["fields"]!["radiusCm"]!, "sourceMod"), Is.EqualTo(Minimal));
        Assert.That(profile["usedBy"]!.ToJsonString(), Does.Contain(MinimalUnit));

        JsonObject presenter = (JsonObject)((JsonArray)unit["presenters"]!).Single()!;
        Assert.That(Text(presenter, "definitionId"), Is.EqualTo(MinimalUnit));
        Assert.That(Flag(presenter, "defined"), Is.True);
        Assert.That(Text(presenter, "sourceMod"), Is.EqualTo(Minimal));
    }

    [Test]
    public void UnitTemplates_S4_FieldsComeFromTheDependency()
    {
        JsonObject view = UnitTemplates(CrowdSimulationEditorApi.Load(FindRepoRoot(), S4));
        JsonObject unit = Item(view, InfantryR100);
        Assert.That(Flag(unit, "ownedBySelection"), Is.False);
        Assert.That(Text((JsonObject)unit["fields"]!["speedCmPerSecond"]!, "sourceMod"), Is.EqualTo(DemoContent));
        Assert.That(Text((JsonObject)unit["profile"]!, "id"), Is.EqualTo("foot_r100"));
    }

    [Test]
    public void UnitTemplates_InvalidSpeed_ReturnsLoaderTextWithSourceAndDoesNotWrite()
    {
        string copy = CopyMod(Minimal);
        string templatesPath = Path.Combine(copy, "assets", "Entities", "templates.json");
        string before = File.ReadAllText(templatesPath);
        try
        {
            JsonObject draft = TemplatesDraft(Minimal, copy);
            Agent(draft, MinimalUnit)["speedCmPerSecond"] = -5;
            JsonObject result = CrowdSimulationEditorApi.Apply(FindRepoRoot(), Minimal, "templates", draft, copy);
            Assert.That(Flag(result, "ok"), Is.False, result.ToJsonString());
            JsonObject issue = (JsonObject)((JsonArray)result["issues"]!)[0]!;
            Assert.That(Text(issue, "message"), Does.Contain($"模板 \"{MinimalUnit}\" 的 CrowdSimulationAgent.speedCmPerSecond 需为 > 0 的数值"));
            Assert.That(Text(issue, "path"), Is.EqualTo("CrowdSimulationAgent.speedCmPerSecond"));
            Assert.That(Text(issue, "sourceMod"), Is.EqualTo(Minimal));
            Assert.That(Text(issue, "sourceFile"), Does.EndWith("Entities/templates.json"));
            Assert.That(File.ReadAllText(templatesPath), Is.EqualTo(before));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void UnitTemplates_UnknownAgentType_ReturnsLoaderText()
    {
        string copy = CopyMod(Minimal);
        try
        {
            JsonObject draft = TemplatesDraft(Minimal, copy);
            Agent(draft, MinimalUnit)["agentTypeId"] = "hover";
            JsonObject result = CrowdSimulationEditorApi.Validate(FindRepoRoot(), Minimal, "templates", draft, copy);
            Assert.That(Flag(result, "ok"), Is.False, result.ToJsonString());
            Assert.That(result["issues"]!.ToJsonString(), Does.Contain("agentTypeId \\u0022hover\\u0022").Or.Contain("agentTypeId \"hover\""));
            Assert.That(result["issues"]!.ToJsonString(), Does.Contain("Navigation/pathing.json"));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void UnitTemplates_RadiusWithoutMatchingPersonalRadius_IsRejected()
    {
        string copy = CopyMod(Minimal);
        string profilesPath = Path.Combine(copy, "assets", "Navigation", "agent_profiles.json");
        string before = File.ReadAllText(profilesPath);
        try
        {
            JsonObject draft = TemplatesDraft(Minimal, copy);
            Profile(draft, "crowd_minimal_unit")["radiusCm"] = 200;
            JsonObject result = CrowdSimulationEditorApi.Apply(FindRepoRoot(), Minimal, "templates", draft, copy);
            Assert.That(Flag(result, "ok"), Is.False, result.ToJsonString());
            Assert.That(result["issues"]!.ToJsonString(), Does.Contain("personalRadiusCm"));
            Assert.That(File.ReadAllText(profilesPath), Is.EqualTo(before));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void UnitTemplates_Minimal_SpeedAndRadius_WriteOwnFilesAndReload()
    {
        string repo = FindRepoRoot();
        string copy = CopyMod(Minimal);
        try
        {
            JsonObject draft = TemplatesDraft(Minimal, copy);
            Agent(draft, MinimalUnit)["speedCmPerSecond"] = 1650;
            Agent(draft, MinimalUnit)["personalRadiusCm"] = 1200;
            Profile(draft, "crowd_minimal_unit")["radiusCm"] = 200;
            JsonObject result = CrowdSimulationEditorApi.Apply(repo, Minimal, "templates", draft, copy);
            Assert.That(Flag(result, "ok"), Is.True, result.ToJsonString());
            string written = result["written"]!.ToJsonString();
            Assert.That(written, Does.Contain(Minimal + "/assets/Entities/templates.json"));
            Assert.That(written, Does.Contain(Minimal + "/assets/Navigation/agent_profiles.json"));

            string templatesText = File.ReadAllText(Path.Combine(copy, "assets", "Entities", "templates.json"));
            Assert.That(templatesText, Does.Contain("\n  {\n    \"id\": \"crowd_minimal.team\""), "两格缩进，和其他编辑器写回一致");
            Assert.That(templatesText, Does.Contain("crowd_minimal.wall_block"));

            JsonObject unit = Item(UnitTemplates(CrowdSimulationEditorApi.Load(repo, Minimal, copy)), MinimalUnit);
            Assert.That(unit["agent"]!["speedCmPerSecond"]!.GetValue<int>(), Is.EqualTo(1650));
            Assert.That(unit["agent"]!["personalRadiusCm"]!.GetValue<int>(), Is.EqualTo(1200));
            Assert.That(unit["profile"]!["values"]!["radiusCm"]!.GetValue<int>(), Is.EqualTo(200));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Test]
    public void UnitTemplates_S4_OverrideWritesOnlyChangedKeysInTheSelectedMod()
    {
        string repo = FindRepoRoot();
        string demoTemplates = Path.Combine(repo, "mods", "showcases", "crowd_simulation", DemoContent, "assets", "Entities", "templates.json");
        string demoBefore = File.ReadAllText(demoTemplates);
        string copy = CopyMod(S4);
        try
        {
            JsonObject draft = TemplatesDraft(S4, copy);
            Agent(draft, InfantryR100)["speedCmPerSecond"] = 1500;
            JsonObject result = CrowdSimulationEditorApi.Apply(repo, S4, "templates", draft, copy);
            Assert.That(Flag(result, "ok"), Is.True, result.ToJsonString());
            Assert.That(result["written"]!.ToJsonString(), Does.Contain(S4 + "/assets/Entities/templates.json"));
            Assert.That(result["written"]!.ToJsonString(), Does.Not.Contain(DemoContent));
            Assert.That(File.ReadAllText(demoTemplates), Is.EqualTo(demoBefore));

            var own = (JsonArray)JsonNode.Parse(File.ReadAllText(Path.Combine(copy, "assets", "Entities", "templates.json")))!;
            JsonObject entry = (JsonObject)own.Single(node => node!["id"]!.GetValue<string>() == InfantryR100)!;
            Assert.That(entry.ToJsonString(), Is.EqualTo(
                "{\"id\":\"" + InfantryR100 + "\",\"components\":{\"CrowdSimulationAgent\":{\"speedCmPerSecond\":1500}}}"));

            JsonObject unit = Item(UnitTemplates(CrowdSimulationEditorApi.Load(repo, S4, copy)), InfantryR100);
            Assert.That(Flag(unit, "ownedBySelection"), Is.True);
            Assert.That(unit["agent"]!["speedCmPerSecond"]!.GetValue<int>(), Is.EqualTo(1500));
            Assert.That(Text((JsonObject)unit["fields"]!["speedCmPerSecond"]!, "sourceMod"), Is.EqualTo(S4));
            Assert.That(Text((JsonObject)unit["fields"]!["profileId"]!, "sourceMod"), Is.EqualTo(DemoContent));

            JsonObject reset = TemplatesDraft(S4, copy);
            Agent(reset, InfantryR100)["speedCmPerSecond"] = 1400;
            Assert.That(Flag(CrowdSimulationEditorApi.Apply(repo, S4, "templates", reset, copy), "ok"), Is.True);
            string ownAfterReset = File.ReadAllText(Path.Combine(copy, "assets", "Entities", "templates.json"));
            Assert.That(ownAfterReset, Does.Not.Contain(InfantryR100));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
            Assert.That(File.ReadAllText(demoTemplates), Is.EqualTo(demoBefore));
        }
    }

    [Test]
    public void UnitTemplates_S4_DroppingAKeyTheDependencyGives_IsRejected()
    {
        string copy = CopyMod(S4);
        try
        {
            JsonObject draft = TemplatesDraft(S4, copy);
            Agent(draft, InfantryR100).Remove("pushPriority");
            JsonObject result = CrowdSimulationEditorApi.Validate(FindRepoRoot(), S4, "templates", draft, copy);
            Assert.That(Flag(result, "ok"), Is.False, result.ToJsonString());
            Assert.That(result["issues"]!.ToJsonString(), Does.Contain($"unitTypes.{InfantryR100}.components.CrowdSimulationAgent.pushPriority"));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    private const string Minimal = "CrowdSimulationMinimalMod";
    private const string MinimalUnit = "crowd_minimal.unit";
    private const string DemoContent = "CrowdSimulationDemoContentMod";
    private const string InfantryR100 = "crowd_simulation.unit.infantry.r100";

    private static JsonObject UnitTemplates(JsonObject snapshot)
    {
        Assert.That(Flag(snapshot, "ok"), Is.True, snapshot.ToJsonString());
        JsonObject view = (JsonObject)snapshot["unitTemplates"]!;
        Assert.That(((JsonArray)view["issues"]!).Count, Is.EqualTo(0), view["issues"]!.ToJsonString());
        return view;
    }

    private static JsonObject Item(JsonObject view, string id)
        => (JsonObject)((JsonArray)view["items"]!).Single(item => item!["id"]!.GetValue<string>() == id)!;

    private static JsonObject TemplatesDraft(string modId, string copy)
        => (JsonObject)CrowdSimulationEditorApi.Load(FindRepoRoot(), modId, copy)["templates"]!.DeepClone()!;

    private static JsonObject Agent(JsonObject draft, string templateId)
        => (JsonObject)((JsonArray)draft["unitTypes"]!)
            .Single(unit => unit!["id"]!.GetValue<string>() == templateId)!["components"]!["CrowdSimulationAgent"]!;

    private static JsonObject Profile(JsonObject draft, string profileId)
        => (JsonObject)((JsonArray)draft["agentProfiles"]!).Single(profile => profile!["id"]!.GetValue<string>() == profileId)!;

    private static string CopyS4() => CopyMod(S4);

    private static string CopyMod(string modId)
    {
        string source = Path.Combine(FindRepoRoot(), "mods", "showcases", "crowd_simulation", modId);
        string copy = Path.Combine(Path.GetTempPath(), "ludots-crowd-edit-test-" + Guid.NewGuid().ToString("N"));
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
