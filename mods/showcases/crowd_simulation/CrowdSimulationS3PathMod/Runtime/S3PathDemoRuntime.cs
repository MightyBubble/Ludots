using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation.Terrain;

namespace CrowdSimulationS3PathMod.Runtime;

/// <summary>
/// S3 两点路径演示运行时(spec 演示契约):左键点起点、右键点终点,路线与方向图
/// 经完整服务契约生效——请求于 requestTick 发出,生效帧 = requestTick + latencyTicks,
/// 没算完仿真原地等待。按键:Q 巡回代理体型(移动类型 × 净空)、T 巡回后台线程
/// 1/2/4、G 注入后台变慢(仿真暂停等待,画面不卡)。呈现线程只入队,仿真 tick 消费。
/// </summary>
public sealed class S3PathDemoRuntime : IDisposable
{
    public required CrowdSimulationRuntimeConfig Config { get; init; }
    public required IReadOnlyList<NavContext> Contexts { get; init; }
    public required IReadOnlyList<string> ContextLabels { get; init; }
    public required PathQueryService Service { get; init; }

    public int TickCounter { get; private set; }
    public int ContextIndex { get; private set; }
    /// <summary>视图模式:0 路线(方向场+折线) · 1 可走区域(不可走红罩/桥面/portal) · 2 NavMesh+HPA 线框。</summary>
    public int ViewMode { get; private set; }
    public static readonly string[] ViewModeLabels = { "路线", "可走区域", "NavMesh+HPA" };
    public int StartCell { get; private set; } = -1;
    public int GoalCell { get; private set; } = -1;
    public int HoveredCell { get; set; } = -1;
    public int RequestTick { get; private set; }
    public int DueTick { get; private set; }
    public PathResult? Current { get; private set; }
    /// <summary>流场呈现源(GameStart 时由 Mod 入口注入,答复到达即发布)。</summary>
    public CrowdFlowFieldVisualSource? FlowVisual { get; set; }
    public int ThreadCount => Service.WorkerCount;
    public int TotalWaits => Service.Waits;
    public bool SlowInjected { get; private set; }
    public string? Fault => Service.FaultMessage;
    public NavContext Nav => Contexts[ContextIndex];
    public string ContextLabel => ContextLabels[ContextIndex];

    private int? _pendingId;
    private bool _pathDirty;
    private readonly object _inputGate = new();
    private readonly Queue<Action> _inputQueue = new();

