using Arch.System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.MovePlanning;
using Ludots.Core.Movement;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationRuntime
{
    private MassNavigationConfig? _config;
    private bool _configResolved;
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private MassNavigationSimulationRuntime? _simulation;

    public bool HandleMapFocused(GameEngine engine, MapId mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!TryEnsureConfig(engine, out MassNavigationConfig? config) || config is null ||
            !string.Equals(mapId.Value, config.MapId, StringComparison.Ordinal))
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
            if (config.ScenarioRuntime.AutoSpawnConfiguredScenario)
            {
                EnsureScenario(engine);
            }
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
        if (!TryEnsureConfig(engine, out MassNavigationConfig? config) || config is null ||
            !string.Equals(mapId.Value, config.MapId, StringComparison.Ordinal))
        {
            return false;
        }

        if (unloadScenario)
        {
            _scenarioSpawned = false;
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
            config.ScenarioRuntime.RuntimeCapacity.RelationshipDomainCapacity,
            config.RelationshipPolicy.CooperativeStance));
        _simulation = simulation;
        return simulation;
    }

    private bool TryEnsureConfig(GameEngine engine, out MassNavigationConfig? config)
    {
        if (_config != null)
        {
            config = _config;
            return true;
        }

        if (_configResolved)
        {
            config = null;
            return false;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("MassNavigation runtime requires ConfigPipeline before loading MassNavigationConfig.");
        }

        var loader = new MassNavigationConfigLoader(engine.ConfigPipeline);
        if (!loader.TryLoad(engine.ConfigCatalog, engine.ConfigConflictReport, out MassNavigationConfig? loaded) ||
            loaded is null)
        {
            _configResolved = true;
            config = null;
            return false;
        }

        AgentProfileRegistry agentProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("MassNavigation runtime requires AgentProfiles.");
        loaded.AgentProfiles.BindAgentProfiles(agentProfiles);
        _config = loaded;
        _configResolved = true;
        config = loaded;
        return true;
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioSpawned &&
            _simulation is { } existing &&
            existing.AgentState.TotalAgents > 0)
        {
            return;
        }

        MassNavigationSimulationRuntime simulation = RequireSimulationRuntime("spawning the configured scenario");
        MassNavigationScenarioBootstrap.SpawnConfiguredScenario(
            engine,
            simulation,
            engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("MassNavigation runtime requires TeamEntityLookup."));
        _scenarioSpawned = true;
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
