using Arch.System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.MovePlanning;
using Ludots.Core.Movement;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace Ludots.Core.MassNavigation.Runtime;

/// <summary>
/// 地图聚焦激活合同：MassNavigationConfig.json 在场 = overlay（mapId 可选作用域）；
/// 缺席 = 全默认配置，且仅当地图模板里出现 MassNavigationAgent 组件时激活。
/// 单位一律 authored 实体（地图 Entities 或 trigger 蓝图 spawn），
/// 由 <see cref="MassNavigationAuthoredAgentBindingSystem"/> 自动绑定。
/// </summary>
public sealed class MassNavigationRuntime
{
    private const string NavAgentComponentKey = "MassNavigationAgent";

    private MassNavigationConfig? _fileConfig;
    private bool _configResolved;
    private bool _systemsInstalled;
    private MassNavigationSimulationRuntime? _simulation;

    public bool HandleMapFocused(GameEngine engine, MapId mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!TryResolveConfig(engine, mapId, out MassNavigationConfig? config) || config is null)
        {
            return false;
        }

        EnsureSystemsInstalled(engine, config);
        MassNavigationSimulationRuntime simulation = EnsureSimulationRuntime(engine, config);
        MassNavigationRuntimeBinding binding = RequireRuntimeBinding(engine);
        bool resumePreparedRuntime = simulation.RuntimeBindingPreparationComplete;
        binding.Activate(mapId, simulation);
        if (!resumePreparedRuntime)
        {
            simulation.BeginRuntimeBindingPreparation();
        }

        try
        {
            BindBoardWorld(engine);
        }
        catch
        {
            binding.Clear(mapId, simulation);
            simulation.ReleaseLoadedChunkContribution();
            throw;
        }

        if (resumePreparedRuntime)
        {
            MassNavigationIds.PublishPreparedWhenBindingComplete(engine, simulation);
        }