    public static S3PathDemoRuntime Load(IModContext context)
    {
        Stream Res(string modId, string rel) => context.GetResource($"{modId}:{rel}");
        const string seed = "CrowdSimulationS1Terrain1337Mod";

        var merged = (JsonObject)JsonNode.Parse(Res("CrowdSimulationMod", "assets/CrowdSimulationConfig.json"))!;
        var overlay = (JsonObject)JsonNode.Parse(Res(seed, "assets/CrowdSimulationConfig.json"))!;
        ConfigPipeline.DeepMerge(merged, overlay);
        var config = CrowdSimulationConfig.Load(merged);

        var map = JsonSerializer.Deserialize<MapConfig>(
            Res(seed, "assets/Maps/crowd_simulation_s1337.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var templates = JsonSerializer.Deserialize<List<EntityTemplate>>(
            Res(seed, "assets/Entities/templates.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var templatesById = templates.Where(t => !string.IsNullOrWhiteSpace(t.Id)).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var profileList = JsonSerializer.Deserialize<List<AgentProfileConfig>>(
            Res("CrowdSimulationMod", "assets/Navigation/agent_profiles.json"),
            StrictJsonOptions.CreateCamelCase())!;
        var runtime = CrowdSimulationConfigLoader.Load(config, map, new AgentProfileRegistry(profileList), fixedHz: 30);

        var surface = NavSurfaceAsset.Read(Res(seed, "assets/terrain/crowd_simulation_s1337.navsurface"));
        var mapSurface = CrowdSimulationMapSurfaceSource.Extract(map, templatesById);
        var grid = SurfaceGrid.Build(runtime, surface, mapSurface.Blockers);
        var heightAsset = ContinuousHeightmapBinary.Read(Res(seed, "assets/terrain/crowd_simulation_s1337.height"));
        var heights = NavHeightField.FromHeightmap(heightAsset, runtime.NavCellCount, runtime.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtime);

        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, runtime.Hpa.ClusterSize,
            runtime.Navmesh.MinRegionArea.ToDouble(), runtime.Navmesh.MaxSimplificationError.ToDouble(),
            runtime.Navmesh.MaxEdgeLen.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        // 全部去重导航上下文(移动类型 × 净空)——Q 键巡回的对象
        var contexts = new List<NavContext>();
        var labels = new List<string>();
        var seen = new HashSet<int>();
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            foreach (var clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtime, grid, heights, deck, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;
                contexts.Add(nav);
                labels.Add($"{runtime.AgentTypes[a].Id} c{clearance}");
            }
        }

        // 初始起终点:真值 64 对的第 0 组(与对拍同一份数据,不另造)
        var truth = JsonNode.Parse(Res(seed, "assets/CrowdSimulation/parity/s3c-query-truth.json"))!;
        var firstPair = truth["pairs"]!.AsArray()[0]!;
        var navs = contexts.ToDictionary(n => n.Id);
        return new S3PathDemoRuntime
        {
            Config = runtime,
            Contexts = contexts,
            ContextLabels = labels,
            Service = new PathQueryService(navs, runtime, workerThreads: 1, TimeSpan.FromSeconds(5)),
            StartCell = firstPair[0]!.GetValue<int>(),
            GoalCell = firstPair[1]!.GetValue<int>(),
            _pathDirty = true,
        };
    }

    // ── 呈现线程输入(只入队) ─────────────────────────────
    public void EnqueueSetStart(int cell) { lock (_inputGate) _inputQueue.Enqueue(() => { StartCell = cell; _pathDirty = true; }); }
    public void EnqueueSetGoal(int cell) { lock (_inputGate) _inputQueue.Enqueue(() => { GoalCell = cell; _pathDirty = true; }); }
    public void EnqueueCycleContext() { lock (_inputGate) _inputQueue.Enqueue(() => { ContextIndex = (ContextIndex + 1) % Contexts.Count; _pathDirty = true; }); }
    public void EnqueueCycleThreads() { lock (_inputGate) _inputQueue.Enqueue(() => Service.SetWorkerCount(Service.WorkerCount == 1 ? 2 : Service.WorkerCount == 2 ? 4 : 1)); }
    public void EnqueueCycleView() { lock (_inputGate) _inputQueue.Enqueue(() => ViewMode = (ViewMode + 1) % ViewModeLabels.Length); }
    public void EnqueueToggleSlow()
    {
        lock (_inputGate) _inputQueue.Enqueue(() =>
        {
            SlowInjected = !SlowInjected;
            Service.TestDelayPerJob = SlowInjected ? TimeSpan.FromMilliseconds(400) : TimeSpan.Zero;
        });
    }

    /// <summary>仿真 tick(固定 30 Hz)。</summary>
    public void Tick()
    {
        TickCounter++;
        for (; ; )
        {
            Action? action = null;
            lock (_inputGate)
            {
                if (_inputQueue.Count > 0) action = _inputQueue.Dequeue();
            }

            if (action == null) break;
            action();
        }

        if (_pathDirty && StartCell >= 0 && GoalCell >= 0 && !Service.Faulted)
        {
            _pathDirty = false;
            if (_pendingId.HasValue)
            {
                Service.Discard(_pendingId.Value);
                _pendingId = null;
            }

            RequestTick = TickCounter;
            _pendingId = Service.Request(new PathQuery(Nav.Id, StartCell, GoalCell, 0), TickCounter);
            DueTick = Service.DueTick(_pendingId.Value);
        }

        if (_pendingId.HasValue && TickCounter >= DueTick)
        {
            // 生效帧取答复:没算完就原地等待(仿真时钟不走,等待计数)
            var result = Service.AwaitDue(_pendingId.Value);
            _pendingId = null;
            if (Current?.Flow != null) Service.Recycle(Current.Flow);
            Current = result;
            if (FlowVisual != null) FlowVisual.Flow = result.Flow;
        }
    }

    public void Dispose() => Service.Dispose();
}

/// <summary>固定步进仿真驱动(30 Hz,一帧一 tick)。</summary>
public sealed class S3PathDemoSimulationSystem : Arch.System.ISystem<float>
{
    private readonly S3PathDemoRuntime _runtime;

    public S3PathDemoSimulationSystem(S3PathDemoRuntime runtime) => _runtime = runtime;

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void Update(in float dt) => _runtime.Tick();
    public void AfterUpdate(in float t) { }
    public void Dispose() { }
}
