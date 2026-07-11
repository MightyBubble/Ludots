using Arch.System;
using System.Collections.Generic;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationRuntime
{
    private readonly MassNavigationRuntimeBinding _binding = new();
    private readonly Dictionary<MapId, MassNavigationMapRuntimeState> _statesByMap = new();
    private bool _systemsInstalled;

    public bool HandleMapFocused(
        GameEngine engine,
        MapId mapId,
        out MassNavigationMapRuntimeState? mapState)
    {
        ArgumentNullException.ThrowIfNull(engine);
        bool created = false;
        if (!_statesByMap.TryGetValue(mapId, out mapState))
        {
            if (!TryLoadConfigForCurrentMap(engine, mapId, out MassNavigationCapabilityProfile? capabilityProfile))
            {
                return false;
            }

            MassNavigationCapabilityProfile loadedProfile = capabilityProfile
                ?? throw new InvalidOperationException("MassNavigation config activation reported success without a capability profile.");
            var newSimulation = new MassNavigationSimulationRuntime(mapId, loadedProfile.Runtime);
            newSimulation.BindCapabilityProfileProvenance(loadedProfile);
            mapState = new MassNavigationMapRuntimeState(
                mapId,
                loadedProfile,
                newSimulation);
            created = true;
        }

        MassNavigationSimulationRuntime simulation = mapState.Simulation;
        try
        {
            BindBoardWorld(engine, simulation);
        }
        catch
        {
            simulation.ReleaseStreamingWindow();
            throw;
        }

        if (created)
        {
            _statesByMap.Add(mapId, mapState);
        }

        _binding.Activate(simulation);

        engine.SetService(MassNavigationKeys.RuntimeBinding, _binding);
        engine.SetService(MassNavigationKeys.SimulationRuntime, simulation);
        EnsureSystemsInstalled(engine);
        simulation.SetWorldOperationsReady(true);
        return true;
    }

    public bool HandleMapSuspended(GameEngine engine, MapId mapId)
    {
        return ReleaseMapFocus(engine, mapId);
    }

    public bool HandleMapUnloaded(GameEngine engine, MapId mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!_statesByMap.TryGetValue(mapId, out MassNavigationMapRuntimeState? mapState))
        {
            return false;
        }

        ReleaseMapFocus(engine, mapId);
        mapState.Simulation.ResetRuntimeState(engine.World);
        _statesByMap.Remove(mapId);
        return true;
    }

    public bool TryGetMapState(MapId mapId, out MassNavigationMapRuntimeState? mapState)
    {
        return _statesByMap.TryGetValue(mapId, out mapState);
    }

    public void AttachSceneController(MapId mapId, IMassNavigationSceneController sceneController)
    {
        ArgumentNullException.ThrowIfNull(sceneController);
        if (!_statesByMap.TryGetValue(mapId, out MassNavigationMapRuntimeState? mapState))
        {
            throw new InvalidOperationException(
                $"MassNavigation cannot attach scene state before map '{mapId.Value}' has a loaded runtime state.");
        }

        if (mapState.SceneController != null && !ReferenceEquals(mapState.SceneController, sceneController))
        {
            throw new InvalidOperationException(
                $"MassNavigation map '{mapId.Value}' already owns a different scene controller.");
        }

        mapState.SceneController = sceneController;
    }

    private bool ReleaseMapFocus(GameEngine engine, MapId mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!_statesByMap.TryGetValue(mapId, out MassNavigationMapRuntimeState? mapState))
        {
            return false;
        }

        MassNavigationSimulationRuntime simulation = mapState.Simulation;
        simulation.SetWorldOperationsReady(false);
        simulation.ReleaseStreamingWindow();

        if (ReferenceEquals(_binding.Current, simulation))
        {
            _binding.Clear(simulation);
            if (ReferenceEquals(engine.GetService(MassNavigationKeys.SimulationRuntime), simulation))
            {
                engine.RemoveService(MassNavigationKeys.SimulationRuntime);
            }

            engine.RemoveService(MassNavigationKeys.RouteExecutionSink);
        }

        return true;
    }

    private void EnsureSystemsInstalled(GameEngine engine)
    {
        if (_systemsInstalled)
        {
            return;
        }

        engine.RegisterSystem(new MassNavigationAgentMetadataSyncSystem(engine, _binding), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationControlSystem(engine, _binding), SystemGroup.InputCollection);
        engine.InsertSystemBeforeRequired<WorldToGridSyncSystem>(
            new MassNavigationFormationSystem(engine, _binding),
            SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
            new MassNavigationFormationFollowerSystem(engine, _binding),
            SystemGroup.PostMovement);
        engine.RegisterSystem(
            new MassNavigationAuthoredAgentBindingSystem(engine, _binding),
            SystemGroup.RuntimeEntityBinding);
        engine.RegisterSystem(
            new MassNavigationEnvironmentBindingSystem(engine, _binding),
            SystemGroup.RuntimeEntityBinding);
        engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
            new MassNavigationPreSimulationStepSystem(),
            SystemGroup.PostMovement);
        engine.RegisterSystem(
            new MassNavigationOrderIngestionSystem(engine, _binding),
            SystemGroup.AbilityActivation);
        engine.InsertPresentationSystemBefore<AnimatorRuntimeSystem>(
            new MassNavigationLocomotionAnimatorParamSystem(engine.World, _binding));
        _systemsInstalled = true;
        Log.Info(in LogChannels.Engine, "[MassNavigation runtime] Installed mass-navigation runtime.");
    }

    private static bool TryLoadConfigForCurrentMap(
        GameEngine engine,
        MapId mapId,
        out MassNavigationCapabilityProfile? profile)
    {
        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("MassNavigation runtime requires ConfigPipeline before loading MassNavigationConfig.");
        }

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("MassNavigation runtime requires an active MapSession before resolving a map profile.");
        if (session.MapId != mapId)
        {
            throw new InvalidOperationException(
                $"MassNavigation focus event map '{mapId.Value}' does not match current MapSession '{session.MapId.Value}'.");
        }

        var loader = new MassNavigationConfigLoader(engine.ConfigPipeline);
        if (!loader.TryLoad(
                engine.ConfigCatalog,
                engine.ConfigConflictReport,
                session.MapConfig,
                out MassNavigationCapabilityProfile? loaded))
        {
            profile = null;
            return false;
        }

        AgentProfileRegistry agentProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("MassNavigation runtime requires AgentProfiles.");
        MassNavigationCapabilityProfile loadedProfile = loaded
            ?? throw new InvalidOperationException("MassNavigation config loader reported success without a capability profile.");
        loadedProfile.Runtime.AgentProfiles.BindAgentProfiles(agentProfiles);
        profile = loadedProfile;
        return true;
    }

    private static void BindBoardWorld(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("MassNavigation runtime requires an active MapSession.");
        var board = session.PrimaryBoard
            ?? throw new InvalidOperationException("MassNavigation runtime requires a primary board.");
        if (!ReferenceEquals(engine.GetService(CoreServiceKeys.LoadedChunks), board.LoadedChunks))
        {
            throw new InvalidOperationException("MassNavigation requires CoreServiceKeys.LoadedChunks to be owned by the active primary board.");
        }

        if (board.LoadedChunks is not WorldGridLoadedChunks worldGridLoadedChunks)
        {
            throw new InvalidOperationException(
                $"MassNavigation requires the active primary board to expose {nameof(WorldGridLoadedChunks)}, but received '{board.LoadedChunks.GetType().FullName}'.");
        }

        simulation.BindBoardWorld(board.WorldSize, worldGridLoadedChunks);
    }
}

public sealed class MassNavigationMapRuntimeState
{
    internal MassNavigationMapRuntimeState(
        MapId mapId,
        MassNavigationCapabilityProfile profile,
        MassNavigationSimulationRuntime simulation)
    {
        MapId = mapId;
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    public MapId MapId { get; }
    public MassNavigationCapabilityProfile Profile { get; }
    public MassNavigationSimulationRuntime Simulation { get; }
    public IMassNavigationSceneController? SceneController { get; internal set; }
}

public sealed class MassNavigationPreSimulationStepSystem : ISystem<float>
{
    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void Update(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}
