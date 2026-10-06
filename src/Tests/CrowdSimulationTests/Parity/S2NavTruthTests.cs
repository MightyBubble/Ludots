using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S2 逐上下文对拍:每种(移动类型 × 净空)的可走栅格、连通域划分、跳跃链接集
/// 与 Web 导出的真相摘要(FNV-1a,字节流契约见导出器 LS2T 段)一致。
/// 连通域按"划分"比较(规范化标签:按连通域最小格号排序编号),不比较标签编号。
/// </summary>
[TestFixture]
public class S2NavTruthTests
{
    public static readonly string[] Seeds = { "s1337", "s2024", "s7" };

    [Test]
    public void NavContexts_MatchWebTruth([ValueSource(nameof(Seeds))] string seed)
    {
        var runtime = S1SurfaceTruthTests.LoadRuntime(seed);
        var surface = S1SurfaceTruthTests.ReadSurface(seed);
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface(seed);
        var grid = SurfaceGrid.Build(runtime, surface, mapSurface.Blockers);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtime);
        var heightAsset = ContinuousHeightmapBinary.Read(File.OpenRead(
            Path.Combine(S1Dir(seed), "terrain", $"crowd_simulation_{seed}.height")));
        var heights = NavHeightField.FromHeightmap(heightAsset, runtime.NavCellCount, runtime.NavCellSizeCm);

