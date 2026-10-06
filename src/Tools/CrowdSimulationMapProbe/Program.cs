using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.World;
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

        var seen = new HashSet<int>();
        for (int a = 0; a < bundle.Runtime.AgentTypes.Count; a++)
        {
            foreach (int clearance in bundle.Runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
            {
                var nav = NavContextBaker.Bake(bundle.Runtime, bundle.Grid, heights, bundle.MapSurface.Bridges, a, clearance);
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

    private sealed record SeedBundle(
        string MapId,
        CrowdSimulationRuntimeConfig Runtime,
        NavSurfaceAsset Surface,
        ContinuousHeightmapAsset Height,
        CrowdSimulationMapSurface MapSurface,
        SurfaceGrid Grid);

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
