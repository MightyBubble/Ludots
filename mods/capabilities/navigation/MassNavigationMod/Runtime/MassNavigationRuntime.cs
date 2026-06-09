using System.Threading.Tasks;
using System.Numerics;
using Arch.Core;
using CoreInputMod;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Diagnostics;
using Ludots.Core.Navigation.Pathing.Config;
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
    private const string MassNavigationTacticalCameraProfileId = "Camera.Profile.MassNavigationTactical";
    private const string MassNavigationStrategicCameraProfileId = "Camera.Profile.MassNavigationStrategic";
    private const string MassNavigationMeshViewCameraProfileId = "Camera.Profile.MassNavigationMeshView";
    private const float MassNavigationTacticalCameraDistanceCm = 7_000f;
    private const float MassNavigationStrategicCameraDistanceCm = 58_000f;
    private const float MassNavigationMeshViewCameraDistanceCm = 42_000f;
    private const float MassNavigationMeshViewCameraMinDistanceCm = 18_000f;
    private const float MassNavigationMeshViewCameraMaxDistanceCm = 68_000f;

    private static readonly QueryDescription AuthoredPlayerOwnerQuery = new QueryDescription().WithAll<PlayerOwner>();

    private readonly IModContext _context;
    private MassNavigationConfig? _config;
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private RenderDebugSnapshot _savedRenderDebug;
    private bool _savedRenderDebugValid;
    private NodeGraphBoard? _roadGraphBoard;
    private MassNavigationSimulationRuntime? _roadGraphSimulation;
    private MassNavigationRoadGraphDiagnostics? _roadGraphDiagnostics;
    private System.Action<long>? _roadGraphChunkLoaded;
    private System.Action<long>? _roadGraphChunkUnloaded;
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
        var showcaseGuide = new MassNavigationShowcaseGuideRuntime(config.Showcase);
        engine.SetService(MassNavigationKeys.SimulationRuntime, simulation);
        engine.SetService(MassNavigationKeys.ShowcaseGuideRuntime, showcaseGuide);
        engine.RegisterSystem(new MassNavigationAgentMetadataSyncSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationSelectionSyncSystem(engine, simulation), SystemGroup.InputCollection);
        if (MassNavigationShowcaseReplaySystem.TryCreate(engine, simulation, showcaseGuide, out MassNavigationShowcaseReplaySystem replaySystem))
        {
            engine.RegisterSystem(replaySystem, SystemGroup.InputCollection);
        }

        engine.RegisterSystem(new MassNavigationControlSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationRuntimeBakeAuthoringInputSystem(engine, simulation, showcaseGuide), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationPathPreviewInputSystem(engine, simulation, showcaseGuide), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationCommandBridgeSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavigationSpawnReceiptBindingSystem(engine, simulation), SystemGroup.PostMovement);
        engine.RegisterSystem(new MassNavigationOrderBridgeSystem(engine, simulation), SystemGroup.PostMovement);
        engine.RegisterSystem(new MassNavigationFormationSystem(engine, simulation), SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(new MassNavigationShowcasePresentationSystem(engine, simulation, showcaseGuide));
        engine.RegisterPresentationSystem(new MassNavigationHudPresentationSystem(engine, simulation));
        engine.RegisterPresentationSystem(new MassNavigationPanelPresentationSystem(engine, this));
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

        if (!MassNavigationIds.IsNavigationMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            return Task.CompletedTask;
        }

        EnsureSystemsInstalled(engine);
        BindBoardWorld(engine);
        BindMassNavigationRoadGraph(engine);
        BindBakeDataDiagnostics(engine);
        BindMassNavigationLoadedChunks(engine);
        BindLocalSelectionOwner(engine);
        ConfigureRenderDebug(engine);
        ConfigureCoreMinimap(engine);
        EnsureInitialShowcaseCamera(engine);
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
            UnbindMassNavigationRoadGraph();
        }

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
        MassNavigationScenarioBootstrap.SpawnDefaultScenario(
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

    private void BindMassNavigationRoadGraph(GameEngine engine)
    {
        if (engine.CurrentMapSession?.PrimaryBoard is not NodeGraphBoard board)
        {
            UnbindMassNavigationRoadGraph();
            return;
        }

        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime.");
        if (!ReferenceEquals(_roadGraphBoard, board) || !ReferenceEquals(_roadGraphSimulation, simulation))
        {
            UnbindMassNavigationRoadGraph();
            _roadGraphBoard = board;
            _roadGraphSimulation = simulation;
            _roadGraphDiagnostics = new MassNavigationRoadGraphDiagnostics(simulation.StreamingChunkSizeCm);
            _roadGraphChunkLoaded = chunkKey => LoadMassNavigationRoadGraphChunk(board, _roadGraphDiagnostics, chunkKey);
            _roadGraphChunkUnloaded = chunkKey => board.LoadedChunksSource.SetLoaded(chunkKey, loaded: false);
            simulation.LoadedChunks.ChunkLoaded += _roadGraphChunkLoaded;
            simulation.LoadedChunks.ChunkUnloaded += _roadGraphChunkUnloaded;
        }

        foreach (long chunkKey in simulation.LoadedChunks.ActiveChunkKeys)
        {
            LoadMassNavigationRoadGraphChunk(board, _roadGraphDiagnostics!, chunkKey);
        }

        _ = board.GraphRuntime.CurrentGraph;
    }

    private static void LoadMassNavigationRoadGraphChunk(
        NodeGraphBoard board,
        MassNavigationRoadGraphDiagnostics diagnostics,
        long chunkKey)
    {
        board.LoadedChunksSource.SetLoaded(chunkKey, loaded: true);
        if (diagnostics.TryGetChunk(chunkKey, out GraphChunkData chunk))
        {
            board.GraphStore.AddOrReplace(chunkKey, chunk);
        }
    }

    private void UnbindMassNavigationRoadGraph()
    {
        if (_roadGraphSimulation != null)
        {
            if (_roadGraphChunkLoaded != null)
            {
                _roadGraphSimulation.LoadedChunks.ChunkLoaded -= _roadGraphChunkLoaded;
            }

            if (_roadGraphChunkUnloaded != null)
            {
                _roadGraphSimulation.LoadedChunks.ChunkUnloaded -= _roadGraphChunkUnloaded;
            }
        }

        _roadGraphBoard?.LoadedChunksSource.Reset();
        _roadGraphBoard = null;
        _roadGraphSimulation = null;
        _roadGraphDiagnostics = null;
        _roadGraphChunkLoaded = null;
        _roadGraphChunkUnloaded = null;
    }

    private static void BindBakeDataDiagnostics(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime.");
        var pathService = engine.GetService(CoreServiceKeys.PathService);
        var pathStore = engine.GetService(CoreServiceKeys.PathStore);
        var navMeshConfig = engine.GetService(CoreServiceKeys.NavMeshBakeConfig)
            ?? new NavMeshBakeConfigLoader(engine.ConfigPipeline).Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        var pathingConfig = engine.GetService(CoreServiceKeys.PathingConfig)
            ?? new PathingConfigLoader(engine.ConfigPipeline).Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        var navBakeDiagnostics = NavBakeDiagnosticsLoader.TryLoad(
            engine.VFS,
            engine.ModLoader?.LoadedModIds,
            simulation.Config.MapId);
        var hpaGraphDiagnostics = MassNavigationHpaGraphDiagnosticsBuilder.Build(
            engine.VFS,
            engine.ModLoader?.LoadedModIds,
            simulation.Config.MapId,
            navMeshConfig,
            navBakeDiagnostics);
        simulation.BindBakeDataDiagnostics(
            navMeshConfig,
            pathingConfig,
            navBakeDiagnostics,
            pathService,
            pathStore,
            hpaGraphDiagnostics,
            engine.VFS,
            engine.ModLoader?.LoadedModIds);
        engine.GetService(MassNavigationKeys.ShowcaseGuideRuntime)?.BindNavMeshSample(
            simulation.BakeDataDiagnostics,
            navBakeDiagnostics,
            engine.VFS,
            engine.ModLoader?.LoadedModIds,
            simulation.Config.MapId);
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
        RequestCameraPose(engine, MassNavigationTacticalCameraProfileId, targetCm, distanceCm);
    }

    internal static void RequestNavMeshInspectionCamera(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime before navmesh inspection camera reset.");
        MassNavigationShowcaseGuideRuntime? guide = engine.GetService(MassNavigationKeys.ShowcaseGuideRuntime);
        Vector2 targetCm = ResolveNavMeshInspectionTarget(simulation, guide, out float distanceCm);
        RequestCameraPose(
            engine,
            MassNavigationMeshViewCameraProfileId,
            targetCm,
            distanceCm,
            pitch: 64f,
            yaw: 225f,
            fovYDeg: 62f);
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

        selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.LivePrimary, out _);
        selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
        globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
        globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
    }

    private static void EnsureInitialShowcaseCamera(GameEngine engine)
    {
        if (engine.GetService(MassNavigationKeys.ShowcaseGuideRuntime) is { FocusedPanel: true, CurrentStepId: MassNavigationShowcaseStepId.BakeToolQuery })
        {
            RequestNavMeshInspectionCamera(engine);
            return;
        }

        RequestTacticalCameraReset(engine);
    }

    private static Vector2 ResolveNavMeshInspectionTarget(
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime? guide,
        out float distanceCm)
    {
        if (TryResolveGuideSegmentBounds(guide, out float minX, out float minY, out float maxX, out float maxY))
        {
            float spanX = MathF.Max(1f, maxX - minX);
            float spanY = MathF.Max(1f, maxY - minY);
            distanceCm = Math.Clamp(
                MathF.Max(spanX, spanY) * 1.35f,
                MassNavigationMeshViewCameraDistanceCm,
                MassNavigationMeshViewCameraMaxDistanceCm);
            return new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        }

        if (guide?.NavMeshSample.Available == true && simulation.BakeDataDiagnostics != null)
        {
            MassNavigationBakeDataDiagnostics bake = simulation.BakeDataDiagnostics;
            distanceCm = MassNavigationMeshViewCameraDistanceCm;
            return new Vector2(
                bake.WorldMinXCm + (guide.NavMeshSample.ChunkX * bake.MacroChunkSizeXCm) + (bake.MacroChunkSizeXCm * 0.5f),
                bake.WorldMinYCm + (guide.NavMeshSample.ChunkY * bake.MacroChunkSizeYCm) + (bake.MacroChunkSizeYCm * 0.5f));
        }

        distanceCm = MassNavigationMeshViewCameraDistanceCm;
        return new Vector2(simulation.SolverWindowCenterXCm, simulation.SolverWindowCenterYCm);
    }

    private static bool TryResolveGuideSegmentBounds(
        MassNavigationShowcaseGuideRuntime? guide,
        out float minX,
        out float minY,
        out float maxX,
        out float maxY)
    {
        minX = float.PositiveInfinity;
        minY = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        maxY = float.NegativeInfinity;

        if (guide == null)
        {
            return false;
        }

        bool any = false;
        ReadOnlySpan<MassNavigationGuideSegment> activeEdges = guide.ActiveWindowNavMeshEdges;
        for (int i = 0; i < activeEdges.Length; i++)
        {
            IncludeSegment(activeEdges[i], ref minX, ref minY, ref maxX, ref maxY);
            any = true;
        }

        if (!any)
        {
            ReadOnlySpan<MassNavigationGuideSegment> sampleEdges = guide.NavMeshSample.TriangleEdges;
            for (int i = 0; i < sampleEdges.Length; i++)
            {
                IncludeSegment(sampleEdges[i], ref minX, ref minY, ref maxX, ref maxY);
                any = true;
            }
        }

        return any &&
            float.IsFinite(minX) &&
            float.IsFinite(minY) &&
            float.IsFinite(maxX) &&
            float.IsFinite(maxY) &&
            maxX > minX &&
            maxY > minY;
    }

    private static void IncludeSegment(
        MassNavigationGuideSegment segment,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY)
    {
        minX = MathF.Min(minX, MathF.Min(segment.Axcm, segment.Bxcm));
        minY = MathF.Min(minY, MathF.Min(segment.Aycm, segment.Bycm));
        maxX = MathF.Max(maxX, MathF.Max(segment.Axcm, segment.Bxcm));
        maxY = MathF.Max(maxY, MathF.Max(segment.Aycm, segment.Bycm));
    }

    private void ConfigureRenderDebug(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
        {
            return;
        }

        MassNavigationConfig config = EnsureConfig(engine);
        bool focusedShowcase = config.Showcase.FocusedPanel;

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
        renderDebug.DrawWorldHudBars = !focusedShowcase;
        renderDebug.DrawWorldHudText = !focusedShowcase;
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
        RequestCameraPose(engine, MassNavigationTacticalCameraProfileId, targetCm, MassNavigationTacticalCameraDistanceCm);
    }

    internal static void RequestStrategicCameraReset(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod requires simulation runtime before strategic camera reset.");
        _ = simulation;
        RequestCameraPose(
            engine,
            MassNavigationStrategicCameraProfileId,
            new Vector2(0f, 0f),
            MassNavigationStrategicCameraDistanceCm,
            pitch: 68f,
            yaw: 225f,
            fovYDeg: 70f);
    }

    private static void RequestCameraPose(
        GameEngine engine,
        string virtualCameraId,
        Vector2 targetCm,
        float distanceCm,
        float? pitch = null,
        float? yaw = null,
        float? fovYDeg = null)
    {
        if (string.Equals(virtualCameraId, MassNavigationMeshViewCameraProfileId, System.StringComparison.OrdinalIgnoreCase))
        {
            distanceCm = Math.Clamp(
                distanceCm,
                MassNavigationMeshViewCameraMinDistanceCm,
                MassNavigationMeshViewCameraMaxDistanceCm);
        }

        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = virtualCameraId,
            BlendDurationSeconds = 0f,
            ResetRuntimeState = true,
            SnapToFollowTargetWhenAvailable = false
        };
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = virtualCameraId,
            TargetCm = targetCm,
            DistanceCm = distanceCm,
            Pitch = pitch,
            Yaw = yaw,
            FovYDeg = fovYDeg
        });
    }

    internal static bool IsStrategicWorldCameraActive(GameEngine engine)
    {
        return string.Equals(
            engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId,
            MassNavigationStrategicCameraProfileId,
            System.StringComparison.OrdinalIgnoreCase);
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

