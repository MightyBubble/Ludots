using System.Threading.Tasks;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.UI;
using MassNavigationMod.Systems;
using MassNavigationMod.UI;

namespace MassNavigationMod.Runtime;

internal sealed class MassNavigationRuntime
{
    private static readonly QueryDescription AuthoredPlayerOwnerQuery = new QueryDescription().WithAll<PlayerOwner>();

    private readonly IModContext _context;
    private MassNavigationConfig? _config;
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private RenderDebugSnapshot _savedRenderDebug;
    private bool _savedRenderDebugValid;
    private ILoadedChunks? _savedLoadedChunks;
    private ILoadedChunks? _savedSpatialLoadedChunks;
    private ILoadedChunks? _activeLoadedChunksOverride;
    private bool _savedLoadedChunksValid;
    private bool _loadedChunksOverrideActive;
    private readonly MassNavigationPanelController _panelController = new();

    public MassNavigationRuntime(IModContext context)
    {
        _context = context;
    }

    public void EnsureSystemsInstalled(GameEngine engine)
    {
        if (_systemsInstalled)
        {
            return;
        }

        MassNavigationConfig config = EnsureConfig(engine);
        var simulation = new MassNavigationSimulationRuntime(config);
        engine.SetService(MassNavigationKeys.SimulationRuntime, simulation);
        engine.RegisterSystem(new MassNavigationAgentMetadataSyncSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationSelectionSyncSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationControlSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationLocalCommandInputSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationFormationSystem(engine, simulation), SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
            new MassNavigationOrderIngestionSystem(engine, simulation),
            SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationOrderIngestionSystem>(
            new MassNavigationSpawnReceiptBindingSystem(engine, simulation),
            SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
            new MassNavigationPreSimulationStepSystem(),
            SystemGroup.PostMovement);
        engine.InsertPresentationSystemBefore<CameraCullingSystem>(
            new MassNavigationCameraFocusPresentationSystem(engine, simulation));
        engine.RegisterPresentationSystem(new MassNavigationHudPresentationSystem(engine, simulation));
        engine.RegisterPresentationSystem(new MassNavigationPanelPresentationSystem(
            engine,
            this,
            config.ScenarioRuntime.PanelControls.PanelRefreshIntervalSeconds));
        _systemsInstalled = true;
        _context.Log("[MassNavigationMod] Installed mass-navigation runtime.");
    }

    private MassNavigationConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new System.InvalidOperationException("MassNavigationMod requires ConfigPipeline before loading MassNavigationConfig.");
        }

