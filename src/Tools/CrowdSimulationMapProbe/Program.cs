using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation.Terrain;

namespace CrowdSimulationMapProbe;

/// <summary>
/// S1 地图探针：用真实 Ludots 装载路径（CrowdSimulationConfig 合并、.navsurface 读取、
/// 地图实体提取、.height 读取）渲染三张种子图的地形类型 / 阻挡格 / 桥梁 / 跳跃候选。
/// 用法: CrowdSimulationMapProbe &lt;modAssetsDir&gt; &lt;capabilityAssetsDir&gt; &lt;outDir&gt;
/// </summary>
public static class Program
{
    private const int RenderSize = 1024;

    // 地形类型着色用水/岸/陆/山/悬崖各自一眼可分的颜色(与参考实现区域着色同色系)
    private static readonly (int r, int g, int b)[] TypeColors =
        { (70, 110, 170), (200, 184, 130), (150, 170, 110), (150, 120, 90), (60, 50, 44) };

    public static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--s2")
        {
            // --s2 <seedAssetsDir> <capabilityAssetsDir> <outDir>
            return RunS2(args[1], args[2], args[3]);
        }

        if (args.Length >= 1 && args[0] == "--s3")
        {
            // --s3 <seedAssetsDir> <capabilityAssetsDir> <outDir>
            return RunS3(args[1], args[2], args[3]);
        }

        if (args.Length >= 1 && args[0] == "--s3c")
        {
            // --s3c <seedAssetsDir> <capabilityAssetsDir> <outDir>
            return RunS3C(args[1], args[2], args[3]);
        }

