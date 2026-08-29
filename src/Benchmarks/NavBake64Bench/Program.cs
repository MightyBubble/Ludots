using System.Diagnostics;
using System.Text;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Spatial;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Platform.Abstractions;

// #1355 时间盒基准切片:东亚 .height 连续高度图 64km 板全板 nav 烘焙外推锚点。
// 用法: NavBake64Bench <path-to-east_asia_continuous.height> [--repeats N] [--world-width-cm N]
// 产出: 控制台表格 + artifacts/benchmarks/nav-bake-64km-slice.md

string heightPath = args.Length > 0 && !args[0].StartsWith("--")
    ? args[0]
    : throw new ArgumentException("pass the east_asia_continuous.height path as the first argument");
int repeats = 3;
long worldWidthCm = 6_399_232; // east_asia_visual_heightmap.json VisualHeightmap.WorldWidthCm
string feedName = NavBakeNames.TerrainFeedTriangles;
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--repeats")
    {
        repeats = int.TryParse(args[i + 1], out int parsedRepeats)
            ? Math.Clamp(parsedRepeats, 1, 20)
            : throw new ArgumentException($"--repeats expects an integer, got '{args[i + 1]}'.");
    }

    if (args[i] == "--world-width-cm")
    {
        worldWidthCm = long.TryParse(args[i + 1], out long parsedWidth)
            ? parsedWidth
            : throw new ArgumentException($"--world-width-cm expects an integer, got '{args[i + 1]}'.");
    }

    if (args[i] == "--feed")
    {
        feedName = args[i + 1];
    }
}

NavTerrainFeedKind feed = NavBakeNames.ParseTerrainFeed(feedName, "--feed");

const int ChunkCells = SpatialScaleDefaults.TerrainChunkCells; // 64
const int MiniChunksPerSide = 3;
const int MiniCells = ChunkCells * MiniChunksPerSide;
const byte MaxHeightLevel = (byte)SpatialScaleDefaults.LogicTerrainMaxHeightLevel;
const float HeightScaleMeters = 1.0f; // east_asia_navmesh_debug navmesh.json runtimeIncremental.heightScaleMeters

using var stream = File.OpenRead(heightPath);
var asset = VisualHeightmapBinary.Read(stream);
short[] samples = asset.HeightSamplesCm.Length > 0 ? asset.HeightSamplesCm : throw new InvalidDataException("expected Int16-centimeter sample layout");
int sampleCols = asset.SampleColumns;
int sampleRows = asset.SampleRows;
float sourceWidthCm = asset.Bounds.Width;
float sourceHeightCm = asset.Bounds.Height;
long worldHeightCm = (long)Math.Round(worldWidthCm * sourceHeightCm / sourceWidthCm);

float SampleAt(float u, float v)
{
    float fx = u * (sampleCols - 1);
    float fz = v * (sampleRows - 1);
    int x0 = (int)fx;
    int z0 = (int)fz;
    int x1 = Math.Min(x0 + 1, sampleCols - 1);
    int z1 = Math.Min(z0 + 1, sampleRows - 1);
    float tx = fx - x0;
    float tz = fz - z0;
    float h00 = samples[z0 * sampleCols + x0];
    float h10 = samples[z0 * sampleCols + x1];
    float h01 = samples[z1 * sampleCols + x0];
    float h11 = samples[z1 * sampleCols + x1];
    return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
}

float minH = short.MaxValue;
float maxH = short.MinValue;
for (int i = 0; i < samples.Length; i += 7)
{
    short h = samples[i];
    if (h < minH) minH = h;
    if (h > maxH) maxH = h;
}

var report = new StringBuilder();
report.AppendLine("# Nav 烘焙 64km 板直灌基准切片(#1355)");
report.AppendLine();
report.AppendLine($"- 资产: `{Path.GetFileName(heightPath)}` VHTM Int16cm,源采样 {sampleCols}×{sampleRows}(源幅 {sourceWidthCm / 100000:F1}km × {sourceHeightCm / 100000:F1}km,{sourceWidthCm / (sampleCols - 1) / 100:F1}m/采样)");
report.AppendLine($"- 烘焙世界: {worldWidthCm / 100000:F0}km × {worldHeightCm / 100000:F0}km(横向压缩 {sourceWidthCm / worldWidthCm:F0}×);高度范围 {minH:F0}..{maxH:F0}cm;{Environment.ProcessorCount} 逻辑核;.NET {Environment.Version}");
report.AppendLine($"- 口径: 迷你场 3×3 块({MiniCells}×{MiniCells} 格)烤中心瓦片;Small 单 profile(30cm/40cm/45°);heightScaleMeters={HeightScaleMeters} 对齐 east_asia_navmesh_debug 实配;每格 {repeats} 轮取中位;无障碍");
report.AppendLine();

