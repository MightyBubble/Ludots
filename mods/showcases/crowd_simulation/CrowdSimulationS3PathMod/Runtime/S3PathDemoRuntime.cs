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
/// S3 两点路径演示运行时:从 S1 地形 mod 与能力 mod 的资产烘焙导航上下文,
/// 起终点取真值对拍用的同一 64 组;每个演示对走完整的服务契约——
/// 请求于 requestTick 发出,生效帧 = requestTick + latencyTicks,没算完仿真原地等待。
/// 巡回切换后台线程 1 → 2 → 4;每 5 对注入一次后台变慢(仿真暂停等待,画面不卡)。
/// </summary>
public sealed class S3PathDemoRuntime : IDisposable
{
    public const int ShowTicksPerPair = 150; // 5 仿真秒一对

    public required CrowdSimulationRuntimeConfig Config { get; init; }
    public required NavContext Nav { get; init; }
    public required PathQueryService Service { get; init; }
    public required (int Start, int Goal)[] Pairs { get; init; }

    public int TickCounter { get; private set; }
    public int PairIndex { get; private set; } = -1;
    public int RequestTick { get; private set; }
    public int DueTick { get; private set; }
    public PathResult? Current { get; private set; }
    /// <summary>流场呈现源(GameStart 时由 Mod 入口注入,答复到达即发布)。</summary>
    public CrowdFlowFieldVisualSource? FlowVisual { get; set; }
    public int ThreadCount => Service.WorkerCount;
    public int TotalWaits => Service.Waits;
    public bool SlowInjected { get; private set; }
    public string? Fault => Service.FaultMessage;

    private int? _pendingId;
    private int _ticksUntilRequest = 30;
    private readonly int[] _threadCycle = { 1, 2, 4 };

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
        // 演示用步兵上下文(foot,c1)
        var nav = NavContextBaker.Bake(runtime, grid, heights, deck, 0, 1, cache);

        var truth = JsonNode.Parse(Res(seed, "assets/CrowdSimulation/parity/s3c-query-truth.json"))!;
        var pairs = truth["pairs"]!.AsArray()
            .Select(p => (Start: p![0]!.GetValue<int>(), Goal: p[1]!.GetValue<int>())).ToArray();

        var navs = new Dictionary<int, NavContext> { [nav.Id] = nav };
        return new S3PathDemoRuntime
        {
            Config = runtime,
            Nav = nav,
            Service = new PathQueryService(navs, runtime, workerThreads: 1, TimeSpan.FromSeconds(5)),
            Pairs = pairs,
        };
    }

    /// <summary>仿真 tick(固定 30 Hz;每对展示 5 秒)。</summary>
    public void Tick()
    {
        TickCounter++;
        if (_pendingId.HasValue && TickCounter >= DueTick)
        {
            // 生效帧取答复:没算完就原地等待(仿真时钟不走,等待计数)
            var result = Service.AwaitDue(_pendingId.Value);
            _pendingId = null;
            if (Current?.Flow != null) Service.Recycle(Current.Flow);
            Current = result;
            if (FlowVisual != null) FlowVisual.Flow = result.Flow;
            if (Service.Faulted) return;
            Service.TestDelayPerJob = TimeSpan.Zero;
            SlowInjected = false;
            _ticksUntilRequest = ShowTicksPerPair;
        }

        if (_ticksUntilRequest > 0 && --_ticksUntilRequest == 0 && !Service.Faulted)
        {
            PairIndex = (PairIndex + 1) % Pairs.Length;
            var (start, goal) = Pairs[PairIndex];
            if (PairIndex % _threadCycle.Length == 0)
            {
                Service.SetWorkerCount(_threadCycle[PairIndex / _threadCycle.Length % _threadCycle.Length]);
            }

            if (PairIndex % 5 == 4)
            {
                Service.TestDelayPerJob = TimeSpan.FromMilliseconds(400);
                SlowInjected = true;
            }

            RequestTick = TickCounter;
            _pendingId = Service.Request(new PathQuery(Nav.Id, start, goal, 0), TickCounter);
            DueTick = Service.DueTick(_pendingId.Value);
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
