using System.Threading.Tasks;
using System.Numerics;
using Arch.Core;
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
using MassNavWebParityMod.Systems;
using MassNavWebParityMod.UI;

namespace MassNavWebParityMod.Runtime;

internal sealed class MassNavWebParityRuntime
{
    private const string MassNavTacticalCameraProfileId = "Camera.Profile.MassNavWebParityTactical";
    private const string MassNavStrategicCameraProfileId = "Camera.Profile.MassNavWebParityStrategic";
    private const float MassNavTacticalCameraDistanceCm = 7_000f;
    private const float MassNavStrategicCameraDistanceCm = 58_000f;

    private static readonly QueryDescription AuthoredPlayerOwnerQuery = new QueryDescription().WithAll<PlayerOwner>();

    private readonly IModContext _context;
    private readonly MassNavWebParityConfig _config;
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private RenderDebugSnapshot _savedRenderDebug;
    private bool _savedRenderDebugValid;
    private readonly MassNavWebParityPanelController _panelController = new();

    public MassNavWebParityRuntime(IModContext context, MassNavWebParityConfig config)
    {
        _context = context;
        _config = config ?? throw new System.ArgumentNullException(nameof(config));
    }

    public void EnsureSystemsInstalled(GameEngine engine)
    {
        if (_systemsInstalled)
        {
            return;
        }

        var simulation = new MassNavSimulationRuntime(_config);
        engine.SetService(MassNavWebParityKeys.SimulationRuntime, simulation);
        engine.RegisterSystem(new MassNavAgentMetadataSyncSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavSelectionSyncSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavWebParityControlSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavCommandBridgeSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavSpawnReceiptBindingSystem(engine, simulation), SystemGroup.PostMovement);
        engine.RegisterSystem(new MassNavCommandApplySystem(engine, simulation), SystemGroup.PostMovement);
        engine.RegisterSystem(new MassNavOrderBridgeSystem(engine, simulation), SystemGroup.PostMovement);
        engine.RegisterSystem(new MassNavFormationSystem(engine, simulation), SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(new MassNavSelectionPerformerSyncSystem(engine, simulation));
        engine.RegisterPresentationSystem(new MassNavHudPresentationSystem(engine, simulation));
        engine.RegisterPresentationSystem(new MassNavPanelPresentationSystem(engine, this));
        _systemsInstalled = true;
        _context.Log("[MassNavWebParityMod] Installed mass-nav fa莽ade runtime skeleton.");
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!MassNavWebParityIds.IsPlaygroundMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            return Task.CompletedTask;
        }

        EnsureSystemsInstalled(engine);
        BindBoardWorld(engine);
        BindMassNavLoadedChunks(engine);
        BindLocalSelectionOwner(engine);
        ConfigureRenderDebug(engine);
        ConfigureCoreMinimap(engine);
        EnsureTacticalCamera(engine);
        EnsureScenario(engine);
        RefreshPanel(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        _scenarioSpawned = false;
        if (context.GetEngine() is { } engine)
        {
            RestoreRenderDebug(engine);
            ClearPanelIfOwned(engine);
        }

        return Task.CompletedTask;
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(engine))
        {
            ClearPanelIfOwned(engine);
            return;
        }

        if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
        {
            renderDebug.DrawSkiaUi = true;
        }

        MassNavSimulationRuntime simulation = engine.GetService(MassNavWebParityKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires simulation runtime.");
        _panelController.MountOrSync(engine, simulation);
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioSpawned &&
            engine.GetService(MassNavWebParityKeys.SimulationRuntime) is { } existing &&
            existing.AgentState.TotalAgents > 0)
        {
            return;
        }

        MassNavSimulationRuntime simulation = engine.GetService(MassNavWebParityKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires simulation runtime.");
        MassNavScenarioBootstrap.SpawnDefaultScenario(
            engine,
            simulation,
            engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new System.InvalidOperationException("MassNavWebParityMod requires TeamEntityLookup."));
        _scenarioSpawned = true;
    }

    private static void BindBoardWorld(GameEngine engine)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires an active MapSession.");
        var board = session.PrimaryBoard
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires a primary board.");
        MassNavSimulationRuntime simulation = engine.GetService(MassNavWebParityKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires simulation runtime.");
        simulation.BindBoardWorld(board.WorldSize);
    }

    private static void BindMassNavLoadedChunks(GameEngine engine)
    {
        MassNavSimulationRuntime simulation = engine.GetService(MassNavWebParityKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires simulation runtime.");
        engine.SetService(CoreServiceKeys.LoadedChunks, (ILoadedChunks)simulation.LoadedChunks);
        if (engine.SpatialQueries is SpatialQueryService spatialQueries)
        {
            spatialQueries.SetLoadedChunks(simulation.LoadedChunks);
        }
    }

    private static void ConfigureCoreMinimap(GameEngine engine)
    {
        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires core MinimapRuntime.");
        minimap.UseRtsFullMapPreset();
        minimap.Visible = true;
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

    internal static void RequestCameraJump(GameEngine engine, Vector2 targetCm, float distanceCm)
    {
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = MassNavTacticalCameraProfileId,
            BlendDurationSeconds = 0f,
            ResetRuntimeState = true,
            SnapToFollowTargetWhenAvailable = false
        };
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = MassNavTacticalCameraProfileId,
            TargetCm = targetCm,
            DistanceCm = distanceCm
        });
    }

