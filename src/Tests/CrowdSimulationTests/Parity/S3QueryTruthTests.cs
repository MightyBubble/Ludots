using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S3-c 对拍(两套浮点体系下唯一诚实的口径):64 组固定起终点 × 每导航上下文——
/// 逐位(FNV):走廊分支(tile A*+漏斗 / HPA* / 不可达)、trace 是否到达;
/// 带宽:折线总长度(点列由浮点派生,平局时点数与顶点都可不同)、路径代价、起格积分 / 绷紧长度
/// (路径代价对齐即走廊最优性对齐);
/// 结构容差(s3c-detail.bin):HPA 走廊 / poly 链 / 到达集合是浮点派生量——等代价十字路口
/// 的择路会被最后位差翻转(两解同最优),逐格 integ / len 在两侧到达集合的交集上按带宽
/// 比较,独占格计数限容差;路点(s3c-wp.bin):决定性全等,平局双解按规则校验。
/// </summary>
[TestFixture]
public class S3QueryTruthTests
{
    public static readonly string[] Seeds = { "s1337", "s2024", "s7" };

    [Test]
    public void CorridorAndFlow_MatchWebTruth([ValueSource(nameof(Seeds))] string seed)
    {
        var runtime = S1SurfaceTruthTests.LoadRuntime(seed);
        var surface = S1SurfaceTruthTests.ReadSurface(seed);
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface(seed);
        var grid = SurfaceGrid.Build(runtime, surface, mapSurface.Blockers);
        var heightAsset = ContinuousHeightmapBinary.Read(File.OpenRead(
            Path.Combine(S1Dir(seed), "terrain", $"crowd_simulation_{seed}.height")));
        var heights = NavHeightField.FromHeightmap(heightAsset, runtime.NavCellCount, runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtime);

        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, runtime.Hpa.ClusterSize,
            runtime.Navmesh.MinRegionArea.ToDouble(), runtime.Navmesh.MaxSimplificationError.ToDouble(),
            runtime.Navmesh.MaxEdgeLen.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        var parityDir = Path.Combine(S1Dir(seed), "CrowdSimulation", "parity");
        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(parityDir, "s3c-query-truth.json")))!;
        var pairs = truth["pairs"]!.AsArray()
            .Select(p => (Start: p![0]!.GetValue<int>(), Goal: p[1]!.GetValue<int>())).ToArray();
        var truthContexts = truth["contexts"]!.AsArray();
        var floats = File.ReadAllBytes(Path.Combine(parityDir, "s3c-floats.bin"));
        var detail = File.ReadAllBytes(Path.Combine(parityDir, "s3c-detail.bin"));
        var wpBin = File.ReadAllBytes(Path.Combine(parityDir, "s3c-wp.bin"));

        int n = runtime.NavCellCount, t = runtime.Hpa.ClusterSize, c = n / t;
        var exact = new List<byte>();
        exact.AddRange(Encoding.ASCII.GetBytes("LS3C"));
        WriteI32(exact, n);
        exact.AddRange(new byte[4]); // 上下文数占位,循环后回填
        WriteI32(exact, pairs.Length);

        int floatCursor = 0;
        int detailCursor = 4 + 12; // LS3D 头 + N/ctx/pairs
        int wpCursor = 4 + 12;
        // 代价类(integ / cost):紧带宽;几何类(len / plen):等代价链择路不同几何就不同,松带宽
        double maxTightAbs = 0, maxTightRel = 0, maxGeoAbs = 0, maxGeoRel = 0;
        long wpDecisive = 0, wpTie = 0, exclusiveMine = 0, exclusiveWeb = 0, sharedCells = 0;
        var seen = new HashSet<int>();
        int ctxIndex = 0;
        var pool = new FlowPool(n, runtime.Flowfield.PoolCapacity);
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            foreach (var clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtime, grid, heights, deck, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;

                WriteI32(exact, nav.Id);
                Assert.That(ReadI32(detail, ref detailCursor), Is.EqualTo(nav.Id), $"{seed} detail 流 navId 错位");
                Assert.That(ReadI32(wpBin, ref wpCursor), Is.EqualTo(nav.Id), $"{seed} wp 流 navId 错位");
                var scratch = new CorridorQuery.Scratch(t * t);
                int reachable = 0;
                int pairIndex = -1;
                foreach (var (start, goal) in pairs)
                {
                    pairIndex++;
                    var mask = new byte[c * c];
                    mask[NavGridSteps.ClusterOf(goal, n, t, c)] = 1;
                    var corridor = CorridorQuery.CorridorTo(nav, runtime, start, goal, mask, 0, scratch);
                    var flow = FlowFieldBuilder.Build(nav, goal, FlowFieldBuilder.PadMask(mask, c, runtime.Flowfield.CorridorPadding), pool);
                    var trace = FlowFieldBuilder.TracePath(flow, start, n * n);
                    int traceOk = trace[trace.Count - 1] == goal ? 1 : 0;
                    var myCells = pool.Order.Take(flow.Reached).OrderBy(v => v).ToArray();
                    var myInteg = new Dictionary<int, Fix64>(flow.Reached);
                    var myLen = new Dictionary<int, Fix64>(flow.Reached);
                    foreach (int cell in myCells) { myInteg[cell] = flow.Integ[cell]; myLen[cell] = flow.Len[cell]; }

                    // 逐位段
                    exact.Add((byte)corridor.Kind);
                    exact.Add((byte)traceOk);

                    // 浮点段:折线总长度 + 代价(点列由浮点派生,平局时点数与顶点都可不同)
                    double plen = 0;
                    if (corridor.Points != null)
                    {
                        for (int i = 2; i < corridor.Points.Length; i += 2)
                        {
                            double dx = (corridor.Points[i] - corridor.Points[i - 2]).ToDouble();
                            double dy = (corridor.Points[i + 1] - corridor.Points[i - 1]).ToDouble();
                            plen += Math.Sqrt(dx * dx + dy * dy);
                        }
                    }

                    CompareFloat(plen, floats, ref floatCursor, ref maxGeoAbs, ref maxGeoRel);
                    CompareFloat(corridor.Cost.ToDouble(), floats, ref floatCursor, ref maxTightAbs, ref maxTightRel);
                    if (maxTightRel > 1e-4) TestContext.Out.WriteLine($"TIGHT cost ctx {ctxIndex} 对 {pairIndex} ({start}→{goal}): 我 {corridor.Cost.ToDouble()} rel {maxTightRel:E2}");

                    // 结构容差段:web 折线坐标(诊断)、链与到达集合
                    int webPointCount = ReadI32(detail, ref detailCursor);
                    detailCursor += Math.Max(0, webPointCount) * 16;
                    var webPolys = ReadI32s(detail, ref detailCursor);
                    var webClusters = ReadI32s(detail, ref detailCursor);
                    SkipI32s(detail, ref detailCursor); // cells(诊断,链最优性由代价带宽覆盖)
                    ReadI32(detail, ref detailCursor); // reached(= 集合大小,与下行同值)
                    int webCellCount = ReadI32(detail, ref detailCursor);

                    // 两侧走廊掩码(含 pad):独占格只能来自走廊分歧的 cluster(掩码对称差),
                    // 不能来自场内的随机泄漏
                    var webMask = new byte[c * c];
                    webMask[NavGridSteps.ClusterOf(goal, n, t, c)] = 1;
                    foreach (int cl in webClusters) webMask[cl] = 1;
                    int tileCellsSq = t * t;
                    foreach (int p in webPolys) webMask[(p / tileCellsSq) >> 1] = 1;
                    webMask = FlowFieldBuilder.PadMask(webMask, c, runtime.Flowfield.CorridorPadding);
                    var myMask = flow.Mask!;

                    var webCellsArr = new int[webCellCount];
                    var webIntegArr = new double[webCellCount];
                    var webLenArr = new double[webCellCount];
                    var webCells = new HashSet<int>();
                    int exWeb = 0, unexplained = 0;
                    for (int k = 0; k < webCellCount; k++)
                    {
                        int webCell = ReadI32(detail, ref detailCursor);
                        webCellsArr[k] = webCell;
                        webCells.Add(webCell);
                        webIntegArr[k] = BitConverter.ToDouble(floats, floatCursor); floatCursor += 8;
                        webLenArr[k] = BitConverter.ToDouble(floats, floatCursor); floatCursor += 8;
                        if (!myInteg.ContainsKey(webCell))
                        {
                            exWeb++;
                            int cl = NavGridSteps.ClusterOf(webCell % (n * n), n, t, c);
                            if ((webMask[cl] != 0) == (myMask[cl] != 0)) unexplained++;
                        }
                    }

                    int exMine = 0;
                    foreach (int cell in myCells)
                    {
                        if (webCells.Contains(cell)) continue;
                        exMine++;
                        int cl = NavGridSteps.ClusterOf(cell % (n * n), n, t, c);
                        if ((webMask[cl] != 0) == (myMask[cl] != 0)) unexplained++;
                    }

                    HashSet<int>? pairFlood = null;
                    if (exMine + exWeb > 0)
                    {
                        // 走廊分歧也会改变可达性:口袋格在双方掩码的交上连不到终点时,独占同样合法。
                        // 只有"交集洪泛内仍只被一侧到达"才算真分歧。
                        var intersection = new byte[c * c];
                        for (int k = 0; k < intersection.Length; k++) intersection[k] = (byte)(webMask[k] & myMask[k]);
                        pairFlood = MaskedFlood(nav, goal, intersection, n);
                        unexplained = 0;
                        foreach (int cell in webCells)
                        {
                            if (!myInteg.ContainsKey(cell) && pairFlood.Contains(cell)) unexplained++;
                        }

                        foreach (int cell in myCells)
                        {
                            if (!webCells.Contains(cell) && pairFlood.Contains(cell)) unexplained++;
                        }
                    }

                    exclusiveMine += exMine;
                    exclusiveWeb += exWeb;
                    Assert.That(unexplained, Is.EqualTo(0),
                        $"{seed} ctx {ctxIndex} 对 {pairIndex} ({start}→{goal}): {unexplained} 个独占格不在走廊分歧的 cluster 内(我独占 {exMine} / web 独占 {exWeb})");

                    // 走廊分歧的对子:我方场与 web 场的掩码不同,逐格值各自相对己方走廊正确,
                    // 不能互比——改用 web 掩码重建一场与 web 逐格对拍(验证构建器);
                    // 我方走廊的合法性由代价紧带宽 + 结构容差覆盖
                    var cmpFlow = flow;
                    if (exMine + exWeb > 0)
                    {
                        cmpFlow = FlowFieldBuilder.Build(nav, goal, webMask, pool);
                    }

                    var cmpInteg = new Dictionary<int, Fix64>(cmpFlow.Reached);
                    var cmpLen = new Dictionary<int, Fix64>(cmpFlow.Reached);
                    for (int k = 0; k < cmpFlow.Reached; k++)
                    {
                        int cell = pool.Order[k];
                        cmpInteg[cell] = cmpFlow.Integ[cell];
                        cmpLen[cell] = cmpFlow.Len[cell];
                    }

                    int shared = 0;
                    for (int k = 0; k < webCellCount; k++)
                    {
                        int webCell = webCellsArr[k];
                        Assert.That(cmpInteg.ContainsKey(webCell),
                            $"{seed} ctx {ctxIndex} 对 {pairIndex} ({start}→{goal}): 同掩码场未到达 web 格 {webCell}(构建器真分歧)");
                        shared++;
                        Accumulate(Math.Abs(cmpInteg[webCell].ToDouble() - webIntegArr[k]), webIntegArr[k], ref maxTightAbs, ref maxTightRel);
                        Accumulate(Math.Abs(cmpLen[webCell].ToDouble() - webLenArr[k]), webLenArr[k], ref maxGeoAbs, ref maxGeoRel);
                    }

                    sharedCells += shared;

                    CompareFloat(cmpFlow.Integ[start].ToDouble(), floats, ref floatCursor, ref maxTightAbs, ref maxTightRel);
                    CompareFloat(cmpFlow.Len[start].ToDouble(), floats, ref floatCursor, ref maxGeoAbs, ref maxGeoRel);

                    // wp 诊断流:cmpFlow 与 web 同掩码,web 的每个路点都应在我方场里到达
                    int webTraceCount = ReadI32(wpBin, ref wpCursor);
                    wpCursor += webTraceCount * 4;
                    int wpCellCount = ReadI32(wpBin, ref wpCursor);
                    Assert.That(wpCellCount, Is.EqualTo(webCellCount), $"{seed} wp 流与 detail 流到达数不一致");
                    for (int k = 0; k < wpCellCount; k++)
                    {
                        int webCell = ReadI32(wpBin, ref wpCursor);
                        int webWp = ReadI32(wpBin, ref wpCursor);
                        if (webWp == cmpFlow.Wp[webCell]) { wpDecisive++; continue; }
                        if (IsNearTieAccepted(nav, cmpFlow, webCell, webWp, n)) wpTie++;
                        else
                        {
                            Assert.Fail($"{seed} navId {nav.Id} 格 {webCell}: 路点 {cmpFlow.Wp[webCell]} 与 Web {webWp} 既不相等也不是平局双解");
                        }
                    }

                    pool.Give(flow);
                    if (!ReferenceEquals(cmpFlow, flow)) pool.Give(cmpFlow);
                    if (corridor.Kind != CorridorQuery.Branch.None || traceOk != 0) reachable++;
                }

                Assert.That(reachable, Is.EqualTo(truthContexts[ctxIndex]!["reachable"]!.GetValue<int>()),
                    $"{seed} navId {nav.Id} 可达对数");
                ctxIndex++;
            }
        }

        var ccBytes = BitConverter.GetBytes(ctxIndex);
        for (int i = 0; i < 4; i++) exact[8 + i] = ccBytes[i];
        var hash = S2NavTruthTests.Fnv(exact.ToArray());
        var expected = truth["fnv1a"]!.GetValue<string>();
        if (hash != expected)
        {
            string dumpDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s3c-diff", seed);
            Directory.CreateDirectory(dumpDir);
            File.WriteAllBytes(Path.Combine(dumpDir, "exact.bin"), exact.ToArray());
        }

        Assert.That(hash, Is.EqualTo(expected), $"S3-c 分支 / 到达判定与 Web 不一致(seed {seed})");
        Assert.That(floatCursor, Is.EqualTo(floats.Length), $"{seed} 浮点流长度不符(读取 {floatCursor} ≠ {floats.Length})");
        Assert.That(detailCursor, Is.EqualTo(detail.Length), $"{seed} detail 流长度不符");
        Assert.That(wpCursor, Is.EqualTo(wpBin.Length), $"{seed} wp 流长度不符");
        Assert.That(maxTightRel, Is.LessThan(5e-6), $"S3-c 代价分歧超紧带宽(seed {seed}): abs {maxTightAbs:E2} rel {maxTightRel:E2}");
        Assert.That(maxGeoRel, Is.LessThan(0.25), $"S3-c 几何分歧超松带宽(seed {seed}): abs {maxGeoAbs:E2} rel {maxGeoRel:E2}");
        TestContext.Out.WriteLine(
            $"seed {seed}: 逐位一致;代价最大分歧 {maxTightRel:E2} 几何最大分歧 {maxGeoRel:E2};" +
            $"共有格 {sharedCells} 独占(我 {exclusiveMine} / web {exclusiveWeb});路点全等 {wpDecisive} 平局双解 {wpTie}");
    }

    // 掩码交集上自终点的可达洪泛(与流场同扩张规则:逐层 8 向、portal 跨层、链接反向)。
    private static HashSet<int> MaskedFlood(NavContext nav, int goal, byte[] mask, int n)
    {
        int n2 = n * n;
        int s = nav.Hpa!.ClusterSize, c = nav.Hpa.ClustersPerSide;
        bool layered = nav.UpperTiles is { Count: > 0 };
        var seen = new HashSet<int> { goal };
        var queue = new Queue<int>(new[] { goal });
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            int lv = u >= n2 ? 1 : 0, o = lv * n2, cell = u - o;
            var grid = lv != 0 ? nav.UpPass : nav.Passable;
            int x = cell % n, y = cell / n;
            for (int d = 0; d < 8; d++)
            {
                if (!NavGridSteps.CanStep(grid, n, x, y, d)) continue;
                int vc = (y + NavGridSteps.DY[d]) * n + x + NavGridSteps.DX[d], v = o + vc;
                if (mask[NavGridSteps.ClusterOf(vc, n, s, c)] == 0) continue;
                if (seen.Add(v)) queue.Enqueue(v);
            }

            if (layered && nav.Portal[cell] != 0 && nav.Passable[cell] != 0 && nav.UpPass[cell] != 0)
            {
                int v = lv != 0 ? cell : n2 + cell;
                if (seen.Add(v)) queue.Enqueue(v);
            }

            if (nav.Links != null && lv == 0)
            {
                for (int j = nav.Links.OutStart[u], e1 = nav.Links.OutStart[u + 1]; j < e1; j++)
                {
                    int v = nav.Links.To[nav.Links.OutList[j]];
                    if (mask[NavGridSteps.ClusterOf(v, n, s, c)] == 0) continue;
                    if (seen.Add(v)) queue.Enqueue(v);
                }
            }
        }

        return seen;
    }

    // 平局双解 / 拉直差:web 的路点在我方场里必须已到达且沿场下降(允许浮点尾差),
    // 相邻格当作下降父格直接合法,非相邻格还需同层视线——三者同真时该路点就是
    // 一个合法的绷紧下降路点;跨层平局(0 代价边)在双层可走的 portal 格双解皆合法。
    private static bool IsNearTieAccepted(NavContext nav, FlowField flow, int cell, int webWp, int n)
    {
        int n2 = n * n;
        if (webWp < 0 || webWp >= 2 * n2) return false;
        int lv = cell >= n2 ? 1 : 0;
        int o = lv * n2, c = cell - o;
        if (webWp >= n2 != (lv != 0))
        {
            return webWp == (lv != 0 ? c : n2 + c)
                && nav.Portal[c] != 0 && nav.Passable[c] != 0 && nav.UpPass[c] != 0;
        }

        var inf = Fix64.MaxValue / 4;
        if (flow.Integ[webWp] >= inf) return false;
        var grid = lv != 0 ? nav.UpPass : nav.Passable;
        int x = c % n, y = c / n, wc = webWp - o;
        double descentSlack = 1e-4 + 1e-5 * flow.Integ[cell].ToDouble();
        bool descends = flow.Integ[webWp].ToDouble() < flow.Integ[cell].ToDouble() + descentSlack;
        if (!descends) return false;
        // 相邻格 = web 的下降父格(无需视线);非相邻 = 拉直路点(需同层视线)
        int dx = Math.Abs(wc % n - x), dy = Math.Abs(wc / n - y);
        if (dx <= 1 && dy <= 1) return true;
        return NavGridSteps.LineOfSight(grid, n, c, wc);
    }

    private static void Accumulate(double abs, double truthRef, ref double maxAbs, ref double maxRel)
    {
        double rel = abs / Math.Max(1e-3, Math.Abs(truthRef));
        if (abs > maxAbs) maxAbs = abs;
        if (rel > maxRel) maxRel = rel;
    }

    private static readonly double UnreachedSentinel = Fix64.MaxValue.ToDouble() / 4;

    private static void CompareFloat(double mine, byte[] truth, ref int cursor, ref double maxAbs, ref double maxRel)
    {
        double w = BitConverter.ToDouble(truth, cursor);
        cursor += 8;
        if (double.IsInfinity(w) || mine >= UnreachedSentinel)
        {
            // 未到达:web 写 Infinity,我方是 Fix64.MaxValue——同真即合法,一真一假即真分歧
            if (double.IsInfinity(w) != mine >= UnreachedSentinel)
            {
                maxAbs = double.PositiveInfinity;
                maxRel = double.PositiveInfinity;
            }

            return;
        }

        Accumulate(Math.Abs(mine - w), w, ref maxAbs, ref maxRel);
    }

    private static int ReadI32(byte[] buffer, ref int cursor)
    {
        int v = BitConverter.ToInt32(buffer, cursor);
        cursor += 4;
        return v;
    }

    private static void SkipI32s(byte[] buffer, ref int cursor)
    {
        int count = ReadI32(buffer, ref cursor);
        cursor += count * 4;
    }

    private static int[] ReadI32s(byte[] buffer, ref int cursor)
    {
        int count = ReadI32(buffer, ref cursor);
        var arr = new int[count];
        for (int i = 0; i < count; i++) arr[i] = ReadI32(buffer, ref cursor);
        return arr;
    }

    private static void WriteI32(List<byte> buffer, int v) => buffer.AddRange(BitConverter.GetBytes(v));

    private static string S1Dir(string seed) => Path.Combine("assets", "s1", seed);
}
