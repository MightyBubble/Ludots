using Arch.System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Systems;
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
        if (!TryEnsureConfig(engine, out MassNavigationConfig config) ||
            !string.Equals(mapId.Value, config.MapId, StringComparison.Ordinal))
        {
            return false;
        }

        EnsureSystemsInstalled(engine, config);
        MassNavigationSimulationRuntime simulation = RequireSimulationRuntime("activating map focus");
        MassNavigationRuntimeBinding binding = RequireRuntimeBinding(engine);
        binding.Activate(mapId, simulation);
        try
        {
            BindBoardWorld(engine);
            if (config.ScenarioRuntime.AutoSpawnConfiguredScenario)
            {
                EnsureScenario(engine);
            }

            binding.MarkPrepared(mapId, simulation);
        }
        catch
        {
            binding.Clear(mapId, simulation);
            simulation.ReleaseLoadedChunkContribution();
            throw;
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
        if (!TryEnsureConfig(engine, out MassNavigationConfig config) ||
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
            RequireRuntimeBinding(engine).Clear(mapId, simulation);
        }

        engine.RemoveService(MassNavigationKeys.RouteExecutionSink);
        _simulation?.ReleaseLoadedChunkContribution();
        return true;
    }

    private void EnsureSystemsInstalled(GameEngine engine, MassNavigationConfig config)
    {
        if (_systemsInstalled)
        {
            return;
        }

        var simulation = new MassNavigationSimulationRuntime(config);
        DomainStanceQuery stances = engine.GetService(CoreServiceKeys.DomainStanceQuery)
            ?? throw new InvalidOperationException("MassNavigation runtime requires DomainStanceQuery.");
        simulation.SetDomainRelationshipProjection(new MassNavigationDomainStanceProjection(
            stances,
            config.ScenarioRuntime.RuntimeCapacity.RelationshipDomainCapacity,
            config.RelationshipPolicy.CooperativeStance));
        _simulation = simulation;
        engine.SetService(MassNavigationKeys.RuntimeBinding, new MassNavigationRuntimeBinding());
        engine.RegisterSystem(new MassNavigationAgentMetadataSyncSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationSimulationStepSystem(engine, simulation), SystemGroup.PostMovement);
        engine.RegisterSystem(
            new MassNavigationAuthoredAgentBindingSystem(engine, simulation),
            SystemGroup.RuntimeEntityBinding);
        engine.RegisterSystem(
            new MassNavigationEnvironmentBindingSystem(engine, simulation),
            SystemGroup.RuntimeEntityBinding);
        engine.InsertSystemBeforeRequired<MassNavigationSimulationStepSystem>(
            new MassNavigationPreSimulationStepSystem(engine, simulation),
            SystemGroup.PostMovement);
        engine.RegisterSystem(
            new MassNavigationOrderIngestionSystem(engine, simulation),
            SystemGroup.AbilityActivation);
        engine.InsertPresentationSystemBefore<AnimatorRuntimeSystem>(
            new MassNavigationLocomotionAnimatorParamSystem(engine.World, simulation));
        _systemsInstalled = true;
        Log.Info(in LogChannels.Engine, "[MassNavigation runtime] Installed mass-navigation runtime.");
    }

    private bool TryEnsureConfig(GameEngine engine, out MassNavigationConfig config)
    {
        if (_config != null)
        {
            config = _config;
            return true;
        }

        if (_configResolved)
        {
            config = null!;
            return false;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("MassNavigation runtime requires ConfigPipeline before loading MassNavigationConfig.");
        }

        var loader = new MassNavigationConfigLoader(engine.ConfigPipeline);
        if (!loader.TryLoad(engine.ConfigCatalog, engine.ConfigConflictReport, out MassNavigationConfig? loaded))
        {
            _configResolved = true;
            config = null!;
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
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationPreSimulationStepSystem(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void Update(in float dt)
    {
        if (MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            _simulation.BeginFrame(dt);
            _simulation.ObserveControlTick();
        }
    }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}
