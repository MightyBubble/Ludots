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
    private string? _activeMapId;
    private bool _systemsInstalled;
    private PresenterEntityRuntime? _presenterRuntime;
    private CrowdSimulation.Nav.Pathing.PathQueryService? _pathService;
    private bool _stallLogged;
    private readonly System.Diagnostics.Stopwatch _tickWatch = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<(int Tick, string Hash)> _hashes = new(4096);
    private readonly List<CrowdCommand> _pendingScript = new();
    private int _autoReplayAtTick = -1;
    private bool _autoReplayDone;

    public CrowdSimSession? Session => _session;
    public Entity SessionEntity => _sessionEntity;
    /// <summary>回放结论:0 未跑 / 1 逐位一致 / 2 分歧(分歧 tick 见黑板)。</summary>
    public int ReplayStatus { get; private set; }
    public int ReplayDivergenceTick { get; private set; } = -1;

    /// <summary>blackboard 键(呈现侧 ownerBlackboardFloat 绑定的数据契约)。</summary>
    public static class Keys
    {
        public const string UnitRadiusM = "crowd_simulation.unit.radius_m";
        public const string SessionTick = "crowd_simulation.session.tick";
        public const string SessionUnits = "crowd_simulation.session.units";
        public const string SessionSelected = "crowd_simulation.session.selected";
        public const string SessionReplayStatus = "crowd_simulation.session.replay_status";
        public const string SessionReplayDivergenceTick = "crowd_simulation.session.replay_divergence_tick";
    }

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
            engine.RegisterSystem(new CrowdSimulationSessionTickSystem(this), SystemGroup.Cleanup);
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
        var heights = NavHeightField.FromHeightmap(
            ContinuousHeightmapBinary.Read(OpenAsset(engine, RequireHeightAssetPath(mapConfig, mapId))),
            runtimeConfig.NavCellCount, runtimeConfig.NavCellSizeCm);
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtimeConfig);
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
        // 在此预解析成 id 表并做闭包校验(模板存在、profile 的移动类型与半径级一致)。
        var templateRegistry = engine.MapLoader.TemplateRegistry;
        var unitTypeTemplates = new (string TemplateId, int TemplateKeyId)[runtimeConfig.UnitTypes.Count][];
        for (int t = 0; t < runtimeConfig.UnitTypes.Count; t++)
        {
            var unitType = runtimeConfig.UnitTypes[t];
            if (unitType.TemplatesByRadiusCm == null || unitType.TemplatesByRadiusCm.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{CrowdSimulationConfigPath}: unitTypes.{unitType.Id} 未声明 templates(半径级厘米 → 模板 id);单位生成管线按(兵种 × 半径级)实例化模板。");
            }

            var row = new (string TemplateId, int TemplateKeyId)[radiusClasses.Count];
            for (int r = 0; r < radiusClasses.Count; r++)
            {
                int radiusCm = radiusClasses[r];
                if (!unitType.TemplatesByRadiusCm.TryGetValue(radiusCm, out string? templateId))
                {
                    throw new InvalidOperationException(
                        $"{CrowdSimulationConfigPath}: unitTypes.{unitType.Id}.templates 缺半径级 {radiusCm} 的模板声明。");
                }

                var template = templateRegistry.Get(templateId)
                    ?? throw new InvalidOperationException(
                        $"{CrowdSimulationConfigPath}: unitTypes.{unitType.Id}.templates[{radiusCm}] 引用未知实体模板 \"{templateId}\"。");
                string? profileId = template.Components.TryGetValue("CrowdSimulationAgent", out var agentNode)
                    ? agentNode?["profileId"]?.GetValue<string>()
                    : null;
                if (string.IsNullOrWhiteSpace(profileId) || !agentProfiles.TryGet(profileId, out var profile))
                {
                    throw new InvalidOperationException(
                        $"Entities/templates.json: 模板 \"{templateId}\" 的 CrowdSimulationAgent 缺有效 profileId(需存在于 Navigation/agent_profiles.json)。");
                }

                int agentTypeLayer = runtimeConfig.AgentTypes[unitType.AgentTypeIndex].Layer;
                if (profile.Layer != agentTypeLayer || (int)profile.RadiusCm != radiusCm)
                {
                    throw new InvalidOperationException(
                        $"{CrowdSimulationConfigPath}: unitTypes.{unitType.Id}.templates[{radiusCm}] 的模板 \"{templateId}\" 声明 profile \"{profileId}\"(layer {profile.Layer}, 半径 {profile.RadiusCm}),与本兵种(layer {agentTypeLayer})半径级 {radiusCm} 不一致。");
                }

                int templateKeyId = templateKeys.GetId(templateId);
                if (templateKeyId <= 0)
                {
                    throw new InvalidOperationException(
                        $"CrowdSimulation 单位模板键 '{templateId}' 未注册(应由 CrowdSimulationMod 的 Entities/templates.json 提供)。");
                }

                row[r] = (templateId, templateKeyId);
            }

            unitTypeTemplates[t] = row;
        }

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
        _activeMapId = mapId.Value;

        // S5 移动:规划器(路径服务,固定生效帧)+ 运动内核 + 阻挡盒索引
        _pathService?.Dispose();
        _pathService = new CrowdSimulation.Nav.Pathing.PathQueryService(navs, runtimeConfig, workerThreads: 1, TimeSpan.FromSeconds(5));
        var planner = new CrowdSimulation.Movement.CrowdSimPlanner(_session, _pathService);
        var kernel = CrowdSimulation.Movement.CrowdMovementKernel.Create(_session);
        _session.EnableMovement(kernel, planner);
        _session.Blockers = CrowdSimulation.Movement.CrowdBlockerColliders.Build(
            mapSurface.Blockers, runtimeConfig.NavCellCount, runtimeConfig.NavCellSizeCm);

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
                int tick = entry!["tick"]!.GetValue<int>();
                var cmd = (JsonObject)entry["cmd"]!.DeepClone();
                _pendingScript.Add(new CrowdCommand(tick, cmd));
            }
        }

        _session.Commands.Schedule(_pendingScript);
        _autoReplayAtTick = _debug["session"]?["autoReplayAtTick"]?.GetValue<int>() ?? -1;
        _autoReplayDone = false;
        ReplayStatus = 0;
        ReplayDivergenceTick = -1;
        _hashes.Clear();

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
        _hashes.Clear();
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
        string? hash = session.Step();
        if (hash == null)
        {
            if (!_stallLogged)
            {
                _stallLogged = true;
                Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine,
                    $"CrowdSimulation stalled at tick {session.TickCount}: waiting for path reply (planner pending={session.Planner?.PendingCount ?? -1}, serviceFault={_pathService?.Faulted}).");
            }

            return;
        }

        _stallLogged = false;
        if (_hashes.Count >= 4096) _hashes.RemoveAt(0);
        _hashes.Add((session.TickCount, hash));
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
                $"CrowdSimulation tick {session.TickCount}: units={session.Units.Count}, selected={session.SelectedCount}, hash={hash}, presenters=[{presenters}], elapsed={_tickWatch.ElapsedMilliseconds}ms.");
        }

        WriteStats(session);
    }

    /// <summary>回放:一次性世界重放同一指令日志,逐帧对拍校验码(不进引擎世界,不碰呈现)。</summary>
    public void RunReplay()
    {
        var session = _session;
        if (session == null) return;
        var entries = session.Commands.Log
            .Select(e => new CrowdCommand(e.Tick, (JsonNode)e.Cmd.DeepClone()))
            .ToArray();
        int ticks = session.TickCount;
        var replay = new CrowdSimSession(session.Config, ArchWorld.Create(), session.Navs, session.NavByLayerRadius);
        // 回放会话同样挂运动栈(一次性世界 + 自己的路径服务;不入引擎世界,不碰呈现)
        if (session.Movement != null)
        {
            using var replayService = new CrowdSimulation.Nav.Pathing.PathQueryService(
                session.Navs, session.Config, workerThreads: 1, TimeSpan.FromSeconds(5));
            replay.EnableMovement(
                CrowdSimulation.Movement.CrowdMovementKernel.Create(replay),
                new CrowdSimulation.Movement.CrowdSimPlanner(replay, replayService));
            replay.Blockers = session.Blockers;
            replay.BlockOnDueReplies = true;
            replay.Commands.Schedule(entries);
            var replayHashes = new List<string>(ticks);
            replay.Advance(ticks, replayHashes);
            CompareReplay(ticks, replayHashes);
            return;
        }

        replay.Commands.Schedule(entries);
        var replayHashes2 = new List<string>(ticks);
        replay.Advance(ticks, replayHashes2);
        CompareReplay(ticks, replayHashes2);
    }

    private void CompareReplay(int ticks, List<string> replayHashes)
    {
        ReplayDivergenceTick = -1;
        for (int i = 0; i < Math.Min(_hashes.Count, replayHashes.Count); i++)
        {
            if (_hashes[i].Hash != replayHashes[i]) { ReplayDivergenceTick = _hashes[i].Tick; break; }
        }

        if (ReplayDivergenceTick < 0 && _hashes.Count != replayHashes.Count) ReplayDivergenceTick = -2;
        ReplayStatus = ReplayDivergenceTick == -1 ? 1 : 2;
    }

    private void WriteStats(CrowdSimSession session)
    {
        var world = session.World;
        if (!world.IsAlive(_sessionEntity)) return;
        ref var blackboard = ref world.Get<BlackboardFloatBuffer>(_sessionEntity);
        blackboard.Set(ConfigKeyRegistry.Register(Keys.SessionTick), session.TickCount);
        blackboard.Set(ConfigKeyRegistry.Register(Keys.SessionUnits), session.Units.Count);
        blackboard.Set(ConfigKeyRegistry.Register(Keys.SessionSelected), session.SelectedCount);
        blackboard.Set(ConfigKeyRegistry.Register(Keys.SessionReplayStatus), ReplayStatus);
        blackboard.Set(ConfigKeyRegistry.Register(Keys.SessionReplayDivergenceTick), ReplayDivergenceTick);
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
