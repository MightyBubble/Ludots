using System.Security.Cryptography;
using System.Text.Json;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Tool;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
[Category("acceptance")]
public sealed class EastAsiaNavMeshDebugContractTests
{
    private const string ModId = "EastAsiaNavMeshDebugMod";
    private const string MapId = "east_asia_visual_heightmap";

    [Test]
    public void Overlay_EnablesContinentalRecastNavigationWithoutReplacingVisualHeightmap()
    {
        string root = FindRepoRoot();
        var map = ToolMapConfigResolver.LoadMap(root, MapId, ModId);
        var board = ToolMapConfigResolver.ResolvePrimaryNavigationBoard(map);
        NavMeshBakeConfigContext nav = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(root, ModId);

        Assert.That(map.VisualHeightmap, Is.Not.Null);
        Assert.That(
            map.VisualHeightmap.Asset,
            Is.EqualTo("assets/samples/LudotsSample/east_asia/east_asia_continuous.vhtm"));
        Assert.That(map.Tags, Does.Contain("Feature.NavMesh:On"));
        Assert.That(board.WidthInMacroTiles, Is.EqualTo(7));
        Assert.That(board.HeightInMacroTiles, Is.EqualTo(4));
        Assert.That(board.GridCellSizeCm, Is.EqualTo(3571));
        Assert.That(board.ChunkSizeCells, Is.EqualTo(64));
        Assert.That(board.LoadedChunkCapacity, Is.EqualTo(512));
        Assert.That(board.TerrainHeightStepCm, Is.EqualTo(100));
        Assert.That(board.TerrainBlockedAtOrBelowHeightCm, Is.Zero);
        Assert.That(map.Metadata, Does.ContainKey("navWalkabilityOverlay"));
        Assert.That(nav.Config.ParsedAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Recast));
        Assert.That(nav.Config.ParsedMode, Is.EqualTo(NavBakeMode.Offline));
        Assert.That(nav.Config.Profiles.Select(profile => profile.Id), Is.EqualTo(new[] { "Small" }));
    }

    [Test]
    public void Overlay_ShipsCompleteTileGridAndHasBothLandAndBlockedOcean()
    {
        string modRoot = ResolveModRoot();
        string tileRoot = Path.Combine(
            modRoot,
            "assets",
            "Data",
            "Nav",
            MapId,
            "layer0",
            "profile_Small");
        string[] paths = Directory
            .EnumerateFiles(tileRoot, "*.ntil", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(paths, Has.Length.EqualTo(28 * 16));
        int emptyTileCount = 0;
        int walkableTriangleCount = 0;
        foreach (string path in paths)
        {
            using FileStream stream = File.OpenRead(path);
            NavTile tile = NavTileBinary.Read(stream);
            if (tile.TriangleCount == 0)
            {
                emptyTileCount++;
            }
            else
            {
                walkableTriangleCount += tile.TriangleCount;
            }
        }

        Assert.That(emptyTileCount, Is.GreaterThan(0));
        Assert.That(walkableTriangleCount, Is.GreaterThan(0));
    }

    [Test]
    public void WalkabilityTexture_SidecarMatchesPngAndWorldBounds()
    {
        string texturePath = Path.Combine(ResolveModRoot(), "assets", "Textures", "nav_walkability.png");
        byte[] png = File.ReadAllBytes(texturePath);
        using JsonDocument sidecar = JsonDocument.Parse(File.ReadAllText(texturePath + ".json"));
        JsonElement root = sidecar.RootElement;
        JsonElement bounds = root.GetProperty("boundsCm");
        string hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();

        Assert.That(root.GetProperty("width").GetInt32(), Is.EqualTo(4096));
        Assert.That(root.GetProperty("height").GetInt32(), Is.EqualTo(2341));
        Assert.That(bounds.GetProperty("minX").GetInt32(), Is.EqualTo(-3_199_616));
        Assert.That(bounds.GetProperty("minZ").GetInt32(), Is.EqualTo(-1_828_352));
        Assert.That(bounds.GetProperty("maxX").GetInt32(), Is.EqualTo(3_199_616));
        Assert.That(bounds.GetProperty("maxZ").GetInt32(), Is.EqualTo(1_828_352));
        Assert.That(root.GetProperty("sourceTileCount").GetInt32(), Is.EqualTo(448));
        Assert.That(root.GetProperty("triangleCount").GetInt32(), Is.GreaterThan(0));
        Assert.That(root.GetProperty("contentHash").GetString(), Is.EqualTo("sha256:" + hash));
        Assert.That(root.GetProperty("encoding").GetProperty("alpha").GetString(), Does.Contain("walkable"));
    }

    [Test]
    public void ShowcaseRegistration_ProvidesDataOnlyModBindingAndRaylibPreset()
    {
        string root = FindRepoRoot();
        string modRoot = ResolveModRoot();
        Assert.That(File.Exists(Path.Combine(modRoot, "mod.json")), Is.True);
        Assert.That(Directory.EnumerateFiles(modRoot, "*.csproj", SearchOption.TopDirectoryOnly), Is.Empty);

        using JsonDocument launcher = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "launcher.config.json")));
        using JsonDocument presets = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "launcher.presets.json")));
        using JsonDocument registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "showcase.registry.json")));

        Assert.That(
            launcher.RootElement.GetProperty("bindings").EnumerateArray().Any(
                entry => entry.GetProperty("name").GetString() == "east_asia_navmesh_debug"),
            Is.True);
        Assert.That(
            presets.RootElement.GetProperty("presets").EnumerateArray().Any(
                entry => entry.GetProperty("id").GetString() == "east_asia_navmesh_debug_raylib"),
            Is.True);
        Assert.That(
            registry.RootElement.GetProperty("showcases").EnumerateArray().Any(
                entry => entry.GetProperty("id").GetString() == "east_asia_navmesh_debug"),
            Is.True);
    }

    private static string ResolveModRoot()
        => ToolMapConfigResolver.ResolveModRoot(FindRepoRoot(), ModId);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "showcase.registry.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
