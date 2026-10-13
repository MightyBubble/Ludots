using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Client;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Core.Vision;

namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// CrowdSimulation 会话宿主(引擎持有,MassNavigationRuntime 同款形态):
/// maps.&lt;mapId&gt;.solver 为 crowd-simulation 的地图在聚焦时必须合并出 CrowdSimulationConfig.json,然后建会话;挂起时释放。
/// 调试目录项由提供 CrowdSimulationDebug.json 的 Mod 登记,只带脚本、相机和演示呈现。
/// 会话跑在引擎世界上,单位带呈现投影接线;部署/脚本/回放都是指令流(数据),
/// 回放另起一次性世界逐帧对拍校验码。
/// </summary>
public sealed partial class CrowdSimulationRuntime : IPresentationAlphaHold
{
    private CrowdSimulationConfig? _configDto;
    private CrowdSimulationDebugConfig? _debug;
    private CrowdSimulationDebugViewConfig? _debugView;
    private CrowdPlayerViewConfig? _playerView;
    internal bool HasFormalConfig => _configDto != null;
    private string? _focusedCrowdMapId;
    internal string? FormalConfigMapId => _focusedCrowdMapId;
    internal bool HasDebugConfig => _debug != null;
    private TimeSpan? _planTimeout;

    private CrowdSimSession? _session;
    public string DebugValidationLine { get; private set; } = "校验:未跑";
    public bool DebugValidationFailed { get; private set; }
    public bool DebugValidationRan { get; private set; }
    public const string DebugFuzzIdleLine = "模糊:未跑";
    public const string DebugFuzzNoSessionLine = "模糊:无会话";
    public string DebugFuzzLine { get; private set; } = DebugFuzzIdleLine;
    public bool DebugFuzzFailed { get; private set; }
    public bool DebugFuzzRan { get; private set; }
    private CrowdFuzzCursor? _debugFuzz;
    private bool _debugFuzzStepped;

    /// <summary>在 tick 之间跑全部不变量。步进本身不调用。</summary>
    public void RunDebugValidation()
    {
        if (_session == null)
        {
            DebugValidationLine = "校验:无会话";
            DebugValidationFailed = true;
            DebugValidationRan = true;
            return;
        }

        try
        {
            // 哈希在本子步积分之前重建。帧间看到的是积分后的位置，先按当前位置重铺再对照。
            _session.Movement?.RebuildHash();
            CrowdSimulationValidator.RunAll(_session);
            DebugValidationLine = $"校验:通过 tick {_session.TickCount}";
            DebugValidationFailed = false;
            DebugValidationRan = true;
        }
        catch (Exception ex)
        {
            DebugValidationLine = "校验:失败 " + ex.Message;
            DebugValidationFailed = true;
            DebugValidationRan = true;
        }
    }

    /// <summary>调试页按下模糊测试。已经在跑的一轮不重开，避免半路丢掉停摆开关。</summary>
    public void StartDebugFuzz()
    {
        if (_session == null)
        {
            DebugFuzzLine = DebugFuzzNoSessionLine;
            DebugFuzzFailed = true;
            DebugFuzzRan = true;
            return;
        }

        if (_debugFuzz != null && !_debugFuzz.Finished) return;
        CrowdDebugFuzz? fuzz = _debug?.Fuzz;
        CrowdDebugTools? tools = _debug?.Tools;
        if (fuzz == null || tools == null)
        {
            DebugFuzzLine = "模糊:未配置";
            DebugFuzzFailed = true;
            DebugFuzzRan = true;
            return;
        }

        _debugFuzz = new CrowdFuzzCursor(fuzz.ToPlan(tools));
        PumpDebugFuzz();
    }

    /// <summary>按调试配置的时间片接着跑。不在 <see cref="Tick"/> 里调用。</summary>
    public void PumpDebugFuzz()
    {
        if (_debugFuzz == null || _debugFuzz.Finished) return;
        if (_session == null)
        {
            _debugFuzz = null;
            DebugFuzzLine = DebugFuzzNoSessionLine;
            DebugFuzzFailed = true;
            DebugFuzzRan = true;
            return;
        }

        CrowdDebugFuzz fuzz = _debug?.Fuzz
            ?? throw new InvalidOperationException("CrowdSimulationDebug.json: 部署演示必须带 fuzz。");
        _debugFuzz.Advance(_session, TimeSpan.FromMilliseconds(fuzz.SliceMs));
        _debugFuzzStepped = _debugFuzz.Stepped;
        CrowdFuzzResult? result = _debugFuzz.Result;
        DebugFuzzRan = true;
        if (result == null)
        {
            DebugFuzzLine = $"模糊:{_debugFuzz.Completed}/{fuzz.OpCount}";
            DebugFuzzFailed = false;
            return;
        }

        DebugFuzzFailed = !result.Passed;
        DebugFuzzLine = result.Passed
            ? $"模糊:通过 seed {result.Seed} 步 {result.Step}"
            : $"模糊:失败 seed {result.Seed} 步 {result.Step} {result.OpId} {result.Error}";
    }

    /// <summary>本帧的模糊时间片已经步进过。会话 tick 读一次后清掉，避免同一帧再走一步。</summary>
    public bool ConsumeDebugFuzzStepped()
    {
        bool stepped = _debugFuzzStepped;
        _debugFuzzStepped = false;
        return stepped;
    }
    private CrowdPairRulesSessionGuard? _pairRulesGuard;
    private Entity _sessionEntity;