        return true;
    }

    public bool HandleMapSuspended(GameEngine engine, MapId mapId)
    {
        return ReleaseMapState(engine, mapId, unloadScenario: false);
    }

    public bool HandleMapUnloaded(GameEngine engine, MapId mapId)
    {
        return ReleaseMapState(engine, mapId, unloadScenario: true);
    }

    private bool ReleaseMapState(GameEngine engine, MapId mapId, bool unloadScenario)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!TryResolveConfig(engine, mapId, out MassNavigationConfig? resolved) ||
            resolved is not MassNavigationConfig config)
        {
            return false;
        }

        if (_simulation is MassNavigationSimulationRuntime simulation)
        {
            // 地图挂起/卸载时批量取消全部位姿写权窗口：必须发生在 binding 清除之前，
            // 桥此刻还能解析到运行时并做求解器侧的幂等清理；活跃位移效果会在下一 tick
            // 识别到窗口消失并合法终止。
            PoseAuthorityArbiter? poseAuthorityArbiter = engine.GetService(CoreServiceKeys.PoseAuthorityArbiter);
            poseAuthorityArbiter?.CancelAllWindows(engine.World);
            RequireRuntimeBinding(engine).Clear(mapId, simulation);
        }

        engine.RemoveService(MassNavigationKeys.RouteExecutionSink);
        _simulation?.ReleaseLoadedChunkContribution();
        if (unloadScenario)
        {
            _simulation?.ClearAuthoredRuntimeBindings(engine.World);
            _simulation = null;
        }

        return true;
    }

    private void EnsureSystemsInstalled(GameEngine engine, MassNavigationConfig config)
    {
        if (_systemsInstalled)
        {
            return;
        }

        if (engine.GetService(MassNavigationKeys.RuntimeBinding) == null)
        {
            engine.SetService(MassNavigationKeys.RuntimeBinding, new MassNavigationRuntimeBinding());
        }

        PoseAuthorityArbiter poseAuthorityArbiter = engine.GetService(CoreServiceKeys.PoseAuthorityArbiter)
            ?? throw new InvalidOperationException("MassNavigation runtime requires the PoseAuthorityArbiter service.");
        poseAuthorityArbiter.AddListener(new MassNavigationPoseAuthorityBridge(
            () => MassNavigationIds.TryGetCurrentNavigationRuntime(engine, out MassNavigationSimulationRuntime simulation)
                ? simulation
                : null));
        engine.RegisterSystem(new MassNavigationAgentMetadataSyncSystem(engine, config), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationSimulationStepSystem(engine), SystemGroup.PostMovement);
        engine.RegisterSystem(
            new MassNavigationAuthoredAgentBindingSystem(engine, config),
            SystemGroup.RuntimeEntityBinding);
        engine.RegisterSystem(
            new MassNavigationEnvironmentBindingSystem(engine),
            SystemGroup.RuntimeEntityBinding);
        engine.InsertSystemBeforeRequired<MassNavigationSimulationStepSystem>(
            new MassNavigationPreSimulationStepSystem(engine),
            SystemGroup.PostMovement);
        engine.RegisterSystem(
            new MassNavigationMovePlanExecutionSystem(engine, config),
            SystemGroup.AbilityActivation);
        engine.InsertPresentationSystemBefore<AnimatorRuntimeSystem>(
            new MassNavigationLocomotionAnimatorParamSystem(engine));
        _systemsInstalled = true;
        Log.Info(in LogChannels.Engine, "[MassNavigation runtime] Installed mass-navigation runtime.");
    }

    private MassNavigationSimulationRuntime EnsureSimulationRuntime(GameEngine engine, MassNavigationConfig config)
    {
        if (_simulation is MassNavigationSimulationRuntime existing)
        {
            return existing;
        }

        var simulation = new MassNavigationSimulationRuntime(config);
        DomainStanceQuery stances = engine.GetService(CoreServiceKeys.DomainStanceQuery)
            ?? throw new InvalidOperationException("MassNavigation runtime requires DomainStanceQuery.");
        simulation.SetDomainRelationshipProjection(new MassNavigationDomainStanceProjection(
            stances,
            config.RuntimeCapacity.RelationshipDomainCapacity,
            config.RelationshipPolicy.CooperativeStance));
        _simulation = simulation;
        return simulation;
    }

    private bool TryResolveConfig(GameEngine engine, MapId focusedMapId, out MassNavigationConfig? config)
    {
        if (_fileConfig is MassNavigationConfig fileConfig)
        {
            config = fileConfig.AppliesToMap(focusedMapId.Value) ? fileConfig : null;
            return config != null;
        }

        if (!_configResolved)
        {
            if (engine.ConfigPipeline == null)
            {
                throw new InvalidOperationException("MassNavigation runtime requires ConfigPipeline before loading MassNavigationConfig.");
            }

            var loader = new MassNavigationConfigLoader(engine.ConfigPipeline);
            _configResolved = true;
            if (loader.TryLoad(engine.ConfigCatalog, engine.ConfigConflictReport, out MassNavigationConfig? loaded) &&
                loaded is not null)
            {
                _fileConfig = loaded;
                config = loaded.AppliesToMap(focusedMapId.Value) ? loaded : null;
                return config != null;
            }
        }

        config = MapDeclaresNavAgents(engine)
            ? MassNavigationConfig.CreateDefaultForMap(focusedMapId.Value)
            : null;
        return config != null;
    }

    private static bool MapDeclaresNavAgents(GameEngine engine)
    {
        var templates = engine.MapLoader?.TemplateRegistry?.GetAll();
        if (templates == null)
        {
            return false;
        }

        foreach (Ludots.Core.Config.EntityTemplate template in templates)
        {
            if (template?.Components != null &&
                template.Components.ContainsKey(NavAgentComponentKey))
            {
                return true;
            }
        }

        return false;
    }

    private void BindBoardWorld(GameEngine engine)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("MassNavigation runtime requires an active MapSession.");
        var board = session.PrimaryBoard
            ?? throw new InvalidOperationException("MassNavigation runtime requires a primary board.");
        MassNavigationSimulationRuntime simulation = RequireSimulationRuntime("binding the board world");
        if (board.LoadedChunks is not WorldGridLoadedChunks loadedChunks)
        {
            throw new InvalidOperationException(
                $"MassNavigation requires board-owned {nameof(WorldGridLoadedChunks)}, got {board.LoadedChunks?.GetType().FullName ?? "null"}.");
        }

        simulation.BindBoardWorld(board.WorldSize, loadedChunks);
    }

    private MassNavigationSimulationRuntime RequireSimulationRuntime(string action)
    {
        return _simulation
            ?? throw new InvalidOperationException($"MassNavigation runtime requires simulation runtime before {action}.");
    }

    private static MassNavigationRuntimeBinding RequireRuntimeBinding(GameEngine engine)
    {
        return engine.GetService(MassNavigationKeys.RuntimeBinding)
            ?? throw new InvalidOperationException("MassNavigation runtime requires RuntimeBinding.");
    }
}

public sealed class MassNavigationPreSimulationStepSystem : ISystem<float>
{
    private readonly GameEngine _engine;

    public MassNavigationPreSimulationStepSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void Update(in float dt)
    {
        if (MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            simulation.BeginFrame(dt);
            simulation.ObserveControlTick();
        }
    }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}
