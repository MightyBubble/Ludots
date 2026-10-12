using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
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

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new CrowdSimulationConfigLoader(pipeline).LoadConfig(catalog))!;

            const string standardUri = "CrowdSimulationStandardMod:assets/CrowdSimulationConfig.json";
            string winnerUri = $"{ProbeModId}:assets/CrowdSimulationConfig.json";
            AssertProvenance(ex.Message, "CrowdSimulationConfig.json", standardUri, winnerUri, $"胜出: {winnerUri}");
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

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new NavMeshBakeConfigLoader(pipeline, DefaultProfiles()).Load(catalog))!;

            AssertProvenance(
                ex.Message,
                "Navigation/navmesh.json",
                "Core:Navigation/navmesh.json",
                $"{ProbeModId}:assets/Navigation/navmesh.json",
                $"胜出: {ProbeModId}:assets/Navigation/navmesh.json");
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

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new PathingConfigLoader(pipeline).Load(catalog))!;

            AssertProvenance(
                ex.Message,
                "Navigation/pathing.json",
                "Core:Navigation/pathing.json",
                $"{ProbeModId}:assets/Navigation/pathing.json",
                $"胜出: {ProbeModId}:assets/Navigation/pathing.json");
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
                """[{ "id": "Small", "radiusCm": "not-a-number" }]""");
            (ConfigPipeline pipeline, ConfigCatalog catalog) = Mount(root, (ProbeModId, probe));

            Exception ex = Assert.Throws<JsonException>(
                () => new AgentProfileConfigLoader(pipeline).Load(catalog))!;

            string winnerUri = $"{ProbeModId}:assets/Navigation/agent_profiles.json";
            AssertProvenance(
                ex.Message,
                "Navigation/agent_profiles.json",
                "Core:Navigation/agent_profiles.json",
                winnerUri,
                $"Small={winnerUri}");
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

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new DataRegistry<EntityTemplate>(pipeline).Load("Entities/templates.json", catalog))!;

            string winnerUri = $"{ProbeModId}:assets/Entities/templates.json";
            AssertProvenance(
                ex.Message,
                "Entities/templates.json",
                "Core:Entities/templates.json",
                winnerUri,
                $"Unit.Probe={winnerUri}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Capture_StandardModLoadOrder_ReportsNavmeshFragmentsAndFileWinner()
    {
        string repoRoot = FindRepoRoot();
        var mounts = new List<ConfigModMount>
        {
            new("LudotsCoreMod", Path.Combine(repoRoot, "mods", "LudotsCoreMod")),
            new("CrowdSimulationMod", Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod")),
            new("CrowdSimulationStandardMod", Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod")),
        };

        ConfigProvenanceSnapshot snapshot = ConfigMergeProvenance.Capture(Path.Combine(repoRoot, "assets"), mounts);
        ConfigProvenanceFile navmesh = FindFile(snapshot, "Navigation/navmesh.json");
        Assert.That(navmesh.Fragments, Does.Contain("Core:Navigation/navmesh.json"));
        Assert.That(navmesh.Fragments, Does.Contain("LudotsCoreMod:assets/Navigation/navmesh.json"));
        Assert.That(IndexOf(navmesh.Fragments, "Core:Navigation/navmesh.json"), Is.LessThan(IndexOf(navmesh.Fragments, "LudotsCoreMod:assets/Navigation/navmesh.json")));
        Assert.That(navmesh.FileWinner, Is.EqualTo("LudotsCoreMod:assets/Navigation/navmesh.json"));

        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.That(json, Does.Contain("\"relativePath\":\"Navigation/navmesh.json\""));
        Assert.That(json, Does.Contain("\"fileWinner\":\"LudotsCoreMod:assets/Navigation/navmesh.json\""));
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
            Assert.That(templates.Fragments, Does.Contain("Core:Entities/templates.json"));
            Assert.That(templates.Fragments, Does.Contain(probeUri));
            Assert.That(templates.Winners, Has.Some.Matches<ConfigProvenanceIdWinner>(w => w.Id == "Shared" && w.SourceUri == probeUri));
            Assert.That(templates.Winners, Has.None.Matches<ConfigProvenanceIdWinner>(w => w.Id == "Gone"));
            Assert.That(templates.Deletions, Has.Some.Matches<ConfigProvenanceDeletion>(d => d.Id == "Gone"));
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

    private static void AssertProvenance(string message, string relativePath, string earlierUri, string laterUri, string winnerText)
    {
        Assert.That(message, Does.Contain($"配置路径: {relativePath}"));
        Assert.That(message.Split("配置路径:").Length - 1, Is.EqualTo(1));
        int orderAt = message.IndexOf("合并顺序:", StringComparison.Ordinal);
        Assert.That(orderAt, Is.GreaterThanOrEqualTo(0));
        int earlierAt = message.IndexOf(earlierUri, orderAt, StringComparison.Ordinal);
        int laterAt = message.IndexOf(laterUri, orderAt, StringComparison.Ordinal);
        Assert.That(earlierAt, Is.GreaterThanOrEqualTo(0));
        Assert.That(laterAt, Is.GreaterThan(earlierAt));
        Assert.That(message, Does.Contain(winnerText));
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
        new AgentProfileConfig { Id = "Small", RadiusCm = 30, HeightCm = 180, ClearanceCm = 40, Mass = 1, Layer = 0 }
    });

    private static string MinimalNavmesh(string maxClimbCmJson) =>
        $$"""
        {
          "mode": "offline",
          "algorithm": "recast",
          "profiles": [ { "id": "Small", "maxClimbCm": {{maxClimbCmJson}}, "maxSlopeDeg": 45 } ],
          "layers": [ { "id": "Ground", "layer": 0 } ],
          "areas": [ { "id": "Default", "areaId": 0, "cost": 1.0 } ],
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
              "navMesh": { "areaCosts": [ { "areaId": 0, "cost": 1.0 } ] },
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
            "layer": 0
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
