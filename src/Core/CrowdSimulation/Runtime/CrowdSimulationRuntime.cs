using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// CrowdSimulation 会话宿主(引擎持有,MassNavigationRuntime 同款形态):
/// 配置目录带 CrowdSimulationConfig.json 且调试配置声明 session.autostart 时,
/// 地图聚焦即建会话——会话跑在引擎世界上,单位带呈现投影接线,
/// 部署/脚本/回放都是指令流(数据),回放另起一次性世界逐帧对拍校验码。
/// </summary>
public sealed class CrowdSimulationRuntime
{
    private CrowdSimulationConfig? _configDto;
    private JsonObject? _debug;
    private bool _configResolved;

    private CrowdSimSession? _session;
    private Entity _sessionEntity;

    /// <summary>地图桥入认知仓(F02):桥模板按 id 查配置表(缺失 = 配置没有 bridge 模板,
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
    private PresenterEntityRuntime? _presenterRuntime;
    private CrowdSimulation.Nav.Pathing.PathQueryService? _pathService;
    /// <summary>地图烘焙输入(回放会话重建结构仓与全新导航用;与活会话的仓互不共享可变栅格)。</summary>
    private CrowdSimulation.World.SurfaceGrid? _surfaceGrid;
    private CrowdSimulation.World.NavSurfaceJumpCandidate[]? _jumpCandidates;
    private CrowdSimulation.World.NavHeightField? _heights;
    private CrowdSimulation.Nav.DeckSurface? _deck;
    private IReadOnlyList<CrowdSimulation.World.BlockerFootprint>? _mapSurfaceBlockers;
    private IReadOnlyList<CrowdSimulation.World.BridgeDeckRecord>? _mapSurfaceBridges;
    private bool _stallLogged;
    private readonly System.Diagnostics.Stopwatch _tickWatch = System.Diagnostics.Stopwatch.StartNew();
    // 校验码历史(回放对拍用):定长环形,零逐 tick 分配
    private readonly int[] _hashTicks = new int[4096];
    private readonly ulong[] _hashValues = new ulong[4096];
    private int _hashCount, _hashHead;
    private readonly List<CrowdCommand> _pendingScript = new();
    private int _autoReplayAtTick = -1;
    private bool _autoReplayDone;

    public CrowdSimSession? Session => _session;
    public Entity SessionEntity => _sessionEntity;
    /// <summary>回放结论:0 未跑 / 1 逐位一致 / 2 分歧(分歧 tick 见黑板)。</summary>
    /// <summary>人群遥测(类型化计数/采样;看板与调试面板只读)。</summary>
    public CrowdSimulationTelemetry Telemetry { get; } = new();

    public int ReplayStatus { get; private set; }
    public int ReplayDivergenceTick { get; private set; } = -1;

    /// <summary>停用本次激活的自动回放:现场指令(玩家交互)进入会话后,自动回放的对拍自证
    /// 不再是"同输入重放"。只拦自动触发;RunReplay 与对拍语义本身不动。</summary>
    public void SuppressAutoReplay() => _autoReplayDone = true;

    /// <summary>blackboard 键(呈现侧 ownerBlackboardFloat 绑定的数据契约)。</summary>
    public static class Keys
    {
        public const string UnitRadiusM = "crowd_simulation.unit.radius_m";
        public const string SessionTick = "crowd_simulation.session.tick";
        public const string SessionUnits = "crowd_simulation.session.units";
        public const string SessionSelected = "crowd_simulation.session.selected";
        public const string SessionReplayStatus = "crowd_simulation.session.replay_status";
        public const string SessionReplayDivergenceTick = "crowd_simulation.session.replay_divergence_tick";
        // F02 迷雾 HUD(观察玩家 = 玩家 1 的视野组):可见/已探索 fog 格数、认知实体数、
        // 残影数(认知有真相无)、乐观迷雾开关。渲染底座(W2)经迷雾查询面画图。
        public const string SessionFogVisibleCells = "crowd_simulation.session.fog.visible_cells";
        public const string SessionFogExploredCells = "crowd_simulation.session.fog.explored_cells";
        public const string SessionFogBeliefEntities = "crowd_simulation.session.fog.belief_entities";
        public const string SessionFogGhosts = "crowd_simulation.session.fog.ghosts";
        public const string SessionFogTerrainOptimistic = "crowd_simulation.session.fog.terrain_optimistic";
    }