// ---- 探区:6×4 宏块按方差/均值挑 relief / plains / sea(世界坐标 = 源坐标等比映射,探区选样在源上做) ----
const int bx = 6, bz = 4;
var stats = new (float Mean, float Var)[bx, bz];
for (int ix = 0; ix < bx; ix++)
{
    for (int iz = 0; iz < bz; iz++)
    {
        float u0 = (ix + 0.3f) / bx, u1 = (ix + 0.7f) / bx;
        float v0 = (iz + 0.3f) / bz, v1 = (iz + 0.7f) / bz;
        double sum = 0, sumSq = 0;
        int n = 0;
        for (float u = u0; u <= u1; u += (u1 - u0) / 24)
        {
            for (float v = v0; v <= v1; v += (v1 - v0) / 24)
            {
                float h = SampleAt(u, v);
                sum += h;
                sumSq += (double)h * h;
                n++;
            }
        }

        double mean = sum / n;
        stats[ix, iz] = ((float)mean, (float)(sumSq / n - mean * mean));
    }
}

(int Rx, int Rz) relief = (0, 0), plains = (0, 0), sea = (0, 0);
for (int ix = 0; ix < bx; ix++)
{
    for (int iz = 0; iz < bz; iz++)
    {
        if (stats[ix, iz].Var > stats[relief.Rx, relief.Rz].Var) relief = (ix, iz);
        if (stats[ix, iz].Mean < stats[sea.Rx, sea.Rz].Mean) sea = (ix, iz);
    }
}

float bestPlainsDelta = float.MaxValue;
float reliefVar = stats[relief.Rx, relief.Rz].Var;
for (int ix = 0; ix < bx; ix++)
{
    for (int iz = 0; iz < bz; iz++)
    {
        float delta = Math.Abs(stats[ix, iz].Var - reliefVar * 0.3f);
        if (delta < bestPlainsDelta)
        {
            bestPlainsDelta = delta;
            plains = (ix, iz);
        }
    }
}

var probes = new (string Kind, float U, float V)[]
{
    ("relief", (relief.Rx + 0.5f) / bx, (relief.Rz + 0.5f) / bz),
    ("plains", (plains.Rx + 0.5f) / bx, (plains.Rz + 0.5f) / bz),
    ("sea", (sea.Rx + 0.5f) / bx, (sea.Rz + 0.5f) / bz),
};

var agentProfile = new AgentProfileConfig { Id = "Small", RadiusCm = 30, HeightCm = 180, ClearanceCm = 40, Mass = 1, Layer = 0 };
var navProfile = new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 };
var legacyConfig = new NavBuildConfig(HeightScaleMeters, 0.6f, cliffHeightThreshold: 1);
var obstacles = new NavObstacleSet();

int[] cellSizes = { SpatialScaleDefaults.CellCm, 800 }; // 1m 细档 / 8m 战略档
report.AppendLine("## 单瓦片全管线实测(RecastNavTileBaker,现行三角形输入路径)");
report.AppendLine();
report.AppendLine("| 探区 | 格边(cm) | 瓦片边 | 中位耗时(ms) | 单次分配(MB) | 输入可行三角 | 输出三角 | 顶点 | 门户 | 阶段 |");
report.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

var perTileMsByCellSize = new Dictionary<int, List<double>>();
        var poisonedCellSizes = new HashSet<int>();