        _config = new MassNavigationConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        return _config;
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        MassNavigationConfig config = EnsureConfig(engine);
        if (!string.Equals(context.Get(CoreServiceKeys.MapId).Value, config.MapId, System.StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        EnsureSystemsInstalled(engine);
        BindBoardWorld(engine);
        BindMassNavigationLoadedChunks(engine);
        BindLocalSelectionOwner(engine);
        ConfigureRenderDebug(engine);
        ConfigureCoreMinimap(engine);
        ApplyCullingFocusOverride(engine);
        EnsureTacticalCamera(engine);
        if (config.ScenarioRuntime.AutoSpawnConfiguredScenario)
        {
            EnsureScenario(engine);
        }
        ClearPanelIfOwned(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        MassNavigationConfig config = EnsureConfig(engine);
        if (!string.Equals(context.Get(CoreServiceKeys.MapId).Value, config.MapId, System.StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _scenarioSpawned = false;
        ReleaseMassNavigationLoadedChunks(engine);
        RestoreRenderDebug(engine);
        ClearCullingFocusOverride(engine);
        ClearPanelIfOwned(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapSuspendedAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        MassNavigationConfig config = EnsureConfig(engine);
        if (!string.Equals(context.Get(CoreServiceKeys.MapId).Value, config.MapId, System.StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        ReleaseMassNavigationLoadedChunks(engine);
        RestoreRenderDebug(engine);
        ClearCullingFocusOverride(engine);
        ClearPanelIfOwned(engine);
        return Task.CompletedTask;
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        if (!MassNavigationIds.IsCurrentNavigationMap(engine))
        {
            return;
        }

        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime.");
        if (!simulation.Config.ScenarioRuntime.PanelControls.Visible)
        {
            _panelController.ClearIfOwned(root);
            return;
        }

        _panelController.MountOrSync(engine, simulation);
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
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime.");
        MassNavigationScenarioBootstrap.SpawnConfiguredScenario(
            engine,
            simulation,
            engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new System.InvalidOperationException("MassNavigationMod requires TeamEntityLookup."));
        _scenarioSpawned = true;
    }

    private static void BindBoardWorld(GameEngine engine)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new System.InvalidOperationException("MassNavigationMod requires an active MapSession.");
        var board = session.PrimaryBoard
            ?? throw new System.InvalidOperationException("MassNavigationMod requires a primary board.");
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime.");
        simulation.BindBoardWorld(board.WorldSize);
    }

    private void BindMassNavigationLoadedChunks(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime.");
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
            throw new System.InvalidOperationException("MassNavigationMod loaded chunks override found a non-ILoadedChunks service value.");
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

    private static void ConfigureCoreMinimap(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = RequireSimulationRuntime(engine, "configuring minimap");
        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires core MinimapRuntime.");
        ApplyConfiguredMinimapPreset(minimap, simulation.Config.Minimap);
        minimap.Visible = simulation.Config.Minimap.Visible;
    }

    private static void ApplyConfiguredMinimapPreset(MinimapRuntime minimap, MassNavigationMinimapConfig config)
    {
        switch (config.ParsedInitialPreset)
        {
            case MinimapPreset.RtsFullMap:
                minimap.UseRtsFullMapPreset();
                minimap.SetRotateWithCamera(config.RotateWithCamera);
                return;
            case MinimapPreset.FollowCamera:
                minimap.UseFollowCameraPreset(config.FollowCameraHalfExtentCm, config.RotateWithCamera);
                return;
            default:
                throw new System.InvalidOperationException(
                    $"MassNavigationMod minimap preset '{config.InitialPreset}' was not validated.");
        }
    }

    internal static void ApplyCullingFocusOverride(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime before culling focus override.");
        if (engine.GetService(CoreServiceKeys.CameraCullingFocusOverride) is not Ludots.Core.Presentation.Camera.CameraCullingFocusOverride focus)
        {
            if (simulation.ViewResidency.UsesProbeFocus)
            {
                throw new System.InvalidOperationException("MassNavigationMod viewResidency mode 'Probe' requires CameraCullingFocusOverride service.");
            }

            return;
        }

        if (!simulation.ViewResidency.UsesProbeFocus)
        {
            focus.Enabled = false;
            focus.SourceId = string.Empty;
            return;
        }

        MassNavigationCameraProbeConfig probe = simulation.ViewResidency.ActiveProbe;
        focus.Enabled = true;
        focus.SourceId = probe.Id;
        focus.TargetCm = new Vector2(probe.TargetXCm, probe.TargetYCm);
        focus.DistanceCm = probe.DistanceCm;
        focus.Yaw = probe.Yaw;
        focus.Pitch = probe.Pitch;
        focus.FovYDeg = probe.FovYDeg;
    }

    internal static void ClearCullingFocusOverride(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.CameraCullingFocusOverride) is not Ludots.Core.Presentation.Camera.CameraCullingFocusOverride focus)
        {
            return;
        }

        focus.Enabled = false;
        focus.SourceId = string.Empty;
    }

    internal static void RequestMinimapStrategicWorldView(GameEngine engine)
    {
        ConfigureCoreMinimap(engine);
    }

    internal static void RequestMinimapTacticalWorldView(GameEngine engine)
    {
        ConfigureCoreMinimap(engine);
    }

    internal static void RequestCameraJump(GameEngine engine, Vector2 targetCm)
    {
        MassNavigationCameraProfilesConfig cameraProfiles = RequireCameraProfiles(engine);
        string tacticalProfileId = cameraProfiles.TacticalProfileId;
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = CreateCameraRequest(
            tacticalProfileId,
            cameraProfiles.RequestPolicy);
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = tacticalProfileId,
            TargetCm = targetCm
        });
    }

    private static void BindLocalSelectionOwner(GameEngine engine)
    {
        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires SelectionRuntime.");
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) ||
            localObj is not Entity owner ||
            !engine.World.IsAlive(owner))
        {
            owner = ResolveSingleAuthoredPlayerOwner(engine);
            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
        }

        if (!engine.World.Has<PlayerOwner>(owner))
        {
            throw new System.InvalidOperationException("MassNavigationMod LocalPlayerEntity must author PlayerOwner.");
        }

        EnsureSelectionOwner(engine.World, owner, selection, engine.GlobalContext);
    }

    private static Entity ResolveSingleAuthoredPlayerOwner(GameEngine engine)
    {
        Entity resolved = Entity.Null;
        int count = 0;
        engine.World.Query(in AuthoredPlayerOwnerQuery, (Entity entity, ref PlayerOwner _) =>
        {
            resolved = entity;
            count++;
        });

        return count switch
        {
            1 => resolved,
            0 => throw new System.InvalidOperationException("MassNavigationMod requires the map to author exactly one PlayerOwner local player entity."),
            _ => throw new System.InvalidOperationException("MassNavigationMod found multiple PlayerOwner entities before LocalPlayerEntity was resolved; author one local player or bind CoreServiceKeys.LocalPlayerEntity explicitly.")
        };
    }

    private static void EnsureSelectionOwner(World world, Entity owner, SelectionRuntime selection, System.Collections.Generic.Dictionary<string, object> globals)
    {
        if (!world.Has<SelectionDragState>(owner))
        {
            throw new System.InvalidOperationException("MassNavigationMod local player template must author SelectionDragState.");
        }

        if (!SelectionContextRuntime.TrySetCurrentView(
                world,
                globals,
                selection,
                owner,
                SelectionViewKeys.Primary,
                owner,
                SelectionSetKeys.LivePrimary,
                out _))
        {
            throw new System.InvalidOperationException("MassNavigationMod failed to bind LivePrimary as the primary selection view.");
        }
    }

    private static void EnsureTacticalCamera(GameEngine engine)
    {
        RequestTacticalCameraReset(engine);
    }

    private void ConfigureRenderDebug(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
        {
            return;
        }

        if (!_savedRenderDebugValid)
        {
            _savedRenderDebug = new RenderDebugSnapshot(
                renderDebug.DrawTerrain,
                renderDebug.DrawPrimitives,
                renderDebug.DrawDebugDraw,
                renderDebug.DrawSkiaUi,
                renderDebug.DrawWorldHudBars,
                renderDebug.DrawWorldHudText,
                renderDebug.DrawCombatText,
                renderDebug.AcceptanceScaleMultiplier);
            _savedRenderDebugValid = true;
        }

        renderDebug.DrawTerrain = true;
        renderDebug.DrawDebugDraw = false;
        // Raylib currently routes the official performer ISM/static-mesh lane through this shared draw toggle.
        renderDebug.DrawPrimitives = true;
        renderDebug.DrawSkiaUi = true;
        renderDebug.DrawWorldHudBars = true;
        renderDebug.DrawWorldHudText = true;
        renderDebug.DrawCombatText = true;
    }

    private void RestoreRenderDebug(GameEngine engine)
    {
        if (!_savedRenderDebugValid ||
            engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
        {
            return;
        }

        renderDebug.DrawTerrain = _savedRenderDebug.DrawTerrain;
        renderDebug.DrawPrimitives = _savedRenderDebug.DrawPrimitives;
        renderDebug.DrawDebugDraw = _savedRenderDebug.DrawDebugDraw;
        renderDebug.DrawSkiaUi = _savedRenderDebug.DrawSkiaUi;
        renderDebug.DrawWorldHudBars = _savedRenderDebug.DrawWorldHudBars;
        renderDebug.DrawWorldHudText = _savedRenderDebug.DrawWorldHudText;
        renderDebug.DrawCombatText = _savedRenderDebug.DrawCombatText;
        renderDebug.AcceptanceScaleMultiplier = _savedRenderDebug.AcceptanceScaleMultiplier;
        _savedRenderDebugValid = false;
    }

    private void ClearPanelIfOwned(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.ClearIfOwned(root);
    }

    internal static void RequestTacticalCameraReset(GameEngine engine)
    {
        Vector2 targetCm = ResolveCameraTarget(engine);
        MassNavigationCameraProfilesConfig cameraProfiles = RequireCameraProfiles(engine);
        string tacticalProfileId = cameraProfiles.TacticalProfileId;
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = CreateCameraRequest(
            tacticalProfileId,
            cameraProfiles.RequestPolicy);
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = tacticalProfileId,
            TargetCm = targetCm
        });
    }