    /// <summary>统计键注册一次、缓存整型 id——逐 tick 写入不再做字符串哈希查表。</summary>
    private static class StatKeyIds
    {
        public static readonly int Tick = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionTick);
        public static readonly int Units = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionUnits);
        public static readonly int Selected = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionSelected);
        public static readonly int ReplayStatus = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionReplayStatus);
        public static readonly int ReplayDivergenceTick = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionReplayDivergenceTick);
        public static readonly int FogVisible = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionFogVisibleCells);
        public static readonly int FogExplored = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionFogExploredCells);
        public static readonly int FogBelief = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionFogBeliefEntities);
        public static readonly int FogGhosts = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionFogGhosts);
        public static readonly int FogTerrain = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(Keys.SessionFogTerrainOptimistic);
    }

    /// <summary>迷雾观察口径(F02 演示):-1 = 全知 debug 视图(现状默认,玩家视角迷雾用键切换,
    /// S7 spec 要求两种观察范围并存);≥ 0 = 只看该视野组。渲染底座(W2)读它决定画哪份
    /// View(),HUD 计数恒按玩家 1 的视野组。</summary>
    public int FogViewGroup { get; set; } = -1;

    public bool HandleMapFocused(GameEngine engine, MapId mapId, MapConfig mapConfig)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!TryEnsureConfig(engine) || _configDto == null || _debug == null) return false;
        if (!string.Equals(mapId.Value, _configDto.MapId, StringComparison.Ordinal)) return false;
        if (!IsAutostartEnabled(_debug)) return false;

        Activate(engine, mapId, mapConfig);
        return true;
    }

    public bool HandleMapUnloaded(GameEngine engine, MapId mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (_activeMapId == null || !string.Equals(mapId.Value, _activeMapId, StringComparison.Ordinal)) return false;
        Deactivate(engine);
        return true;
    }

    public bool HandleMapSuspended(GameEngine engine, MapId mapId) => HandleMapUnloaded(engine, mapId);

    private bool TryEnsureConfig(GameEngine engine)
    {
        if (_configResolved) return _configDto != null;
        _configResolved = true;

        var catalog = engine.ConfigCatalog;
        if (catalog == null ||
            !catalog.TryGet(CrowdSimulationConfigPath, out var configEntry) ||
            !catalog.TryGet(CrowdSimulationDebugPath, out var debugEntry))
        {
            return false;
        }

        var merged = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in configEntry, engine.ConfigConflictReport)
            ?? throw new InvalidOperationException($"{CrowdSimulationConfigPath} 存在目录登记但合并为空。");
        _configDto = CrowdSimulationConfig.Load(merged);
        _debug = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in debugEntry, engine.ConfigConflictReport)
            ?? throw new InvalidOperationException($"{CrowdSimulationDebugPath} 存在目录登记但合并为空。");
        return true;
    }

    private const string CrowdSimulationConfigPath = "CrowdSimulationConfig.json";
    private const string CrowdSimulationDebugPath = "CrowdSimulationDebug.json";
    private const string SessionTemplateId = "crowd_simulation.session";
    private const string SelectedCollectionKey = SelectionMirror.CollectionKey;

    private static bool IsAutostartEnabled(JsonObject debug) =>
        debug["session"]?["autostart"]?.GetValue<bool>() == true;

    private void Activate(GameEngine engine, MapId mapId, MapConfig mapConfig)
    {
        if (!_systemsInstalled)
        {
            _systemsInstalled = true;
            // 会话步进是 PostMovement 组的移动生产者,必须排在 SpatialPartitionUpdateSystem 之前:
            // 分区同步按 PreviousWorldPositionCm != WorldPositionCm 判定本 tick 是否移动,步进晚于
            // 同步会让该判定恒为 false,成员格冻结在出生位置。
            engine.InsertSystemBeforeRequired<SpatialPartitionUpdateSystem>(
                new CrowdSimulationSessionTickSystem(this), SystemGroup.PostMovement);
        }

        // 体型档案:配置显式声明来源(crowd 会话的半径级只属于它自己的档案集);
        // 引擎全局注册表是多特性合并产物,直接拿会把无关小体型并进半径级(真实事故)。
        AgentProfileRegistry agentProfiles;
        if (!string.IsNullOrWhiteSpace(_configDto!.Agents.ProfilesUri))
        {
            var profileList = JsonSerializer.Deserialize<List<AgentProfileConfig>>(
                OpenUri(engine, _configDto.Agents.ProfilesUri), StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException($"CrowdSimulation 体型档案反序列化为空: {_configDto.Agents.ProfilesUri}");
            agentProfiles = new AgentProfileRegistry(profileList);
        }
        else
        {
            agentProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
                ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 AgentProfiles 服务。");
        }

        var runtimeConfig = CrowdSimulationConfigLoader.Load(
            _configDto!, mapConfig, agentProfiles, (int)MathF.Round(1f / Time.FixedDeltaTime));

        // 模板级契约(双求解器 Agent 互斥/模板 profileId 存在/阻挡物正方形 Box)在进图激活时执行,
        // 与配置门禁构成同一条装载检查链;失败即报错,不进入烘焙与生成。
        CrowdSimulationAuthoringContract.Validate(
            engine.MapLoader.TemplateRegistry.GetAll(), _configDto!, mapConfig, agentProfiles);

        // ── 资产(走 VFS,跨 mod 解析,冲突即 fail-fast)──
        var surface = NavSurfaceAsset.Read(OpenAsset(engine, "assets/" + runtimeConfig.SurfaceAsset));
        var mapSurface = CrowdSimulationMapSurfaceSource.Extract(
            mapConfig,
            engine.MapLoader.TemplateRegistry.GetAll().Where(t => !string.IsNullOrWhiteSpace(t.Id))
                .ToDictionary(t => t.Id, StringComparer.Ordinal));
        var grid = SurfaceGrid.Build(runtimeConfig, surface, mapSurface.Blockers);
        _surfaceGrid = grid;
        _jumpCandidates = surface.JumpCandidates;
        _mapSurfaceBlockers = mapSurface.Blockers;
        _mapSurfaceBridges = mapSurface.Bridges;
        var heights = NavHeightField.FromHeightmap(
            ContinuousHeightmapBinary.Read(OpenAsset(engine, RequireHeightAssetPath(mapConfig, mapId))),
            runtimeConfig.NavCellCount, runtimeConfig.NavCellSizeCm);
        _heights = heights;
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtimeConfig);
        _deck = deck;
        var cache = new NavTileCache(
            runtimeConfig.NavtileCacheCapacity, runtimeConfig.Hpa.ClusterSize,
            runtimeConfig.Navmesh.MinRegionArea.ToDouble(), runtimeConfig.Navmesh.MaxSimplificationError.ToDouble(),
            runtimeConfig.Navmesh.MaxEdgeLen.ToDouble(), runtimeConfig.Navmesh.MaxVertsPerPoly);

        var navs = new Dictionary<int, NavContext>();
        var navByLayerRadius = new Dictionary<(int, int), NavContext>();
        var radiusClasses = CrowdDeployment.DistinctRadiusClasses(runtimeConfig);
        var seen = new HashSet<int>();
        for (int a = 0; a < runtimeConfig.AgentTypes.Count; a++)
        {
            foreach (int clearance in runtimeConfig.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtimeConfig, grid, heights, deck, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;
                navs[nav.Id] = nav;
            }
        }

        foreach (var profile in runtimeConfig.Profiles)
        {
            int rIdx = radiusClasses.IndexOf((int)profile.RadiusCm.ToInt());
            navByLayerRadius[(profile.AgentTypeIndex, rIdx)] = navs[profile.NavContextId];
        }

        // ── 呈现接线(模板键/黑板键/集合键全部预解析成 id)──
        var templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 EntityTemplateKeyRegistry 服务。");
        var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 PresentationStableIdAllocator 服务。");
        var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("CrowdSimulation 会话宿主需要 EntityCollectionStore 服务。");
        _presenterRuntime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);

        int sessionTemplateKeyId = templateKeys.GetId(SessionTemplateId);
        if (sessionTemplateKeyId <= 0)
        {
            throw new InvalidOperationException(
                $"CrowdSimulation 会话模板 '{SessionTemplateId}' 未注册(应由 CrowdSimulationMod 的 Entities/templates.json 提供)。");
        }

        _sessionEntity = engine.World.Create(
            new Name { Value = "CrowdSimulation.Session" },
            new PresentationStableId { Value = stableIds.Allocate() },
            new Gameplay.Spawning.EntityTemplateKeyRef { TemplateKeyId = sessionTemplateKeyId },
            new BlackboardFloatBuffer());

        // 单位模板映射:unitTypes[].templates 按(兵种 × 半径级)声明,模板自身声明 profile;
        // 在此预解析成 id 表并做闭包校验(与无头装配共用 CrowdSimPresentationWiring 同一口径)。
        var templateRegistry = engine.MapLoader.TemplateRegistry;
        var unitTypeTemplates = CrowdSimPresentationWiring.BuildUnitTypeTemplates(
            runtimeConfig, templateRegistry, templateKeys, agentProfiles, CrowdSimulationConfigPath);

        var wiring = new CrowdSimPresentationWiring
        {
            StableIds = stableIds,
            TemplateRegistry = templateRegistry,
            TemplatesByUnitTypeRadius = unitTypeTemplates,
            RadiusMetersBlackboardKeyId = ConfigKeyRegistry.Register(Keys.UnitRadiusM),
            SelectionOwner = _sessionEntity,
            SelectedCollectionKeyId = collections.KeyRegistry.Register(SelectedCollectionKey),
            Collections = collections,
        };

        _session = new CrowdSimSession(runtimeConfig, engine.World, navs, navByLayerRadius, wiring);
        // S7 结构动态化:仓(静态阻挡物入仓,动态建造/拆除共用一套重标)+ 增量重烘源 + tile 缓存。
        // 阻挡盒索引取仓的 CSR(reach 生长,参考同形;S5 对拍会话不装阻挡盒,不受影响)。
        var structures = Ludots.Core.CrowdSimulation.Structures.CrowdStructuresStore.Build(runtimeConfig, grid, mapSurface.Blockers);
        InstallMapBridges(structures, runtimeConfig, mapSurface.Bridges); // F02:桥入认知仓(id 与导出端参考序同构)
        _session.Structures = structures;
        _session.RebakeSources = new Ludots.Core.CrowdSimulation.Nav.CrowdRebakeSources(
            runtimeConfig, heights, deck, surface.JumpCandidates);
        _session.NavTileCache = cache;
        // DB-03 调试开关:每次结构变更后跑增量 vs 全量一致性检查(debug 配置驱动)
        _session.VerifyIncrementalNav = _debug["session"]?["incrementalNavVerify"]?.GetValue<bool>() == true;
        _activeMapId = mapId.Value;

        // S5 移动:规划器(路径服务,固定生效帧)+ 运动内核 + 静态阻挡盒索引
        // (有结构仓时 Blockers 跟随仓的活 CSR,这里的静态索引不生效)
        _pathService?.Dispose();
        _pathService = new CrowdSimulation.Nav.Pathing.PathQueryService(navs, runtimeConfig, workerThreads: 1, TimeSpan.FromSeconds(5), _session.ResolveNavContext);
        var planner = new CrowdSimulation.Movement.CrowdSimPlanner(_session, _pathService);
        var kernel = CrowdSimulation.Movement.CrowdMovementKernel.Create(_session);
        _session.EnableMovement(kernel, planner);
        _session.SetStaticBlockers(CrowdSimulation.Movement.CrowdBlockerColliders.Build(
            mapSurface.Blockers, runtimeConfig.NavCellCount, runtimeConfig.NavCellSizeCm));

        // 玩家调色板:deploy.bases[].color 是数据;缺色玩家落到调色板外,呈现层回退默认。
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
        if (_debug!["session"]?["script"] is JsonArray script)
        {
            foreach (var entry in script)
            {
                _pendingScript.Add(new CrowdCommand(entry!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(entry["cmd"]!)));
            }
        }

        _session.Commands.Schedule(_session, _pendingScript);
        _autoReplayAtTick = _debug["session"]?["autoReplayAtTick"]?.GetValue<int>() ?? -1;
        _autoReplayDone = false;
        ReplayStatus = 0;
        ReplayDivergenceTick = -1;
        _hashCount = 0;
        _hashHead = 0;

        engine.SetService(CoreServiceKeys.CrowdSimulationSession, _session);
        engine.SetService(CoreServiceKeys.CrowdSimulationRuntime, this);
        // 演示相机:debug 配置指名一个虚拟相机(数据),经引擎现成的 VirtualCameraRequest 通道切换
        if (_debug["session"]?["cameraId"]?.GetValue<string>() is { } cameraId && !string.IsNullOrWhiteSpace(cameraId))
        {
            engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new Ludots.Core.Gameplay.Camera.VirtualCameraRequest { Id = cameraId };
            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"CrowdSimulation demo camera switch requested: {cameraId}.");
        }

        Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
            $"CrowdSimulation session activated for map '{_activeMapId}': navs={navs.Count}, script={_pendingScript.Count}, autoReplayAt={_autoReplayAtTick}.");
    }

    private void Deactivate(GameEngine engine)
    {
        _session?.Units.Clear();
        _pathService?.Dispose();
        _pathService = null;
        if (engine.World.IsAlive(_sessionEntity)) engine.World.Destroy(_sessionEntity);
        _sessionEntity = Entity.Null;
        _session = null;
        _activeMapId = null;
        _hashCount = 0;
        _hashHead = 0;
        _pendingScript.Clear();
        ReplayStatus = 0;
        ReplayDivergenceTick = -1;
        engine.RemoveService(CoreServiceKeys.CrowdSimulationSession);
        engine.RemoveService(CoreServiceKeys.CrowdSimulationRuntime);
        engine.RemoveService(CoreServiceKeys.TeamColorPalette);
    }

    /// <summary>固定步进一次(系统组每个 FixedHz tick 调一次);答复未按生效帧返回时本 tick 停摆。</summary>
    public void Tick()
    {
        var session = _session;
        if (session == null) return;
        long t0 = _tickWatch.ElapsedTicks;
        if (!session.Step(out var hash))
        {
            if (!_stallLogged)
            {
                _stallLogged = true;
                Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                    $"CrowdSimulation stalled at tick {session.TickCount}: waiting for path reply (planner pending={session.Planner?.PendingCount ?? -1}, serviceFault={_pathService?.Faulted}).");
            }

            Telemetry.BeginFrame(session.Units.Count, 0, stalled: true,
                (double)(_tickWatch.ElapsedTicks - t0) / System.Diagnostics.Stopwatch.Frequency * 1000.0);
            return;
        }

        Telemetry.BeginFrame(session.Units.Count, 0, stalled: false,
            (double)(_tickWatch.ElapsedTicks - t0) / System.Diagnostics.Stopwatch.Frequency * 1000.0);
        _stallLogged = false;
        if (_hashCount == _hashTicks.Length)
        {
            _hashTicks[_hashHead] = session.TickCount;
            _hashValues[_hashHead] = hash;
            _hashHead = (_hashHead + 1) % _hashTicks.Length;
        }
        else
        {
            int w = (_hashHead + _hashCount) % _hashTicks.Length;
            _hashTicks[w] = session.TickCount;
            _hashValues[w] = hash;
            _hashCount++;
        }
        if (!_autoReplayDone && _autoReplayAtTick >= 0 && session.TickCount >= _autoReplayAtTick)
        {
            _autoReplayDone = true;
            RunReplay();
            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"CrowdSimulation replay at tick {session.TickCount}: status={ReplayStatus} divergenceTick={ReplayDivergenceTick}.");
        }

        if (session.TickCount % 30 == 0)
        {
            string presenters = _presenterRuntime?.BuildActiveDefinitionSummary(8) ?? "-";
            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                $"CrowdSimulation tick {session.TickCount}: units={session.Units.Count}, selected={session.SelectedCount}, hash={Units.CrowdSimChecksum.Format(hash)}, presenters=[{presenters}], elapsed={_tickWatch.ElapsedMilliseconds}ms.");
        }

        WriteStats(session);
    }

    /// <summary>回放:一次性世界重放同一指令日志,逐帧对拍校验码(不进引擎世界,不碰呈现)。</summary>
    public void RunReplay()
    {
        var session = _session;
        if (session == null) return;
        // 日志载荷是边界一次成形的 struct,回放直接复用(无需克隆,解析后只读)。
        var entries = session.Commands.Log
            .Select(e => new CrowdCommand(e.Tick, e.Cmd))
            .ToArray();
        int ticks = session.TickCount;
        // 回放派生接线:共享模板数据,稳定 id 全新分配(回放世界用后即弃),选中属主置空——
        // 选中镜像与集合仓写入都按空属主跳过,活呈现状态零触碰。
        var replayWiring = new CrowdSimPresentationWiring
        {
            StableIds = new PresentationStableIdAllocator(),
            TemplateRegistry = session.Presentation.TemplateRegistry,
            TemplatesByUnitTypeRadius = session.Presentation.TemplatesByUnitTypeRadius,
            RadiusMetersBlackboardKeyId = session.Presentation.RadiusMetersBlackboardKeyId,
            SelectionOwner = Entity.Null,
            SelectedCollectionKeyId = session.Presentation.SelectedCollectionKeyId,
            Collections = session.Presentation.Collections,
        };
        var replay = new CrowdSimSession(session.Config, ArchWorld.Create(), session.Navs, session.NavByLayerRadius, replayWiring);
        // 回放 = 全新仿真(参考 createHeadless / runReplay 同形):结构 op 会改写导航上下文,
        // 回放共享活会话的导航即带着已生效的结构变更从头跑(首个分歧恰在首个规划落地)。
        // 有结构仓的会话,回放用烘焙输入重建仓 + 全新导航 + 独立 tile 缓存。
        if (session.Structures != null && _surfaceGrid != null && _heights != null && _deck != null && _mapSurfaceBlockers != null)
        {
            var replayNavs = new Dictionary<int, NavContext>();
            var replayCache = new NavTileCache(
                session.Config.NavtileCacheCapacity, session.Config.Hpa.ClusterSize,
                session.Config.Navmesh.MinRegionArea.ToDouble(), session.Config.Navmesh.MaxSimplificationError.ToDouble(),
                session.Config.Navmesh.MaxEdgeLen.ToDouble(), session.Config.Navmesh.MaxVertsPerPoly);
            var seenIds = new HashSet<int>();
            for (int a = 0; a < session.Config.AgentTypes.Count; a++)
            {
                foreach (int clearance in session.Config.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceCells).Distinct())
                {
                    var nav = NavContextBaker.Bake(session.Config, _surfaceGrid, _heights, _deck, a, clearance, replayCache);
                    if (!seenIds.Add(nav.Id)) continue;
                    replayNavs[nav.Id] = nav;
                }
            }

            replay.ReplaceNavs(replayNavs);
            replay.Structures = Ludots.Core.CrowdSimulation.Structures.CrowdStructuresStore.Build(
                session.Config, _surfaceGrid, _mapSurfaceBlockers);
            InstallMapBridges(replay.Structures, session.Config, _mapSurfaceBridges); // F02:回放仓同构补桥
            replay.RebakeSources = new CrowdSimulation.Nav.CrowdRebakeSources(
                session.Config, _heights, _deck, _jumpCandidates ?? Array.Empty<CrowdSimulation.World.NavSurfaceJumpCandidate>());
            replay.NavTileCache = replayCache;
        }
        // 回放会话同样挂运动栈(一次性世界 + 自己的路径服务;不入引擎世界,不碰呈现)
        if (session.Movement != null)
        {
            using var replayService = new CrowdSimulation.Nav.Pathing.PathQueryService(
                replay.Navs, session.Config, workerThreads: 1, TimeSpan.FromSeconds(5), replay.ResolveNavContext);
            replay.EnableMovement(
                CrowdSimulation.Movement.CrowdMovementKernel.Create(replay),
                new CrowdSimulation.Movement.CrowdSimPlanner(replay, replayService));
            replay.SetStaticBlockers(session.Structures == null ? session.Blockers : null);
            replay.BlockOnDueReplies = true;
            replay.Commands.Schedule(replay, entries);
            var replayHashes = new List<ulong>(ticks);
            replay.Advance(ticks, replayHashes);
            CompareReplay(ticks, replayHashes);
            return;
        }

        replay.Commands.Schedule(replay, entries);
        var replayHashes2 = new List<ulong>(ticks);
        replay.Advance(ticks, replayHashes2);
        CompareReplay(ticks, replayHashes2);
    }

    private void CompareReplay(int ticks, List<ulong> replayHashes)
    {
        ReplayDivergenceTick = -1;
        int common = Math.Min(_hashCount, replayHashes.Count);
        for (int i = 0; i < common; i++)
        {
            int w = (_hashHead + i) % _hashTicks.Length;
            if (_hashValues[w] != replayHashes[i]) { ReplayDivergenceTick = _hashTicks[w]; break; }
        }

        if (ReplayDivergenceTick < 0 && _hashCount != replayHashes.Count) ReplayDivergenceTick = -2;
        ReplayStatus = ReplayDivergenceTick == -1 ? 1 : 2;
    }

    private void WriteStats(CrowdSimSession session)
    {
        var world = session.World;
        if (!world.IsAlive(_sessionEntity)) return;
        ref var blackboard = ref world.Get<BlackboardFloatBuffer>(_sessionEntity);
        blackboard.Set(StatKeyIds.Tick, session.TickCount);
        blackboard.Set(StatKeyIds.Units, session.Units.Count);
        blackboard.Set(StatKeyIds.Selected, session.SelectedCount);
        blackboard.Set(StatKeyIds.ReplayStatus, ReplayStatus);
        blackboard.Set(StatKeyIds.ReplayDivergenceTick, ReplayDivergenceTick);
        // F02 迷雾 HUD 计数:玩家 1 的视野组(无迷雾会话不写,呈现层回退缺省)
        if (session.Fog is { } fog && session.Config.Relations.IndexByPlayerId[1] is { } pIdx)
        {
            int g = fog.GroupOf[pIdx];
            int f2 = fog.F * fog.F, o = g * f2;
            int visible = 0, explored = 0;
            for (int c = o; c < o + f2; c++)
            {
                if (fog.Visible[c] != 0) visible++;
                if (fog.Explored[c] != 0) explored++;
            }

            int ghosts = 0;
            foreach (int id in fog.Belief[g].Keys)
            {
                if (!session.Structures!.TryGetFootprint(id, out _)) ghosts++;
            }

            blackboard.Set(StatKeyIds.FogVisible, visible);
            blackboard.Set(StatKeyIds.FogExplored, explored);
            blackboard.Set(StatKeyIds.FogBelief, fog.Belief[g].Count);
            blackboard.Set(StatKeyIds.FogGhosts, ghosts);
            blackboard.Set(StatKeyIds.FogTerrain, fog.Terrain ? 1 : 0);
        }
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
    private static Stream OpenAsset(GameEngine engine, string relPath)
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
    private static Stream OpenUri(GameEngine engine, string uri)
    {
        if (!engine.VFS.TryResolveFullPath(uri, out string? full) || !File.Exists(full))
        {
            throw new FileNotFoundException($"CrowdSimulation 资产缺失: {uri}");
        }

        return File.OpenRead(full);
    }
}
