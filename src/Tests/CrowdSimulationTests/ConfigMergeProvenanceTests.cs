using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 严格装载失败时，异常文本带上相对路径、合并顺序里的片段地址和胜出。
/// 编辑器桥返回的也是这份快照。
/// </summary>
[TestFixture]
public class ConfigMergeProvenanceTests
{
    private const string ProbeModId = "CrowdSimulationProvenanceProbeMod";

    [Test]
    public void StandardMod_WrongTypeOverride_NamesBothUrisAndWinner()
    {
        string repoRoot = FindRepoRoot();
        string probeRoot = NewTempDir();
        string emptyCore = Path.Combine(probeRoot, "empty-core");
        Directory.CreateDirectory(Path.Combine(probeRoot, "assets"));
        Directory.CreateDirectory(emptyCore);
        File.WriteAllText(
            Path.Combine(probeRoot, "assets", "CrowdSimulationConfig.json"),
            """{ "world": { "navCellSizeCm": "not-a-number" } }""");
        try
        {
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(
                emptyCore,
                ( "CrowdSimulationMod", Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod") ),
                ( "CrowdSimulationStandardMod", Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod") ),
                ( ProbeModId, probeRoot ));

            ConfigLoadException ex = Assert.Throws<ConfigLoadException>(
                () => new CrowdSimulationConfigLoader(pipeline).LoadConfig(catalog))!;

            const string standardUri = "CrowdSimulationStandardMod:assets/CrowdSimulationConfig.json";
            string winnerUri = $"{ProbeModId}:assets/CrowdSimulationConfig.json";
            AssertProvenance(ex, "CrowdSimulationConfig.json", new[] { standardUri, winnerUri }, winnerUri, typeof(InvalidOperationException));
        }
        finally
        {
            Directory.Delete(probeRoot, recursive: true);
        }
    }

    [Test]
    public void Navmesh_WrongTypeOverride_NamesBothUrisAndFileWinner()
    {
        string root = NewTempDir();
        try
        {
            WriteCoreCatalog(root, """[{ "Path": "Navigation/navmesh.json", "Policy": "DeepObject" }]""");
            WriteCoreFile(root, "Navigation/navmesh.json", MinimalNavmesh("40"));
            string probe = WriteProbe(root, "Navigation/navmesh.json", MinimalNavmesh("\"not-a-number\""));
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root, (ProbeModId, probe));

            ConfigLoadException ex = Assert.Throws<ConfigLoadException>(
                () => new NavMeshBakeConfigLoader(pipeline, DefaultProfiles()).Load(catalog))!;

            string winnerUri = $"{ProbeModId}:assets/Navigation/navmesh.json";
            AssertProvenance(
                ex,
                "Navigation/navmesh.json",
                new[] { "Core:Navigation/navmesh.json", winnerUri },
                winnerUri,
                typeof(InvalidOperationException));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Pathing_WrongTypeOverride_NamesBothUrisAndFileWinner()
    {
        string root = NewTempDir();
        try
        {
            WriteCoreCatalog(root, """[{ "Path": "Navigation/pathing.json", "Policy": "DeepObject" }]""");
            WriteCoreFile(root, "Navigation/pathing.json", MinimalPathing());
            string probe = WriteProbe(root, "Navigation/pathing.json", """{ "agentTypes": "not-an-array" }""");
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root, (ProbeModId, probe));

            ConfigLoadException ex = Assert.Throws<ConfigLoadException>(
                () => new PathingConfigLoader(pipeline).Load(catalog))!;

            string winnerUri = $"{ProbeModId}:assets/Navigation/pathing.json";
            AssertProvenance(
                ex,
                "Navigation/pathing.json",
                new[] { "Core:Navigation/pathing.json", winnerUri },
                winnerUri,
                typeof(InvalidOperationException));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void AgentProfiles_WrongTypeOverride_NamesBothUrisAndIdWinner()
    {
        string root = NewTempDir();
        try
        {
            WriteCoreCatalog(root, """[{ "Path": "Navigation/agent_profiles.json", "Policy": "ArrayById", "IdField": "id" }]""");
            WriteCoreFile(root, "Navigation/agent_profiles.json", MinimalProfiles(100));
            string probe = WriteProbe(
                root,
                "Navigation/agent_profiles.json",
                """
                [{
                  "id": "Small",
                  "radiusCm": "not-a-number",
                  "heightCm": 180,
                  "clearanceCm": 40,
                  "draftCm": 0,
                  "beamCm": 0,
                  "mass": 1,
                  "layer": 0,
                  "navProfileId": "Small"
                }]
                """);
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root, (ProbeModId, probe));

            ConfigLoadException ex = Assert.Throws<ConfigLoadException>(
                () => new AgentProfileConfigLoader(pipeline).Load(catalog))!;

            string winnerUri = $"{ProbeModId}:assets/Navigation/agent_profiles.json";
            AssertProvenance(
                ex,
                "Navigation/agent_profiles.json",
                new[] { "Core:Navigation/agent_profiles.json", winnerUri },
                $"Small={winnerUri}",
                typeof(JsonException));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Templates_WrongTypeOverride_NamesBothUrisAndIdWinner()
    {
        string root = NewTempDir();
        try
        {
            WriteCoreCatalog(root, """[{ "Path": "Entities/templates.json", "Policy": "ArrayById", "IdField": "id" }]""");
            WriteCoreFile(root, "Entities/templates.json", """[{ "id": "Unit.Probe", "components": { "Health": { "max": 1 } } }]""");
            string probe = WriteProbe(
                root,
                "Entities/templates.json",
                """[{ "id": "Unit.Probe", "components": "not-an-object" }]""");
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root, (ProbeModId, probe));

            ConfigLoadException ex = Assert.Throws<ConfigLoadException>(
                () => new DataRegistry<EntityTemplate>(pipeline).Load("Entities/templates.json", catalog))!;

            string winnerUri = $"{ProbeModId}:assets/Entities/templates.json";
            AssertProvenance(
                ex,
                "Entities/templates.json",
                new[] { "Core:Entities/templates.json", winnerUri },
                $"Unit.Probe={winnerUri}",
                typeof(InvalidOperationException));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Capture_StandardModLoadOrder_ReportsTraversalFragmentsAndFileWinner()
    {
        string repoRoot = FindRepoRoot();
        var mounts = new List<ConfigModMount>
        {
            new("LudotsCoreMod", Path.Combine(repoRoot, "mods", "LudotsCoreMod")),
            new("CrowdSimulationMod", Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod")),
            new("CrowdSimulationStandardMod", Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod")),
            new("CrowdSimulationDemoContentMod", Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationDemoContentMod")),
        };

        ConfigProvenanceSnapshot snapshot = ConfigMergeProvenance.Capture(Path.Combine(repoRoot, "assets"), mounts);
        ConfigProvenanceFile navmesh = FindFile(snapshot, "Navigation/navmesh.json");
        Assert.That(navmesh.Fragments, Is.EqualTo(new[] { "Core:Navigation/navmesh.json", "LudotsCoreMod:assets/Navigation/navmesh.json" }));

        ConfigProvenanceFile traversal = FindFile(snapshot, "CrowdSimulation/traversal.json");
        const string standardUri = "CrowdSimulationStandardMod:assets/CrowdSimulation/traversal.json";
        const string demoUri = "CrowdSimulationDemoContentMod:assets/CrowdSimulation/traversal.json";
        Assert.That(traversal.Fragments, Is.EqualTo(new[] { standardUri, demoUri }));
        Assert.That(traversal.FileWinner, Is.EqualTo(demoUri));

        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.That(json, Does.Contain("\"relativePath\":\"CrowdSimulation/traversal.json\""));
        Assert.That(json, Does.Contain("\"fileWinner\":\"CrowdSimulationDemoContentMod:assets/CrowdSimulation/traversal.json\""));
        Assert.That(json, Does.Contain("\"fragments\""));
        Assert.That(json, Does.Contain("\"winners\""));
        Assert.That(json, Does.Contain("\"deletions\""));
        Assert.That(json, Does.Contain("\"duplicates\""));
        Assert.That(json, Does.Contain("\"componentOverrideChains\""));
    }

    [Test]
    public void Capture_ArrayById_ReportsPerIdWinnerDeletionsAndDuplicates()
    {
        string root = NewTempDir();
        try
        {
            WriteCoreCatalog(root, """
                [
                  { "Path": "Entities/templates.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
            WriteCoreFile(root, "Entities/templates.json", """
                [
                  { "id": "Kept", "components": {} },
                  { "id": "Gone", "components": {} },
                  { "id": "Shared", "components": { "Health": { "max": 1 } } }
                ]
                """);
            string probe = WriteProbe(root, "Entities/templates.json", """
                [
                  { "id": "Gone", "__delete": true },
                  { "id": "Shared", "components": { "Health": { "max": 2 } } }
                ]
                """);
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root, (ProbeModId, probe));
            ConfigProvenanceSnapshot snapshot = ConfigMergeProvenance.Capture(pipeline, catalog);
            ConfigProvenanceFile templates = FindFile(snapshot, "Entities/templates.json");
            string probeUri = $"{ProbeModId}:assets/Entities/templates.json";

            Assert.That(templates.FileWinner, Is.Null);
            Assert.That(templates.Fragments, Is.EqualTo(new[] { "Core:Entities/templates.json", probeUri }));
            Assert.That(templates.Winners, Has.Some.Matches<ConfigProvenanceIdWinner>(w => w.Id == "Shared" && w.SourceUri == probeUri));
            Assert.That(templates.Winners, Has.None.Matches<ConfigProvenanceIdWinner>(w => w.Id == "Gone"));
            Assert.That(templates.Deletions, Has.Some.Matches<ConfigProvenanceDeletion>(d => d.Id == "Gone" && d.SourceUri == probeUri));
            Assert.That(templates.Duplicates, Has.Some.Matches<ConfigProvenanceDuplicate>(d =>
                d.Id == "Shared" &&
                d.FirstSource == "Core:Entities/templates.json" &&
                d.SecondSource == probeUri));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ExceptionCaughtInsideLoad_IsUnchanged()
    {
        var caught = new InvalidOperationException("inside-load");
        string result = ConfigMergeProvenance.Wrap("probe.json", null, _ =>
        {
            try
            {
                throw caught;
            }
            catch (InvalidOperationException ex)
            {
                Assert.That(ex, Is.SameAs(caught));
                Assert.That(ex.Message, Is.EqualTo("inside-load"));
                Assert.That(ex.Message, Does.Not.Contain("配置路径:"));
                return "ok";
            }
        });

        Assert.That(result, Is.EqualTo("ok"));
    }

    [Test]
    public void DeepObject_SkipsNonObject_WinnerIsLastAppliedObject()
    {
        string root = NewTempDir();
        try
        {
            WriteCoreCatalog(root, """[{ "Path": "probe.json", "Policy": "DeepObject" }]""");
            WriteCoreFile(root, "probe.json", """{ "kept": 1, "replaced": 1 }""");
            string probe = WriteProbe(root, "probe.json", """[1, 2, 3]""");
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root, (ProbeModId, probe));
            var entry = ConfigPipeline.RequireEntry(catalog, "probe.json", ConfigMergePolicy.DeepObject);
            var report = new ConfigConflictReport();
            pipeline.MergeDeepObjectFromCatalog(in entry, report);
            ConfigProvenanceFile file = FindFile(ConfigMergeProvenance.ToSnapshot(report), "probe.json");
            Assert.That(file.Fragments, Is.EqualTo(new[] { "Core:probe.json", $"{ProbeModId}:assets/probe.json" }));
            Assert.That(file.FileWinner, Is.EqualTo("Core:probe.json"));
            Assert.That(file.FileWinnerKind, Is.EqualTo(ConfigConflictReport.FileWinnerLastFragment));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ArrayReplace_WinnerIsFragmentWhoseArrayWasUsed()
    {
        string root = NewTempDir();
        try
        {
            WriteCoreCatalog(root, """[{ "Path": "probe.json", "Policy": "ArrayReplace" }]""");
            WriteCoreFile(root, "probe.json", """[{ "id": "A" }]""");
            string skipped = WriteProbe(root, "probe.json", """{ "not": "an-array" }""");
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root, (ProbeModId, skipped));
            var entry = ConfigPipeline.RequireEntry(catalog, "probe.json", ConfigMergePolicy.ArrayReplace);
            var report = new ConfigConflictReport();
            pipeline.MergeFromCatalog(in entry, report);
            ConfigProvenanceFile file = FindFile(ConfigMergeProvenance.ToSnapshot(report), "probe.json");
            Assert.That(file.FileWinner, Is.EqualTo("Core:probe.json"));
            Assert.That(file.FileWinnerKind, Is.EqualTo(ConfigConflictReport.FileWinnerAppliedArray));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Capture_BadTemplateExpand_StillReturnsProvenance()
    {
        string root = NewTempDir();
        try
        {
            WriteCoreCatalog(root, """[{ "Path": "Entities/templates.json", "Policy": "ArrayById", "IdField": "id" }]""");
            WriteCoreFile(root, "Entities/templates.json", """[{ "id": "Child", "extends": "Missing", "components": {} }]""");
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root);
            ConfigProvenanceSnapshot snapshot = ConfigMergeProvenance.Capture(pipeline, catalog);
            ConfigProvenanceFile templates = FindFile(snapshot, "Entities/templates.json");
            Assert.That(templates.Fragments, Is.EqualTo(new[] { "Core:Entities/templates.json" }));
            Assert.That(snapshot.Error, Does.Contain("extends unknown template"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Capture_ModOrder_MatchesGameResolvedPlan()
    {
        string root = NewTempDir();
        try
        {
            string assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(assets);
            WriteCoreCatalog(assets, """[{ "Path": "probe.json", "Policy": "DeepObject" }]""");
            WriteCoreFile(assets, "probe.json", """{ "k": 0 }""");
            WriteMod(root, "LowMod", 1, "{}", """{ "k": 1 }""");
            WriteMod(root, "HighMod", 9, "{}", """{ "k": 9 }""");
            WriteMod(root, "TargetMod", 0, """{ "HighMod": "*", "LowMod": "*" }""", null);

            IReadOnlyList<ResolvedModLoadEntry> plan = NavMeshBakeConfigLoader.ResolveModLoadPlan(root, "TargetMod");
            Assert.That(plan.Select(item => item.Id).ToArray(), Is.EqualTo(new[] { "HighMod", "LowMod", "TargetMod" }));

            var vfs = new VirtualFileSystem();
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadResolvedPlan(plan);
            Assert.That(modLoader.LoadedModIds, Is.EqualTo(new[] { "HighMod", "LowMod", "TargetMod" }));

            var mounts = new List<ConfigModMount>(plan.Count);
            for (int i = 0; i < plan.Count; i++)
            {
                mounts.Add(new ConfigModMount(plan[i].Id, plan[i].RootPath));
            }

            ConfigProvenanceSnapshot snapshot = ConfigMergeProvenance.Capture(Path.Combine(root, "assets"), mounts);
            ConfigProvenanceFile file = FindFile(snapshot, "probe.json");
            Assert.That(file.Fragments, Is.EqualTo(new[]
            {
                "Core:probe.json",
                "HighMod:assets/probe.json",
                "LowMod:assets/probe.json",
            }));
            Assert.Throws<InvalidOperationException>(() => NavMeshBakeConfigLoader.ResolveModLoadPlan(root, "NoSuchMod"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertProvenance(
        ConfigLoadException ex,
        string relativePath,
        string[] fragments,
        string winnerText,
        Type innerType)
    {
        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException, Is.TypeOf(innerType));
        Assert.That(ex.InnerException!.Message, Does.Not.Contain("配置路径:"));
        string message = ex.Message;
        Assert.That(message, Does.Contain($"配置路径: {relativePath}"));
        Assert.That(message.Split("配置路径:").Length - 1, Is.EqualTo(1));
        Assert.That(LineAfter(message, "合并顺序:"), Is.EqualTo(string.Join(" | ", fragments)));
        Assert.That(LineAfter(message, "胜出:"), Is.EqualTo(winnerText));
    }

    private static string LineAfter(string message, string label)
    {
        int at = message.IndexOf(label, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0));
        int start = at + label.Length;
        int end = message.IndexOf('\n', start);
        string line = end < 0 ? message.Substring(start) : message.Substring(start, end - start);
        return line.Trim();
    }

    private static int IndexOf(IReadOnlyList<string> items, string value)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static ConfigProvenanceFile FindFile(ConfigProvenanceSnapshot snapshot, string relativePath)
    {
        for (int i = 0; i < snapshot.Files.Count; i++)
        {
            if (string.Equals(snapshot.Files[i].RelativePath, relativePath, StringComparison.Ordinal))
            {
                return snapshot.Files[i];
            }
        }

        Assert.Fail($"快照里没有 {relativePath}");
        return null!;
    }

    private static (ConfigPipeline Pipeline, ConfigCatalog Catalog) Mount(string coreRoot, params (string Id, string Root)[] mods)
    {
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", coreRoot);
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        for (int i = 0; i < mods.Length; i++)
        {
            vfs.Mount(mods[i].Id, mods[i].Root);
            modLoader.LoadedModIds.Add(mods[i].Id);
        }

        var pipeline = new ConfigPipeline(vfs, modLoader);
        return (pipeline, ConfigCatalogLoader.Load(pipeline));
    }

    private static AgentProfileRegistry DefaultProfiles() => new(new[]
    {
        new AgentProfileConfig
        {
            Id = "Small",
            RadiusCm = 30,
            HeightCm = 180,
            ClearanceCm = 40,
            Mass = 1,
            Layer = 0,
            NavProfileId = "Small",
        }
    });

    private static string MinimalNavmesh(string maxClimbCmJson) =>
        $$"""
        {
          "mode": "offline",
          "algorithm": "recast",
          "profiles": { "Small": { "maxClimbCm": {{maxClimbCmJson}}, "maxSlopeDeg": 45, "agentProfileId": "Small" } },
          "layers": [ { "id": "Ground", "layer": 0 } ],
          "areas": { "Default": { "areaId": 0, "slopeFree": false } },
          "runtimeIncremental": {
            "tileBudgetPerFixedTick": 1,
            "includeNeighborTiles": true,
            "heightScaleMeters": 1.0,
            "minWalkableUpDot": 0.6,
            "cliffHeightThreshold": 1
          }
        }
        """;

    private static string MinimalPathing() =>
        """
        {
          "agentTypes": [
            {
              "id": "Humanoid",
              "profileId": "Small",
              "selection": {
                "mode": "PreferMesh",
                "graphBias": 0.0,
                "meshBias": 0.0,
                "graphCostWeight": 1.0,
                "meshCostWeight": 1.0
              },
              "navMesh": { "areaCosts": [ { "areaId": 0, "cost": 1.0, "passable": true } ] },
              "nodeGraph": {
                "projectionMaxRadiusCm": 200000,
                "useDynamicOverlay": false,
                "forbiddenTagsAny": [],
                "requiredTagsAll": [],
                "tagCostRules": []
              }
            }
          ]
        }
        """;

    private static string MinimalProfiles(int radiusCm) =>
        $$"""
        [
          {
            "id": "Small",
            "radiusCm": {{radiusCm}},
            "heightCm": 180,
            "clearanceCm": 40,
            "draftCm": 0,
            "beamCm": 0,
            "mass": 1,
            "layer": 0,
            "navProfileId": "Small"
          }
        ]
        """;

    private static void WriteCoreCatalog(string coreRoot, string json)
    {
        Directory.CreateDirectory(coreRoot);
        File.WriteAllText(Path.Combine(coreRoot, "config_catalog.json"), json);
    }

    private static void WriteCoreFile(string coreRoot, string relativePath, string json)
    {
        string path = Path.Combine(coreRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static void WriteMod(string repo, string id, int priority, string dependenciesJson, string? probeJson)
    {
        string modRoot = Path.Combine(repo, "mods", id);
        Directory.CreateDirectory(modRoot);
        File.WriteAllText(
            Path.Combine(modRoot, "mod.json"),
            $$"""
            {
              "name": "{{id}}",
              "version": "1.0.0",
              "priority": {{priority}},
              "dependencies": {{dependenciesJson}}
            }
            """);
        if (probeJson == null)
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(modRoot, "assets"));
        File.WriteAllText(Path.Combine(modRoot, "assets", "probe.json"), probeJson);
    }

    private static string WriteProbe(string parent, string relativePath, string json)
    {
        string probe = Path.Combine(parent, ProbeModId);
        string path = Path.Combine(probe, "assets", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return probe;
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ludots-provenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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
