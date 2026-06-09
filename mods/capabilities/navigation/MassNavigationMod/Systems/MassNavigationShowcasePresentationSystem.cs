using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationShowcasePresentationSystem : ISystem<float>
{
    private const float OverlayY = 0.075f;
    private const float GroundOverlayLiftMeters = 0.45f;
    private const float ScreenRouteLiftMeters = 2.2f;
    private const float GroundOverlaySegmentLengthCm = 2_000f;
    private const int MaxGroundOverlayLineSegments = 64;
    private const float PathWidthCm = 55f;
    private const float RouteOnlyPathWidthCm = 150f;
    private const float CorridorWidthCm = 360f;
    private const int MaxPathPoints = 48;
    private const int MaxSlotMarkers = 60;
    private const int MaxHpaRouteLabels = 12;
    private const int MaxFocusedHpaRouteLabels = 7;
    private const int MaxHpaInsetRouteSamples = 11;
    private const int MaxProjectedNavMeshEdgeLabels = 6;
    private const int MaxProjectedPortalLabels = 4;
    private const int MaxProjectedHpaPortalLabels = 5;
    private const int MaxObstacleBucketMarkers = 220;
    private const int MaxRuntimeDirtyChunkMarkers = 36;
    private const int MaxRuntimeObstaclePolygons = 8;

    private static readonly Vector4 PanelFill = new(0.03f, 0.07f, 0.10f, 0.82f);
    private static readonly Vector4 PanelBorder = new(0.24f, 0.48f, 0.60f, 0.78f);
    private static readonly Vector4 Text = new(0.90f, 0.96f, 1.0f, 1f);
    private static readonly Vector4 Muted = new(0.62f, 0.72f, 0.80f, 1f);
    private static readonly Vector4 Warn = new(1.0f, 0.70f, 0.42f, 1f);
    private static readonly Vector4 Good = new(0.52f, 0.96f, 0.62f, 1f);
    private static readonly Vector4 PathFill = new(0.20f, 0.92f, 0.76f, 0.18f);
    private static readonly Vector4 PathBorder = new(0.35f, 1.0f, 0.86f, 0.95f);
    private static readonly Vector4 RouteOnlyPathFill = new(0.20f, 0.96f, 0.82f, 0.36f);
    private static readonly Vector4 RouteOnlyPathBorder = new(0.62f, 1.0f, 0.92f, 1.0f);
    private static readonly Vector4 CorridorFill = new(0.16f, 0.72f, 1.0f, 0.11f);
    private static readonly Vector4 CorridorBorder = new(0.20f, 0.76f, 1.0f, 0.45f);
    private static readonly Vector4 WaypointFill = new(1.0f, 0.84f, 0.28f, 0.36f);
    private static readonly Vector4 WaypointBorder = new(1.0f, 0.92f, 0.46f, 0.96f);
    private static readonly Vector4 PortalFill = new(1.0f, 0.42f, 0.22f, 0.28f);
    private static readonly Vector4 PortalBorder = new(1.0f, 0.56f, 0.34f, 0.96f);
    private static readonly Vector4 HpaRouteLineFill = new(0.56f, 0.44f, 1.0f, 0.18f);
    private static readonly Vector4 HpaRouteLineBorder = new(0.78f, 0.70f, 1.0f, 0.96f);
    private static readonly Vector4 HpaFill = new(0.56f, 0.44f, 1.0f, 0.14f);
    private static readonly Vector4 HpaBorder = new(0.72f, 0.62f, 1.0f, 0.88f);
    private static readonly Vector4 HpaCellFill = new(0.58f, 0.50f, 1.0f, 0.20f);
    private static readonly Vector4 HpaCellBorder = new(0.78f, 0.70f, 1.0f, 0.92f);
    private static readonly Vector4 SlotFill = new(1.0f, 0.85f, 0.30f, 0.18f);
    private static readonly Vector4 SlotBorder = new(1.0f, 0.91f, 0.46f, 0.70f);
    private static readonly Vector4 BlockedFill = new(1.0f, 0.18f, 0.16f, 0.12f);
    private static readonly Vector4 BlockedBorder = new(1.0f, 0.35f, 0.34f, 0.80f);
    private static readonly Vector4 RuntimeObstacleFill = new(1.0f, 0.18f, 0.14f, 0.18f);
    private static readonly Vector4 RuntimeObstacleBorder = new(1.0f, 0.38f, 0.32f, 0.96f);
    private static readonly Vector4 RuntimeDirtyChunkFill = new(1.0f, 0.82f, 0.18f, 0.12f);
    private static readonly Vector4 RuntimeDirtyChunkBorder = new(1.0f, 0.86f, 0.32f, 0.92f);
    private static readonly Vector4 NavMeshCoverageFill = new(0.06f, 0.72f, 0.92f, 0.06f);
    private static readonly Vector4 NavMeshCoverageBorder = new(0.12f, 0.90f, 1.0f, 0.92f);
    private static readonly Vector4 WorldCoverageBorder = new(1.0f, 0.70f, 0.26f, 0.48f);
    private static readonly Vector4 AirFill = new(0.78f, 0.90f, 1.0f, 0.09f);
    private static readonly Vector4 AirBorder = new(0.78f, 0.92f, 1.0f, 0.66f);
    private static readonly Vector4 LabelFill = new(0.03f, 0.07f, 0.10f, 0.78f);
    private static readonly Vector4 LabelBorder = new(0.30f, 0.68f, 0.82f, 0.88f);

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly MassNavigationShowcaseGuideRuntime _guide;

    private enum PathDebugVisualMode
    {
        RouteOnly,
        CorridorAndPortals,
        WaypointAuthoring
    }

    private readonly record struct HpaRouteChunk(int X, int Y, int PortalIndex, bool FromGraphRoute);

    public MassNavigationShowcasePresentationSystem(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _guide = guide ?? throw new ArgumentNullException(nameof(guide));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is GroundOverlayBuffer ground)
        {
            RenderGroundOverlays(ground);
        }

        if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is ScreenOverlayBuffer overlay)
        {
            RenderGuideOverlay(overlay);
            RenderProjectedWorldLabels(overlay);
        }
    }

    private void RenderGroundOverlays(GroundOverlayBuffer ground)
    {
        switch (_guide.CurrentStepId)
        {
            case MassNavigationShowcaseStepId.VisualHeightmapBake:
            case MassNavigationShowcaseStepId.LogicHeightmapBake:
                if (_guide.DebugNavMeshEnabled)
                {
                    DrawNavMeshSample(ground);
                    DrawLogicHeightmapBakeFlow(ground);
                    DrawActiveWindow(ground);
                }
                break;
            case MassNavigationShowcaseStepId.LayerAreaEditor:
                if (_guide.DebugLayerCostEnabled)
                {
                    DrawLayerCostRegions(ground);
                }

                if (_guide.DebugNavMeshEnabled)
                {
                    DrawNavMeshSample(ground);
                }
                break;
            case MassNavigationShowcaseStepId.BakeToolQuery:
                if (_guide.DebugNavMeshEnabled)
                {
                    DrawNavMeshSample(ground);
                }

                if (_guide.DebugPathEnabled)
                {
                    DrawPathOnly(ground, PathDebugVisualMode.RouteOnly);
                }

                if (_guide.DebugHpaEnabled)
                {
                    DrawHpaRoute(ground);
                    DrawActiveWindow(ground);
                }

                if (_guide.DebugLayerCostEnabled)
                {
                    DrawLayerCostRegions(ground);
                }

                DrawRuntimeBakeAuthoring(ground);
                break;
            case MassNavigationShowcaseStepId.WorldHpa:
            case MassNavigationShowcaseStepId.LargeWorldStreaming:
                if (_guide.DebugHpaEnabled)
                {
                    DrawHpaRoute(ground);
                    DrawActiveWindow(ground);
                }

                if (_guide.DebugPathEnabled)
                {
                    DrawPathOnly(ground, PathDebugVisualMode.RouteOnly);
                }
                break;
            case MassNavigationShowcaseStepId.PathOnly:
                if (_guide.DebugPathEnabled)
                {
                    DrawPathOnly(
                        ground,
                        _simulation.AcceptanceDiagnostics.WaypointPath.HasAuthoredPlan
                            ? PathDebugVisualMode.WaypointAuthoring
                            : PathDebugVisualMode.RouteOnly);
                }
                break;
            case MassNavigationShowcaseStepId.NavMeshBake:
                if (_guide.DebugNavMeshEnabled)
                {
                    DrawNavMeshSample(ground);
                    DrawActiveWindow(ground);
                }

                if (_guide.DebugPathEnabled)
                {
                    DrawPathOnly(ground, PathDebugVisualMode.RouteOnly);
                }
                break;
            case MassNavigationShowcaseStepId.StrategySwitch:
                DrawPathOnly(ground, PathDebugVisualMode.CorridorAndPortals);
                DrawStrategyCompare(ground);
                break;
            case MassNavigationShowcaseStepId.LayerCosts:
                if (_guide.DebugLayerCostEnabled)
                {
                    DrawLayerCostRegions(ground);
                }

                if (_guide.DebugNavMeshEnabled)
                {
                    DrawNavMeshSample(ground);
                }
                break;
            case MassNavigationShowcaseStepId.OrderReuse:
                if (_guide.DebugPathEnabled)
                {
                    DrawPathOnly(ground, PathDebugVisualMode.RouteOnly);
                    DrawOrderReuseBucket(ground);
                }
                break;
            case MassNavigationShowcaseStepId.TargetAllocation:
                if (_guide.DebugSlotsEnabled)
                {
                    DrawTargetAllocation(ground);
                }
                break;
            case MassNavigationShowcaseStepId.TenKFlow:
                if (_guide.DebugPathEnabled)
                {
                    DrawPathOnly(ground, PathDebugVisualMode.RouteOnly);
                }

                if (_guide.DebugSlotsEnabled)
                {
                    DrawTargetAllocation(ground);
                }
                break;
            case MassNavigationShowcaseStepId.StaticObstacleWorld:
                DrawActiveWindow(ground);
                DrawStaticObstacleBuckets(ground);
                DrawRuntimeBakeAuthoring(ground);
                break;
            case MassNavigationShowcaseStepId.WaypointAuthoring:
                DrawPathOnly(ground, PathDebugVisualMode.WaypointAuthoring);
                break;
            case MassNavigationShowcaseStepId.PerformanceDebug:
            case MassNavigationShowcaseStepId.DebugVisualBudget:
                DrawActiveWindow(ground);
                DrawTargetAllocation(ground, sampleOnly: true);
                break;
        }
    }

    private void RenderGuideOverlay(ScreenOverlayBuffer overlay)
    {
        if (_guide.FocusedPanel)
        {
            RenderFocusedGuideOverlay(overlay);
            return;
        }

        MassNavigationShowcaseStep step = _guide.CurrentStep;
        int x = 496;
        int y = 16;
        int width = 760;
        int height = 380;
        int serial = ApplyLiveDiagnosticSerial(_guide.ActionRevision + ((int)_guide.CurrentStepId * 1000));

        overlay.AddRect(x, y, width, height, PanelFill, PanelBorder, stableId: 45000, dirtySerial: serial);
        overlay.AddText(x + 14, y + 12, $"Operation {(_guide.CurrentStepIndex + 1):00}/{_guide.StepCount:00}: {step.Title}", 18, Text, 45001, serial);
        overlay.AddText(x + 14, y + 38, $"Use case body: {BuildUseCaseBodyLine()}", 13, Text, 45002, serial);
        overlay.AddText(x + 14, y + 58, $"User operation: {BuildUserOperationLine()}", 13, Warn, 45003, serial);
        overlay.AddText(x + 14, y + 82, $"Live output: {BuildFocusedStatusLine()}", 13, Good, 45004, serial);
        overlay.AddText(x + 14, y + 106, $"Acceptance signal: {Shorten(BuildAcceptanceSignalLine(), 100)}", 13, Text, 45005, serial);
        overlay.AddText(x + 14, y + 130, $"Debug meaning: {Shorten(step.DebugLegend, 102)}", 12, Muted, 45006, serial);
        overlay.AddText(x + 14, y + 152, $"Production chain: {Shorten(_guide.OperationContract, 102)}", 12, Text, 45007, serial);
        overlay.AddText(x + 14, y + 176, $"Current action: {Shorten(_guide.LastActionText, 104)}", 12, Good, 45008, serial);
        overlay.AddText(x + 14, y + 202, $"Why this matters: {Shorten(step.Why, 102)}", 12, Warn, 45009, serial);
        overlay.AddText(x + 14, y + 224, $"Data source: {Shorten(BuildDataSourceLine(), 102)}", 12, Text, 45010, serial);
        overlay.AddText(x + 14, y + 248, $"Summary: {Shorten(BuildStatusLine(), 102)}", 12, Muted, 45011, serial);
        overlay.AddText(x + 14, y + 270, $"Layer/nav: {Shorten(BuildLayerLine(), 102)}", 12, Muted, 45012, serial);
        overlay.AddText(x + 14, y + 292, $"Use-case detail: {Shorten(BuildStepSpecificLine(), 102)}", 12, Muted, 45013, serial);
        overlay.AddText(x + 14, y + 318, "Operate in the window first; captures and JSONL traces are evidence after the operation.", 12, Warn, 45014, serial);
        overlay.AddText(x + 14, y + 340, $"Player check: {Shorten(step.PlayerExpected, 102)}", 12, Text, 45015, serial);
        overlay.AddText(x + 14, y + 360, $"Mod-author check: {Shorten(_guide.ModAuthorPerspective, 102)}", 12, Muted, 45016, serial);

        RenderLegendOverlay(overlay, x, y + height + 10, serial);
    }

    private void RenderFocusedGuideOverlay(ScreenOverlayBuffer overlay)
    {
        ResolveViewport(out int viewportWidth, out int viewportHeight);
        int shortEdge = Math.Max(1, Math.Min(viewportWidth, viewportHeight));
        int x = Math.Clamp((int)MathF.Round(viewportWidth * 0.01f), 8, 14);
        int y = Math.Clamp((int)MathF.Round(viewportHeight * 0.012f), 8, 14);
        bool pathOnlyWithAuthoredWaypoint =
            _guide.CurrentStepId == MassNavigationShowcaseStepId.PathOnly &&
            _simulation.AcceptanceDiagnostics.WaypointPath.HasAuthoredPlan;
        int width = _guide.CurrentStepId == MassNavigationShowcaseStepId.PathOnly
            ? Math.Clamp((int)MathF.Round(shortEdge * 0.30f), 260, 380)
            : Math.Clamp((int)MathF.Round(shortEdge * 0.20f), 144, 220);
        float maxViewportRatio = _guide.CurrentStepId == MassNavigationShowcaseStepId.PathOnly ? 0.34f : 0.20f;
        width = Math.Min(width, Math.Max(128, (int)MathF.Round(viewportWidth * maxViewportRatio)));
        int height = pathOnlyWithAuthoredWaypoint
            ? 86
            : _guide.CurrentStepId == MassNavigationShowcaseStepId.PathOnly ||
                _guide.CurrentStepId == MassNavigationShowcaseStepId.WaypointAuthoring
                    ? 54
                    : _guide.CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery
                        ? 54
                    : 38;
        int titleCharacters = Math.Clamp((width - 18) / 8, 16, 28);
        int signalCharacters = Math.Clamp((width - 18) / 7, 20, 32);
        int serial = _guide.ActionRevision + ((int)_guide.CurrentStepId * 1000) + 97;
        serial = ApplyLiveDiagnosticSerial(serial);

        overlay.AddRect(x, y, width, height, PanelFill, PanelBorder, stableId: 44900, dirtySerial: serial);
        overlay.AddText(x + 9, y + 7, Shorten(BuildFocusedCompactTitle(), titleCharacters), 11, Text, 44901, serial);
        overlay.AddText(x + 9, y + 22, Shorten(BuildFocusedCompactSignal(), signalCharacters), 10, Good, 44902, serial);
        if (_guide.CurrentStepId == MassNavigationShowcaseStepId.PathOnly)
        {
            overlay.AddText(x + 9, y + 37, BuildPathOnlyOrderCompactSignal(), 10, Good, 44903, serial);
            if (pathOnlyWithAuthoredWaypoint)
            {
                MassNavigationWaypointPathDiagnostics waypoint = _simulation.AcceptanceDiagnostics.WaypointPath;
                overlay.AddText(x + 9, y + 52, $"Waypoint plan authored={waypoint.HasAuthoredPlan}", 10, WaypointBorder, 44904, serial);
                overlay.AddText(x + 9, y + 67, $"invalidatedOldPathpoints={waypoint.InvalidatedPathPointCount}", 10, WaypointBorder, 44905, serial);
            }
        }
        else if (_guide.CurrentStepId == MassNavigationShowcaseStepId.WaypointAuthoring)
        {
            overlay.AddText(x + 9, y + 37, Shorten(BuildWaypointLine(), signalCharacters), 10, Good, 44903, serial);
        }
        else if (_guide.CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery)
        {
            overlay.AddText(x + 9, y + 37, BuildRuntimeBakeResultCompactLine(), 10, Good, 44903, serial);
        }

        if (_guide.CurrentStepId == MassNavigationShowcaseStepId.TargetAllocation)
        {
            RenderFocusedTargetAllocationStrip(overlay, viewportWidth, viewportHeight, serial);
        }

        if (_guide.CurrentStepId == MassNavigationShowcaseStepId.WorldHpa ||
            _guide.CurrentStepId == MassNavigationShowcaseStepId.LargeWorldStreaming)
        {
            int insetWidth = Math.Clamp((int)MathF.Round(shortEdge * 0.28f), 180, 300);
            insetWidth = Math.Min(insetWidth, Math.Max(160, (int)MathF.Round(viewportWidth * 0.26f)));
            int insetHeight = Math.Clamp((int)MathF.Round(viewportHeight * 0.15f), 92, 124);
            RenderFocusedHpaRouteInset(overlay, x, y + height + 8, insetWidth, insetHeight, serial);
        }
    }

    private string BuildFocusedCompactTitle()
    {
        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.TenKFlow => "U12 Flow",
            MassNavigationShowcaseStepId.TargetAllocation => "U8 Target Slots",
            MassNavigationShowcaseStepId.WorldHpa => "U5 HPA Route",
            MassNavigationShowcaseStepId.PathOnly => "U4 Path Preview",
            MassNavigationShowcaseStepId.OrderReuse => "U7 Route Reuse",
            MassNavigationShowcaseStepId.WaypointAuthoring => "U10 Waypoints",
            _ => Shorten(_guide.CurrentStep.Title, 24)
        };
    }

    private int ApplyLiveDiagnosticSerial(int serial)
    {
        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.TargetAllocation or MassNavigationShowcaseStepId.TenKFlow =>
                BuildTargetAllocationDiagnosticSerial(serial),
            MassNavigationShowcaseStepId.WorldHpa or MassNavigationShowcaseStepId.LargeWorldStreaming =>
                HashCode.Combine(
                    serial,
                    _simulation.AcceptanceDiagnostics.HpaMacro.SampleRouteChunkCount,
                    _simulation.AcceptanceDiagnostics.HpaMacro.SamplePortalCount,
                    _simulation.AcceptanceDiagnostics.HpaGraph.ActiveWindowChunkCount,
                    _simulation.AcceptanceDiagnostics.HpaGraph.LoadedTileCount),
            _ => serial
        };
    }

    private int BuildTargetAllocationDiagnosticSerial(int serial)
    {
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        var hash = new HashCode();
        hash.Add(serial);
        hash.Add(_simulation.SelectedCount);
        hash.Add(_simulation.LastCommandSelectionCount);
        hash.Add(allocation.SelectedCount);
        hash.Add(allocation.SlotCount);
        hash.Add(allocation.ReachableSlotCount);
        hash.Add(allocation.BlockedSlotCount);
        hash.Add(allocation.FallbackSlotCount);
        hash.Add(_simulation.NavGroupRuntime.PendingTargetRefreshCount);
        hash.Add(_simulation.MassFlow.CountUnitsWithTargets());
        return hash.ToHashCode();
    }

    private string BuildFocusedCompactSignal()
    {
        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.TenKFlow =>
                $"10k ok | moving {Math.Min(9999, _simulation.MassFlow.CountMovingUnits(0.0001f)):0000}+",
            MassNavigationShowcaseStepId.TargetAllocation =>
                BuildTargetAllocationCompactSignal(),
            MassNavigationShowcaseStepId.WorldHpa =>
                BuildHpaCompactSignal(),
            MassNavigationShowcaseStepId.PathOnly =>
                BuildPathOnlyCompactSignal(),
            MassNavigationShowcaseStepId.OrderReuse =>
                $"reuse={_simulation.AcceptanceDiagnostics.OrderReuse.CacheHit} route {_simulation.AcceptanceDiagnostics.OrderReuse.ReusedRouteId}",
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                $"waypoints {_simulation.AcceptanceDiagnostics.WaypointPath.WaypointCount} pathpoints {_simulation.AcceptanceDiagnostics.WaypointPath.PathPointCount}",
            _ => Shorten(BuildFocusedStatusLine(), 28)
        };
    }

    private string BuildPathOnlyCompactSignal()
    {
        MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        string source = query.RouteProvenance.Contains("NavMesh", StringComparison.Ordinal)
            ? "NavMesh"
            : Shorten(query.RouteProvenance, 14);
        return $"source={source}";
    }

    private string BuildPathOnlyOrderCompactSignal()
    {
        MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        return $"pathpoints={query.PathPointCount} noOrder={query.NoOrderSubmitted}";
    }

    private string BuildTargetAllocationCompactSignal()
    {
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        int selected = allocation.SelectedCount > 0 ? allocation.SelectedCount : _simulation.SelectedCount;
        return allocation.SlotCount >= 10_000 && allocation.ReachableSlotCount >= 10_000 && allocation.FallbackSlotCount == 0
            ? "10k->10k slots ok"
            : $"sel {selected} slots {allocation.SlotCount}";
    }

    private void RenderFocusedTargetAllocationStrip(
        ScreenOverlayBuffer overlay,
        int viewportWidth,
        int viewportHeight,
        int serial)
    {
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        int stripWidth = Math.Clamp((int)MathF.Round(viewportWidth * 0.62f), 640, 860);
        stripWidth = Math.Min(stripWidth, Math.Max(360, viewportWidth - 32));
        int stripHeight = 100;
        int x = Math.Clamp((viewportWidth - stripWidth) / 2, 12, Math.Max(12, viewportWidth - stripWidth - 12));
        int y = Math.Max(8, viewportHeight - stripHeight - 18);
        int selected = allocation.SelectedCount > 0 ? allocation.SelectedCount : _simulation.SelectedCount;
        int commanded = Math.Max(_simulation.LastCommandSelectionCount, selected);
        string pass = allocation.SlotCount >= 10_000 &&
            allocation.ReachableSlotCount >= 10_000 &&
            allocation.BlockedSlotCount == 0 &&
            allocation.FallbackSlotCount == 0
                ? "chain evidence ok"
                : "waiting for order";
        int shownSlots = Math.Min(MaxSlotMarkers, allocation.ActualTargetSampleCount);

        overlay.AddRect(x, y, stripWidth, stripHeight, PanelFill, PanelBorder, stableId: 44920, dirtySerial: serial);
        overlay.AddText(x + 12, y + 9, "U8 Target Allocation: Select 10k Army, then right-click one destination.", 12, Text, 44921, serial);
        overlay.AddText(x + 12, y + 29, $"Output: allocated={allocation.SlotCount}; reachable={allocation.ReachableSlotCount}; blocked={allocation.BlockedSlotCount}; noFallbackPathUsed={allocation.FallbackSlotCount == 0}.", 11, Good, 44922, serial);
        overlay.AddText(x + 12, y + 49, $"Debug: yellow footprint=full formation area; markers={shownSlots}/{allocation.SlotCount} real MassFlow targets.", 11, WaypointBorder, 44923, serial);
        overlay.AddText(x + 12, y + 69, "Why: the army spreads around the goal instead of stacking every unit on one point.", 11, WaypointBorder, 44924, serial);
        overlay.AddText(x + 12, y + 87, $"Chain: SelectionRuntime -> OrderBuffer -> NavGroupRuntime -> MassFlow; {pass}; routeId={allocation.AllocationRouteId}; commanded={commanded}.", 11, pass == "chain evidence ok" ? Good : Warn, 44925, serial);
    }

    private string BuildHpaCompactSignal()
    {
        MassNavigationHpaMacroDiagnostics hpa = _simulation.AcceptanceDiagnostics.HpaMacro;
        if (!hpa.Available)
        {
            return "global route unavailable";
        }

        MassNavigationHpaGraphAssetDiagnostics graph = _simulation.AcceptanceDiagnostics.HpaGraph;
        string window = graph.Available
            ? $" | window {graph.ActiveWindowMinChunkX},{graph.ActiveWindowMinChunkY}-{graph.ActiveWindowMaxChunkX},{graph.ActiveWindowMaxChunkY}"
            : string.Empty;
        return $"global {hpa.StartMacroChunkX},{hpa.StartMacroChunkY}->{hpa.GoalMacroChunkX},{hpa.GoalMacroChunkY} chunks {hpa.SampleRouteChunkCount}{window}";
    }

    private string BuildFocusedStatusLine()
    {
        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.WorldHpa => BuildHpaRouteLine(),
            MassNavigationShowcaseStepId.LargeWorldStreaming => BuildLargeWorldLine(),
            MassNavigationShowcaseStepId.NavMeshBake => BuildNavMeshSemanticLine(),
            MassNavigationShowcaseStepId.LayerAreaEditor => BuildLayerCostLine(),
            MassNavigationShowcaseStepId.LayerCosts => BuildLayerCostLine(),
            MassNavigationShowcaseStepId.TargetAllocation => BuildAllocationLine(),
            MassNavigationShowcaseStepId.TenKFlow => BuildTenKFlowLine(),
            MassNavigationShowcaseStepId.StaticObstacleWorld => BuildObstacleLine(),
            MassNavigationShowcaseStepId.WaypointAuthoring => BuildWaypointLine(),
            MassNavigationShowcaseStepId.BakeToolQuery => BuildRuntimeBakeUpdateLine(),
            _ => BuildStatusLine()
        };
    }

    private string BuildUseCaseBodyLine()
    {
        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts or
            MassNavigationShowcaseStepId.BakeToolQuery =>
                _guide.CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery
                    ? "runtime_navdata_authoring_update; game runtime draw/dirty/recast-bake/query"
                    : "interactive_editor_workbench; runtime view previews the same loaded bake data",
            MassNavigationShowcaseStepId.PerformanceDebug or
            MassNavigationShowcaseStepId.DebugVisualBudget =>
                "interactive_diagnostics_mod; play first, then inspect measured debug budget",
            _ =>
                "interactive_playable_mod; box-select/click/order in the Raylib window"
        };
    }

    private string BuildUserOperationLine()
    {
        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.PathOnly =>
                "click Pick Path Preview, left-click start, right-click goal; ground and minimap both work; verify this is a query only",
            MassNavigationShowcaseStepId.WorldHpa =>
                "click Pick HPA Route, left-click far start chunk, right-click far goal chunk",
            MassNavigationShowcaseStepId.StrategySwitch =>
                "click Pick Strategy Route, pick one start/goal pair, compare graph/navmesh/hybrid",
            MassNavigationShowcaseStepId.OrderReuse =>
                "click Select Reuse Squad, right-click the same point twice, then a nearby point",
            MassNavigationShowcaseStepId.TargetAllocation =>
                "box-select or click Select 10k Army, then right-click one destination",
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                "pick start/goal, click Edit Waypoint Plan, then move the editable waypoint",
            MassNavigationShowcaseStepId.TenKFlow =>
                "select 10k units, right-click one destination, watch shared command/slots/flow",
            MassNavigationShowcaseStepId.LargeWorldStreaming =>
                "switch Full Map/Field Camera and verify the active data window follows the world focus",
            MassNavigationShowcaseStepId.StaticObstacleWorld =>
                "open the 40k obstacle mod view and compare authored/baked/loaded/solver-active buckets",
            MassNavigationShowcaseStepId.BakeToolQuery =>
                "pick route endpoints, Draw Poly, left-click obstacle vertices, right-click or Close Poly, then Update NavData",
            MassNavigationShowcaseStepId.PerformanceDebug =>
                "play the scene with normal controls, then read renderer scope, p95/p99 and loaded-data flags",
            MassNavigationShowcaseStepId.DebugVisualBudget =>
                "toggle route/navmesh/HPA/slot layers and confirm the sampled overlay budget stays bounded",
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts =>
                "open the Raylib bake workbench; switch views, edit layer/area, query path, save patch/dirty chunks",
            _ => _guide.CurrentStep.PlayerInput
        };
    }

    private string BuildAcceptanceSignalLine()
    {
        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.PathOnly =>
                $"NavMesh route source visible, S/G persist, pathpoints>0, NoOrderSubmitted=true, orderDelta=0; current {BuildFocusedStatusLine()}",
            MassNavigationShowcaseStepId.WorldHpa =>
                "global macro route, active-window portal sample, start/goal chunks and loaded window are separately visible",
            MassNavigationShowcaseStepId.StrategySwitch =>
                "all candidates are produced from the same user-picked start/goal query",
            MassNavigationShowcaseStepId.OrderReuse =>
                "second same-point and near-point order reuse one route bucket/signature",
            MassNavigationShowcaseStepId.TargetAllocation =>
                "selected=10000, slots>=10000, reachable>=10000, blocked=0, fallback=0",
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                "waypoints remain editable; old pathpoints are invalidated and regenerated",
            MassNavigationShowcaseStepId.TenKFlow =>
                "10k selected units receive shared command targets and flow remains enabled",
            MassNavigationShowcaseStepId.StaticObstacleWorld =>
                "40k authored/baked/loaded obstacle data is separate from bounded solver-active subset",
            MassNavigationShowcaseStepId.BakeToolQuery =>
                "authored polygon, dirty chunks, runtime Recast baked tiles, changed mesh evidence and post-update path query are visible",
            MassNavigationShowcaseStepId.PerformanceDebug =>
                "Raylib timing evidence meets the configured FPS/frame budget for the measured scope",
            MassNavigationShowcaseStepId.DebugVisualBudget =>
                "diagnostics off has no trace/screenshot dump cost; diagnostics on stays sampled",
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts =>
                "workbench writes formal patch/dirty chunks/nav-bake diagnostics/result JSON from the tool chain",
            _ => _guide.CurrentStep.ReadablePassSignal
        };
    }

    private string BuildDataSourceLine()
    {
        MassNavigationNavMeshGuideSample nav = _guide.NavMeshSample;
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        string bakeSource = bake == null
            ? "bake diagnostics unavailable"
            : $"{bake.MacroChunkColumns}x{bake.MacroChunkRows} macro chunks, nav {bake.NavMesh.BakedChunks}/{bake.NavMesh.TotalChunks}";

        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts =>
                $"{ShortPath(nav.LogicHeightmapSource)} -> .ntil tile {nav.ChunkX},{nav.ChunkY}; {bakeSource}",
            MassNavigationShowcaseStepId.BakeToolQuery =>
                $"{ShortPath(nav.LogicHeightmapSource)} -> runtime Recast dirty chunks; {BuildRuntimeBakeUpdateLine()}",
            MassNavigationShowcaseStepId.WorldHpa or
            MassNavigationShowcaseStepId.LargeWorldStreaming =>
                $"HPA graph routeSource={_simulation.AcceptanceDiagnostics.HpaMacro.RouteSource}; {BuildLargeWorldLine()}",
            MassNavigationShowcaseStepId.TargetAllocation or
            MassNavigationShowcaseStepId.TenKFlow =>
                $"SelectionRuntime -> CommandBridge -> OrderBuffer -> OrderBridge -> NavGroupRuntime -> MassFlow; {BuildAllocationLine()}",
            MassNavigationShowcaseStepId.OrderReuse =>
                $"formal order cache: {BuildLayerLine()}",
            MassNavigationShowcaseStepId.StaticObstacleWorld =>
                $"static obstacle asset/runtime diagnostics: {BuildObstacleLine()}",
            _ =>
                _simulation.AcceptanceDiagnostics.PathOnlyQuery.QuerySource
        };
    }

    private void RenderFocusedHpaRouteInset(ScreenOverlayBuffer overlay, int x, int y, int width, int height, int serial)
    {
        MassNavigationHpaMacroDiagnostics hpa = _simulation.AcceptanceDiagnostics.HpaMacro;
        if (!hpa.Available || hpa.SampleRouteChunkCount <= 0)
        {
            overlay.AddText(x, y, "HPA route inset unavailable.", 12, Warn, 44920, serial);
            return;
        }

        MassNavigationHpaGraphAssetDiagnostics graph = _simulation.AcceptanceDiagnostics.HpaGraph;
        HpaRouteChunk[] globalRoute = ResolveMacroEstimateHpaRouteChunks(hpa);
        if (globalRoute.Length == 0)
        {
            overlay.AddText(x, y, "HPA route inset has no route chunks.", 12, Warn, 44921, serial);
            return;
        }

        int minX = Math.Min(hpa.StartMacroChunkX, hpa.GoalMacroChunkX);
        int maxX = Math.Max(hpa.StartMacroChunkX, hpa.GoalMacroChunkX);
        int minY = Math.Min(hpa.StartMacroChunkY, hpa.GoalMacroChunkY);
        int maxY = Math.Max(hpa.StartMacroChunkY, hpa.GoalMacroChunkY);
        int mapX = x + 4;
        int mapY = y + 44;
        int mapW = width - 8;
        int mapH = height - 68;
        int gridW = mapW;
        int gridH = Math.Max(36, mapH);
        int headingCharacters = Math.Clamp((width - 20) / 7, 28, 54);
        int footerCharacters = Math.Clamp((width - 20) / 7, 30, 58);

        overlay.AddRect(x, y, width, height, PanelFill, PanelBorder, 46019, serial);
        overlay.AddText(x + 10, y + 8, Shorten("Global HPA route: sampled 256x256 macro chunks", headingCharacters), 12, Text, 46021, serial);
        overlay.AddText(x + 10, y + 24, Shorten(BuildGlobalHpaRouteLine(), headingCharacters), 11, Good, 46022, serial);
        overlay.AddRect(mapX, mapY, gridW, gridH, new Vector4(0.02f, 0.04f, 0.06f, 0.64f), PanelBorder, 46023, serial);

        for (int i = 1; i < 4; i++)
        {
            int px = mapX + ((gridW * i) / 4);
            int py = mapY + ((gridH * i) / 4);
            overlay.AddLine(px, mapY, px, mapY + gridH, 1, new Vector4(0.24f, 0.42f, 0.52f, 0.42f), 46030 + i, serial);
            overlay.AddLine(mapX, py, mapX + gridW, py, 1, new Vector4(0.24f, 0.42f, 0.52f, 0.42f), 46040 + i, serial);
        }

        if (graph.Available)
        {
            int activeX = MapChunkXToInset(mapX, gridW, minX, maxX, graph.ActiveWindowMinChunkX);
            int activeY = MapChunkYToInset(mapY, gridH, minY, maxY, graph.ActiveWindowMinChunkY);
            int activeRight = MapChunkXToInset(mapX, gridW, minX, maxX, graph.ActiveWindowMaxChunkX);
            int activeBottom = MapChunkYToInset(mapY, gridH, minY, maxY, graph.ActiveWindowMaxChunkY);
            int activeW = Math.Max(8, activeRight - activeX + 5);
            int activeH = Math.Max(8, activeBottom - activeY + 5);
            overlay.AddRect(activeX, activeY, activeW, activeH, new Vector4(0.18f, 0.86f, 0.42f, 0.06f), Good, 46070, serial);
            overlay.AddText(
                mapX + 8,
                mapY + gridH - 18,
                $"Loaded window {graph.ActiveWindowMinChunkX},{graph.ActiveWindowMinChunkY}->{graph.ActiveWindowMaxChunkX},{graph.ActiveWindowMaxChunkY}",
                10,
                Good,
                46071,
                serial);
        }

        int[] indices = BuildHpaLabelIndices(globalRoute.Length, MaxHpaInsetRouteSamples);
        int previousX = 0;
        int previousY = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            int routeIndex = indices[i];
            int chunkX = globalRoute[routeIndex].X;
            int chunkY = globalRoute[routeIndex].Y;
            int centerX = MapChunkXToInset(mapX, gridW, minX, maxX, chunkX);
            int centerY = MapChunkYToInset(mapY, gridH, minY, maxY, chunkY);
            if (i > 0)
            {
                overlay.AddLine(previousX, previousY, centerX, centerY, 4, HpaRouteLineBorder, 46090 + i, serial);
            }

            overlay.AddRect(centerX - 5, centerY - 5, 10, 10, HpaCellFill, HpaCellBorder, 46240 + i, serial);
            string label = routeIndex == 0
                ? "S"
                : routeIndex == globalRoute.Length - 1
                    ? "G"
                    : (routeIndex + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            overlay.AddText(centerX + 7, centerY - 7, label, 10, routeIndex == 0 || routeIndex == globalRoute.Length - 1 ? Good : Text, 46320 + i, serial);

            previousX = centerX;
            previousY = centerY;
        }

        string portalSample = graph.ActiveWindowRouteAvailable
            ? $"Portal sample: loaded window route {graph.RouteStartChunkX},{graph.RouteStartChunkY}:p{graph.RouteStartPortalIndex}->{graph.RouteGoalChunkX},{graph.RouteGoalChunkY}:p{graph.RouteGoalPortalIndex}"
            : "Portal sample: active window graph route unavailable";
        overlay.AddText(x + 10, y + height - 18, Shorten(portalSample, footerCharacters), 10, PortalBorder, 46410, serial);
    }

    private void ResolveViewport(out int width, out int height)
    {
        if (_engine.GetService(CoreServiceKeys.ViewController) is IViewController view)
        {
            Vector2 resolution = view.Resolution;
            if (resolution.X > 0f && resolution.Y > 0f)
            {
                width = (int)MathF.Round(resolution.X);
                height = (int)MathF.Round(resolution.Y);
                return;
            }
        }

        width = _engine.MergedConfig?.WindowWidth > 0 ? _engine.MergedConfig.WindowWidth : 1280;
        height = _engine.MergedConfig?.WindowHeight > 0 ? _engine.MergedConfig.WindowHeight : 720;
    }

    private void RenderProjectedWorldLabels(ScreenOverlayBuffer overlay)
    {
        if (_engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
        {
            return;
        }

        int serial = ApplyLiveDiagnosticSerial(_guide.ActionRevision + ((int)_guide.CurrentStepId * 1000));
        switch (_guide.CurrentStepId)
        {
            case MassNavigationShowcaseStepId.VisualHeightmapBake:
            case MassNavigationShowcaseStepId.LogicHeightmapBake:
                RenderProjectedNavMeshLabels(overlay, projector, serial);
                RenderProjectedBakeFlowLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.LayerAreaEditor:
                RenderProjectedLayerCostLabels(overlay, projector, serial);
                RenderProjectedNavMeshLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.BakeToolQuery:
                RenderProjectedRuntimeBakeLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.WorldHpa:
            case MassNavigationShowcaseStepId.LargeWorldStreaming:
                RenderProjectedHpaLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.PathOnly:
            case MassNavigationShowcaseStepId.WaypointAuthoring:
                RenderProjectedPathLabels(overlay, projector, serial);
                RenderProjectedPathRoute(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.NavMeshBake:
                RenderProjectedNavMeshLabels(overlay, projector, serial);
                RenderProjectedPathLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.StrategySwitch:
                RenderProjectedStrategyLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.LayerCosts:
                RenderProjectedLayerCostLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.OrderReuse:
                RenderProjectedOrderReuseLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.TargetAllocation:
                RenderProjectedTargetAllocationLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.TenKFlow:
                RenderProjectedPathLabels(overlay, projector, serial);
                RenderProjectedPathRoute(overlay, projector, serial);
                RenderProjectedTargetAllocationLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.StaticObstacleWorld:
                RenderProjectedStaticObstacleLabels(overlay, projector, serial);
                RenderProjectedRuntimeBakeLabels(overlay, projector, serial);
                break;
            case MassNavigationShowcaseStepId.PerformanceDebug:
            case MassNavigationShowcaseStepId.DebugVisualBudget:
                RenderProjectedPerformanceLabels(overlay, projector, serial);
                break;
        }
    }

    private void RenderProjectedHpaLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        MassNavigationHpaMacroDiagnostics hpa = _simulation.AcceptanceDiagnostics.HpaMacro;
        if (!hpa.Available || hpa.SampleRouteChunkCount <= 0)
        {
            return;
        }

        MassNavigationHpaGraphAssetDiagnostics graph = _simulation.AcceptanceDiagnostics.HpaGraph;
        HpaRouteChunk[] globalRoute = ResolveMacroEstimateHpaRouteChunks(hpa);
        if (globalRoute.Length == 0)
        {
            return;
        }

        int[] indices = BuildHpaLabelIndices(globalRoute.Length, _guide.FocusedPanel ? MaxFocusedHpaRouteLabels : MaxHpaRouteLabels);
        for (int i = 0; i < indices.Length; i++)
        {
            int routeIndex = indices[i];
            int chunkX = globalRoute[routeIndex].X;
            int chunkY = globalRoute[routeIndex].Y;
            string role = routeIndex == 0
                ? "start"
                : routeIndex == globalRoute.Length - 1
                    ? "goal"
                    : "global route";
            Vector2 offset = role == "global route"
                ? new Vector2(0f, -620f)
                : Vector2.Zero;
            AddWorldLabel(
                overlay,
                projector,
                ChunkCenter(chunkX, chunkY) + offset,
                $"{routeIndex + 1:000} {role} chunk {chunkX},{chunkY}",
                45200 + i,
                serial,
                role == "start" || role == "goal" ? Good : HpaBorder);
        }

        HpaRouteChunk[] activeWindowRoute = ResolveGraphHpaRouteChunks(graph);
        RenderProjectedHpaPortalLabels(overlay, projector, activeWindowRoute, serial);

        if (graph.Available)
        {
            AddWorldLabel(
                overlay,
                projector,
                ChunkMin(graph.ActiveWindowMinChunkX, graph.ActiveWindowMinChunkY),
                $"loaded active window {graph.ActiveWindowMinChunkX},{graph.ActiveWindowMinChunkY}->{graph.ActiveWindowMaxChunkX},{graph.ActiveWindowMaxChunkY}",
                45250,
                serial,
                Good);
        }
    }

    private void RenderProjectedHpaPortalLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, HpaRouteChunk[] route, int serial)
    {
        if (route.Length < 2)
        {
            return;
        }

        int portalCount = Math.Min(MaxProjectedHpaPortalLabels, route.Length - 1);
        for (int i = 0; i < portalCount; i++)
        {
            float ratio = portalCount == 1 ? 0f : i / (float)(portalCount - 1);
            int stepIndex = Math.Clamp(1 + (int)MathF.Round(ratio * (route.Length - 2)), 1, route.Length - 1);
            HpaRouteChunk from = route[stepIndex - 1];
            HpaRouteChunk to = route[stepIndex];
            int fromX = from.X;
            int fromY = from.Y;
            int toX = to.X;
            int toY = to.Y;
            Vector2 portal = (ChunkCenter(fromX, fromY) + ChunkCenter(toX, toY)) * 0.5f;
            AddWorldLabel(
                overlay,
                projector,
                portal + new Vector2(0f, 680f),
                $"active-window portal {fromX},{fromY}{FormatHpaPortalSuffix(from)}->{toX},{toY}{FormatHpaPortalSuffix(to)}",
                45270 + i,
                serial,
                PortalBorder);
        }
    }

    private void RenderProjectedPathLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        Vector2 start = ResolveVisiblePathPreviewStart(query);
        Vector2 goal = ResolveVisiblePathPreviewGoal(query);
        if (_guide.HasPathPreviewStart || (query.Available && IsFinite(query.StartWorldCm)))
        {
            AddWorldLabel(overlay, projector, start, "S picked start", 45300, serial, PathBorder);
        }

        if (_guide.HasPathPreviewGoal || (query.Available && IsFinite(query.GoalWorldCm)))
        {
            AddWorldLabel(overlay, projector, goal, "G picked goal", 45301, serial, PathBorder);
        }

        Vector2 instructionAnchor = IsFinite(start) && IsFinite(goal)
            ? (start + goal) * 0.5f + new Vector2(0f, -3_200f)
            : IsFinite(start)
                ? start + new Vector2(0f, -3_200f)
                : ResolveGoal();
        AddWorldLabel(overlay, projector, instructionAnchor, "left=start; right=goal; ground or minimap; no order", 45305, serial, Warn);

        ReadOnlySpan<MassNavigationPathPointSample> pathPoints = _simulation.AcceptanceDiagnostics.PathOnlyPathPoints;
        if (pathPoints.Length > 0)
        {
            AddWorldLabel(
                overlay,
                projector,
                new Vector2(pathPoints[pathPoints.Length / 2].Xcm, pathPoints[pathPoints.Length / 2].Ycm),
                "pathpoints: immutable query result",
                45302,
                serial,
                PathBorder);
        }

        if (_guide.CurrentStepId == MassNavigationShowcaseStepId.WaypointAuthoring)
        {
            Vector2 waypointLabelPoint = query.StartWorldCm == Vector2.Zero || query.GoalWorldCm == Vector2.Zero
                ? ResolveGoal()
                : (query.StartWorldCm + query.GoalWorldCm) * 0.5f + new Vector2(0f, 4_500f);
            AddWorldLabel(overlay, projector, waypointLabelPoint, "waypoints: editable order intent", 45303, serial, WaypointBorder);
            AddWorldLabel(overlay, projector, waypointLabelPoint + new Vector2(0f, -9_000f), "old pathpoints invalidated -> regenerated", 45304, serial, BlockedBorder);
        }
    }

    private void RenderProjectedPathRoute(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        ReadOnlySpan<MassNavigationPathPointSample> pathPoints = _simulation.AcceptanceDiagnostics.PathOnlyPathPoints;
        if (pathPoints.Length < 2)
        {
            RenderProjectedPendingPathPickMarkers(overlay, projector, serial);
            MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
            if (!query.Available || !IsFinite(query.StartWorldCm) || !IsFinite(query.GoalWorldCm))
            {
                return;
            }

            AddProjectedRouteSegment(
                overlay,
                projector,
                query.StartWorldCm,
                query.GoalWorldCm,
                stableId: 46200,
                serial);
            return;
        }

        int max = Math.Min(pathPoints.Length, MaxPathPoints);
        for (int i = 0; i < max - 1; i++)
        {
            Vector2 a = new(pathPoints[i].Xcm, pathPoints[i].Ycm);
            Vector2 b = new(pathPoints[i + 1].Xcm, pathPoints[i + 1].Ycm);
            AddProjectedRouteSegment(overlay, projector, a, b, 46200 + i, serial);
        }

        for (int i = 0; i < max; i++)
        {
            Vector2 point = new(pathPoints[i].Xcm, pathPoints[i].Ycm);
            AddProjectedRouteNode(overlay, projector, point, 46300 + i, serial, i == 0 || i == max - 1);
        }
    }

    private void RenderProjectedPendingPathPickMarkers(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        if (_guide.HasPathPreviewStart)
        {
            AddProjectedRouteNode(overlay, projector, _guide.PathPreviewStartWorldCm, 46380, serial, endpoint: true);
        }

        if (_guide.HasPathPreviewGoal)
        {
            AddProjectedRouteNode(overlay, projector, _guide.PathPreviewGoalWorldCm, 46381, serial, endpoint: true);
        }
    }

    private void RenderProjectedBakeFlowLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        MassNavigationNavMeshGuideSample sample = _guide.NavMeshSample;
        if (!sample.Available)
        {
            return;
        }

        Vector2 tileCenter = ChunkCenter(sample.ChunkX, sample.ChunkY);
        AddWorldLabel(overlay, projector, tileCenter + new Vector2(-6_000f, 0f), "source -> LogicHeightmap", 45350, serial, Good);
        AddWorldLabel(overlay, projector, tileCenter + new Vector2(0f, -5_600f), "LogicHeightmap -> .ntil tile", 45351, serial, WaypointBorder);
        AddWorldLabel(overlay, projector, tileCenter + new Vector2(6_000f, 0f), "tile -> query/portal graph", 45352, serial, PortalBorder);
    }

    private void RenderProjectedNavMeshLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        MassNavigationNavMeshGuideSample sample = _guide.NavMeshSample;
        if (!sample.Available)
        {
            return;
        }

        Vector2 tileCenter = ChunkCenter(sample.ChunkX, sample.ChunkY);
        AddWorldLabel(overlay, projector, tileCenter, $"agent radius {sample.AgentRadiusCm}cm (scaled ring)", 45400, serial, WaypointBorder);
        AddWorldLabel(overlay, projector, tileCenter + new Vector2(-2_300f, -2_300f), $"walkable triangle edges x{sample.TriangleCount}", 45401, serial, Good);
        AddWorldLabel(overlay, projector, tileCenter + new Vector2(-4_200f, 3_300f), $"blocked cells {sample.BlockedCellCount}", 45402, serial, BlockedBorder);
        AddWorldLabel(overlay, projector, tileCenter + new Vector2(4_100f, -3_000f), $"high-cost cells {sample.HighCostCellCount}", 45403, serial, Warn);
        AddWorldLabel(overlay, projector, tileCenter + new Vector2(3_600f, 3_600f), $"border portals; off-mesh: {sample.OffMeshLinkSource}", 45404, serial, PortalBorder);
        AddWorldLabel(overlay, projector, tileCenter + new Vector2(0f, 4_800f), "cyan corridor + orange portals come from the path query", 45405, serial, PathBorder);

        int portalLabels = Math.Min(MaxProjectedPortalLabels, sample.Portals.Length);
        for (int i = 0; i < portalLabels; i++)
        {
            MassNavigationGuideSegment portal = sample.Portals[i];
            Vector2 mid = (new Vector2(portal.Axcm, portal.Aycm) + new Vector2(portal.Bxcm, portal.Bycm)) * 0.5f;
            AddWorldLabel(overlay, projector, mid, $"portal clearance {portal.ClearanceCm}cm", 45410 + i, serial, PortalBorder);
        }

        int edgeLabels = Math.Min(MaxProjectedNavMeshEdgeLabels, sample.TriangleEdges.Length);
        for (int i = 0; i < edgeLabels; i++)
        {
            if (i % 2 != 0)
            {
                continue;
            }

            MassNavigationGuideSegment edge = sample.TriangleEdges[i];
            Vector2 mid = (new Vector2(edge.Axcm, edge.Aycm) + new Vector2(edge.Bxcm, edge.Bycm)) * 0.5f;
            AddWorldLabel(overlay, projector, mid, $"walkable edge area {edge.AreaId}", 45430 + i, serial, Muted);
        }
    }

    private void RenderProjectedStrategyLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        ReadOnlySpan<MassNavigationPathPointSample> points = _simulation.AcceptanceDiagnostics.PathOnlyPathPoints;
        if (points.Length == 0)
        {
            return;
        }

        Vector2 mid = new(points[points.Length / 2].Xcm, points[points.Length / 2].Ycm);
        AddWorldLabel(overlay, projector, mid + new Vector2(-420f, -240f), "Road graph candidate", 45500, serial, new Vector4(0.44f, 0.76f, 1f, 1f));
        AddWorldLabel(overlay, projector, mid + new Vector2(380f, 210f), "NavMesh candidate", 45501, serial, Good);
        AddWorldLabel(overlay, projector, mid + new Vector2(0f, 520f), "Hybrid selected candidate", 45502, serial, WaypointBorder);
    }

    private void RenderProjectedLayerCostLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        Vector2 center = new(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
        float w = MathF.Max(3_000f, _simulation.SolverWindowWidthCm);
        float h = MathF.Max(3_000f, _simulation.SolverWindowHeightCm);
        AddWorldLabel(overlay, projector, center + new Vector2(-w * 0.30f, -h * 0.15f), "Ground layer/cost", 45600, serial, Good);
        AddWorldLabel(overlay, projector, center + new Vector2(w * 0.24f, -h * 0.18f), "Water/Naval layer", 45601, serial, new Vector4(0.40f, 0.76f, 1f, 1f));
        AddWorldLabel(overlay, projector, center + new Vector2(-w * 0.10f, h * 0.30f), "Mountain/high-cost area", 45602, serial, Warn);
        AddWorldLabel(overlay, projector, center + new Vector2(w * 0.18f, h * 0.22f), "Blocked/NoFly area", 45603, serial, BlockedBorder);
        AddWorldLabel(overlay, projector, center + new Vector2(-w * 0.34f, h * 0.24f), "Air layer: high traversal, blocked by NoFly", 45604, serial, AirBorder);
    }

    private void RenderProjectedOrderReuseLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        Vector2 goal = ResolveGoal();
        MassNavigationOrderReuseDiagnostics reuse = _simulation.AcceptanceDiagnostics.OrderReuse;
        AddWorldLabel(overlay, projector, goal, $"reuse bucket routeId={reuse.ReusedRouteId}", 45700, serial, WaypointBorder);
        AddWorldLabel(overlay, projector, goal + new Vector2(100f, 80f), $"near order scope={reuse.ReuseScope}", 45701, serial, WaypointBorder);
    }

    private void RenderProjectedTargetAllocationLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        Vector2 destination = allocation.HasAllocation ? allocation.DestinationWorldCm : ResolveGoal();
        int selected = allocation.SelectedCount > 0 ? allocation.SelectedCount : _simulation.SelectedCount;
        int shownSlots = Math.Min(MaxSlotMarkers, allocation.ActualTargetSampleCount);
        AddWorldLabel(overlay, projector, destination, "right-click goal -> footprint", 45800, serial, Warn, new Vector2(0f, -388f));
        AddWorldLabel(overlay, projector, destination, $"shown {shownSlots}/{allocation.SlotCount} real MassFlow targets", 45801, serial, Good, new Vector2(0f, -356f));
        AddWorldLabel(overlay, projector, destination, $"allocated {allocation.SlotCount}, reachable {allocation.ReachableSlotCount}, no fallback", 45802, serial, allocation.BlockedSlotCount == 0 && allocation.FallbackSlotCount == 0 ? Good : BlockedBorder, new Vector2(0f, -324f));
    }

    private void RenderProjectedPerformanceLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        AddWorldLabel(
            overlay,
            projector,
            new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm),
            "debug overlays sampled and bounded; FPS scope is smoke",
            45900,
            serial,
            Good);
    }

    private void RenderProjectedStaticObstacleLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        Vector2 center = new(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
        AddWorldLabel(overlay, projector, center, "active solver obstacle window", 45950, serial, Good);
        AddWorldLabel(overlay, projector, center + new Vector2(-10_000f, 8_000f), "40k authored/baked/loaded world buckets", 45951, serial, WaypointBorder);
        AddWorldLabel(overlay, projector, center + new Vector2(10_000f, -8_000f), $"bright green crosses: solver active={_simulation.AcceptanceDiagnostics.Obstacles.SolverActiveStaticObstacleCount}/{_simulation.AcceptanceDiagnostics.Obstacles.SolverStaticObstacleCapacity}", 45952, serial, Warn);
    }

    private void RenderProjectedRuntimeBakeLabels(ScreenOverlayBuffer overlay, IScreenProjector projector, int serial)
    {
        MassNavigationRuntimeBakeAuthoringRuntime authoring = _guide.RuntimeBakeAuthoring;
        MassNavigationRuntimeNavDataUpdateDiagnostics update = _simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate;
        bool hasRuntimeUpdate = update.NavDataRevision > 0 ||
            update.BakedTileCount > 0 ||
            update.ChangedTileCount > 0;
        IReadOnlyList<MassNavigationRuntimeAuthoredObstaclePolygon> polygons = authoring.AuthoredPolygons;
        if (polygons.Count > 0)
        {
            Vector2 centroid = ResolvePolygonCentroid(polygons[polygons.Count - 1].PointsWorldCm);
            AddWorldLabel(
                overlay,
                projector,
                centroid,
                $"runtime obstacle polygons={authoring.AuthoredPolygonCount}",
                45970,
                serial,
                RuntimeObstacleBorder);
        }
        else if (authoring.DraftPointCount > 0)
        {
            AddWorldLabel(
                overlay,
                projector,
                ResolvePolygonCentroid(authoring.DraftPoints),
                $"draft polygon points={authoring.DraftPointCount}",
                45970,
                serial,
                RuntimeObstacleBorder);
        }

        IReadOnlyList<MassNavigationRuntimeDirtyChunk> dirtyChunks = authoring.DirtyChunks;
        if (dirtyChunks.Count > 0)
        {
            MassNavigationRuntimeDirtyChunk chunk = dirtyChunks[dirtyChunks.Count - 1];
            AddWorldLabel(
                overlay,
                projector,
                chunk.HasWorldBounds ? chunk.CenterWorldCm : ChunkCenter(chunk.X, chunk.Y),
                $"dirty chunks={authoring.DirtyChunkCount}; navDataRev={Math.Max(authoring.UpdateRevision, update.NavDataRevision)}",
                45971,
                serial,
                RuntimeDirtyChunkBorder);
        }

        if (hasRuntimeUpdate)
        {
            Vector2 anchor = new(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
            AddWorldLabel(
                overlay,
                projector,
                anchor + new Vector2(0f, -9_000f),
                $"baked={update.BakedTileCount} changed={update.ChangedTileCount} tris={update.BeforeTriangleCount}->{update.AfterTriangleCount}",
                45972,
                serial,
                Good);
            AddWorldLabel(
                overlay,
                projector,
                anchor + new Vector2(0f, -7_900f),
                $"source={Shorten(update.UpdateSource, 42)}",
                45973,
                serial,
                Good);
        }
    }

    private static Vector2 ResolvePolygonCentroid(IReadOnlyList<Vector2> points)
    {
        if (points.Count == 0)
        {
            return Vector2.Zero;
        }

        Vector2 sum = Vector2.Zero;
        for (int i = 0; i < points.Count; i++)
        {
            sum += points[i];
        }

        return sum / points.Count;
    }

    private string BuildStatusLine()
    {
        MassNavigationPathOnlyQueryDiagnostics path = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        MassNavigationHpaMacroDiagnostics hpa = _simulation.AcceptanceDiagnostics.HpaMacro;
        return $"Path status={path.Status} pathpoints={path.PathPointCount} portals={path.CorridorPortalCount} noOrder={path.NoOrderSubmitted} | HPA chunks={hpa.SampleRouteChunkCount} portals={hpa.SamplePortalCount} | slots={allocation.SlotCount}/{allocation.ReachableSlotCount}";
    }

    private Vector2 ResolveVisiblePathPreviewStart(MassNavigationPathOnlyQueryDiagnostics query)
    {
        if (_guide.HasPathPreviewStart)
        {
            return _guide.PathPreviewStartWorldCm;
        }

        return query.StartWorldCm;
    }

    private Vector2 ResolveVisiblePathPreviewGoal(MassNavigationPathOnlyQueryDiagnostics query)
    {
        if (_guide.HasPathPreviewGoal)
        {
            return _guide.PathPreviewGoalWorldCm;
        }

        return query.GoalWorldCm;
    }

    private string BuildLayerLine()
    {
        MassNavigationNavMeshGuideSample nav = _guide.NavMeshSample;
        MassNavigationOrderReuseDiagnostics reuse = _simulation.AcceptanceDiagnostics.OrderReuse;
        return $"NavMesh tile={nav.ChunkX},{nav.ChunkY} layer={nav.Layer} tris={nav.TriangleCount} portals={nav.PortalCount} clearance={nav.MinPortalClearanceCm}cm radius={nav.AgentRadiusCm}cm | reuseHit={reuse.CacheHit} scope={reuse.ReuseScope} routeId={reuse.ReusedRouteId}";
    }

    private string BuildStepSpecificLine()
    {
        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.VisualHeightmapBake => BuildBakeSourceLine("VisualHeightmap"),
            MassNavigationShowcaseStepId.LogicHeightmapBake => BuildBakeSourceLine("LogicHeightmap"),
            MassNavigationShowcaseStepId.LayerAreaEditor => BuildLayerCostLine(),
            MassNavigationShowcaseStepId.WorldHpa => BuildHpaRouteLine(),
            MassNavigationShowcaseStepId.LargeWorldStreaming => BuildLargeWorldLine(),
            MassNavigationShowcaseStepId.NavMeshBake => BuildNavMeshSemanticLine(),
            MassNavigationShowcaseStepId.LayerCosts => BuildLayerCostLine(),
            MassNavigationShowcaseStepId.TargetAllocation => BuildAllocationLine(),
            MassNavigationShowcaseStepId.TenKFlow => BuildTenKFlowLine(),
            MassNavigationShowcaseStepId.StaticObstacleWorld => BuildObstacleLine(),
            MassNavigationShowcaseStepId.WaypointAuthoring => "Waypoints are authored/editable; pathpoints are immutable query output and are regenerated after waypoint edits.",
            MassNavigationShowcaseStepId.PerformanceDebug => $"Ground overlay items are sampled: slot ticks<={MaxSlotMarkers}; Raylib benchmark is the production FPS/debug-budget gate.",
            MassNavigationShowcaseStepId.DebugVisualBudget => $"Debug layers are sampled: pathpoints<={MaxPathPoints}, slot ticks<={MaxSlotMarkers}, obstacle buckets<={MaxObstacleBucketMarkers}.",
            MassNavigationShowcaseStepId.BakeToolQuery => BuildRuntimeBakeUpdateLine(),
            _ => $"Route source={_simulation.AcceptanceDiagnostics.PathOnlyQuery.RouteProvenance}; strategy={_simulation.AcceptanceDiagnostics.PathOnlyQuery.Strategy}"
        };
    }

    private string BuildBakeSourceLine(string sourceName)
    {
        MassNavigationNavMeshGuideSample nav = _guide.NavMeshSample;
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        string macro = bake != null
            ? $"{bake.MacroChunkColumns}x{bake.MacroChunkRows} chunks"
            : "macro chunks unavailable";
        return $"{sourceName} normalizes to {ShortPath(nav.LogicHeightmapSource)}; active tile={nav.ChunkX},{nav.ChunkY}; {macro}; triangles={nav.TriangleCount}; portals={nav.PortalCount}.";
    }

    private string BuildHpaRouteLine()
    {
        MassNavigationHpaMacroDiagnostics hpa = _simulation.AcceptanceDiagnostics.HpaMacro;
        if (!hpa.Available)
        {
            return "HPA route unavailable.";
        }

        string global = BuildHpaRouteChunkText(hpa, MaxHpaRouteLabels);
        string activeWindow = BuildActiveWindowHpaSampleLine(_simulation.AcceptanceDiagnostics.HpaGraph);
        return $"HPA chunks: global route {global}; {activeWindow}; source={hpa.RouteSource}; gate={hpa.ProductionGap}";
    }

    private string BuildGlobalHpaRouteLine()
    {
        MassNavigationHpaMacroDiagnostics hpa = _simulation.AcceptanceDiagnostics.HpaMacro;
        if (!hpa.Available)
        {
            return "Global route unavailable.";
        }

        return $"Global route {hpa.StartMacroChunkX},{hpa.StartMacroChunkY}->{hpa.GoalMacroChunkX},{hpa.GoalMacroChunkY}; chunks={hpa.SampleRouteChunkCount}; expanded={hpa.SampleExpandedChunkCount}; portals={hpa.SamplePortalCount}";
    }

    private static string BuildActiveWindowHpaSampleLine(MassNavigationHpaGraphAssetDiagnostics graph)
    {
        if (!graph.Available)
        {
            return "active window unavailable";
        }

        string window = $"active window {graph.ActiveWindowMinChunkX},{graph.ActiveWindowMinChunkY}->{graph.ActiveWindowMaxChunkX},{graph.ActiveWindowMaxChunkY}";
        if (!graph.ActiveWindowRouteAvailable)
        {
            return $"{window}; portal sample unavailable";
        }

        return $"{window}; portal sample {graph.RouteStartChunkX},{graph.RouteStartChunkY}:p{graph.RouteStartPortalIndex}->{graph.RouteGoalChunkX},{graph.RouteGoalChunkY}:p{graph.RouteGoalPortalIndex}";
    }

    private string BuildNavMeshSemanticLine()
    {
        MassNavigationNavMeshGuideSample nav = _guide.NavMeshSample;
        return $"Logic source={ShortPath(nav.LogicHeightmapSource)}; navmeshEdges={_guide.ActiveWindowNavMeshEdges.Length}; blocked={nav.BlockedCellCount}; highCost={nav.HighCostCellCount}; water={nav.WaterCellCount}; ramp={nav.RampCellCount}; borderPortals={nav.PortalCount}; authoredOffMesh={nav.OffMeshLinkSource}";
    }

    private string BuildLayerCostLine()
    {
        MassNavigationNavMeshGuideSample nav = _guide.NavMeshSample;
        return $"Layers={nav.LayerLegend}; Areas={nav.AreaLegend}";
    }

    private string BuildAllocationLine()
    {
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        MassNavigationGroupRuntime groups = _simulation.NavGroupRuntime;
        int selected = allocation.SelectedCount > 0 ? allocation.SelectedCount : _simulation.SelectedCount;
        return $"Allocation selected={selected} slots={allocation.SlotCount} reachable={allocation.ReachableSlotCount} blocked={allocation.BlockedSlotCount} noFallbackPathUsed={allocation.FallbackSlotCount == 0} targetRefresh={groups.AppliedTargetRefreshCountFrame}/{groups.PendingTargetRefreshCount}+pending routeId={allocation.AllocationRouteId}";
    }

    private string BuildLargeWorldLine()
    {
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        MassNavigationHpaGraphAssetDiagnostics graph = _simulation.AcceptanceDiagnostics.HpaGraph;
        if (bake == null)
        {
            return "Large-world bake diagnostics unavailable.";
        }

        return $"World={_simulation.WorldWidthCm / 100000f:F0}km x {_simulation.WorldHeightCm / 100000f:F0}km; macro={bake.MacroChunkColumns}x{bake.MacroChunkRows}; {BuildNavMeshCoverageLine()}; hpaActiveWindow={graph.LoadedTileCount}/{graph.ActiveWindowChunkCount}.";
    }

    private string BuildTenKFlowLine()
    {
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        MassNavigationGroupRuntime groups = _simulation.NavGroupRuntime;
        int commanded = _simulation.MassFlow.CountUnitsWithTargets();
        int moving = _simulation.MassFlow.CountMovingUnits(0.0001f);
        int settled = _simulation.MassFlow.SettledUnitCount;
        int stuck = _simulation.MassFlow.CountStuckUnits();
        int waiting = _simulation.MassFlow.CountTargetedIdleUnits(0.0001f);
        return $"10k flow selected={allocation.SelectedCount}; commanded={commanded}; slots={allocation.SlotCount}; pendingTargetRefresh={groups.PendingTargetRefreshCount}; moving/settled/stuck/waiting={moving}/{settled}/{stuck}/{waiting}; blocked/fallback={allocation.BlockedSlotCount}/{allocation.FallbackSlotCount}; flow={_simulation.FlowTuning.Enabled}.";
    }

    private string BuildObstacleLine()
    {
        MassNavigationObstacleDiagnostics obstacle = _simulation.AcceptanceDiagnostics.Obstacles;
        MassNavigationStaticObstacleWorldDiagnostics world = _simulation.AcceptanceDiagnostics.StaticObstacleWorld;
        return $"Obstacles target/authored/baked/loaded/solver={obstacle.TargetStaticObstacleCount}/{obstacle.AuthoredStaticObstacleCount}/{obstacle.BakedStaticObstacleCount}/{obstacle.LoadedStaticObstacleCount}/{obstacle.SolverActiveStaticObstacleCount}; buckets={world.MacroChunkCoverageCount}; activation={world.RuntimeActivationStrategy}.";
    }

    private string BuildRuntimeBakeUpdateLine()
    {
        MassNavigationRuntimeBakeAuthoringRuntime authoring = _guide.RuntimeBakeAuthoring;
        MassNavigationRuntimeNavDataUpdateDiagnostics update = _simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate;
        int revision = Math.Max(authoring.UpdateRevision, update.NavDataRevision);
        return $"Runtime bake update baked={update.BakedTileCount} changed={update.ChangedTileCount} triangles={update.BeforeTriangleCount}->{update.AfterTriangleCount} polygons={authoring.AuthoredPolygonCount} dirtyChunks={authoring.DirtyChunkCount} revision={revision} query={update.QueryStatusAfterUpdate}/{update.QueryPathPointCount}; {BuildNavMeshCoverageLine()} source={update.UpdateSource}.";
    }

    private string BuildRuntimeBakeResultCompactLine()
    {
        MassNavigationRuntimeNavDataUpdateDiagnostics update = _simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate;
        MassNavigationNavMeshCoverageGuide coverage = _guide.NavMeshCoverage;
        string scope = coverage.Available
            ? $"{coverage.TargetChunkCount}/{coverage.WorldChunkCount} {(coverage.IsPartialCoverage ? "active-window only" : "full-world")}"
            : "coverage missing";
        return $"baked={update.BakedTileCount} changed={update.ChangedTileCount} mesh={_guide.ActiveWindowNavMeshEdges.Length} {scope}";
    }

    private string BuildNavMeshCoverageLine()
    {
        MassNavigationNavMeshCoverageGuide coverage = _guide.NavMeshCoverage;
        if (!coverage.Available)
        {
            return "navmesh coverage unavailable";
        }

        string percent = FormatCoveragePercent(coverage.TargetChunkCount, coverage.WorldChunkCount);
        string scope = coverage.IsPartialCoverage ? "active-window only" : "full-world";
        string window = coverage.ActiveWindowChunkCount > 0
            ? $"window={coverage.ActiveWindowMinChunkX},{coverage.ActiveWindowMinChunkY}->{coverage.ActiveWindowMaxChunkX},{coverage.ActiveWindowMaxChunkY}"
            : "window=missing";
        return $"navmeshCoverage={coverage.TargetChunkCount}/{coverage.WorldChunkCount} ({percent}) {scope}; {window}; bakedTiles={coverage.TotalBakedTiles}/{coverage.TotalExpectedTileBakes}; notLoadedWorldChunks={coverage.NotLoadedWorldChunkCount}";
    }

    private static string FormatCoveragePercent(int bakedChunks, int worldChunks)
    {
        if (worldChunks <= 0)
        {
            return "0%";
        }

        float ratio = bakedChunks * 100f / worldChunks;
        if (ratio > 0f && ratio < 1f)
        {
            return "<1%";
        }

        return $"{MathF.Round(ratio):0}%";
    }

    private string BuildWaypointLine()
    {
        MassNavigationWaypointPathDiagnostics waypoint = _simulation.AcceptanceDiagnostics.WaypointPath;
        return $"Waypoint plan authored={waypoint.HasAuthoredPlan}; waypoints={waypoint.WaypointCount}; pathpoints={waypoint.PathPointCount}; invalidatedOldPathpoints={waypoint.InvalidatedPathPointCount}; editRevision={waypoint.EditRevision}; state={waypoint.EditState}.";
    }

    private void RenderLegendOverlay(ScreenOverlayBuffer overlay, int x, int y, int serial)
    {
        int width = 760;
        int height = _guide.CurrentStepId == MassNavigationShowcaseStepId.NavMeshBake ||
            _guide.CurrentStepId == MassNavigationShowcaseStepId.LayerCosts ||
            _guide.CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery
                ? 138
                : _guide.CurrentStepId == MassNavigationShowcaseStepId.PathOnly ||
                    _guide.CurrentStepId == MassNavigationShowcaseStepId.WaypointAuthoring
                    ? 102
                : 82;
        overlay.AddRect(x, y, width, height, PanelFill, PanelBorder, stableId: 45100, dirtySerial: serial);
        overlay.AddText(x + 14, y + 10, "Debug Presentation Legend", 14, Text, 45101, serial);
        switch (_guide.CurrentStepId)
        {
            case MassNavigationShowcaseStepId.VisualHeightmapBake:
            case MassNavigationShowcaseStepId.LogicHeightmapBake:
                overlay.AddText(x + 14, y + 34, BuildBakeSourceLine(_guide.CurrentStepId == MassNavigationShowcaseStepId.VisualHeightmapBake ? "VisualHeightmap" : "LogicHeightmap"), 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, "Bake chain: source -> LogicHeightmap (.lhtm) -> NavMesh tile (.ntil) -> query/corridor/portal/HPA diagnostics.", 12, Muted, 45103, serial);
                break;
            case MassNavigationShowcaseStepId.LayerAreaEditor:
                overlay.AddText(x + 14, y + 34, "Layer editor: ground/water/mountain/NoFly regions are the bake source for profile costs and blocked masks.", 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, BuildLayerCostLine(), 12, Muted, 45103, serial);
                break;
            case MassNavigationShowcaseStepId.WorldHpa:
            case MassNavigationShowcaseStepId.LargeWorldStreaming:
                overlay.AddText(x + 14, y + 34, BuildHpaRouteLine(), 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, "Purple=sampled global macro route; green=loaded active window; orange=portal sample inside loaded data.", 12, Muted, 45103, serial);
                break;
            case MassNavigationShowcaseStepId.NavMeshBake:
                overlay.AddText(x + 14, y + 34, "Walkable=green/cyan triangle edges  Non-walkable=blocked source count  High-cost=area ids 2/3/5/6  Portal=orange segment  Agent radius=yellow circle", 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, BuildNavMeshSemanticLine(), 12, Muted, 45103, serial);
                overlay.AddText(x + 14, y + 74, $"Portal clearance min={_guide.NavMeshSample.MinPortalClearanceCm}cm vs agent radius={_guide.NavMeshSample.AgentRadiusCm}cm; cyan corridor/orange portals are overlaid here from the path query.", 12, Muted, 45104, serial);
                overlay.AddText(x + 14, y + 94, $"Border portals are loaded from .ntil; authored off-mesh links: {_guide.NavMeshSample.OffMeshLinkSource}", 12, Muted, 45105, serial);
                break;
            case MassNavigationShowcaseStepId.LayerCosts:
                overlay.AddText(x + 14, y + 34, "Layer colors: ground green, water blue, air/profile data in rows, mountain/high-cost gold, blocked/NoFly red.", 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, BuildLayerCostLine(), 12, Muted, 45103, serial);
                overlay.AddText(x + 14, y + 74, BuildNavMeshSemanticLine(), 12, Muted, 45104, serial);
                overlay.AddText(x + 14, y + 94, "Different unit layers consume different profile/layer/cost rows without changing the player order model.", 12, Muted, 45105, serial);
                break;
            case MassNavigationShowcaseStepId.TenKFlow:
                overlay.AddText(x + 14, y + 34, "10k flow: gold slots are sampled target allocation; cyan path/flow remains separate from the player waypoint order.", 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, BuildTenKFlowLine(), 12, Muted, 45103, serial);
                break;
            case MassNavigationShowcaseStepId.TargetAllocation:
                overlay.AddText(x + 14, y + 34, "Target allocation: one RTS destination expands into 10k logical slots; the debug view samples only the visible markers.", 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, BuildAllocationLine(), 12, Muted, 45103, serial);
                break;
            case MassNavigationShowcaseStepId.StaticObstacleWorld:
                overlay.AddText(x + 14, y + 34, "Obstacle chain: authored world buckets, baked data, loaded data, and solver-active subset are separate counters.", 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, BuildObstacleLine(), 12, Muted, 45103, serial);
                break;
            case MassNavigationShowcaseStepId.PathOnly:
            case MassNavigationShowcaseStepId.WaypointAuthoring:
                overlay.AddText(x + 14, y + 34, "Pick endpoints directly or on minimap: left=start, right=goal. U4 queries NavMesh only and does not move units.", 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, $"source={_simulation.AcceptanceDiagnostics.PathOnlyQuery.RouteProvenance}; NoOrderSubmitted={_simulation.AcceptanceDiagnostics.PathOnlyQuery.NoOrderSubmitted}; orderDelta={_guide.LastActionOrderDelta}; pathpoints={_simulation.AcceptanceDiagnostics.PathOnlyQuery.PathPointCount}", 12, Muted, 45103, serial);
                overlay.AddText(x + 14, y + 74, _guide.CurrentStepId == MassNavigationShowcaseStepId.WaypointAuthoring ? BuildWaypointLine() : "S/G markers persist while the camera moves; cyan line is the immutable navmesh pathpoint result.", 12, Muted, 45104, serial);
                break;
            case MassNavigationShowcaseStepId.BakeToolQuery:
                overlay.AddText(x + 14, y + 34, "Runtime bake/update: draw polygon -> dirty chunks -> Recast tile bake -> NavMesh/query refresh.", 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 114, "Cyan mesh shows sampled loaded NavMesh plus dirty runtime tiles; coverage frame comes from bake diagnostics.", 12, Warn, 45106, serial);
                overlay.AddText(x + 14, y + 54, BuildBakeSourceLine("vtxm/vhtm/lhtm"), 12, Muted, 45103, serial);
                overlay.AddText(x + 14, y + 74, BuildNavMeshCoverageLine(), 12, Warn, 45104, serial);
                overlay.AddText(x + 14, y + 94, BuildRuntimeBakeUpdateLine(), 12, Muted, 45105, serial);
                break;
            case MassNavigationShowcaseStepId.DebugVisualBudget:
                overlay.AddText(x + 14, y + 34, _guide.CurrentStep.DebugLegend, 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, BuildStepSpecificLine(), 12, Muted, 45103, serial);
                break;
            default:
                overlay.AddText(x + 14, y + 34, _guide.CurrentStep.DebugLegend, 12, Good, 45102, serial);
                overlay.AddText(x + 14, y + 54, _guide.CurrentStep.ReadablePassSignal, 12, Muted, 45103, serial);
                break;
        }
    }

    private void DrawPathOnly(GroundOverlayBuffer ground, PathDebugVisualMode visualMode)
    {
        ReadOnlySpan<MassNavigationPathPointSample> pathPoints = _simulation.AcceptanceDiagnostics.PathOnlyPathPoints;
        if (pathPoints.Length < 2)
        {
            MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
            if (visualMode != PathDebugVisualMode.RouteOnly)
            {
                DrawHeightSampledLine(ground, query.StartWorldCm, query.GoalWorldCm, PathWidthCm, PathFill, PathBorder);
            }

            DrawRouteOnlyEndpointMarkers(ground, query.StartWorldCm, query.GoalWorldCm);
            DrawPendingPathPreviewEndpointMarkers(ground);
            return;
        }

        int max = Math.Min(pathPoints.Length, MaxPathPoints);
        Span<Vector2> points = stackalloc Vector2[max];
        for (int i = 0; i < max; i++)
        {
            points[i] = new Vector2(pathPoints[i].Xcm, pathPoints[i].Ycm);
        }

        for (int i = 0; i < max - 1; i++)
        {
            if (visualMode != PathDebugVisualMode.RouteOnly)
            {
                DrawHeightSampledLine(ground, points[i], points[i + 1], CorridorWidthCm, CorridorFill, CorridorBorder);
            }

            float pathWidthCm = visualMode == PathDebugVisualMode.RouteOnly ? RouteOnlyPathWidthCm : PathWidthCm;
            Vector4 pathFill = visualMode == PathDebugVisualMode.RouteOnly ? RouteOnlyPathFill : PathFill;
            Vector4 pathBorder = visualMode == PathDebugVisualMode.RouteOnly ? RouteOnlyPathBorder : PathBorder;
            DrawHeightSampledLine(ground, points[i], points[i + 1], pathWidthCm, pathFill, pathBorder);
        }

        if (visualMode == PathDebugVisualMode.RouteOnly)
        {
            DrawRouteOnlyPathPointMarkers(ground, points);
        }
        else
        {
            for (int i = 0; i < max; i++)
            {
                DrawCircle(ground, points[i], radiusCm: i == 0 || i == max - 1 ? 95f : 42f, PathFill, PathBorder);
            }
        }

        if (visualMode == PathDebugVisualMode.WaypointAuthoring)
        {
            DrawWaypointPlan(ground, points[0], points[max - 1], edited: true);
        }

        if (visualMode != PathDebugVisualMode.RouteOnly)
        {
            DrawPortalMarkers(ground, points);
        }
    }

    private void DrawRouteOnlyEndpointMarkers(GroundOverlayBuffer ground, Vector2 start, Vector2 goal)
    {
        if (IsFinite(start))
        {
            DrawCircle(ground, start, 95f, PathFill, PathBorder);
        }

        if (IsFinite(goal))
        {
            DrawCircle(ground, goal, 95f, PathFill, PathBorder);
        }
    }

    private void DrawPendingPathPreviewEndpointMarkers(GroundOverlayBuffer ground)
    {
        if (_guide.HasPathPreviewStart)
        {
            DrawCircle(ground, _guide.PathPreviewStartWorldCm, 135f, PathFill, PathBorder);
        }

        if (_guide.HasPathPreviewGoal)
        {
            DrawCircle(ground, _guide.PathPreviewGoalWorldCm, 135f, PathFill, PathBorder);
        }
    }

    private void DrawRouteOnlyPathPointMarkers(GroundOverlayBuffer ground, ReadOnlySpan<Vector2> points)
    {
        if (points.Length == 0)
        {
            return;
        }

        int last = points.Length - 1;
        for (int i = 0; i < points.Length; i++)
        {
            bool endpoint = i == 0 || i == last;
            bool importantBend =
                i > 0 &&
                i < last &&
                Vector2.DistanceSquared(points[i - 1], points[i + 1]) > 9_000_000f;
            if (!endpoint && !importantBend)
            {
                continue;
            }

            DrawCircle(ground, points[i], endpoint ? 95f : 54f, PathFill, PathBorder);
        }
    }

    private void DrawWaypointPlan(GroundOverlayBuffer ground, Vector2 start, Vector2 goal, bool edited)
    {
        Vector2 mid = (start + goal) * 0.5f;
        Vector2 normal = Vector2.Normalize(new Vector2(-(goal.Y - start.Y), goal.X - start.X));
        if (!IsFinite(normal))
        {
            normal = new Vector2(0f, 1f);
        }

        MassNavigationWaypointPathDiagnostics waypoint = _simulation.AcceptanceDiagnostics.WaypointPath;
        Vector2 oldMid = mid + normal * 4_500f;
        Vector2 newMid = edited && waypoint.HasAuthoredPlan && IsFinite(waypoint.AuthoredMidpointWorldCm)
            ? waypoint.AuthoredMidpointWorldCm
            : edited
                ? mid - normal * 4_500f
                : oldMid;
        DrawLine(ground, start, newMid, 32f, WaypointFill, WaypointBorder);
        DrawLine(ground, newMid, goal, 32f, WaypointFill, WaypointBorder);
        DrawCircle(ground, start, 145f, WaypointFill, WaypointBorder);
        DrawCircle(ground, newMid, 145f, WaypointFill, WaypointBorder);
        DrawCircle(ground, goal, 145f, WaypointFill, WaypointBorder);

        if (edited)
        {
            DrawInvalidatedWaypointPath(ground, start, oldMid, goal);
        }
    }

    private void DrawInvalidatedWaypointPath(GroundOverlayBuffer ground, Vector2 start, Vector2 oldMid, Vector2 goal)
    {
        ReadOnlySpan<MassNavigationPathPointSample> oldPoints = _simulation.AcceptanceDiagnostics.InvalidatedWaypointPathPoints;
        if (oldPoints.Length >= 2)
        {
            int max = Math.Min(oldPoints.Length, MaxPathPoints);
            Vector2 previous = new(oldPoints[0].Xcm, oldPoints[0].Ycm);
            for (int i = 1; i < max; i++)
            {
                Vector2 current = new(oldPoints[i].Xcm, oldPoints[i].Ycm);
                DrawLine(ground, previous, current, 20f, BlockedFill, BlockedBorder);
                previous = current;
            }
            return;
        }

        DrawCircle(ground, oldMid, 120f, BlockedFill, BlockedBorder);
        DrawLine(ground, start, oldMid, 20f, BlockedFill, BlockedBorder);
        DrawLine(ground, oldMid, goal, 20f, BlockedFill, BlockedBorder);
    }

    private void DrawPortalMarkers(GroundOverlayBuffer ground, ReadOnlySpan<Vector2> points)
    {
        for (int i = 1; i < points.Length - 1; i++)
        {
            if (i % 2 != 1)
            {
                continue;
            }

            DrawCircle(ground, points[i], 82f, PortalFill, PortalBorder);
        }
    }

    private void DrawHpaRoute(GroundOverlayBuffer ground)
    {
        MassNavigationHpaMacroDiagnostics hpa = _simulation.AcceptanceDiagnostics.HpaMacro;
        if (!hpa.Available || hpa.SampleRouteChunkCount <= 0)
        {
            return;
        }

        MassNavigationHpaGraphAssetDiagnostics graph = _simulation.AcceptanceDiagnostics.HpaGraph;
        HpaRouteChunk[] globalRoute = ResolveMacroEstimateHpaRouteChunks(hpa);
        HpaRouteChunk[] activeWindowRoute = ResolveGraphHpaRouteChunks(graph);
        HpaRouteChunk[] route = activeWindowRoute.Length > 0 ? activeWindowRoute : globalRoute;
        if (route.Length == 0)
        {
            return;
        }

        DrawHpaRouteCells(ground, route);
        Vector2 previous = ChunkCenter(route[0].X, route[0].Y);
        DrawCircle(ground, previous, 520f, HpaFill, HpaBorder);
        for (int i = 1; i < route.Length; i++)
        {
            Vector2 current = ChunkCenter(route[i].X, route[i].Y);
            DrawLine(ground, previous, current, 260f, HpaRouteLineFill, HpaRouteLineBorder);
            DrawCircle(ground, (previous + current) * 0.5f, 210f, PortalFill, PortalBorder);
            DrawCircle(ground, current, 410f, HpaFill, HpaBorder);
            DrawCircle(ground, current + new Vector2(0f, -360f), 120f + (i * 18f), new Vector4(1f, 0.84f, 0.28f, 0.18f), WaypointBorder);
            previous = current;
        }

        if (activeWindowRoute.Length > 0 && globalRoute.Length > 0)
        {
            DrawCircle(ground, ChunkCenter(globalRoute[0].X, globalRoute[0].Y), 620f, HpaFill, Good);
            DrawCircle(ground, ChunkCenter(globalRoute[^1].X, globalRoute[^1].Y), 620f, HpaFill, Good);
        }
    }

    private void DrawHpaRouteCells(GroundOverlayBuffer ground, HpaRouteChunk[] route)
    {
        for (int i = 0; i < route.Length; i++)
        {
            DrawChunkCell(ground, route[i].X, route[i].Y, HpaCellFill, i == 0 ? Good : HpaCellBorder);
        }
    }

    private void DrawActiveWindow(GroundOverlayBuffer ground)
    {
        MassNavigationHpaGraphAssetDiagnostics graph = _simulation.AcceptanceDiagnostics.HpaGraph;
        if (!graph.Available)
        {
            return;
        }

        Vector2 min = ChunkMin(graph.ActiveWindowMinChunkX, graph.ActiveWindowMinChunkY);
        Vector2 max = ChunkMax(graph.ActiveWindowMaxChunkX, graph.ActiveWindowMaxChunkY);
        Vector2 a = new(min.X, min.Y);
        Vector2 b = new(max.X, min.Y);
        Vector2 c = new(max.X, max.Y);
        Vector2 d = new(min.X, max.Y);
        DrawLine(ground, a, b, 130f, Good with { W = 0.12f }, Good);
        DrawLine(ground, b, c, 130f, Good with { W = 0.12f }, Good);
        DrawLine(ground, c, d, 130f, Good with { W = 0.12f }, Good);
        DrawLine(ground, d, a, 130f, Good with { W = 0.12f }, Good);
    }

    private void DrawChunkCell(GroundOverlayBuffer ground, int chunkX, int chunkY, Vector4 fill, Vector4 border)
    {
        Vector2 min = ChunkMin(chunkX, chunkY);
        Vector2 max = ChunkMax(chunkX, chunkY);
        float insetX = MathF.Max(120f, (max.X - min.X) * 0.04f);
        float insetY = MathF.Max(120f, (max.Y - min.Y) * 0.04f);
        Vector2 a = new(min.X + insetX, min.Y + insetY);
        Vector2 b = new(max.X - insetX, min.Y + insetY);
        Vector2 c = new(max.X - insetX, max.Y - insetY);
        Vector2 d = new(min.X + insetX, max.Y - insetY);
        DrawLine(ground, a, b, 220f, fill, border);
        DrawLine(ground, b, c, 220f, fill, border);
        DrawLine(ground, c, d, 220f, fill, border);
        DrawLine(ground, d, a, 220f, fill, border);
        DrawCircle(ground, (a + c) * 0.5f, MathF.Min(max.X - min.X, max.Y - min.Y) * 0.10f, fill, border);
    }

    private void DrawNavMeshSample(GroundOverlayBuffer ground)
    {
        MassNavigationNavMeshGuideSample sample = _guide.NavMeshSample;
        if (!sample.Available)
        {
            return;
        }

        DrawNavMeshCoverageBounds(ground);
        DrawActiveWindowNavMeshEdges(ground);

        for (int i = 0; i < sample.TriangleEdges.Length; i++)
        {
            MassNavigationGuideSegment edge = sample.TriangleEdges[i];
            Vector4 border = edge.AreaId switch
            {
                1 => new Vector4(0.28f, 0.95f, 1.0f, 0.72f),
                2 => new Vector4(0.50f, 0.95f, 0.42f, 0.72f),
                3 => new Vector4(0.92f, 0.76f, 0.36f, 0.72f),
                4 => new Vector4(0.34f, 0.58f, 1.0f, 0.72f),
                _ => new Vector4(0.46f, 0.96f, 0.68f, 0.72f)
            };
            DrawLine(ground, new Vector2(edge.Axcm, edge.Aycm), new Vector2(edge.Bxcm, edge.Bycm), 18f, border with { W = 0.08f }, border);
        }

        DrawNavMeshSemanticSamples(ground, sample);

        for (int i = 0; i < sample.Portals.Length; i++)
        {
            MassNavigationGuideSegment portal = sample.Portals[i];
            DrawLine(ground, new Vector2(portal.Axcm, portal.Aycm), new Vector2(portal.Bxcm, portal.Bycm), 120f, PortalFill, PortalBorder);
            DrawCircle(ground, (new Vector2(portal.Axcm, portal.Aycm) + new Vector2(portal.Bxcm, portal.Bycm)) * 0.5f, MathF.Max(60f, portal.ClearanceCm * 0.25f), PortalFill, PortalBorder);
        }

        Vector2 tileCenter = ChunkCenter(sample.ChunkX, sample.ChunkY);
        float radius = sample.AgentRadiusCm > 0
            ? sample.AgentRadiusCm
            : _simulation.MassFlow.Semantics.Obstacle.AgentBodyRadiusCm;
        DrawCircle(ground, tileCenter, MathF.Max(280f, radius * 4f), WaypointFill, WaypointBorder);
    }

    private void DrawActiveWindowNavMeshEdges(GroundOverlayBuffer ground)
    {
        ReadOnlySpan<MassNavigationGuideSegment> edges = _guide.ActiveWindowNavMeshEdges;
        bool runtimeBakeView = _guide.CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery;
        Vector4 fill = runtimeBakeView
            ? new Vector4(0.08f, 0.90f, 1f, 0.10f)
            : new Vector4(0.30f, 0.95f, 0.72f, 0.04f);
        Vector4 border = runtimeBakeView
            ? new Vector4(0.08f, 0.96f, 1f, 0.78f)
            : new Vector4(0.36f, 1f, 0.78f, 0.38f);
        float widthCm = runtimeBakeView ? 24f : 12f;
        for (int i = 0; i < edges.Length; i++)
        {
            MassNavigationGuideSegment edge = edges[i];
            DrawLine(
                ground,
                new Vector2(edge.Axcm, edge.Aycm),
                new Vector2(edge.Bxcm, edge.Bycm),
                widthCm,
                fill,
                border);
        }
    }

    private void DrawNavMeshCoverageBounds(GroundOverlayBuffer ground)
    {
        MassNavigationNavMeshCoverageGuide coverage = _guide.NavMeshCoverage;
        if (!coverage.Available)
        {
            return;
        }

        if (coverage.IsPartialCoverage && _simulation.BakeDataDiagnostics != null)
        {
            MassNavigationBakeDataDiagnostics bake = _simulation.BakeDataDiagnostics;
            Vector2 worldMin = new(bake.WorldMinXCm, bake.WorldMinYCm);
            Vector2 worldMax = new(bake.WorldMinXCm + bake.WorldWidthCm, bake.WorldMinYCm + bake.WorldHeightCm);
            DrawRectOutline(ground, worldMin, worldMax, 360f, WorldCoverageBorder with { W = 0.04f }, WorldCoverageBorder);
        }

        if (TryResolveNavMeshLoadedBounds(out Vector2 min, out Vector2 max))
        {
            DrawRectOutline(ground, min, max, 160f, NavMeshCoverageFill, NavMeshCoverageBorder);
        }
    }

    private bool TryResolveNavMeshLoadedBounds(out Vector2 min, out Vector2 max)
    {
        if (TryResolveNavMeshCoverageBounds(out min, out max))
        {
            return true;
        }

        min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        bool any = false;
        ReadOnlySpan<MassNavigationGuideSegment> edges = _guide.ActiveWindowNavMeshEdges;
        for (int i = 0; i < edges.Length; i++)
        {
            IncludePoint(edges[i].Axcm, edges[i].Aycm, ref min, ref max);
            IncludePoint(edges[i].Bxcm, edges[i].Bycm, ref min, ref max);
            any = true;
        }

        if (!any)
        {
            ReadOnlySpan<MassNavigationGuideSegment> sampleEdges = _guide.NavMeshSample.TriangleEdges;
            for (int i = 0; i < sampleEdges.Length; i++)
            {
                IncludePoint(sampleEdges[i].Axcm, sampleEdges[i].Aycm, ref min, ref max);
                IncludePoint(sampleEdges[i].Bxcm, sampleEdges[i].Bycm, ref min, ref max);
                any = true;
            }
        }

        return any &&
            float.IsFinite(min.X) &&
            float.IsFinite(min.Y) &&
            float.IsFinite(max.X) &&
            float.IsFinite(max.Y) &&
            max.X > min.X &&
            max.Y > min.Y;
    }

    private bool TryResolveNavMeshCoverageBounds(out Vector2 min, out Vector2 max)
    {
        min = default;
        max = default;
        MassNavigationNavMeshCoverageGuide coverage = _guide.NavMeshCoverage;
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (!coverage.Available || bake == null || coverage.ActiveWindowChunkCount <= 0)
        {
            return false;
        }

        int maxChunkX = Math.Max(0, bake.MacroChunkColumns - 1);
        int maxChunkY = Math.Max(0, bake.MacroChunkRows - 1);
        int minX = Math.Clamp(coverage.ActiveWindowMinChunkX, 0, maxChunkX);
        int minY = Math.Clamp(coverage.ActiveWindowMinChunkY, 0, maxChunkY);
        int endX = Math.Clamp(coverage.ActiveWindowMaxChunkX, minX, maxChunkX);
        int endY = Math.Clamp(coverage.ActiveWindowMaxChunkY, minY, maxChunkY);
        if (endX < minX || endY < minY)
        {
            return false;
        }

        min = ChunkMin(minX, minY);
        max = ChunkMax(endX, endY);
        return max.X > min.X && max.Y > min.Y;
    }

    private static void IncludePoint(float x, float y, ref Vector2 min, ref Vector2 max)
    {
        min = new Vector2(MathF.Min(min.X, x), MathF.Min(min.Y, y));
        max = new Vector2(MathF.Max(max.X, x), MathF.Max(max.Y, y));
    }

    private void DrawLogicHeightmapBakeFlow(GroundOverlayBuffer ground)
    {
        MassNavigationNavMeshGuideSample sample = _guide.NavMeshSample;
        if (!sample.Available)
        {
            return;
        }

        Vector2 tileCenter = ChunkCenter(sample.ChunkX, sample.ChunkY);
        Vector2 source = tileCenter + new Vector2(-7_000f, 0f);
        Vector2 logic = tileCenter + new Vector2(0f, -5_600f);
        Vector2 query = tileCenter + new Vector2(7_000f, 0f);
        DrawCircle(ground, source, 520f, new Vector4(0.24f, 0.95f, 0.58f, 0.12f), Good);
        DrawCircle(ground, logic, 520f, new Vector4(1f, 0.78f, 0.24f, 0.12f), WaypointBorder);
        DrawCircle(ground, query, 520f, PortalFill, PortalBorder);
        DrawLine(ground, source, logic, 90f, new Vector4(0.24f, 0.95f, 0.58f, 0.10f), Good);
        DrawLine(ground, logic, query, 90f, PortalFill, PortalBorder);
    }

    private void DrawNavMeshSemanticSamples(GroundOverlayBuffer ground, MassNavigationNavMeshGuideSample sample)
    {
        Vector2 tileCenter = ChunkCenter(sample.ChunkX, sample.ChunkY);
        float tileW = _simulation.BakeDataDiagnostics?.MacroChunkSizeXCm ?? 25_000f;
        float tileH = _simulation.BakeDataDiagnostics?.MacroChunkSizeYCm ?? 25_000f;
        if (sample.BlockedCellCount > 0)
        {
            DrawRegion(ground, tileCenter + new Vector2(-tileW * 0.26f, tileH * 0.22f), tileW * 0.18f, tileH * 0.18f, BlockedFill, BlockedBorder);
        }

        if (sample.HighCostCellCount > 0)
        {
            DrawRegion(ground, tileCenter + new Vector2(tileW * 0.22f, -tileH * 0.20f), tileW * 0.20f, tileH * 0.16f, new Vector4(0.95f, 0.72f, 0.28f, 0.12f), new Vector4(1f, 0.82f, 0.38f, 0.72f));
        }

        if (sample.WaterCellCount > 0)
        {
            DrawRegion(ground, tileCenter + new Vector2(-tileW * 0.10f, -tileH * 0.28f), tileW * 0.26f, tileH * 0.12f, new Vector4(0.25f, 0.62f, 1f, 0.13f), new Vector4(0.40f, 0.76f, 1f, 0.68f));
        }
    }

    private void DrawStrategyCompare(GroundOverlayBuffer ground)
    {
        ReadOnlySpan<MassNavigationPathPointSample> points = _simulation.AcceptanceDiagnostics.PathOnlyPathPoints;
        if (points.Length < 2)
        {
            return;
        }

        int max = Math.Min(points.Length, MaxPathPoints);
        Vector2 offsetA = new(-420f, -240f);
        Vector2 offsetB = new(380f, 210f);
        Vector2 offsetC = new(0f, 520f);
        for (int i = 0; i < max - 1; i++)
        {
            Vector2 a = new(points[i].Xcm, points[i].Ycm);
            Vector2 b = new(points[i + 1].Xcm, points[i + 1].Ycm);
            DrawLine(ground, a + offsetA, b + offsetA, 38f, new Vector4(0.30f, 0.66f, 1f, 0.12f), new Vector4(0.44f, 0.76f, 1f, 0.82f));
            DrawLine(ground, a + offsetB, b + offsetB, 38f, new Vector4(0.24f, 0.95f, 0.58f, 0.12f), new Vector4(0.34f, 1f, 0.68f, 0.82f));
            DrawLine(ground, a + offsetC, b + offsetC, 38f, new Vector4(1f, 0.78f, 0.24f, 0.12f), new Vector4(1f, 0.86f, 0.38f, 0.82f));
        }
    }

    private void DrawLayerCostRegions(GroundOverlayBuffer ground)
    {
        Vector2 center = new(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
        float w = MathF.Max(3_000f, _simulation.SolverWindowWidthCm);
        float h = MathF.Max(3_000f, _simulation.SolverWindowHeightCm);
        DrawRegion(ground, center + new Vector2(-w * 0.34f, h * 0.24f), w * 0.26f, h * 0.22f, AirFill, AirBorder);
        DrawRegion(ground, center + new Vector2(-w * 0.30f, -h * 0.15f), w * 0.42f, h * 0.52f, new Vector4(0.26f, 0.85f, 0.42f, 0.12f), new Vector4(0.44f, 1f, 0.58f, 0.62f));
        DrawRegion(ground, center + new Vector2(w * 0.24f, -h * 0.18f), w * 0.30f, h * 0.72f, new Vector4(0.25f, 0.62f, 1f, 0.13f), new Vector4(0.40f, 0.76f, 1f, 0.68f));
        DrawRegion(ground, center + new Vector2(-w * 0.10f, h * 0.30f), w * 0.70f, h * 0.18f, new Vector4(0.95f, 0.72f, 0.28f, 0.12f), new Vector4(1f, 0.82f, 0.38f, 0.72f));
        DrawRegion(ground, center + new Vector2(w * 0.18f, h * 0.22f), w * 0.22f, h * 0.28f, BlockedFill, BlockedBorder);
    }

    private void DrawOrderReuseBucket(GroundOverlayBuffer ground)
    {
        Vector2 goal = ResolveGoal();
        DrawCircle(ground, goal, 1_000f, new Vector4(1f, 0.86f, 0.32f, 0.08f), new Vector4(1f, 0.86f, 0.32f, 0.90f));
        DrawCircle(ground, goal + new Vector2(100f, 80f), 460f, WaypointFill, WaypointBorder);
    }

    private void DrawTargetAllocation(GroundOverlayBuffer ground, bool sampleOnly = false)
    {
        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        Vector2 destination = allocation.HasAllocation ? allocation.DestinationWorldCm : ResolveGoal();
        float footprintRadius = MathF.Max(1_200f, allocation.GoalFootprintRadiusCm);
        DrawTargetFootprint(ground, destination, footprintRadius, SlotBorder);
        if (!sampleOnly)
        {
            DrawFootprintGuides(ground, destination, footprintRadius);
        }
        DrawLine(ground, destination + new Vector2(-420f, 0f), destination + new Vector2(420f, 0f), 42f, new Vector4(0.02f, 0.04f, 0.04f, 0.30f), Warn);
        DrawLine(ground, destination + new Vector2(0f, -420f), destination + new Vector2(0f, 420f), 42f, new Vector4(0.02f, 0.04f, 0.04f, 0.30f), Warn);

        ReadOnlySpan<MassNavigationTargetSlotSample> samples = _simulation.AcceptanceDiagnostics.TargetSlotSamples;
        int count = Math.Min(sampleOnly ? 48 : MaxSlotMarkers, samples.Length);
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                MassNavigationTargetSlotSample sample = samples[i];
                DrawSlotTick(ground, new Vector2(sample.Xcm, sample.Ycm), i);
            }
        }
        else
        {
            DrawTargetFootprint(ground, destination, MathF.Max(460f, footprintRadius * 0.10f), Warn);
        }

        DrawTargetFootprint(ground, destination, 520f, Warn);
    }

    private void DrawTargetFootprint(GroundOverlayBuffer ground, Vector2 center, float radius, Vector4 stroke)
    {
        const int segments = 16;
        Vector4 fill = new(stroke.X, stroke.Y, stroke.Z, MathF.Min(0.10f, stroke.W * 0.20f));
        Vector2 previous = center + new Vector2(radius, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = (MathF.PI * 2f * i) / segments;
            Vector2 next = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            DrawLine(ground, previous, next, 30f, fill, stroke);
            previous = next;
        }
    }

    private void DrawSlotTick(GroundOverlayBuffer ground, Vector2 slot, int index)
    {
        float half = index % 5 == 0 ? 118f : 78f;
        DrawLine(ground, slot + new Vector2(-half, 0f), slot + new Vector2(half, 0f), 28f, SlotFill, SlotBorder);
        DrawLine(ground, slot + new Vector2(0f, -half), slot + new Vector2(0f, half), 28f, SlotFill, SlotBorder);
    }

    private void DrawFootprintGuides(GroundOverlayBuffer ground, Vector2 center, float radius)
    {
        Vector4 fill = new(1f, 0.86f, 0.32f, 0.06f);
        for (int i = 0; i < 8; i++)
        {
            float angle = (MathF.PI * 2f * i) / 8f;
            Vector2 dir = new(MathF.Cos(angle), MathF.Sin(angle));
            DrawLine(ground, center + (dir * (radius * 0.28f)), center + (dir * radius), 34f, fill, SlotBorder);
        }
    }

    private void DrawStaticObstacleBuckets(GroundOverlayBuffer ground)
    {
        MassNavigationStaticObstacleWorldDiagnostics world = _simulation.AcceptanceDiagnostics.StaticObstacleWorld;
        if (!world.WorldDistributionReady)
        {
            return;
        }

        Vector2 center = new(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
        float width = MathF.Max(12_000f, _simulation.SolverWindowWidthCm * 1.6f);
        float height = MathF.Max(12_000f, _simulation.SolverWindowHeightCm * 1.6f);
        int count = Math.Min(MaxObstacleBucketMarkers, Math.Max(24, world.MacroChunkCoverageCount / 128));
        int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));
        int rows = Math.Max(1, (int)MathF.Ceiling(count / (float)cols));
        for (int i = 0; i < count; i++)
        {
            int row = i / cols;
            int col = i % cols;
            float u = (col + 0.5f) / cols;
            float v = (row + 0.5f) / rows;
            Vector2 point = center + new Vector2((u - 0.5f) * width, (v - 0.5f) * height);
            DrawLine(ground, point + new Vector2(-280f, -280f), point + new Vector2(280f, 280f), 44f, SlotFill, SlotBorder);
            if (i % 5 == 0)
            {
                DrawCircle(ground, point, 86f, SlotFill, SlotBorder);
            }
        }

        DrawSolverActiveObstacleSubset(ground, center);
    }

    private void DrawSolverActiveObstacleSubset(GroundOverlayBuffer ground, Vector2 center)
    {
        MassNavigationObstacleDiagnostics obstacle = _simulation.AcceptanceDiagnostics.Obstacles;
        int active = Math.Min(16, Math.Max(0, obstacle.SolverActiveStaticObstacleCount));
        if (active == 0)
        {
            return;
        }

        float radius = 620f;
        for (int i = 0; i < active; i++)
        {
            float angle = (MathF.PI * 2f * i) / active;
            Vector2 point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            DrawLine(ground, point + new Vector2(-210f, -210f), point + new Vector2(210f, 210f), 52f, Good with { W = 0.14f }, Good);
            DrawLine(ground, point + new Vector2(-210f, 210f), point + new Vector2(210f, -210f), 52f, Good with { W = 0.14f }, Good);
        }
    }

    private void DrawRuntimeBakeAuthoring(GroundOverlayBuffer ground)
    {
        MassNavigationRuntimeBakeAuthoringRuntime authoring = _guide.RuntimeBakeAuthoring;
        IReadOnlyList<MassNavigationRuntimeDirtyChunk> dirtyChunks = authoring.DirtyChunks;
        int dirtyCount = Math.Min(MaxRuntimeDirtyChunkMarkers, dirtyChunks.Count);
        for (int i = 0; i < dirtyCount; i++)
        {
            MassNavigationRuntimeDirtyChunk chunk = dirtyChunks[i];
            DrawRuntimeDirtyTile(ground, chunk);
        }

        IReadOnlyList<MassNavigationRuntimeAuthoredObstaclePolygon> polygons = authoring.AuthoredPolygons;
        int polygonCount = Math.Min(MaxRuntimeObstaclePolygons, polygons.Count);
        for (int i = 0; i < polygonCount; i++)
        {
            DrawRuntimeObstaclePolygon(ground, polygons[i].PointsWorldCm, closed: true);
        }

        IReadOnlyList<Vector2> draftPoints = authoring.DraftPoints;
        if (draftPoints.Count > 0)
        {
            DrawRuntimeObstaclePolygon(ground, draftPoints, closed: false);
        }
    }

    private void DrawRuntimeDirtyTile(GroundOverlayBuffer ground, MassNavigationRuntimeDirtyChunk chunk)
    {
        if (!chunk.HasWorldBounds)
        {
            return;
        }

        Vector2 min = new(chunk.MinWorldXCm, chunk.MinWorldYCm);
        Vector2 max = new(chunk.MaxWorldXCm, chunk.MaxWorldYCm);
        DrawRectOutline(ground, min, max, 96f, RuntimeDirtyChunkFill, RuntimeDirtyChunkBorder);
        DrawCircle(
            ground,
            chunk.CenterWorldCm,
            MathF.Max(120f, MathF.Min(chunk.SizeXCm, chunk.SizeYCm) * 0.08f),
            RuntimeDirtyChunkFill,
            RuntimeDirtyChunkBorder);
    }

    private void DrawRuntimeObstaclePolygon(GroundOverlayBuffer ground, IReadOnlyList<Vector2> points, bool closed)
    {
        if (points.Count == 0)
        {
            return;
        }

        Vector2 centroid = Vector2.Zero;
        for (int i = 0; i < points.Count; i++)
        {
            centroid += points[i];
            DrawCircle(ground, points[i], i == 0 ? 130f : 92f, RuntimeObstacleFill, RuntimeObstacleBorder);
            if (i > 0)
            {
                DrawHeightSampledLine(ground, points[i - 1], points[i], 120f, RuntimeObstacleFill, RuntimeObstacleBorder);
            }
        }

        centroid /= points.Count;
        if (closed && points.Count >= 3)
        {
            DrawHeightSampledLine(ground, points[points.Count - 1], points[0], 120f, RuntimeObstacleFill, RuntimeObstacleBorder);
            DrawCircle(ground, centroid, 220f, RuntimeObstacleFill, RuntimeObstacleBorder);
        }
        else
        {
            DrawCircle(ground, centroid, 150f, RuntimeDirtyChunkFill, RuntimeDirtyChunkBorder);
        }
    }

    private void DrawRegion(GroundOverlayBuffer ground, Vector2 center, float widthCm, float heightCm, Vector4 fill, Vector4 border)
    {
        Vector2 a = center + new Vector2(-widthCm * 0.5f, -heightCm * 0.5f);
        Vector2 b = center + new Vector2(widthCm * 0.5f, -heightCm * 0.5f);
        Vector2 c = center + new Vector2(widthCm * 0.5f, heightCm * 0.5f);
        Vector2 d = center + new Vector2(-widthCm * 0.5f, heightCm * 0.5f);
        DrawLine(ground, a, b, 120f, fill, border);
        DrawLine(ground, b, c, 120f, fill, border);
        DrawLine(ground, c, d, 120f, fill, border);
        DrawLine(ground, d, a, 120f, fill, border);
        DrawCircle(ground, center, MathF.Min(widthCm, heightCm) * 0.22f, fill, border);
    }

    private void DrawRectOutline(GroundOverlayBuffer ground, Vector2 min, Vector2 max, float widthCm, Vector4 fill, Vector4 border)
    {
        Vector2 a = new(min.X, min.Y);
        Vector2 b = new(max.X, min.Y);
        Vector2 c = new(max.X, max.Y);
        Vector2 d = new(min.X, max.Y);
        DrawLine(ground, a, b, widthCm, fill, border);
        DrawLine(ground, b, c, widthCm, fill, border);
        DrawLine(ground, c, d, widthCm, fill, border);
        DrawLine(ground, d, a, widthCm, fill, border);
    }

    private Vector2 ResolveGoal()
    {
        MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        return query.GoalWorldCm != Vector2.Zero
            ? query.GoalWorldCm
            : new Vector2(_simulation.SolverWindowCenterXCm + 3_200f, _simulation.SolverWindowCenterYCm + 2_400f);
    }

    private Vector2 ChunkCenter(int x, int y)
    {
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (bake == null)
        {
            return new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
        }

        return new Vector2(
            bake.WorldMinXCm + (x * bake.MacroChunkSizeXCm) + (bake.MacroChunkSizeXCm * 0.5f),
            bake.WorldMinYCm + (y * bake.MacroChunkSizeYCm) + (bake.MacroChunkSizeYCm * 0.5f));
    }

    private Vector2 ChunkMin(int x, int y)
    {
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (bake == null)
        {
            return new Vector2(_simulation.SolverWindowMinXCm, _simulation.SolverWindowMinYCm);
        }

        return new Vector2(
            bake.WorldMinXCm + (x * bake.MacroChunkSizeXCm),
            bake.WorldMinYCm + (y * bake.MacroChunkSizeYCm));
    }

    private Vector2 ChunkMax(int x, int y)
    {
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (bake == null)
        {
            return new Vector2(_simulation.SolverWindowMaxXCm, _simulation.SolverWindowMaxYCm);
        }

        return new Vector2(
            bake.WorldMinXCm + ((x + 1) * bake.MacroChunkSizeXCm),
            bake.WorldMinYCm + ((y + 1) * bake.MacroChunkSizeYCm));
    }

    private static string BuildHpaRouteChunkText(
        MassNavigationHpaMacroDiagnostics hpa,
        int maxLabels)
    {
        if (!hpa.Available || hpa.SampleRouteChunkCount <= 0)
        {
            return "not_available";
        }

        HpaRouteChunk[] route = ResolveMacroEstimateHpaRouteChunks(hpa);
        if (route.Length == 0)
        {
            return "not_available";
        }

        int[] indices = BuildHpaLabelIndices(route.Length, maxLabels);
        var parts = new string[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            int routeIndex = indices[i];
            HpaRouteChunk chunk = route[routeIndex];
            parts[i] = $"{routeIndex + 1:00}:{chunk.X},{chunk.Y}{FormatHpaPortalSuffix(chunk)}";
        }

        string suffix = route.Length > indices.Length ? " ..." : string.Empty;
        return string.Join(" -> ", parts) + suffix;
    }

    private static int MapChunkXToInset(int mapX, int gridW, int minX, int maxX, int chunkX)
    {
        int span = Math.Max(1, maxX - minX);
        float ratio = Math.Clamp((chunkX - minX) / (float)span, 0f, 1f);
        return mapX + Math.Clamp((int)MathF.Round(ratio * gridW), 4, Math.Max(4, gridW - 4));
    }

    private static int MapChunkYToInset(int mapY, int gridH, int minY, int maxY, int chunkY)
    {
        int span = Math.Max(1, maxY - minY);
        float ratio = Math.Clamp((chunkY - minY) / (float)span, 0f, 1f);
        return mapY + Math.Clamp((int)MathF.Round(ratio * gridH), 4, Math.Max(4, gridH - 4));
    }

    private static int[] BuildHpaLabelIndices(int routeChunkCount, int maxLabels)
    {
        int count = Math.Max(1, routeChunkCount);
        int labelCount = Math.Clamp(maxLabels, 1, count);
        if (labelCount == count)
        {
            var all = new int[count];
            for (int i = 0; i < count; i++)
            {
                all[i] = i;
            }

            return all;
        }

        var indices = new int[labelCount];
        indices[0] = 0;
        indices[labelCount - 1] = count - 1;
        for (int i = 1; i < labelCount - 1; i++)
        {
            float ratio = i / (float)(labelCount - 1);
            indices[i] = Math.Clamp((int)MathF.Round(ratio * (count - 1)), 1, count - 2);
        }

        return indices;
    }

    private static HpaRouteChunk[] ResolveGraphHpaRouteChunks(MassNavigationHpaGraphAssetDiagnostics graph)
    {
        if (!graph.ActiveWindowRouteAvailable || string.IsNullOrWhiteSpace(graph.RouteSignature))
        {
            return Array.Empty<HpaRouteChunk>();
        }

        string[] parts = graph.RouteSignature.Split("->", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return Array.Empty<HpaRouteChunk>();
        }

        var route = new List<HpaRouteChunk>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            if (!TryParseGraphHpaRoutePart(parts[i], out HpaRouteChunk chunk))
            {
                continue;
            }

            if (route.Count > 0 && route[^1].X == chunk.X && route[^1].Y == chunk.Y)
            {
                route[^1] = chunk;
                continue;
            }

            route.Add(chunk);
        }

        return route.ToArray();
    }

    private static bool TryParseGraphHpaRoutePart(string part, out HpaRouteChunk chunk)
    {
        chunk = default;
        int comma = part.IndexOf(',');
        int colon = part.IndexOf(':');
        if (comma <= 0 || colon <= comma + 1 || colon >= part.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(part.AsSpan(0, comma), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(part.AsSpan(comma + 1, colon - comma - 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int y) ||
            !int.TryParse(part.AsSpan(colon + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int portalIndex))
        {
            return false;
        }

        chunk = new HpaRouteChunk(x, y, portalIndex, FromGraphRoute: true);
        return true;
    }

    private static HpaRouteChunk[] ResolveMacroEstimateHpaRouteChunks(MassNavigationHpaMacroDiagnostics hpa)
    {
        int x = hpa.StartMacroChunkX;
        int y = hpa.StartMacroChunkY;
        int count = Math.Max(0, hpa.SampleRouteChunkCount);
        if (!hpa.Available || count <= 0)
        {
            return Array.Empty<HpaRouteChunk>();
        }

        var route = new HpaRouteChunk[count];
        route[0] = new HpaRouteChunk(x, y, PortalIndex: -1, FromGraphRoute: false);
        for (int i = 1; i < count; i++)
        {
            if (x != hpa.GoalMacroChunkX)
            {
                x += Math.Sign(hpa.GoalMacroChunkX - x);
            }
            else if (y != hpa.GoalMacroChunkY)
            {
                y += Math.Sign(hpa.GoalMacroChunkY - y);
            }

            route[i] = new HpaRouteChunk(x, y, PortalIndex: -1, FromGraphRoute: false);
        }

        return route;
    }

    private static string FormatHpaPortalSuffix(HpaRouteChunk chunk)
    {
        return chunk.FromGraphRoute && chunk.PortalIndex >= 0
            ? $":p{chunk.PortalIndex}"
            : string.Empty;
    }

    private static string ShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "not_available";
        }

        string file = System.IO.Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(file) ? path : file;
    }

    private static string Shorten(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxCharacters)
        {
            return value;
        }

        return value[..Math.Max(0, maxCharacters - 3)] + "...";
    }

    private void DrawCircle(GroundOverlayBuffer ground, Vector2 centerCm, float radiusCm, Vector4 fill, Vector4 border)
    {
        ground.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Circle,
            Center = ToVisualMeters(centerCm),
            Radius = WorldUnits.CmToM(radiusCm),
            FillColor = fill,
            BorderColor = border,
            BorderWidth = 0.035f
        });
    }

    private void DrawLine(GroundOverlayBuffer ground, Vector2 startCm, Vector2 endCm, float widthCm, Vector4 fill, Vector4 border)
    {
        Vector2 deltaCm = endCm - startCm;
        float lengthCm = deltaCm.Length();
        if (lengthCm <= 0.001f)
        {
            return;
        }

        DrawLineSegment(ground, startCm, endCm, widthCm, fill, border);
    }

    private void DrawHeightSampledLine(GroundOverlayBuffer ground, Vector2 startCm, Vector2 endCm, float widthCm, Vector4 fill, Vector4 border)
    {
        Vector2 deltaCm = endCm - startCm;
        float lengthCm = deltaCm.Length();
        if (lengthCm <= 0.001f)
        {
            return;
        }

        int segmentCount = Math.Clamp((int)MathF.Ceiling(lengthCm / GroundOverlaySegmentLengthCm), 1, MaxGroundOverlayLineSegments);
        if (segmentCount > 1)
        {
            Vector2 previous = startCm;
            for (int i = 1; i <= segmentCount; i++)
            {
                float t = i / (float)segmentCount;
                Vector2 current = startCm + (deltaCm * t);
                DrawLineSegment(ground, previous, current, widthCm, fill, border);
                previous = current;
            }

            return;
        }

        DrawLineSegment(ground, startCm, endCm, widthCm, fill, border);
    }

    private void DrawLineSegment(GroundOverlayBuffer ground, Vector2 startCm, Vector2 endCm, float widthCm, Vector4 fill, Vector4 border)
    {
        Vector2 deltaCm = endCm - startCm;
        float lengthCm = deltaCm.Length();
        if (lengthCm <= 0.001f)
        {
            return;
        }

        ground.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Line,
            Center = ToVisualMeters(startCm),
            Length = WorldUnits.CmToM(lengthCm),
            Width = WorldUnits.CmToM(widthCm),
            Rotation = WorldPlane2D.FacingRadFromDirection(deltaCm.X, deltaCm.Y),
            FillColor = fill,
            BorderColor = border,
            BorderWidth = 0.025f
        });
    }

    private Vector3 ToVisualMeters(Vector2 worldCm)
    {
        return new Vector3(WorldUnits.CmToM(worldCm.X), ResolveOverlayHeightMeters(worldCm, GroundOverlayLiftMeters), WorldUnits.CmToM(worldCm.Y));
    }

    private float ResolveOverlayHeightMeters(Vector2 worldCm, float liftMeters)
    {
        if (!IsFinite(worldCm) ||
            !_engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? heightmap) ||
            !heightmap.TrySampleHeightCm(worldCm.X, worldCm.Y, out float heightCm) ||
            !float.IsFinite(heightCm))
        {
            return OverlayY;
        }

        return WorldUnits.CmToM(heightCm) + liftMeters;
    }

    private static void AddWorldLabel(
        ScreenOverlayBuffer overlay,
        IScreenProjector projector,
        Vector2 worldCm,
        string label,
        int stableId,
        int serial,
        Vector4 color)
    {
        AddWorldLabel(overlay, projector, worldCm, label, stableId, serial, color, Vector2.Zero);
    }

    private static void AddWorldLabel(
        ScreenOverlayBuffer overlay,
        IScreenProjector projector,
        Vector2 worldCm,
        string label,
        int stableId,
        int serial,
        Vector4 color,
        Vector2 screenOffsetPx)
    {
        if (!IsFinite(worldCm))
        {
            return;
        }

        Vector2 screen;
        try
        {
            screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(worldCm.X, worldCm.Y, yMeters: 1.3f));
        }
        catch (ArgumentException)
        {
            AddFallbackWorldLabel(overlay, label, stableId, serial, color);
            return;
        }
        if (!IsFinite(screen))
        {
            AddFallbackWorldLabel(overlay, label, stableId, serial, color);
            return;
        }

        int textWidth = Math.Clamp((label.Length * 7) + 10, 72, 360);
        int x = (int)MathF.Round(screen.X + screenOffsetPx.X) - (textWidth / 2);
        int y = (int)MathF.Round(screen.Y + screenOffsetPx.Y) - 18;
        overlay.AddRect(x - 4, y - 4, textWidth + 8, 23, LabelFill, LabelBorder, stableId, serial);
        overlay.AddText(x, y, label, 12, color, stableId + 1000, serial);
    }

    private static void AddFallbackWorldLabel(
        ScreenOverlayBuffer overlay,
        string label,
        int stableId,
        int serial,
        Vector4 color)
    {
        int local = Math.Abs(stableId % 100);
        int col = local / 8;
        int row = local % 8;
        int textWidth = Math.Clamp((label.Length * 7) + 64, 150, 380);
        int x = 496 + (col * 386);
        int y = 532 + (row * 24);
        overlay.AddRect(x - 4, y - 4, textWidth + 8, 23, LabelFill, LabelBorder, stableId, serial);
        overlay.AddText(x, y, label, 12, color, stableId + 1000, serial);
    }

    private void AddProjectedRouteSegment(
        ScreenOverlayBuffer overlay,
        IScreenProjector projector,
        Vector2 startWorldCm,
        Vector2 endWorldCm,
        int stableId,
        int serial)
    {
        if (!TryProjectWorldPoint(projector, startWorldCm, ResolveOverlayHeightMeters(startWorldCm, ScreenRouteLiftMeters), out Vector2 start) ||
            !TryProjectWorldPoint(projector, endWorldCm, ResolveOverlayHeightMeters(endWorldCm, ScreenRouteLiftMeters), out Vector2 end))
        {
            return;
        }

        int x0 = (int)MathF.Round(start.X);
        int y0 = (int)MathF.Round(start.Y);
        int x1 = (int)MathF.Round(end.X);
        int y1 = (int)MathF.Round(end.Y);
        overlay.AddLine(x0, y0, x1, y1, 8, new Vector4(0.02f, 0.05f, 0.06f, 0.82f), stableId, serial);
        overlay.AddLine(x0, y0, x1, y1, 4, PathBorder, stableId + 500, serial);
    }

    private void AddProjectedRouteNode(
        ScreenOverlayBuffer overlay,
        IScreenProjector projector,
        Vector2 worldCm,
        int stableId,
        int serial,
        bool endpoint)
    {
        if (!TryProjectWorldPoint(projector, worldCm, ResolveOverlayHeightMeters(worldCm, ScreenRouteLiftMeters + 0.05f), out Vector2 screen))
        {
            return;
        }

        int radius = endpoint ? 7 : 5;
        int x = (int)MathF.Round(screen.X) - radius;
        int y = (int)MathF.Round(screen.Y) - radius;
        int size = radius * 2;
        overlay.AddRect(x - 2, y - 2, size + 4, size + 4, new Vector4(0.02f, 0.05f, 0.06f, 0.84f), PathBorder, stableId, serial);
        overlay.AddRect(x, y, size, size, PathBorder with { W = endpoint ? 0.96f : 0.78f }, PathBorder, stableId + 500, serial);
    }

    private static bool TryProjectWorldPoint(
        IScreenProjector projector,
        Vector2 worldCm,
        float yMeters,
        out Vector2 screen)
    {
        screen = default;
        if (!IsFinite(worldCm))
        {
            return false;
        }

        try
        {
            screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(worldCm.X, worldCm.Y, yMeters));
            return IsFinite(screen);
        }
        catch (ArgumentException)
        {
            screen = default;
            return false;
        }
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