foreach (int cellSize in cellSizes)
{
    foreach ((string kind, float u, float v) in probes)
    {
        MutableGridLogicTerrainField field = BuildMiniField(u, v, cellSize);
        var timings = new List<double>();
        double allocBytes = 0;
        NavBakeArtifact lastArtifact = default;
        NavTile lastTile = null!;
        for (int r = 0; r < repeats; r++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            bool ok = RecastNavTileBaker.TryBake(
                field, 1, 1, tileVersion: 1, legacyConfig, agentProfile, navProfile,
                layer: 0, "ground", obstacles, out lastTile, out _, out lastArtifact, feed);
            sw.Stop();
            if (!ok)
            {
                timings.Clear();
                break;
            }

            timings.Add(sw.Elapsed.TotalMilliseconds);
            allocBytes = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        }

        double median = timings.Count > 0 ? Median(timings) : -1;
        if (!perTileMsByCellSize.TryGetValue(cellSize, out var list))
        {
            perTileMsByCellSize[cellSize] = list = new List<double>();
        }

        if (median < 0) poisonedCellSizes.Add(cellSize);
        if (median > 0) list.Add(median);
        report.AppendLine($"| {kind} | {cellSize} | {cellSize * ChunkCells / 100}m | {(median > 0 ? median.ToString("F1") : "失败")} | {(median > 0 ? (allocBytes / 1048576.0).ToString("F1") : "-")} | {lastArtifact.WalkableTriangleCount} | {lastArtifact.TriangleCount} | {lastArtifact.VertexCount} | {lastArtifact.PortalCount} | {(median > 0 ? lastArtifact.Stage.ToString() : lastArtifact.ErrorCode + ": " + lastArtifact.Message)} |");
    }
}

report.AppendLine();

// ---- 每列直灌成本微基准(span 物化,不含三角形化/轮廓/多边形化) ----
report.AppendLine("## 每列直灌成本微基准");
report.AppendLine();

MutableGridLogicTerrainField reliefField1m = BuildMiniField(probes[0].U, probes[0].V, SpatialScaleDefaults.CellCm);
double logicNsPerCol = BenchLogicColumns(reliefField1m);
report.AppendLine($"- 逻辑列(1m 格,level→span 物化): **{logicNsPerCol:F2} ns/列**");

double voxelNsPerCol = BenchVoxelColumns(probes[0].U, probes[0].V, tileMeters: 64, voxelCm: 25);
report.AppendLine($"- 体素列(64m 瓦片 @25cm = 65,536 列,双线性采样+span 物化): **{voxelNsPerCol:F2} ns/列**");
report.AppendLine();

// ---- 全板外推 ----
report.AppendLine("## 全板外推(单 profile,无障碍)");
report.AppendLine();

foreach (int cellSize in cellSizes)
{
    long cellsX = (long)Math.Ceiling(worldWidthCm / (double)cellSize);
    long cellsZ = (long)Math.Ceiling(worldHeightCm / (double)cellSize);
    long totalCells = cellsX * cellsZ;
    long tiles = (long)Math.Ceiling(cellsX / (double)ChunkCells) * (long)Math.Ceiling(cellsZ / (double)ChunkCells);
    bool overBudget = totalCells > 100_000_000;
    string perTile = perTileMsByCellSize.TryGetValue(cellSize, out var list) && list.Count > 0
        ? Median(list).ToString("F1")
        : "无成功样本";
    report.AppendLine($"- {cellSize}cm 格: {cellsX:N0}×{cellsZ:N0} = {totalCells / 1e6:F0}M 格({(overBudget ? "**超引擎 100M 投影预算,现行引擎会落平地兜底**" : "预算内")}),{tiles:N0} 瓦片(瓦片边 {cellSize * ChunkCells / 100}m)");
    if (poisonedCellSizes.Contains(cellSize))
    {
        report.AppendLine("  - **该格边存在烤败探区,外推中止——禁止用幸存探区代表全板**");
        continue;
    }

    if (double.TryParse(perTile, out double ms) && ms > 0)
    {
        double serialMin = ms * tiles / 60000;
        double lowerBoundMin = serialMin / Environment.ProcessorCount;
        report.AppendLine($"  - 单瓦片中位 {ms:F1}ms → 串行全板 {serialMin:F1} 分钟;{Environment.ProcessorCount} 核**理想并行下界 {lowerBoundMin:F1} 分钟**(完美扩展假设,未实测并行;8m 档单瓦 ~9GB 分配下真实并行受内存墙约束会更差)。B 计划线 10 分钟:即便按下界也{(lowerBoundMin > 10 ? "**触发**" : "未触发")}");
    }
}

report.AppendLine();
report.AppendLine("## 附带发现(切片外但必须留档)");
report.AppendLine();
report.AppendLine("- 源采样分辨率直烤(1,256m/格,80km 瓦片):现行管线三探区全部 `SerializationFailed: Arithmetic operation` 溢出——粗格瓦片的 cm 域乘法炸 int。大格战略档烘焙(#1347 档位)落地前必须修");
report.AppendLine("- NavBakeService 按 profile 数线性重复全量烘焙(红队锚点),多 profile 按倍数外推");
report.AppendLine("- 迷你场仅 3×3 块,边界裁剪使三角形数略偏低,吞吐量级不受影响;heightScale=1.0 下 >15m 地形钳制,青藏级 relief 的可行域会偏低");