    /// <summary>地图桥入认知仓:桥模板按 id 查配置表(缺失 = 配置没有 bridge 模板,
    /// 无桥地图自然为空表)。</summary>
    private static void InstallMapBridges(
        Structures.CrowdStructuresStore store,
        Config.CrowdSimulationRuntimeConfig config,
        IReadOnlyList<BridgeDeckRecord>? bridges)
    {
        if (bridges == null || bridges.Count == 0) return;
        int tpl = store.TemplateIndexOf("bridge");
        if (tpl < 0) return;
        var spans = new List<CrowdSimulationBridgeSpan>(bridges.Count);
        foreach (var b in bridges) spans.Add(b.Span);
        store.InstallMapBridges(tpl, spans);
    }
    private string? _activeMapId;
    private bool _systemsInstalled;
    private CrowdSimulation.Nav.Pathing.PathQueryService? _pathService;
    /// <summary>地图烘焙输入(回放会话重建结构仓与全新导航用;与活会话的仓互不共享可变栅格)。</summary>
    private CrowdSimulation.World.SurfaceGrid? _surfaceGrid;
    private CrowdSimulation.World.NavSurfaceJumpCandidate[]? _jumpCandidates;
    private CrowdSimulation.World.NavHeightField? _heights;
    private CrowdSimulation.Nav.DeckSurface? _deck;
    private IReadOnlyList<CrowdSimulation.World.BlockerFootprint>? _mapSurfaceBlockers;
    private IReadOnlyList<CrowdSimulation.World.BridgeDeckRecord>? _mapSurfaceBridges;
    private bool _stallLogged;
    private bool _pathReplyPending;
    private CrowdPlayerOrderPrefs? _orderPrefs;
    private readonly System.Diagnostics.Stopwatch _tickWatch = System.Diagnostics.Stopwatch.StartNew();
    // 分区摘要历史:定长环形。回放按 tick 对上环里还在的记录，再比断面。合计在摘要的 Total 上。
    private readonly int[] _hashTicks = new int[4096];
    private readonly CrowdSimStateDigest[] _hashDigests = new CrowdSimStateDigest[4096];
    private int _hashCount, _hashHead;
    private readonly List<CrowdCommand> _pendingScript = new();
    private CrowdDemoScript? _demoScript;
    private GameEngine? _engine;
    private int _autoReplayAtTick = -1;
    private bool _autoReplayDone;

    public CrowdSimSession? Session => _session;
    public Entity SessionEntity => _sessionEntity;
    private readonly CrowdSimulationTelemetry _detachedTelemetry = new();
    /// <summary>人群遥测(活会话的那一份;未激活时是空壳)。看板与调试面板只读。</summary>
    public CrowdSimulationTelemetry Telemetry => _session?.Telemetry ?? _detachedTelemetry;

    /// <summary>进图时世界生成与导航烘焙的墙钟。未烘焙时 <see cref="WorldBakeRecorded"/> 为 false。</summary>
    public bool WorldBakeRecorded { get; private set; }
    public double WorldBakeMs { get; private set; }
    public int WorldBakeCacheHits { get; private set; }
    public int WorldBakeCacheMisses { get; private set; }

    /// <summary>
    /// 把下一次路径任务的工人延迟设成两倍规划超时,并提交清缓存,用来把路径故障浮层打出来。
    /// 工人仍在后台睡,仿真停摆,不改到主线程重算。
    /// </summary>
    public void ProbePathFault()
    {
        var service = _pathService;
        var session = _session;
        if (service == null || session == null) return;
        var timeout = service.PlanTimeout;
        service.TestDelayPerJob = timeout + timeout;
        NoteLiveCommand();
        session.Commands.Submit(
            session,
            new CrowdSimCommand { Kind = CrowdSimCommandKind.ClearCaches },
            CrowdSimCommands.Exec);
    }

    /// <summary>回放结论:0 未跑 / 1 合计相同 / 2 合计分歧。诊断断面不改这个结论。</summary>
    public int ReplayStatus { get; private set; }

    /// <summary>合计的首个分歧 tick。-1 表示合计一致，-2 表示两边条数不同。</summary>
    public int ReplayDivergenceTick { get; private set; } = -1;

    /// <summary>合计的首个分歧分区名。合计一致或条数不同时为空。</summary>
    public string ReplayDivergenceSection { get; private set; } = "";

    /// <summary>最早一个诊断断面有差的 tick。没有时为 -1。</summary>
    public int ReplayDiagnosticTick { get; private set; } = -1;

    /// <summary>最早诊断分歧 tick 上有差的诊断断面。没有时为 0。</summary>
    public CrowdSimDiagnosticSection ReplayDivergenceDiagnostic { get; private set; }

    /// <summary>停用本次激活的自动回放:现场指令(玩家交互)进入会话后,自动回放的对拍自证
    /// 不再是"同输入重放"。只拦自动触发;RunReplay 与对拍语义本身不动。</summary>
    public void SuppressAutoReplay() => _autoReplayDone = true;

    /// <summary>现场下令已进入本次会话：停自动回放，演示 HUD 改记交互模式。</summary>
    public void NoteLiveCommand()
    {
        SuppressAutoReplay();
        _deployDemo?.MarkInteractive(Session);
    }

    /// <summary>单位半径黑板键(呈现侧 ownerBlackboardFloat 绑定的数据契约,Activate 时注册一次)。</summary>
    public static class Keys
    {
        public const string UnitRadiusKey = "crowd_simulation.unit.radius_m";
    }

    /// <summary>迷雾观察口径:-1 = 全知，≥ 0 = 只看该视野组。当前没有读取点。</summary>
    public int FogViewGroup { get; set; } = -1;

    public bool HoldAtCurrent => _session != null && (_session.PlaybackHeld || _pathReplyPending);

    internal NavHeightField? Heights => _heights;

    internal CrowdPlayerOrderPrefs? OrderPrefs
    {
        get => _orderPrefs;
        set => _orderPrefs = value;
    }

