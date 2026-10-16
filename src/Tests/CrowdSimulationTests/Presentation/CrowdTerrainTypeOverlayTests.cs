using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Presentation;

public sealed class CrowdTerrainTypeOverlayTests
{
    private const int PlaceXCm = 503125;
    private const int PlaceYCm = 565625;
    private const int PlaceSizeCm = 14000;

    [Test]
    public void PixelsFollowColorTable_BlockedCellsUseConfiguredColor_OnlyChangedTilesRewrite()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        var table = LoadColors();
        CrowdTerrainTypeOverlaySettings settings = LoadOverlaySettings();
        int blockedR = settings.Blocked.Red;
        int blockedG = settings.Blocked.Green;
        int blockedB = settings.Blocked.Blue;
        int blockedA = settings.Blocked.Alpha;
        var overlay = CrowdTerrainTypeOverlay.Create(session, table, blockedR, blockedG, blockedB, blockedA);
        var surface = session.Structures!.SnapshotSurface();
        var ids = session.Config.TerrainTypeIds;
        int side = overlay.Width;
        Assert.That(overlay.Height, Is.EqualTo(side));
        Assert.That(overlay.Enabled, Is.False);

        int blockedCells = 0;
        var seenTypes = new bool[ids.Count];
        for (int cell = 0; cell < side * side; cell++)
        {
            byte terrain = surface.TerrainType[cell];
            byte blocked = surface.Blocked[cell];
            Assert.That(terrain, Is.LessThan(ids.Count));
            seenTypes[terrain] = true;
            if (blocked != 0) blockedCells++;
            var color = table.Colors[ids[terrain]];
            byte r = blocked != 0 ? (byte)blockedR : (byte)color.Red;
            byte g = blocked != 0 ? (byte)blockedG : (byte)color.Green;
            byte b = blocked != 0 ? (byte)blockedB : (byte)color.Blue;
            byte a = blocked != 0 ? (byte)blockedA : (byte)color.Alpha;
            AssertPixel(overlay.Rgba, cell, r, g, b, a);
        }

        Assert.That(blockedCells, Is.GreaterThan(0), "s1337 应有静态阻挡格");
        int typeCount = 0;
        for (int i = 0; i < seenTypes.Length; i++)
        {
            if (seenTypes[i]) typeCount++;
        }

        Assert.That(typeCount, Is.GreaterThan(1));

        overlay.SetEnabled(true);
        MeasureSync(overlay, 8);
        Assert.That(MeasureSync(overlay, 32), Is.EqualTo(0), "没有重烘时同步应零分配");

        for (int i = 0; i < 3; i++)
        {
            CrowdStructureOps.PlaceStructure(
                session, "building", Fix64.FromInt(PlaceXCm), Fix64.FromInt(PlaceYCm), Fix64.FromInt(PlaceSizeCm), Fix64.Zero, Fix64.Zero);
            MeasureSync(overlay, 1);
            CrowdStructureOps.RemoveStructureAt(session, Fix64.FromInt(PlaceXCm), Fix64.FromInt(PlaceYCm));
            MeasureSync(overlay, 1);
        }

        overlay.SetEnabled(false);
        overlay.SetEnabled(true);
        var version = new Dictionary<int, int>();
        foreach (var pair in session.Navs) version[pair.Key] = pair.Value.Version;

        Array.Fill(overlay.Rgba, (byte)0x11);
        CrowdStructureOps.PlaceStructure(
            session, "building", Fix64.FromInt(PlaceXCm), Fix64.FromInt(PlaceYCm), Fix64.FromInt(PlaceSizeCm), Fix64.Zero, Fix64.Zero);

        int cellsPerTile = session.Config.Hpa.NavCellsPerTile;
        int tilesPerSide = side / cellsPerTile;
        var changed = new bool[tilesPerSide * tilesPerSide];
        int changedCount = 0;
        foreach (var pair in session.Navs)
        {
            var nav = pair.Value;
            int before = version[pair.Key];
            if (nav.Version == before) continue;
            Assert.That(nav.Version, Is.EqualTo(before + 1), $"nav {pair.Key} 一次放置应只重烘一次");
            List<int> tiles = nav.ChangedTiles ?? throw new InvalidOperationException($"nav {pair.Key} 重烘后没有 ChangedTiles。");
            for (int i = 0; i < tiles.Count; i++)
            {
                int tile = tiles[i];
                Assert.That(tile, Is.GreaterThanOrEqualTo(0).And.LessThan(changed.Length));
                if (changed[tile]) continue;
                changed[tile] = true;
                changedCount++;
            }
        }

        Assert.That(changedCount, Is.GreaterThan(0));
        Assert.That(changedCount, Is.LessThan(changed.Length));

        Assert.That(MeasureSync(overlay, 1), Is.EqualTo(0), "按 ChangedTiles 重写应零分配");