    internal static void RequestStrategicCameraReset(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = RequireSimulationRuntime(engine, "strategic camera reset");
        MassNavigationCameraRequestPolicyConfig requestPolicy = simulation.Config.CameraProfiles.RequestPolicy;
        string strategicProfileId = simulation.Config.CameraProfiles.StrategicProfileId;
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = CreateCameraRequest(
            strategicProfileId,
            requestPolicy);
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = strategicProfileId,
            TargetCm = new Vector2(requestPolicy.StrategicTargetXCm, requestPolicy.StrategicTargetYCm)
        });
    }

    internal static bool IsStrategicWorldCameraActive(GameEngine engine)
    {
        return string.Equals(
            engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId,
            RequireCameraProfiles(engine).StrategicProfileId,
            System.StringComparison.Ordinal);
    }

    private static MassNavigationCameraProfilesConfig RequireCameraProfiles(GameEngine engine)
    {
        return RequireSimulationRuntime(engine, "resolving camera profile ids").Config.CameraProfiles;
    }

    private static MassNavigationSimulationRuntime RequireSimulationRuntime(GameEngine engine, string action)
    {
        return engine.GetService(MassNavigationKeys.SimulationRuntime) as MassNavigationSimulationRuntime
            ?? throw new System.InvalidOperationException($"MassNavigationMod requires simulation runtime before {action}.");
    }

    private static VirtualCameraRequest CreateCameraRequest(
        string profileId,
        MassNavigationCameraRequestPolicyConfig policy)
    {
        return new VirtualCameraRequest
        {
            Id = profileId,
            BlendDurationSeconds = policy.BlendDurationSeconds,
            ResetRuntimeState = policy.ResetRuntimeState,
            SnapToFollowTargetWhenAvailable = policy.SnapToFollowTargetWhenAvailable
        };
    }

    private static Vector2 ResolveCameraTarget(GameEngine engine)
    {
        if (engine.GetService(MassNavigationKeys.SimulationRuntime) is not MassNavigationSimulationRuntime simulation)
        {
            throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime before camera reset.");
        }

        return new Vector2(simulation.SolverWindowCenterXCm, simulation.SolverWindowCenterYCm);
    }

    private readonly record struct RenderDebugSnapshot(
        bool DrawTerrain,
        bool DrawPrimitives,
        bool DrawDebugDraw,
        bool DrawSkiaUi,
        bool DrawWorldHudBars,
        bool DrawWorldHudText,
        bool DrawCombatText,
        float AcceptanceScaleMultiplier);
}

public sealed class MassNavigationPreSimulationStepSystem : ISystem<float>
{
    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void Update(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}

