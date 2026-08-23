using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using DynamicNavBakeShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.MovePlanning;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.UI;

namespace DynamicNavBakeShowcaseMod.Runtime;

internal sealed partial class DynamicNavBakeShowcaseRuntime
{
    private const int MaxPathFramingAnchors = 64;
    // Matches MinimapRuntime chrome budget used when estimating whether native chrome fits an external rect.
    private const int MinimapChromeZoomSliderHeight = 28;
    private const int MinimapChromeToggleButtonHeight = 22;
    private const int MinimapChromeGapBelowField = 8;

    private readonly DynamicNavBakeShowcasePanelController _panelController;
    private readonly DynamicNavBakeEditTransaction _editTransaction = new();
    private DynamicNavBakeShowcaseConfig? _config;
    private DynamicNavBakeShowcaseWallPool? _wallPool;
    private RuntimeEntitySpawnRequest[] _spawnScratch = Array.Empty<RuntimeEntitySpawnRequest>();
    private Entity[] _squadEntities = Array.Empty<Entity>();
    private NavBakeTileCoord[] _residentScratch = Array.Empty<NavBakeTileCoord>();
    private int[] _pathXcm = Array.Empty<int>();
    private int[] _pathZcm = Array.Empty<int>();
    private Vector2[] _playerFramingAnchorScratch = Array.Empty<Vector2>();
    private int[] _coarseNodePath = Array.Empty<int>();
    private (int XCm, int ZCm)[] _coarseCorridorWorldPoints = Array.Empty<(int, int)>();
    private DynamicNavBakeShowcaseCoarseGraphBootstrap.CoarseGraphState? _coarseGraph;
    private NodeGraphBoard? _nodeGraphBoard;
    private GridBoard? _terrainBoard;
    private bool _scenarioSpawned;
    private bool _entitiesBound;
    private bool _mapFocusPresentationPending;
    private bool _squadDeployed;
    private bool _constructionMode;
    private bool _moveCommandActive;
    private int _openWorldHotspotIndex;
    private int _corridorCursor;
    private int _localSegmentGoalXCm;
    private int _localSegmentGoalZCm;
    private int _presentationPathRevision;
    private int _presentationCorridorRevision;
    private ulong _publishedFormalRouteGeometrySignature;
    private readonly int[] _formalRouteWaypointXScratch = new int[64];
    private readonly int[] _formalRouteWaypointYScratch = new int[64];
    private NavPathStatus _lastPathStatus = NavPathStatus.NotReady;
    private int _lastPathPointCount;
    private int _lastCoarseNodeCount;
    private ulong _lastPathGeneration;
    private DynamicNavBakePathOrchestrationState _pathOrchestrationState = DynamicNavBakePathOrchestrationState.Idle;
    private string _lastStatus = "Dynamic NavMesh bake showcase ready.";
    private Order[] _moveOrderScratch = Array.Empty<Order>();
    private bool _openWorldMinimapEnabledByShowcase;
    private bool _openWorldMinimapVisibleSaved;
    private bool _openWorldMinimapNativeChromeVisibleSaved;
    private MinimapPreset _openWorldMinimapPresetSaved;
    private Entity _openWorldMinimapFollowEntitySaved = Entity.Null;
    private float _openWorldMinimapHalfExtentCmSaved;
    private bool _openWorldMinimapRotateWithCameraSaved;
    private float _openWorldMinimapZoomNormalizedSaved;
    private bool _openWorldAutoCaptureMinimapRectActive;
    private Entity _openWorldKnowledgeViewer = Entity.Null;
    private Entity[] _openWorldKnowledgeTargets = Array.Empty<Entity>();
    private KnowledgeDisclosureRecord[] _openWorldKnowledgePrevious = Array.Empty<KnowledgeDisclosureRecord>();
    private bool[] _openWorldKnowledgeHadPrevious = Array.Empty<bool>();
    private KnowledgeDisclosureRecord _openWorldKnowledgeOwnedSemantic;
    private int _openWorldKnowledgeTargetCount;
    private int _formalMoveCommandSubmitCount;
    private readonly CameraPoseRequest _autoCapturePoseScratch = new();
    private bool _autoTimelineEnvironmentValidated;
    /// <summary>
    /// Sticky-true cache for auto-timeline enablement. False is never cached so tests may set the
    /// env var after map load; once true (or Validate succeeds), Enable/framing only reads this bool.
    /// </summary>
    private bool _autoTimelineEnabledSticky;
    private bool _editBakeAwaitingCompletion;
    private ulong _editBakeGenerationBefore;
    private int _editBakeFailedBatchCountBefore;

    public DynamicNavBakeShowcaseRuntime()
    {
        _panelController = new DynamicNavBakeShowcasePanelController(this);
    }

    public DynamicNavBakeShowcaseConfig ActiveConfig => _config
        ?? throw new InvalidOperationException("DynamicNavBakeShowcase config has not been loaded.");

    public bool IsActive => _config != null;
    public string LastStatus => _lastStatus;
    public NavPathStatus LastPathStatus => _lastPathStatus;
    public int LastPathPointCount => _lastPathPointCount;
    public int LastCoarseCorridorNodeCount => _lastCoarseNodeCount;
    public int OpenWorldCorridorCursor => _corridorCursor;
    public bool MoveCommandActive => _moveCommandActive;
    public DynamicNavBakePathOrchestrationState PathOrchestrationState => _pathOrchestrationState;
    public bool SquadDeployed => _squadDeployed;
    public bool ConstructionMode => _constructionMode;
    public int WallDeployedCount => _wallPool?.DeployedCount ?? 0;
    public int PresentationPathRevision => _presentationPathRevision;
    public int PresentationCorridorRevision => _presentationCorridorRevision;
    public IReadOnlyList<int> CurrentPathXcm => _pathXcm;
    public IReadOnlyList<int> CurrentPathZcm => _pathZcm;
    public ReadOnlySpan<(int XCm, int ZCm)> CoarseCorridorWorldPoints
        => _coarseCorridorWorldPoints.AsSpan(0, _lastCoarseNodeCount);
    public ReadOnlySpan<Entity> SquadEntities => _squadEntities;

    /// <summary>
    /// Count of successful <see cref="TryCommandMoveToGoal"/> submissions in the current showcase session.
    /// Showcase-owned, allocation-free counter for proving Initial/Dynamic/Final each submit exactly once.
    /// Does not count open-world corridor checkpoint re-orders.
    /// </summary>
    public int FormalMoveCommandSubmitCount => _formalMoveCommandSubmitCount;

    public Vector2 ResolveAuthoredCameraTargetCm()
    {
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        if (config.ResolvedSceneKind != DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            return new Vector2(config.CameraTargetXCm, config.CameraTargetYCm);
        }

        DynamicNavBakeShowcaseOpenWorldConfig openWorld = config.OpenWorld
            ?? throw new InvalidOperationException("Open-world config is required for authored camera target.");
        if ((uint)_openWorldHotspotIndex >= (uint)openWorld.Hotspots.Length)
        {
            throw new InvalidOperationException(
                $"Open-world hotspot index {_openWorldHotspotIndex} is out of range for {openWorld.Hotspots.Length} hotspots.");
        }

        DynamicNavBakeShowcaseHotspotConfig hotspot = openWorld.Hotspots[_openWorldHotspotIndex];
        return new Vector2(hotspot.CameraTargetXCm, hotspot.CameraTargetYCm);
    }

    public void EnsureAutoCaptureCameraActive(GameEngine engine)
    {
        ValidateAutoTimelineEnvironment(
            "EnsureAutoCaptureCameraActive requires the Dynamic NavBake auto timeline environment variable.");

        ActivateShowcaseCamera(engine, ResolveAuthoredCameraTargetCm());
        if (ActiveConfig.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            EnableOpenWorldMinimap(engine);
        }

        ApplyAutoCapturePlayerFraming(engine);
    }

    /// <summary>
    /// Deterministic auto-capture framing from live squad + action hotspot + path lookahead.
    /// Reuses a single <see cref="CameraPoseRequest"/>; no LINQ / heap collections on the hot path.
    /// </summary>
    public void ApplyAutoCapturePlayerFraming(GameEngine engine)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        ValidateAutoTimelineEnvironment(
            "ApplyAutoCapturePlayerFraming requires the Dynamic NavBake auto timeline environment variable.");

        CameraManager camera = engine.GameSession.Camera
            ?? throw new InvalidOperationException("ApplyAutoCapturePlayerFraming requires GameSession.Camera.");
        VirtualCameraBrain brain = camera.VirtualCameraBrain
            ?? throw new InvalidOperationException("ApplyAutoCapturePlayerFraming requires VirtualCameraBrain.");
        string activeId = brain.ActiveCameraId;
        if (!string.Equals(activeId, DynamicNavBakeShowcaseIds.AutoCaptureCameraId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ApplyAutoCapturePlayerFraming requires active virtual camera '{DynamicNavBakeShowcaseIds.AutoCaptureCameraId}', " +
                $"got '{(string.IsNullOrEmpty(activeId) ? "<none>" : activeId)}'.");
        }

        ResolveAutoCaptureOrbitOptics(camera, out float pitchDeg, out float fovYDeg, out float yawDeg);
        DynamicNavBakeShowcasePlayerFramingPose pose = ResolvePlayerFramingPose(engine);
        _autoCapturePoseScratch.VirtualCameraId = DynamicNavBakeShowcaseIds.AutoCaptureCameraId;
        _autoCapturePoseScratch.TargetCm = pose.TargetCm;
        _autoCapturePoseScratch.DistanceCm = pose.DistanceCm;
        _autoCapturePoseScratch.TargetHeightCm = null;
        _autoCapturePoseScratch.Yaw = yawDeg;
        _autoCapturePoseScratch.Pitch = pitchDeg;
        _autoCapturePoseScratch.FovYDeg = fovYDeg;
        camera.ApplyPose(_autoCapturePoseScratch);

        // Auto timeline env may flip on after map bind; re-sync the authored capture rect here.
        if (ActiveConfig.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            EnableOpenWorldMinimap(engine);
        }

