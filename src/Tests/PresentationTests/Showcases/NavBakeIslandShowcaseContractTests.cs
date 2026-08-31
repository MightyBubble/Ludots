using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Buffers.Binary;
using NUnit.Framework;

namespace Ludots.Tests.Presentation.Showcases;

[TestFixture]
public sealed class NavBakeIslandShowcaseContractTests
{
    [Test]
    public void ShowcaseAssetsDescribeOneConfigDrivenMassNavigationChain()
    {
        string root = FindRepoRoot();
        string showcaseRoot = Path.Combine(root, "mods", "showcases", "navmesh_bake_island", "NavBakeIslandShowcaseMod");
        string massNavigationRoot = Path.Combine(root, "mods", "capabilities", "navigation", "MassNavigationMod");
        using JsonDocument massNav = ReadJson(Path.Combine(showcaseRoot, "assets", "MassNavigationConfig.json"));
        using JsonDocument game = ReadJson(Path.Combine(showcaseRoot, "assets", "game.json"));
        using JsonDocument pathing = ReadJson(Path.Combine(showcaseRoot, "assets", "Navigation", "pathing.json"));
        using JsonDocument clips = ReadJson(Path.Combine(massNavigationRoot, "assets", "Presentation", "animation_clips.json"));
        using JsonDocument controllers = ReadJson(Path.Combine(massNavigationRoot, "assets", "Presentation", "animator_controllers.json"));
        using JsonDocument profiles = ReadJson(Path.Combine(massNavigationRoot, "assets", "Presentation", "animation_profiles.json"));
        using JsonDocument showcaseCatalog = ReadJson(Path.Combine(showcaseRoot, "assets", "config_catalog.json"));
        using JsonDocument inputMappings = ReadJson(Path.Combine(showcaseRoot, "assets", "Input", "input_order_mappings.json"));
        using JsonDocument map = ReadJson(Path.Combine(showcaseRoot, "assets", "Maps", "nav_bake_island.json"));

        JsonElement massNavRoot = massNav.RootElement;
        JsonElement teams = massNavRoot.GetProperty("scenario").GetProperty("teams");
        int expectedAgents = massNavRoot.GetProperty("scenario").GetProperty("agentsPerTeam").GetInt32() * teams.GetArrayLength();
        Assert.That(expectedAgents, Is.GreaterThan(0));
        Assert.That(massNavRoot.GetProperty("scenarioRuntime").GetProperty("runtimeCapacity").GetProperty("navigationGroupCapacity").GetInt32(), Is.GreaterThanOrEqualTo(teams.GetArrayLength()));
        Assert.That(massNavRoot.GetProperty("scenarioRuntime").GetProperty("runtimeCapacity").GetProperty("groupMemberCapacity").GetInt32(), Is.GreaterThanOrEqualTo(expectedAgents));
        Assert.That(game.RootElement.GetProperty("presentation").GetProperty("skinnedVisualBatchCapacity").GetInt32(), Is.GreaterThanOrEqualTo(expectedAgents));
        Assert.That(game.RootElement.GetProperty("presentation").GetProperty("minimapMarkerCapacity").GetInt32(), Is.GreaterThanOrEqualTo(expectedAgents));
        JsonElement heightmap = map.RootElement.GetProperty("ContinuousHeightmap");
        Assert.That(heightmap.GetProperty("Asset").GetString(), Does.EndWith("tropical_island.height"));
        Assert.That(heightmap.GetProperty("WorldWidthCm").GetInt32(), Is.EqualTo(128_000));
        Assert.That(File.Exists(Path.Combine(showcaseRoot, "assets", "terrain", "tropical_island.height")), Is.True);

        JsonElement board = map.RootElement.GetProperty("Boards")[0];
        int boardWidthCm = checked(board.GetProperty("WidthInMacroTiles").GetInt32() * 256 * board.GetProperty("GridCellSizeCm").GetInt32());
        int boardHeightCm = checked(board.GetProperty("HeightInMacroTiles").GetInt32() * 256 * board.GetProperty("GridCellSizeCm").GetInt32());
        JsonElement navTileGrid = board.GetProperty("NavTileGrid");
        int navWidthCm = checked(navTileGrid.GetProperty("widthChunks").GetInt32() * navTileGrid.GetProperty("chunkSizeCells").GetInt32() * navTileGrid.GetProperty("cellSizeCm").GetInt32());
        int navHeightCm = checked(navTileGrid.GetProperty("heightChunks").GetInt32() * navTileGrid.GetProperty("chunkSizeCells").GetInt32() * navTileGrid.GetProperty("cellSizeCm").GetInt32());
        Assert.That(boardWidthCm, Is.EqualTo(128_000));
        Assert.That(boardHeightCm, Is.EqualTo(128_000));
        Assert.That(navWidthCm, Is.EqualTo(boardWidthCm));
        Assert.That(navHeightCm, Is.EqualTo(boardHeightCm));
        Assert.That(navTileGrid.GetProperty("originXcm").GetInt32(), Is.EqualTo(-(boardWidthCm / 2)));
        Assert.That(navTileGrid.GetProperty("originZcm").GetInt32(), Is.EqualTo(-(boardHeightCm / 2)));
        Assert.That(map.RootElement.TryGetProperty("ContinuousHeightmapAsset", out _), Is.False);

        string heightmapPath = Path.Combine(showcaseRoot, "assets", "terrain", "tropical_island.height");
        byte[] heightmapHeader = File.ReadAllBytes(heightmapPath)[..24];
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(heightmapHeader.AsSpan(8, 4)), Is.EqualTo(-(boardWidthCm / 2)));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(heightmapHeader.AsSpan(12, 4)), Is.EqualTo(-(boardHeightCm / 2)));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(heightmapHeader.AsSpan(16, 4)), Is.EqualTo(boardWidthCm));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(heightmapHeader.AsSpan(20, 4)), Is.EqualTo(boardHeightCm));

        int streamingRadiusCm = massNavRoot.GetProperty("streaming").GetProperty("radiusCm").GetInt32();
        int streamingChunkSizeCm = massNavRoot.GetProperty("world").GetProperty("streamingChunkSizeCm").GetInt32();
        int solverWindowWidthCm = massNavRoot.GetProperty("world").GetProperty("solverWindowWidthCm").GetInt32();
        int focusRangeCm = checked((boardWidthCm - solverWindowWidthCm) / 2);
        int retainedSpanCm = checked((streamingRadiusCm + focusRangeCm) * 2);
        int retainedSpanChunks = checked((int)Math.Ceiling(retainedSpanCm / (double)streamingChunkSizeCm));
        int minimumRetainedChunkCapacity = checked(retainedSpanChunks * retainedSpanChunks);
        int runtimeLoadedChunkCapacity = massNavRoot.GetProperty("scenarioRuntime").GetProperty("runtimeCapacity").GetProperty("loadedChunkCapacity").GetInt32();
        int boardLoadedChunkCapacity = board.GetProperty("LoadedChunkCapacity").GetInt32();
        Assert.That(runtimeLoadedChunkCapacity, Is.GreaterThanOrEqualTo(minimumRetainedChunkCapacity));
        Assert.That(boardLoadedChunkCapacity, Is.EqualTo(runtimeLoadedChunkCapacity));

        JsonElement agentTypes = pathing.RootElement.GetProperty("agentTypes");
        Assert.That(agentTypes.GetArrayLength(), Is.EqualTo(2));
        foreach (JsonElement agentType in agentTypes.EnumerateArray())
        {
            Assert.That(agentType.GetProperty("selection").GetProperty("mode").GetString(), Is.EqualTo("PreferMesh"));
        }

        JsonElement stateClips = profiles.RootElement[0].GetProperty("stateClips");
        Assert.That(stateClips.EnumerateArray().Select(static x => x.GetProperty("packedStateIndex").GetInt32()), Is.EquivalentTo(new[] { 41, 42 }));
        Assert.That(
            controllers.RootElement[0].GetProperty("states").EnumerateArray().Select(static x => x.GetProperty("name").GetString()),
            Is.EqualTo(new[] { "Idle", "Walking_A" }));
        string[] clipRefs = clips.RootElement.EnumerateArray()
            .SelectMany(static x => x.GetProperty("locators").EnumerateArray())
            .Select(static x => x.GetProperty("assetRef").GetString() ?? string.Empty)
            .ToArray();
        Assert.That(clipRefs, Has.Length.EqualTo(2));
        Assert.That(clipRefs, Has.All.Contains("MassNavigationMod:assets/Models/mass_navigation_agent_soldier.glb#anim:"));
        Assert.That(clipRefs, Has.None.Contains("#anim:0"));
        string[] showcaseCatalogPaths = showcaseCatalog.RootElement.EnumerateArray()
            .Select(static x => x.GetProperty("Path").GetString() ?? string.Empty)
            .ToArray();
        Assert.That(showcaseCatalogPaths, Has.None.EqualTo("Presentation/animation_clips.json"));
        Assert.That(showcaseCatalogPaths, Has.None.EqualTo("Presentation/animation_profiles.json"));
        JsonElement commandMapping = inputMappings.RootElement.GetProperty("mappings").EnumerateArray()
            .Single(static x => x.GetProperty("actionId").GetString() == "Command");
        Assert.That(commandMapping.GetProperty("orderTypeKey").GetString(), Is.EqualTo("massNavigationMove"));
        Assert.That(commandMapping.GetProperty("actorCollectionKey").GetString(), Is.EqualTo("collection.command.source"));
        Assert.That(commandMapping.GetProperty("requireTarget").GetBoolean(), Is.True);
        Assert.That(inputMappings.RootElement.GetProperty("groupMoveTargetLayout").GetProperty("orderTypeKeys")[0].GetString(), Is.EqualTo("massNavigationMove"));

        string[] navFiles = Directory.GetFiles(Path.Combine(showcaseRoot, "assets", "Data", "Nav", "nav_bake_island"), "*", SearchOption.AllDirectories);
        Assert.That(navFiles.Length, Is.GreaterThanOrEqualTo(800));
    }

    [Test]
    public void LauncherAndRegistryPointAtTheShowcaseEntry()
    {
        string root = FindRepoRoot();
        using JsonDocument presets = ReadJson(Path.Combine(root, "launcher.presets.json"));
        using JsonDocument registry = ReadJson(Path.Combine(root, "showcase.registry.json"));
        Assert.That(
            presets.RootElement.GetProperty("presets").EnumerateArray().Any(static x => x.GetProperty("id").GetString() == "nav_bake_island_showcase_raylib"),
            Is.True);
        Assert.That(
            registry.RootElement.GetProperty("showcases").EnumerateArray().Any(static x => x.GetProperty("id").GetString() == "nav_bake_island_showcase"),
            Is.True);
    }

    private static JsonDocument ReadJson(string path)
    {
        Assert.That(File.Exists(path), Is.True, path);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) && File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repository root.");
    }
}
