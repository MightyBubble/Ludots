using Arch.System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Navigation.AgentProfiles;
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
    private ILoadedChunks? _savedLoadedChunks;
    private ILoadedChunks? _savedSpatialLoadedChunks;
    private ILoadedChunks? _activeLoadedChunksOverride;
    private bool _savedLoadedChunksValid;
    private bool _loadedChunksOverrideActive;

    public bool HandleMapFocused(GameEngine engine, MapId mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!TryEnsureConfig(engine, out MassNavigationConfig config) ||
            !string.Equals(mapId.Value, config.MapId, StringComparison.Ordinal))
        {
            return false;
        }

        EnsureSystemsInstalled(engine, config);
        BindBoardWorld(engine);
        BindMassNavigationLoadedChunks(engine);
        RequireSimulationRuntime(engine, "activating map focus").SetWorldOperationsReady(true);
        if (config.ScenarioRuntime.AutoSpawnConfiguredScenario)
        {
            EnsureScenario(engine);
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

        if (engine.GetService(MassNavigationKeys.SimulationRuntime) is MassNavigationSimulationRuntime simulation)
        {
            simulation.SetWorldOperationsReady(false);
        }

        engine.RemoveService(MassNavigationKeys.RouteExecutionSink);
        ReleaseMassNavigationLoadedChunks(engine);
        return true;
    }

    private void EnsureSystemsInstalled(GameEngine engine, MassNavigationConfig config)
    {
        if (_systemsInstalled)
        {
            return;
        }

        var simulation = new MassNavigationSimulationRuntime(config);
        engine.SetService(MassNavigationKeys.SimulationRuntime, simulation);
        engine.RegisterSystem(new MassNavigationAgentMetadataSyncSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationControlSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationFormationSystem(engine, simulation), SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
            new MassNavigationFormationFollowerSystem(engine, simulation),
            SystemGroup.PostMovement);
        engine.RegisterSystem(
            new MassNavigationAuthoredAgentBindingSystem(engine, simulation),
            SystemGroup.RuntimeEntityBinding);
        engine.RegisterSystem(
            new MassNavigationEnvironmentBindingSystem(engine, simulation),
            SystemGroup.RuntimeEntityBinding);
        engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
            new MassNavigationPreSimulationStepSystem(),
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
            engine.GetService(MassNavigationKeys.SimulationRuntime) is { } existing &&
            existing.AgentState.TotalAgents > 0)
        {
            return;
        }

        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new InvalidOperationException("MassNavigation runtime requires simulation runtime.");
        MassNavigationScenarioBootstrap.SpawnConfiguredScenario(
            engine,
            simulation,
            engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("MassNavigation runtime requires TeamEntityLookup."));
        _scenarioSpawned = true;
    }

    private static void BindBoardWorld(GameEngine engine)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("MassNavigation runtime requires an active MapSession.");
        var board = session.PrimaryBoard
            ?? throw new InvalidOperationException("MassNavigation runtime requires a primary board.");
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new InvalidOperationException("MassNavigation runtime requires simulation runtime.");
        simulation.BindBoardWorld(board.WorldSize);
    }

    private void BindMassNavigationLoadedChunks(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new InvalidOperationException("MassNavigation runtime requires simulation runtime.");
        if (_loadedChunksOverrideActive &&
            engine.GetService(CoreServiceKeys.LoadedChunks) is ILoadedChunks current &&
            ReferenceEquals(current, simulation.LoadedChunks) &&
            engine.SpatialQueries is SpatialQueryService activeSpatialQueries &&
            ReferenceEquals(activeSpatialQueries.LoadedChunks, simulation.LoadedChunks))
        {
            return;
        }

        if (_loadedChunksOverrideActive)
        {
            _savedLoadedChunks = null;
            _savedSpatialLoadedChunks = null;
            _activeLoadedChunksOverride = null;
            _savedLoadedChunksValid = false;
            _loadedChunksOverrideActive = false;
        }

        _savedLoadedChunksValid = engine.GlobalContext.TryGetValue(CoreServiceKeys.LoadedChunks.Name, out object? savedRaw);
        if (savedRaw != null && savedRaw is not ILoadedChunks)
        {
            throw new InvalidOperationException("MassNavigation runtime loaded chunks override found a non-ILoadedChunks service value.");
        }

        _savedLoadedChunks = savedRaw as ILoadedChunks;
        _savedSpatialLoadedChunks = engine.SpatialQueries is SpatialQueryService savedSpatialQueries
            ? savedSpatialQueries.LoadedChunks
            : null;
        _activeLoadedChunksOverride = simulation.LoadedChunks;
        _loadedChunksOverrideActive = true;
        engine.SetService(CoreServiceKeys.LoadedChunks, (ILoadedChunks)simulation.LoadedChunks);
        if (engine.SpatialQueries is SpatialQueryService spatialQueries)
        {
            spatialQueries.SetLoadedChunks(simulation.LoadedChunks);
        }
    }

    private void ReleaseMassNavigationLoadedChunks(GameEngine engine)
    {
        if (!_loadedChunksOverrideActive)
        {
            return;
        }

        if (_activeLoadedChunksOverride != null &&
            engine.GetService(CoreServiceKeys.LoadedChunks) is ILoadedChunks loadedChunks &&
            ReferenceEquals(loadedChunks, _activeLoadedChunksOverride))
        {
            if (_savedLoadedChunksValid)
            {
                engine.SetService(CoreServiceKeys.LoadedChunks, _savedLoadedChunks!);
            }
            else
            {
                engine.RemoveService(CoreServiceKeys.LoadedChunks);
            }
        }

        if (_activeLoadedChunksOverride != null &&
            engine.SpatialQueries is SpatialQueryService spatialQueries &&
            ReferenceEquals(spatialQueries.LoadedChunks, _activeLoadedChunksOverride))
        {
            spatialQueries.SetLoadedChunks(_savedSpatialLoadedChunks);
        }

        _savedLoadedChunks = null;
        _savedSpatialLoadedChunks = null;
        _activeLoadedChunksOverride = null;
        _savedLoadedChunksValid = false;
        _loadedChunksOverrideActive = false;
    }

    private static MassNavigationSimulationRuntime RequireSimulationRuntime(GameEngine engine, string action)
    {
        return engine.GetService(MassNavigationKeys.SimulationRuntime) as MassNavigationSimulationRuntime
            ?? throw new InvalidOperationException($"MassNavigation runtime requires simulation runtime before {action}.");
    }
}

public sealed class MassNavigationPreSimulationStepSystem : ISystem<float>
{
    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void Update(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}
