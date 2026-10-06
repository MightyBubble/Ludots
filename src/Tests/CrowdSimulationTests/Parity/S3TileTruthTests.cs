using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S3-a 逐 tile 对拍:tile 缓存中的每个条目(分水岭 + 轮廓 + 三角化 + 凸合并的完整几何)
/// 与 Web 导出逐字段一致。条目按内容键排序(跨端不依赖获取序),uid 不参与。
/// </summary>
[TestFixture]
public class S3TileTruthTests
{
    public static readonly string[] Seeds = { "s1337", "s2024", "s7" };

    [Test]
    public void TileCache_MatchesWebTruth([ValueSource(nameof(Seeds))] string seed)
    {
        var runtime = S1SurfaceTruthTests.LoadRuntime(seed);
        var surface = S1SurfaceTruthTests.ReadSurface(seed);
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface(seed);
        var grid = SurfaceGrid.Build(runtime, surface, mapSurface.Blockers);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtime);
        var heightAsset = Ludots.Core.Presentation.Terrain.ContinuousHeightmapBinary.Read(File.OpenRead(
            Path.Combine(S1Dir(seed), "terrain", $"crowd_simulation_{seed}.height")));
        var heights = NavHeightField.FromHeightmap(heightAsset, runtime.NavCellCount, runtime.NavCellSizeCm);

        int n = runtime.NavCellCount, t = runtime.Hpa.ClusterSize, c = n / t;
        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity,
            t,
            runtime.Navmesh.MinRegionArea.ToDouble(),
            runtime.Navmesh.MaxSimplificationError.ToDouble(),
            runtime.Navmesh.MaxEdgeLen.ToDouble(),
            runtime.Navmesh.MaxVertsPerPoly);

        // 与导出端同一获取序:逐(移动类型 × 净空)上下文,缓存随烘焙填充(同 buildNavContext)
        var seen = new HashSet<int>();
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            foreach (int clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtime, grid, heights, deck, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;
            }
        }

        var (hash, entryCount, perEntry) = ComputeDigest(cache, n);
        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(S1Dir(seed), "CrowdSimulation", "parity", "s3-tile-truth.json")))!;
        Assert.That(entryCount, Is.EqualTo(truth["tiles"]!.GetValue<int>()), "不同 tile 数");
        Assert.That(cache.Bakes, Is.EqualTo(truth["bakes"]!.GetValue<int>()), "烘焙次数");
        // 按键指纹对齐比较(内容键相同才是同一块 tile;位置序不作准)
        var truthByKey = truth["entryHashes"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>());
        int matched = 0;
        foreach (var (keyFp, entryHash, entry, keyChars) in perEntry)
        {
            if (!truthByKey.TryGetValue(keyFp, out var truthHash))
            {
                string dumpDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s3-diff", seed);
                Directory.CreateDirectory(dumpDir);
                File.WriteAllText(Path.Combine(dumpDir, $"key-{keyFp}.json"), System.Text.Json.JsonSerializer.Serialize(
                    keyChars.Select(c => (int)c).ToArray()));
                Assert.Fail($"S3 tile 内容键 {keyFp} 在 Web 真相中不存在(seed {seed})——该 tile 的输入内容两端不同;键内容已转储 {dumpDir}/key-{keyFp}.json");
            }

            if (entryHash != truthHash)
            {
                DumpEntry(entry, seed, keyFp);
                Assert.Fail($"S3 tile 条目(键 {keyFp})与 Web 不一致(seed {seed}): {entryHash} ≠ {truthHash}");
            }

            matched++;
        }

        if (matched != truthByKey.Count)
        {
            Assert.Fail($"S3 tile 键集合不一致(seed {seed}): C# 有 {perEntry.Count} 条,Web 有 {truthByKey.Count} 条,匹配 {matched} 条");
        }

        Assert.That(hash, Is.EqualTo(truth["fnv1a"]!.GetValue<string>()), $"S3 tile 烘焙与 Web 真相不一致(seed {seed})");
    }

    private static string S1Dir(string seed) => Path.Combine("assets", "s1", seed);

    private static void DumpEntry(NavTileEntry entry, string seed, string keyFp)
    {
        string dumpDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s3-diff", seed);
        Directory.CreateDirectory(dumpDir);
        File.WriteAllText(Path.Combine(dumpDir, $"entry-{keyFp}.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            entry.Count,
            entry.RegionCount,
            entry.Vx,
            entry.Vy,
            entry.PolyStart,
            entry.PolyVerts,
            entry.NeiStart,
            entry.Nei,
            entry.NeiA,
            entry.NeiB,
            entry.PolyOf,
            entry.BorderPoly,
            BorderSide = entry.BorderSide.Select(b => (int)b).ToArray(),
            entry.BorderLo,
            entry.BorderHi,
            BorderRev = entry.BorderRev.Select(b => (int)b).ToArray(),
        }));
    }

    private static (string Hash, int EntryCount, List<(string KeyFp, string Hash, NavTileEntry Entry, char[] KeyChars)> PerEntry) ComputeDigest(NavTileCache cache, int n)
    {
        var entries = cache.Entries.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("LS3T"));
        w.Write(n);
        w.Write(entries.Count);

        void WI32(BinaryWriter w2, int[] arr) { w2.Write(arr.Length); foreach (var v in arr) w2.Write(v); }
        void WF32(BinaryWriter w2, int[] arr) { w2.Write(arr.Length); foreach (var v in arr) w2.Write((float)v); }
        void WU8(BinaryWriter w2, byte[] arr) { w2.Write(arr.Length); foreach (var v in arr) w2.Write(v); }

        void WriteEntry(BinaryWriter w2, NavTileEntry e)
        {
            w2.Write(e.Count);
            w2.Write(e.RegionCount);
            WF32(w2, e.Vx); WF32(w2, e.Vy);
            WI32(w2, e.PolyStart); WI32(w2, e.PolyVerts);
            WI32(w2, e.NeiStart); WI32(w2, e.Nei); WI32(w2, e.NeiA); WI32(w2, e.NeiB);
            WI32(w2, e.BMinX); WI32(w2, e.BMinY); WI32(w2, e.BMaxX); WI32(w2, e.BMaxY);
            WI32(w2, e.PolyOf);
            WI32(w2, e.BorderPoly);
            WU8(w2, e.BorderSide);
            WI32(w2, e.BorderLo); WI32(w2, e.BorderHi);
            WU8(w2, e.BorderRev);
        }

        var perEntry = new List<(string, string, NavTileEntry, char[])>();
        foreach (var (key, e) in entries)
        {
            WriteEntry(w, e);
            using var one = new MemoryStream();
            using (var w1 = new BinaryWriter(one, Encoding.UTF8, leaveOpen: true)) WriteEntry(w1, e);
            // 键指纹:UTF-16 码元小端字节流(与导出端同)
            using var kb = new MemoryStream();
            foreach (char ch in key) { kb.WriteByte((byte)(ch & 0xff)); kb.WriteByte((byte)(ch >> 8)); }
            perEntry.Add((S2NavTruthTests.Fnv(kb.ToArray()), S2NavTruthTests.Fnv(one.ToArray()), e, key.ToCharArray()));
        }

        w.Write(cache.Bakes);
        w.Write(cache.Hits);
        w.Write(cache.Misses);
        w.Flush();
        return (S2NavTruthTests.Fnv(ms.ToArray()), entries.Count, perEntry);
    }
}