    public bool HandleMapFocused(GameEngine engine, MapId mapId, MapConfig mapConfig)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!IsCrowdMap(engine, mapId.Value)) return false;
        RequireConfig(engine, mapId.Value);
        if (_session != null && !string.Equals(_activeMapId, mapId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"群体会话仍绑在地图 \"{_activeMapId}\"，聚焦 \"{mapId.Value}\" 之前要先挂起或卸载。");
        }

        _focusedCrowdMapId = mapId.Value;
        if (_session == null)
        {
            Activate(engine, mapId, mapConfig);
        }

        string? presentationKind = PresentationKindName;
        if (presentationKind == CrowdPresentationConfig.KindPath) InstallPathPresentation(engine, mapConfig);
        if (presentationKind == CrowdPresentationConfig.KindDeploy) InstallDeployPresentation(engine);
        return true;
    }

    public bool HandleMapUnloaded(GameEngine engine, MapId mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        bool releasedPath = ReleasePathPresentation(engine, mapId);
        bool releasedDeploy = ReleaseDeployPresentation(engine, mapId);
        if (_activeMapId == null || !string.Equals(mapId.Value, _activeMapId, StringComparison.Ordinal))
            return releasedPath || releasedDeploy;
        Deactivate(engine);
        return true;
    }

    public bool HandleMapSuspended(GameEngine engine, MapId mapId) => HandleMapUnloaded(engine, mapId);

    private NavMeshBakeConfig _navmesh;

    private NavMeshBakeConfig RequireLoadedNavmesh(GameEngine engine)
    {
        if (_navmesh != null)
        {
            return _navmesh;
        }

        if (engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig loaded) && loaded != null)
        {
            _navmesh = loaded;
            return loaded;
        }

        var agentProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 AgentProfiles 服务。");
        _navmesh = new NavMeshBakeConfigLoader(engine.ConfigPipeline, agentProfiles)
            .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        engine.SetService(CoreServiceKeys.NavMeshBakeConfig, _navmesh);
        return _navmesh;
    }

    private bool IsCrowdMap(GameEngine engine, string mapId)
        => NavSolverDeclaration.Is(RequireLoadedNavmesh(engine), mapId, NavSolverDeclaration.CrowdSimulation);

    private void RequireConfig(GameEngine engine, string mapId)
    {
        if (_configDto != null)
        {
            return;
        }

        var catalog = engine.ConfigCatalog;
        if (catalog == null || !catalog.TryGet(CrowdSimulationConfigPath, out var configEntry))
        {
            throw new InvalidOperationException(
                $"地图 \"{mapId}\" 的 solver 是 {NavSolverDeclaration.CrowdSimulation}，但 {CrowdSimulationConfigPath} 缺失或合并为空。");
        }

        _configDto = ConfigMergeProvenance.Wrap(CrowdSimulationConfigPath, engine.ConfigConflictReport, tracked =>
        {
            var merged = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in configEntry, tracked)
                ?? throw new InvalidOperationException(
                    $"地图 \"{mapId}\" 的 solver 是 {NavSolverDeclaration.CrowdSimulation}，但 {CrowdSimulationConfigPath} 缺失或合并为空。");
            return CrowdSimulationConfig.Load(merged);
        });

        _planTimeout = TimeSpan.FromMilliseconds(_configDto.Worker.PlanTimeoutMs);

        if (!catalog.TryGet(CrowdSimulationDebugPath, out var debugEntry))
        {
            _debug = null;
        }
        else
        {
            CrowdSimulationDebugConfig debug = ConfigMergeProvenance.Wrap(CrowdSimulationDebugPath, engine.ConfigConflictReport, tracked =>
            {
                var debugMerged = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in debugEntry, tracked)
                    ?? throw new InvalidOperationException($"{CrowdSimulationDebugPath} 存在目录登记但合并为空。");
                return CrowdSimulationDebugConfig.Load(debugMerged);
            });
            _debug = debug;
            _presentation = debug.Presentation;
            if (_presentation?.FogDecal != null)
            {
                engine.SetService(CoreServiceKeys.FogDecalPalette, _presentation.FogDecal.ToPalette());
            }
        }

        if (!catalog.TryGet(CrowdSimulationDebugViewPath, out var viewEntry))
        {
            _debugView = null;
        }
        else
        {
            _debugView = ConfigMergeProvenance.Wrap(CrowdSimulationDebugViewPath, engine.ConfigConflictReport, tracked =>
            {
                var viewMerged = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in viewEntry, tracked)
                    ?? throw new InvalidOperationException($"{CrowdSimulationDebugViewPath} 存在目录登记但合并为空。");
                return CrowdSimulationDebugViewConfig.Load(viewMerged, CrowdSimulationDebugViewPath);
            });
        }

        if (!catalog.TryGet(CrowdSimulationPlayerViewPath, out var playerViewEntry))
        {
            _playerView = null;
            return;
        }

        _playerView = ConfigMergeProvenance.Wrap(CrowdSimulationPlayerViewPath, engine.ConfigConflictReport, tracked =>
        {
            var merged = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in playerViewEntry, tracked)
                ?? throw new InvalidOperationException($"{CrowdSimulationPlayerViewPath} 存在目录登记但合并为空。");
            return CrowdPlayerViewConfig.Load(merged, CrowdSimulationPlayerViewPath);
        });
    }

    private TimeSpan RequirePlanTimeout()
        => _planTimeout ?? throw new InvalidOperationException(
            "CrowdSimulationConfig.json: 路径服务需要 worker.planTimeoutMs。");

    private const string CrowdSimulationConfigPath = "CrowdSimulationConfig.json";
    private const string CrowdSimulationDebugPath = "CrowdSimulationDebug.json";
    private const string CrowdSimulationDebugViewPath = "CrowdSimulationDebugView.json";
    private const string CrowdSimulationPlayerViewPath = "CrowdSimulationPlayerView.json";
    private const string SessionTemplateId = "crowd_simulation.session";
    private const string SelectedCollectionKey = CrowdSimPresentationWiring.SelectedCollectionKey;

    private void Activate(GameEngine engine, MapId mapId, MapConfig mapConfig)
    {
        _engine = engine;
        if (!_systemsInstalled)
        {
            _systemsInstalled = true;
            // 会话步进是 PostMovement 组的移动生产者,必须排在 SpatialPartitionUpdateSystem 之前:
            // 分区同步按 PreviousWorldPositionCm != WorldPositionCm 判定本 tick 是否移动,步进晚于
            // 同步会让该判定恒为 false,成员格冻结在出生位置。
            engine.InsertSystemBeforeRequired<SpatialPartitionUpdateSystem>(
                new CrowdSimulationSessionTickSystem(this), SystemGroup.PostMovement);
            CrowdSimulationOrderBridge.Register(engine, this);
        }

        // 装载只取单位模板点名的档案。全局注册表里的其它体型不进半径级。
        var agentProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 AgentProfiles 服务。");
        var navmesh = RequireLoadedNavmesh(engine);
        var pathing = new PathingConfigLoader(engine.ConfigPipeline)
            .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        var templateFacts = CrowdUnitTemplateFacts.FromTemplates(engine.MapLoader.TemplateRegistry.GetAll());

        var teamRelations = engine.GetService(CoreServiceKeys.TeamRelationQuery)
            ?? throw new InvalidOperationException("CrowdSimulation 会话需要 TeamRelationQuery。");
        var players = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("CrowdSimulation 会话需要 PlayerEntityLookup。");
        var projected = CrowdPairRulesProjection.Project(_configDto!, mapConfig, teamRelations, players, engine.World);
        _pairRulesGuard = projected.Guard;
        var runtimeConfig = CrowdSimulationConfigLoader.Load(
            _configDto!, mapConfig, agentProfiles, navmesh, pathing, templateFacts,
            (int)MathF.Round(1f / Time.FixedDeltaTime), projected.Rules);

        SolverAgentExclusivity.RejectMassNavigationAgent(engine.World, mapId);
        // 模板级契约(双求解器 Agent 互斥/模板 profileId 存在/阻挡物正方形 Box)在进图激活时执行,
        // 与配置门禁构成同一条装载检查链;失败即报错,不进入烘焙与生成。
        CrowdSimulationAuthoringContract.Validate(
            engine.MapLoader.TemplateRegistry.GetAll(), _configDto!, mapConfig, agentProfiles);

        // 资产走 VFS，跨 mod 解析，冲突即失败。尺寸对照在进图装配里做。
        string surfaceAssetPath = "assets/" + runtimeConfig.SurfaceAsset;
        var surface = NavSurfaceAsset.Read(OpenAsset(engine, surfaceAssetPath));
        string heightAssetPath = RequireHeightAssetPath(mapConfig, mapId);
        var heightAsset = ContinuousHeightmapBinary.Read(OpenAsset(engine, heightAssetPath));
        BoardConfig solverBoard = NavSolverDeclaration.RequireBoard(
            mapConfig, NavSolverDeclaration.RequireMap(navmesh, mapConfig.Id));
        var mapSurface = CrowdSimulationMapSurfaceSource.Extract(
            mapConfig,
            engine.MapLoader.TemplateRegistry.GetAll().Where(t => !string.IsNullOrWhiteSpace(t.Id))
                .ToDictionary(t => t.Id, StringComparer.Ordinal));
        var worldBake = System.Diagnostics.Stopwatch.StartNew();
        var ingested = CrowdSimulationWorldIngest.Build(
            runtimeConfig,
            solverBoard.WidthCm,
            solverBoard.HeightCm,
            surface,
            surfaceAssetPath,
            heightAsset,
            heightAssetPath,
            mapSurface.Blockers);
        var grid = ingested.Grid;
        _surfaceGrid = grid;
        _jumpCandidates = surface.JumpCandidates;
        _mapSurfaceBlockers = mapSurface.Blockers;
        _mapSurfaceBridges = mapSurface.Bridges;
        var heights = ingested.Heights;
        _heights = heights;
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtimeConfig);
        _deck = deck;
        var cache = new NavTileCache(
            runtimeConfig.NavtileCacheCapacity, runtimeConfig.Hpa.NavCellsPerTile,
            runtimeConfig.Navmesh.MinRegionNavCellArea.ToDouble(), runtimeConfig.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            runtimeConfig.Navmesh.MaxEdgeLenNavCells.ToDouble(), runtimeConfig.Navmesh.MaxVertsPerPoly);

        var navs = new Dictionary<int, NavContext>();
        var navByLayerRadius = new Dictionary<(int, int), NavContext>();
        var radiusClasses = CrowdDeployment.DistinctRadiusClasses(runtimeConfig);
        var seen = new HashSet<int>();
        for (int a = 0; a < runtimeConfig.AgentTypes.Count; a++)
        {
            foreach (int clearance in runtimeConfig.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceNavCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtimeConfig, grid, heights, deck, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;
                navs[nav.Id] = nav;
            }
        }

        foreach (var profile in runtimeConfig.Profiles)
        {
            int rIdx = CrowdDeployment.IndexOfRadiusClass(radiusClasses, (int)profile.RadiusCm.ToInt());
            navByLayerRadius[(profile.AgentTypeIndex, rIdx)] = navs[profile.NavContextId];
        }

        worldBake.Stop();
        WorldBakeRecorded = true;
        WorldBakeMs = worldBake.Elapsed.TotalMilliseconds;
        WorldBakeCacheHits = cache.Hits;
        WorldBakeCacheMisses = cache.Misses;

        // 呈现接线：模板键、黑板键、集合键全部预解析成 id。
        var templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 EntityTemplateKeyRegistry 服务。");
        var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 PresentationStableIdAllocator 服务。");
        var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 EntityCollectionStore 服务。");

        int sessionTemplateKeyId = templateKeys.GetId(SessionTemplateId);
        if (sessionTemplateKeyId <= 0)
        {
            throw new InvalidOperationException(
                $"CrowdSimulation 会话模板 '{SessionTemplateId}' 未注册(应由 CrowdSimulationMod 的 Entities/templates.json 提供)。");
        }

        _sessionEntity = engine.World.Create(
            new Name { Value = "CrowdSimulation.Session" },
            new PresentationStableId { Value = stableIds.Allocate() },
            new Gameplay.Spawning.EntityTemplateKeyRef { TemplateKeyId = sessionTemplateKeyId });

        // 单位模板映射:unitTypes[].templates 按(兵种 × 半径级)声明,模板自身声明 profile;
        // 在此预解析成 id 表并做闭包校验(与无头装配共用 CrowdSimPresentationWiring 同一口径)。
        var templateRegistry = engine.MapLoader.TemplateRegistry;
        var unitTypeTemplates = CrowdSimPresentationWiring.BuildUnitTypeTemplates(
            runtimeConfig, templateRegistry, templateKeys, agentProfiles, CrowdSimulationConfigPath);

        var spawnSystem = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnSystem)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 RuntimeEntitySpawnSystem 服务。");
        var spawnReceipts = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 RuntimeEntitySpawnReceiptQueue 服务。");
        var spawnChannels = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 RuntimeEntitySpawnReceiptChannelRegistry 服务。");
        if (!spawnChannels.TryGetId(CrowdSimUnits.ReceiptChannelName, out int spawnChannelId))
        {
            spawnChannelId = spawnChannels.Register(CrowdSimUnits.ReceiptChannelName);
        }

        int playerCount = runtimeConfig.PairRules.PlayerCount;
        var playerReps = new Entity[playerCount + 1];
        for (int player = 1; player <= playerCount; player++)
        {
            playerReps[player] = players.Get(player);
        }

        var wiring = new CrowdSimPresentationWiring
        {
            StableIds = stableIds,
            TemplateRegistry = templateRegistry,
            TemplateKeys = templateKeys,
            TemplatesByUnitTypeRadius = unitTypeTemplates,
            RadiusMetersBlackboardKeyId = ConfigKeyRegistry.Register(Keys.UnitRadiusKey),
            SelectedCollectionKeyId = collections.KeyRegistry.Register(SelectedCollectionKey),
            HiddenTemplateTagId = RequireHiddenTemplateTagId(),
            Collections = collections,
            Spawns = spawnSystem,
            SpawnReceipts = spawnReceipts,
            SpawnReceiptChannelId = spawnChannelId,
            CollectionApplier = engine.GetService(CoreServiceKeys.CollectionApplier),
            PlayerReps = playerReps,
            Ownership = engine.GetService(CoreServiceKeys.OwnershipResolver),
        };

        _session = new CrowdSimSession(runtimeConfig, engine.World, navs, navByLayerRadius, wiring);
        // S7 结构动态化:仓(静态阻挡物入仓,动态建造/拆除共用一套重标)+ 增量重烘源 + tile 缓存。
        // 阻挡盒索引取仓的 CSR（外扩 ReachCm）。S5 对拍会话不装阻挡盒。
        var structures = Ludots.Core.CrowdSimulation.Structures.CrowdStructuresStore.Build(runtimeConfig, grid, mapSurface.Blockers);
        InstallMapBridges(structures, runtimeConfig, mapSurface.Bridges); // 桥入认知仓，按给出的 span 顺序续号
        _session.Structures = structures;
        _session.RebakeSources = new Ludots.Core.CrowdSimulation.Nav.CrowdRebakeSources(
            runtimeConfig, heights, deck, surface.JumpCandidates);
        _session.NavTileCache = cache;
        // 调试开关:每次结构变更后跑增量 vs 全量一致性检查。没有调试配置时不做。
        _session.VerifyIncrementalNav = _debug?.Session?.IncrementalNavVerify == true;
        _activeMapId = mapId.Value;

        // S5 移动:规划器(路径服务,固定生效帧)+ 运动内核 + 静态阻挡盒索引
        // (有结构仓时 Blockers 跟随仓的活 CSR,这里的静态索引不生效)
        _pathService?.Dispose();
        TimeSpan planTimeout = RequirePlanTimeout();
        _pathService = new CrowdSimulation.Nav.Pathing.PathQueryService(navs, runtimeConfig, workerThreads: 1, planTimeout, _session.ResolveNavContext);
        var planner = new CrowdSimulation.Movement.CrowdSimPlanner(_session, _pathService);
        var kernel = CrowdSimulation.Movement.CrowdMovementKernel.Create(_session);
        _session.BindVision(
            engine.GetService(CoreServiceKeys.VisionFogLayerRegistry),
            engine.GetService(CoreServiceKeys.VisionFogFieldStore));
        _session.EnableMovement(kernel, planner);
        if (_session.Fog != null &&
            engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) &&
            seats != null)
        {
            engine.SetService(
                CoreServiceKeys.SeatVisionScope,
                new CrowdSeatVisionScope(seats, () =>
                    engine.TryGetService(CoreServiceKeys.CrowdSimulationSession, out CrowdSimSession? session)
                        ? session
                        : null));
        }
        _session.SetStaticBlockers(CrowdSimulation.Movement.CrowdBlockerColliders.Build(
            mapSurface.Blockers, runtimeConfig.NavCellsPerSide, runtimeConfig.NavCellSizeCm));

        // 玩家调色板:deploy.bases[].color 要么全有要么全无。全有才注册；全无则不注册。
        int maxPlayer = runtimeConfig.Deploy.Bases.Count == 0 ? 0 : runtimeConfig.Deploy.Bases.Max(b => b.PlayerId);
        var palette = new Vector4[maxPlayer + 1];
        bool anyColor = false;
        foreach (var b in runtimeConfig.Deploy.Bases)
        {
            if (string.IsNullOrWhiteSpace(b.Color)) continue;
            palette[b.PlayerId] = TeamColorPalette.ParseHex(b.Color, $"deploy.bases player {b.PlayerId}");
            anyColor = true;
        }

        if (anyColor) engine.SetService(CoreServiceKeys.TeamColorPalette, new TeamColorPalette(palette));

        // 脚本与自动回放(指令流即数据;脚本与对拍资产同构,演示自己跑一遍规范场景)
        _pendingScript.Clear();
        if (_debug?.Session?.Script is { Length: > 0 } script)
        {
            var rows = new JsonArray();
            foreach (var entry in script)
            {
                rows.Add(new JsonObject { ["tick"] = entry.Tick, ["cmd"] = entry.Cmd.DeepClone() });
            }

            _demoScript = CrowdDemoScript.Load(rows);
            _demoScript.ValidateStructure(_session);
            _session.StageDeferredCommands = _demoScript.StageFormable;
        }

        _autoReplayAtTick = _debug?.Session?.AutoReplayAtTick ?? -1;
        _autoReplayDone = false;
        if (_autoReplayAtTick >= 0) _session.DiagnosticDigest = true;
        ReplayStatus = 0;
        ReplayDivergenceTick = -1;
        ReplayDivergenceSection = "";
        ReplayDiagnosticTick = -1;
        ReplayDivergenceDiagnostic = CrowdSimDiagnosticSection.None;
        _hashCount = 0;
        _hashHead = 0;

        engine.SetService(CoreServiceKeys.CrowdSimulationSession, _session);
        engine.SetService(CoreServiceKeys.CrowdSimulationRuntime, this);
        _pathReplyPending = false;
        engine.SetService(CoreServiceKeys.PresentationAlphaHold, this);
        // 演示相机:debug 配置指名一个虚拟相机(数据),经引擎现成的 VirtualCameraRequest 通道切换
        if (_debug?.Session?.CameraId is { } cameraId && !string.IsNullOrWhiteSpace(cameraId))
        {
            engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new Ludots.Core.Gameplay.Camera.VirtualCameraRequest { Id = cameraId };
            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"CrowdSimulation demo camera switch requested: {cameraId}.");
        }

        Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
            $"CrowdSimulation session activated for map '{_activeMapId}': navs={navs.Count}, script={_demoScript?.Count ?? 0}, autoReplayAt={_autoReplayAtTick}.");
    }

    private void Deactivate(GameEngine engine)
    {
        _debugFuzz = null;
        _debugFuzzStepped = false;
        DebugFuzzLine = DebugFuzzIdleLine;
        DebugFuzzFailed = false;
        DebugFuzzRan = false;
        _session?.Units.Clear();
        _session?.Units.ReleaseSpawnInitializer();
        _pathService?.Dispose();
        _pathService = null;
        if (engine.World.IsAlive(_sessionEntity)) engine.World.Destroy(_sessionEntity);
        _sessionEntity = Entity.Null;
        _session = null;
        _pairRulesGuard = null;
        _activeMapId = null;
        _hashCount = 0;
        _hashHead = 0;
        _pendingScript.Clear();
        _demoScript = null;
        _engine = null;
        ReplayStatus = 0;
        ReplayDivergenceTick = -1;
        ReplayDivergenceSection = "";
        ReplayDiagnosticTick = -1;
        ReplayDivergenceDiagnostic = CrowdSimDiagnosticSection.None;
        _pathReplyPending = false;
        _orderPrefs = null;
        engine.RemoveService(CoreServiceKeys.CrowdSimulationSession);
        engine.RemoveService(CoreServiceKeys.CrowdSimulationRuntime);
        engine.RemoveService(CoreServiceKeys.TeamColorPalette);
        engine.RemoveService(CoreServiceKeys.PresentationAlphaHold);
    }

    /// <summary>固定步进一次(系统组每个 FixedHz tick 调一次);答复未按生效帧返回时本 tick 停摆。</summary>
    public void Tick()
    {
        var session = _session;
        if (session == null) return;
        // 系统组 FixedHz 的 tick 边界，先于本 tick 的指令和运动。
        // 先重读玩家代表的 Team。队伍变了就抛。修订号变了就在临时缓冲重算；与现场矩阵不同也抛，不写回。相同则只更新修订号。
        (_pairRulesGuard ?? throw new InvalidOperationException("CrowdSimulation 会话缺少功能位修订检查。"))
            .CheckAtTickBoundary();
        bool fuzzStepped = ConsumeDebugFuzzStepped();
        if (!fuzzStepped && session.PlaybackHeld)
        {
            session.FlushDueCommands();
            return;
        }

        long t0 = _tickWatch.ElapsedTicks;
        if (!fuzzStepped && !session.Step(out _))
        {
            _pathReplyPending = true;
            if (!_stallLogged)
            {
                _stallLogged = true;
                Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                    $"CrowdSimulation stalled at tick {session.TickCount}: waiting for path reply (planner pending={session.Planner?.PendingCount ?? -1}, serviceFault={_pathService?.Faulted}).");
            }

            session.PublishTelemetryFrame(stalled: true,
                (double)(_tickWatch.ElapsedTicks - t0) / System.Diagnostics.Stopwatch.Frequency * 1000.0);
            return;
        }

        session.PublishTelemetryFrame(stalled: false,
            (double)(_tickWatch.ElapsedTicks - t0) / System.Diagnostics.Stopwatch.Frequency * 1000.0);
        _stallLogged = false;
        _pathReplyPending = false;
        if (_hashCount == _hashDigests.Length)
        {
            _hashDigests[_hashHead] = session.LastDigest;
            _hashTicks[_hashHead] = session.TickCount;
            _hashHead = (_hashHead + 1) % _hashDigests.Length;
        }
        else
        {
            int w = (_hashHead + _hashCount) % _hashDigests.Length;
            _hashDigests[w] = session.LastDigest;
            _hashTicks[w] = session.TickCount;
            _hashCount++;
        }
        if (!_autoReplayDone && _autoReplayAtTick >= 0 && session.TickCount >= _autoReplayAtTick)
        {
            _autoReplayDone = true;
            RunReplay();
            string diag = CrowdSimChecksum.FormatDiagnostic(ReplayDivergenceDiagnostic);
            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                diag.Length == 0
                    ? $"CrowdSimulation replay at tick {session.TickCount}: status={ReplayStatus} divergenceTick={ReplayDivergenceTick} section={ReplayDivergenceSection}."
                    : $"CrowdSimulation replay at tick {session.TickCount}: status={ReplayStatus} divergenceTick={ReplayDivergenceTick} section={ReplayDivergenceSection} diag differs: {diag} @tick {ReplayDiagnosticTick}.");
        }

        if (session.TickCount % session.Config.FixedHz == 0)
        {
            Ludots.Core.Diagnostics.Log.Dbg(in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"CrowdSimulation tick {session.TickCount}: units={session.Units.Count}, selected={FormatLocalSeatSelected(session)}, hash={session.LastDigest.Total}");
        }
    }

    /// <summary>回放:一次性世界重放同一指令日志,按 tick 对上环里还在的校验码(不进引擎世界,不碰呈现)。</summary>
    public void RunReplay()
    {
        var session = _session;
        if (session == null) return;
        // Flush 只在执行时写日志。尚未到点的脚本行还在队列里，回放输入是日志再接上这段队列。
        var log = session.Commands.Log;
        int pending = session.Commands.PendingCount;
        var entries = new CrowdCommand[log.Count + pending];
        for (int i = 0; i < log.Count; i++)
        {
            var e = log[i];
            entries[i] = new CrowdCommand(e.Tick, e.Cmd);
        }

        for (int i = 0; i < pending; i++)
        {
            var e = session.Commands.PendingAt(i);
            entries[log.Count + i] = new CrowdCommand(e.Tick, e.Cmd);
        }
        int ticks = session.TickCount;
        // 回放派生接线:共享模板数据,稳定 id 全新分配。不挂集合写者与 owns,
        // 清空单位时不改活集合和关系图。
        var replayWiring = new CrowdSimPresentationWiring
        {
            StableIds = new PresentationStableIdAllocator(),
            TemplateRegistry = session.Presentation.TemplateRegistry,
            TemplateKeys = session.Presentation.TemplateKeys,
            TemplatesByUnitTypeRadius = session.Presentation.TemplatesByUnitTypeRadius,
            RadiusMetersBlackboardKeyId = session.Presentation.RadiusMetersBlackboardKeyId,
            SelectedCollectionKeyId = session.Presentation.SelectedCollectionKeyId,
            HiddenTemplateTagId = session.Presentation.HiddenTemplateTagId,
            Collections = session.Presentation.Collections,
        };
        var replay = new CrowdSimSession(session.Config, ArchWorld.Create(), session.Navs, session.NavByLayerRadius, replayWiring);
        replay.DiagnosticDigest = session.DiagnosticDigest;
        // 回放是全新仿真:结构 op 会改写导航上下文,
        // 回放共享活会话的导航即带着已生效的结构变更从头跑(首个分歧恰在首个规划落地)。
        // 有结构仓的会话,回放用烘焙输入重建仓 + 全新导航 + 独立 tile 缓存。
        if (session.Structures != null && _surfaceGrid != null && _heights != null && _deck != null && _mapSurfaceBlockers != null)
        {
            var replayNavs = new Dictionary<int, NavContext>();
            var replayCache = new NavTileCache(
                session.Config.NavtileCacheCapacity, session.Config.Hpa.NavCellsPerTile,
                session.Config.Navmesh.MinRegionNavCellArea.ToDouble(), session.Config.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
                session.Config.Navmesh.MaxEdgeLenNavCells.ToDouble(), session.Config.Navmesh.MaxVertsPerPoly);
            var seenIds = new HashSet<int>();
            for (int a = 0; a < session.Config.AgentTypes.Count; a++)
            {
                foreach (int clearance in session.Config.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceNavCells).Distinct())
                {
                    var nav = NavContextBaker.Bake(session.Config, _surfaceGrid, _heights, _deck, a, clearance, replayCache);
                    if (!seenIds.Add(nav.Id)) continue;
                    replayNavs[nav.Id] = nav;
                }
            }

            replay.ReplaceNavs(replayNavs);
            replay.Structures = Ludots.Core.CrowdSimulation.Structures.CrowdStructuresStore.Build(
                session.Config, _surfaceGrid, _mapSurfaceBlockers);
            InstallMapBridges(replay.Structures, session.Config, _mapSurfaceBridges); // 回放仓按同一 span 列表补桥
            replay.RebakeSources = new CrowdSimulation.Nav.CrowdRebakeSources(
                session.Config, _heights, _deck, _jumpCandidates ?? Array.Empty<CrowdSimulation.World.NavSurfaceJumpCandidate>());
            replay.NavTileCache = replayCache;
        }
        // 回放会话同样挂运动栈(一次性世界 + 自己的路径服务;不入引擎世界,不碰呈现)
        if (session.Movement != null)
        {
            using var replayService = new CrowdSimulation.Nav.Pathing.PathQueryService(
                replay.Navs, session.Config, workerThreads: 1, RequirePlanTimeout(), replay.ResolveNavContext);
            replay.BindVision(new FogLayerRegistry(), new FogFieldStore());
            replay.EnableMovement(
                CrowdSimulation.Movement.CrowdMovementKernel.Create(replay),
                new CrowdSimulation.Movement.CrowdSimPlanner(replay, replayService));
            replay.SetStaticBlockers(session.Structures == null ? session.Blockers : null);
            replay.BlockOnDueReplies = true;
            replay.Commands.Schedule(replay, entries);
            var replayDigests = new List<CrowdSimStateDigest>(ticks);
            replay.Advance(ticks, digests: replayDigests);
            CompareReplay(replayDigests);
            return;
        }

        replay.Commands.Schedule(replay, entries);
        var replayDigests2 = new List<CrowdSimStateDigest>(ticks);
        replay.Advance(ticks, digests: replayDigests2);
        CompareReplay(replayDigests2);
    }

    private void CompareReplay(List<CrowdSimStateDigest> replayDigests)
    {
        bool chained = false;
        int chainedTick = -1;
        string section = "";
        bool diagnostic = false;
        int diagnosticTick = -1;
        var mask = CrowdSimDiagnosticSection.None;
        bool missing = false;
        int n = _hashDigests.Length;
        for (int i = 0; i < _hashCount; i++)
        {
            int w = (_hashHead + i) % n;
            int tick = _hashTicks[w];
            int index = tick - 1;
            if ((uint)index >= (uint)replayDigests.Count || replayDigests[index].Tick != tick)
            {
                missing = true;
                break;
            }

            bool chainedHere = CrowdSimChecksum.TryFirstSection(
                _hashDigests[w], replayDigests[index], out string name, out CrowdSimDiagnosticSection flags);
            if (!chained && chainedHere)
            {
                chained = true;
                chainedTick = tick;
                section = name;
            }

            if (!diagnostic && flags != CrowdSimDiagnosticSection.None)
            {
                diagnostic = true;
                diagnosticTick = tick;
                mask = flags;
            }

            if (chained && diagnostic) break;
        }

        if (chained)
        {
            ReplayDivergenceTick = chainedTick;
            ReplayDivergenceSection = section;
        }
        else if (missing)
        {
            ReplayDivergenceTick = -2;
            ReplayDivergenceSection = "";
        }
        else
        {
            ReplayDivergenceTick = -1;
            ReplayDivergenceSection = "";
        }

        ReplayDiagnosticTick = diagnostic ? diagnosticTick : -1;
        ReplayDivergenceDiagnostic = diagnostic ? mask : CrowdSimDiagnosticSection.None;
        ReplayStatus = chained || missing ? 2 : 1;
    }

    public bool TryLocalSeatPlayerId(out int playerId)
    {
        playerId = 0;
        if (_engine == null) return false;
        var seats = _engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry);
        if (seats == null || !seats.TryGetSoleSeat(out Client.ClientLocalSeat seat)) return false;
        if (seat.PossessedPlayerId <= 0) return false;
        playerId = seat.PossessedPlayerId;
        return true;
    }

    private string FormatLocalSeatSelected(CrowdSimSession session)
        => TryLocalSeatPlayerId(out int playerId)
            ? session.SelectedCollectionCount(playerId).ToString()
            : "-";

    private static int RequireHiddenTemplateTagId()
    {
        int id = TagRegistry.GetId(CrowdSimPresentationWiring.HiddenTemplateTag);
        if (id <= 0)
        {
            throw new InvalidOperationException(
                "crowd_simulation.hidden_template 未登记。不参与全体下令的模板在模板数据里带上这个标签。");
        }

        return id;
    }

    private static string RequireHeightAssetPath(MapConfig mapConfig, MapId mapId)
    {
        string? asset = mapConfig.ContinuousHeightmap?.Asset;
        if (string.IsNullOrWhiteSpace(asset))
        {
            throw new InvalidOperationException($"地图 '{mapId.Value}' 未声明 ContinuousHeightmap.Asset,CrowdSimulation 会话需要高度场。");
        }

        return asset;
    }

    /// <summary>VFS 跨 mod 解析(与 GameEngine.TryResolveSingleExistingUri 同一约定:多 mod 命中即冲突)。</summary>
    internal static Stream OpenAsset(GameEngine engine, string relPath)
    {
        string rel = relPath.Replace('\\', '/');
        string? found = null;
        int hits = 0;
        for (int i = 0; i < engine.ModLoader.LoadedModIds.Count; i++)
        {
            string modId = engine.ModLoader.LoadedModIds[i];
            if (!engine.VFS.TryResolveFullPath($"{modId}:{rel}", out string? full) || !File.Exists(full)) continue;
            hits++;
            found = full;
        }

        if (hits > 1) throw new InvalidOperationException($"资产冲突(多个 mod 命中): {rel}");
        if (hits == 0) throw new FileNotFoundException($"CrowdSimulation 资产缺失: {rel}");
        return File.OpenRead(found!);
    }

    /// <summary>按完整 mod URI 直开("ModId:path" 形式,配置里声明的显式来源)。</summary>
    internal static Stream OpenUri(GameEngine engine, string uri)
    {
        if (!engine.VFS.TryResolveFullPath(uri, out string? full) || !File.Exists(full))
        {
            throw new FileNotFoundException($"CrowdSimulation 资产缺失: {uri}");
        }

        return File.OpenRead(full);
    }
}
