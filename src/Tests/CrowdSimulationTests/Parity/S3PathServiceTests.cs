using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 固定生效帧路径服务验收(S3 怎样算完成 2/3/5):
/// 后台线程 1 / 2 / 4 结果逐字节相同(RT-03:每 worker 各自持有搜索缓冲);
/// 生效帧恒为请求帧 + latencyTicks,没算完就等待(仿真不走),绝不使用半成品;
/// 单次规划超过 planTimeoutMs 即故障报错,不悄悄换算法。
/// </summary>
[TestFixture]
public class S3PathServiceTests
{
    private const string Seed = "s1337";

    private static CrowdSimulationRuntimeConfig _runtime = null!;
    private static Dictionary<int, NavContext> _navs = null!;
    private static (int Start, int Goal)[] _pairs = null!;

    [OneTimeSetUp]
    public void Load()
    {
        _runtime = S1SurfaceTruthTests.LoadRuntime(Seed);
        var surface = S1SurfaceTruthTests.ReadSurface(Seed);
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface(Seed);
        var grid = SurfaceGrid.Build(_runtime, surface, mapSurface.Blockers);
        var heightAsset = ContinuousHeightmapBinary.Read(File.OpenRead(
            Path.Combine("assets", "s1", Seed, "terrain", $"crowd_simulation_{Seed}.height")));
        var heights = NavHeightField.FromHeightmap(heightAsset, _runtime.NavCellCount, _runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, _runtime);
        var cache = new NavTileCache(
            _runtime.NavtileCacheCapacity, _runtime.Hpa.ClusterSize,
            _runtime.Navmesh.MinRegionArea.ToDouble(), _runtime.Navmesh.MaxSimplificationError.ToDouble(),
            _runtime.Navmesh.MaxEdgeLen.ToDouble(), _runtime.Navmesh.MaxVertsPerPoly);

        _navs = new Dictionary<int, NavContext>();
        var seen = new HashSet<int>();
        for (int a = 0; a < _runtime.AgentTypes.Count; a++)
        {
            foreach (var clearance in _runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
            {
                var nav = NavContextBaker.Bake(_runtime, grid, heights, deck, a, clearance, cache);
                if (seen.Add(nav.Id)) _navs[nav.Id] = nav;
            }
        }

        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "s1", Seed, "CrowdSimulation", "parity", "s3c-query-truth.json")))!;
        _pairs = truth["pairs"]!.AsArray().Select(p => (p![0]!.GetValue<int>(), p[1]!.GetValue<int>())).ToArray();
    }

    [Test]
    public void Replies_ByteIdentical_AcrossWorkerCounts()
    {
        var runs = new byte[3][];
        int runIndex = -1;
        foreach (int workers in new[] { 1, 2, 4 })
        {
            runIndex++;
            using var service = new PathQueryService(_navs, _runtime, workers, TimeSpan.FromSeconds(10));
            var results = new Dictionary<(int NavId, int Pair), PathResult>();
            int tick = 100;
            foreach (var (navId, nav) in _navs)
            {
                int pairIndex = -1;
                foreach (var (start, goal) in _pairs)
                {
                    pairIndex++;
                    int id = service.Request(new PathQuery(navId, start, goal, 0), tick);
                    Assert.That(service.DueTick(id), Is.EqualTo(tick + _runtime.Planning.LatencyTicks), "生效帧 = 请求帧 + latencyTicks");
                    results[(navId, Array.FindIndex(_pairs, p => p.Start == start && p.Goal == goal))] = service.AwaitDue(id);
                }
            }

            runs[runIndex] = new byte[8];
            ulong runHash = HashResults(results, _pairs.Length);
            for (int b = 0; b < 8; b++) runs[runIndex][b] = (byte)(runHash >> (b * 8));
            foreach (var r in results.Values)
            {
                if (r.Flow != null) service.Recycle(r.Flow);
            }
        }

        Assert.That(BitConverter.ToUInt64(runs[1]), Is.EqualTo(BitConverter.ToUInt64(runs[0])), "2 线程 ≠ 1 线程");
        Assert.That(BitConverter.ToUInt64(runs[2]), Is.EqualTo(BitConverter.ToUInt64(runs[0])), "4 线程 ≠ 1 线程");
    }

    [Test]
    public void LateReply_SimWaits_EffectTickUnchanged()
    {
        using var service = new PathQueryService(_navs, _runtime, 1, TimeSpan.FromSeconds(10))
        {
            TestDelayPerJob = TimeSpan.FromMilliseconds(250),
        };
        var (start, goal) = _pairs[0];
        int navId = _navs.Keys.First();
        int id = service.Request(new PathQuery(navId, start, goal, 0), 42);
        Assert.That(service.DueTick(id), Is.EqualTo(42 + _runtime.Planning.LatencyTicks));
        // 仿真在生效帧取答复:后台慢了 250ms,仿真原地等待,答复内容不变
        var result = service.AwaitDue(id);
        Assert.That(service.Waits, Is.EqualTo(1), "等待次数按请求计一次");
        Assert.That(result.Query.StartCell, Is.EqualTo(start));
        if (result.Flow != null) service.Recycle(result.Flow);
    }

    [Test]
    public void Timeout_Faults_NeverFallsBack()
    {
        using var service = new PathQueryService(_navs, _runtime, 1, TimeSpan.FromMilliseconds(60))
        {
            TestDelayPerJob = TimeSpan.FromMilliseconds(800),
        };
        int navId = _navs.Keys.First();
        int id = service.Request(new PathQuery(navId, _pairs[0].Start, _pairs[0].Goal, 0), 0);
        var ex = Assert.Throws<PathServiceFaultException>(() => service.AwaitDue(id));
        Assert.That(ex!.Message, Does.Contain($"#{id}"));
        Assert.That(service.Faulted, Is.True);
    }

    [Test]
    public void Discard_SkipsComputeAndDropsReply()
    {
        using var service = new PathQueryService(_navs, _runtime, 1, TimeSpan.FromSeconds(10));
        int navId = _navs.Keys.First();
        int id1 = service.Request(new PathQuery(navId, _pairs[0].Start, _pairs[0].Goal, 0), 0);
        int id2 = service.Request(new PathQuery(navId, _pairs[1].Start, _pairs[1].Goal, 0), 0);
        service.Discard(id1);
        var result = service.AwaitDue(id2);
        Assert.That(result.Query.StartCell, Is.EqualTo(_pairs[1].Start));
        Assert.That(service.Faulted, Is.False);
        if (result.Flow != null) service.Recycle(result.Flow);
    }

    [Test]
    public void WorkerCount_CanSwitchAtRuntime()
    {
        using var service = new PathQueryService(_navs, _runtime, 1, TimeSpan.FromSeconds(10));
        service.SetWorkerCount(4);
        Assert.That(service.WorkerCount, Is.EqualTo(4));
        service.SetWorkerCount(2);
        int navId = _navs.Keys.First();
        int id = service.Request(new PathQuery(navId, _pairs[1].Start, _pairs[1].Goal, 0), 7);
        var result = service.AwaitDue(id);
        Assert.That(result.Query.NavContextId, Is.EqualTo(navId));
        if (result.Flow != null) service.Recycle(result.Flow);
    }

    // 答复全量滚动哈希:分支 / 可达 / 折线 / 层标 / 流场(目标 / 到达数 / 掩码 / integ / len / wp / lk)
    // 64 位 FNV-1a,边算边滚(全量序列化要占数百 MB,已把测试主机压崩过一次)
    private static ulong HashResults(Dictionary<(int NavId, int Pair), PathResult> results, int pairCount)
    {
        const ulong basis = 14695981039346656037UL, prime = 1099511628211UL;
        ulong h = basis;
        void Byte(byte v) => h = (h ^ v) * prime;
        void I32(int v) { for (int b = 0; b < 4; b++) Byte((byte)(v >> (b * 8))); }
        void I64(long v) { for (int b = 0; b < 8; b++) Byte((byte)(v >> (b * 8))); }
        void F64s(Ludots.Core.Mathematics.FixedPoint.Fix64[]? values)
        {
            if (values == null) { I32(-1); return; }
            I32(values.Length);
            foreach (var v in values) I64(v.RawValue);
        }

        foreach (var navId in _navs.Keys.OrderBy(v => v))
        {
            I32(navId);
            for (int p = 0; p < pairCount; p++)
            {
                var r = results[(navId, p)];
                Byte((byte)r.Branch);
                Byte((byte)(r.Reachable ? 1 : 0));
                F64s(r.Points);
                if (r.PointLayers == null) I32(-1);
                else { I32(r.PointLayers.Length); foreach (int v in r.PointLayers) I32(v); }
                if (r.Flow == null) { I32(-1); continue; }
                I32(r.Flow.Goal);
                I32(r.Flow.Reached);
                foreach (byte v in r.Flow.Mask!) Byte(v);
                foreach (var v in r.Flow.Integ) I64(v.RawValue);
                foreach (var v in r.Flow.Len) I64(v.RawValue);
                foreach (int v in r.Flow.Wp) I32(v);
                I32(r.Flow.Lk?.Length ?? -1);
                if (r.Flow.Lk != null) foreach (int v in r.Flow.Lk) I32(v);
            }
        }

        return h;
    }

}