string repoRoot = FindRepoRoot();
string reportDir = Path.Combine(repoRoot, "artifacts", "benchmarks");
Directory.CreateDirectory(reportDir);
string reportPath = Path.Combine(reportDir, "nav-bake-64km-slice.md");
File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
Console.WriteLine(report.ToString());
Console.WriteLine($"report written: {reportPath}");
return poisonedCellSizes.Count > 0 ? 2 : 0;

MutableGridLogicTerrainField BuildMiniField(float centerU, float centerV, int cellSizeCm)
{
    // 世界坐标 → 源采样坐标等比映射:世界窗按 cellSize 网格化,窗内每格取源双线性高度
    long extentCm = (long)MiniCells * cellSizeCm;
    long wx0 = (long)(centerU * worldWidthCm) - extentCm / 2;
    long wz0 = (long)(centerV * worldHeightCm) - extentCm / 2;
    var field = new MutableGridLogicTerrainField(MiniCells, MiniCells, cellSizeCm, ChunkCells);
    for (int j = 0; j < MiniCells; j++)
    {
        float v = (wz0 + (j + 0.5f) * cellSizeCm) / worldHeightCm;
        for (int i = 0; i < MiniCells; i++)
        {
            float u = (wx0 + (i + 0.5f) * cellSizeCm) / worldWidthCm;
            float h = SampleAt(u, v);
            if (h <= 0)
            {
                field.SetCell(i, j, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Water));
                continue;
            }

            byte level = (byte)Math.Clamp((int)Math.Round(h / (HeightScaleMeters * 100f)), 0, MaxHeightLevel);
            field.SetCell(i, j, new LogicTerrainCell(level, 0, LogicTerrainSurfaceFlags.None));
        }
    }

    return field;
}

static double BenchLogicColumns(MutableGridLogicTerrainField field)
{
    int n = field.WidthCells * field.HeightCells;
    var heights = new float[n];
    for (int j = 0; j < field.HeightCells; j++)
    {
        for (int i = 0; i < field.WidthCells; i++)
        {
            heights[j * field.WidthCells + i] = field.GetCell(i, j).HeightLevel * HeightScaleMeters * 100f;
        }
    }

    var spanMin = new float[n];
    var spanMax = new float[n];
    const int iterations = 300;
    var sw = Stopwatch.StartNew();
    for (int it = 0; it < iterations; it++)
    {
        for (int i = 0; i < n - 1; i++)
        {
            float a = heights[i];
            float b = heights[i + 1];
            spanMin[i] = a < b ? a : b;
            spanMax[i] = (a < b ? b : a) + 40f;
        }
    }

    sw.Stop();
    return sw.Elapsed.TotalNanoseconds / ((double)(n - 1) * iterations);
}

double BenchVoxelColumns(float centerU, float centerV, int tileMeters, int voxelCm)
{
    int side = tileMeters * 100 / voxelCm;
    long wx0 = (long)(centerU * worldWidthCm) - tileMeters * 100L / 2;
    long wz0 = (long)(centerV * worldHeightCm) - tileMeters * 100L / 2;
    var spanMin = new float[side];
    var spanMax = new float[side];
    const int iterations = 200;
    var sw = Stopwatch.StartNew();
    for (int it = 0; it < iterations; it++)
    {
        for (int j = 0; j < side; j++)
        {
            float v = (wz0 + j * voxelCm) / (float)worldHeightCm;
            for (int i = 0; i < side; i++)
            {
                float u = (wx0 + i * voxelCm) / (float)worldWidthCm;
                float h = SampleAt(u, v);
                spanMin[i] = h;
                spanMax[i] = h + 180f;
            }
        }
    }

    sw.Stop();
    return sw.Elapsed.TotalNanoseconds / ((double)side * side * iterations);
}

static double Median(List<double> values)
{
    var sorted = values.OrderBy(v => v).ToList();
    return sorted[sorted.Count / 2];
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (int i = 0; i < 12 && dir != null; i++)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
            Directory.Exists(Path.Combine(dir.FullName, "artifacts")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException("Could not locate repo root.");
}
