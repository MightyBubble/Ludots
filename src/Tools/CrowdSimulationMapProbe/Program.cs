using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Systems;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace CrowdSimulationMapProbe;

/// <summary>
/// S1 地图探针：用真实 Ludots 装载路径（CrowdSimulationConfig 合并、.navsurface 读取、
/// 地图实体提取、.height 读取）渲染三张种子图的地形类型 / 阻挡格 / 桥梁 / 跳跃候选。
/// 用法: CrowdSimulationMapProbe &lt;seedAssetsDir&gt; &lt;standardAssetsDir&gt; &lt;demoAssetsDir&gt; &lt;outDir&gt;
/// 配置读标准参数 Mod，体型档案读能力 Mod，FixedHz 读种子目录的 Engine/clock.json。
/// </summary>
public static class Program
{
    private const int RenderSize = 1024;

    public static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--s2")
        {
            if (args.Length < 5) return Usage();
            return RunS2(args[1], args[2], args[3], args[4]);
        }

        if (args.Length >= 1 && args[0] == "--s3")
        {
            if (args.Length < 5) return Usage();
            return RunS3(args[1], args[2], args[3], args[4]);
        }

        if (args.Length >= 1 && args[0] == "--s3c")
        {
            if (args.Length < 5) return Usage();
            return RunS3C(args[1], args[2], args[3], args[4]);
        }

        if (args.Length >= 1 && args[0] == "--s4probe")
        {
            if (args.Length < 4) return Usage();
            return RunS4Probe(args[1], args[2], args[3]);
        }

        if (args.Length < 4)
        {
            return Usage();
        }

        string seedDir = args[0];
        string standardDir = args[1];
        string demoDir = args[2];
        string outDir = args[3];
        Directory.CreateDirectory(outDir);

        var bundle = Load(seedDir, standardDir, demoDir);
        RenderTypes(bundle.Surface, bundle.Height, bundle.TypeColors, Path.Combine(outDir, $"{bundle.MapId}_types.png"));
        RenderBlocked(bundle.Surface, bundle.Height, bundle.TypeColors, bundle.Grid, Path.Combine(outDir, $"{bundle.MapId}_blocked.png"));
        RenderFull(bundle.Surface, bundle.Height, bundle.TypeColors, bundle.Grid, bundle.MapSurface, bundle.Runtime, Path.Combine(outDir, $"{bundle.MapId}_full.png"));

