using System.Threading.Tasks;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CoreInputMod;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
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
        engine.RegisterSystem(new MassNavigationCommandBridgeSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationFormationSystem(engine, simulation), SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
            new MassNavigationOrderBridgeSystem(engine, simulation),
            SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationOrderBridgeSystem>(
            new MassNavigationCommandApplySystem(engine, simulation),
            SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationCommandApplySystem>(
            new MassNavigationSpawnReceiptBindingSystem(engine, simulation),
            SystemGroup.PostMovement);
        engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
            new MassNavigationPreSimulationStepSystem(),
            SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(new MassNavigationHudPresentationSystem(engine, simulation));
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
            ClearPanelIfOwned(engine);
            return;
        }

        if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
        {
            renderDebug.DrawSkiaUi = true;
        }

        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime.");
        ClearPanelIfOwned(engine);
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

    private static void BindMassNavigationLoadedChunks(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime.");
        engine.SetService(CoreServiceKeys.LoadedChunks, (ILoadedChunks)simulation.LoadedChunks);
        if (engine.SpatialQueries is SpatialQueryService spatialQueries)
        {
            spatialQueries.SetLoadedChunks(simulation.LoadedChunks);
        }
    }

    private static void ConfigureCoreMinimap(GameEngine engine)
    {
        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires core MinimapRuntime.");
        minimap.UseRtsFullMapPreset();
        minimap.Visible = true;
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

    internal static void RequestMinimapTacticalHotZoneView(GameEngine engine)
    {
        RequestMinimapTacticalWorldView(engine);
    }

    internal static void RequestCameraJump(GameEngine engine, Vector2 targetCm)
    {
        string tacticalProfileId = RequireCameraProfiles(engine).TacticalProfileId;
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = tacticalProfileId,
            BlendDurationSeconds = 0f,
            ResetRuntimeState = true,
            SnapToFollowTargetWhenAvailable = false
        };
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
        string tacticalProfileId = RequireCameraProfiles(engine).TacticalProfileId;
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = tacticalProfileId,
            BlendDurationSeconds = 0f,
            ResetRuntimeState = true,
            SnapToFollowTargetWhenAvailable = false
        };
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = tacticalProfileId,
            TargetCm = targetCm
        });
    }

    internal static void RequestStrategicCameraReset(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime before strategic camera reset.");
        string strategicProfileId = simulation.Config.CameraProfiles.StrategicProfileId;
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = strategicProfileId,
            BlendDurationSeconds = 0f,
            ResetRuntimeState = true,
            SnapToFollowTargetWhenAvailable = false
        };
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = strategicProfileId,
            TargetCm = new Vector2(0f, 0f)
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
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime before resolving camera profile ids.");
        return simulation.Config.CameraProfiles;
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

