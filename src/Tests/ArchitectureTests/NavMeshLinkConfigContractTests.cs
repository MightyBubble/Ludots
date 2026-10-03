using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

/// <summary>
/// navmesh.json 的 links[] 合同：Link 是跨表面连接的正式边，
/// 端点与允许的 profile 必须在加载期就对已声明的 layer / agent profile 成立；
/// 未知键、重复 id、未声明引用一律 fail-fast，不允许运行期降级为直线。
/// </summary>
[TestFixture]
public sealed class NavMeshLinkConfigContractTests
{
    private const string SceneAssets = "mods/showcases/navmesh_openworld/NavMeshOpenWorldShowcaseMod/assets";
    [Test]
    public void Links_AbsentKeyIsAllowed()
    {
        NavMeshBakeConfig config = Load("\"links\": []", out _);
        Assert.That(config.Links, Is.Not.Null);
        Assert.That(config.Links, Is.Empty);
    }

    [Test]
    public void Links_TwoLayerConnectionDeserializes()
    {
        NavMeshBakeConfig config = Load(
            """
            "links": [
              {
                "id": "pier",
                "from": { "layer": "Ground", "xCm": 1000, "yCm": 2000 },
                "to": { "layer": "Water", "xCm": 3000, "yCm": 4000 },
                "bidirectional": true,
                "allowedProfiles": ["amphibious"],
                "cost": 50.0,
                "action": "boarding"
              }
            ]
            """,
            out _);

        Assert.That(config.Links, Has.Count.EqualTo(1));
        NavLinkConfig link = config.Links[0];
        Assert.That(link.Id, Is.EqualTo("pier"));
        Assert.That(link.From.Layer, Is.EqualTo("Ground"));
        Assert.That(link.From.XCm, Is.EqualTo(1000));
        Assert.That(link.From.YCm, Is.EqualTo(2000));
        Assert.That(link.To.Layer, Is.EqualTo("Water"));
        Assert.That(link.Bidirectional, Is.True);
        Assert.That(link.AllowedProfiles, Is.EqualTo(new[] { "amphibious" }));
        Assert.That(link.Cost, Is.EqualTo(50f));
        Assert.That(link.Action, Is.EqualTo("boarding"));
    }

    [Test]
    public void Links_UndeclaredLayerIsRejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(
            """
            "links": [
              {
                "id": "bad",
                "from": { "layer": "Space", "xCm": 0, "yCm": 0 },
                "to": { "layer": "Water", "xCm": 0, "yCm": 0 }
              }
            ]
            """,
            out _))!;

        Assert.That(ex.Message, Does.Contain("undeclared layer 'Space'"));
    }

    [Test]
    public void Links_UndeclaredAllowedProfileIsRejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(
            """
            "links": [
              {
                "id": "bad",
                "from": { "layer": "Ground", "xCm": 0, "yCm": 0 },
                "to": { "layer": "Water", "xCm": 0, "yCm": 0 },
                "allowedProfiles": ["ghost_unit"]
              }
            ]
            """,
            out _))!;

        Assert.That(ex.Message, Does.Contain("undeclared agent profile 'ghost_unit'"));
    }

    [Test]
    public void Links_DuplicateIdIsRejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(
            """
            "links": [
              { "id": "dup", "from": { "layer": "Ground", "xCm": 0, "yCm": 0 }, "to": { "layer": "Water", "xCm": 0, "yCm": 0 } },
              { "id": "dup", "from": { "layer": "Water", "xCm": 0, "yCm": 0 }, "to": { "layer": "Ground", "xCm": 0, "yCm": 0 } }
            ]
            """,
            out _))!;

        Assert.That(ex.Message, Does.Contain("duplicate link id 'dup'"));
    }

    [Test]
    public void Links_UnknownKeyIsRejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(
            """
            "links": [
              {
                "id": "bad",
                "from": { "layer": "Ground", "xCm": 0, "yCm": 0 },
                "to": { "layer": "Water", "xCm": 0, "yCm": 0 },
                "teleport": true
              }
            ]
            """,
            out _))!;

        Assert.That(ex.Message, Does.Contain("unknown property 'teleport'"));
    }

    [Test]
    public void Links_MissingEndpointIsRejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(
            """
            "links": [
              { "id": "half", "from": { "layer": "Ground", "xCm": 0, "yCm": 0 } }
            ]
            """,
            out _))!;

        Assert.That(ex.Message, Does.Contain("requires an explicit 'to' endpoint"));
    }

    [Test]
    public void Links_NegativeCostIsRejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load(
            """
            "links": [
              {
                "id": "cheap",
                "from": { "layer": "Ground", "xCm": 0, "yCm": 0 },
                "to": { "layer": "Water", "xCm": 0, "yCm": 0 },
                "cost": -1.0
              }
            ]
            """,
            out _))!;

        Assert.That(ex.Message, Does.Contain("cost must be a finite value >= 0"));
    }

    private static NavMeshBakeConfig Load(string linksJson, out AgentProfileRegistry profiles)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-nav-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "Navigation"));
        File.WriteAllText(
            Path.Combine(tempRoot, "config_catalog.json"),
            """
            [
              { "Path": "Navigation/agent_profiles.json", "Policy": "ArrayById", "IdField": "id" },
              { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" }
            ]
            """);
        File.WriteAllText(
            Path.Combine(tempRoot, "Navigation", "agent_profiles.json"),
            """
            [
              { "id": "infantry",    "radiusCm": 50,  "heightCm": 180, "clearanceCm": 50,  "draftCm": 0,   "beamCm": 0,   "mass": 80,    "layer": 0 },
              { "id": "ship",        "radiusCm": 300, "heightCm": 400, "clearanceCm": 300, "draftCm": 120, "beamCm": 600, "mass": 5000,  "layer": 2 },
              { "id": "amphibious",  "radiusCm": 250, "heightCm": 350, "clearanceCm": 250, "draftCm": 100, "beamCm": 500, "mass": 4000,  "layer": 0 }
            ]
            """);
        File.WriteAllText(
            Path.Combine(tempRoot, "Navigation", "navmesh.json"),
            $$"""
            {
              "mode": "offline",
              "algorithm": "recast",
              "profiles": [
                { "id": "infantry", "maxClimbCm": 45, "maxSlopeDeg": 35 },
                { "id": "ship", "maxClimbCm": 0, "maxSlopeDeg": 1 },
                { "id": "amphibious", "maxClimbCm": 45, "maxSlopeDeg": 35 }
              ],
              "layers": [
                { "id": "Ground", "layer": 0 },
                { "id": "Water", "layer": 2 }
              ],
              "areas": [ { "id": "Default", "areaId": 0, "cost": 1.0 } ],
              "runtimeIncremental": {
                "tileBudgetPerFixedTick": 1,
                "includeNeighborTiles": true,
                "heightScaleMeters": 1.0,
                "minWalkableUpDot": 0.6,
                "cliffHeightThreshold": 1
              },
              {{linksJson}}
            }
            """);

        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", tempRoot);
        var pipeline = new ConfigPipeline(vfs, modLoader: null!);
        var catalog = ConfigCatalogLoader.Load(pipeline);
        profiles = new AgentProfileConfigLoader(pipeline).Load(catalog);
        NavMeshBakeConfig config = new NavMeshBakeConfigLoader(pipeline, profiles).Load(catalog);
        Directory.Delete(tempRoot, recursive: true);
        return config;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