    private static void BindLocalSelectionOwner(GameEngine engine)
    {
        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires SelectionRuntime.");
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) ||
            localObj is not Entity owner ||
            !engine.World.IsAlive(owner))
        {
            owner = ResolveSingleAuthoredPlayerOwner(engine);
            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
        }

        if (!engine.World.Has<PlayerOwner>(owner))
        {
            throw new System.InvalidOperationException("MassNavWebParityMod LocalPlayerEntity must author PlayerOwner.");
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
            0 => throw new System.InvalidOperationException("MassNavWebParityMod requires the map to author exactly one PlayerOwner local player entity."),
            _ => throw new System.InvalidOperationException("MassNavWebParityMod found multiple PlayerOwner entities before LocalPlayerEntity was resolved; author one local player or bind CoreServiceKeys.LocalPlayerEntity explicitly.")
        };
    }

    private static void EnsureSelectionOwner(World world, Entity owner, SelectionRuntime selection, System.Collections.Generic.Dictionary<string, object> globals)
    {
        if (!world.Has<SelectionDragState>(owner))
        {
            throw new System.InvalidOperationException("MassNavWebParityMod local player template must author SelectionDragState.");
        }

        selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.LivePrimary, out _);
        selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
        globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
        globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
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
        // Raylib currently gates the official performer ISM/static-mesh lane behind this legacy-named toggle.
        renderDebug.DrawPrimitives = true;
        renderDebug.DrawSkiaUi = true;
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
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = MassNavTacticalCameraProfileId,
            BlendDurationSeconds = 0f,
            ResetRuntimeState = true,
            SnapToFollowTargetWhenAvailable = false
        };
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = MassNavTacticalCameraProfileId,
            TargetCm = targetCm,
            DistanceCm = MassNavTacticalCameraDistanceCm
        });
    }

    internal static void RequestStrategicCameraReset(GameEngine engine)
    {
        MassNavSimulationRuntime simulation = engine.GetService(MassNavWebParityKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavWebParityMod requires simulation runtime before strategic camera reset.");
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = MassNavStrategicCameraProfileId,
            BlendDurationSeconds = 0f,
            ResetRuntimeState = true,
            SnapToFollowTargetWhenAvailable = false
        };
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = MassNavStrategicCameraProfileId,
            TargetCm = new Vector2(0f, 0f),
            DistanceCm = MassNavStrategicCameraDistanceCm,
            Pitch = 68f,
            Yaw = 225f,
            FovYDeg = 70f
        });
    }

    internal static bool IsStrategicWorldCameraActive(GameEngine engine)
    {
        return string.Equals(
            engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId,
            MassNavStrategicCameraProfileId,
            System.StringComparison.OrdinalIgnoreCase);
    }

    private static Vector2 ResolveCameraTarget(GameEngine engine)
    {
        if (engine.GetService(MassNavWebParityKeys.SimulationRuntime) is not MassNavSimulationRuntime simulation)
        {
            throw new System.InvalidOperationException("MassNavWebParityMod requires simulation runtime before camera reset.");
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