        // Presentation can run before the next fixed-step camera update; write State so the same
        // host frame renders and validates the computed framing without allocating Synchronize.
        camera.State.TargetCm = pose.TargetCm;
        camera.State.DistanceCm = pose.DistanceCm;
        camera.State.Yaw = yawDeg;
        camera.State.Pitch = pitchDeg;
        camera.State.FovYDeg = fovYDeg;
        camera.PreviousState.TargetCm = pose.TargetCm;
        camera.PreviousState.DistanceCm = pose.DistanceCm;
        camera.PreviousState.Yaw = yawDeg;
        camera.PreviousState.Pitch = pitchDeg;
        camera.PreviousState.FovYDeg = fovYDeg;
    }

    private void ValidateAutoTimelineEnvironment(string failureMessage)
    {
        if (_autoTimelineEnvironmentValidated)
        {
            return;
        }

        if (!IsAutoTimelineEnabledSticky())
        {
            throw new InvalidOperationException(failureMessage);
        }

        _autoTimelineEnvironmentValidated = true;
        _autoTimelineEnabledSticky = true;
    }

    /// <summary>
    /// Sticky-true auto-timeline gate: once enabled, never re-reads the environment variable.
    /// False is not cached so interactive map load may later flip the env for auto-capture tests.
    /// </summary>
    private bool IsAutoTimelineEnabledSticky()
    {
        if (_autoTimelineEnabledSticky)
        {
            return true;
        }

        if (!DynamicNavBakeShowcaseIds.IsAutoTimelineEnabled())
        {
            return false;
        }

        _autoTimelineEnabledSticky = true;
        return true;
    }

    public DynamicNavBakeShowcasePlayerFramingPose ResolvePlayerFramingPose(GameEngine engine)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        DynamicNavBakeShowcasePlayerFramingConfig framing = config.RaylibAutoTimeline.PlayerFraming
            ?? throw new InvalidOperationException(
                "ResolvePlayerFramingPose requires raylibAutoTimeline.playerFraming.");

        CameraManager camera = engine.GameSession.Camera
            ?? throw new InvalidOperationException("ResolvePlayerFramingPose requires GameSession.Camera.");
        ResolveAutoCaptureOrbitOptics(camera, out float pitchDeg, out float fovYDeg, out float yawDeg);

        int count = CollectPlayerFramingAnchors(engine, config, _playerFramingAnchorScratch);
        return DynamicNavBakeShowcasePlayerFraming.Compute(
            _playerFramingAnchorScratch.AsSpan(0, count),
            framing,
            pitchDeg,
            fovYDeg,
            yawDeg);
    }

    public int CountSquadMembersInsidePlayerFraming(GameEngine engine)
        => CaptureSquadPlayerFramingVisibility(engine).InsideCount;

    public DynamicNavBakeShowcasePlayerFramingVisibility CaptureSquadPlayerFramingVisibility(GameEngine engine)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        DynamicNavBakeShowcasePlayerFramingConfig framing = config.RaylibAutoTimeline.PlayerFraming
            ?? throw new InvalidOperationException(
                "CountSquadMembersInsidePlayerFraming requires raylibAutoTimeline.playerFraming.");
        CameraManager camera = engine.GameSession.Camera
            ?? throw new InvalidOperationException("CountSquadMembersInsidePlayerFraming requires GameSession.Camera.");
        IViewController view = engine.GetService(CoreServiceKeys.ViewController)
            ?? throw new InvalidOperationException(
                "CountSquadMembersInsidePlayerFraming requires CoreServiceKeys.ViewController.");
        Vector2 resolution = view.Resolution;
        if (MathF.Abs(resolution.X - framing.CaptureWidthPx) > 0.01f ||
            MathF.Abs(resolution.Y - framing.CaptureHeightPx) > 0.01f)
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake capture viewport mismatch. actual={resolution.X:F0}x{resolution.Y:F0} " +
                $"configured={framing.CaptureWidthPx}x{framing.CaptureHeightPx}.");
        }

        if (_squadEntities.Length != config.Squad.Count)
        {
            throw new InvalidOperationException(
                $"Authored squad binding count mismatch. bound={_squadEntities.Length} configured={config.Squad.Count}.");
        }

        CameraRenderState3D renderState = CameraViewportUtil.StateToRenderState(camera.State);
        float minX = framing.SafeInsetLeftPx;
        float minY = framing.SafeInsetTopPx;
        float maxX = framing.CaptureWidthPx - framing.SafeInsetRightPx;
        float maxY = framing.CaptureHeightPx - framing.SafeInsetBottomPx;

        int inside = 0;
        int finite = 0;
        float projectedMinX = float.PositiveInfinity;
        float projectedMinY = float.PositiveInfinity;
        float projectedMaxX = float.NegativeInfinity;
        float projectedMaxY = float.NegativeInfinity;
        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"Authored squad member[{i}] is missing or dead at the screenshot framing gate.");
            }

            if (!engine.World.TryGet(entity, out WorldPositionCm position))
            {
                throw new InvalidOperationException(
                    $"Authored squad member[{i}] is alive but missing WorldPositionCm.");
            }

            WorldCmInt2 world = position.ToWorldCmInt2();
            Vector2 screen = CameraViewportUtil.WorldToScreen(
                WorldUnits.WorldCmToVisualMeters(in world),
                in renderState,
                resolution,
                framing.AspectRatio);
            if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
            {
                continue;
            }

            finite++;
            projectedMinX = MathF.Min(projectedMinX, screen.X);
            projectedMinY = MathF.Min(projectedMinY, screen.Y);
            projectedMaxX = MathF.Max(projectedMaxX, screen.X);
            projectedMaxY = MathF.Max(projectedMaxY, screen.Y);
            if (screen.X >= minX &&
                screen.X <= maxX &&
                screen.Y >= minY &&
                screen.Y <= maxY)
            {
                inside++;
            }
        }

        return new DynamicNavBakeShowcasePlayerFramingVisibility(
            inside,
            finite,
            projectedMinX,
            projectedMinY,
            projectedMaxX,
            projectedMaxY);
    }

    private int CollectPlayerFramingAnchors(
        GameEngine engine,
        DynamicNavBakeShowcaseConfig config,
        Span<Vector2> anchors)
    {
        DynamicNavBakeShowcasePlayerFramingConfig framing = config.RaylibAutoTimeline.PlayerFraming
            ?? throw new InvalidOperationException(
                "CollectPlayerFramingAnchors requires raylibAutoTimeline.playerFraming.");

        int count = 0;
        float squadCentroidX = 0f;
        float squadCentroidY = 0f;
        int squadAnchorCount = 0;
        if (_squadDeployed)
        {
            int alive = 0;
            for (int i = 0; i < _squadEntities.Length; i++)
            {
                Entity entity = _squadEntities[i];
                if (entity == Entity.Null || !engine.World.IsAlive(entity))
                {
                    continue;
                }

                if (!engine.World.TryGet(entity, out WorldPositionCm position))
                {
                    throw new InvalidOperationException(
                        $"Authored squad member[{i}] is alive but missing WorldPositionCm for player framing.");
                }

                if (count >= anchors.Length)
                {
                    throw new InvalidOperationException(
                        $"Player framing anchor capacity ({anchors.Length}) is too small for the authored squad.");
                }

                WorldCmInt2 world = position.ToWorldCmInt2();
                anchors[count++] = new Vector2(world.X, world.Y);
                squadCentroidX += world.X;
                squadCentroidY += world.Y;
                squadAnchorCount++;
                alive++;
            }

            if (alive <= 0)
            {
                throw new InvalidOperationException(
                    "Player framing requires at least one alive authored squad member after deploy.");
            }
        }
        else
        {
            anchors[count++] = new Vector2(config.Squad.CenterXCm, config.Squad.CenterYCm);
            squadCentroidX = config.Squad.CenterXCm;
            squadCentroidY = config.Squad.CenterYCm;
            squadAnchorCount = 1;
        }

        squadCentroidX /= squadAnchorCount;
        squadCentroidY /= squadAnchorCount;

        float lookahead = framing.PathLookaheadCm;
        float lookaheadSq = lookahead * lookahead;
        // Wall/hotspot anchors only while the gate is physically deployed. After demolish,
        // never keep a phantom wall center (or invent a replacement hardcoded anchor).
        if (WallDeployedCount > 0)
        {
            ResolveActiveWallCenter(out int wallXCm, out int wallYCm, out _);
            float wallDx = wallXCm - squadCentroidX;
            float wallDy = wallYCm - squadCentroidY;
            if (config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.Rts ||
                (wallDx * wallDx) + (wallDy * wallDy) <= lookaheadSq)
            {
                if (count >= anchors.Length)
                {
                    throw new InvalidOperationException("Player framing anchor capacity exhausted before hotspot/wall.");
                }

                anchors[count++] = new Vector2(wallXCm, wallYCm);
            }
        }

        // Local battlefield only: never pull the remote final goal or local segment goal into the AABB.
        // The active wall/hotspot and local path share the same authored lookahead boundary.
        int pathPoints = Math.Min(_pathXcm.Length, _pathZcm.Length);
        int pathAnchorsAdded = 0;
        for (int i = 0; i < pathPoints && pathAnchorsAdded < MaxPathFramingAnchors; i++)
        {
            float dx = _pathXcm[i] - squadCentroidX;
            float dy = _pathZcm[i] - squadCentroidY;
            if ((dx * dx) + (dy * dy) > lookaheadSq)
            {
                continue;
            }

            if (count >= anchors.Length)
            {
                throw new InvalidOperationException(
                    "Player framing anchor capacity exhausted while collecting path lookahead points.");
            }

            anchors[count++] = new Vector2(_pathXcm[i], _pathZcm[i]);
            pathAnchorsAdded++;
        }

        return count;
    }

    /// <summary>
    /// Auto-capture framing must use the locked Orbit definition optics, not a stale Tactical State
    /// left over before the next CameraManager.Update sync.
    /// </summary>
    private static void ResolveAutoCaptureOrbitOptics(
        CameraManager camera,
        out float pitchDeg,
        out float fovYDeg,
        out float yawDeg)
    {
        VirtualCameraBrain brain = camera.VirtualCameraBrain
            ?? throw new InvalidOperationException("Auto-capture framing requires VirtualCameraBrain.");
        VirtualCameraDefinition definition = brain.ActiveDefinition
            ?? throw new InvalidOperationException("Auto-capture framing requires an active virtual camera definition.");
        if (!string.Equals(definition.Id, DynamicNavBakeShowcaseIds.AutoCaptureCameraId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Auto-capture framing requires '{DynamicNavBakeShowcaseIds.AutoCaptureCameraId}', got '{definition.Id}'.");
        }

        pitchDeg = definition.Pitch;
        fovYDeg = definition.FovYDeg;
        yawDeg = definition.Yaw;
        if (!float.IsFinite(pitchDeg) || !float.IsFinite(fovYDeg) || !float.IsFinite(yawDeg) || fovYDeg <= 0f || fovYDeg >= 179f)
        {
            throw new InvalidOperationException(
                $"Auto-capture camera '{definition.Id}' has malformed optics pitch={pitchDeg}, yaw={yawDeg}, fovYDeg={fovYDeg}.");
        }
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        string? mapId = engine.CurrentMapSession?.MapId.Value;
        if (!DynamicNavBakeShowcaseIds.IsShowcaseMap(mapId))
        {
            Unbind(engine);
            return Task.CompletedTask;
        }

        DynamicNavBakeShowcaseConfig config = EnsureConfig(engine);
        if (!string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            Unbind(engine);
            return Task.CompletedTask;
        }

        ConfigureNavMeshPresentation(engine, config.Presentation.NavMeshEnabled);

        BindBoards(engine);
        if (!_scenarioSpawned)
        {
            BootstrapScenario(engine, config);
        }

        if (_scenarioSpawned)
        {
            EnsureEntitiesBound(engine);
        }

        if (!_entitiesBound)
        {
            _mapFocusPresentationPending = true;
            return Task.CompletedTask;
        }

        CompleteFocusedMapPresentation(engine);
        return Task.CompletedTask;
    }

    internal void CompletePendingMapFocusPresentation(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!_mapFocusPresentationPending || !IsActive)
        {
            return;
        }

        EnsureEntitiesBound(engine);
        if (!_entitiesBound)
        {
            return;
        }

        CompleteFocusedMapPresentation(engine);
    }

    private void CompleteFocusedMapPresentation(GameEngine engine)
    {
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        ActivateShowcaseCamera(engine, new Vector2(config.CameraTargetXCm, config.CameraTargetYCm));
        if (config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            EnableOpenWorldMinimap(engine);
        }

        RefreshPanel(engine);
        _mapFocusPresentationPending = false;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (DynamicNavBakeShowcaseIds.IsShowcaseMap(mapId))
        {
            Unbind(engine);
        }

        return Task.CompletedTask;
    }

    public bool TrySwitchAlgorithm(GameEngine engine, NavBakeAlgorithmKind algorithm, out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        EnsureResidentScratch(Math.Max(queue.ResidentWindowCount, queue.CommittedResidentWindowCount));
        int count = queue.CommittedResidentWindowCount > 0
            ? queue.CopyCommittedResidentWindow(_residentScratch)
            : queue.CopyResidentWindow(_residentScratch);
        if (count <= 0)
        {
            error = "Resident window is empty.";
            return false;
        }

        try
        {
            queue.SwitchAlgorithm(algorithm, _residentScratch.AsSpan(0, count));
            _lastStatus = $"Requested algorithm '{NavBakeNames.FormatAlgorithm(algorithm)}' over {count} committed resident tiles.";
            ClearStalePath(engine, "Algorithm switch requested; waiting for new generation.");
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }
    }

    public bool TryBuildWall(GameEngine engine, out string error)
    {
        EnsureEntitiesBound(engine);
        DynamicNavBakeShowcaseWallPool pool = RequireWallPool();
        ResolveActiveWallCenter(out int centerXCm, out int centerYCm, out string? hotspotLabel);
        if (!pool.TryBuildAll(engine, ActiveConfig, centerXCm, centerYCm, out error))
        {
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        string builtStatus = hotspotLabel == null
            ? "Central gate wall deployed."
            : $"Wall deployed at hotspot '{hotspotLabel}'.";
        ClearStalePath(engine, builtStatus);
        RefreshPanel(engine);
        return true;
    }

    public bool TryDemolishWall(GameEngine engine, out string error)
    {
        EnsureEntitiesBound(engine);
        DynamicNavBakeShowcaseWallPool pool = RequireWallPool();
        ResolveActiveWallCenter(out _, out _, out string? hotspotLabel);
        if (!pool.TryDemolishAll(engine, ActiveConfig, out error))
        {
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        string demolishedStatus = hotspotLabel == null
            ? "Central gate wall demolished."
            : $"Wall demolished at hotspot '{hotspotLabel}'.";
        ClearStalePath(engine, demolishedStatus);
        RefreshPanel(engine);
        return true;
    }

    public bool TryDeploySquad(GameEngine engine, out string error)
        => TryDeploySquadCore(engine, DynamicNavBakeShowcaseDeployWaitPolicy.SynchronousDrain, out error);

    public bool TryDeploySquadNonBlocking(GameEngine engine, out string error)
        => TryDeploySquadCore(engine, DynamicNavBakeShowcaseDeployWaitPolicy.NonBlocking, out error);

    private bool TryDeploySquadCore(
        GameEngine engine,
        DynamicNavBakeShowcaseDeployWaitPolicy waitPolicy,
        out string error)
    {
        error = string.Empty;
        EnsureEntitiesBound(engine);
        if (_squadDeployed)
        {
            if (waitPolicy == DynamicNavBakeShowcaseDeployWaitPolicy.NonBlocking)
            {
                error = string.Empty;
                return true;
            }

            error = "Squad is already deployed.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        ApplyInitialCommandSource(engine);
        RefreshSquadCommandKnowledge(engine);
        _squadDeployed = true;
        _corridorCursor = 0;
        _lastStatus = waitPolicy == DynamicNavBakeShowcaseDeployWaitPolicy.NonBlocking
            ? "Squad deployed and selected (non-blocking wait for resident nav)."
            : "Squad deployed and selected.";
        RecomputePath(engine);
        if (_pathOrchestrationState == DynamicNavBakePathOrchestrationState.WindowRebuilding)
        {
            if (waitPolicy == DynamicNavBakeShowcaseDeployWaitPolicy.SynchronousDrain)
            {
                DrainUntilIdle(engine, maxTicks: 8192);
                RecomputePath(engine);
            }
            // NonBlocking: return after scheduling; later host frames wait for nav stability.
        }

        RefreshPanel(engine);
        return true;
    }

    public bool TryCommandMoveToGoal(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (!_squadDeployed)
        {
            error = "Deploy the squad before issuing a move command.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        RecomputePath(engine);
        if (_pathOrchestrationState == DynamicNavBakePathOrchestrationState.WindowRebuilding ||
            _lastPathStatus == NavPathStatus.NotReady)
        {
            error = "Navmesh generation is not ready yet.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        if (_lastPathStatus != NavPathStatus.Ok ||
            _pathOrchestrationState != DynamicNavBakePathOrchestrationState.LocalSegmentReady)
        {
            error = _lastPathStatus == NavPathStatus.NotReachable
                ? "Goal is unreachable with the current navmesh generation."
                : $"Path status: {_lastPathStatus}.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        if (!TrySubmitLocalSegmentMoveOrders(engine, out error))
        {
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        _moveCommandActive = true;
        _formalMoveCommandSubmitCount = checked(_formalMoveCommandSubmitCount + 1);
        _lastStatus = ActiveConfig.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld
            ? $"Local segment ordered ({_lastPathPointCount} points) along {_lastCoarseNodeCount}-node corridor."
            : $"Path ordered with {_lastPathPointCount} points.";
        RefreshPanel(engine);
        return true;
    }

    public bool TryNextHotspot(GameEngine engine, out string error)
    {
        error = string.Empty;
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        if (config.ResolvedSceneKind != DynamicNavBakeShowcaseSceneKind.OpenWorld || config.OpenWorld == null)
        {
            error = "Next hotspot is only available in open-world scenes.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        int next = (_openWorldHotspotIndex + 1) % config.OpenWorld.Hotspots.Length;
        return TryFocusHotspot(engine, next, out error);
    }

    public bool TryReturn(GameEngine engine, out string error)
    {
        error = string.Empty;
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        if (config.ResolvedSceneKind != DynamicNavBakeShowcaseSceneKind.OpenWorld || config.OpenWorld == null)
        {
            error = "Return is only available in open-world scenes.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        return TryFocusHotspot(engine, config.OpenWorld.InitialHotspotIndex, out error);
    }

    public void DrainUntilIdle(GameEngine engine, int maxTicks)
    {
        if (maxTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTicks));
        }

        EnsureEntitiesBound(engine);
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        float fixedDt = Time.FixedDeltaTime;
        if (fixedDt <= 0f)
        {
            throw new InvalidOperationException(
                $"DynamicNavBake DrainUntilIdle requires Time.FixedDeltaTime > 0; got {fixedDt}.");
        }

        // Structural obstacle first-capture / bridge pose settle can enqueue dirty on the FixedStep
        // AFTER the rebuild queue first reports Idle. Returning on that first Idle tick lets a late
        // valid dirty generation leak into the next evidence epoch (unequal bootstrap work).
        // Require consecutive quiescent FixedSteps so late capture is drained, not discarded.
        // Callers that pass maxTicks:1 (single-step probes) keep a one-tick idle contract.
        int requiredQuiescentFixedTicks = maxTicks >= 2 ? 2 : 1;
        int quiescentStreak = 0;
        engine.TryGetService(CoreServiceKeys.RuntimeNavMeshTelemetry, out RuntimeNavMeshTelemetryService? telemetry);

        for (int i = 0; i < maxTicks; i++)
        {
            // Advance by one FixedDeltaTime so RealtimePacemaker always runs a FixedStep.
            // Tick(1/60) under FixedDeltaTime=0.05 returns Idle before DirtySystem can capture
            // wall teleports when the rebuild queue is already empty.
            int samplesBefore = telemetry?.SampleCount ?? 0;
            engine.Tick(fixedDt);
            ThrowIfSimulationBudgetFused(engine, i);
            // FixedStep orchestration (open-world corridor + generation refresh) runs inside Tick
            // via DynamicNavBakeShowcaseFixedStepSystem — SSOT, no second path here.

            bool queueBusy = queue.Status != RuntimeNavMeshRebuildStatus.Idle ||
                             queue.HasResidentWindowTransition ||
                             queue.PendingTileCount > 0 ||
                             queue.SealedRemainingCount > 0;
            bool telemetryBusy = telemetry != null &&
                                 (telemetry.HasOpenGeneration || telemetry.SampleCount > samplesBefore);
            if (queueBusy || telemetryBusy)
            {
                quiescentStreak = 0;
                continue;
            }

            quiescentStreak++;
            if (quiescentStreak < requiredQuiescentFixedTicks)
            {
                continue;
            }

            RefreshPanel(engine);
            return;
        }

        bool stillBusy = queue.Status != RuntimeNavMeshRebuildStatus.Idle ||
                         queue.HasResidentWindowTransition ||
                         queue.PendingTileCount > 0 ||
                         queue.SealedRemainingCount > 0 ||
                         (telemetry != null && telemetry.HasOpenGeneration);
        if (stillBusy)
        {
            RefreshPanel(engine);
            throw new InvalidOperationException(
                $"DynamicNavBake DrainUntilIdle exhausted {maxTicks} FixedSteps while nav rebuild remained busy " +
                $"(status={queue.Status}, pending={queue.PendingTileCount}, sealed={queue.SealedRemainingCount}, " +
                $"residentTransition={queue.HasResidentWindowTransition}, hasOpenGeneration={telemetry?.HasOpenGeneration == true}).");
        }

        RefreshPanel(engine);
    }

    /// <summary>
    /// Non-blocking FixedStep orchestration shared by the ECS system and production FixedStep cadence.
    /// Refreshes committed-generation path evidence and advances open-world corridor checkpoints.
    /// Never calls engine.Tick / DrainUntilIdle.
    /// </summary>
    public void AdvanceFixedStepOrchestration(GameEngine engine)
    {
        if (!IsActive)
        {
            return;
        }

        EnsureEntitiesBound(engine);
        RefreshSquadCommandKnowledge(engine);
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        _ = TryUpdatePlacementPreview(engine, out _);
        AdvanceEditBake(engine, queue);

        // MovePlan may re-track routes earlier in AbilityActivation. While tiles are rebuilding,
        // release sink rows only (orders stay) so PostMovement ApplyRoute cannot re-solve NoPath.
        if (_editBakeAwaitingCompletion)
        {
            ReleaseSquadFormalRoutesFromSink(engine);
        }

        UpdateProgressStatus(engine, queue);
        if (_moveCommandActive)
        {
            AdvanceOpenWorldMoveIfNeeded(engine);
        }

        MaybeRefreshPathAfterGeneration(engine, queue);
        SyncFormalRoutePathPresentation(engine);
    }

    /// <summary>
    /// Publishes the live MassNavigation formal PathService polyline into the showcase path overlay.
    /// This is the same waypoint strip the route sink feeds to agents — not a parallel preview solve.
    /// </summary>
    private void SyncFormalRoutePathPresentation(GameEngine engine)
    {
        MassNavigationRouteExecutionSink? routeSink = ResolveRouteExecutionSink(engine);
        if (routeSink == null || routeSink.ActiveRouteCount <= 0 || _squadEntities.Length <= 0)
        {
            if (_publishedFormalRouteGeometrySignature != 0UL)
            {
                _publishedFormalRouteGeometrySignature = 0UL;
                // Keep goal-path overlay when formal routes are gone; only clear when we were
                // showing a formal strip (signature non-zero) and no showcase goal path is active.
                if (!_moveCommandActive && _pathOrchestrationState == DynamicNavBakePathOrchestrationState.Idle)
                {
                    UpdatePathBuffers(Array.Empty<int>(), Array.Empty<int>());
                }
            }

            return;
        }

        Entity routeActor = Entity.Null;
        MassNavigationRouteEvidence evidence = default;
        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity candidate = _squadEntities[i];
            if (candidate == Entity.Null ||
                !engine.World.IsAlive(candidate) ||
                !routeSink.TryGetActiveRouteEvidence(candidate, out evidence) ||
                !evidence.RouteReady ||
                evidence.WaypointCount < 2 ||
                evidence.ResolvedDomain == PathDomain.None)
            {
                continue;
            }

            routeActor = candidate;
            break;
        }

        if (routeActor == Entity.Null)
        {
            return;
        }

        if (evidence.WaypointGeometrySignature == _publishedFormalRouteGeometrySignature &&
            _pathXcm.Length == evidence.WaypointCount)
        {
            return;
        }

        if (!routeSink.TryCopyActiveRouteWaypoints(
                routeActor,
                _formalRouteWaypointXScratch,
                _formalRouteWaypointYScratch,
                out int count) ||
            count < 2)
        {
            return;
        }

        var pathX = new int[count];
        var pathZ = new int[count];
        Array.Copy(_formalRouteWaypointXScratch, pathX, count);
        Array.Copy(_formalRouteWaypointYScratch, pathZ, count);
        UpdatePathBuffers(pathX, pathZ);
        _lastPathPointCount = count;
        _lastPathStatus = NavPathStatus.Ok;
        _publishedFormalRouteGeometrySignature = evidence.WaypointGeometrySignature;
    }

    public DynamicNavBakeShowcaseEvidence CaptureEvidence(GameEngine engine)
    {
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        if (!engine.TryGetService(CoreServiceKeys.RuntimeNavMeshTelemetry, out RuntimeNavMeshTelemetryService telemetry) ||
            telemetry == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBake evidence requires RuntimeNavMeshTelemetry; all-zero fallback snapshots are not allowed.");
        }

        if (!engine.TryGetService(CoreServiceKeys.NavTriangleSurface, out NavTriangleSurfaceTileIndex triangleSurface) ||
            triangleSurface == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBake evidence requires NavTriangleSurface; missing surface is an explicit failure.");
        }

        if (!engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig bakeConfig) ||
            bakeConfig?.RuntimeIncremental == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBake evidence requires NavMeshBakeConfig.runtimeIncremental for tile budget.");
        }

        if (telemetry.SampleCapacity != ActiveConfig.EvidenceSampleCount)
        {
            throw new InvalidOperationException(
                $"RuntimeNavMeshTelemetry sampleCapacity {telemetry.SampleCapacity} must equal " +
                $"DynamicNavBakeShowcaseConfig.evidenceSampleCount {ActiveConfig.EvidenceSampleCount}.");
        }

        return DynamicNavBakeShowcaseEvidenceCapture.Capture(
            ActiveConfig,
            queue,
            telemetry,
            triangleSurface,
            bakeConfig,
            _lastPathStatus,
            _lastPathPointCount,
            _lastCoarseNodeCount,
            WallDeployedCount,
            _squadDeployed,
            engine.LastNavBootstrapUriResolveCount,
            _pathOrchestrationState,
            ResolveRouteExecutionSink(engine),
            _squadEntities,
            bakeConfig.RuntimeIncremental.TileBudgetPerFixedTick,
            _pathXcm,
            _pathZcm);
    }

    /// <summary>
    /// Allocation-free formal player-route observation for host-frame readiness / screenshot gates.
    /// Never allocates checksum sequences; full <see cref="CaptureEvidence"/> remains cold-path only.
    /// </summary>
    public DynamicNavBakeShowcaseFormalPlayerRouteSnapshot CaptureFormalPlayerRouteSnapshot(GameEngine engine)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (!engine.TryGetService(CoreServiceKeys.RuntimeNavMeshTelemetry, out RuntimeNavMeshTelemetryService telemetry) ||
            telemetry == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBake formal player-route snapshot requires RuntimeNavMeshTelemetry.");
        }

        ulong committedGeneration = telemetry.CaptureSnapshot().LastGeneration;
        return DynamicNavBakeShowcaseEvidenceCapture.CaptureFormalPlayerRoute(
            ResolveRouteExecutionSink(engine),
            _squadEntities,
            _lastPathStatus,
            _pathXcm,
            _pathZcm,
            committedGeneration);
    }

    /// <summary>
    /// Allocation-free read-only arrival observation over pre-bound <see cref="SquadEntities"/>.
    /// Validates alive entity, OrderBuffer move state, and WorldPositionCm within authored
    /// Goal + formation slot tolerance. Never treats vanished orders alone as arrival.
    /// </summary>
    public DynamicNavBakeShowcaseSquadArrivalSnapshot CaptureSquadArrivalSnapshot(GameEngine engine)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = config.RaylibAutoTimeline
            ?? throw new InvalidOperationException(
                "CaptureSquadArrivalSnapshot requires raylibAutoTimeline.");
        int toleranceCm = timeline.FinalArrivalMemberToleranceCm;
        if (toleranceCm <= 0)
        {
            throw new InvalidOperationException(
                "CaptureSquadArrivalSnapshot requires finalArrivalMemberToleranceCm > 0.");
        }

        long toleranceSq = (long)toleranceCm * toleranceCm;
        if (!_squadDeployed)
        {
            throw new InvalidOperationException(
                "CaptureSquadArrivalSnapshot requires a deployed authored squad.");
        }

        if (_squadEntities.Length != config.Squad.Count)
        {
            throw new InvalidOperationException(
                $"CaptureSquadArrivalSnapshot squad binding mismatch. bound={_squadEntities.Length} configured={config.Squad.Count}.");
        }

        if (engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not OrderTypeRegistry registry ||
            !registry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            throw new InvalidOperationException(
                $"CaptureSquadArrivalSnapshot requires order type '{MassNavigationOrderKeys.Move}'.");
        }

        DynamicNavBakeShowcaseSquadConfig squad = config.Squad;
        int goalXCm = config.Goal.XCm;
        int goalZCm = config.Goal.YCm;
        int idleInTolerance = 0;
        int activeMoveOrders = 0;
        int outsideWithoutMove = 0;
        int firstOutsideSlot = -1;
        int firstOutsideX = 0;
        int firstOutsideZ = 0;
        int firstExpectedX = 0;
        int firstExpectedZ = 0;
        long farthestDistanceSq = -1;
        int farthestSlot = -1;
        int farthestX = 0;
        int farthestZ = 0;
        int farthestExpectedX = 0;
        int farthestExpectedZ = 0;

        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"CaptureSquadArrivalSnapshot authored squad member[{i}] is missing or dead.");
            }

            if (!engine.World.TryGet(entity, out OrderBuffer buffer))
            {
                throw new InvalidOperationException(
                    $"CaptureSquadArrivalSnapshot authored squad member[{i}] is missing OrderBuffer.");
            }

            if (!engine.World.TryGet(entity, out WorldPositionCm position))
            {
                throw new InvalidOperationException(
                    $"CaptureSquadArrivalSnapshot authored squad member[{i}] is missing WorldPositionCm.");
            }

            DynamicNavBakeShowcaseWallPool.ComputeSquadSlotOffsetCm(
                squad,
                i,
                out int offsetXCm,
                out int offsetZCm);
            int expectedXCm = checked(goalXCm + offsetXCm);
            int expectedZCm = checked(goalZCm + offsetZCm);
            WorldCmInt2 world = position.ToWorldCmInt2();
            long dx = world.X - (long)expectedXCm;
            long dz = world.Y - (long)expectedZCm;
            long distSq = (dx * dx) + (dz * dz);
            bool insideTolerance = distSq <= toleranceSq;
            if (distSq > farthestDistanceSq)
            {
                farthestDistanceSq = distSq;
                farthestSlot = i;
                farthestX = world.X;
                farthestZ = world.Y;
                farthestExpectedX = expectedXCm;
                farthestExpectedZ = expectedZCm;
            }

            if (buffer.HasQueued || buffer.HasPending)
            {
                throw new InvalidOperationException(
                    $"CaptureSquadArrivalSnapshot authored squad member[{i}] still has queued or pending orders " +
                    $"(queued={buffer.QueuedCount}, pending={buffer.HasPending}).");
            }

            if (buffer.HasActive)
            {
                if (buffer.ActiveOrder.Order.OrderTypeId != moveOrderTypeId)
                {
                    throw new InvalidOperationException(
                        $"CaptureSquadArrivalSnapshot authored squad member[{i}] has unexpected active order type " +
                        $"{buffer.ActiveOrder.Order.OrderTypeId}; expected formal move type {moveOrderTypeId} or no active order.");
                }

                activeMoveOrders++;
                continue;
            }

            // Move order is gone. Vanished orders alone never count as arrival: outside tolerance fails loudly upstream.
            if (!insideTolerance)
            {
                outsideWithoutMove++;
                if (firstOutsideSlot < 0)
                {
                    firstOutsideSlot = i;
                    firstOutsideX = world.X;
                    firstOutsideZ = world.Y;
                    firstExpectedX = expectedXCm;
                    firstExpectedZ = expectedZCm;
                }

                continue;
            }

            idleInTolerance++;
        }

        return new DynamicNavBakeShowcaseSquadArrivalSnapshot(
            squad.Count,
            idleInTolerance,
            activeMoveOrders,
            outsideWithoutMove,
            firstOutsideSlot,
            firstOutsideX,
            firstOutsideZ,
            firstExpectedX,
            firstExpectedZ,
            farthestDistanceSq,
            farthestSlot,
            farthestX,
            farthestZ,
            farthestExpectedX,
            farthestExpectedZ);
    }

    public DynamicNavBakeShowcasePanelState BuildPanelState(GameEngine engine)
    {
        _ = ActiveConfig;
        _ = RequireQueue(engine);
        _ = engine.GetService(CoreServiceKeys.RuntimeNavMeshTelemetry)
            ?? throw new InvalidOperationException(
                "DynamicNavBake showcase panel requires CoreServiceKeys.RuntimeNavMeshTelemetry.");
        string status = string.IsNullOrWhiteSpace(_lastStatus)
            ? _editTransaction.PlayerStatus
            : _lastStatus;
        return new DynamicNavBakeShowcasePanelState(
            Title: "Dynamic NavBake",
            Status: status,
            ConstructionMode: _constructionMode,
            NavMeshVisible: engine.GetService(CoreServiceKeys.NavMeshPresentationState)?.Enabled == true);
    }

    private static string ResolveMapLabel(DynamicNavBakeShowcaseConfig config)
        => config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld
            ? "Open World"
            : "RTS Fortress";

    private string ResolveActiveHotspotLabel(DynamicNavBakeShowcaseConfig config)
    {
        if (config.ResolvedSceneKind != DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            return "Central Gate";
        }

        DynamicNavBakeShowcaseOpenWorldConfig openWorld = config.OpenWorld
            ?? throw new InvalidOperationException("Open-world config is required for hotspot label.");
        if ((uint)_openWorldHotspotIndex >= (uint)openWorld.Hotspots.Length)
        {
            throw new InvalidOperationException(
                $"Open-world hotspot index {_openWorldHotspotIndex} is out of range for {openWorld.Hotspots.Length} hotspots.");
        }

        return openWorld.Hotspots[_openWorldHotspotIndex].Label;
    }

    private bool TryFocusHotspot(GameEngine engine, int hotspotIndex, out string error)
    {
        error = string.Empty;
        DynamicNavBakeShowcaseOpenWorldConfig openWorld = ActiveConfig.OpenWorld
            ?? throw new InvalidOperationException("Open-world config is required.");
        DynamicNavBakeShowcaseHotspotConfig hotspot = openWorld.Hotspots[hotspotIndex];
        _openWorldHotspotIndex = hotspotIndex;
        ActivateShowcaseCamera(engine, new Vector2(hotspot.CameraTargetXCm, hotspot.CameraTargetYCm));
        if (!TrySlideResidentWindow(
                engine,
                hotspot.ResidentOriginChunkX,
                hotspot.ResidentOriginChunkZ,
                out error))
        {
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        _lastStatus = $"Focused hotspot '{hotspot.Label}'.";
        ClearStalePath(engine, "Hotspot changed; resident nav window moved.");
        RefreshPanel(engine);
        return true;
    }

    private void BootstrapScenario(GameEngine engine, DynamicNavBakeShowcaseConfig config)
    {
        NavTriangleSurfaceTileIndex triangleSurface = engine.GetService(CoreServiceKeys.NavTriangleSurface)
            ?? throw new InvalidOperationException(
                "DynamicNavBake showcase requires CoreServiceKeys.NavTriangleSurface before coarse graph or resident window setup.");
        NavTriangleSurfaceTileGrid surfaceGrid = triangleSurface.Grid;

        if (config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            NodeGraphBoard board = _nodeGraphBoard
                ?? throw new InvalidOperationException("Open-world showcase requires a NodeGraph primary board.");
            DynamicNavBakeShowcaseCoarseGraphBootstrap.ValidateConfigMatchesGrid(config, surfaceGrid);
            _coarseGraph = DynamicNavBakeShowcaseCoarseGraphBootstrap.BuildAndInstall(board, config, surfaceGrid);
            _openWorldHotspotIndex = config.OpenWorld!.InitialHotspotIndex;
            DynamicNavBakeShowcaseHotspotConfig hotspot = config.OpenWorld.Hotspots[_openWorldHotspotIndex];
            if (!TrySlideResidentWindow(engine, hotspot.ResidentOriginChunkX, hotspot.ResidentOriginChunkZ, out string slideError))
            {
                throw new InvalidOperationException(slideError);
            }
        }
        else
        {
            // RTS still requires the authored triangle surface grid to match centered board extents.
            DynamicNavBakeShowcaseCoarseGraphBootstrap.ValidateConfigMatchesGrid(config, surfaceGrid);
        }

        FocusMassNavigationRuntimeForInitialSquad(engine, config);

        _wallPool = new DynamicNavBakeShowcaseWallPool(config.WallPoolCapacity);
        int spawnCount = DynamicNavBakeShowcaseWallPool.BuildSpawnRequestCount(config);
        EnsureSpawnScratch(spawnCount);
        MapId mapId = RequireCurrentMapId(engine, config.MapId);
        ValidateTemplates(engine, config);
        int written = DynamicNavBakeShowcaseWallPool.WriteSpawnRequests(engine, config, mapId, _spawnScratch, _wallPool);
        if (written != spawnCount)
        {
            throw new InvalidOperationException($"DynamicNavBake showcase wrote {written} spawn requests, expected {spawnCount}.");
        }

        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("DynamicNavBake showcase requires RuntimeEntitySpawnQueue.");
        if (spawnQueue.FreeCapacity < spawnCount)
        {
            throw new InvalidOperationException(
                $"DynamicNavBake showcase requires RuntimeEntitySpawnQueue free capacity {spawnCount}, actual {spawnQueue.FreeCapacity}.");
        }

        int enqueued = spawnQueue.EnqueueMany(_spawnScratch.AsSpan(0, spawnCount));
        if (enqueued != spawnCount)
        {
            throw new InvalidOperationException($"DynamicNavBake showcase enqueued {enqueued} requests, expected {spawnCount}.");
        }

        _scenarioSpawned = true;
        _entitiesBound = false;
        _lastStatus = "Scenario authored. Deploy the squad to begin.";
    }

    private static void FocusMassNavigationRuntimeForInitialSquad(
        GameEngine engine,
        DynamicNavBakeShowcaseConfig config)
    {
        MassNavigationRuntimeBinding binding = engine.GetService(MassNavigationKeys.RuntimeBinding)
            ?? throw new InvalidOperationException(
                "DynamicNavBake showcase requires MassNavigationRuntimeBinding before squad spawn.");
        if (binding.Current == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBake showcase requires an active MassNavigation runtime before squad spawn.");
        }

        if (!string.Equals(binding.CurrentMapId.Value, config.MapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"DynamicNavBake showcase requires MassNavigation runtime for map '{config.MapId}', got '{binding.CurrentMapId.Value}'.");
        }

        binding.Current.FocusSimulationWindow(new Vector2(config.Squad.CenterXCm, config.Squad.CenterYCm));
    }

    private void EnsureEntitiesBound(GameEngine engine)
    {
        if (_entitiesBound)
        {
            return;
        }

        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("DynamicNavBake showcase requires RuntimeEntitySpawnQueue.");
        if (spawnQueue.Count > 0)
        {
            return;
        }

        BindSpawnedEntities(engine, config);
        _entitiesBound = true;
        if (!_squadDeployed)
        {
            ApplyInitialCommandSource(engine);
            _squadDeployed = true;
            _lastStatus = "单位已就绪。左键框选，右键移动，或点击建造建筑。";
        }

        // Formal box/click acquisition requires LiveVisible knowledge for the local player.
        // Open-world also refreshes this every FixedStep; RTS needs the same disclosure or selection stays empty.
        RefreshSquadCommandKnowledge(engine);
        if (config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            // MapLoaded often arrives before spawn drain; enable minimap once the open-world focus is actually playable.
            EnableOpenWorldMinimap(engine);
        }
    }

    private void BindSpawnedEntities(GameEngine engine, DynamicNavBakeShowcaseConfig config)
    {
        DynamicNavBakeShowcaseWallPool pool = RequireWallPool();
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("DynamicNavBake showcase requires EntityTemplateKeyRegistry.");
        if (!templateKeys.TryGetId(config.Gate.WallTemplateId, out int wallTemplateKeyId) || wallTemplateKeyId <= 0)
        {
            throw new InvalidOperationException($"DynamicNavBake showcase template '{config.Gate.WallTemplateId}' is not registered.");
        }

        if (!templateKeys.TryGetId(config.Squad.TemplateId, out int squadTemplateKeyId) || squadTemplateKeyId <= 0)
        {
            throw new InvalidOperationException($"DynamicNavBake showcase template '{config.Squad.TemplateId}' is not registered.");
        }

        var walls = new List<Entity>(pool.Capacity);
        var squad = new List<Entity>(config.Squad.Count);
        MapId expectedMap = new MapId(config.MapId);
        var query = new QueryDescription().WithAll<MapEntity, EntityTemplateKeyRef, WorldPositionCm>();
        engine.World.Query(in query, (Entity entity, ref MapEntity mapEntity, ref EntityTemplateKeyRef template, ref WorldPositionCm position) =>
        {
            if (mapEntity.MapId != expectedMap)
            {
                return;
            }

            if (template.TemplateKeyId == wallTemplateKeyId)
            {
                walls.Add(entity);
                return;
            }

            if (template.TemplateKeyId == squadTemplateKeyId)
            {
                squad.Add(entity);
            }
        });

        if (walls.Count != pool.Capacity)
        {
            throw new InvalidOperationException(
                $"DynamicNavBake showcase found {walls.Count} wall pool entities, expected {pool.Capacity}.");
        }

        if (squad.Count != config.Squad.Count)
        {
            throw new InvalidOperationException(
                $"DynamicNavBake showcase found {squad.Count} squad agents, expected {config.Squad.Count}.");
        }

        walls.Sort((left, right) => left.Id.CompareTo(right.Id));
        pool.ClearBindings();
        for (int i = 0; i < walls.Count; i++)
        {
            if (!engine.World.IsAlive(walls[i]))
            {
                throw new InvalidOperationException($"Wall pool candidate entity {walls[i].Id} is not alive.");
            }

            pool.BindSpawnedEntity(i, walls[i]);
        }

        squad.Sort((left, right) => left.Id.CompareTo(right.Id));
        for (int i = 0; i < squad.Count; i++)
        {
            if (!engine.World.IsAlive(squad[i]))
            {
                throw new InvalidOperationException($"Squad candidate entity {squad[i].Id} is not alive.");
            }
        }

        _squadEntities = squad.ToArray();
        AssertSquadControllableByLocalPlayer(engine);
    }

    private void AssertSquadControllableByLocalPlayer(GameEngine engine)
    {
        Ludots.Core.Gameplay.Teams.PlayerEntityLookup players =
            engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException(
                "DynamicNavBake squad binding requires PlayerEntityLookup before right-click authorization can be validated.");
        int playerId = 1;
        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? playerIdObj) &&
            playerIdObj is int configuredPlayerId &&
            configuredPlayerId > 0)
        {
            playerId = configuredPlayerId;
        }

        if (!players.TryGet(playerId, out Entity controllerRep) ||
            controllerRep == Entity.Null ||
            !engine.World.IsAlive(controllerRep))
        {
            throw new InvalidOperationException(
                $"DynamicNavBake squad binding requires a live PlayerEntityLookup representative for player {playerId}.");
        }

        // Keep the host LocalPlayer* services aligned with the relationship representative used by Command auth.
        engine.SetService(CoreServiceKeys.LocalPlayerEntity, controllerRep);
        engine.SetService(CoreServiceKeys.LocalPlayerId, playerId);

        Ludots.Core.Gameplay.Relationships.ControlDomainQuery controlDomains =
            engine.GetService(CoreServiceKeys.ControlDomainQuery)
            ?? throw new InvalidOperationException(
                "DynamicNavBake squad binding requires ControlDomainQuery before right-click authorization can be validated.");

        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (!controlDomains.IsControllableBy(controllerRep, entity))
            {
                throw new InvalidOperationException(
                    $"DynamicNavBake squad member[{i}] entity {entity.Id} is not controllable by local player rep {controllerRep.Id}. " +
                    "Formal right-click Command requires an Owns edge from the local player representative.");
            }
        }
    }

    private void RecomputePath(GameEngine engine)
    {
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        if (queue.Status != RuntimeNavMeshRebuildStatus.Idle || queue.HasResidentWindowTransition)
        {
            _lastPathStatus = NavPathStatus.NotReady;
            _lastPathPointCount = 0;
            _pathOrchestrationState = DynamicNavBakePathOrchestrationState.WindowRebuilding;
            UpdatePathBuffers(Array.Empty<int>(), Array.Empty<int>());
            return;
        }

        ResolveSquadWorldCm(engine, out int startX, out int startZ);
        int goalX = config.Goal.XCm;
        int goalZ = config.Goal.YCm;

        if (config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            RecomputeOpenWorldPath(engine, queue, startX, startZ, goalX, goalZ);
            return;
        }

        _lastCoarseNodeCount = 0;
        ClearCorridorPresentation();
        NavQueryService navQuery = RequireNavQuery(engine);
        NavPathResult local = navQuery.TryFindPath(startX, startZ, goalX, goalZ);
        ApplyLocalPathResult(engine, queue, local, localSegmentGoalX: goalX, localSegmentGoalZ: goalZ);
    }

    private void RecomputeOpenWorldPath(
        GameEngine engine,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        int startX,
        int startZ,
        int goalX,
        int goalZ)
    {
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        if (_coarseGraph == null)
        {
            throw new InvalidOperationException("Open-world coarse graph is not initialized.");
        }

        int startNode = DynamicNavBakeShowcaseCoarseGraphBootstrap.FindNearestNodeId(_coarseGraph, startX, startZ);
        int goalNode = DynamicNavBakeShowcaseCoarseGraphBootstrap.FindNearestNodeId(_coarseGraph, goalX, goalZ);
        EnsureCoarseScratch(_coarseGraph.NodeCount);
        EnsureCorridorScratch(_coarseGraph.NodeCount);
        var scratch = new NodeGraphPathScratch();
        var policy = new DefaultTraversalPolicy();
        GraphPathResult coarse = NodeGraphPathService.FindPathAStar(
            _coarseGraph.FullView.Graph,
            startNode,
            goalNode,
            _coarseNodePath,
            ref scratch,
            ref policy,
            maxExpanded: _coarseGraph.NodeCount);
        if (coarse.Status != GraphPathStatus.Success)
        {
            _pathOrchestrationState = DynamicNavBakePathOrchestrationState.Unreachable;
            _lastPathStatus = NavPathStatus.NotReachable;
            _lastPathPointCount = 0;
            _lastCoarseNodeCount = 0;
            UpdatePathBuffers(Array.Empty<int>(), Array.Empty<int>());
            ClearCorridorPresentation();
            return;
        }

        _lastCoarseNodeCount = coarse.NodeCount;
        NodeGraph graph = _coarseGraph.FullView.Graph;
        for (int i = 0; i < _lastCoarseNodeCount; i++)
        {
            int nodeId = _coarseNodePath[i];
            _coarseCorridorWorldPoints[i] = (graph.PosXcm[nodeId], graph.PosYcm[nodeId]);
        }

        _presentationCorridorRevision++;
        _pathOrchestrationState = DynamicNavBakePathOrchestrationState.GlobalCorridorReady;
        _corridorCursor = Math.Clamp(_corridorCursor, 0, Math.Max(0, _lastCoarseNodeCount - 1));
        ReadOnlySpan<(int XCm, int ZCm)> corridorPoints = _coarseCorridorWorldPoints.AsSpan(0, _lastCoarseNodeCount);

        if (!TryEnsureResidentWindowCoversPoint(engine, startX, startZ, out string slideError))
        {
            throw new InvalidOperationException(slideError);
        }

        queue = RequireQueue(engine);
        if (queue.Status != RuntimeNavMeshRebuildStatus.Idle || queue.HasResidentWindowTransition)
        {
            _pathOrchestrationState = DynamicNavBakePathOrchestrationState.WindowRebuilding;
            _lastPathStatus = NavPathStatus.NotReady;
            _lastPathPointCount = 0;
            UpdatePathBuffers(Array.Empty<int>(), Array.Empty<int>());
            return;
        }

        ResolveCommittedResidentBounds(engine, out int minX, out int minZ, out int maxX, out int maxZ);
        if (!IsPointInsideInclusive(startX, startZ, minX, minZ, maxX, maxZ))
        {
            throw new InvalidOperationException(
                $"Open-world local segment start ({startX},{startZ}) is outside the committed resident window " +
                $"[{minX},{minZ}]-[{maxX},{maxZ}].");
        }

        ResolveFormationCenterBounds(config.Squad, minX, minZ, maxX, maxZ,
            out int goalMinX, out int goalMinZ, out int goalMaxX, out int goalMaxZ);
        int localGoalIndex = FindFarthestCorridorIndexInsideWindow(
            corridorPoints,
            _corridorCursor,
            goalMinX,
            goalMinZ,
            goalMaxX,
            goalMaxZ);
        if (localGoalIndex < 0)
        {
            // Cursor checkpoint is outside the committed window: slide so live squad AABB + checkpoint formation remain inside.
            (int checkpointX, int checkpointZ) = corridorPoints[_corridorCursor];
            if (!TryEnsureResidentWindowCoversSquadAndCheckpoint(
                    engine,
                    checkpointX,
                    checkpointZ,
                    out string coverError))
            {
                throw new InvalidOperationException(
                    $"Open-world corridor checkpoint ({checkpointX},{checkpointZ}) at cursor {_corridorCursor} " +
                    $"is outside the committed resident window and the required squad+checkpoint slide could not be requested: {coverError}");
            }

            queue = RequireQueue(engine);
            if (queue.Status != RuntimeNavMeshRebuildStatus.Idle || queue.HasResidentWindowTransition)
            {
                _pathOrchestrationState = DynamicNavBakePathOrchestrationState.WindowRebuilding;
                _lastPathStatus = NavPathStatus.NotReady;
                _lastPathPointCount = 0;
                UpdatePathBuffers(Array.Empty<int>(), Array.Empty<int>());
                return;
            }

            ResolveCommittedResidentBounds(engine, out minX, out minZ, out maxX, out maxZ);
            ResolveFormationCenterBounds(config.Squad, minX, minZ, maxX, maxZ,
                out goalMinX, out goalMinZ, out goalMaxX, out goalMaxZ);
            localGoalIndex = FindFarthestCorridorIndexInsideWindow(
                corridorPoints,
                _corridorCursor,
                goalMinX,
                goalMinZ,
                goalMaxX,
                goalMaxZ);
            if (localGoalIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Open-world corridor checkpoint ({checkpointX},{checkpointZ}) at cursor {_corridorCursor} " +
                    $"remains outside the committed resident window [{minX},{minZ}]-[{maxX},{maxZ}] after slide request.");
            }
        }

        int localGoalX = corridorPoints[localGoalIndex].XCm;
        int localGoalZ = corridorPoints[localGoalIndex].ZCm;
        if (localGoalIndex >= _lastCoarseNodeCount - 1)
        {
            // Final corridor node: aim at the authored world goal when it still lies inside the window.
            if (IsPointInsideInclusive(goalX, goalZ, goalMinX, goalMinZ, goalMaxX, goalMaxZ))
            {
                localGoalX = goalX;
                localGoalZ = goalZ;
            }
        }

        if (!IsPointInsideInclusive(localGoalX, localGoalZ, goalMinX, goalMinZ, goalMaxX, goalMaxZ))
        {
            throw new InvalidOperationException(
                $"Open-world local segment goal ({localGoalX},{localGoalZ}) cannot keep every authored formation slot " +
                $"inside committed resident window [{minX},{minZ}]-[{maxX},{maxZ}].");
        }

        NavQueryService navQuery = RequireNavQuery(engine);
        NavPathResult local = navQuery.TryFindPath(startX, startZ, localGoalX, localGoalZ);
        ApplyLocalPathResult(engine, queue, local, localGoalX, localGoalZ);
        if (_lastPathStatus == NavPathStatus.Ok)
        {
            _corridorCursor = localGoalIndex;
        }
    }

    private void ApplyLocalPathResult(
        GameEngine engine,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        in NavPathResult local,
        int localSegmentGoalX,
        int localSegmentGoalZ)
    {
        _lastPathStatus = local.Status;
        _lastPathPointCount = local.PathXcm.Length;
        UpdatePathBuffers(local.PathXcm, local.PathZcm);
        _localSegmentGoalXCm = localSegmentGoalX;
        _localSegmentGoalZCm = localSegmentGoalZ;
        _lastPathGeneration = queue.Status == RuntimeNavMeshRebuildStatus.Idle
            ? ReadLatestGeneration(engine)
            : _lastPathGeneration;
        _pathOrchestrationState = local.Status switch
        {
            NavPathStatus.Ok => DynamicNavBakePathOrchestrationState.LocalSegmentReady,
            NavPathStatus.NotReachable => DynamicNavBakePathOrchestrationState.Unreachable,
            _ => DynamicNavBakePathOrchestrationState.WindowRebuilding
        };
    }

    private bool TryEnsureResidentWindowCoversPoint(
        GameEngine engine,
        int worldX,
        int worldZ,
        out string error)
        => TryEnsureResidentWindowCoversPoints(engine, worldX, worldZ, worldX, worldZ, out error);

    /// <summary>
    /// Slides the fixed-size resident window so both world points stay queryable after commit.
    /// Each point is padded once by authored formation half-extents, then covered as a bounds AABB
    /// (never pad actual member positions a second time — use
    /// <see cref="TryEnsureResidentWindowCoversSquadAndCheckpoint"/> for live-squad coverage).
    /// </summary>
    private bool TryEnsureResidentWindowCoversPoints(
        GameEngine engine,
        int worldAX,
        int worldAZ,
        int worldBX,
        int worldBZ,
        out string error)
    {
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        DynamicNavBakeShowcaseWallPool.ComputeSquadHalfExtentsCm(
            config.Squad,
            out int halfWidthCm,
            out int halfDepthCm);
        int requiredMinX = checked(Math.Min(worldAX, worldBX) - halfWidthCm);
        int requiredMinZ = checked(Math.Min(worldAZ, worldBZ) - halfDepthCm);
        int requiredMaxX = checked(Math.Max(worldAX, worldBX) + halfWidthCm);
        int requiredMaxZ = checked(Math.Max(worldAZ, worldBZ) + halfDepthCm);
        return TryEnsureResidentWindowCoversBounds(
            engine,
            requiredMinX,
            requiredMinZ,
            requiredMaxX,
            requiredMaxZ,
            out error);
    }

    /// <summary>
    /// Slides residency so the union of (1) every live squad member's actual WorldPositionCm AABB and
    /// (2) the next checkpoint's authored formation AABB stays inside the committed window.
    /// Actual positions are not padded again — only the checkpoint center uses half-extents.
    /// </summary>
    private bool TryEnsureResidentWindowCoversSquadAndCheckpoint(
        GameEngine engine,
        int checkpointXCm,
        int checkpointZCm,
        out string error)
    {
        ResolveLiveSquadWorldAabb(engine, out int squadMinX, out int squadMinZ, out int squadMaxX, out int squadMaxZ);
        DynamicNavBakeShowcaseWallPool.ComputeSquadHalfExtentsCm(
            ActiveConfig.Squad,
            out int halfWidthCm,
            out int halfDepthCm);
        int checkpointMinX = checked(checkpointXCm - halfWidthCm);
        int checkpointMinZ = checked(checkpointZCm - halfDepthCm);
        int checkpointMaxX = checked(checkpointXCm + halfWidthCm);
        int checkpointMaxZ = checked(checkpointZCm + halfDepthCm);
        int requiredMinX = Math.Min(squadMinX, checkpointMinX);
        int requiredMinZ = Math.Min(squadMinZ, checkpointMinZ);
        int requiredMaxX = Math.Max(squadMaxX, checkpointMaxX);
        int requiredMaxZ = Math.Max(squadMaxZ, checkpointMaxZ);
        return TryEnsureResidentWindowCoversBounds(
            engine,
            requiredMinX,
            requiredMinZ,
            requiredMaxX,
            requiredMaxZ,
            out error);
    }

    /// <summary>
    /// Slides the fixed-size resident window so an already-resolved world AABB stays queryable after commit.
    /// Centering on a forward corridor checkpoint alone can eject trailing members behind the west edge.
    /// </summary>
    private bool TryEnsureResidentWindowCoversBounds(
        GameEngine engine,
        int requiredMinX,
        int requiredMinZ,
        int requiredMaxX,
        int requiredMaxZ,
        out string error)
    {
        error = string.Empty;
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        ResolveCommittedResidentBounds(engine, out int minX, out int minZ, out int maxX, out int maxZ);
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        if (requiredMinX >= minX && requiredMinZ >= minZ &&
            requiredMaxX < maxX && requiredMaxZ < maxZ &&
            queue.CommittedResidentWindowCount > 0 &&
            !queue.HasResidentWindowTransition)
        {
            return true;
        }

        NavTriangleSurfaceTileGrid grid = RequireTriangleSurfaceGrid(engine);
        if (!TryComputeResidentOriginCoveringWorldPoints(
                grid,
                requiredMinX,
                requiredMinZ,
                requiredMaxX,
                requiredMaxZ,
                config.ResidentWidthChunks,
                config.ResidentHeightChunks,
                out int chunkX,
                out int chunkZ,
                out error))
        {
            return false;
        }

        return TrySlideResidentWindow(engine, chunkX, chunkZ, out error);
    }

    private static bool TryComputeResidentOriginCoveringWorldPoints(
        in NavTriangleSurfaceTileGrid grid,
        int worldAX,
        int worldAZ,
        int worldBX,
        int worldBZ,
        int residentWidthChunks,
        int residentHeightChunks,
        out int originChunkX,
        out int originChunkZ,
        out string error)
    {
        originChunkX = 0;
        originChunkZ = 0;
        error = string.Empty;
        if (residentWidthChunks <= 0 || residentHeightChunks <= 0)
        {
            error = "Resident window dimensions must be positive.";
            return false;
        }

        if (residentWidthChunks > grid.TileCountX || residentHeightChunks > grid.TileCountZ)
        {
            error =
                $"Resident window {residentWidthChunks}x{residentHeightChunks} exceeds grid " +
                $"{grid.TileCountX}x{grid.TileCountZ}.";
            return false;
        }

        int chunkAX = MathUtil.FloorDiv(checked(worldAX - grid.OriginXcm), grid.TileWidthCm);
        int chunkAZ = MathUtil.FloorDiv(checked(worldAZ - grid.OriginZcm), grid.TileHeightCm);
        int chunkBX = MathUtil.FloorDiv(checked(worldBX - grid.OriginXcm), grid.TileWidthCm);
        int chunkBZ = MathUtil.FloorDiv(checked(worldBZ - grid.OriginZcm), grid.TileHeightCm);
        int minChunkX = Math.Min(chunkAX, chunkBX);
        int maxChunkX = Math.Max(chunkAX, chunkBX);
        int minChunkZ = Math.Min(chunkAZ, chunkBZ);
        int maxChunkZ = Math.Max(chunkAZ, chunkBZ);
        int spanX = checked(maxChunkX - minChunkX + 1);
        int spanZ = checked(maxChunkZ - minChunkZ + 1);
        if (spanX > residentWidthChunks || spanZ > residentHeightChunks)
        {
            error =
                $"Open-world resident window {residentWidthChunks}x{residentHeightChunks} cannot cover both " +
                $"required points ({worldAX},{worldAZ}) and ({worldBX},{worldBZ}) spanning {spanX}x{spanZ} chunks.";
            return false;
        }

        // Pack toward the trailing (min) edge so the live squad stays inside while leaving room ahead.
        originChunkX = Math.Clamp(minChunkX, 0, grid.TileCountX - residentWidthChunks);
        originChunkZ = Math.Clamp(minChunkZ, 0, grid.TileCountZ - residentHeightChunks);
        if (originChunkX + residentWidthChunks - 1 < maxChunkX)
        {
            originChunkX = Math.Clamp(maxChunkX + 1 - residentWidthChunks, 0, grid.TileCountX - residentWidthChunks);
        }

        if (originChunkZ + residentHeightChunks - 1 < maxChunkZ)
        {
            originChunkZ = Math.Clamp(maxChunkZ + 1 - residentHeightChunks, 0, grid.TileCountZ - residentHeightChunks);
        }

        return true;
    }

    private void ResolveCommittedResidentBounds(
        GameEngine engine,
        out int minX,
        out int minZ,
        out int maxX,
        out int maxZ)
    {
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        NavTriangleSurfaceTileGrid grid = RequireTriangleSurfaceGrid(engine);
        int committedAdvertised = queue.CommittedResidentWindowCount;
        if (committedAdvertised > 0)
        {
            EnsureResidentScratch(committedAdvertised);
            int committedCount = queue.CopyCommittedResidentWindow(_residentScratch);
            if (committedCount != committedAdvertised)
            {
                throw new InvalidOperationException(
                    $"Committed resident window advertised {committedAdvertised} tiles but copied {committedCount}.");
            }

            if (committedCount <= 0)
            {
                throw new InvalidOperationException(
                    "Committed resident window advertised a nonempty state but copied zero entries.");
            }

            DynamicNavBakeShowcaseCoarseGraphBootstrap.ResolveWindowWorldBounds(
                grid,
                _residentScratch.AsSpan(0, committedCount),
                out minX,
                out minZ,
                out maxX,
                out maxZ);
            return;
        }

        // Bootstrap / pre-commit: use the explicitly requested resident window, not a silent substitute.
        int requestedAdvertised = queue.ResidentWindowCount;
        if (requestedAdvertised <= 0)
        {
            minX = 0;
            minZ = 0;
            maxX = -1;
            maxZ = -1;
            return;
        }

        EnsureResidentScratch(requestedAdvertised);
        int requestedCount = queue.CopyResidentWindow(_residentScratch);
        if (requestedCount != requestedAdvertised)
        {
            throw new InvalidOperationException(
                $"Requested resident window advertised {requestedAdvertised} tiles but copied {requestedCount}.");
        }

        DynamicNavBakeShowcaseCoarseGraphBootstrap.ResolveWindowWorldBounds(
            grid,
            _residentScratch.AsSpan(0, requestedCount),
            out minX,
            out minZ,
            out maxX,
            out maxZ);
    }

    /// <summary>
    /// Returns the farthest corridor index at or after <paramref name="startIndex"/> that still lies
    /// inside the committed resident window, or -1 when <paramref name="startIndex"/> itself is outside.
    /// Never returns an outside index (callers must slide the resident window first).
    /// </summary>
    private static int FindFarthestCorridorIndexInsideWindow(
        ReadOnlySpan<(int XCm, int ZCm)> corridorPoints,
        int startIndex,
        int minX,
        int minZ,
        int maxX,
        int maxZ)
    {
        if ((uint)startIndex >= (uint)corridorPoints.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startIndex),
                $"Corridor start index {startIndex} is out of range for {corridorPoints.Length} corridor points.");
        }

        int best = -1;
        for (int i = startIndex; i < corridorPoints.Length; i++)
        {
            if (!IsPointInsideInclusive(corridorPoints[i].XCm, corridorPoints[i].ZCm, minX, minZ, maxX, maxZ))
            {
                break;
            }

            best = i;
        }

        return best;
    }

    private static bool IsPointInsideInclusive(int x, int z, int minX, int minZ, int maxX, int maxZ)
        => x >= minX && x <= maxX && z >= minZ && z <= maxZ;

    /// <summary>
    /// Committed resident world bounds use an exclusive max edge (tile end). Membership is half-open.
    /// </summary>
    private static bool IsPointInsideResidentWindow(int x, int z, int minX, int minZ, int maxX, int maxZ)
        => x >= minX && x < maxX && z >= minZ && z < maxZ;

    private static void ResolveFormationCenterBounds(
        DynamicNavBakeShowcaseSquadConfig squad,
        int residentMinX,
        int residentMinZ,
        int residentMaxX,
        int residentMaxZ,
        out int minX,
        out int minZ,
        out int maxX,
        out int maxZ)
    {
        DynamicNavBakeShowcaseWallPool.ComputeSquadHalfExtentsCm(
            squad,
            out int halfWidthCm,
            out int halfDepthCm);
        minX = checked(residentMinX + halfWidthCm);
        minZ = checked(residentMinZ + halfDepthCm);
        maxX = checked(residentMaxX - halfWidthCm - 1);
        maxZ = checked(residentMaxZ - halfDepthCm - 1);
        if (minX > maxX || minZ > maxZ)
        {
            throw new InvalidOperationException(
                $"Committed resident window [{residentMinX},{residentMinZ}]-[{residentMaxX},{residentMaxZ}] " +
                $"is too small for authored squad half-extents ({halfWidthCm},{halfDepthCm})cm.");
        }
    }

    /// <summary>
    /// Per-member open-world slot arrival tolerance derived from authored formation spacing.
    /// </summary>
    private static int ResolveOpenWorldSlotArrivalToleranceCm(DynamicNavBakeShowcaseConfig config)
    {
        DynamicNavBakeShowcaseSquadConfig squad = config.Squad;
        int toleranceCm = Math.Max(squad.SpacingXCm, squad.SpacingYCm);
        if (toleranceCm <= 0)
        {
            throw new InvalidOperationException(
                "Open-world slot arrival tolerance requires positive authored squad spacing (max of SpacingXCm/SpacingYCm).");
        }

        return toleranceCm;
    }

    private void ResolveSquadWorldCm(GameEngine engine, out int xCm, out int zCm)
    {
        if (_squadEntities.Length <= 0)
        {
            throw new InvalidOperationException(
                "ResolveSquadWorldCm requires a bound authored squad.");
        }

        long sumX = 0L;
        long sumZ = 0L;
        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"ResolveSquadWorldCm authored squad member[{i}] is missing or dead.");
            }

            if (!engine.World.TryGet(entity, out WorldPositionCm position))
            {
                throw new InvalidOperationException(
                    $"ResolveSquadWorldCm authored squad member[{i}] is missing WorldPositionCm.");
            }

            WorldCmInt2 world = position.ToWorldCmInt2();
            sumX += world.X;
            sumZ += world.Y;
        }

        int count = _squadEntities.Length;
        xCm = (int)(sumX / count);
        zCm = (int)(sumZ / count);
    }

    private void ResolveLiveSquadWorldAabb(
        GameEngine engine,
        out int minX,
        out int minZ,
        out int maxX,
        out int maxZ)
    {
        if (_squadEntities.Length <= 0)
        {
            throw new InvalidOperationException(
                "ResolveLiveSquadWorldAabb requires a bound authored squad.");
        }

        minX = int.MaxValue;
        minZ = int.MaxValue;
        maxX = int.MinValue;
        maxZ = int.MinValue;
        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"ResolveLiveSquadWorldAabb authored squad member[{i}] is missing or dead.");
            }

            if (!engine.World.TryGet(entity, out WorldPositionCm position))
            {
                throw new InvalidOperationException(
                    $"ResolveLiveSquadWorldAabb authored squad member[{i}] is missing WorldPositionCm.");
            }

            WorldCmInt2 world = position.ToWorldCmInt2();
            if (world.X < minX)
            {
                minX = world.X;
            }

            if (world.Y < minZ)
            {
                minZ = world.Y;
            }

            if (world.X > maxX)
            {
                maxX = world.X;
            }

            if (world.Y > maxZ)
            {
                maxZ = world.Y;
            }
        }
    }

    private bool AreAllSquadMembersWithinFormationSlots(
        GameEngine engine,
        DynamicNavBakeShowcaseSquadConfig squad,
        int centerXCm,
        int centerZCm,
        int toleranceCm)
    {
        if (_squadEntities.Length <= 0)
        {
            throw new InvalidOperationException(
                "AreAllSquadMembersWithinFormationSlots requires a bound authored squad.");
        }

        if (toleranceCm <= 0)
        {
            throw new InvalidOperationException(
                "AreAllSquadMembersWithinFormationSlots requires a positive slot arrival tolerance.");
        }

        long toleranceSq = (long)toleranceCm * toleranceCm;
        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"AreAllSquadMembersWithinFormationSlots authored squad member[{i}] is missing or dead.");
            }

            if (!engine.World.TryGet(entity, out WorldPositionCm position))
            {
                throw new InvalidOperationException(
                    $"AreAllSquadMembersWithinFormationSlots authored squad member[{i}] is missing WorldPositionCm.");
            }

            DynamicNavBakeShowcaseWallPool.ComputeSquadSlotOffsetCm(
                squad,
                i,
                out int offsetXCm,
                out int offsetZCm);
            int expectedXCm = checked(centerXCm + offsetXCm);
            int expectedZCm = checked(centerZCm + offsetZCm);
            WorldCmInt2 world = position.ToWorldCmInt2();
            long dx = world.X - (long)expectedXCm;
            long dz = world.Y - (long)expectedZCm;
            if ((dx * dx) + (dz * dz) > toleranceSq)
            {
                return false;
            }
        }

        return true;
    }

    private bool AreAllSquadMembersWithinLocalSegmentSlots(GameEngine engine, DynamicNavBakeShowcaseConfig config)
        => AreAllSquadMembersWithinFormationSlots(
            engine,
            config.Squad,
            _localSegmentGoalXCm,
            _localSegmentGoalZCm,
            ResolveOpenWorldSlotArrivalToleranceCm(config));

    private bool AreAllSquadMembersWithinWorldGoalSlots(GameEngine engine, DynamicNavBakeShowcaseConfig config)
        => AreAllSquadMembersWithinFormationSlots(
            engine,
            config.Squad,
            config.Goal.XCm,
            config.Goal.YCm,
            ResolveOpenWorldSlotArrivalToleranceCm(config));

    private void SlideResidentWindowForCorridor(GameEngine engine, IReadOnlyList<int> corridorNodeIds)
    {
        _ = engine;
        _ = corridorNodeIds;
        // Retained no-op: open-world orchestration now slides windows only to cover the live squad
        // and the next in-window corridor checkpoint. Mid-corridor slides that leave both endpoints
        // outside the resident window are intentionally removed.
    }

    private bool TrySlideResidentWindow(GameEngine engine, int originChunkX, int originChunkZ, out string error)
    {
        error = string.Empty;
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        int tileCount = checked(config.ResidentWidthChunks * config.ResidentHeightChunks);
        EnsureResidentScratch(tileCount);
        int index = 0;
        for (int dz = 0; dz < config.ResidentHeightChunks; dz++)
        {
            for (int dx = 0; dx < config.ResidentWidthChunks; dx++)
            {
                _residentScratch[index++] = new NavBakeTileCoord(originChunkX + dx, originChunkZ + dz);
            }
        }

        try
        {
            queue.RequestResidentWindowTransition(_residentScratch.AsSpan(0, tileCount));
            _pathOrchestrationState = DynamicNavBakePathOrchestrationState.WindowRebuilding;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void AdvanceOpenWorldMoveIfNeeded(GameEngine engine)
    {
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        if (!_moveCommandActive || config.ResolvedSceneKind != DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            return;
        }

        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        if (queue.Status != RuntimeNavMeshRebuildStatus.Idle || queue.HasResidentWindowTransition)
        {
            _pathOrchestrationState = DynamicNavBakePathOrchestrationState.WindowRebuilding;
            return;
        }

        // After a resident-window slide for the next corridor checkpoint: recompute from the live
        // squad position without incrementing the cursor again, then submit the next local march.
        if (_pathOrchestrationState == DynamicNavBakePathOrchestrationState.WindowRebuilding ||
            _lastPathStatus == NavPathStatus.NotReady)
        {
            RecomputePath(engine);
            if (_lastPathStatus == NavPathStatus.Ok &&
                _pathOrchestrationState == DynamicNavBakePathOrchestrationState.LocalSegmentReady)
            {
                if (TrySubmitLocalSegmentMoveOrders(engine, out string resumeError))
                {
                    _lastStatus =
                        $"Corridor continued after resident-window commit; local segment ordered ({_lastPathPointCount} points).";
                }
                else
                {
                    _moveCommandActive = false;
                    _lastStatus = resumeError;
                }
            }

            return;
        }

        if (!AreAllSquadMembersWithinLocalSegmentSlots(engine, config))
        {
            return;
        }

        if (AreAllSquadMembersWithinWorldGoalSlots(engine, config))
        {
            _moveCommandActive = false;
            _pathOrchestrationState = DynamicNavBakePathOrchestrationState.Arrived;
            _lastStatus = "Squad arrived at the open-world goal.";
            return;
        }

        if (_lastCoarseNodeCount <= 0)
        {
            throw new InvalidOperationException(
                "Open-world corridor advance requires a non-empty coarse corridor.");
        }

        // Advance to exactly the next corridor checkpoint (never skip ahead here).
        int nextCursor = Math.Min(_corridorCursor + 1, _lastCoarseNodeCount - 1);
        _corridorCursor = nextCursor;
        (int checkpointX, int checkpointZ) = _coarseCorridorWorldPoints[_corridorCursor];

        ResolveCommittedResidentBounds(engine, out int minX, out int minZ, out int maxX, out int maxZ);
        if (!IsPointInsideInclusive(checkpointX, checkpointZ, minX, minZ, maxX, maxZ))
        {
            // Cover live squad AABB + next checkpoint formation; checkpoint-only centering ejects trailers.
            if (!TryEnsureResidentWindowCoversSquadAndCheckpoint(
                    engine,
                    checkpointX,
                    checkpointZ,
                    out string slideError))
            {
                _moveCommandActive = false;
                _lastStatus = slideError;
                throw new InvalidOperationException(
                    $"Open-world next corridor checkpoint ({checkpointX},{checkpointZ}) at cursor {_corridorCursor} " +
                    $"is outside the committed resident window [{minX},{minZ}]-[{maxX},{maxZ}] " +
                    $"and the required squad+checkpoint slide could not be requested: {slideError}");
            }

            _pathOrchestrationState = DynamicNavBakePathOrchestrationState.WindowRebuilding;
            _lastPathStatus = NavPathStatus.NotReady;
            _lastPathPointCount = 0;
            UpdatePathBuffers(Array.Empty<int>(), Array.Empty<int>());
            _lastStatus = "Resident window sliding to cover the squad and next corridor checkpoint.";
            return;
        }

        RecomputePath(engine);
        if (_lastPathStatus == NavPathStatus.Ok &&
            _pathOrchestrationState == DynamicNavBakePathOrchestrationState.LocalSegmentReady)
        {
            if (TrySubmitLocalSegmentMoveOrders(engine, out string error))
            {
                _lastStatus = $"Advanced corridor checkpoint; local segment ordered ({_lastPathPointCount} points).";
            }
            else
            {
                _moveCommandActive = false;
                _lastStatus = error;
            }
        }
    }

    private bool TrySubmitLocalSegmentMoveOrders(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (_squadEntities.Length <= 0)
        {
            error = "Squad entities are not bound.";
            return false;
        }

        if (engine.GetService(CoreServiceKeys.OrderQueue) is not OrderQueue orderQueue)
        {
            error = "DynamicNavBake move requires OrderQueue.";
            return false;
        }

        if (engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not OrderTypeRegistry registry ||
            !registry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            error = $"DynamicNavBake move requires order type '{MassNavigationOrderKeys.Move}'.";
            return false;
        }

        // Formal MassNavigation seam: issue one WorldCm goal per member. Showcase-owned formation
        // slot expansion keeps massNavigationMove as a single destination while preserving the
        // authored grid around the shared local-segment center (MassNavigationMod boundary).
        // TryEnqueueBatch (not SharedBatch) assigns distinct OrderIds so each slot is its own
        // CommandGroup token — MassNavigation forbids conflicting destinations under one token.
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        DynamicNavBakeShowcaseSquadConfig squad = config.Squad;
        bool enforceResidentBounds = config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld;
        int residentMinX = 0;
        int residentMinZ = 0;
        int residentMaxX = 0;
        int residentMaxZ = 0;
        if (enforceResidentBounds)
        {
            ResolveCommittedResidentBounds(
                engine,
                out residentMinX,
                out residentMinZ,
                out residentMaxX,
                out residentMaxZ);
            if (residentMaxX < residentMinX || residentMaxZ < residentMinZ)
            {
                error =
                    "Open-world local segment move requires a committed resident window before enqueue.";
                return false;
            }
        }

        EnsureMoveOrderScratch(_squadEntities.Length);
        int orderCount = 0;
        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                error = $"DynamicNavBake move refused: authored squad member[{i}] is missing or dead.";
                return false;
            }

            DynamicNavBakeShowcaseWallPool.ComputeSquadSlotOffsetCm(
                squad,
                i,
                out int offsetXCm,
                out int offsetZCm);
            int goalXCm = checked(_localSegmentGoalXCm + offsetXCm);
            int goalZCm = checked(_localSegmentGoalZCm + offsetZCm);
            if (enforceResidentBounds)
            {
                if (!engine.World.TryGet(entity, out WorldPositionCm position))
                {
                    error =
                        $"Open-world local segment move requires WorldPositionCm on live squad member[{i}].";
                    return false;
                }

                WorldCmInt2 start = position.ToWorldCmInt2();
                if (!IsPointInsideResidentWindow(start.X, start.Y, residentMinX, residentMinZ, residentMaxX, residentMaxZ))
                {
                    error =
                        $"Open-world local segment refused: squad member[{i}] start ({start.X},{start.Y}) " +
                        $"is outside committed resident window [{residentMinX},{residentMinZ}]-[{residentMaxX},{residentMaxZ}].";
                    return false;
                }

                if (!IsPointInsideResidentWindow(goalXCm, goalZCm, residentMinX, residentMinZ, residentMaxX, residentMaxZ))
                {
                    error =
                        $"Open-world local segment refused: squad member[{i}] slot destination ({goalXCm},{goalZCm}) " +
                        $"is outside committed resident window [{residentMinX},{residentMinZ}]-[{residentMaxX},{residentMaxZ}].";
                    return false;
                }
            }

            _moveOrderScratch[orderCount++] = new Order
            {
                OrderId = 0,
                OrderTypeId = moveOrderTypeId,
                PlayerId = 1,
                Actor = entity,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = OrderArgs.CreateSingleWorldCm(new Vector3(goalXCm, 0f, goalZCm))
            };
        }

        if (orderCount <= 0)
        {
            error = "No live squad actors available for move orders.";
            return false;
        }

        OrderSubmitResult submit = orderQueue.TryEnqueueBatch(_moveOrderScratch.AsSpan(0, orderCount));
        if (!OrderSubmitResultSemantics.IsAccepted(submit))
        {
            error =
                $"Could not admit {orderCount} move orders ({submit}); OrderQueue available capacity is {orderQueue.AvailableCapacity}.";
            return false;
        }

        return true;
    }

    private void UpdateProgressStatus(GameEngine engine, RuntimeIncrementalNavMeshRebuildQueue queue)
    {
        if (queue.Status == RuntimeNavMeshRebuildStatus.Idle && !queue.HasResidentWindowTransition)
        {
            return;
        }

        _pathOrchestrationState = DynamicNavBakePathOrchestrationState.WindowRebuilding;
        _lastStatus = queue.HasResidentWindowTransition
            ? $"Resident window rebuilding ({queue.PendingTileCount} pending)."
            : $"Navmesh dirty rebuild in progress ({queue.PendingTileCount} pending).";
        _ = engine;
    }

    private void MaybeRefreshPathAfterGeneration(GameEngine engine, RuntimeIncrementalNavMeshRebuildQueue queue)
    {
        ulong generation = ReadLatestGeneration(engine);
        if (_squadDeployed && generation != _lastPathGeneration)
        {
            RecomputePath(engine);
        }
    }

    private static ulong ReadLatestGeneration(GameEngine engine)
    {
        ulong storeGeneration = 0UL;
        if (engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry registry) &&
            registry != null &&
            registry.TryGetStore(0, 0, out NavTileStore store))
        {
            storeGeneration = store.Generation;
        }

        if (engine.TryGetService(CoreServiceKeys.RuntimeNavMeshTelemetry, out RuntimeNavMeshTelemetryService telemetry) &&
            telemetry != null)
        {
            ulong telemetryGeneration = telemetry.CaptureSnapshot().LastGeneration;
            // ResetSamples clears last-* telemetry while the store still exposes the live generation.
            // Prefer a nonzero telemetry sample when present; otherwise keep the store pin.
            if (telemetryGeneration != 0UL)
            {
                return telemetryGeneration;
            }
        }

        return storeGeneration;
    }

    private void ClearStalePath(GameEngine engine, string status)
    {
        CancelOutstandingFormalMoves(engine);
        ClearShowcasePathOverlayOnly();
        _moveCommandActive = false;
        _corridorCursor = 0;
        _pathOrchestrationState = DynamicNavBakePathOrchestrationState.Idle;
        _lastStatus = status;
    }

    /// <summary>
    /// Clears only the showcase-owned path overlay / goal-path evidence.
    /// Never cancels formal massNavigationMove orders.
    /// </summary>
    private void ClearShowcasePathOverlayOnly()
    {
        _lastPathStatus = NavPathStatus.NotReady;
        _lastPathPointCount = 0;
        UpdatePathBuffers(Array.Empty<int>(), Array.Empty<int>());
        _lastCoarseNodeCount = 0;
        ClearCorridorPresentation();
        _publishedFormalRouteGeometrySignature = 0UL;
    }

    /// <summary>
    /// After a structural nav bake commits, ask the formal route sink to re-solve.
    /// Live orders stay active; agents keep marching and pick up the new polyline.
    /// </summary>
    private void RequestFormalRouteRepath(GameEngine engine)
    {
        if (!engine.TryGetService(MassNavigationKeys.RouteExecutionSink, out MassNavigationRouteExecutionSink routeSink) ||
            routeSink == null ||
            routeSink.ActiveRouteCount <= 0)
        {
            return;
        }

        routeSink.MarkAllActiveRoutesNeedsResolve();
    }

    /// <summary>
    /// Drops MassNavigation route-sink ownership for squad agents without cancelling orders/intents.
    /// Used while structural bake is in flight so ApplyRoute cannot re-solve against a torn mesh.
    /// </summary>
    private void ReleaseSquadFormalRoutesFromSink(GameEngine engine)
    {
        if (_squadEntities.Length <= 0 ||
            !engine.TryGetService(MassNavigationKeys.RouteExecutionSink, out MassNavigationRouteExecutionSink routeSink) ||
            routeSink == null)
        {
            return;
        }

        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (entity == Entity.Null)
            {
                continue;
            }

            routeSink.RemoveAgent(entity);
        }
    }

    private void CancelOutstandingFormalMoves(GameEngine engine)
    {
        if (_squadEntities.Length <= 0)
        {
            return;
        }

        OrderTypeRegistry registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException(
                "DynamicNavBake structural nav change requires OrderTypeRegistry to cancel outstanding formal moves.");

        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity entity = _squadEntities[i];
            if (entity == Entity.Null)
            {
                continue;
            }

            if (engine.World.IsAlive(entity))
            {
                OrderSubmitter.CancelAll(engine.World, entity, registry);
                if (engine.World.Has<MovePlanExecutionIntent>(entity))
                {
                    engine.World.Set(entity, default(MovePlanExecutionIntent));
                }

                if (engine.World.Has<MovePlanExecutionResult>(entity))
                {
                    engine.World.Set(entity, default(MovePlanExecutionResult));
                }
            }
        }

        ReleaseSquadFormalRoutesFromSink(engine);
    }

    private void UpdatePathBuffers(int[] pathXcm, int[] pathZcm)
    {
        _pathXcm = pathXcm ?? Array.Empty<int>();
        _pathZcm = pathZcm ?? Array.Empty<int>();
        _presentationPathRevision++;
    }

    private void ClearCorridorPresentation()
    {
        _presentationCorridorRevision++;
    }

    private void ApplyInitialCommandSource(GameEngine engine)
    {
        if (_squadEntities.Length <= 0)
        {
            throw new InvalidOperationException("Squad entities are not bound.");
        }

        Entity localPlayer = ResolveOrCreateLocalPlayer(engine);
        if (engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
        {
            throw new InvalidOperationException("DynamicNavBake showcase requires EntityCollectionStore for command-source selection.");
        }

        Span<Entity> selected = _squadEntities.AsSpan();
        Entity primary = selected[0];
        for (int i = 0; i < selected.Length; i++)
        {
            Entity entity = selected[i];
            if (!engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException($"Squad entity at index {i} is not alive.");
            }

            if (!engine.World.Has<CommandSourceSelectableState>(entity))
            {
                throw new InvalidOperationException("Squad actor is missing CommandSourceSelectableState.");
            }

            ref CommandSourceSelectableState selectable = ref engine.World.Get<CommandSourceSelectableState>(entity);
            selectable.IsEnabled = 1;
        }

        var descriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.UiAcquisition,
            EntityCollectionRoleKind.CommandSource,
            localPlayer,
            primary,
            "Dynamic nav bake squad",
            $"{selected.Length} actors");
        collections.Replace(localPlayer, descriptor, selected, localPlayer);
    }

    private static Entity ResolveOrCreateLocalPlayer(GameEngine engine)
    {
        Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (localPlayer != Entity.Null && engine.World.IsAlive(localPlayer))
        {
            return localPlayer;
        }

        // Prefer the map-bound relationship representative used by Command authorization.
        Ludots.Core.Gameplay.Teams.PlayerEntityLookup? players = engine.GetService(CoreServiceKeys.PlayerEntityLookup);
        int playerId = 1;
        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? playerIdObj) &&
            playerIdObj is int configuredPlayerId &&
            configuredPlayerId > 0)
        {
            playerId = configuredPlayerId;
        }

        if (players != null &&
            players.TryGet(playerId, out Entity boundRep) &&
            boundRep != Entity.Null &&
            engine.World.IsAlive(boundRep))
        {
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, boundRep);
            engine.SetService(CoreServiceKeys.LocalPlayerId, playerId);
            return boundRep;
        }

        throw new InvalidOperationException(
            $"DynamicNavBake requires a live PlayerEntityLookup representative for local player {playerId} before command-source selection.");
    }

    private void EnsureMoveOrderScratch(int capacity)
    {
        if (_moveOrderScratch.Length < capacity)
        {
            _moveOrderScratch = new Order[capacity];
        }
    }

    private void BindBoards(GameEngine engine)
    {
        MapSession? session = engine.CurrentMapSession;
        if (session == null)
        {
            return;
        }

        _nodeGraphBoard = null;
        _terrainBoard = null;
        foreach (IBoard board in session.AllBoards)
        {
            if (board is NodeGraphBoard nodeGraphBoard)
            {
                _nodeGraphBoard = nodeGraphBoard;
            }
            else if (board is GridBoard gridBoard)
            {
                _terrainBoard = gridBoard;
            }
        }
    }

    private DynamicNavBakeShowcaseConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("DynamicNavBake showcase requires ConfigPipeline before loading config.");
        }

        _config = new DynamicNavBakeShowcaseConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport,
            engine.CurrentMapSession?.MapId.Value
                ?? throw new InvalidOperationException("Dynamic NavBake showcase config requires an active map id."));
        EnsureSpawnScratch(DynamicNavBakeShowcaseWallPool.BuildSpawnRequestCount(_config));
        EnsureResidentScratch(checked(_config.ResidentWidthChunks * _config.ResidentHeightChunks));
        EnsurePlayerFramingAnchorScratch(checked(_config.Squad.Count + 1 + MaxPathFramingAnchors));
        RebindTelemetryToEvidenceSampleCount(engine, _config);
        return _config;
    }

    public bool TrySetNavMeshVisible(GameEngine engine, bool visible, out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        try
        {
            ConfigureNavMeshPresentation(engine, visible);
            _lastStatus = visible ? "Navigation surface shown." : "Navigation surface hidden.";
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }
    }

    private void ConfigureNavMeshPresentation(GameEngine engine, bool enabled)
    {
        DynamicNavBakeShowcasePresentationConfig presentation = ActiveConfig.Presentation;
        NavMeshPresentationState state = engine.GetService(CoreServiceKeys.NavMeshPresentationState)
            ?? throw new InvalidOperationException(
                "Dynamic NavBake showcase requires CoreServiceKeys.NavMeshPresentationState.");
        NavMeshPresentationStyle style = presentation.ToNavMeshStyle();
        state.Configure(
            enabled,
            presentation.NavMeshLayer,
            presentation.NavMeshProfile,
            in style);
    }

    private static void RebindTelemetryToEvidenceSampleCount(GameEngine engine, DynamicNavBakeShowcaseConfig config)
    {
        if (config.EvidenceSampleCount <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.evidenceSampleCount must be > 0.");
        }

        if (engine.TryGetService(CoreServiceKeys.RuntimeNavMeshTelemetry, out RuntimeNavMeshTelemetryService existing) &&
            existing != null &&
            existing.SampleCapacity == config.EvidenceSampleCount)
        {
            return;
        }

        engine.SetService(
            CoreServiceKeys.RuntimeNavMeshTelemetry,
            new RuntimeNavMeshTelemetryService(config.EvidenceSampleCount));
    }

    private void EnsureSpawnScratch(int capacity)
    {
        if (_spawnScratch.Length != capacity)
        {
            _spawnScratch = new RuntimeEntitySpawnRequest[capacity];
        }
    }

    private void EnsureResidentScratch(int capacity)
    {
        if (_residentScratch.Length < capacity)
        {
            _residentScratch = new NavBakeTileCoord[capacity];
        }
    }

    private void EnsureCoarseScratch(int capacity)
    {
        if (_coarseNodePath.Length < capacity)
        {
            _coarseNodePath = new int[capacity];
        }
    }

    private void EnsureCorridorScratch(int capacity)
    {
        if (_coarseCorridorWorldPoints.Length < capacity)
        {
            _coarseCorridorWorldPoints = new (int XCm, int ZCm)[capacity];
        }
    }

    private void EnsurePlayerFramingAnchorScratch(int capacity)
    {
        if (_playerFramingAnchorScratch.Length < capacity)
        {
            _playerFramingAnchorScratch = new Vector2[capacity];
        }
    }

    private DynamicNavBakeShowcaseWallPool RequireWallPool()
        => _wallPool ?? throw new InvalidOperationException("Wall pool is not initialized.");

    private void ResolveActiveWallCenter(out int centerXCm, out int centerYCm, out string? hotspotLabel)
    {
        DynamicNavBakeShowcaseConfig config = ActiveConfig;
        if (config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            DynamicNavBakeShowcaseOpenWorldConfig openWorld = config.OpenWorld
                ?? throw new InvalidOperationException("Open-world config is required for hotspot wall placement.");
            if ((uint)_openWorldHotspotIndex >= (uint)openWorld.Hotspots.Length)
            {
                throw new InvalidOperationException(
                    $"Open-world hotspot index {_openWorldHotspotIndex} is out of range for {openWorld.Hotspots.Length} hotspots.");
            }

            DynamicNavBakeShowcaseHotspotConfig hotspot = openWorld.Hotspots[_openWorldHotspotIndex];
            centerXCm = hotspot.WallCenterXCm;
            centerYCm = hotspot.WallCenterYCm;
            hotspotLabel = hotspot.Label;
            return;
        }

        centerXCm = config.Gate.CenterXCm;
        centerYCm = config.Gate.CenterYCm;
        hotspotLabel = null;
    }

    private static NavTriangleSurfaceTileGrid RequireTriangleSurfaceGrid(GameEngine engine)
    {
        NavTriangleSurfaceTileIndex surface = engine.GetService(CoreServiceKeys.NavTriangleSurface)
            ?? throw new InvalidOperationException(
                "DynamicNavBake showcase requires CoreServiceKeys.NavTriangleSurface.");
        return surface.Grid;
    }

    private static void ThrowIfSimulationBudgetFused(GameEngine engine, int tickIndex)
    {
        bool fused = (engine.Pacemaker is RealtimePacemaker realtime && realtime.IsBudgetFused) ||
                     (engine.Pacemaker is TurnBasedPacemaker turnBased && turnBased.IsBudgetFused);
        if (!fused)
        {
            return;
        }

        throw new InvalidOperationException(
            $"DynamicNavBake DrainUntilIdle aborted: simulation budget fused at drain tick {tickIndex} " +
            $"(budgetMs={engine.SimulationBudgetMsPerFrame}, sliceLimit={engine.SimulationMaxSlicesPerLogicFrame}). " +
            "FixedStep is halted, so structural wall teleports cannot dirty or rebuild navmesh.");
    }

    private static RuntimeIncrementalNavMeshRebuildQueue RequireQueue(GameEngine engine)
        => engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
            ?? throw new InvalidOperationException("DynamicNavBake showcase requires RuntimeNavMeshRebuildQueue.");

    private static MassNavigationRouteExecutionSink? ResolveRouteExecutionSink(GameEngine engine)
    {
        return engine.TryGetService(MassNavigationKeys.RouteExecutionSink, out MassNavigationRouteExecutionSink sink)
            ? sink
            : null;
    }

    private static NavQueryService RequireNavQuery(GameEngine engine)
    {
        NavQueryServiceRegistry registry = engine.GetService(CoreServiceKeys.NavQueryServices)
            ?? throw new InvalidOperationException("DynamicNavBake showcase requires NavQueryServices.");
        if (!registry.TryCreateQuery(layer: 0, profile: 0, NavAreaCostTable.CreateDefault(), out NavQueryService service))
        {
            throw new InvalidOperationException("DynamicNavBake showcase requires Ground/light nav query service.");
        }

        return service;
    }

    private static MapId RequireCurrentMapId(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("DynamicNavBake showcase requires an active map session.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"DynamicNavBake showcase requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

    private static void ValidateTemplates(GameEngine engine, DynamicNavBakeShowcaseConfig config)
    {
        ValidateTemplate(engine, config.Gate.WallTemplateId);
        ValidateTemplate(engine, config.Goal.TemplateId);
        ValidateTemplate(engine, config.SideRouteWest.MarkerTemplateId);
        ValidateTemplate(engine, config.SideRouteEast.MarkerTemplateId);
        ValidateTemplate(engine, config.Squad.TemplateId);
    }

    private static void ValidateTemplate(GameEngine engine, string templateId)
    {
        if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
        {
            throw new InvalidOperationException($"DynamicNavBake showcase requires template '{templateId}'.");
        }
    }

    private void ActivateShowcaseCamera(GameEngine engine, Vector2 targetCm)
    {
        // Interactive play keeps Camera.Profile.Tactical (KeyboardAndEdge). Auto timeline uses a
        // dedicated locked Orbit profile so edge-pan from a centered pointer cannot drift capture.
        string cameraId = IsAutoTimelineEnabledSticky()
            ? DynamicNavBakeShowcaseIds.AutoCaptureCameraId
            : "Camera.Profile.Tactical";

        if (engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is VirtualCameraRegistry registry &&
            registry.TryGet(cameraId, out VirtualCameraDefinition? definition) &&
            definition != null)
        {
            engine.GameSession.Camera.ResetVirtualCameras();
            engine.GameSession.Camera.ActivateVirtualCamera(
                cameraId,
                blendDurationSeconds: 0f,
                followTarget: CameraFollowTargetFactory.Build(
                    engine.World,
                    engine.GlobalContext,
                    definition.FollowTargetKind,
                    Arch.Core.Entity.Null,
                    definition.FollowCollectionKey),
                snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable);
        }
        else
        {
            throw new InvalidOperationException(
                $"DynamicNavBake showcase requires virtual camera '{cameraId}' to be registered.");
        }

        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = cameraId,
            TargetCm = targetCm
        });
    }

    private void RefreshPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            // Headless hosts omit UIRoot; panel mount is skipped deliberately.
            return;
        }

        _panelController.MountOrSync(root, engine, BuildPanelState(engine));
    }

    private void EnableOpenWorldMinimap(GameEngine engine)
    {
        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException(
                "Open-world DynamicNavBake showcase requires CoreServiceKeys.MinimapRuntime.");

        if (!_openWorldMinimapEnabledByShowcase)
        {
            _openWorldMinimapVisibleSaved = minimap.Visible;
            _openWorldMinimapNativeChromeVisibleSaved = minimap.NativeChromeVisible;
            _openWorldMinimapPresetSaved = minimap.Preset;
            _openWorldMinimapFollowEntitySaved = minimap.FollowEntity;
            _openWorldMinimapHalfExtentCmSaved = minimap.HalfExtentCm;
            _openWorldMinimapRotateWithCameraSaved = minimap.RotateWithCamera;
            _openWorldMinimapZoomNormalizedSaved = minimap.ZoomNormalized;
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            _openWorldMinimapEnabledByShowcase = true;
        }

        // Sticky-true only: never re-read Environment on the framing/Enable hot path.
        if (IsAutoTimelineEnabledSticky())
        {
            ApplyOpenWorldAutoCaptureMinimapRect(minimap);
        }
        else
        {
            // Interactive path never authored an external rect — do not ClearExternalFieldRect
            // and do not force NativeChromeVisible.
            ClearOpenWorldAutoCaptureMinimapRectIfOwned(minimap);
        }
    }

    private void ApplyOpenWorldAutoCaptureMinimapRect(MinimapRuntime minimap)
    {
        DynamicNavBakeShowcaseOpenWorldConfig openWorld = ActiveConfig.OpenWorld
            ?? throw new InvalidOperationException(
                "Open-world auto-capture minimap requires openWorld config.");
        DynamicNavBakeShowcaseMinimapRectConfig rect = openWorld.AutoCaptureMinimapRect
            ?? throw new InvalidOperationException(
                "Open-world auto-capture minimap requires openWorld.autoCaptureMinimapRect.");

        minimap.SetExternalFieldRect(rect.X, rect.Y, rect.Width, rect.Height);
        bool chromeFits = NativeChromeFitsExternalRect(
            rect.Width,
            rect.Height,
            minimap.ZoomSliderEnabled);
        minimap.NativeChromeVisible = chromeFits;
        _openWorldAutoCaptureMinimapRectActive = true;
    }

    private void ClearOpenWorldAutoCaptureMinimapRectIfOwned(MinimapRuntime minimap)
    {
        if (!_openWorldAutoCaptureMinimapRectActive)
        {
            return;
        }

        minimap.ClearExternalFieldRect();
        _openWorldAutoCaptureMinimapRectActive = false;
    }

    private static bool NativeChromeFitsExternalRect(int width, int height, bool zoomSliderEnabled)
    {
        int fieldSize = Math.Max(1, Math.Min(width, height));
        int chromeBelow =
            (zoomSliderEnabled ? MinimapChromeZoomSliderHeight : 0) +
            MinimapChromeGapBelowField +
            MinimapChromeToggleButtonHeight;
        int availableBelowInsideRect = (height - fieldSize) / 2;
        return availableBelowInsideRect >= chromeBelow;
    }

    private void HideOpenWorldMinimapIfOwned(GameEngine engine)
    {
        if (!_openWorldMinimapEnabledByShowcase)
        {
            return;
        }

        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException(
                "Open-world DynamicNavBake showcase requires CoreServiceKeys.MinimapRuntime to release minimap ownership.");

        ClearOpenWorldAutoCaptureMinimapRectIfOwned(minimap);
        RestoreOpenWorldMinimapPublicState(minimap);
        minimap.NativeChromeVisible = _openWorldMinimapNativeChromeVisibleSaved;
        minimap.Visible = _openWorldMinimapVisibleSaved;

        _openWorldMinimapEnabledByShowcase = false;
    }

    private void RestoreOpenWorldMinimapPublicState(MinimapRuntime minimap)
    {
        switch (_openWorldMinimapPresetSaved)
        {
            case MinimapPreset.FollowEntity:
                minimap.UseFollowEntityPreset(
                    _openWorldMinimapFollowEntitySaved,
                    _openWorldMinimapHalfExtentCmSaved);
                break;
            case MinimapPreset.FollowCamera:
                minimap.UseFollowCameraPreset(
                    _openWorldMinimapHalfExtentCmSaved,
                    _openWorldMinimapRotateWithCameraSaved);
                break;
            default:
                minimap.UseRtsFullMapPreset();
                break;
        }

        minimap.SetRotateWithCamera(_openWorldMinimapRotateWithCameraSaved);
        minimap.SetZoomNormalized(_openWorldMinimapZoomNormalizedSaved);
    }

    /// <summary>
    /// Discloses authored squad members as LiveVisible to the local player so formal CommandSource
    /// click/box acquisition can authorize them. Required in every scene once KnowledgeProjectionResolver
    /// is installed (selection no longer treats "no knowledge" as visible).
    /// </summary>
    internal void RefreshSquadCommandKnowledge(GameEngine engine)
    {
        if (!_entitiesBound)
        {
            return;
        }

        KnowledgeProjectionStore store = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException(
                "DynamicNavBake showcase requires CoreServiceKeys.KnowledgeProjectionStore before squad selection knowledge can be disclosed.");

        Entity viewer = ResolveOrCreateLocalPlayer(engine);
        if (viewer == Entity.Null || !engine.World.IsAlive(viewer))
        {
            throw new InvalidOperationException(
                "DynamicNavBake showcase requires a live LocalPlayerEntity before disclosing squad selection knowledge.");
        }

        int liveCount = 0;
        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity candidate = _squadEntities[i];
            if (candidate == Entity.Null || !engine.World.IsAlive(candidate))
            {
                throw new InvalidOperationException(
                    $"DynamicNavBake showcase squad member[{i}] is missing while disclosing selection knowledge.");
            }

            liveCount++;
        }

        // Same viewer/targets already owned: do not Upsert every FixedStep.
        if (_openWorldKnowledgeTargetCount == liveCount &&
            _openWorldKnowledgeViewer == viewer &&
            OpenWorldKnowledgeTargetsMatch(_squadEntities, liveCount))
        {
            return;
        }

        if (_openWorldKnowledgeTargetCount > 0)
        {
            ReleaseOwnedOpenWorldKnowledgePairs(engine, store);
        }

        if (_openWorldKnowledgeTargets.Length < liveCount)
        {
            _openWorldKnowledgeTargets = new Entity[liveCount];
            _openWorldKnowledgePrevious = new KnowledgeDisclosureRecord[liveCount];
            _openWorldKnowledgeHadPrevious = new bool[liveCount];
        }

        int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
        var empty = KnowledgeIdMask256.Empty;
        var record = new KnowledgeDisclosureRecord(
            KnowledgePresence.LiveVisible,
            KnowledgePositionAccess.Live,
            empty,
            empty,
            empty,
            viewer,
            observedTick,
            expiryTick: 0,
            confidencePermille: 1000,
            revision: 0);

        int written = 0;
        for (int i = 0; i < _squadEntities.Length; i++)
        {
            Entity target = _squadEntities[i];
            bool hadPrevious = store.TryGet(viewer, target, observedTick, out KnowledgeDisclosureRecord previous);
            _openWorldKnowledgeHadPrevious[written] = hadPrevious;
            _openWorldKnowledgePrevious[written] = hadPrevious ? previous : default;
            store.Upsert(viewer, target, in record);
            _openWorldKnowledgeTargets[written++] = target;
        }

        _openWorldKnowledgeOwnedSemantic = record;
        _openWorldKnowledgeViewer = viewer;
        _openWorldKnowledgeTargetCount = written;
    }

    // Keep the historical name as a thin alias for open-world call sites / tests.
    internal void RefreshOpenWorldSquadKnowledge(GameEngine engine) => RefreshSquadCommandKnowledge(engine);

    internal void ClearOpenWorldSquadKnowledge(GameEngine engine)
    {
        if (_openWorldKnowledgeTargetCount <= 0 || _openWorldKnowledgeViewer == Entity.Null)
        {
            _openWorldKnowledgeViewer = Entity.Null;
            _openWorldKnowledgeTargetCount = 0;
            return;
        }

        KnowledgeProjectionStore store = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException(
                "DynamicNavBake showcase requires CoreServiceKeys.KnowledgeProjectionStore to release owned knowledge disclosures.");

        ReleaseOwnedOpenWorldKnowledgePairs(engine, store);
    }

    private void ReleaseOwnedOpenWorldKnowledgePairs(GameEngine engine, KnowledgeProjectionStore store)
    {
        Entity viewer = _openWorldKnowledgeViewer;
        int count = _openWorldKnowledgeTargetCount;
        if (count <= 0 || viewer == Entity.Null)
        {
            _openWorldKnowledgeViewer = Entity.Null;
            _openWorldKnowledgeTargetCount = 0;
            return;
        }

        int tick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
        for (int i = 0; i < count; i++)
        {
            Entity target = _openWorldKnowledgeTargets[i];
            if (!store.TryGet(viewer, target, tick, out KnowledgeDisclosureRecord current))
            {
                continue;
            }

            // Only touch pairs still carrying this showcase's semantic record.
            if (!SameKnowledgeSemantics(in current, in _openWorldKnowledgeOwnedSemantic))
            {
                continue;
            }

            if (_openWorldKnowledgeHadPrevious[i])
            {
                store.Upsert(viewer, target, in _openWorldKnowledgePrevious[i]);
            }
            else
            {
                store.Remove(viewer, target);
            }
        }

        _openWorldKnowledgeViewer = Entity.Null;
        _openWorldKnowledgeTargetCount = 0;
    }

    private bool OpenWorldKnowledgeTargetsMatch(Entity[] squadEntities, int liveCount)
    {
        if (_openWorldKnowledgeTargetCount != liveCount)
        {
            return false;
        }

        for (int i = 0; i < liveCount; i++)
        {
            if (_openWorldKnowledgeTargets[i] != squadEntities[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameKnowledgeSemantics(
        in KnowledgeDisclosureRecord left,
        in KnowledgeDisclosureRecord right)
    {
        return left.Presence == right.Presence &&
               left.Position == right.Position &&
               left.AttributeMask == right.AttributeMask &&
               left.RelationshipTypeMask == right.RelationshipTypeMask &&
               left.TagMask == right.TagMask &&
               left.Source == right.Source &&
               left.ObservedTick == right.ObservedTick &&
               left.ExpiryTick == right.ExpiryTick &&
               left.ConfidencePermille == right.ConfidencePermille;
    }

    private void Unbind(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.NavMeshPresentationState) is NavMeshPresentationState navMeshPresentation)
        {
            navMeshPresentation.Disable();
        }

        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }

        ClearOpenWorldSquadKnowledge(engine);
        HideOpenWorldMinimapIfOwned(engine);
        engine.GlobalContext.Remove(DynamicNavBakeShowcaseIds.RuntimeServiceKey);

        _config = null;
        _wallPool = null;
        _coarseGraph = null;
        _nodeGraphBoard = null;
        _terrainBoard = null;
        _scenarioSpawned = false;
        _entitiesBound = false;
        _mapFocusPresentationPending = false;
        _squadDeployed = false;
        _constructionMode = false;
        engine.GlobalContext.Remove(CoreServiceKeys.CommandSourceAcquisitionSuppressed.Name);
        _moveCommandActive = false;
        _formalMoveCommandSubmitCount = 0;
        _openWorldHotspotIndex = 0;
        _corridorCursor = 0;
        _lastPathStatus = NavPathStatus.NotReady;
        _lastPathPointCount = 0;
        _lastCoarseNodeCount = 0;
        _pathOrchestrationState = DynamicNavBakePathOrchestrationState.Idle;
        _pathXcm = Array.Empty<int>();
        _pathZcm = Array.Empty<int>();
        _playerFramingAnchorScratch = Array.Empty<Vector2>();
        _coarseNodePath = Array.Empty<int>();
        _coarseCorridorWorldPoints = Array.Empty<(int, int)>();
        _openWorldKnowledgeTargets = Array.Empty<Entity>();
        _openWorldKnowledgePrevious = Array.Empty<KnowledgeDisclosureRecord>();
        _openWorldKnowledgeHadPrevious = Array.Empty<bool>();
        _openWorldKnowledgeOwnedSemantic = default;
        _openWorldMinimapFollowEntitySaved = Entity.Null;
        _autoTimelineEnvironmentValidated = false;
        _autoTimelineEnabledSticky = false;
        _presentationPathRevision++;
        _presentationCorridorRevision++;
        _squadEntities = Array.Empty<Entity>();
        _editTransaction.Reset();
        _editBakeAwaitingCompletion = false;
        _editBakeGenerationBefore = 0UL;
        _editBakeFailedBatchCountBefore = 0;
        _lastStatus = "Dynamic NavMesh bake showcase ready.";
    }
}
