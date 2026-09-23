using System;
using System.IO;
using System.Linq;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Tool;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

/// <summary>
/// NavMeshOpenWorldShowcaseMod 是纯资产（0 编码）showcase：三种通行层、五个 agent profile、
/// 一套 pathing 全部由配置声明。本测试证明该配置能被正式严格 loader 接受，
/// 且四类通行能力（陆军 / 山地军 / 深吃水船 / 浅吃水船）在 profile 与 layer 上确实分离。
/// </summary>
[TestFixture]
public sealed class NavMeshOpenWorldContractTests
{
    private const string ModId = "NavMeshOpenWorldShowcaseMod";
    private const string MapId = "navmesh_openworld_strait";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new InvalidOperationException("repository root with showcase.registry.json not found");
        }

        return dir.FullName;
    }

    [Test]
    public void Showcase_IsAssetOnly()
    {
        string modRoot = Path.Combine(FindRepoRoot(), "mods", "showcases", "navmesh_openworld", ModId);
        string[] sources = Directory.EnumerateFiles(modRoot, "*.cs", SearchOption.AllDirectories).ToArray();

        Assert.That(sources, Is.Empty,
            "openworld showcase must stay data-driven: no C# sources, no gameplay or navigation logic in code.");
    }

    [Test]
    public void Showcase_DeclaresNavigationEnabledMapWithSingleBoard()
    {
        string root = FindRepoRoot();
        MapConfig map = ToolMapConfigResolver.LoadMap(root, MapId, ModId);

        Assert.That(map.Tags, Does.Contain("Feature.NavMesh:On"));
        Assert.That(map.RootBoard, Is.EqualTo("mainland"));

        var board = ToolMapConfigResolver.ResolvePrimaryNavigationBoard(map);
        Assert.That(board.Name, Is.EqualTo("mainland"));
        Assert.That(board.WidthCells, Is.EqualTo(2048));
        Assert.That(board.HeightCells, Is.EqualTo(2048));
        Assert.That(board.GridCellSizeCm, Is.EqualTo(100));
    }

    [Test]
    public void Showcase_DeclaresThreeIsolatedLayers()
    {
        NavMeshBakeConfigContext nav = LoadNavBakeConfig();

        Assert.That(nav.Config.ParsedMode, Is.EqualTo(NavBakeMode.Offline));
        Assert.That(nav.Config.ParsedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Recast));

        Assert.That(
            nav.Config.Layers.Select(layer => (layer.Id, layer.Layer)),
            Is.EqualTo(new[] { ("Ground", 0), ("Mountain", 1), ("Water", 2) }),
            "ground, mountain and water must be separate navigation layers with distinct layer ids.");
    }

    [Test]
    public void Showcase_DeclaresPerCapabilityBakeProfiles()
    {
        NavMeshBakeConfigContext nav = LoadNavBakeConfig();

        NavMeshAgentProfileConfig infantry = nav.Config.Profiles.Single(p => p.Id == "land_infantry");
        NavMeshAgentProfileConfig mountaineer = nav.Config.Profiles.Single(p => p.Id == "mountain_corps");
        NavMeshAgentProfileConfig ship = nav.Config.Profiles.Single(p => p.Id == "naval_shallow");

        Assert.That(mountaineer.MaxClimbCm, Is.GreaterThan(infantry.MaxClimbCm),
            "mountain corps must out-climb line infantry.");
        Assert.That(mountaineer.MaxSlopeDeg, Is.GreaterThan(infantry.MaxSlopeDeg),
            "mountain corps must out-slope line infantry.");
        Assert.That(ship.MaxClimbCm, Is.EqualTo(0),
            "ships must not climb terrain.");
        Assert.That(ship.MaxSlopeDeg, Is.LessThanOrEqualTo(1f),
            "ships must only traverse water surfaces.");
    }

    [Test]
    public void Showcase_AgentProfilesSeparateByLayerAndDraft()
    {
        AgentProfileRegistry registry = LoadAgentProfiles();

        AgentProfileConfig infantry = registry.Require("land_infantry", "test");
        AgentProfileConfig mountain = registry.Require("mountain_corps", "test");
        AgentProfileConfig shallow = registry.Require("naval_shallow", "test");
        AgentProfileConfig deep = registry.Require("naval_deep", "test");
        AgentProfileConfig amphibious = registry.Require("amphibious", "test");

        Assert.That(infantry.Layer, Is.EqualTo(0));
        Assert.That(mountain.Layer, Is.EqualTo(1));
        Assert.That(shallow.Layer, Is.EqualTo(2));
        Assert.That(deep.Layer, Is.EqualTo(2));

        Assert.That(infantry.DraftCm, Is.Zero, "land units must declare no draft.");
        Assert.That(mountain.DraftCm, Is.Zero, "mountain units must declare no draft.");
        Assert.That(deep.DraftCm, Is.GreaterThan(shallow.DraftCm),
            "the deep-draft hull must draw more water than the shallow-draft hull.");
        Assert.That(deep.BeamCm, Is.GreaterThan(shallow.BeamCm),
            "the deep-draft hull must be wider than the shallow-draft hull.");

        Assert.That(amphibious.Layer, Is.EqualTo(0),
            "amphibious units begin on the ground layer and change layer through links.");
    }

    private static AgentProfileRegistry LoadAgentProfiles()
    {
        // 配置源路径就是 "Navigation/agent_profiles.json"（不经 {modId}:assets/ 前缀），
        // 因此把 showcase 的 assets 目录挂成 Core 源，与 NavBakeConfigLoaderTestHelpers 同一范式。
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", ShowcaseAssetsRoot());
        var pipeline = new ConfigPipeline(vfs, modLoader: null!);
        var catalog = ConfigCatalogLoader.Load(pipeline);
        return new AgentProfileConfigLoader(pipeline).Load(catalog);
    }

    private static NavMeshBakeConfigContext LoadNavBakeConfig()
    {
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", ShowcaseAssetsRoot());
        var pipeline = new ConfigPipeline(vfs, modLoader: null!);
        var catalog = ConfigCatalogLoader.Load(pipeline);
        AgentProfileRegistry profiles = new AgentProfileConfigLoader(pipeline).Load(catalog);
        NavMeshBakeConfig config = new NavMeshBakeConfigLoader(pipeline, profiles).Load(catalog);
        return new NavMeshBakeConfigContext(config, profiles);
    }

    private static string ShowcaseAssetsRoot()
    {
        return Path.Combine(FindRepoRoot(), "mods", "showcases", "navmesh_openworld", ModId, "assets");
    }
}