        // 每个移动类型 × 其涉及的净空(默认全部 clearance 1,与参考实现按 navId 去重一致)
        var contexts = new List<NavContext>();
        var seen = new HashSet<int>();
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            var clearances = runtime.Profiles
                .Where(p => p.AgentTypeIndex == a)
                .Select(p => p.ClearanceCells)
                .Distinct();
            foreach (int c in clearances)
            {
                var nav = NavContextBaker.Bake(runtime, grid, heights, deck, a, c);
                if (seen.Add(nav.Id)) contexts.Add(nav);
            }
        }

        // 重建字节流并比对
        var (hash, perContext) = ComputeDigest(runtime.NavCellCount, contexts);
        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(S1Dir(seed), "CrowdSimulation", "parity", "s2-nav-truth.json")))!;
        var expected = truth["fnv1a"]!.GetValue<string>();
        var truthContexts = truth["contexts"]!.AsArray();

        var failures = new List<string>();
        if (contexts.Count != truthContexts.Count) failures.Add($"上下文数量 {contexts.Count} ≠ {truthContexts.Count}");
        for (int i = 0; i < contexts.Count && i < truthContexts.Count; i++)
        {
            var t = truthContexts[i]!;
            var c = contexts[i];
            if (c.Id != t["navId"]!.GetValue<int>()) failures.Add($"context[{i}] navId {c.Id} ≠ {t["navId"]!.GetValue<int>()}");
            if (c.Cells.Length != t["passableCount"]!.GetValue<int>()) failures.Add($"{t["agentType"]} 可走格数 {c.Cells.Length} ≠ {t["passableCount"]!.GetValue<int>()}");
            if (c.CompCount != t["compCount"]!.GetValue<int>()) failures.Add($"{t["agentType"]} 连通域数 {c.CompCount} ≠ {t["compCount"]!.GetValue<int>()}");
            if ((c.Links?.Count ?? 0) != t["linkCount"]!.GetValue<int>()) failures.Add($"{t["agentType"]} 链接数 {c.Links?.Count ?? 0} ≠ {t["linkCount"]!.GetValue<int>()}");
        }

        if (hash != expected) failures.Add($"fnv {hash} ≠ {expected}");

        if (failures.Count > 0)
        {
            // 分歧定位:把 C# 侧栅格与坡度写出来供 diff(调试辅助,不改变判定)
            string dumpDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s2-diff", seed);
            Directory.CreateDirectory(dumpDir);
            foreach (var nav in contexts)
            {
                File.WriteAllBytes(Path.Combine(dumpDir, $"{runtime.AgentTypes[nav.LayerIndex].Id}_c{nav.ClearanceCells}.pass"), nav.Passable);
            }

            using (var slopeStream = File.Create(Path.Combine(dumpDir, "slope.f32")))
            {
                foreach (var s in heights.Slope)
                {
                    slopeStream.Write(BitConverter.GetBytes((float)s.ToDouble()));
                }
            }

            Assert.Fail($"S2 导航烘焙与 Web 真相不一致(seed {seed}); 栅格已转储到 {dumpDir}: {string.Join("; ", failures)}");
        }
    }

    [Test]
    public void JumpLinkFilter_RespectsDownCm()
    {
        // S2 验收:把 leaper 的 jump.downCm 改小后,超过能力的下跳链接消失。
        var runtime = S1SurfaceTruthTests.LoadRuntime("s1337");
        var surface = S1SurfaceTruthTests.ReadSurface("s1337");
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface("s1337");
        var grid = SurfaceGrid.Build(runtime, surface, mapSurface.Blockers);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtime);
        var heightAsset = ContinuousHeightmapBinary.Read(File.OpenRead(
            Path.Combine(S1Dir("s1337"), "terrain", "crowd_simulation_s1337.height")));
        var heights = NavHeightField.FromHeightmap(heightAsset, runtime.NavCellCount, runtime.NavCellSizeCm);

        var baseline = NavContextBaker.Bake(runtime, grid, heights, deck, agentTypeIndex: 4, clearanceCells: 1);
        Assert.That(baseline.Links, Is.Not.Null);
        int baselineCount = baseline.Links!.Count;

        // 构造 downCm = 5000(50 m)的修改版运行时配置——走文件级修改再组装,不用内部钩子
        var merged = TestDefaults.DefaultConfigJson();
        var overlay = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(S1Dir("s1337"), "CrowdSimulationConfig.json")))!;
        ConfigPipeline.DeepMerge(merged, overlay);
        ((JsonObject)merged["navProfiles"]![4]!)["jump"]!["downCm"] = 5000;
        var config = CrowdSimulationConfig.Load(merged);
        var map = System.Text.Json.JsonSerializer.Deserialize<Ludots.Core.Config.MapConfig>(
            File.ReadAllText(Path.Combine(S1Dir("s1337"), "Maps", "crowd_simulation_s1337.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var restricted = CrowdSimulationConfigLoader.Load(config, map, TestDefaults.DemoProfiles(), TestDefaults.FixedHz);
        var narrowed = NavContextBaker.Bake(restricted, grid, heights, deck, agentTypeIndex: 4, clearanceCells: 1);

        Assert.That(narrowed.Links?.Count ?? 0, Is.LessThan(baselineCount), "downCm 收紧后链接必须减少");
        // 留下的链接全部满足新上限
        if (narrowed.Links != null)
        {
            var set = new HashSet<(int, int)>(
                narrowed.Links.From.Zip(narrowed.Links.To, (f, t) => (f, t)));
            int n = runtime.NavCellCount;
            foreach (var cand in surface.JumpCandidates)
            {
                if (cand.DropCm <= 5000) continue;
                int a = cand.FromY * n + cand.FromX, b = cand.ToY * n + cand.ToX;
                Assert.That(set.Contains((a, b)), Is.False, "超过 downCm 的候选不得成链");
            }
        }
    }

    private static string S1Dir(string seed) => Path.Combine("assets", "s1", seed);

    /// <summary>规范化连通域标签:按连通域最小格号排序编号(-1 保持)。</summary>
    internal static int[] CanonComp(int[] comp, int n2)
    {
        var minCell = new Dictionary<int, int>();
        for (int i = 0; i < n2; i++)
        {
            int c = comp[i];
            if (c < 0) continue;
            if (!minCell.TryGetValue(c, out int m) || i < m) minCell[c] = i;
        }

        var order = minCell.OrderBy(kv => kv.Value).Select((kv, idx) => (kv.Key, idx))
            .ToDictionary(x => x.Key, x => x.idx);
        var output = new int[n2];
        Array.Fill(output, -1);
        for (int i = 0; i < n2; i++)
        {
            if (comp[i] >= 0) output[i] = order[comp[i]];
        }

        return output;
    }

    private static (string hash, Dictionary<int, string?> perContext) ComputeDigest(int n, List<NavContext> contexts)
    {
        var perContext = new Dictionary<int, string?>();
        using var all = new MemoryStream();
        using (var w = new BinaryWriter(all, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes("LS2T"));
            w.Write(n);
            w.Write(contexts.Count);
            int n2 = n * n;
            foreach (var nav in contexts)
            {
                w.Write(nav.Id);
                w.Write(nav.Passable);
                var cc = CanonComp(nav.Comp, n2);
                foreach (var c in cc) w.Write(c);
                var links = nav.Links;
                w.Write(links?.Count ?? 0);
                if (links != null)
                {
                    for (int e = 0; e < links.Count; e++)
                    {
                        w.Write(links.From[e]);
                        w.Write(links.To[e]);
                        w.Write(links.TwoWay[e]);
                        w.Write(links.LengthCells[e]);
                    }
                }
            }
        }

        return (Fnv(all.ToArray()), perContext);
    }

    internal static string Fnv(byte[] bytes)
    {
        uint h = 2166136261u;
        foreach (byte b in bytes)
        {
            h ^= b;
            h = unchecked(h * 16777619u);
        }

        return h.ToString("x8");
    }
}
