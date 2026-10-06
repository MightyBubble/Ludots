using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.World;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S1 逐格对拍：从 Ludots 侧资产（.navsurface + Maps/<id>.json + 模板）重建与 Web 导出器
/// 同一字节流，FNV-1a 摘要必须相等。字节流契约见导出器 scripts/exportLudots.mjs 头部注释。
/// </summary>
[TestFixture]
public class S1SurfaceTruthTests
{
    public static readonly string[] Seeds = { "s1337", "s2024", "s7" };

    private static string SeedDir(string seed) => Path.Combine("assets", "s1", seed);

    [Test]
    public void NavSurface_Loads_EverySeed([ValueSource(nameof(Seeds))] string seed)
    {
        var runtime = LoadRuntime(seed);
        using var stream = File.OpenRead(Path.Combine(SeedDir(seed), "terrain", $"crowd_simulation_{seed}.navsurface"));
        var surface = NavSurfaceAsset.Read(stream);
        Assert.DoesNotThrow(() => NavSurfaceContract.Validate(surface, runtime));
        Assert.That(surface.CellsX, Is.EqualTo(256));
    }

    [Test]
    public void TruthDigest_MatchesWebExport([ValueSource(nameof(Seeds))] string seed)
    {
        var runtime = LoadRuntime(seed);
        var surface = NavSurfaceAsset.Read(File.OpenRead(Path.Combine(SeedDir(seed), "terrain", $"crowd_simulation_{seed}.navsurface")));
        var (blockers, bridges) = LoadMapEntities(seed);
        var grid = SurfaceGrid.Build(runtime, surface, blockers);

        var hash = ComputeDigest(surface, grid, blockers, bridges);
        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(SeedDir(seed), "CrowdSimulation", "parity", "s1-truth.json")))!;
        Assert.That(hash, Is.EqualTo(truth["fnv1a"]!.GetValue<string>()),
            $"S1 地表与 Web 导出逐格不一致（seed {seed}）");
        Assert.That(blockers.Count, Is.EqualTo(truth["blockers"]!.GetValue<int>()));
        Assert.That(bridges.Count, Is.EqualTo(truth["bridges"]!.GetValue<int>()));
    }

    [Test]
    public void Heightmap_Loads_ThroughLudotsReader()
    {
        var asset = Ludots.Core.Presentation.Terrain.ContinuousHeightmapBinary.Read(
            File.OpenRead(Path.Combine(SeedDir("s1337"), "terrain", "crowd_simulation_s1337.height")));
        Assert.That(asset.SampleColumns, Is.EqualTo(1024));
        Assert.That(asset.SampleRows, Is.EqualTo(1024));
        Assert.That(asset.Bounds.Width, Is.EqualTo(1600000));
        Assert.That(asset.UsesRawUInt16Samples, Is.True);
        // 最高约 1400 m + 台地：抽样一个已知点的高度必须在合理范围。
        float top = asset.SampleScale.Decode(asset.HeightSamplesRaw.Max());
        Assert.That(top, Is.GreaterThan(100000f));
    }

    [Test]
    public void BlockerRemoval_ChangesRaster()
    {
        var runtime = LoadRuntime("s1337");
        var surface = ReadSurface("s1337");
        var (blockers, _) = LoadMapEntities("s1337");
        var full = SurfaceGrid.Build(runtime, surface, blockers);

        // 小尺寸阻挡物按覆盖率阈值可能一格都不占；取第一个 140 m（半宽 7000 cm）的阻挡物,
        // 其中心格必定被覆盖（中心格覆盖 1.0,相邻正交格 0.62 ≥ 0.5）。
        var victim = blockers.First(b => b.HalfSizeCm == 7000);
        var kept = blockers.Where(b => !(b.XCm == victim.XCm && b.YCm == victim.YCm && b.HalfSizeCm == victim.HalfSizeCm)).ToArray();
        var reduced = SurfaceGrid.Build(runtime, surface, kept);

        int removed = 0;
        for (int i = 0; i < full.Blocked.Length; i++)
        {
            if (full.Blocked[i] != reduced.Blocked[i]) removed++;
        }

        Assert.That(removed, Is.GreaterThan(0), "删掉一个阻挡物实例后阻挡栅格必须变化");
        int centerCell = (victim.YCm / 6250) * 256 + victim.XCm / 6250;
        Assert.That(full.Blocked[centerCell], Is.EqualTo(1));
        Assert.That(reduced.Blocked[centerCell], Is.EqualTo(0), "被删阻挡物的中心格必须解除阻挡");
    }

    [Test]
    public void TemplateResize_PropagatesToRaster()
    {
        var runtime = LoadRuntime("s1337");
        var surface = ReadSurface("s1337");
        var (blockers, _) = LoadMapEntities("s1337");
        var grown = blockers.Select(b => new BlockerFootprint(b.XCm, b.YCm, b.HalfSizeCm + 6250)).ToArray();
        var baseline = SurfaceGrid.Build(runtime, surface, blockers);
        var resized = SurfaceGrid.Build(runtime, surface, grown);
        int delta = 0;
        for (int i = 0; i < baseline.Blocked.Length; i++)
        {
            if (baseline.Blocked[i] != resized.Blocked[i]) delta++;
        }

        Assert.That(delta, Is.GreaterThan(0), "模板尺寸变化必须传导到所有实例的栅格");
    }

    [Test]
    public void CoverageThreshold_IsRespected()
    {
        var runtime = TestDefaults.Assemble();
        var surface = EmptySurface(runtime);
        // 6250 cm 格：半宽 1562 cm（边长 3125 cm）居中于格心 → 覆盖 0.25 < 0.5 不阻挡
        var small = SurfaceGrid.Build(runtime, surface, new[] { new BlockerFootprint(3125, 3125, 1562) });
        Assert.That(small.Blocked[0], Is.EqualTo(0));
        // 半宽 3125 cm（边长 = 格边长）居中 → 覆盖 1.0 ≥ 0.5 阻挡
        var full = SurfaceGrid.Build(runtime, surface, new[] { new BlockerFootprint(3125, 3125, 3125) });
        Assert.That(full.Blocked[0], Is.EqualTo(1));
    }

    internal static NavSurfaceAsset ReadSurface(string seed)
        => NavSurfaceAsset.Read(File.OpenRead(Path.Combine(SeedDir(seed), "terrain", $"crowd_simulation_{seed}.navsurface")));

    internal static CrowdSimulationRuntimeConfig LoadRuntime(string seed)
    {
        // CrowdSimulationMod 全量默认 + 地图 Mod 覆盖（与 config_catalog 的 DeepObject 合并同路径）。
        var merged = TestDefaults.DefaultConfigJson();
        var overlay = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(SeedDir(seed), "CrowdSimulationConfig.json")))!;
        ConfigPipeline.DeepMerge(merged, overlay);
        var config = CrowdSimulationConfig.Load(merged);

        var mapJson = File.ReadAllText(Path.Combine(SeedDir(seed), "Maps", $"crowd_simulation_{seed}.json"));
        var map = JsonSerializer.Deserialize<MapConfig>(mapJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return CrowdSimulationConfigLoader.Load(config, map, TestDefaults.DemoProfiles(), TestDefaults.FixedHz);
    }

    internal static (List<BlockerFootprint> blockers, List<(int x0, int y0, int x1, int y1, int width)> bridges) LoadMapEntities(string seed)
    {
        var templates = JsonSerializer.Deserialize<List<EntityTemplate>>(
            File.ReadAllText(Path.Combine(SeedDir(seed), "Entities", "templates.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var templatesById = templates.Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .ToDictionary(t => t.Id, StringComparer.Ordinal);

        var map = JsonSerializer.Deserialize<MapConfig>(
            File.ReadAllText(Path.Combine(SeedDir(seed), "Maps", $"crowd_simulation_{seed}.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var surface = CrowdSimulationMapSurfaceSource.Extract(map, templatesById);

        var blockers = surface.Blockers.ToList();
        var bridges = surface.Bridges.Select(b => (b.X0Cm, b.Y0Cm, b.X1Cm, b.Y1Cm, b.WidthCm)).ToList();
        blockers.Sort((a, b) => a.XCm != b.XCm ? a.XCm - b.XCm : a.YCm != b.YCm ? a.YCm - b.YCm : a.HalfSizeCm - b.HalfSizeCm);
        bridges.Sort((a, b) => a.X0Cm != b.X0Cm ? a.X0Cm - b.X0Cm : a.Y0Cm != b.Y0Cm ? a.Y0Cm - b.Y0Cm : a.X1Cm != b.X1Cm ? a.X1Cm - b.X1Cm : a.Y1Cm - b.Y1Cm);
        return (blockers, bridges);
    }

    private static NavSurfaceAsset EmptySurface(CrowdSimulationRuntimeConfig runtime)
    {
        int n = runtime.NavCellCount;
        return new NavSurfaceAsset
        {
            CellsX = n,
            CellsY = n,
            CellSizeCm = runtime.NavCellSizeCm,
            TerrainTypeIds = runtime.TerrainTypeIds.ToArray(),
            TerrainCells = new byte[n * n], // 全部 water（下标 0）
            JumpCandidates = Array.Empty<NavSurfaceJumpCandidate>(),
        };
    }

    internal static string ComputeDigest(
        NavSurfaceAsset surface,
        SurfaceGrid grid,
        List<BlockerFootprint> blockers,
        List<(int x0, int y0, int x1, int y1, int width)> bridges)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("LS1T"));
        w.Write(surface.CellsX);
        w.Write(surface.CellSizeCm);
        w.Write((ushort)surface.TerrainTypeIds.Length);
        foreach (var id in surface.TerrainTypeIds)
        {
            var bytes = Encoding.UTF8.GetBytes(id);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        w.Write(surface.TerrainCells);
        w.Write(grid.Area);
        w.Write(grid.Blocked);
        w.Write(blockers.Count);
        foreach (var b in blockers)
        {
            w.Write(b.XCm);
            w.Write(b.YCm);
            w.Write(b.HalfSizeCm * 2);
        }

        w.Write(bridges.Count);
        foreach (var b in bridges)
        {
            w.Write(b.x0);
            w.Write(b.y0);
            w.Write(b.x1);
            w.Write(b.y1);
            w.Write(b.width);
        }

        w.Write(surface.JumpCandidates.Length);
        foreach (var j in surface.JumpCandidates)
        {
            w.Write(j.FromX);
            w.Write(j.FromY);
            w.Write(j.ToX);
            w.Write(j.ToY);
            w.Write(j.DropCm);
            w.Write(j.LengthCells);
        }

        w.Flush();
        uint h = 2166136261u;
        foreach (byte b in ms.GetBuffer().AsSpan(0, (int)ms.Length))
        {
            h ^= b;
            h = unchecked(h * 16777619u);
        }

        return h.ToString("x8");
    }
}
