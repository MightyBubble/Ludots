using NUnit.Framework;
using Ludots.Launcher.Backend;

namespace Ludots.Tests.EditorBridge;

[TestFixture]
public sealed class EditorBridgeEastAsiaMapContractTests
{
    [TestCase("EastAsiaGridEntryMod", "east_asia_grid", "east_asia_grid_map_data.bin")]
    [TestCase("EastAsiaHexEntryMod", "east_asia_hex", "east_asia_hex.vtxm")]
    [TestCase("EastAsiaVisualHeightmapEntryMod", "east_asia_visual_heightmap", null)]
    public void EastAsiaEntryMaps_KeepSharedPlayableTerrainAssetsAsEditorSource(
        string entryModId,
        string mapId,
        string? dataFile)
    {
        string repoRoot = FindRepoRoot();
        string sharedAssetRoot = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "east_asia_playable_terrain",
            "EastAsiaPlayableTerrainMod",
            "assets");
        string expectedMapPath = Path.Combine(sharedAssetRoot, "Maps", $"{mapId}.json");

        var ctx = global::EditorRepo.CreateContext(repoRoot, entryModId);
        var directSources = global::EditorRepo.FindDirectMapConfigSources(ctx, mapId);
        var loaded = global::EditorRepo.LoadMergedMapConfig(ctx, mapId);

        Assert.That(loaded.Found, Is.True, mapId);
        Assert.That(directSources, Is.EqualTo(new[] { expectedMapPath }), mapId);
        Assert.That(global::EditorRepo.ResolveWritableMapConfigPath(ctx, mapId), Is.EqualTo(expectedMapPath), mapId);
        Assert.That(loaded.Map.VisualHeightmapAsset, Is.EqualTo("assets/terrain/east_asia_continuous.vhtm"), mapId);
        Assert.That(loaded.Map.Metadata.Keys, Does.Contain("terrainProfile"), mapId);
        Assert.That(loaded.Map.DefaultCamera?.VirtualCameraId, Is.EqualTo("EastAsia.Camera.PlayableTerrain"), mapId);

        if (dataFile == null)
        {
            Assert.That(loaded.Map.Boards, Is.Empty, mapId);
            return;
        }

        string expectedDataPath = Path.Combine(sharedAssetRoot, "Data", "Maps", dataFile);
        Assert.That(global::EditorRepo.TryResolveDataFile(ctx, dataFile, out string resolvedDataPath, out _), Is.True, dataFile);
        Assert.That(resolvedDataPath, Is.EqualTo(expectedDataPath), dataFile);
        Assert.That(global::EditorRepo.ResolveWritableDataFilePath(ctx, dataFile), Is.EqualTo(expectedDataPath), dataFile);
    }

    [TestCase("east_asia_grid_raylib", "EastAsiaGridEntryMod")]
    [TestCase("east_asia_hex_raylib", "EastAsiaHexEntryMod")]
    [TestCase("east_asia_visual_heightmap_raylib", "EastAsiaVisualHeightmapEntryMod")]
    public void EastAsiaRaylibPresets_RequestCefHostRuntime(string presetId, string expectedRootModId)
    {
        string repoRoot = FindRepoRoot();
        var service = new LauncherService(repoRoot);
        var result = service.Resolve(
            new[] { $"preset:{presetId}" },
            "raylib",
            LauncherBuildMode.Never);

        Assert.That(result.Plan.RootModIds, Is.EqualTo(new[] { expectedRootModId }));
        Assert.That(result.Plan.BrowserRuntime, Is.Not.Null, presetId);
        Assert.That(result.Plan.BrowserRuntime!.Provider, Is.EqualTo("cef"), presetId);
        Assert.That(result.Plan.BrowserRuntime.Required, Is.True, presetId);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