        for (int tile = 0; tile < changed.Length; tile++)
        {
            int tx = tile % tilesPerSide;
            int ty = tile / tilesPerSide;
            int x0 = tx * cellsPerTile;
            int y0 = ty * cellsPerTile;
            for (int y = y0; y < y0 + cellsPerTile; y++)
            {
                for (int x = x0; x < x0 + cellsPerTile; x++)
                {
                    int cell = y * side + x;
                    if (!changed[tile])
                    {
                        AssertPixel(overlay.Rgba, cell, 0x11, 0x11, 0x11, 0x11);
                        continue;
                    }

                    byte terrain = surface.TerrainType[cell];
                    byte blocked = surface.Blocked[cell];
                    var color = table.Colors[ids[terrain]];
                    byte r = blocked != 0 ? (byte)blockedR : (byte)color.Red;
                    byte g = blocked != 0 ? (byte)blockedG : (byte)color.Green;
                    byte b = blocked != 0 ? (byte)blockedB : (byte)color.Blue;
                    byte a = blocked != 0 ? (byte)blockedA : (byte)color.Alpha;
                    AssertPixel(overlay.Rgba, cell, r, g, b, a);
                }
            }
        }
    }

    private static long MeasureSync(CrowdTerrainTypeOverlay overlay, int times)
    {
        long total = 0;
        for (int i = 0; i < times; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            overlay.Sync();
            total += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return total;
    }

    private static void AssertPixel(byte[] rgba, int cell, byte r, byte g, byte b, byte a)
    {
        int pixel = cell * 4;
        if (rgba[pixel] == r && rgba[pixel + 1] == g && rgba[pixel + 2] == b && rgba[pixel + 3] == a) return;
        Assert.Fail($"格 {cell} 像素 {rgba[pixel]},{rgba[pixel + 1]},{rgba[pixel + 2]},{rgba[pixel + 3]}，期望 {r},{g},{b},{a}");
    }

    [Test]
    public void ShippedSettings_DefaultOff_ActionAndBlockedColor()
    {
        CrowdTerrainTypeOverlaySettings settings = LoadOverlaySettings();
        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.Action, Is.EqualTo("CrowdSimulation.ToggleTerrainTypes"));
        Assert.That(settings.Blocked.Red, Is.EqualTo(211));
        Assert.That(settings.Blocked.Green, Is.EqualTo(47));
        Assert.That(settings.Blocked.Blue, Is.EqualTo(47));
        Assert.That(settings.Blocked.Alpha, Is.EqualTo(230));

        string root = FindRepoRoot();
        var catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root, "mods", "capabilities", "navigation", "CrowdSimulationMod", "assets", "config_catalog.json")))!.AsArray();
        JsonNode? entry = null;
        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog[i]?["Path"]?.GetValue<string>() == CrowdTerrainTypeOverlaySettings.RelativePath)
            {
                entry = catalog[i];
            }
        }

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!["Policy"]!.GetValue<string>(), Is.EqualTo("DeepObject"));

        string input = File.ReadAllText(Path.Combine(
            root, "mods", "capabilities", "navigation", "CrowdSimulationMod", "assets", "Input", "default_input.json"));
        Assert.That(input, Does.Contain("\"id\": \"CrowdSimulation.ToggleTerrainTypes\""));
        Assert.That(input, Does.Contain("\"path\": \"<Keyboard>/leftBracket\""));

        string s4Input = File.ReadAllText(Path.Combine(
            root, "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod", "assets", "Input", "default_input.json"));
        Assert.That(s4Input, Does.Not.Contain("ToggleTerrainTypes"));
        Assert.That(s4Input, Does.Not.Contain("leftBracket"));

        string s1 = Path.Combine(root, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod", "assets");
        Assert.That(File.Exists(Path.Combine(s1, "CrowdSimulationDebugView.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(s1, "config_catalog.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(s1, "Input", "default_input.json")), Is.False);
    }

    [Test]
    public void MissingAction_NamesTheFile()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdTerrainTypeOverlaySettings.Load(Parse("""
            { "enabled": false, "blocked": { "red": 1, "green": 2, "blue": 3, "alpha": 4 } }
            """)));
        Assert.That(ex!.Message, Does.Contain(CrowdTerrainTypeOverlaySettings.RelativePath));
        Assert.That(ex.Message, Does.Contain("action").IgnoreCase);
    }

    [Test]
    public void UnknownKey_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdTerrainTypeOverlaySettings.Load(Parse("""
            {
              "enabled": false,
              "action": "CrowdSimulation.ToggleTerrainTypes",
              "blocked": { "red": 1, "green": 2, "blue": 3, "alpha": 4 },
              "palette": true
            }
            """)));
        Assert.That(ex!.Message, Does.Contain("palette").IgnoreCase);
    }

    [Test]
    public void MissingCatalogEntry_NamesTheFile()
    {
        var pipeline = new ConfigPipeline(new VirtualFileSystem(), null!);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdTerrainTypeOverlaySettings.LoadMerged(pipeline, new ConfigCatalog(), new ConfigConflictReport()));
        Assert.That(ex!.Message, Does.Contain(CrowdTerrainTypeOverlaySettings.RelativePath));
    }

    private static CrowdTerrainTypeOverlaySettings LoadOverlaySettings()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods", "capabilities", "navigation", "CrowdSimulationMod",
            "assets", "Presentation", "crowd_terrain_type_overlay.json");
        return CrowdTerrainTypeOverlaySettings.Load(JsonNode.Parse(File.ReadAllText(path))!.AsObject());
    }

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static CrowdTerrainTypeColorTable LoadColors()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods", "capabilities", "navigation", "CrowdSimulationStandardMod",
            "assets", "Presentation", "terrain_type_colors.json");
        return CrowdTerrainTypeColors.Load(JsonNode.Parse(File.ReadAllText(path))!.AsObject());
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
