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
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Scripting;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// S3 两点路径演示运行时(spec 演示契约):左键点起点、右键点终点,路线与方向图
/// 经完整服务契约生效——请求于 requestTick 发出,生效帧 = requestTick + latencyTicks,
/// 没算完仿真原地等待。按键:Q 巡回代理体型(移动类型 × 净空)、T 巡回后台线程
/// 1/2/4。G 把 TestDelayPerJob 设为 400ms。生效帧上 AwaitDue 阻塞调用方。
/// 输入系统和仿真 tick 都在 PostMovement，没有单独的呈现线程；这一等会挡住这一帧。
/// </summary>
public sealed class S3PathDemoRuntime : IDisposable
{
    public required CrowdSimulationRuntimeConfig Config { get; init; }
    public required IReadOnlyList<NavContext> Contexts { get; init; }
    public required IReadOnlyList<string> ContextLabels { get; init; }
    public required PathQueryService Service { get; init; }

    public bool Active { get; set; } = true;
    public int TickCounter { get; private set; }
    public int ContextIndex { get; private set; }
    /// <summary>视图模式:0 路线(方向场+折线) · 1 可走区域(不可走红罩/上层/portal) · 2 NavMesh+HPA 线框。</summary>
    public int ViewMode { get; private set; }
    private string[]? _viewLabels;
    private int _viewModeCount = 3;
    public string ViewLabel => (_viewLabels ?? throw new InvalidOperationException("路径演示视图名未配置。"))[ViewMode];
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

    public static S3PathDemoRuntime Load(GameEngine engine, CrowdSimulationConfig configDto, MapConfig map, string truthAsset, TimeSpan planTimeout)
    {
        var agentProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("S3 路径演示需要 AgentProfiles 服务。");
        var navmesh = new NavMeshBakeConfigLoader(engine.ConfigPipeline, agentProfiles)
            .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        var traversal = CrowdTraversalConfig.Load(engine.ConfigPipeline, engine.ConfigCatalog, engine.ConfigConflictReport);
        var templateFacts = CrowdUnitTemplateFacts.FromTemplates(engine.MapLoader.TemplateRegistry.GetAll());
        int fixedHz = (int)MathF.Round(1f / Time.FixedDeltaTime);
        var teamRelations = engine.GetService(CoreServiceKeys.TeamRelationQuery)
            ?? throw new InvalidOperationException("S3 路径演示需要 TeamRelationQuery。");
        var players = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("S3 路径演示需要 PlayerEntityLookup。");
        var projected = CrowdPairRulesProjection.Project(configDto, map, teamRelations, players, engine.World);
        var runtime = CrowdSimulationConfigLoader.Load(
            configDto, map, agentProfiles, navmesh, traversal, templateFacts, fixedHz, projected.Rules);
        var templatesById = engine.MapLoader.TemplateRegistry.GetAll()
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .ToDictionary(t => t.Id, StringComparer.Ordinal);

        string surfaceAssetPath = "assets/" + runtime.SurfaceAsset;
        var surface = NavSurfaceAsset.Read(CrowdSimulationRuntime.OpenAsset(engine, surfaceAssetPath));
        var mapSurface = CrowdSimulationMapSurfaceSource.Extract(map, templatesById);
        string? heightRel = map.ContinuousHeightmap?.Asset;
        if (string.IsNullOrWhiteSpace(heightRel))
        {
            throw new InvalidOperationException($"地图 '{map.Id}' 未声明 ContinuousHeightmap.Asset。");
        }

        var heightAsset = ContinuousHeightmapBinary.Read(CrowdSimulationRuntime.OpenAsset(engine, heightRel));
        var board = NavSolverDeclaration.RequireBoard(map, NavSolverDeclaration.RequireMap(navmesh, map.Id));
        var ingested = CrowdSimulationWorldIngest.Build(
            runtime,
            board.WidthCm,
            board.HeightCm,
            surface,
            surfaceAssetPath,
            heightAsset,
            heightRel,
            mapSurface.Blockers,
            mapSurface.LayeredPaths);
        var grid = ingested.Grid;
        var heights = ingested.Heights;
        var upperLayer = UpperLayerBake.RasterizeLayeredPaths(mapSurface.LayeredPaths, runtime);

        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, runtime.Hpa.NavCellsPerTile,
            runtime.Navmesh.MinRegionNavCellArea.ToDouble(), runtime.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            runtime.Navmesh.MaxEdgeLenNavCells.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);

        // 全部去重导航上下文(移动类型 × 净空)——Q 键巡回的对象
        var contexts = new List<NavContext>();
        var labels = new List<string>();
        var seen = new HashSet<int>();
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            foreach (var clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceNavCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtime, grid, heights, upperLayer, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;
                contexts.Add(nav);
                labels.Add($"{runtime.AgentTypes[a].Id} c{clearance}");
            }
        }

        // 初始起终点:真值 64 对的第 0 组(与对拍同一份数据,不另造)
        var truth = JsonNode.Parse(CrowdSimulationRuntime.OpenAsset(engine, truthAsset))!;
        var firstPair = truth["pairs"]!.AsArray()[0]!;
        var navs = contexts.ToDictionary(n => n.Id);
        return new S3PathDemoRuntime
        {
            Config = runtime,
            Contexts = contexts,
            ContextLabels = labels,
            Service = new PathQueryService(navs, runtime, workerThreads: 1, planTimeout),
            StartCell = firstPair[0]!.GetValue<int>(),
            GoalCell = firstPair[1]!.GetValue<int>(),
            _pathDirty = true,
        };
    }

    // 输入在仿真 tick 里出队。这些方法只入队。
    public void EnqueueSetStart(int cell) { lock (_inputGate) _inputQueue.Enqueue(() => { StartCell = cell; _pathDirty = true; }); }
    public void EnqueueSetGoal(int cell) { lock (_inputGate) _inputQueue.Enqueue(() => { GoalCell = cell; _pathDirty = true; }); }
    public void EnqueueCycleContext() { lock (_inputGate) _inputQueue.Enqueue(() => { ContextIndex = (ContextIndex + 1) % Contexts.Count; _pathDirty = true; }); }
    public void EnqueueCycleThreads() { lock (_inputGate) _inputQueue.Enqueue(() => Service.SetWorkerCount(Service.WorkerCount == 1 ? 2 : Service.WorkerCount == 2 ? 4 : 1)); }
    public void UseViewLabels(CrowdViewCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Length == 0)
            throw new InvalidOperationException("路径演示视图名至少一条。");
        _viewLabels = catalog.Labels;
        _viewModeCount = catalog.Length;
    }

    public void EnqueueCycleView() { lock (_inputGate) _inputQueue.Enqueue(() => ViewMode = (ViewMode + 1) % _viewModeCount); }
    public void EnqueueToggleSlow()
    {
        lock (_inputGate) _inputQueue.Enqueue(() =>
        {
            SlowInjected = !SlowInjected;
            Service.TestDelayPerJob = SlowInjected ? TimeSpan.FromMilliseconds(400) : TimeSpan.Zero;
        });
    }

    /// <summary>仿真 tick。步频跟引擎 FixedHz，不在这里写死。</summary>
    public void Tick()
    {
        if (!Active) return;
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
            if (Current != null)
            {
                if (Current.Flow != null) Service.Recycle(Current.Flow);
                Service.ReleaseReply(Current);
            }

            Current = result;
            if (FlowVisual != null) FlowVisual.Flow = result.Flow;
        }
    }

    public void Dispose() => Service.Dispose();
}

/// <summary>固定步进仿真驱动(一帧一 tick,步频跟引擎 FixedHz)。</summary>
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
