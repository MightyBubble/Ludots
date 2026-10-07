using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S3 浸泡验收(怎样算完成 4):固定地图 / 指令集,预热后连续运行 10 仿真分钟
/// (FixedHz 30 × 600 s = 18000 帧),每 6 帧一次两点路径请求,答复即取即还;
/// 记录存活流场 / 池 / 内存的分钟趋势——预热后池 allocated 必须平台化,
/// 托管内存不得有失去引用的持续增长(缓存 / 空闲池容量来自配置,不要求初次填充期零增长)。
/// </summary>
[TestFixture]
[Category("Soak")]
public class S3SoakTests
{
    private const string Seed = "s1337";

    [Test]
    public void TenSimMinutes_NoUnboundedGrowth()
    {
        var runtime = S1SurfaceTruthTests.LoadRuntime(Seed);
        var surface = S1SurfaceTruthTests.ReadSurface(Seed);
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface(Seed);
        var grid = SurfaceGrid.Build(runtime, surface, mapSurface.Blockers);
        var heightAsset = ContinuousHeightmapBinary.Read(File.OpenRead(
            Path.Combine("assets", "s1", Seed, "terrain", $"crowd_simulation_{Seed}.height")));
        var heights = NavHeightField.FromHeightmap(heightAsset, runtime.NavCellCount, runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtime);
        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, runtime.Hpa.ClusterSize,
            runtime.Navmesh.MinRegionArea.ToDouble(), runtime.Navmesh.MaxSimplificationError.ToDouble(),
            runtime.Navmesh.MaxEdgeLen.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        var navs = new Dictionary<int, NavContext>();
        var seen = new HashSet<int>();
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            foreach (var clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtime, grid, heights, deck, a, clearance, cache);
                if (seen.Add(nav.Id)) navs[nav.Id] = nav;
            }
        }

        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "s1", Seed, "CrowdSimulation", "parity", "s3c-query-truth.json")))!;
        var pairs = truth["pairs"]!.AsArray().Select(p => (Start: p![0]!.GetValue<int>(), Goal: p[1]!.GetValue<int>())).ToArray();
        var navIds = navs.Keys.OrderBy(v => v).ToArray();

        const int totalTicks = 18000; // 10 仿真分钟 @ 30 Hz
        const int ticksPerMinute = 1800;
        using var service = new PathQueryService(navs, runtime, workerThreads: 2, TimeSpan.FromSeconds(10));
        var memory = new long[11];
        var allocated = new long[11];
        int requests = 0, replies = 0;
        uint lcg = 0x2f6e2b1;
        uint Rng() => lcg = lcg * 1103515245 + 12345;

        for (int tick = 0; tick <= totalTicks; tick++)
        {
            if (tick % 6 == 0 && tick + runtime.Planning.LatencyTicks <= totalTicks)
            {
                var navId = navIds[Rng() % navIds.Length];
                var (start, goal) = pairs[Rng() % pairs.Length];
                int id = service.Request(new PathQuery(navId, start, goal, 0), tick);
                requests++;
                var result = service.AwaitDue(id); // 仿真在生效帧取答复
                replies++;
                if (result.Flow != null) service.Recycle(result.Flow);
            }

            if (tick % ticksPerMinute == 0)
            {
                int m = tick / ticksPerMinute;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                memory[m] = GC.GetTotalMemory(forceFullCollection: false);
                allocated[m] = service.GetPoolStats().Allocated;
            }
        }

        Assert.That(replies, Is.EqualTo(requests), "答复数 ≠ 请求数(有半成品或丢失)");
        Assert.That(service.Waits, Is.LessThanOrEqualTo(requests), "等待次数不应超过请求数");
        TestContext.Out.WriteLine(
            $"请求 {requests} 等待 {service.Waits};内存(MB) {string.Join(' ', memory.Select(v => (v / 1048576).ToString()))};" +
            $"池 allocated {string.Join(' ', allocated)} tileCache bakes={cache.Bakes} evictions={cache.Evictions}");

        // 预热期 = 前 5 分钟(池 / 缓存初次填充);后 5 分钟必须平台化
        for (int m = 6; m <= 10; m++)
        {
            Assert.That(allocated[m], Is.EqualTo(allocated[5]), $"第 {m} 分钟池仍有新分配(预热后应零增长)");
        }

        long warmupPeak = memory.Skip(1).Take(5).Max();
        for (int m = 6; m <= 10; m++)
        {
            Assert.That(memory[m], Is.LessThan(warmupPeak + 32 * 1048576L),
                $"第 {m} 分钟内存超预热峰值 32 MB 以上(疑似失去引用的增长)");
        }
    }
}