        if (args.Length >= 1 && args[0] == "--s4probe")
        {
            // --s4probe <seedAssetsDir> <capabilityAssetsDir> <outDir> — 部署前 8 单位与真值比对
            return RunS4Probe(args[1], args[2]);
        }

        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: CrowdSimulationMapProbe <seedAssetsDir> <capabilityAssetsDir> <outDir>");
            return 2;
        }

        string seedDir = args[0];
        string capabilityDir = args[1];
        string outDir = args[2];
        Directory.CreateDirectory(outDir);

        var bundle = Load(seedDir, capabilityDir);
        RenderTypes(bundle.Surface, bundle.Height, Path.Combine(outDir, $"{bundle.MapId}_types.png"));
        RenderBlocked(bundle.Surface, bundle.Height, bundle.Grid, Path.Combine(outDir, $"{bundle.MapId}_blocked.png"));
        RenderFull(bundle.Surface, bundle.Height, bundle.Grid, bundle.MapSurface, bundle.Runtime, Path.Combine(outDir, $"{bundle.MapId}_full.png"));

        Console.WriteLine($"[probe] {bundle.MapId}: cells={bundle.Grid.CellCount}x{bundle.Grid.CellCount} blockers={bundle.MapSurface.Blockers.Count} bridges={bundle.MapSurface.Bridges.Count} jumps={bundle.Surface.JumpCandidates.Length}");
        return 0;
    }

    /// <summary>S2:逐代理体型的可走区域视图(不可走 = 红色遮罩;桥 portal 高亮;跳跃链接叠画)。</summary>
    private static int RunS2(string seedDir, string capabilityDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var bundle = Load(seedDir, capabilityDir);
        var heights = NavHeightField.FromHeightmap(bundle.Height, bundle.Runtime.NavCellCount, bundle.Runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(bundle.MapSurface.Bridges, bundle.Runtime);

        var seen = new HashSet<int>();
        for (int a = 0; a < bundle.Runtime.AgentTypes.Count; a++)
        {
            foreach (int clearance in bundle.Runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
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
        var px = BaseLayer(bundle.Surface, bundle.Height);
        int n = nav.CellCount;
        // 不可走遮罩
        for (int cy = 0; cy < n; cy++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                if (nav.Passable[cy * n + cx] == 0) FillCell(px, cx, cy, n, (160, 30, 30), 0.5f);
            }
        }

        // 桥面可走格与 portal
        for (int cy = 0; cy < n; cy++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                if (nav.Portal[cy * n + cx] != 0) FillCell(px, cx, cy, n, (255, 213, 79), 0.9f);
                else if (nav.UpPass[cy * n + cx] != 0) FillCell(px, cx, cy, n, (150, 110, 70), 0.85f);
            }
        }

        // 跳跃链接:双向靛蓝 / 单向琥珀
        if (nav.Links != null)
        {
            float cs = bundle.Runtime.NavCellSizeCm;
            float worldCm = n * cs;
            for (int e = 0; e < nav.Links.Count; e++)
            {
                int from = nav.Links.From[e], to = nav.Links.To[e];
                var color = nav.Links.TwoWay[e] != 0 ? (92, 107, 192) : (255, 179, 0);
                DrawLine(px,
                    (from % n + 0.5f) * cs, ((from / n) + 0.5f) * cs,
                    (to % n + 0.5f) * cs, ((to / n) + 0.5f) * cs,
                    worldCm, color);
            }
        }

        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    /// <summary>S3:NavMesh 多边形网格视图(地面 tile 白色描边,桥面 tile 棕色)。</summary>
    private static int RunS3(string seedDir, string capabilityDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var bundle = Load(seedDir, capabilityDir);
        var runtime = bundle.Runtime;
        var heights = NavHeightField.FromHeightmap(bundle.Height, runtime.NavCellCount, runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(bundle.MapSurface.Bridges, runtime);
        int n = runtime.NavCellCount, t = runtime.Hpa.ClusterSize, c = n / t;

        // 步兵上下文(含桥面),缓存随烘焙填充
        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, t,
            runtime.Navmesh.MinRegionArea.ToDouble(), runtime.Navmesh.MaxSimplificationError.ToDouble(),
            runtime.Navmesh.MaxEdgeLen.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);
        var nav = NavContextBaker.Bake(runtime, bundle.Grid, heights, deck, 0, 1, cache);

        var px = BaseLayer(bundle.Surface, bundle.Height);
        float cellPx = (float)RenderSize / n;
        float csCm = runtime.NavCellSizeCm;

        for (int ty = 0; ty < c; ty++)
        {
            for (int tx = 0; tx < c; tx++)
            {
                DrawTileEntry(px, cache, nav.Passable, bundle.Grid.Area, n, tx, ty, t, (230, 230, 230), cellPx, csCm);
            }
        }

        foreach (int tileId in deck.TileOrder)
        {
            bool any = false;
            int tx = tileId % c, ty = tileId / c;
            for (int y = ty * t; y < ty * t + t && !any; y++)
                for (int x = tx * t; x < tx * t + t; x++)
                    if (nav.UpPass[y * n + x] != 0) { any = true; break; }
            if (any) DrawTileEntry(px, cache, nav.UpPass, nav.UpArea, n, tx, ty, t, (255, 213, 79), cellPx, csCm);
        }

        // HPA* 分区:cluster 网格 + 入口节点(抽象图形状)
        var hpa = HpaGraph.Build(nav, runtime);
        var flat = hpa.Flatten();
        float worldCm2 = n * csCm;
        for (int i = 0; i < flat.NodeCell.Length; i++)
        {
            int cell = flat.NodeCell[i];
            float cx = (cell % n + 0.5f) * csCm / worldCm2 * RenderSize;
            float cy = ((cell / n) + 0.5f) * csCm / worldCm2 * RenderSize;
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
    private static int RunS3C(string seedDir, string capabilityDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var bundle = Load(seedDir, capabilityDir);
        var runtime = bundle.Runtime;
        var heights = NavHeightField.FromHeightmap(bundle.Height, runtime.NavCellCount, runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(bundle.MapSurface.Bridges, runtime);
        int n = runtime.NavCellCount, t = runtime.Hpa.ClusterSize;

        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(seedDir, "CrowdSimulation", "parity", "s3c-query-truth.json")))!;
        var pairs = truth["pairs"]!.AsArray().Select(p => (Start: p![0]!.GetValue<int>(), Goal: p[1]!.GetValue<int>())).ToArray();

        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, t,
            runtime.Navmesh.MinRegionArea.ToDouble(), runtime.Navmesh.MaxSimplificationError.ToDouble(),
            runtime.Navmesh.MaxEdgeLen.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        // 逐移动类型(去重上下文):0 = 步兵(地面,含桥面),最后一个 = 跳跃层
        var agentIndices = new[] { 0, runtime.AgentTypes.Count - 1 };
        foreach (int a in agentIndices)
        {
            var nav = NavContextBaker.Bake(runtime, bundle.Grid, heights, deck, a, 1, cache);
            string typeId = runtime.AgentTypes[a].Id;
            var scratch = new CorridorQuery.Scratch(t * t);
            var pool = new FlowPool(n, runtime.Flowfield.PoolCapacity);
            int shown = 0;
            foreach (var (start, goal) in pairs)
            {
                if (shown >= (a == 0 ? 6 : 2)) break;
                var mask = new byte[(n / t) * (n / t)];
                mask[NavGridSteps.ClusterOf(goal, n, t, n / t)] = 1;
                var corridor = CorridorQuery.CorridorTo(nav, runtime, start, goal, mask, 0, scratch);
                var flow = FlowFieldBuilder.Build(nav, goal, FlowFieldBuilder.PadMask(mask, n / t, runtime.Flowfield.CorridorPadding), pool);
                var trace = FlowFieldBuilder.TracePath(flow, start, n * n);
                if (trace[trace.Count - 1] != goal && corridor.Points == null) { pool.Give(flow); continue; }

                var pts = corridor.Points != null && nav.Links == null
                    ? corridor.Points
                    : CorridorQuery.FlowPoints(flow, start)?.Points;
                RenderPath(bundle, nav, flow, trace, pts, start, goal,
                    Path.Combine(outDir, $"{bundle.MapId}_s3c_{typeId}_p{shown}.png"));
                shown++;
                pool.Give(flow);
            }

            Console.WriteLine($"[probe:s3c] {bundle.MapId} {typeId}: 路径视图 {shown} 张");
        }

        return 0;
    }

    private static void RenderPath(SeedBundle bundle, NavContext nav, FlowField flow, List<int> trace, Fix64[]? pts, int start, int goal, string path)
    {
        var px = BaseLayer(bundle.Surface, bundle.Height);
        int n = nav.CellCount, n2 = n * n;
        float worldCm = n * bundle.Runtime.NavCellSizeCm;

        // 方向箭头:每 6 格抽样,从格心指向路点格心
        for (int cy = 2; cy < n - 2; cy += 6)
        {
            for (int cx = 2; cx < n - 2; cx += 6)
            {
                int u = cy * n + cx;
                if (flow.Integ[u] >= Fix64.MaxValue / 4) continue;
                int w = flow.Wp[u];
                if (w < 0) continue;
                int wc = w % n2;
                float cs = bundle.Runtime.NavCellSizeCm;
                float x0 = (cx + 0.5f) * cs, y0 = (cy + 0.5f) * cs;
                float x1 = (wc % n + 0.5f) * cs, y1 = (wc / n + 0.5f) * cs;
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
            DrawLine(px, (a % n + 0.5f) * csCm, (a / n + 0.5f) * csCm, (b % n + 0.5f) * csCm, (b / n + 0.5f) * csCm, worldCm, (64, 196, 255));
        }

        // 折线(查询结果)品红
        if (pts != null)
        {
            for (int k = 2; k < pts.Length; k += 2)
            {
                DrawLine(px,
                    (float)pts[k - 2].ToDouble() * csCm, (float)pts[k - 1].ToDouble() * csCm,
                    (float)pts[k].ToDouble() * csCm, (float)pts[k + 1].ToDouble() * csCm,
                    worldCm, (255, 64, 200));
            }
        }

        // 起点绿 / 终点红
        FillDisc(px, (start % n + 0.5f) * csCm / worldCm * RenderSize, (start / n + 0.5f) * csCm / worldCm * RenderSize, 5, (76, 217, 100));
        FillDisc(px, (goal % n + 0.5f) * csCm / worldCm * RenderSize, (goal / n + 0.5f) * csCm / worldCm * RenderSize, 5, (255, 69, 58));
        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    /// <summary>S4 部署探针:跑 spawn 12000,前 8 个单位与 s4-units.bin 逐字段比对。</summary>
    private static int RunS4Probe(string seedDir, string capabilityDir)
    {
        var bundle = Load(seedDir, capabilityDir);
        var runtime = bundle.Runtime;
        var heights = NavHeightField.FromHeightmap(bundle.Height, runtime.NavCellCount, runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(bundle.MapSurface.Bridges, runtime);
        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, runtime.Hpa.ClusterSize,
            runtime.Navmesh.MinRegionArea.ToDouble(), runtime.Navmesh.MaxSimplificationError.ToDouble(),
            runtime.Navmesh.MaxEdgeLen.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        var navs = new Dictionary<int, NavContext>();
        var navByLayerRadius = new Dictionary<(int, int), NavContext>();
        var radiusClasses = CrowdDeployment.DistinctRadiusClasses(runtime);
        var seen = new HashSet<int>();
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            foreach (var clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
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
        var session = new CrowdSimSession(runtime, probeWorld, navs, navByLayerRadius, BuildWiring(runtime, probeWorld, capabilityDir));
        // 复刻 spawn 的组分配(不生成),打印组清单与中心供对照
        var dbgRng = CrowdSimRng.Create((long)runtime.WorldSeed * 31 + 1L * 7919);
        int n0 = runtime.NavCellCount;
        var dbgTypes = new List<int>();
        for (int t = 0; t < runtime.UnitTypes.Count; t++) if (!runtime.UnitTypes[t].Special) dbgTypes.Add(t);
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
                    var nav = session.NavFor(l, r);
                    if (nav.Cells.Length == 0) continue;
                    var cs = new List<int>();
                    for (int k = 0; k < runtime.Deploy.CentersPerGroup; k++)
                    {
                        int x = Math.Min(n0 - 1, Math.Max(0, bx + CrowdDeployment.DetRound((dbgRng.Next() * 2 - Fix64.OneValue) * runtime.Deploy.BaseJitterCells)));
                        int y = Math.Min(n0 - 1, Math.Max(0, by + CrowdDeployment.DetRound((dbgRng.Next() * 2 - Fix64.OneValue) * runtime.Deploy.BaseJitterCells)));
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
                $"x={(pos.X.RawValue / 4294967296.0 / 100):F9} y={(pos.Y.RawValue / 4294967296.0 / 100):F9}) " +
                $"rawEq=({pos.X.RawValue == (long)(xm * 100 * 4294967296.0)},{pos.Y.RawValue == (long)(ym * 100 * 4294967296.0)})");
        }

        return 0;
    }

    private static void DrawTileEntry(byte[] px, NavTileCache cache, byte[] passable, byte[] area, int n, int tx, int ty, int tileCells, (int r, int g, int b) color, float cellPx, float csCm)
    {
        var entry = cache.Acquire(passable, area, n, tx, ty);
        for (int p = 0; p < entry.Count; p++)
        {
            int s = entry.PolyStart[p], e = entry.PolyStart[p + 1];
            for (int k = s; k < e; k++)
            {
                int a = entry.PolyVerts[k], b = entry.PolyVerts[k + 1 < e ? k + 1 : s];
                float x0 = (tx * tileCells + entry.Vx[a]) * csCm / 100f;
                float y0 = (ty * tileCells + entry.Vy[a]) * csCm / 100f;
                float x1 = (tx * tileCells + entry.Vx[b]) * csCm / 100f;
                float y1 = (ty * tileCells + entry.Vy[b]) * csCm / 100f;
                float worldCm = n * csCm;
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
        SurfaceGrid Grid);

    /// <summary>单位创建单路径的无头装配:与引擎同一装载链(VFS 挂载能力 mod 资产 →
    /// ConfigPipeline → MapLoader.LoadTemplates)取模板注册表与模板键;选中属主置空,不镜像。</summary>
    private static CrowdSimPresentationWiring BuildWiring(
        CrowdSimulationRuntimeConfig runtime, Arch.Core.World world, string capabilityDir)
    {
        var vfs = new Ludots.Core.Modding.VirtualFileSystem();
        vfs.Mount("Core", capabilityDir);
        var pipeline = new Ludots.Core.Config.ConfigPipeline(vfs, modLoader: null!);
        var mapLoader = new Ludots.Core.Systems.MapLoader(world, new Ludots.Core.Map.WorldMap(), pipeline);
        mapLoader.LoadTemplates(Ludots.Core.Config.ConfigCatalogLoader.Load(pipeline));
        var collections = new Ludots.Core.EntityCollections.EntityCollectionStore(
            new Ludots.Core.Registry.StringIntRegistry());
        return new CrowdSimPresentationWiring
        {
            StableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator(),
            TemplateRegistry = mapLoader.TemplateRegistry,
            TemplatesByUnitTypeRadius = CrowdSimPresentationWiring.BuildUnitTypeTemplates(
                runtime, mapLoader.TemplateRegistry, mapLoader.EntityTemplateKeys,
                new AgentProfileRegistry(JsonSerializer.Deserialize<List<AgentProfileConfig>>(
                    File.ReadAllText(Path.Combine(capabilityDir, "Navigation", "agent_profiles.json")),
                    StrictJsonOptions.CreateCamelCase())!),
                "MapProbe 装配"),
            RadiusMetersBlackboardKeyId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(
                Ludots.Core.CrowdSimulation.Runtime.CrowdSimulationRuntime.Keys.UnitRadiusM),
            SelectionOwner = Arch.Core.Entity.Null,
            SelectedCollectionKeyId = collections.KeyRegistry.Register(SelectionMirror.CollectionKey),
            Collections = collections,
        };
    }

    private static SeedBundle Load(string seedDir, string capabilityDir)
    {
        string mapId = Directory.GetFiles(Path.Combine(seedDir, "Maps"), "*.json").Single()
            .Let(p => Path.GetFileNameWithoutExtension(p));

        var merged = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(capabilityDir, "CrowdSimulationConfig.json")))!;
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
            File.ReadAllText(Path.Combine(capabilityDir, "Navigation", "agent_profiles.json")),
            StrictJsonOptions.CreateCamelCase())!;
        var runtime = CrowdSimulationConfigLoader.Load(config, map, new AgentProfileRegistry(profileList), fixedHz: 30);

        var surface = NavSurfaceAsset.Read(File.OpenRead(Path.Combine(seedDir, "terrain", $"{mapId}.navsurface")));
        var mapSurface = CrowdSimulationMapSurfaceSource.Extract(map, templatesById);
        var grid = SurfaceGrid.Build(runtime, surface, mapSurface.Blockers);
        var height = ContinuousHeightmapBinary.Read(File.OpenRead(Path.Combine(seedDir, "terrain", $"{mapId}.height")));

        return new SeedBundle(mapId, runtime, surface, height, mapSurface, grid);
    }

    private static void RenderTypes(NavSurfaceAsset surface, ContinuousHeightmapAsset height, string path)
    {
        var px = BaseLayer(surface, height);
        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    private static void RenderBlocked(NavSurfaceAsset surface, ContinuousHeightmapAsset height, SurfaceGrid grid, string path)
    {
        var px = BaseLayer(surface, height);
        int n = grid.CellCount;
        for (int cy = 0; cy < n; cy++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                if (grid.Blocked[cy * n + cx] == 0) continue;
                FillCell(px, cx, cy, n, (211, 47, 47), blend: 0.55f);
            }
        }

        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    private static void RenderFull(
        NavSurfaceAsset surface,
        ContinuousHeightmapAsset height,
        SurfaceGrid grid,
        CrowdSimulationMapSurface mapSurface,
        CrowdSimulationRuntimeConfig runtime,
        string path)
    {
        var px = BaseLayer(surface, height);
        int n = grid.CellCount;

        // 阻挡格
        for (int cy = 0; cy < n; cy++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                if (grid.Blocked[cy * n + cx] != 0) FillCell(px, cx, cy, n, (211, 47, 47), 0.55f);
            }
        }

        // 桥面(路径足迹,带宽)
        float worldCm = (float)n * grid.CellSizeCm;
        foreach (var b in mapSurface.Bridges)
        {
            RasterizePath(px, b.Span.X0Cm, b.Span.Y0Cm, b.Span.X1Cm, b.Span.Y1Cm, b.Span.WidthCm, worldCm, (150, 110, 70));
        }

        // 跳跃候选:双向(落差 ≤ 25 m)靛蓝,单向琥珀
        foreach (var j in surface.JumpCandidates)
        {
            float cs = grid.CellSizeCm;
            var color = j.DropCm <= 2500 ? (92, 107, 192) : (255, 179, 0);
            DrawLine(px, (j.FromX + 0.5f) * cs, (j.FromY + 0.5f) * cs, (j.ToX + 0.5f) * cs, (j.ToY + 0.5f) * cs, worldCm, color);
        }

        PngWriter.Write(path, RenderSize, RenderSize, px);
    }

    /// <summary>地形类型着色 + 轻度坡面晕渲(类型颜色为主信号,明暗只作辅助)。</summary>
    private static byte[] BaseLayer(NavSurfaceAsset surface, ContinuousHeightmapAsset height)
    {
        var px = new byte[RenderSize * RenderSize * 3];
        int n = surface.CellsX;
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

                var (r, g, bb) = TypeColors[type];

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