        Console.WriteLine($"[probe] {bundle.MapId}: cells={bundle.Grid.NavCellsPerSide}x{bundle.Grid.NavCellsPerSide} blockers={bundle.MapSurface.Blockers.Count} bridges={bundle.MapSurface.LayeredPaths.Count} jumps={bundle.Surface.JumpCandidates.Length}");
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: CrowdSimulationMapProbe <seedAssetsDir> <standardAssetsDir> <demoAssetsDir> <outDir>");
        Console.Error.WriteLine("       CrowdSimulationMapProbe --s2|--s3|--s3c <seedAssetsDir> <standardAssetsDir> <demoAssetsDir> <outDir>");
        Console.Error.WriteLine("       CrowdSimulationMapProbe --s4probe <seedAssetsDir> <standardAssetsDir> <demoAssetsDir>");
        return 2;
    }

    /// <summary>S2:逐代理体型的可走区域视图(不可走 = 红色遮罩;桥 portal 高亮;跳跃链接叠画)。</summary>
    private static int RunS2(string seedDir, string standardDir, string demoDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var bundle = Load(seedDir, standardDir, demoDir);
        var heights = bundle.Heights;
        var deck = UpperLayerBake.RasterizeLayeredPaths(bundle.MapSurface.LayeredPaths, bundle.Runtime);

        var seen = new HashSet<int>();
        for (int a = 0; a < bundle.Runtime.AgentTypes.Count; a++)
        {
            foreach (int clearance in bundle.Runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceNavCells).Distinct())
            {
                var nav = NavContextBaker.Bake(bundle.Runtime, bundle.Grid, heights, deck, a, clearance);
                if (!seen.Add(nav.Id)) continue;
                string name = $"{bundle.Runtime.AgentTypes[a].Id}_c{clearance}";
                RenderNav(bundle, nav, heights, Path.Combine(outDir, $"{bundle.MapId}_nav_{name}.png"));
                Console.WriteLine($"[probe:s2] {bundle.MapId} {name}: passable={nav.Cells.Length} comps={nav.CompCount} links={nav.Links?.Count ?? 0}");
            }
        }

        return 0;
    }

    private static void RenderNav(SeedBundle bundle, NavContext nav, NavHeightField heights, string path)
    {
        var px = BaseLayer(bundle.Surface, bundle.Height, bundle.TypeColors);
        int navCellsPerSide = nav.NavCellsPerSide;
        // 不可走遮罩
        for (int cy = 0; cy < navCellsPerSide; cy++)
        {
            for (int cx = 0; cx < navCellsPerSide; cx++)
            {
                if (nav.Passable[cy * navCellsPerSide + cx] == 0) FillCell(px, cx, cy, navCellsPerSide, (160, 30, 30), 0.5f);
            }
        }

        // 桥面可走格与 portal
        for (int cy = 0; cy < navCellsPerSide; cy++)
        {
            for (int cx = 0; cx < navCellsPerSide; cx++)
            {
                if (nav.Portal[cy * navCellsPerSide + cx] != 0) FillCell(px, cx, cy, navCellsPerSide, (255, 213, 79), 0.9f);
                else if (nav.UpPass[cy * navCellsPerSide + cx] != 0) FillCell(px, cx, cy, navCellsPerSide, (150, 110, 70), 0.85f);
            }
        }

        // 跳跃链接:双向靛蓝 / 单向琥珀
        if (nav.Links != null)
        {
            float cs = bundle.Runtime.NavCellSizeCm;
            float worldCm = navCellsPerSide * cs;
            for (int e = 0; e < nav.Links.Count; e++)
            {
                int from = nav.Links.From[e], to = nav.Links.To[e];
                var color = nav.Links.TwoWay[e] != 0 ? (92, 107, 192) : (255, 179, 0);
                DrawLine(px,
                    (from % navCellsPerSide + 0.5f) * cs, ((from / navCellsPerSide) + 0.5f) * cs,
                    (to % navCellsPerSide + 0.5f) * cs, ((to / navCellsPerSide) + 0.5f) * cs,
                    worldCm, color);
            }
        }

        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    /// <summary>S3:NavMesh 多边形网格视图(地面 tile 白色描边,桥面 tile 棕色)。</summary>
    private static int RunS3(string seedDir, string standardDir, string demoDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var bundle = Load(seedDir, standardDir, demoDir);
        var runtime = bundle.Runtime;
        var heights = bundle.Heights;
        var deck = UpperLayerBake.RasterizeLayeredPaths(bundle.MapSurface.LayeredPaths, runtime);
        int navCellsPerSide = runtime.NavCellsPerSide, t = runtime.Hpa.NavCellsPerTile, c = navCellsPerSide / t;

        // 步兵上下文(含桥面),缓存随烘焙填充
        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, t,
            runtime.Navmesh.MinRegionNavCellArea.ToDouble(), runtime.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            runtime.Navmesh.MaxEdgeLenNavCells.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);
        var nav = NavContextBaker.Bake(runtime, bundle.Grid, heights, deck, 0, 1, cache);

        var px = BaseLayer(bundle.Surface, bundle.Height, bundle.TypeColors);
        float cellPx = (float)RenderSize / navCellsPerSide;
        float csCm = runtime.NavCellSizeCm;

        for (int ty = 0; ty < c; ty++)
        {
            for (int tx = 0; tx < c; tx++)
            {
                DrawTileEntry(px, cache, nav.Passable, bundle.Grid.Area, navCellsPerSide, tx, ty, t, (230, 230, 230), cellPx, csCm);
            }
        }

        foreach (int tileId in deck.TileOrder)
        {
            bool any = false;
            int tx = tileId % c, ty = tileId / c;
            for (int y = ty * t; y < ty * t + t && !any; y++)
                for (int x = tx * t; x < tx * t + t; x++)
                    if (nav.UpPass[y * navCellsPerSide + x] != 0) { any = true; break; }
            if (any) DrawTileEntry(px, cache, nav.UpPass, nav.UpArea, navCellsPerSide, tx, ty, t, (255, 213, 79), cellPx, csCm);
        }

        // HPA* 分区:tile 网格 + 入口节点(抽象图形状)
        var hpa = HpaGraph.Build(nav, runtime);
        var flat = hpa.Flatten();
        float worldCm2 = navCellsPerSide * csCm;
        for (int i = 0; i < flat.NodeCell.Length; i++)
        {
            int cell = flat.NodeCell[i];
            float cx = (cell % navCellsPerSide + 0.5f) * csCm / worldCm2 * RenderSize;
            float cy = ((cell / navCellsPerSide) + 0.5f) * csCm / worldCm2 * RenderSize;
            FillDisc(px, cx, cy, 2.2f, flat.NodeLayer[i] != 0 ? (255, 213, 79) : (64, 196, 255));
        }

        for (int b = 0; b <= c; b++)
        {
            float p = b * t * cellPx;
            for (int i2 = 0; i2 < RenderSize; i2 += 2)
            {
                if (b < c) { int ii = (i2 * RenderSize + (int)Math.Min(RenderSize - 1, p)) * 3; px[ii] = 255; px[ii + 1] = 255; px[ii + 2] = 255; }
                int ii2 = ((int)Math.Min(RenderSize - 1, p) * RenderSize + i2) * 3;
                if (b < c) { px[ii2] = 255; px[ii2 + 1] = 255; px[ii2 + 2] = 255; }
            }
        }

        Console.WriteLine($"[probe:s3] {bundle.MapId} foot: tiles={cache.Count} bakes={cache.Bakes} hpaNodes={flat.NodeCell.Length} hpaEdges={hpa.EdgeCount}");

        string path = Path.Combine(outDir, $"{bundle.MapId}_navmesh_foot.png");
        PngWriter.Write(path, RenderSize, RenderSize, px);
        Console.WriteLine($"[probe:s3] {bundle.MapId} foot: tiles={cache.Count} bakes={cache.Bakes} hits={cache.Hits} comps={nav.CompCount}");
        return 0;
    }

    /// <summary>S3-c:两点路径 + 方向图视图(前 6 组对子的流场箭头与折线;另含跳跃层 2 组)。</summary>
    private static int RunS3C(string seedDir, string standardDir, string demoDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var bundle = Load(seedDir, standardDir, demoDir);
        var runtime = bundle.Runtime;
        var heights = bundle.Heights;
        var deck = UpperLayerBake.RasterizeLayeredPaths(bundle.MapSurface.LayeredPaths, runtime);
        int navCellsPerSide = runtime.NavCellsPerSide, t = runtime.Hpa.NavCellsPerTile;

        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(seedDir, "CrowdSimulation", "parity", "s3c-query-truth.json")))!;
        var pairs = truth["pairs"]!.AsArray().Select(p => (Start: p![0]!.GetValue<int>(), Goal: p[1]!.GetValue<int>())).ToArray();

        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, t,
            runtime.Navmesh.MinRegionNavCellArea.ToDouble(), runtime.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            runtime.Navmesh.MaxEdgeLenNavCells.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        // 逐移动类型(去重上下文):0 = 步兵(地面,含桥面),最后一个 = 跳跃层
        var agentIndices = new[] { 0, runtime.AgentTypes.Count - 1 };
        foreach (int a in agentIndices)
        {
            var nav = NavContextBaker.Bake(runtime, bundle.Grid, heights, deck, a, 1, cache);
            string typeId = runtime.AgentTypes[a].Id;
            var scratch = new CorridorQuery.Scratch(t * t);
            var pool = new FlowPool(navCellsPerSide, runtime.Flowfield.PoolCapacity);
            int shown = 0;
            foreach (var (start, goal) in pairs)
            {
                if (shown >= (a == 0 ? 6 : 2)) break;
                var mask = new byte[(navCellsPerSide / t) * (navCellsPerSide / t)];
                mask[NavGridSteps.TileOf(goal, navCellsPerSide, t, navCellsPerSide / t)] = 1;
                var corridor = CorridorQuery.CorridorTo(nav, runtime, start, goal, mask, 0, scratch);
                var flow = FlowFieldBuilder.Build(nav, goal, FlowFieldBuilder.PadMask(mask, navCellsPerSide / t, runtime.Flowfield.CorridorPaddingNavTiles), pool);
                var trace = FlowFieldBuilder.TracePath(flow, start, navCellsPerSide * navCellsPerSide);
                if (trace[trace.Count - 1] != goal && corridor.Points == null) { pool.Give(flow); continue; }

                Fix64[]? pts;
                int pointCount;
                if (corridor.Points != null && nav.Links == null)
                {
                    pts = corridor.Points;
                    pointCount = corridor.PointCount;
                }
                else
                {
                    pts = CorridorQuery.FlowPoints(flow, start)?.Points;
                    pointCount = pts?.Length ?? 0;
                }

                RenderPath(bundle, nav, flow, trace, pts, pointCount, start, goal,
                    Path.Combine(outDir, $"{bundle.MapId}_s3c_{typeId}_p{shown}.png"));
                shown++;
                pool.Give(flow);
            }

            Console.WriteLine($"[probe:s3c] {bundle.MapId} {typeId}: 路径视图 {shown} 张");
        }

        return 0;
    }

    private static void RenderPath(SeedBundle bundle, NavContext nav, FlowField flow, List<int> trace, Fix64[]? pts, int pointCount, int start, int goal, string path)
    {
        var px = BaseLayer(bundle.Surface, bundle.Height, bundle.TypeColors);
        int navCellsPerSide = nav.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
        float worldCm = navCellsPerSide * bundle.Runtime.NavCellSizeCm;

        // 方向箭头:每 6 格抽样,从格心指向路点格心
        for (int cy = 2; cy < navCellsPerSide - 2; cy += 6)
        {
            for (int cx = 2; cx < navCellsPerSide - 2; cx += 6)
            {
                int u = cy * navCellsPerSide + cx;
                if (flow.Integ[u] >= Fix64.MaxValue / 4) continue;
                int w = flow.Wp[u];
                if (w < 0) continue;
                int wc = w % n2;
                float cs = bundle.Runtime.NavCellSizeCm;
                float x0 = (cx + 0.5f) * cs, y0 = (cy + 0.5f) * cs;
                float x1 = (wc % navCellsPerSide + 0.5f) * cs, y1 = (wc / navCellsPerSide + 0.5f) * cs;
                float dx = x1 - x0, dy = y1 - y0;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 1e-3f) continue;
                float arrow = Math.Min(len, 3 * cs);
                DrawLine(px, x0, y0, x0 + dx / len * arrow, y0 + dy / len * arrow, worldCm, (220, 235, 245));
            }
        }

        // 路点链(单位实际行走的绷紧路径)青色
        float csCm = bundle.Runtime.NavCellSizeCm;
        for (int k = 1; k < trace.Count; k++)
        {
            int a = trace[k - 1] % n2, b = trace[k] % n2;
            DrawLine(px, (a % navCellsPerSide + 0.5f) * csCm, (a / navCellsPerSide + 0.5f) * csCm, (b % navCellsPerSide + 0.5f) * csCm, (b / navCellsPerSide + 0.5f) * csCm, worldCm, (64, 196, 255));
        }

        // 折线(查询结果)品红
        if (pts != null)
        {
            for (int k = 2; k < pointCount; k += 2)
            {
                DrawLine(px,
                    (float)pts[k - 2].ToDouble() * csCm, (float)pts[k - 1].ToDouble() * csCm,
                    (float)pts[k].ToDouble() * csCm, (float)pts[k + 1].ToDouble() * csCm,
                    worldCm, (255, 64, 200));
            }
        }

        // 起点绿 / 终点红
        FillDisc(px, (start % navCellsPerSide + 0.5f) * csCm / worldCm * RenderSize, (start / navCellsPerSide + 0.5f) * csCm / worldCm * RenderSize, 5, (76, 217, 100));
        FillDisc(px, (goal % navCellsPerSide + 0.5f) * csCm / worldCm * RenderSize, (goal / navCellsPerSide + 0.5f) * csCm / worldCm * RenderSize, 5, (255, 69, 58));
        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    /// <summary>跑 spawn，把前 8 个单位的玩家、组、位置和 rawEq 打印出来。进程不因不一致而失败。</summary>
    private static int RunS4Probe(string seedDir, string standardDir, string demoDir)
    {
        var bundle = Load(seedDir, standardDir, demoDir);
        var runtime = bundle.Runtime;
        var heights = bundle.Heights;
        var deck = UpperLayerBake.RasterizeLayeredPaths(bundle.MapSurface.LayeredPaths, runtime);
        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, runtime.Hpa.NavCellsPerTile,
            runtime.Navmesh.MinRegionNavCellArea.ToDouble(), runtime.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            runtime.Navmesh.MaxEdgeLenNavCells.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        var navs = new Dictionary<int, NavContext>();
        var navByLayerRadius = new Dictionary<(int, int), NavContext>();
        var radiusClasses = CrowdDeployment.DistinctRadiusClasses(runtime);
        var seen = new HashSet<int>();
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            foreach (var clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceNavCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtime, bundle.Grid, heights, deck, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;
                navs[nav.Id] = nav;
            }
        }

        foreach (var profile in runtime.Profiles)
        {
            int rIdx = CrowdDeployment.IndexOfRadiusClass(radiusClasses, (int)profile.RadiusCm.ToInt());
            navByLayerRadius[(profile.AgentTypeIndex, rIdx)] = navs[profile.NavContextId];
        }

        var probeWorld = Arch.Core.World.Create();
        var session = new CrowdSimSession(runtime, probeWorld, navs, navByLayerRadius, BuildWiring(runtime, probeWorld, demoDir, standardDir));
        // 复刻 spawn 的组分配(不生成),打印组清单与中心供对照
        var dbgRng = CrowdSimRng.Create((long)runtime.WorldSeed * 31 + 1L * 7919);
        int n0 = runtime.NavCellsPerSide;
        var dbgTypes = new List<int>();
        for (int t = 0; t < runtime.UnitTypes.Count; t++) dbgTypes.Add(t);
        int gid = 0;
        for (int p = 0; p < runtime.Deploy.Bases.Count; p++)
        {
            var b = runtime.Deploy.Bases[p];
            int bx = (int)(b.XCm / runtime.NavCellSizeCm), by = (int)(b.YCm / runtime.NavCellSizeCm);
            var ids = new List<string>();
            foreach (int t in dbgTypes)
            {
                for (int r = 0; r < radiusClasses.Count; r++)
                {
                    int l = runtime.UnitTypes[t].AgentTypeIndex;
                    if (CrowdDeployment.ProfileOf(runtime, l, r).Special) continue;
                    var nav = session.NavFor(l, r);
                    if (nav.Cells.Length == 0) continue;
                    var cs = new List<int>();
                    for (int k = 0; k < runtime.Deploy.CentersPerGroup; k++)
                    {
                        int x = Math.Min(n0 - 1, Math.Max(0, bx + CrowdDeployment.DetRound((dbgRng.Next() * 2 - Fix64.OneValue) * runtime.Deploy.BaseJitterNavCells)));
                        int y = Math.Min(n0 - 1, Math.Max(0, by + CrowdDeployment.DetRound((dbgRng.Next() * 2 - Fix64.OneValue) * runtime.Deploy.BaseJitterNavCells)));
                        int c = CrowdDeployment.NearestPassable(nav, y * n0 + x);
                        if (c >= 0) cs.Add(c);
                    }

                    if (cs.Count == 0) continue;
                    ids.Add($"{gid}(l{l}r{r})");
                    if (gid is 0 or 8 or 23 or 36 or 44 or 55) Console.WriteLine($"mine g{gid}: (l{l},r{r}) centers: [{string.Join(", ", cs)}]");
                    gid++;
                }
            }

            if (p == 3) Console.WriteLine($"mine p3 list: {string.Join(' ', ids)}");
        }

        int made = CrowdDeployment.Spawn(session, 12000);
        Console.WriteLine($"[probe:s4] made={made} seed={runtime.WorldSeed}");

        var dump = File.ReadAllBytes(Path.Combine(seedDir, "CrowdSimulation", "parity", "s4-units.bin"));
        int cursor = 8;
        for (int i = 0; i < 8; i++)
        {
            int player = BitConverter.ToInt32(dump, cursor); cursor += 4;
            int unitType = BitConverter.ToInt32(dump, cursor); cursor += 4;
            int rIdx = BitConverter.ToInt32(dump, cursor); cursor += 4;
            int group = BitConverter.ToInt32(dump, cursor); cursor += 4;
            double xm = BitConverter.ToDouble(dump, cursor); cursor += 8;
            double ym = BitConverter.ToDouble(dump, cursor); cursor += 8;
            var entity = session.Units.EntityAt(i);
            var pos = session.World.Get<Ludots.Core.Components.WorldPositionCm>(entity).Value;
            var state = session.World.Get<Ludots.Core.CrowdSimulation.Units.CrowdSimulationUnitState>(entity);
            Console.WriteLine(
                $"  u{i} truth(p{player} t{unitType} r{rIdx} g{group} x={xm:F9} y={ym:F9}) | " +
                $"mine(p{session.World.Get<Ludots.Core.Gameplay.Components.PlayerOwner>(entity).PlayerId} g{state.GroupId} " +
                $"x={WorldUnitsFix64.WorldCmToVisualMeters(pos).X:F9} y={WorldUnitsFix64.WorldCmToVisualMeters(pos).Z:F9}) " +
                $"rawEq=({pos.X.RawValue == (long)(xm * WorldUnits.CmPerMeter * 4294967296.0)},{pos.Y.RawValue == (long)(ym * WorldUnits.CmPerMeter * 4294967296.0)})");
        }

        return 0;
    }

    private static void DrawTileEntry(byte[] px, NavTileCache cache, byte[] passable, byte[] area, int navCellsPerSide, int tx, int ty, int navCellsPerTile, (int r, int g, int b) color, float cellPx, float csCm)
    {
        var entry = cache.Acquire(passable, area, navCellsPerSide, tx, ty);
        for (int p = 0; p < entry.Count; p++)
        {
            int s = entry.PolyStart[p], e = entry.PolyStart[p + 1];
            for (int k = s; k < e; k++)
            {
                int a = entry.PolyVerts[k], b = entry.PolyVerts[k + 1 < e ? k + 1 : s];
                float cellVisual = WorldUnits.CmToM(csCm);
                float x0 = (tx * navCellsPerTile + entry.Vx[a]) * cellVisual;
                float y0 = (ty * navCellsPerTile + entry.Vy[a]) * cellVisual;
                float x1 = (tx * navCellsPerTile + entry.Vx[b]) * cellVisual;
                float y1 = (ty * navCellsPerTile + entry.Vy[b]) * cellVisual;
                float worldCm = navCellsPerSide * csCm;
                int steps = (int)(MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / worldCm * RenderSize * 2) + 1;
                for (int si = 0; si <= steps; si++)
                {
                    float tt = si / (float)steps;
                    FillDisc(px,
                        (x0 + (x1 - x0) * tt) / worldCm * RenderSize,
                        (y0 + (y1 - y0) * tt) / worldCm * RenderSize,
                        0.8f, color);
                }
            }
        }
    }

    private sealed record SeedBundle(
        string MapId,
        CrowdSimulationRuntimeConfig Runtime,
        NavSurfaceAsset Surface,
        ContinuousHeightmapAsset Height,
        CrowdSimulationMapSurface MapSurface,
        SurfaceGrid Grid,
        NavHeightField Heights,
        (int R, int G, int B)[] TypeColors);

    /// <summary>单位创建单路径的无头装配:与引擎同一装载链(VFS 挂载能力 mod 资产 →
    /// ConfigPipeline → MapLoader.LoadTemplates)取模板注册表与模板键;选中属主置空,不镜像。</summary>
    private static CrowdSimPresentationWiring BuildWiring(
        CrowdSimulationRuntimeConfig runtime, Arch.Core.World world, string demoDir, string standardDir)
    {
        var vfs = new Ludots.Core.Modding.VirtualFileSystem();
        vfs.Mount("Core", demoDir);
        var pipeline = new Ludots.Core.Config.ConfigPipeline(vfs, modLoader: null!);
        var mapLoader = new Ludots.Core.Systems.MapLoader(world, new Ludots.Core.Map.WorldMap(), pipeline);
        mapLoader.LoadTemplates(Ludots.Core.Config.ConfigCatalogLoader.Load(pipeline));
        var collections = new Ludots.Core.EntityCollections.EntityCollectionStore(
            new Ludots.Core.Registry.StringIntRegistry());
        return new CrowdSimPresentationWiring
        {
            StableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator(),
            TemplateRegistry = mapLoader.TemplateRegistry,
            TemplateKeys = mapLoader.EntityTemplateKeys,
            TemplatesByUnitTypeRadius = CrowdSimPresentationWiring.BuildUnitTypeTemplates(
                runtime, mapLoader.TemplateRegistry, mapLoader.EntityTemplateKeys,
                new AgentProfileRegistry(JsonSerializer.Deserialize<List<AgentProfileConfig>>(
                    File.ReadAllText(Path.Combine(standardDir, "Navigation", "agent_profiles.json")),
                    StrictJsonOptions.CreateCamelCase())!),
                "MapProbe 装配"),
            RadiusMetersBlackboardKeyId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(
                Ludots.Core.CrowdSimulation.Runtime.CrowdSimulationRuntime.Keys.UnitRadiusKey),
            SelectedCollectionKeyId = collections.KeyRegistry.Register(CrowdSimPresentationWiring.SelectedCollectionKey),
            Collections = collections,
        };
    }

    private static int ReadSeedFixedHz(string seedDir)
    {
        string path = Path.Combine(seedDir, "Engine", "clock.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"探针需要种子目录的 Engine/clock.json 提供 FixedHz（{path}）。");
        }

        var clock = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException($"Engine/clock.json 根必须是对象（{path}）。");
        if (clock["FixedHz"] is not JsonValue hz || !hz.TryGetValue<int>(out int fixedHz) || fixedHz < 1)
        {
            throw new InvalidOperationException($"Engine/clock.json: FixedHz 需为 ≥ 1 的整数（{path}）。");
        }

        return fixedHz;
    }

    /// <summary>
    /// 探针不启引擎。关系类型从仓库里的 Relationships/catalog.json 登记，边交给
    /// InstanceRelationMaterializer。RelationshipCatalogPipelineLoader 要 ConfigPipeline、
    /// ModLoader 和 VFS，那是引擎的配置合并，这里不另起一套。
    /// 物化器会建地图上全部实例边，不按队伍代表过滤：过滤又是一套边遍历。
    /// 功能位投影只问队伍代表之间的边，其它边上的关系留在运行时里，不进矩阵。
    /// </summary>
    private static CrowdPairRules ProjectSeedMap(string seedDir, CrowdSimulationConfig config, MapConfig map)
    {
        using var world = World.Create();
        var types = new RelationshipTypeRegistry();
        var metrics = new RelationshipMetricRegistry();
        var flags = new RelationshipFlagRegistry();
        var bands = new RelationshipBandRegistry();
        foreach (string catalogPath in RelationshipCatalogPaths(seedDir))
        {
            RelationshipCatalogConfig? catalog = JsonSerializer.Deserialize<RelationshipCatalogConfig>(
                File.ReadAllText(catalogPath),
                RelationshipCatalogPipelineLoader.SerializerOptions);
            if (catalog == null)
            {
                throw new InvalidOperationException($"关系目录反序列化为空：{catalogPath}");
            }

            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, metrics, flags, bands);
        }

        var relationships = new RelationshipRuntime(
            world,
            types,
            metrics,
            flags,
            bands,
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(world));
        var index = new MapLoadEntityIndex();
        foreach (var authored in map.Entities)
        {
            if (string.IsNullOrWhiteSpace(authored.InstanceId))
            {
                continue;
            }

            index.Register(map.Id, authored.InstanceId, world.Create());
        }

        var teams = new TeamEntityLookup();
        foreach (var team in map.Teams)
        {
            Entity entity = index.GetRequired(map.Id, team.RepresentativeInstanceId, $"Teams TeamId {team.TeamId}");
            world.Add(entity, new Team { Id = team.TeamId });
            world.Add(entity, new TeamIdentity { TeamId = team.TeamId });
            teams.Register(team.TeamId, entity);
        }

        var players = new PlayerEntityLookup();
        foreach (var player in map.Players)
        {
            Entity entity = index.GetRequired(map.Id, player.RepresentativeInstanceId, $"Players PlayerId {player.PlayerId}");
            world.Add(entity, new PlayerIdentity { PlayerId = player.PlayerId });
            world.Add(entity, new Team { Id = player.TeamId });
            players.Register(player.PlayerId, entity);
        }

        using var session = new MapSession(new MapId(map.Id), map);
        InstanceRelationMaterializer.Materialize(session, map, index, relationships, types, metrics);
        return CrowdPairRulesProjection.Project(config, map, new TeamRelationQuery(relationships, teams), players, world).Rules;
    }

    /// <summary>
    /// 群体模拟进图时引擎合并的两份目录：仓库 assets 在前，LudotsCoreMod 在后。
    /// 探针参数只有种子和两个群体目录，目录文件在仓库根，从种子目录往上找。
    /// </summary>
    private static IEnumerable<string> RelationshipCatalogPaths(string seedDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(seedDir));
        while (dir != null)
        {
            string engineCatalog = Path.Combine(dir.FullName, "assets", "Relationships", "catalog.json");
            string coreModCatalog = Path.Combine(dir.FullName, "mods", "LudotsCoreMod", "assets", "Relationships", "catalog.json");
            if (File.Exists(engineCatalog) && File.Exists(coreModCatalog))
            {
                yield return engineCatalog;
                yield return coreModCatalog;
                yield break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"从 {seedDir} 往上找不到 assets/Relationships/catalog.json 和 mods/LudotsCoreMod 的关系目录。");
    }

    /// <summary>Core 的 assets 目录：从 standardDir 往上找同时有 mods/ 和 assets/Navigation/navmesh.json 的仓库根。</summary>
    private static string CoreAssetsDir(string standardDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(standardDir));
        while (dir != null)
        {
            string assets = Path.Combine(dir.FullName, "assets");
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) && File.Exists(Path.Combine(assets, "Navigation", "navmesh.json")))
            {
                return assets;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"从 {standardDir} 往上找不到带 assets/Navigation/navmesh.json 的仓库根。");
    }

    private static SeedBundle Load(string seedDir, string standardDir, string demoDir)
    {
        string mapId = Directory.GetFiles(Path.Combine(seedDir, "Maps"), "*.json").Single()
            .Let(p => Path.GetFileNameWithoutExtension(p));

        var merged = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(standardDir, "CrowdSimulationConfig.json")))!;
        ConfigPipeline.DeepMerge(merged, (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(demoDir, "CrowdSimulationConfig.json")))!);
        var overlay = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(seedDir, "CrowdSimulationConfig.json")))!;
        ConfigPipeline.DeepMerge(merged, overlay);
        var config = CrowdSimulationConfig.Load(merged);

        var map = JsonSerializer.Deserialize<MapConfig>(
            File.ReadAllText(Path.Combine(seedDir, "Maps", $"{mapId}.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var templates = JsonSerializer.Deserialize<List<EntityTemplate>>(
            File.ReadAllText(Path.Combine(seedDir, "Entities", "templates.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var templatesById = templates.Where(t => !string.IsNullOrWhiteSpace(t.Id)).ToDictionary(t => t.Id, StringComparer.Ordinal);

        var profileList = JsonSerializer.Deserialize<List<AgentProfileConfig>>(
            File.ReadAllText(Path.Combine(standardDir, "Navigation", "agent_profiles.json")),
            StrictJsonOptions.CreateCamelCase())!;
        var profiles = new AgentProfileRegistry(profileList);
        var navmeshJson = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(CoreAssetsDir(standardDir), "Navigation", "navmesh.json")))!;
        string seedNavPath = Path.Combine(seedDir, "Navigation", "navmesh.json");
        if (File.Exists(seedNavPath))
        {
            ConfigPipeline.DeepMerge(navmeshJson, (JsonObject)JsonNode.Parse(File.ReadAllText(seedNavPath))!);
        }

        var traversalJson = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(standardDir, "CrowdSimulation", "traversal.json")))!;
        ConfigPipeline.DeepMerge(traversalJson, (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(demoDir, "CrowdSimulation", "traversal.json")))!);
        var traversal = CrowdTraversalConfig.Parse(traversalJson);
        var unitTemplates = JsonSerializer.Deserialize<List<EntityTemplate>>(
            File.ReadAllText(Path.Combine(demoDir, "Entities", "templates.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var bake = NavMeshBakeConfigLoader.Parse(navmeshJson, profiles);
        var runtime = CrowdSimulationConfigLoader.Load(
            config,
            map,
            profiles,
            bake,
            traversal,
            CrowdUnitTemplateFacts.FromTemplates(unitTemplates),
            ReadSeedFixedHz(seedDir),
            ProjectSeedMap(seedDir, config, map));

        string surfacePath = Path.Combine(seedDir, "terrain", $"{mapId}.navsurface");
        string heightPath = Path.Combine(seedDir, "terrain", $"{mapId}.height");
        var surface = NavSurfaceAsset.Read(File.OpenRead(surfacePath));
        var mapSurface = CrowdSimulationMapSurfaceSource.Extract(map, templatesById);
        var height = ContinuousHeightmapBinary.Read(File.OpenRead(heightPath));
        var board = NavSolverDeclaration.RequireBoard(map, NavSolverDeclaration.RequireMap(bake, map.Id));
        var ingested = CrowdSimulationWorldIngest.Build(
            runtime,
            board.WidthCm,
            board.HeightCm,
            surface,
            surfacePath,
            height,
            heightPath,
            mapSurface.Blockers);

        return new SeedBundle(mapId, runtime, surface, height, mapSurface, ingested.Grid, ingested.Heights, TypeColorsOf(standardDir, runtime));
    }

    private static (int R, int G, int B)[] TypeColorsOf(string standardDir, CrowdSimulationRuntimeConfig runtime)
    {
        string path = Path.Combine(standardDir, CrowdTerrainTypeColors.RelativePath);
        CrowdTerrainTypeColorTable table = CrowdTerrainTypeColors.Load(JsonNode.Parse(File.ReadAllText(path))!.AsObject());
        table.RequireCovers(runtime.TerrainTypeIds, runtime.MapId);
        var colors = new (int R, int G, int B)[runtime.TerrainTypeIds.Count];
        for (int i = 0; i < colors.Length; i++)
        {
            CrowdTerrainTypeColor color = table.Colors[runtime.TerrainTypeIds[i]];
            colors[i] = (color.Red, color.Green, color.Blue);
        }

        return colors;
    }

    private static void RenderTypes(NavSurfaceAsset surface, ContinuousHeightmapAsset height, (int R, int G, int B)[] typeColors, string path)
    {
        var px = BaseLayer(surface, height, typeColors);
        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    private static void RenderBlocked(NavSurfaceAsset surface, ContinuousHeightmapAsset height, (int R, int G, int B)[] typeColors, SurfaceGrid grid, string path)
    {
        var px = BaseLayer(surface, height, typeColors);
        int navCellsPerSide = grid.NavCellsPerSide;
        for (int cy = 0; cy < navCellsPerSide; cy++)
        {
            for (int cx = 0; cx < navCellsPerSide; cx++)
            {
                if (grid.Blocked[cy * navCellsPerSide + cx] == 0) continue;
                FillCell(px, cx, cy, navCellsPerSide, (211, 47, 47), blend: 0.55f);
            }
        }

        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    private static void RenderFull(
        NavSurfaceAsset surface,
        ContinuousHeightmapAsset height,
        (int R, int G, int B)[] typeColors,
        SurfaceGrid grid,
        CrowdSimulationMapSurface mapSurface,
        CrowdSimulationRuntimeConfig runtime,
        string path)
    {
        var px = BaseLayer(surface, height, typeColors);
        int navCellsPerSide = grid.NavCellsPerSide;

        // 阻挡格
        for (int cy = 0; cy < navCellsPerSide; cy++)
        {
            for (int cx = 0; cx < navCellsPerSide; cx++)
            {
                if (grid.Blocked[cy * navCellsPerSide + cx] != 0) FillCell(px, cx, cy, navCellsPerSide, (211, 47, 47), 0.55f);
            }
        }

        // 桥面(路径足迹,带宽)
        float worldCm = (float)navCellsPerSide * grid.NavCellSizeCm;
        foreach (var b in mapSurface.LayeredPaths)
        {
            RasterizePath(px, b.Span.FromXCm, b.Span.FromYCm, b.Span.ToXCm, b.Span.ToYCm, b.Span.WidthCm, worldCm, (150, 110, 70));
        }

        // 跳跃候选:双向(落差 ≤ 25 m)靛蓝,单向琥珀
        foreach (var j in surface.JumpCandidates)
        {
            float cs = grid.NavCellSizeCm;
            var color = j.DropCm <= 2500 ? (92, 107, 192) : (255, 179, 0);
            DrawLine(px, (j.FromX + 0.5f) * cs, (j.FromY + 0.5f) * cs, (j.ToX + 0.5f) * cs, (j.ToY + 0.5f) * cs, worldCm, color);
        }

        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    /// <summary>地形类型着色 + 轻度坡面晕渲(类型颜色为主信号,明暗只作辅助)。底色读 Presentation/terrain_type_colors.json。</summary>
    private static byte[] BaseLayer(NavSurfaceAsset surface, ContinuousHeightmapAsset height, (int R, int G, int B)[] typeColors)
    {
        var px = new byte[RenderSize * RenderSize * 3];
        int n = surface.NavCellsX;
        int b = height.SampleColumns;
        float[] heights = DecodeHeights(height);

        for (int y = 0; y < RenderSize; y++)
        {
            for (int x = 0; x < RenderSize; x++)
            {
                int cx = Math.Min(n - 1, x * n / RenderSize);
                int cy = Math.Min(n - 1, y * n / RenderSize);
                int type = surface.TerrainCells[cy * n + cx];

                int hx = Math.Min(b - 1, x * b / RenderSize);
                int hy = Math.Min(b - 1, y * b / RenderSize);

                if ((uint)type >= (uint)typeColors.Length)
                {
                    throw new InvalidOperationException($"地形类型编号 {type} 超出颜色表 {typeColors.Length}。");
                }

                var (r, g, bb) = typeColors[type];

                // 晕渲:东向坡亮、西向坡暗(压低强度,保住类型颜色)
                int xl = Math.Max(0, hx - 1), xr = Math.Min(b - 1, hx + 1);
                int yu = Math.Max(0, hy - 1), yd = Math.Min(b - 1, hy + 1);
                float gx = heights[hy * b + xr] - heights[hy * b + xl];
                float gy = heights[yd * b + hx] - heights[yu * b + hx];
                float shade = Math.Clamp(1f - (gx + gy) * 0.00005f, 0.92f, 1.08f);

                int i = (y * RenderSize + x) * 3;
                px[i] = (byte)Math.Clamp((int)(r * shade), 0, 255);
                px[i + 1] = (byte)Math.Clamp((int)(g * shade), 0, 255);
                px[i + 2] = (byte)Math.Clamp((int)(bb * shade), 0, 255);
            }
        }

        return px;
    }

    private static float[] DecodeHeights(ContinuousHeightmapAsset height)
    {
        int count = height.SampleColumns * height.SampleRows;
        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = height.SampleScale.Decode(height.HeightSamplesRaw[i]);
        }

        return result;
    }

    private static void FillCell(byte[] px, int cx, int cy, int n, (int r, int g, int b) color, float blend)
    {
        int x0 = cx * RenderSize / n, x1 = (cx + 1) * RenderSize / n;
        int y0 = cy * RenderSize / n, y1 = (cy + 1) * RenderSize / n;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = (y * RenderSize + x) * 3;
                px[i] = (byte)(px[i] * (1 - blend) + color.r * blend);
                px[i + 1] = (byte)(px[i + 1] * (1 - blend) + color.g * blend);
                px[i + 2] = (byte)(px[i + 2] * (1 - blend) + color.b * blend);
            }
        }
    }

    private static void RasterizePath(byte[] px, float x0, float y0, float x1, float y1, float widthCm, float worldCm, (int r, int g, int b) color)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float half = widthCm / 2f;
        int steps = Math.Max(1, (int)(len / (worldCm / RenderSize) * 2));
        float radiusPx = half / worldCm * RenderSize;
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            float pxC = (x0 + dx * t) / worldCm * RenderSize;
            float pyC = (y0 + dy * t) / worldCm * RenderSize;
            FillDisc(px, pxC, pyC, Math.Max(1.5f, radiusPx), color);
        }
    }

    private static void DrawLine(byte[] px, float x0, float y0, float x1, float y1, float worldCm, (int r, int g, int b) color)
    {
        float dx = x1 - x0, dy = y1 - y0;
        int steps = Math.Max(1, (int)(MathF.Sqrt(dx * dx + dy * dy) / worldCm * RenderSize * 2));
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            FillDisc(px, (x0 + dx * t) / worldCm * RenderSize, (y0 + dy * t) / worldCm * RenderSize, 1.2f, color);
        }
    }

    private static void FillDisc(byte[] px, float cx, float cy, float radiusPx, (int r, int g, int b) color)
    {
        int x0 = Math.Max(0, (int)(cx - radiusPx)), x1 = Math.Min(RenderSize - 1, (int)(cx + radiusPx));
        int y0 = Math.Max(0, (int)(cy - radiusPx)), y1 = Math.Min(RenderSize - 1, (int)(cy + radiusPx));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int i = (y * RenderSize + x) * 3;
                px[i] = (byte)color.r;
                px[i + 1] = (byte)color.g;
                px[i + 2] = (byte)color.b;
            }
        }
    }
}

internal static class ProgramExtensions
{
    public static T Let<T>(this T value, Func<T, T> fn) => fn(value);
}
