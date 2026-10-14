using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Presentation;

public sealed class CrowdTerrainTypeOverlayTests
{
    private const int BlockedR = 211;
    private const int BlockedG = 47;
    private const int BlockedB = 47;
    private const int BlockedA = 230;
    private const int PlaceXCm = 503125;
    private const int PlaceYCm = 565625;
    private const int PlaceSizeCm = 14000;

    [Test]
    public void PixelsFollowColorTable_BlockedCellsUseConfiguredColor_OnlyChangedTilesRewrite()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        var table = LoadColors();
        var overlay = CrowdTerrainTypeOverlay.Create(session, table, BlockedR, BlockedG, BlockedB, BlockedA);
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
            byte r = blocked != 0 ? (byte)BlockedR : (byte)color.Red;
            byte g = blocked != 0 ? (byte)BlockedG : (byte)color.Green;
            byte b = blocked != 0 ? (byte)BlockedB : (byte)color.Blue;
            byte a = blocked != 0 ? (byte)BlockedA : (byte)color.Alpha;
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
                    byte r = blocked != 0 ? (byte)BlockedR : (byte)color.Red;
                    byte g = blocked != 0 ? (byte)BlockedG : (byte)color.Green;
                    byte b = blocked != 0 ? (byte)BlockedB : (byte)color.Blue;
                    byte a = blocked != 0 ? (byte)BlockedA : (byte)color.Alpha;
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
