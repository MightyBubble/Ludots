using System;
using System.Diagnostics;
using System.Numerics;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.UI;

internal sealed class MassNavigationPanelController
{
    private const long PerfRefreshTicks = TimeSpan.TicksPerSecond / 4;

    private ReactivePage<MassNavigationPanelState>? _page;
    private MassNavigationPanelState _lastState = MassNavigationPanelState.Empty;
    private GameEngine? _engine;
    private MassNavigationSimulationRuntime? _simulation;
    private MassNavigationShowcaseGuideRuntime? _guide;
    private long _lastPerfCaptureTicks;
    private FocusedPanelRefreshKey _lastFocusedPanelRefreshKey;
    private bool _hasFocusedPanelRefreshKey;
    private string _lastActionText = "MassNavigation runtime, flow, and arrival knobs hot-apply now. Physics/Nav buttons only touch engine policies. Agent count and Reset rebuild the scene.";

    public bool MountOrSync(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return false;
        }

        _engine = engine;
        _simulation = simulation;
        _guide = engine.GetService(MassNavigationKeys.ShowcaseGuideRuntime);
        var page = EnsurePage(engine);
        bool changed = false;
        if (!ReferenceEquals(root.Scene, page.Scene))
        {
            root.MountScene(page.Scene);
            root.IsDirty = true;
            changed = true;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        long refreshTicks = (long)(PerfRefreshTicks * (Stopwatch.Frequency / (double)TimeSpan.TicksPerSecond));
        bool focusedPanel = _guide?.FocusedPanel == true;
        FocusedPanelRefreshKey focusedPanelRefreshKey = focusedPanel
            ? BuildFocusedPanelRefreshKey(engine, simulation, _guide!)
            : default;
        if (focusedPanel)
        {
            if (!changed &&
                _hasFocusedPanelRefreshKey &&
                focusedPanelRefreshKey.Equals(_lastFocusedPanelRefreshKey))
            {
                return false;
            }

            _lastFocusedPanelRefreshKey = focusedPanelRefreshKey;
            _hasFocusedPanelRefreshKey = true;
        }
        else
        {
            _hasFocusedPanelRefreshKey = false;
        }

        if (!focusedPanel && _lastPerfCaptureTicks != 0 && nowTicks - _lastPerfCaptureTicks < refreshTicks)
        {
            return changed;
        }

        _lastPerfCaptureTicks = nowTicks;
        MassNavigationPanelState next = CaptureState(engine, simulation);

        if (!_lastState.Equals(next))
        {
            _lastState = next;
            page.SetState(_ => next);
            root.IsDirty = true;
            changed = true;
        }

        return changed;
    }

    private ReactivePage<MassNavigationPanelState> EnsurePage(GameEngine engine)
    {
        if (_page != null)
        {
            return _page;
        }

        var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
        var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
        _page = new ReactivePage<MassNavigationPanelState>(textMeasurer, imageSizeProvider, MassNavigationPanelState.Empty, BuildRoot);
        return _page;
    }

    public void ClearIfOwned(UIRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (_page != null && ReferenceEquals(root.Scene, _page.Scene))
        {
            root.ClearScene();
        }

        _engine = null;
        _simulation = null;
        _guide = null;
        _hasFocusedPanelRefreshKey = false;
        _lastState = MassNavigationPanelState.Empty;
        _lastPerfCaptureTicks = 0;
        _lastActionText = "MassNavigation runtime, flow, and arrival knobs hot-apply now. Physics/Nav buttons only touch engine policies. Agent count and Reset rebuild the scene.";
        _page?.SetState(_ => MassNavigationPanelState.Empty);
    }

    private static FocusedPanelRefreshKey BuildFocusedPanelRefreshKey(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide)
    {
        Vector2 viewportResolution = engine.GetService(CoreServiceKeys.ViewController)?.Resolution ?? Vector2.Zero;
        MassNavigationPathOnlyQueryDiagnostics path = simulation.AcceptanceDiagnostics.PathOnlyQuery;
        MassNavigationTargetAllocationDiagnostics allocation = simulation.AcceptanceDiagnostics.TargetAllocation;
        MassNavigationOrderReuseDiagnostics reuse = simulation.AcceptanceDiagnostics.OrderReuse;
        MassNavigationWaypointPathDiagnostics waypoint = simulation.AcceptanceDiagnostics.WaypointPath;
        MassNavigationObstacleDiagnostics obstacle = simulation.AcceptanceDiagnostics.Obstacles;
        MassNavigationRuntimeBakeAuthoringRuntime runtimeBake = guide.RuntimeBakeAuthoring;
        MassNavigationRuntimeNavDataUpdateDiagnostics runtimeNavData = simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate;
        int debugFlags = (guide.DebugNavMeshEnabled ? 1 : 0) |
            (guide.DebugHpaEnabled ? 2 : 0) |
            (guide.DebugPathEnabled ? 4 : 0) |
            (guide.DebugLayerCostEnabled ? 8 : 0) |
            (guide.DebugSlotsEnabled ? 16 : 0);
        return new FocusedPanelRefreshKey(
            StepId: guide.CurrentStepId,
            ActionRevision: guide.ActionRevision,
            DebugFlags: debugFlags,
            SelectionRevision: simulation.SelectionRevision,
            StructuralChangeRevision: simulation.StructuralChangeRevision,
            FlowWorkAreaRevision: simulation.FlowWorkAreaRevision,
            SelectedCount: simulation.SelectedCount,
            LastCommandSelectionCount: simulation.LastCommandSelectionCount,
            ActiveOrderGroupCount: simulation.NavGroupRuntime.ActiveOrderGroupCount,
            UnitsWithTargets: simulation.MassFlow.CountUnitsWithTargets(),
            AllocationRouteId: allocation.AllocationRouteId,
            AllocationSlotCount: allocation.SlotCount,
            AllocationReachableSlotCount: allocation.ReachableSlotCount,
            AllocationBlockedSlotCount: allocation.BlockedSlotCount,
            AllocationFallbackSlotCount: allocation.FallbackSlotCount,
            ReuseRouteId: reuse.ReusedRouteId,
            ReuseCacheHit: reuse.CacheHit,
            PathPointCount: path.PathPointCount,
            PathTouchedTileCount: path.TouchedTileCount,
            PathMacroRouteChunkCount: path.MacroRouteChunkCount,
            WaypointEditRevision: waypoint.EditRevision,
            ObstacleLoadedCount: obstacle.LoadedStaticObstacleCount,
            ObstacleSolverActiveCount: obstacle.SolverActiveStaticObstacleCount,
            RuntimeBakeAuthoringRevision: runtimeBake.AuthoringRevision,
            RuntimeBakeUpdateRevision: runtimeBake.UpdateRevision,
            RuntimeBakeDraftPointCount: runtimeBake.DraftPointCount,
            RuntimeBakePolygonCount: runtimeBake.AuthoredPolygonCount,
            RuntimeBakeDirtyChunkCount: runtimeBake.DirtyChunkCount,
            RuntimeBakeDiagnosticRevision: runtimeNavData.NavDataRevision,
            ViewportWidth: QuantizeViewport(viewportResolution.X),
            ViewportHeight: QuantizeViewport(viewportResolution.Y));
    }

    private static int QuantizeViewport(float value)
    {
        return value > 0f && float.IsFinite(value)
            ? (int)MathF.Round(value)
            : 0;
    }

    private readonly record struct FocusedPanelRefreshKey(
        MassNavigationShowcaseStepId StepId,
        int ActionRevision,
        int DebugFlags,
        uint SelectionRevision,
        int StructuralChangeRevision,
        int FlowWorkAreaRevision,
        int SelectedCount,
        int LastCommandSelectionCount,
        int ActiveOrderGroupCount,
        int UnitsWithTargets,
        int AllocationRouteId,
        int AllocationSlotCount,
        int AllocationReachableSlotCount,
        int AllocationBlockedSlotCount,
        int AllocationFallbackSlotCount,
        int ReuseRouteId,
        bool ReuseCacheHit,
        int PathPointCount,
        int PathTouchedTileCount,
        int PathMacroRouteChunkCount,
        int WaypointEditRevision,
        int ObstacleLoadedCount,
        int ObstacleSolverActiveCount,
        int RuntimeBakeAuthoringRevision,
        int RuntimeBakeUpdateRevision,
        int RuntimeBakeDraftPointCount,
        int RuntimeBakePolygonCount,
        int RuntimeBakeDirtyChunkCount,
        int RuntimeBakeDiagnosticRevision,
        int ViewportWidth,
        int ViewportHeight);

    private UiElementBuilder BuildRoot(ReactiveContext<MassNavigationPanelState> context)
    {
        var state = context.State;
        if (!state.Visible)
        {
            return Ui.Card(Ui.Text("Mass Navigation").FontSize(20f).Bold().Color("#F8FBFF"))
                .Width(420f)
                .Padding(14f)
                .Radius(18f)
                .Background("#111C2A")
                .Absolute(16f, 16f)
                .ZIndex(20);
        }

        return Ui.Panel(BuildDiagnosticsPanel(state))
            .Absolute(0f, 0f)
            .ZIndex(20);
    }

    private UiElementBuilder BuildDiagnosticsPanel(MassNavigationPanelState state)
    {
        if (state.ShowcaseFocusedPanel)
        {
            return BuildFocusedShowcasePanel(state);
        }

        return Ui.Card(
                Ui.Text("Mass Navigation").FontSize(20f).Bold().Color("#F8FBFF"),
                Ui.Text($"Operation Cockpit {state.ShowcaseStepIndex + 1}/{Math.Max(1, state.ShowcaseStepCount)}").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text(state.ShowcaseTitle).FontSize(16f).Bold().Color("#F8FBFF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Use case body: {state.ShowcaseOperationMode}").FontSize(11f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"User operation: {state.ShowcaseUserOperation}").FontSize(11f).Color("#F8FBFF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Live output: {state.ShowcaseLiveOutput}").FontSize(11f).Color("#8FE388").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Acceptance signal: {state.ShowcaseAcceptanceCheck}").FontSize(11f).Color("#A4F07A").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Debug meaning: {state.ShowcaseDebugLegend}").FontSize(11f).Color("#9FD8FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Production chain: {state.ShowcaseOperationContract}").FontSize(11f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Why this matters: {state.ShowcaseWhy}").FontSize(11f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Production gate: {state.ShowcaseProductionGate}").FontSize(11f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Current action: {state.ShowcaseLastActionText}").FontSize(11f).Color("#8FE388").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildPrimaryActionButton("Run Operation", RunCurrentShowcaseStep),
                        BuildActionButton("Next Objective", RequestNextShowcaseStep),
                        BuildActionButton("Prev Objective", RequestPreviousShowcaseStep))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Path Preview", RunShowcasePathPreview),
                        BuildActionButton("NavMesh View", () => SetShowcaseStep(MassNavigationShowcaseStepId.NavMeshBake)),
                        BuildActionButton("World/HPA", () => SetShowcaseStep(MassNavigationShowcaseStepId.WorldHpa)),
                        BuildActionButton("Layer/Cost", () => SetShowcaseStep(MassNavigationShowcaseStepId.LayerCosts)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Select Reuse Squad", PrepareOrderReuseSelection),
                        BuildActionButton("Select 10k Army", PrepareShowcaseLargeSelection),
                        BuildActionButton("Waypoint Edit", RunShowcaseWaypointEdit))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Strategy", () => SetShowcaseStep(MassNavigationShowcaseStepId.StrategySwitch)),
                        BuildActionButton("U14 FPS", () => SetShowcaseStep(MassNavigationShowcaseStepId.PerformanceDebug)),
                        BuildActionButton("U15 Debug", () => SetShowcaseStep(MassNavigationShowcaseStepId.DebugVisualBudget)),
                        BuildActionButton("U16 BakeTool", () => SetShowcaseStep(MassNavigationShowcaseStepId.BakeToolQuery)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("U1 VHTM", () => SetShowcaseStep(MassNavigationShowcaseStepId.VisualHeightmapBake)),
                        BuildActionButton("U2 Logic", () => SetShowcaseStep(MassNavigationShowcaseStepId.LogicHeightmapBake)),
                        BuildActionButton("U3 Areas", () => SetShowcaseStep(MassNavigationShowcaseStepId.LayerAreaEditor)),
                        BuildActionButton("U11 World", () => SetShowcaseStep(MassNavigationShowcaseStepId.LargeWorldStreaming)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("U12 Flow", () => SetShowcaseStep(MassNavigationShowcaseStepId.TenKFlow)),
                        BuildActionButton("U13 Obstacles", () => SetShowcaseStep(MassNavigationShowcaseStepId.StaticObstacleWorld)),
                        BuildActionButton("Draw Poly", ArmRuntimeObstacleAuthoring),
                        BuildActionButton("Update NavData", RequestRuntimeNavDataUpdate))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Debug Layers").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                        BuildActionButton(state.ShowcaseDebugNavMeshEnabled ? "NavMesh On" : "NavMesh Off", ToggleShowcaseNavMesh),
                        BuildActionButton(state.ShowcaseDebugHpaEnabled ? "HPA On" : "HPA Off", ToggleShowcaseHpa),
                        BuildActionButton(state.ShowcaseDebugPathEnabled ? "Path On" : "Path Off", ToggleShowcasePath),
                        BuildActionButton(state.ShowcaseDebugLayerCostEnabled ? "Costs On" : "Costs Off", ToggleShowcaseLayerCost),
                        BuildActionButton(state.ShowcaseDebugSlotsEnabled ? "Slots On" : "Slots Off", ToggleShowcaseSlots))
                    .Wrap()
                    .Gap(8f),
                Ui.Text($"NavMesh sample {(state.ShowcaseNavMeshSampleAvailable ? "loaded" : "missing")} tile {state.ShowcaseNavMeshChunkX},{state.ShowcaseNavMeshChunkY} layer {state.ShowcaseNavMeshLayer} profile {state.ShowcaseNavMeshProfileId} triangles {state.ShowcaseNavMeshTriangleCount} portals {state.ShowcaseNavMeshPortalCount} minClearance {state.ShowcaseNavMeshMinPortalClearanceCm} cm radius {state.ShowcaseNavMeshAgentRadiusCm} cm").FontSize(11f).Color("#9FD8FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Logic sample: blocked {state.ShowcaseNavMeshBlockedCellCount}  highCost {state.ShowcaseNavMeshHighCostCellCount}  water {state.ShowcaseNavMeshWaterCellCount}  ramp {state.ShowcaseNavMeshRampCellCount}").FontSize(11f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Layers: {state.ShowcaseNavMeshLayerLegend}").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Areas: {state.ShowcaseNavMeshAreaLegend}").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Blocked mask: {state.ShowcaseNavMeshBlockedSource}").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Mesh links: {state.ShowcaseNavMeshOffMeshLinkSource}").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        Ui.Button("Reset Scene", _ => RequestSceneReset())
                            .Background("#6F3B28")
                            .Color("#F8FBFF")
                            .Padding(12f, 8f)
                            .Radius(10f),
                        Ui.Button("Reset Camera", _ => RequestCameraReset())
                            .Background("#274A78")
                            .Color("#F8FBFF")
                            .Padding(12f, 8f)
                            .Radius(10f))
                    .Wrap()
                    .Gap(8f)
                    .Justify(UiJustifyContent.Start),
                Ui.Text("MassFlow uses a solver SoA as the hot-path workset; ECS owns authoring, commands, identity, gameplay truth, presentation handoff, and diagnostics.").FontSize(12f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Teams {state.TeamCount}  Agents/team {state.AgentsPerTeam}  Total {state.TotalAgents}  Selectable {state.ControllableAgents}  Obstacles {state.Blockers}").FontSize(13f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Selected {state.SelectedCount}  Rev {state.SelectionRevision}  Pending cmds {state.PendingCommandCount}").FontSize(13f).Color("#A4F07A"),
                Ui.Text($"Team target {state.SelectedTeamId}  Formation {state.FormationLabel}  Groups {state.FormationCount}  Rotation {state.FormationRotationDeg:0.0} deg").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"FPS {state.Fps:0}  Frame {state.FrameMs:0.0} ms  Performer {state.PerformerEmitMs:0.0} ms  Minimap {state.MinimapProjectionMs:0.0} ms").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"InputCollection: select {state.SelectionSyncHzObserved:0.0} Hz  control {state.ControlHzObserved:0.0} Hz  capture {state.CommandHzObserved:0.0} Hz").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"PostMovement: command {state.CommandDispatchHzObserved:0.0} Hz  mass {state.SimHzObserved:0.0}/{state.MassNavigationSimulationHz} Hz  Presentation: performer {state.PerformerHzObserved:0.0} Hz  hud {state.HudHzObserved:0.0} Hz  panel {state.PanelHzObserved:0.0} Hz").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Mass cadence: target {state.MassNavigationTargetUpdateHz} Hz  flow {state.MassNavigationFlowStepHz}/{state.MassNavigationFlowCrowdStampHz}/{state.MassNavigationFlowObstacleStampHz} Hz  resolve {state.MassNavigationHardResolveHz} Hz  sync {state.MassNavigationEntitySyncHz} Hz").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.LastActionText).FontSize(12f).Color("#8FE388").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("World Map").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Battlefield {state.WorldWidthCm / 100_000f:0.0}x{state.WorldHeightCm / 100_000f:0.0} km  landmarks {state.WorldMarkerCount}  active chunks {state.LoadedChunkCount} @ {state.StreamingChunkSizeCm} cm").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Flow area {state.FlowWorkAreaWidthCm / 100f:0}x{state.FlowWorkAreaHeightCm / 100f:0} m at ({state.FlowWorkAreaCenterXCm},{state.FlowWorkAreaCenterYCm}) cm  rev {state.FlowWorkAreaRevision}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Solver cache {state.SolverWindowWidthCm / 100f:0}x{state.SolverWindowHeightCm / 100f:0} m at ({state.SolverWindowCenterXCm},{state.SolverWindowCenterYCm}) cm  driver {state.SolverWindowDriver}").FontSize(12f).Color("#9FD8FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Camera target ({state.CameraTargetX:0},{state.CameraTargetY:0}) cm  distance {state.CameraDistanceCm:0}  chunk updates {state.StreamingWindowUpdatesFrame}").FontSize(12f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Last command target ({state.CommandFocusX:0},{state.CommandFocusY:0}) cm  selected payload {state.LastCommandSelectionCount}  invalid orders {state.CommandRejectsFrame}/{state.CommandRejectsTotal}").FontSize(11f).Color(state.CommandRejectsFrame > 0 ? "#FF9A73" : "#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton("Full Map", RequestStrategicWorldCamera),
                        BuildActionButton("Field Camera", RequestCameraReset))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Debug Landmarks").FontSize(12f).Bold().Color("#F4C77D"),
                BuildKnownContactRow(),
                Ui.Text("Formation").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                        BuildFormationButton("None", MassNavigationFormationMode.None, state.FormationLabel),
                        BuildFormationButton("Line", MassNavigationFormationMode.Line, state.FormationLabel),
                        BuildFormationButton("Square", MassNavigationFormationMode.Square, state.FormationLabel),
                        BuildFormationButton("Circle", MassNavigationFormationMode.Circle, state.FormationLabel),
                        BuildFormationButton("Wedge", MassNavigationFormationMode.Wedge, state.FormationLabel))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("MassNavigation Runtime").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Immediate knobs: Logic {state.LogicHz} Hz  Budget {state.SimulationBudgetMs} ms  Slice {state.SimulationSliceLimit}  Recovery {(state.ArrivalRecoveryEnabled ? "On" : "Off")}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton("-1 ms", () => AdjustSimulationBudget(-1)),
                        BuildActionButton("+1 ms", () => AdjustSimulationBudget(1)),
                        BuildActionButton("-30 slice", () => AdjustSimulationSlices(-30)),
                        BuildActionButton("+30 slice", () => AdjustSimulationSlices(30)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton(state.ArrivalRecoveryEnabled ? "Recovery On" : "Recovery Off", ToggleArrivalRecovery),
                        BuildActionButton("Timeout -250", () => AdjustArrivalTimeoutMs(-250)),
                        BuildActionButton("Timeout +250", () => AdjustArrivalTimeoutMs(250)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Progress -10", () => AdjustArrivalProgressCm(-10)),
                        BuildActionButton("Progress +10", () => AdjustArrivalProgressCm(10)),
                        BuildActionButton("Wake -10", () => AdjustArrivalWakePushCm(-10)),
                        BuildActionButton("Wake +10", () => AdjustArrivalWakePushCm(10)),
                        BuildActionButton("Retry -1", () => AdjustArrivalMaxRetries(-1)),
                        BuildActionButton("Retry +1", () => AdjustArrivalMaxRetries(1)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Engine Policy Only").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Physics {state.PhysicsHz} Hz / max {state.PhysicsMaxStepsPerFixedTick}  Nav {state.NavigationHz} Hz / max {state.NavigationMaxStepsPerFixedTick}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("LogicHz follows Engine/clock.json and drives the MassNavigation simulation. Physics/Nav buttons apply engine policy, while this mass crowd runtime uses its own configured cadence.").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton("Phys -5", () => AdjustPhysicsHz(-5)),
                        BuildActionButton("Phys +5", () => AdjustPhysicsHz(5)),
                        BuildActionButton("Phys Max-1", () => AdjustPhysicsMaxSteps(-1)),
                        BuildActionButton("Phys Max+1", () => AdjustPhysicsMaxSteps(1)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Nav -5", () => AdjustNavigationHz(-5)),
                        BuildActionButton("Nav +5", () => AdjustNavigationHz(5)),
                        BuildActionButton("Nav Max-1", () => AdjustNavigationMaxSteps(-1)),
                        BuildActionButton("Nav Max+1", () => AdjustNavigationMaxSteps(1)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("MassNavigation Flow").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Flow {(state.FlowEnabled ? "On" : "Off")}  Crowd budget {state.FlowIterations}  Hz step/crowd/obs {state.MassNavigationFlowStepHz}/{state.MassNavigationFlowCrowdStampHz}/{state.MassNavigationFlowObstacleStampHz}  resolve {state.MassNavigationHardResolveHz}  sync {state.MassNavigationEntitySyncHz}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Flow rebuild(last) {state.FlowFieldRebuildMs:0.0} ms  target-driven  rebuild/frame {state.FlowReconcileFrame}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Cadence knobs hot-apply through the MassFlow config scheduler; entity writeback uses a solver dirty queue at the configured sync Hz.").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton(state.FlowEnabled ? "Flow On" : "Flow Off", ToggleFlowEnabled),
                        BuildActionButton("Iter -512", () => AdjustFlowIterations(-512)),
                        BuildActionButton("Iter +512", () => AdjustFlowIterations(512)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Step Hz -1", () => AdjustFlowStepHz(-1)),
                        BuildActionButton("Step Hz +1", () => AdjustFlowStepHz(1)),
                        BuildActionButton("Crowd Hz -1", () => AdjustFlowCrowdHz(-1)),
                        BuildActionButton("Crowd Hz +1", () => AdjustFlowCrowdHz(1)),
                        BuildActionButton("Obs Hz -1", () => AdjustFlowObstacleHz(-1)),
                        BuildActionButton("Obs Hz +1", () => AdjustFlowObstacleHz(1)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Resolve Hz -1", () => AdjustHardResolveHz(-1)),
                        BuildActionButton("Resolve Hz +1", () => AdjustHardResolveHz(1)),
                        BuildActionButton("Sync Hz -1", () => AdjustEntitySyncHz(-1)),
                        BuildActionButton("Sync Hz +1", () => AdjustEntitySyncHz(1)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Scene Rebuild Required").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                        BuildActionButton("-2k", AdjustTotalAgentsDown),
                        BuildActionButton("5k", () => SetTotalAgents(5_000)),
                        BuildActionButton("10k", () => SetTotalAgents(10_000)),
                        BuildActionButton("20k", () => SetTotalAgents(20_000)),
                        BuildActionButton("40k", () => SetTotalAgents(40_000)),
                        BuildActionButton("+2k", AdjustTotalAgentsUp))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Semantic Contract").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text(state.ObstacleSemanticsText).FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.TargetSemanticsText).FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.ArrivalSemanticsText).FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.YieldSemanticsText).FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Team Target").FontSize(12f).Bold().Color("#F4C77D"),
                BuildTeamTargetRow(),
                Ui.Text("Diagnostics").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Select {state.SelectionSyncMs:0.0} ms  Command {state.CommandApplyMs:0.0} ms  Group {state.FormationTargetMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Prep {state.StepPrepMs:0.0} ms  Steer {state.LocalSteeringMs:0.0} ms  Resolve {state.HardResolveMs:0.0} ms  Sim {state.SimStepMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Entity sync {state.EntitySyncMs:0.0} ms  Performer cmd {state.PerformerCommandMs:0.0} ms  xform {state.PerformerTransformMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Performer minimap {state.PerformerMinimapMarkerMs:0.0} ms  markers {state.PerformerMarkers} drop {state.PerformerMarkersDropped}  screen {state.MinimapScreenMarkers} drop {state.MinimapScreenMarkersDropped}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Crowd in view {state.CrowdInViewEntities}  submitted {state.CrowdSubmittedEntities}  obs {state.SubmittedObstacles}  ECS visible {state.EcsVisibleEntities}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Cull {state.CameraCullingMs:0.0} ms  Presenter {state.CameraPresenterMs:0.0} ms  HUD proj {state.WorldHudProjectionMs:0.0} ms").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"UI in {state.UiInputMs:0.0} ms  UI render {state.UiRenderMs:0.0} ms  upload {state.UiUploadMs:0.0} ms  overlay {state.ScreenOverlayBuildMs:0.0}/{state.ScreenOverlayDrawMs:0.0} ms").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Render accounted {state.RenderAccountedMs:0.0} ms  leftover {state.RenderUnaccountedMs:0.0} ms  composite skip {state.CompositeSkipCountLastSecond}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Camera target ({state.CameraTargetX:0.0}, {state.CameraTargetY:0.0}) cm  distance {state.CameraDistanceCm:0.0} cm").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Selection snapshots/frame {state.SelectionSnapshotsFrame}  commands/frame {state.CommandCountFrame}").FontSize(12f).Color("#DFAF6C"),
                Ui.Text($"Structural changes/frame {state.StructuralChangesFrame}  spawn/reset {state.ScenarioSpawnCount}/{state.SceneResetCount}").FontSize(12f).Color("#F18C7F"),
                Ui.Text($"Camera budget {state.CameraBudgetUpdatesFrame}/{state.CameraBudgetUpdatesTotal}  solver move {state.SolverWindowMovesFrame}/{state.SolverWindowMovesTotal}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Use Ludots box selection to grab any units. Right click with a selection issues a formation move. Right click with no selection redirects the chosen team target. Hold Q/E to rotate selected formations.").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal))
            .Width(460f)
            .Padding(14f)
            .Gap(8f)
            .Radius(18f)
            .Background("#111C2A")
            .Absolute(16f, 16f)
            .ZIndex(20);
    }

    private UiElementBuilder BuildFocusedShowcasePanel(MassNavigationPanelState state)
    {
        float viewportWidth = Math.Max(1f, state.ViewportWidth);
        float viewportHeight = Math.Max(1f, state.ViewportHeight);
        float shortEdge = MathF.Min(viewportWidth, viewportHeight);
        float railWidth = Math.Clamp(shortEdge * 0.24f, 176f, 232f);
        railWidth = MathF.Min(railWidth, MathF.Max(144f, viewportWidth * 0.24f));
        bool singleStep = state.ShowcaseStepCount <= 1;
        float railHeight = singleStep
            ? Math.Clamp(viewportHeight * 0.16f, 108f, 132f)
            : Math.Clamp(viewportHeight * 0.22f, 140f, 190f);
        railHeight = MathF.Min(railHeight, Math.Max(104f, viewportHeight * (singleStep ? 0.22f : 0.30f)));
        float marginX = Math.Clamp(viewportWidth * 0.014f, 10f, 22f);
        float marginY = Math.Clamp(viewportHeight * 0.014f, 8f, 16f);
        float left = MathF.Max(marginX, viewportWidth - railWidth - marginX);
        float top = MathF.Max(viewportHeight * 0.48f, viewportHeight - railHeight - marginY);

        return BuildFocusedRightRail(state, railWidth, railHeight, left, top);
    }

    private UiElementBuilder BuildFocusedRightRail(
        MassNavigationPanelState state,
        float railWidth,
        float railHeight,
        float left,
        float top)
    {
        string stepLabel = $"Step {state.ShowcaseStepIndex + 1}/{Math.Max(1, state.ShowcaseStepCount)}";
        if (state.ShowcaseStepCount <= 1)
        {
            return Ui.Card(
                    Ui.Text($"{stepLabel} {state.ShowcaseTitle}")
                        .FontSize(9f)
                        .Bold()
                        .Color("#F8FBFF")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(BuildFocusedUserOperationLine(state))
                        .FontSize(9f)
                        .Color("#F8FBFF")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    BuildFocusedPrimaryRow(state),
                    Ui.Text($"Live: {BuildFocusedLiveLine(state)}")
                        .FontSize(9f)
                        .Bold()
                        .Color("#8FE388")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Pass: {BuildFocusedPassLine(state)}")
                        .FontSize(9f)
                        .Bold()
                        .Color("#A4F07A")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"FPS {state.Fps:0}  selected {state.SelectedCount}/{state.TotalAgents}  cmd {state.LastCommandSelectionCount}")
                        .FontSize(9f)
                        .Color("#F2D483")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Id("mass-navigation-focused-hud")
                .Width(railWidth)
                .Height(railHeight)
                .Padding(7f)
                .Gap(4f)
                .Radius(4f)
                .Background("#070C12")
                .Absolute(left, top)
                .ZIndex(20);
        }

        return Ui.Card(
                Ui.Text($"{stepLabel} {state.ShowcaseTitle}")
                    .FontSize(9f)
                    .Bold()
                    .Color("#F8FBFF")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(BuildFocusedUserOperationLine(state))
                    .FontSize(9f)
                    .Color("#F8FBFF")
                    .WhiteSpace(UiWhiteSpace.Normal),
                BuildFocusedPrimaryRow(state),
                Ui.Text($"Live: {BuildFocusedLiveLine(state)}")
                    .FontSize(9f)
                    .Bold()
                    .Color("#8FE388")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Pass: {BuildFocusedPassLine(state)}")
                    .FontSize(9f)
                    .Bold()
                    .Color("#A4F07A")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"FPS {state.Fps:0}  {state.FrameMs:0.0}ms  selected {state.SelectedCount}/{state.TotalAgents}  cmd {state.LastCommandSelectionCount}")
                    .FontSize(9f)
                    .Color("#F2D483")
                    .WhiteSpace(UiWhiteSpace.Normal),
                BuildFocusedDebugStrip(state))
            .Id("mass-navigation-focused-hud")
            .Width(railWidth)
            .Height(railHeight)
            .Padding(7f)
            .Gap(4f)
            .Radius(4f)
            .Background("#070C12")
            .Absolute(left, top)
            .ZIndex(20);
    }

    private UiElementBuilder BuildFocusedPrimaryRow(MassNavigationPanelState state)
    {
        if (state.ShowcaseStepCount <= 1)
        {
            if (_guide?.CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery)
            {
                return Ui.Row(
                        BuildPrimaryActionButton(state.ShowcasePrimaryActionLabel, RunPrimaryShowcaseAction).Width(116f),
                        BuildFocusedContextButton(state),
                        BuildCompactToggleButton("World Path", RunRuntimeBakeWorldPath),
                        BuildCompactToggleButton("Mesh View", RequestRuntimeBakeMeshCamera))
                    .Wrap()
                    .Gap(4f);
            }

            return Ui.Row(
                    BuildPrimaryActionButton(state.ShowcasePrimaryActionLabel, RunPrimaryShowcaseAction).Width(128f),
                    BuildFocusedContextButton(state),
                    BuildCompactToggleButton("Map", RequestStrategicWorldCamera))
                .Wrap()
                .Gap(4f);
        }

        return Ui.Row(
                BuildPrimaryActionButton(state.ShowcasePrimaryActionLabel, RunPrimaryShowcaseAction).Width(128f),
                BuildOptionalStepButton("Next", RequestNextShowcaseStep, enabled: true),
                BuildOptionalStepButton("Prev", RequestPreviousShowcaseStep, enabled: true))
            .Wrap()
            .Gap(4f);
    }

    private UiElementBuilder BuildFocusedDebugStrip(MassNavigationPanelState state)
    {
        return Ui.Column(
                Ui.Row(
                        BuildCompactToggleButton(state.ShowcaseDebugNavMeshEnabled ? "Nav" : "Nav", ToggleShowcaseNavMesh),
                        BuildCompactToggleButton(state.ShowcaseDebugHpaEnabled ? "HPA" : "HPA", ToggleShowcaseHpa),
                        BuildCompactToggleButton(state.ShowcaseDebugPathEnabled ? "Path" : "Path", ToggleShowcasePath),
                        BuildCompactToggleButton(state.ShowcaseDebugLayerCostEnabled ? "Cost" : "Cost", ToggleShowcaseLayerCost),
                        BuildCompactToggleButton(state.ShowcaseDebugSlotsEnabled ? "Slots" : "Slots", ToggleShowcaseSlots),
                        BuildCompactToggleButton("Field", RequestCameraReset),
                        BuildCompactToggleButton("Map", RequestStrategicWorldCamera))
                    .Wrap()
                    .Gap(3f))
            .Gap(2f);
    }

    private UiElementBuilder BuildFocusedContextButton(MassNavigationPanelState state)
    {
        if (_guide == null)
        {
            return BuildActionButton("Field", RequestCameraReset);
        }

        return _guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.PathOnly or
            MassNavigationShowcaseStepId.WorldHpa or
            MassNavigationShowcaseStepId.StrategySwitch =>
                BuildActionButton("Re-arm", RunCurrentShowcaseStep),
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                BuildActionButton("Edit", RunShowcaseWaypointEdit),
            MassNavigationShowcaseStepId.BakeToolQuery when _guide.RuntimeBakeAuthoring.ObstacleAuthoringArmed &&
                _guide.RuntimeBakeAuthoring.DraftPointCount >= 3 =>
                BuildActionButton("Close Poly", CloseRuntimeObstaclePolygon),
            MassNavigationShowcaseStepId.BakeToolQuery when _guide.RuntimeBakeAuthoring.ObstacleAuthoringArmed =>
                BuildActionButton("Stop Draw", CancelRuntimeObstacleAuthoring),
            MassNavigationShowcaseStepId.BakeToolQuery =>
                BuildActionButton("Draw Poly", ArmRuntimeObstacleAuthoring),
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts =>
                BuildActionButton("Bake", () => SetShowcaseStep(_guide.CurrentStepId)),
            _ => BuildActionButton("Field", RequestCameraReset),
        };
    }

    private UiElementBuilder BuildFocusedActionRow(MassNavigationPanelState state)
    {
        _ = state;
        if (_guide == null)
        {
            return Ui.Row(Array.Empty<UiElementBuilder>()).Gap(8f);
        }

        var buttons = new System.Collections.Generic.List<UiElementBuilder>(4);
        switch (_guide.CurrentStepId)
        {
            case MassNavigationShowcaseStepId.PathOnly:
            case MassNavigationShowcaseStepId.WorldHpa:
            case MassNavigationShowcaseStepId.StrategySwitch:
                buttons.Add(BuildActionButton("Re-arm Picking", RunCurrentShowcaseStep));
                buttons.Add(BuildActionButton("Field Camera", RequestCameraReset));
                break;
            case MassNavigationShowcaseStepId.OrderReuse:
                buttons.Add(BuildActionButton("Field Camera", RequestCameraReset));
                break;
            case MassNavigationShowcaseStepId.TargetAllocation:
            case MassNavigationShowcaseStepId.TenKFlow:
                break;
            case MassNavigationShowcaseStepId.WaypointAuthoring:
                buttons.Add(BuildActionButton("Re-arm Waypoint Edit", RunShowcaseWaypointEdit));
                buttons.Add(BuildActionButton("Field Camera", RequestCameraReset));
                break;
            case MassNavigationShowcaseStepId.VisualHeightmapBake:
            case MassNavigationShowcaseStepId.LogicHeightmapBake:
            case MassNavigationShowcaseStepId.LayerAreaEditor:
            case MassNavigationShowcaseStepId.NavMeshBake:
            case MassNavigationShowcaseStepId.LayerCosts:
            case MassNavigationShowcaseStepId.BakeToolQuery:
                buttons.Add(BuildActionButton("World Path", RunRuntimeBakeWorldPath));
                buttons.Add(BuildActionButton(
                    _guide.CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery ? "Mesh View" : "Focus Bake Data",
                    () => SetShowcaseStep(_guide.CurrentStepId)));
                buttons.Add(BuildActionButton("Full Map", RequestStrategicWorldCamera));
                break;
            case MassNavigationShowcaseStepId.LargeWorldStreaming:
                buttons.Add(BuildActionButton("Full Map", RequestStrategicWorldCamera));
                buttons.Add(BuildActionButton("Field Camera", RequestCameraReset));
                break;
            case MassNavigationShowcaseStepId.StaticObstacleWorld:
            case MassNavigationShowcaseStepId.PerformanceDebug:
            case MassNavigationShowcaseStepId.DebugVisualBudget:
                buttons.Add(BuildActionButton("Focus View", () => SetShowcaseStep(_guide.CurrentStepId)));
                buttons.Add(BuildActionButton("Full Map", RequestStrategicWorldCamera));
                break;
        }

        return buttons.Count == 0
            ? Ui.Row(Array.Empty<UiElementBuilder>()).Height(0f).Gap(0f)
            : Ui.Row(buttons.ToArray()).Wrap().Gap(6f);
    }

    private static UiElementBuilder BuildActionButton(string label, Action onClick)
    {
        return Ui.Button(label, _ => onClick())
            .Background("#182436")
            .Color("#F8FBFF")
            .FontSize(10f)
            .Padding(8f, 6f)
            .Radius(5f);
    }

    private static UiElementBuilder BuildPrimaryActionButton(string label, Action onClick)
    {
        return Ui.Button(label, _ => onClick())
            .Background("#315D35")
            .Color("#F8FBFF")
            .FontSize(12f)
            .Padding(10f, 7f)
            .Radius(6f);
    }

    private static UiElementBuilder BuildOptionalStepButton(string label, Action onClick, bool enabled)
    {
        return Ui.Button(label, _ =>
            {
                if (enabled)
                {
                    onClick();
                }
            })
            .Background(enabled ? "#182436" : "#263040")
            .Color(enabled ? "#F8FBFF" : "#8EA2BD")
            .FontSize(10f)
            .Padding(8f, 6f)
            .Radius(5f);
    }

    private static UiElementBuilder BuildCompactToggleButton(string label, Action onClick)
    {
        return Ui.Button(label, _ => onClick())
            .Background("#182436")
            .Color("#F8FBFF")
            .FontSize(9f)
            .Padding(6f, 4f)
            .Radius(4f)
            .FlexShrink(0f);
    }

    private static string ShortenForHud(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string singleLine = value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return singleLine.Length <= maxCharacters
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, Math.Max(0, maxCharacters - 3)), "...");
    }

    private static string BuildFocusedUserOperationLine(MassNavigationPanelState state)
    {
        return state.ShowcaseTitle switch
        {
            string title when title.Contains("Large-selection target allocation", StringComparison.Ordinal) =>
                "Click Select 10k Army, then Right-click one destination.",
            string title when title.Contains("10k commanded flow", StringComparison.Ordinal) =>
                "Click Select 10k Army, then Right-click one destination.",
            string title when title.Contains("Order reuse", StringComparison.Ordinal) =>
                "Click Select Reuse Squad, then Right-click the same and nearby destinations.",
            string title when title.Contains("Path-only", StringComparison.Ordinal) =>
                "Click Pick Path Preview, left-click start, right-click goal.",
            string title when title.Contains("World and HPA", StringComparison.Ordinal) =>
                "Click Pick HPA Route, left-click start chunk, right-click goal chunk.",
            string title when title.Contains("Waypoint", StringComparison.Ordinal) =>
                "Pick start and goal, then edit the authored waypoint.",
            string title when title.Contains("Runtime bake", StringComparison.Ordinal) =>
                "Pick route, Draw Poly, close it, then Update NavData.",
            _ => ShortenForHud(state.ShowcaseUserOperation, 86),
        };
    }

    private static string BuildFocusedCompactContractLine(MassNavigationPanelState state)
    {
        return state.ShowcaseTitle switch
        {
            string title when title.Contains("Large-selection target allocation", StringComparison.Ordinal) =>
                "Input: selected 10k army plus one right-click destination.",
            string title when title.Contains("10k commanded flow", StringComparison.Ordinal) =>
                "Input: selected 10k army plus one right-click destination.",
            string title when title.Contains("Order reuse", StringComparison.Ordinal) =>
                "Input: same/near destination orders reuse cached route.",
            string title when title.Contains("Path-only", StringComparison.Ordinal) =>
                "Input: picked endpoints only; no order submitted.",
            string title when title.Contains("World and HPA", StringComparison.Ordinal) =>
                "Input: picked endpoints expose chunk route and portals.",
            string title when title.Contains("Waypoint", StringComparison.Ordinal) =>
                "Input: editable waypoints regenerate immutable pathpoints.",
            string title when title.Contains("Runtime bake", StringComparison.Ordinal) =>
                "Input: route picks plus runtime authored obstacle polygon.",
            _ => ShortenForHud(state.ShowcaseOperationContract, 64),
        };
    }

    private static string BuildFocusedLiveLine(MassNavigationPanelState state)
    {
        return state.ShowcaseTitle switch
        {
            string title when title.Contains("10k commanded flow", StringComparison.Ordinal) =>
                $"selected={state.SelectedCount}; commanded={state.LastCommandSelectionCount}; slots={ResolveFocusedSlotCount(state)}; moving/settled/stuck/waiting={ResolveFocusedMovementBuckets(state)}",
            string title when title.Contains("Large-selection target allocation", StringComparison.Ordinal) =>
                ShortenForHud(state.ShowcaseLiveOutput, 78),
            _ => ShortenForHud(state.ShowcaseLiveOutput, 76),
        };
    }

    private static string BuildFocusedPassLine(MassNavigationPanelState state)
    {
        return state.ShowcaseTitle switch
        {
            string title when title.Contains("10k commanded flow", StringComparison.Ordinal) =>
                "accounted=10000; blocked=0; fallback=0; flow=On.",
            string title when title.Contains("Large-selection target allocation", StringComparison.Ordinal) =>
                "Gate target selected=10000; slots>=10000; reachable>=10000; blocked=0; fallback=0.",
            _ => ShortenForHud(state.ShowcaseAcceptanceCheck, 70),
        };
    }

    private static string ResolveFocusedSlotCount(MassNavigationPanelState state)
    {
        string live = state.ShowcaseLiveOutput;
        const string slotsToken = "slots=";
        int start = live.IndexOf(slotsToken, StringComparison.Ordinal);
        if (start < 0)
        {
            return "0";
        }

        start += slotsToken.Length;
        int end = live.IndexOf(';', start);
        return end > start ? live[start..end] : live[start..];
    }

    private static string ResolveFocusedMovementBuckets(MassNavigationPanelState state)
    {
        string live = state.ShowcaseLiveOutput;
        const string bucketsToken = "moving/settled/stuck/waiting=";
        int start = live.IndexOf(bucketsToken, StringComparison.Ordinal);
        if (start < 0)
        {
            return "0/0/0/0";
        }

        start += bucketsToken.Length;
        int end = live.IndexOf(';', start);
        return end > start ? live[start..end] : live[start..];
    }

    private static string BuildShowcaseUserOperation(MassNavigationShowcaseGuideRuntime guide)
    {
        return guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.PathOnly =>
                "1. Click Pick Path Preview. 2. Left-click a start point. 3. Right-click a goal point. Expected user action is only a query, not a move order.",
            MassNavigationShowcaseStepId.WorldHpa =>
                "1. Click Pick HPA Route. 2. Left-click a far-world start chunk. 3. Right-click a far-world goal chunk. Read the numbered chunk sequence and portal crossings.",
            MassNavigationShowcaseStepId.StrategySwitch =>
                "1. Click Pick Strategy Route. 2. Left-click one start and right-click one goal. Compare RoadGraph, NavMesh, and Hybrid candidates for the same intent.",
            MassNavigationShowcaseStepId.OrderReuse =>
                "1. Click Select Reuse Squad. 2. Right-click the same destination twice. 3. Right-click a nearby point. The route id should be reused.",
            MassNavigationShowcaseStepId.TargetAllocation =>
                "1. Click Select 10k Army or box-select the army. 2. Right-click one destination. The click becomes a formation footprint with 10k logical slots.",
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                "1. Pick start and goal. 2. Click Edit Waypoint Plan. 3. Left-click the editable midpoint. Waypoints change; old pathpoints are invalidated.",
            MassNavigationShowcaseStepId.TenKFlow =>
                "1. Click Select 10k Army or box-select the army. 2. Right-click one destination. Units receive shared orders, slots, and flow targets.",
            MassNavigationShowcaseStepId.LargeWorldStreaming =>
                "Use Full Map and Active Window. Pan or jump across the 64km world and verify loaded chunks move while streamed-out data remains explicit.",
            MassNavigationShowcaseStepId.StaticObstacleWorld =>
                "Open the obstacle world view. Compare 40k authored/baked/loaded buckets with the active solver subset around the camera window.",
            MassNavigationShowcaseStepId.BakeToolQuery =>
                "1. Left-click a route start and right-click a route goal. 2. Click Draw Poly. 3. Left-click obstacle vertices. 4. Right-click or Close Poly. 5. Click Update NavData.",
            MassNavigationShowcaseStepId.PerformanceDebug =>
                "Run normal play with diagnostics visible. Watch frame time, p95 scope, draw counts, and whether the scenario is using real loaded bake data.",
            MassNavigationShowcaseStepId.DebugVisualBudget =>
                "Toggle debug layers on/off and check that route, slot, HPA, obstacle, and NavMesh visuals stay sampled and bounded.",
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts =>
                "Use the Raylib bake workbench for this case. The runtime panel only previews the loaded bake data; editor validation happens through paint/bake/query/save in the tool.",
            _ => guide.CurrentStep.PlayerInput
        };
    }

    private static string BuildShowcaseLiveOutput(
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide)
    {
        MassNavigationPathOnlyQueryDiagnostics path = simulation.AcceptanceDiagnostics.PathOnlyQuery;
        MassNavigationTargetAllocationDiagnostics allocation = simulation.AcceptanceDiagnostics.TargetAllocation;
        MassNavigationOrderReuseDiagnostics reuse = simulation.AcceptanceDiagnostics.OrderReuse;
        MassNavigationHpaMacroDiagnostics hpa = simulation.AcceptanceDiagnostics.HpaMacro;
        MassNavigationNavMeshGuideSample nav = guide.NavMeshSample;
        return guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.PathOnly =>
                $"query={path.Status}; pathpoints={path.PathPointCount}; corridorPortals={path.CorridorPortalCount}; noOrder={path.NoOrderSubmitted}; orderDelta={guide.LastActionOrderDelta}",
            MassNavigationShowcaseStepId.WorldHpa =>
                $"world={hpa.MacroChunkColumns}x{hpa.MacroChunkRows} chunks; routeChunks={hpa.SampleRouteChunkCount}; portals={hpa.SamplePortalCount}; start={hpa.StartMacroChunkX},{hpa.StartMacroChunkY}; goal={hpa.GoalMacroChunkX},{hpa.GoalMacroChunkY}; source={hpa.RouteSource}",
            MassNavigationShowcaseStepId.StrategySwitch =>
                BuildStrategyOutput(simulation),
            MassNavigationShowcaseStepId.OrderReuse =>
                $"selected={simulation.SelectedCount}; cacheHit={reuse.CacheHit}; routeId={reuse.ReusedRouteId}; scope={reuse.ReuseScope}; fanout={reuse.FanoutCount}; cacheSize={reuse.RouteCacheSize}",
            MassNavigationShowcaseStepId.TargetAllocation =>
                $"selected={allocation.SelectedCount}; slots={allocation.SlotCount}; reachable={allocation.ReachableSlotCount}; blocked={allocation.BlockedSlotCount}; fallback={allocation.FallbackSlotCount}; routeId={allocation.AllocationRouteId}",
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                BuildWaypointOutput(simulation),
            MassNavigationShowcaseStepId.TenKFlow =>
                BuildTenKFlowOutput(simulation, allocation),
            MassNavigationShowcaseStepId.LargeWorldStreaming =>
                $"world={simulation.WorldWidthCm / 100000f:0}x{simulation.WorldHeightCm / 100000f:0}km; loadedChunks={simulation.LoadedChunkCount}; activeWindow={simulation.AcceptanceDiagnostics.HpaGraph.ActiveWindowChunkCount}; {BuildNavMeshCoverageLine(guide)}",
            MassNavigationShowcaseStepId.StaticObstacleWorld =>
                BuildObstacleOutput(simulation),
            MassNavigationShowcaseStepId.BakeToolQuery =>
                BuildRuntimeNavDataUpdateOutput(simulation, guide),
            MassNavigationShowcaseStepId.PerformanceDebug or MassNavigationShowcaseStepId.DebugVisualBudget =>
                $"fps={simulation.Cadence.SimulationHz}Hz simTarget; selected={simulation.SelectedCount}; screen overlays are sampled; active debug path/navmesh/hpa/slots={guide.DebugPathEnabled}/{guide.DebugNavMeshEnabled}/{guide.DebugHpaEnabled}/{guide.DebugSlotsEnabled}",
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts =>
                $"navTile={nav.ChunkX},{nav.ChunkY}; layer={nav.Layer}; profile={nav.ProfileId}; triangles={nav.TriangleCount}; portals={nav.PortalCount}; radius={nav.AgentRadiusCm}cm; blocked/highCost/water={nav.BlockedCellCount}/{nav.HighCostCellCount}/{nav.WaterCellCount}; {BuildNavMeshCoverageLine(guide)}",
            _ => guide.LastActionText
        };
    }

    private static string BuildShowcaseAcceptanceCheck(
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide)
    {
        MassNavigationPathOnlyQueryDiagnostics path = simulation.AcceptanceDiagnostics.PathOnlyQuery;
        MassNavigationTargetAllocationDiagnostics allocation = simulation.AcceptanceDiagnostics.TargetAllocation;
        MassNavigationOrderReuseDiagnostics reuse = simulation.AcceptanceDiagnostics.OrderReuse;
        return guide.CurrentStepId switch
        {
            MassNavigationShowcaseStepId.PathOnly =>
                $"PASS when pathpoints>0, corridor/portals are visible, NoOrderSubmitted=true, and orderDelta=0. Current: {path.PathPointCount}>0 / {path.NoOrderSubmitted} / {guide.LastActionOrderDelta}.",
            MassNavigationShowcaseStepId.WorldHpa =>
                $"PASS when start/goal chunks, numbered route chunks, portal crossings, and active window are visible. Current routeChunks={simulation.AcceptanceDiagnostics.HpaMacro.SampleRouteChunkCount}, portals={simulation.AcceptanceDiagnostics.HpaMacro.SamplePortalCount}.",
            MassNavigationShowcaseStepId.StrategySwitch =>
                "PASS when graph, navmesh, and hybrid evidence all refer to the same start/goal query and list strategy/cost/source.",
            MassNavigationShowcaseStepId.OrderReuse =>
                $"PASS when the second same-point order and near-point order hit the same route bucket. Current hit={reuse.CacheHit}, scope={reuse.ReuseScope}, routeId={reuse.ReusedRouteId}.",
            MassNavigationShowcaseStepId.TargetAllocation =>
                $"PASS when selected=10000, slots>=10000, reachable>=10000, blocked=0, fallback=0. Current {allocation.SelectedCount}/{allocation.SlotCount}/{allocation.ReachableSlotCount}/{allocation.BlockedSlotCount}/{allocation.FallbackSlotCount}.",
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                BuildWaypointAcceptance(simulation, guide),
            MassNavigationShowcaseStepId.TenKFlow =>
                BuildTenKFlowHudAcceptance(simulation, allocation),
            MassNavigationShowcaseStepId.LargeWorldStreaming =>
                "PASS when 64km, 256x256 chunks, loaded active window, and streamed-out/notLoaded counters are visible together.",
            MassNavigationShowcaseStepId.StaticObstacleWorld =>
                "PASS when 40k authored/baked/loaded obstacle counts are visible and solver-active subset stays bounded by capacity.",
            MassNavigationShowcaseStepId.BakeToolQuery =>
                BuildRuntimeNavDataUpdateAcceptance(simulation, guide),
            MassNavigationShowcaseStepId.PerformanceDebug =>
                "PASS only with Raylib timing evidence meeting the configured FPS/frame budget; this panel is the live scope, not the final performance proof.",
            MassNavigationShowcaseStepId.DebugVisualBudget =>
                "PASS when diagnostics off has no hot-path dump cost and diagnostics on uses bounded sampled draw counts.",
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts =>
                "PASS in the Raylib workbench when edit/bake/query writes patch, dirty chunks, nav-bake diagnostics, result JSON, and screenshots from the formal tool chain.",
            _ => guide.CurrentStep.ReadablePassSignal
        };
    }

    private static string BuildStrategyOutput(MassNavigationSimulationRuntime simulation)
    {
        ReadOnlySpan<MassNavigationStrategySwitchDiagnostics> strategies = simulation.AcceptanceDiagnostics.StrategySwitches;
        if (strategies.Length == 0)
        {
            return "strategy rows unavailable";
        }

        MassNavigationStrategySwitchDiagnostics first = strategies[0];
        return $"profile={first.AgentTypeId}; selected={first.SelectedStrategy}; graph={first.GraphStatus}/{first.GraphPathPointCount}; mesh={first.MeshStatus}/{first.MeshPathPointCount}; meshSource={first.MeshQuerySource}; routeId={first.RouteId}";
    }

    private static string BuildTenKFlowOutput(
        MassNavigationSimulationRuntime simulation,
        MassNavigationTargetAllocationDiagnostics allocation)
    {
        int commanded = simulation.MassFlow.CountUnitsWithTargets();
        int moving = simulation.MassFlow.CountMovingUnits(0.0001f);
        int settled = simulation.MassFlow.SettledUnitCount;
        int stuck = simulation.MassFlow.CountStuckUnits();
        int waiting = simulation.MassFlow.CountTargetedIdleUnits(0.0001f);
        return $"selected={allocation.SelectedCount}; commanded={commanded}; slots={allocation.SlotCount}; moving/settled/stuck/waiting={moving}/{settled}/{stuck}/{waiting}; blocked/fallback={allocation.BlockedSlotCount}/{allocation.FallbackSlotCount}; flow={simulation.FlowTuning.Enabled}";
    }

    private static string BuildTenKFlowAcceptance(
        MassNavigationSimulationRuntime simulation,
        MassNavigationTargetAllocationDiagnostics allocation)
    {
        int commanded = simulation.MassFlow.CountUnitsWithTargets();
        int active = simulation.MassFlow.CountActiveFlowUnits(0.0001f);
        return $"Gate target: selected=10000, commanded=10000, slots>=10000, blocked=0, fallback=0, flow=On, movement buckets account for every commanded unit. Current selected={simulation.SelectedCount}, commanded={commanded}, accounted={active}, blocked/fallback={allocation.BlockedSlotCount}/{allocation.FallbackSlotCount}.";
    }

    private static string BuildTenKFlowHudAcceptance(
        MassNavigationSimulationRuntime simulation,
        MassNavigationTargetAllocationDiagnostics allocation)
    {
        int commanded = simulation.MassFlow.CountUnitsWithTargets();
        int active = simulation.MassFlow.CountActiveFlowUnits(0.0001f);
        return $"Gate target selected=10000 commanded=10000 accounted=10000 slots>=10000 blocked=0 fallback=0 flow=On. Current selected={simulation.SelectedCount}, commanded={commanded}, accounted={active}, blocked/fallback={allocation.BlockedSlotCount}/{allocation.FallbackSlotCount}.";
    }

    private static string BuildWaypointOutput(MassNavigationSimulationRuntime simulation)
    {
        MassNavigationWaypointPathDiagnostics waypoint = simulation.AcceptanceDiagnostics.WaypointPath;
        return $"authored={waypoint.HasAuthoredPlan}; waypoints={waypoint.WaypointCount}; pathpoints={waypoint.PathPointCount}; oldInvalidated={waypoint.InvalidatedPathPointCount}; editRevision={waypoint.EditRevision}; state={waypoint.EditState}";
    }

    private static string BuildWaypointAcceptance(
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide)
    {
        MassNavigationWaypointPathDiagnostics waypoint = simulation.AcceptanceDiagnostics.WaypointPath;
        return $"PASS when waypoints stay editable, pathpoints are immutable, and edit invalidates old pathpoints without submitting an order. Current editable={waypoint.WaypointsEditable}, immutable={waypoint.PathPointsImmutable}, invalidated={waypoint.InvalidatedPathPointCount}, orderDelta={guide.LastActionOrderDelta}.";
    }

    private static string BuildObstacleOutput(MassNavigationSimulationRuntime simulation)
    {
        MassNavigationObstacleDiagnostics obstacle = simulation.AcceptanceDiagnostics.Obstacles;
        MassNavigationStaticObstacleWorldDiagnostics world = simulation.AcceptanceDiagnostics.StaticObstacleWorld;
        return $"target/authored/baked/loaded={obstacle.TargetStaticObstacleCount}/{obstacle.AuthoredStaticObstacleCount}/{obstacle.BakedStaticObstacleCount}/{obstacle.LoadedStaticObstacleCount}; solverActive={obstacle.SolverActiveStaticObstacleCount}/{obstacle.SolverStaticObstacleCapacity}; buckets={world.MacroChunkCoverageCount}; source={world.DataSource}";
    }

    private static string BuildRuntimeNavDataUpdateOutput(
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide)
    {
        MassNavigationRuntimeBakeAuthoringRuntime authoring = guide.RuntimeBakeAuthoring;
        MassNavigationRuntimeNavDataUpdateDiagnostics update = simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate;
        int revision = Math.Max(authoring.UpdateRevision, update.NavDataRevision);
        return $"draft={authoring.DraftPointCount}; polygons={authoring.AuthoredPolygonCount}; dirtyChunks={authoring.DirtyChunkCount}; revision={revision}; baked={update.BakedTileCount}; changed={update.ChangedTileCount}; triangles={update.BeforeTriangleCount}->{update.AfterTriangleCount}; query={update.QueryStatusAfterUpdate}/{update.QueryPathPointCount}; {BuildNavMeshCoverageLine(guide)}; source={update.UpdateSource}";
    }

    private static string BuildNavMeshCoverageLine(MassNavigationShowcaseGuideRuntime guide)
    {
        MassNavigationNavMeshCoverageGuide coverage = guide.NavMeshCoverage;
        if (!coverage.Available)
        {
            return "navmeshCoverage=missing";
        }

        string scope = coverage.IsPartialCoverage ? "active-window only" : "full-world";
        string window = coverage.ActiveWindowChunkCount > 0
            ? $"window={coverage.ActiveWindowMinChunkX},{coverage.ActiveWindowMinChunkY}->{coverage.ActiveWindowMaxChunkX},{coverage.ActiveWindowMaxChunkY}"
            : "window=missing";
        return $"navmeshCoverage={coverage.TargetChunkCount}/{coverage.WorldChunkCount} {scope}; {window}; bakedTiles={coverage.TotalBakedTiles}/{coverage.TotalExpectedTileBakes}";
    }

    private static string BuildRuntimeNavDataUpdateAcceptance(
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseGuideRuntime guide)
    {
        MassNavigationRuntimeBakeAuthoringRuntime authoring = guide.RuntimeBakeAuthoring;
        MassNavigationRuntimeNavDataUpdateDiagnostics update = simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate;
        return $"PASS when polygons>0, dirtyChunks>0, bakedTiles>0, changedTiles>0, navDataRevision increments, and query runs after update. Current polygons={authoring.AuthoredPolygonCount}, dirty={authoring.DirtyChunkCount}, baked={update.BakedTileCount}, changed={update.ChangedTileCount}, revision={Math.Max(authoring.UpdateRevision, update.NavDataRevision)}, query={update.QueryStatusAfterUpdate}.";
    }

    private UiElementBuilder BuildFormationButton(string label, MassNavigationFormationMode mode, string currentLabel)
    {
        bool active = currentLabel.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase);
        return Ui.Button(label, _ => SetFormationMode(mode))
            .Background(active ? "#315D35" : "#182436")
            .Color("#F8FBFF")
            .Padding(10f, 8f)
            .Radius(10f);
    }

    private void RequestSceneReset()
    {
        _simulation?.RequestSceneReset();
        SetActionFeedback("Queued reset: scene rebuild requested.");
    }

    private void RequestCameraReset()
    {
        if (_engine == null || !MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        MassNavigationRuntime.RequestTacticalCameraReset(_engine);
        MassNavigationRuntime.RequestMinimapTacticalWorldView(_engine);
        SetActionFeedback("Hot apply: field camera requested; minimap remains full battlefield.");
    }

    private void RequestStrategicWorldCamera()
    {
        if (_engine == null || !MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        MassNavigationRuntime.RequestStrategicCameraReset(_engine);
        MassNavigationRuntime.RequestMinimapStrategicWorldView(_engine);
        SetActionFeedback("Hot apply: full 64km map camera requested.");
    }

    private void AdjustTotalAgentsDown() => AdjustTotalAgents(-2_000);
    private void AdjustTotalAgentsUp() => AdjustTotalAgents(2_000);

    private void AdjustTotalAgents(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int currentTotal = _simulation.AgentsPerTeam * _simulation.TeamCount;
        SetTotalAgents(currentTotal + delta);
    }

    private void SetTotalAgents(int totalAgents)
    {
        if (_engine == null || _simulation == null)
        {
            return;
        }

        int teamCount = Math.Max(1, _simulation.TeamCount);
        int maxPerTeam = ResolveMaxAgentsPerTeam(_engine);
        int perTeam = Math.Clamp(Math.Max(0, totalAgents / teamCount), 0, maxPerTeam);
        _simulation.SetAgentsPerTeam(perTeam);
        SetActionFeedback($"Queued reset: agents/team = {perTeam}, teams = {teamCount}, total target = {perTeam * teamCount}.");
    }

    private void SetSelectedTeam(int teamId)
    {
        _simulation?.SetSelectedTeam(teamId);
        SetActionFeedback($"Hot apply: selected team = {teamId}.");
    }

    private void SetFormationMode(MassNavigationFormationMode mode)
    {
        _simulation?.SetFormationMode(mode);
        SetActionFeedback($"Hot apply: formation = {mode}.");
    }

    private void JumpToKnownContact(string contactId)
    {
        if (_engine == null || _simulation == null)
        {
            return;
        }

        if (!_simulation.WorldConfig.TryGetHotZone(contactId, out MassNavigationHotZoneConfig? contact))
        {
            throw new InvalidOperationException($"MassNavigationMod contact '{contactId}' is not configured.");
        }

        var targetCm = new System.Numerics.Vector2(contact.CenterXCm, contact.CenterYCm);
        _simulation.ObserveCameraFocus(targetCm);
        MassNavigationRuntime.RequestCameraJump(_engine, targetCm, 18_000f);
        MassNavigationRuntime.RequestMinimapStrategicWorldView(_engine);
        SetActionFeedback($"Camera moved to debug landmark {contact.Label}; camera budget updated without respawn or retarget.");
    }

    private void RequestNextShowcaseStep()
    {
        _guide?.NextStep();
        if (_guide != null)
        {
            FocusShowcaseStep(_guide.CurrentStepId);
        }

        SetActionFeedback(_guide?.LastActionText ?? "Showcase step advanced.");
    }

    private void RequestPreviousShowcaseStep()
    {
        _guide?.PreviousStep();
        if (_guide != null)
        {
            FocusShowcaseStep(_guide.CurrentStepId);
        }

        SetActionFeedback(_guide?.LastActionText ?? "Showcase step moved back.");
    }

    private void SetShowcaseStep(MassNavigationShowcaseStepId stepId)
    {
        _guide?.SetStep(stepId);
        FocusShowcaseStep(stepId);
        SetActionFeedback(_guide?.LastActionText ?? $"Showcase step = {stepId}.");
    }

    private void RunCurrentShowcaseStep()
    {
        if (_simulation == null || _guide == null)
        {
            return;
        }

        _guide.RunCurrentStep(_simulation);
        FocusShowcaseStep(_guide.CurrentStepId);
        SetActionFeedback(_guide.LastActionText);
    }

    private void RunPrimaryShowcaseAction()
    {
        if (_guide == null)
        {
            return;
        }

        if (_guide.CurrentStepId == MassNavigationShowcaseStepId.OrderReuse)
        {
            PrepareOrderReuseSelection();
            return;
        }

        if (_guide.CurrentStepId == MassNavigationShowcaseStepId.TargetAllocation ||
            _guide.CurrentStepId == MassNavigationShowcaseStepId.TenKFlow)
        {
            PrepareShowcaseLargeSelection();
            return;
        }

        if (_guide.CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery)
        {
            RequestRuntimeNavDataUpdate();
            return;
        }

        RunCurrentShowcaseStep();
    }

    private void RunShowcasePathPreview()
    {
        if (_simulation == null || _guide == null)
        {
            return;
        }

        _guide.RunPathPreview(_simulation);
        FocusShowcaseStep(MassNavigationShowcaseStepId.PathOnly);
        SetActionFeedback(_guide.LastActionText);
    }

    private void ArmRuntimeObstacleAuthoring()
    {
        if (_simulation == null || _guide == null)
        {
            return;
        }

        _guide.ArmRuntimeObstacleAuthoring();
        _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
        FocusShowcaseStep(MassNavigationShowcaseStepId.BakeToolQuery);
        SetActionFeedback(_guide.LastActionText);
    }

    private void CancelRuntimeObstacleAuthoring()
    {
        if (_simulation == null || _guide == null)
        {
            return;
        }

        _guide.CancelRuntimeObstacleAuthoring();
        _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
        SetActionFeedback(_guide.LastActionText);
    }

    private void CloseRuntimeObstaclePolygon()
    {
        if (_simulation == null || _guide == null)
        {
            return;
        }

        if (_guide.RuntimeBakeAuthoring.TryCloseObstaclePolygon(_simulation.BakeDataDiagnostics, out string failureReason))
        {
            _guide.RecordRuntimeObstacleClosed();
        }
        else
        {
            _guide.RecordRuntimeObstacleAuthoringFailure(Vector2.Zero, failureReason);
        }

        _simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(_guide.RuntimeBakeAuthoring.CreateSnapshot());
        SetActionFeedback(_guide.LastActionText);
    }

    private void RequestRuntimeNavDataUpdate()
    {
        if (_engine == null || _simulation == null || _guide == null)
        {
            return;
        }

        MassNavigationRuntimeNavDataUpdateDiagnostics diagnostics = _guide.RuntimeBakeAuthoring.RequestRuntimeNavDataUpdate(
            _simulation,
            _engine.GetService(CoreServiceKeys.NavMeshBakeConfig),
            _engine.GetService(CoreServiceKeys.NavQueryServices),
            _engine.GetService(CoreServiceKeys.NavMeshProfiles),
            _engine.GetService(CoreServiceKeys.PathService),
            _engine.GetService(CoreServiceKeys.PathStore));
        _guide.RecordRuntimeNavDataUpdateResult(diagnostics);
        FocusShowcaseStep(MassNavigationShowcaseStepId.BakeToolQuery);
        SetActionFeedback(_guide.LastActionText);
    }

    private void RunRuntimeBakeWorldPath()
    {
        if (_engine == null || _simulation == null || _guide == null)
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.PathService) is not IPathService pathService ||
            _engine.GetService(CoreServiceKeys.PathStore) is not PathStore pathStore)
        {
            SetActionFeedback("World Path unavailable: PathService/PathStore is not bound.");
            return;
        }

        if (!_guide.TryResolveRuntimeBakeWorldPathEndpoints(
                _simulation.BakeDataDiagnostics,
                _engine.GetService(CoreServiceKeys.NavQueryServices),
                _engine.GetService(CoreServiceKeys.NavMeshProfiles),
                out MassNavigationRuntimeWorldPathEndpointResult endpoints))
        {
            SetActionFeedback("World Path unavailable: no reachable full-world NavMesh component endpoint was found.");
            return;
        }

        Vector2 startWorldCm = endpoints.StartWorldCm;
        Vector2 goalWorldCm = endpoints.GoalWorldCm;
        _guide.ArmPathDrivenOperation(MassNavigationShowcaseStepId.BakeToolQuery);
        int before = _simulation.CommandCountFrame + _simulation.PendingCommandCount;
        _simulation.AcceptanceDiagnostics.RecordPathOnlyPreviewQuery(
            pathService,
            pathStore,
            startWorldCm,
            goalWorldCm,
            PathDomain.NavMesh);
        int after = _simulation.CommandCountFrame + _simulation.PendingCommandCount;
        _guide.RecordPathPreviewQueryResult(
            startWorldCm,
            goalWorldCm,
            after - before,
            _simulation.AcceptanceDiagnostics.PathOnlyQuery);

        MassNavigationRuntime.RequestCameraJump(_engine, ResolvePathMidpoint(), ResolvePathCameraDistanceCm());
        MassNavigationRuntime.RequestMinimapTacticalWorldView(_engine);
        string cache = pathService is PathServiceRouter router
            ? $"; cache hits={router.CacheDiagnostics.Hits} misses={router.CacheDiagnostics.Misses}"
            : string.Empty;
        SetActionFeedback($"World Path query {_simulation.AcceptanceDiagnostics.PathOnlyQuery.Status}: start=({_simulation.AcceptanceDiagnostics.PathOnlyQuery.StartMacroChunkX},{_simulation.AcceptanceDiagnostics.PathOnlyQuery.StartMacroChunkY}) goal=({_simulation.AcceptanceDiagnostics.PathOnlyQuery.GoalMacroChunkX},{_simulation.AcceptanceDiagnostics.PathOnlyQuery.GoalMacroChunkY}) routeChunks={endpoints.MacroRouteChunkCount} componentTiles={endpoints.ComponentTileCount} points={_simulation.AcceptanceDiagnostics.PathOnlyQuery.PathPointCount}{cache}.");
    }

    private void RunShowcaseSameOrder()
    {
        PrepareOrderReuseSelection();
    }

    private void RunShowcaseNearOrder()
    {
        PrepareOrderReuseSelection();
    }

    private void PrepareShowcaseLargeSelection()
    {
        if (_simulation == null || _guide == null)
        {
            return;
        }

        MassNavigationShowcaseStepId stepId = _guide.CurrentStepId == MassNavigationShowcaseStepId.TenKFlow
            ? MassNavigationShowcaseStepId.TenKFlow
            : MassNavigationShowcaseStepId.TargetAllocation;
        int selected = TrySelectLargeArmy(10_000);
        if (selected <= 0)
        {
            _guide.RecordLargeSelectionPreparationFailed(stepId, "SelectionRuntime or controllable agents not ready");
        }
        else
        {
            _guide.RecordLargeSelectionPrepared(stepId, selected);
        }

        FocusShowcaseStep(stepId);
        SetActionFeedback(_guide.LastActionText);
    }

    private void PrepareOrderReuseSelection()
    {
        if (_simulation == null || _guide == null)
        {
            return;
        }

        int selected = TrySelectLargeArmy(64);
        if (selected <= 0)
        {
            _guide.RecordLargeSelectionPreparationFailed(MassNavigationShowcaseStepId.OrderReuse, "SelectionRuntime or controllable agents not ready");
        }
        else
        {
            _guide.RecordOrderReuseSelectionPrepared(selected);
        }

        FocusShowcaseStep(MassNavigationShowcaseStepId.OrderReuse);
        SetActionFeedback(_guide.LastActionText);
    }

    private int TrySelectLargeArmy(int requestedCount)
    {
        if (_engine == null || _simulation == null)
        {
            return 0;
        }

        SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime);
        if (selection == null ||
            !_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Arch.Core.Entity owner ||
            !_engine.World.IsAlive(owner))
        {
            return 0;
        }

        int count = Math.Min(Math.Max(1, requestedCount), _simulation.AgentState.ControllableCount);
        if (count <= 0)
        {
            return 0;
        }

        Span<Arch.Core.Entity> scratch = count <= 2048
            ? stackalloc Arch.Core.Entity[count]
            : new Arch.Core.Entity[count];
        for (int i = 0; i < count; i++)
        {
            scratch[i] = _simulation.AgentState.ControllableAgents[i];
        }

        if (!selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, scratch))
        {
            return 0;
        }

        MassNavigationSelectionSync.SyncIfChanged(_engine.World, _engine.GlobalContext, selection, _simulation);
        return _simulation.SelectedCount;
    }

    private void RunShowcaseWaypointEdit()
    {
        if (_simulation == null || _guide == null)
        {
            return;
        }

        _guide.RunWaypointEditProbe(_simulation);
        FocusShowcaseStep(MassNavigationShowcaseStepId.WaypointAuthoring);
        SetActionFeedback(_guide.LastActionText);
    }

    private void FocusShowcaseStep(MassNavigationShowcaseStepId stepId)
    {
        if (_engine == null || _simulation == null || !MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        switch (stepId)
        {
            case MassNavigationShowcaseStepId.VisualHeightmapBake:
            case MassNavigationShowcaseStepId.LogicHeightmapBake:
            case MassNavigationShowcaseStepId.WorldHpa:
            case MassNavigationShowcaseStepId.LargeWorldStreaming:
                MassNavigationRuntime.RequestStrategicCameraReset(_engine);
                MassNavigationRuntime.RequestMinimapStrategicWorldView(_engine);
                return;
            case MassNavigationShowcaseStepId.NavMeshBake:
                MassNavigationRuntime.RequestCameraJump(_engine, ResolveNavMeshSampleCenter(), 18_000f);
                MassNavigationRuntime.RequestMinimapTacticalWorldView(_engine);
                return;
            case MassNavigationShowcaseStepId.LayerAreaEditor:
            case MassNavigationShowcaseStepId.BakeToolQuery:
            case MassNavigationShowcaseStepId.LayerCosts:
                if (stepId == MassNavigationShowcaseStepId.BakeToolQuery)
                {
                    RequestRuntimeBakeMeshCamera();
                    return;
                }

                MassNavigationRuntime.RequestCameraJump(_engine, ResolveNavMeshSampleCenter(), 28_000f);
                MassNavigationRuntime.RequestMinimapTacticalWorldView(_engine);
                return;
            case MassNavigationShowcaseStepId.TargetAllocation:
            case MassNavigationShowcaseStepId.TenKFlow:
                MassNavigationRuntime.RequestCameraJump(_engine, ResolveDefaultGoal(), 22_000f);
                MassNavigationRuntime.RequestMinimapTacticalWorldView(_engine);
                return;
            case MassNavigationShowcaseStepId.StaticObstacleWorld:
            case MassNavigationShowcaseStepId.PerformanceDebug:
            case MassNavigationShowcaseStepId.DebugVisualBudget:
                MassNavigationRuntime.RequestCameraJump(_engine, new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm), 36_000f);
                MassNavigationRuntime.RequestMinimapTacticalWorldView(_engine);
                return;
            default:
                MassNavigationRuntime.RequestCameraJump(_engine, ResolvePathMidpoint(), ResolvePathCameraDistanceCm());
                MassNavigationRuntime.RequestMinimapTacticalWorldView(_engine);
                return;
        }
    }

    private float ResolvePathCameraDistanceCm()
    {
        if (_simulation == null)
        {
            return 18_000f;
        }

        MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (query.StartWorldCm == Vector2.Zero || query.GoalWorldCm == Vector2.Zero)
        {
            return 18_000f;
        }

        float span = Vector2.Distance(query.StartWorldCm, query.GoalWorldCm);
        return Math.Clamp(span * 0.85f, 18_000f, 60_000f);
    }

    private Vector2 ResolvePathMidpoint()
    {
        if (_simulation == null)
        {
            return Vector2.Zero;
        }

        ReadOnlySpan<MassNavigationPathPointSample> points = _simulation.AcceptanceDiagnostics.PathOnlyPathPoints;
        if (points.Length > 0)
        {
            MassNavigationPathPointSample sample = points[points.Length / 2];
            return new Vector2(sample.Xcm, sample.Ycm);
        }

        MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (query.StartWorldCm != Vector2.Zero && query.GoalWorldCm != Vector2.Zero)
        {
            return (query.StartWorldCm + query.GoalWorldCm) * 0.5f;
        }

        return new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
    }

    private Vector2 ResolveDefaultGoal()
    {
        if (_simulation == null)
        {
            return Vector2.Zero;
        }

        MassNavigationTargetAllocationDiagnostics allocation = _simulation.AcceptanceDiagnostics.TargetAllocation;
        if (allocation.HasAllocation)
        {
            return allocation.DestinationWorldCm;
        }

        MassNavigationPathOnlyQueryDiagnostics query = _simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (query.GoalWorldCm != Vector2.Zero)
        {
            return query.GoalWorldCm;
        }

        return new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
    }

    private Vector2 ResolveNavMeshSampleCenter()
    {
        if (_simulation == null || _guide == null)
        {
            return Vector2.Zero;
        }

        MassNavigationNavMeshGuideSample sample = _guide.NavMeshSample;
        MassNavigationBakeDataDiagnostics? bake = _simulation.BakeDataDiagnostics;
        if (!sample.Available || bake == null)
        {
            return new Vector2(_simulation.SolverWindowCenterXCm, _simulation.SolverWindowCenterYCm);
        }

        return new Vector2(
            bake.WorldMinXCm + (sample.ChunkX * bake.MacroChunkSizeXCm) + (bake.MacroChunkSizeXCm * 0.5f),
            bake.WorldMinYCm + (sample.ChunkY * bake.MacroChunkSizeYCm) + (bake.MacroChunkSizeYCm * 0.5f));
    }

    private void RequestRuntimeBakeMeshCamera()
    {
        if (_engine == null || !MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        MassNavigationRuntime.RequestNavMeshInspectionCamera(_engine);
        MassNavigationRuntime.RequestMinimapTacticalWorldView(_engine);
        SetActionFeedback("Hot apply: navmesh mesh view requested; wheel zoom stays inside the mesh-view camera range.");
    }

    private void ToggleShowcaseNavMesh()
    {
        _guide?.ToggleNavMesh();
        SetActionFeedback(_guide?.LastActionText ?? "NavMesh debug toggled.");
    }

    private void ToggleShowcaseHpa()
    {
        _guide?.ToggleHpa();
        SetActionFeedback(_guide?.LastActionText ?? "HPA debug toggled.");
    }

    private void ToggleShowcasePath()
    {
        _guide?.TogglePath();
        SetActionFeedback(_guide?.LastActionText ?? "Path debug toggled.");
    }

    private void ToggleShowcaseLayerCost()
    {
        _guide?.ToggleLayerCost();
        SetActionFeedback(_guide?.LastActionText ?? "Layer/cost debug toggled.");
    }

    private void ToggleShowcaseSlots()
    {
        _guide?.ToggleSlots();
        SetActionFeedback(_guide?.LastActionText ?? "Slot debug toggled.");
    }

    private void AdjustSimulationBudget(int delta)
    {
        if (_engine == null)
        {
            return;
        }

        _engine.SimulationBudgetMsPerFrame = Math.Clamp(_engine.SimulationBudgetMsPerFrame + delta, 1, 64);
        SetActionFeedback($"Hot apply: simulation budget = {_engine.SimulationBudgetMsPerFrame} ms/frame.");
    }

    private void AdjustSimulationSlices(int delta)
    {
        if (_engine == null)
        {
            return;
        }

        _engine.SimulationMaxSlicesPerLogicFrame = Math.Clamp(_engine.SimulationMaxSlicesPerLogicFrame + delta, 1, 2048);
        SetActionFeedback($"Hot apply: simulation slice limit = {_engine.SimulationMaxSlicesPerLogicFrame}.");
    }

    private void AdjustPhysicsHz(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Physics2DTickPolicy) is not Physics2DTickPolicy policy)
        {
            return;
        }

        policy.SetTargetHz(Math.Clamp(policy.TargetHz + delta, 0, 240));
        SetActionFeedback($"Engine policy hot apply: physics = {policy.TargetHz} Hz. Current mass-navigation custom sim still runs on LogicHz/InputCollection.");
    }

    private void AdjustPhysicsMaxSteps(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Physics2DTickPolicy) is not Physics2DTickPolicy policy)
        {
            return;
        }

        policy.SetMaxStepsPerFixedTick(Math.Clamp(policy.MaxStepsPerFixedTick + delta, 1, 32));
        SetActionFeedback($"Engine policy hot apply: physics max steps = {policy.MaxStepsPerFixedTick}. Current mass-navigation custom sim is not consuming Physics2D ticks.");
    }

    private void AdjustNavigationHz(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Navigation2DTickPolicy) is not Navigation2DTickPolicy policy)
        {
            return;
        }

        policy.SetTargetHz(Math.Clamp(policy.TargetHz + delta, 0, 240));
        SetActionFeedback($"Engine policy hot apply: navigation = {policy.TargetHz} Hz. Current mass-navigation custom sim still runs on LogicHz/InputCollection.");
    }

    private void AdjustNavigationMaxSteps(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Navigation2DTickPolicy) is not Navigation2DTickPolicy policy)
        {
            return;
        }

        policy.SetMaxStepsPerFixedTick(Math.Clamp(policy.MaxStepsPerFixedTick + delta, 1, 32));
        SetActionFeedback($"Engine policy hot apply: navigation max steps = {policy.MaxStepsPerFixedTick}. Current mass-navigation custom sim is not consuming Navigation2D ticks.");
    }

    private void ToggleFlowEnabled()
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.FlowTuning.Enabled = !_simulation.FlowTuning.Enabled;
        _simulation.MassFlow.RequestFlowRebuild();
        SetActionFeedback($"Hot apply: flow dynamic crowd stamp = {(_simulation.FlowTuning.Enabled ? "On" : "Off")}.");
    }

    private void AdjustFlowIterations(int delta)
    {
        _simulation?.FlowTuning.AdjustIterations(delta);
        if (_simulation != null)
        {
            _simulation.MassFlow.RequestFlowRebuild();
            SetActionFeedback($"Hot apply: flow crowd stamp budget = {_simulation.FlowTuning.IterationsPerStep}.");
        }
    }

    private void AdjustFlowStepHz(int delta)
    {
        if (_simulation != null)
        {
            _simulation.Cadence.AdjustFlowStepHz(delta);
            _simulation.MassFlow.RequestFlowRebuild();
            SetActionFeedback($"Hot apply: flow solve cadence = {_simulation.Cadence.FlowStepHz} Hz.");
        }
    }

    private void AdjustFlowCrowdHz(int delta)
    {
        if (_simulation != null)
        {
            _simulation.Cadence.AdjustFlowCrowdStampHz(delta);
            _simulation.MassFlow.RequestFlowRebuild();
            SetActionFeedback($"Hot apply: flow crowd stamp cadence = {_simulation.Cadence.FlowCrowdStampHz} Hz.");
        }
    }

    private void AdjustFlowObstacleHz(int delta)
    {
        if (_simulation != null)
        {
            _simulation.Cadence.AdjustFlowObstacleStampHz(delta);
            _simulation.MassFlow.RequestFlowRebuild();
            SetActionFeedback($"Hot apply: flow obstacle stamp cadence = {_simulation.Cadence.FlowObstacleStampHz} Hz.");
        }
    }

    private void AdjustHardResolveHz(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.Cadence.AdjustHardResolveHz(delta);
        SetActionFeedback($"Hot apply: hard resolve cadence = {_simulation.Cadence.HardResolveHz} Hz.");
    }

    private void AdjustEntitySyncHz(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.Cadence.AdjustEntitySyncHz(delta);
        SetActionFeedback($"Hot apply: ECS writeback cadence = {_simulation.Cadence.EntitySyncHz} Hz.");
    }

    private void ToggleArrivalRecovery()
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.MassFlow.ArrivalTuning.Enabled = !_simulation.MassFlow.ArrivalTuning.Enabled;
        SetActionFeedback($"Hot apply: arrival recovery = {(_simulation.MassFlow.ArrivalTuning.Enabled ? "On" : "Off")}.");
    }

    private void AdjustArrivalTimeoutMs(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.MassFlow.ArrivalTuning.AdjustTimeoutMs(delta);
        SetActionFeedback($"Hot apply: arrival timeout = {_simulation.MassFlow.ArrivalTuning.TimeoutMs} ms.");
    }

    private void AdjustArrivalProgressCm(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.MassFlow.ArrivalTuning.AdjustProgressDistanceCm(delta);
        SetActionFeedback($"Hot apply: arrival progress = {_simulation.MassFlow.ArrivalTuning.ProgressDistanceCm} cm.");
    }

    private void AdjustArrivalWakePushCm(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.MassFlow.ArrivalTuning.AdjustWakePushDistanceCm(delta);
        SetActionFeedback($"Hot apply: arrival wake push = {_simulation.MassFlow.ArrivalTuning.WakePushDistanceCm} cm.");
    }

    private void AdjustArrivalMaxRetries(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.MassFlow.ArrivalTuning.AdjustMaxRetryCount(delta);
        SetActionFeedback($"Hot apply: arrival max retries = {_simulation.MassFlow.ArrivalTuning.MaxRetryCount}.");
    }

    private void SetActionFeedback(string text)
    {
        _lastActionText = text;
        PushImmediateState();
    }

    private void PushImmediateState()
    {
        if (_page == null || _engine == null || _simulation == null)
        {
            return;
        }

        MassNavigationPanelState next = CaptureState(_engine, _simulation);
        _lastState = next;
        _lastPerfCaptureTicks = Stopwatch.GetTimestamp();
        _page.SetState(_ => next);
        if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            root.IsDirty = true;
        }
    }

    private MassNavigationPanelState CaptureState(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        bool visible = engine.CurrentMapSession != null && MassNavigationIds.IsNavigationMap(engine.CurrentMapSession.MapId.Value);
        PresentationTimingDiagnostics timing = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
            ?? throw new InvalidOperationException("MassNavigationMod panel requires PresentationTimingDiagnostics.");
        IViewController viewport = engine.GetService(CoreServiceKeys.ViewController)
            ?? throw new InvalidOperationException("MassNavigationMod panel requires ViewController.");
        var viewportResolution = viewport.Resolution;
        int ecsVisibleEntities = (engine.GetService(CoreServiceKeys.CameraCullingDebugState) as CameraCullingDebugState)?.VisibleEntityCount
            ?? timing.VisibleEntitiesLastFrame;
        float frameMs = MathF.Round(ResolveFrameMs(timing), 1);
        float fps = frameMs > 0.001f ? MathF.Round(1000f / frameMs) : 0f;
        float performerEmitMs = MathF.Round(timing.PerformerEmitMs, 1);
        float performerTransformMs = MathF.Round(timing.PerformerEntityTransformSyncMs, 1);
        float performerMinimapMarkerMs = MathF.Round(timing.PerformerMinimapMarkerMs, 1);
        float minimapProjectionMs = MathF.Round(timing.MinimapProjectionMs, 1);
        float uiInputMs = MathF.Round(timing.UiInputMs, 1);
        float uiRenderMs = MathF.Round(timing.UiRenderMs, 1);
        float uiUploadMs = MathF.Round(timing.UiUploadMs, 1);
        float screenOverlayBuildMs = MathF.Round(timing.ScreenOverlayBuildMs, 1);
        float screenOverlayDrawMs = MathF.Round(timing.ScreenOverlayDrawMs, 1);
        float cameraCullingMs = MathF.Round(timing.CameraCullingMs, 1);
        float cameraPresenterMs = MathF.Round(timing.CameraPresenterMs, 1);
        float worldHudProjectionMs = MathF.Round(timing.WorldHudProjectionMs, 1);
        float renderAccountedMs = MathF.Round(
            performerEmitMs +
            performerTransformMs +
            performerMinimapMarkerMs +
            minimapProjectionMs +
            uiInputMs +
            uiRenderMs +
            uiUploadMs +
            screenOverlayBuildMs +
            screenOverlayDrawMs +
            cameraCullingMs +
            cameraPresenterMs +
            worldHudProjectionMs,
            1);
        float renderUnaccountedMs = MathF.Max(0f, MathF.Round(frameMs - renderAccountedMs, 1));
        float firstAgentX = 0f;
        float firstAgentZ = 0f;
        if (simulation.AgentState.ControllableCount > 0)
        {
            var first = simulation.AgentState.ControllableAgents[0];
            if (engine.World.IsAlive(first) && engine.World.TryGet(first, out WorldPositionCm position))
            {
                var cm = position.Value.ToWorldCmInt2();
                firstAgentX = cm.X;
                firstAgentZ = cm.Y;
            }
        }

        var camera = engine.GameSession.Camera.State;
        int logicHz = Time.FixedDeltaTime > 0.000001f ? (int)MathF.Round(1f / Time.FixedDeltaTime) : 0;
        var physicsPolicy = engine.GetService(CoreServiceKeys.Physics2DTickPolicy)
            ?? throw new InvalidOperationException("MassNavigationMod panel requires Physics2DTickPolicy.");
        var navigationPolicy = engine.GetService(CoreServiceKeys.Navigation2DTickPolicy)
            ?? throw new InvalidOperationException("MassNavigationMod panel requires Navigation2DTickPolicy.");
        string obstacleSemanticsText = $"Obstacle semantics: visible radius = authored obstacle. hard block = visible + body {simulation.MassFlow.Semantics.Obstacle.AgentBodyRadiusCm:0} cm. soft push = visible + {simulation.MassFlow.Semantics.Obstacle.SoftPushPaddingCm:0} cm.";
        string targetSemanticsText = $"Target semantics: team target clear {simulation.MassFlow.Semantics.TargetProjection.TeamTargetClearanceCm:0} cm. group center clear {simulation.MassFlow.Semantics.TargetProjection.GroupCenterClearanceCm:0} cm. team slot clear {simulation.MassFlow.Semantics.TargetProjection.TeamSlotClearanceCm:0} cm. loose/group slot clear {simulation.MassFlow.Semantics.TargetProjection.LooseTargetClearanceCm:0}/{simulation.MassFlow.Semantics.TargetProjection.GroupSlotClearanceCm:0} cm.";
        string arrivalSemanticsText = $"Arrival semantics: stop threshold {simulation.MassFlow.Semantics.Group.UnitTargetStopThresholdCm:0} cm. settle timeout/progress/wake/retry = {simulation.MassFlow.ArrivalTuning.TimeoutMs}/{simulation.MassFlow.ArrivalTuning.ProgressDistanceCm}/{simulation.MassFlow.ArrivalTuning.WakePushDistanceCm}/{simulation.MassFlow.ArrivalTuning.MaxRetryCount}. flow slow radius loose/group = {simulation.MassFlow.Semantics.Steering.GoalArrivalRadiusCm:0}/{simulation.MassFlow.Semantics.Group.FormationFlowSlowRadiusCm:0} cm.";
        string yieldSemanticsText = $"Yield semantics: nav mass light/heavy = {simulation.MassFlow.AvoidanceTuning.LightNavMass:0.##}/{simulation.MassFlow.AvoidanceTuning.HeavyNavMass:0.##}. dominant ratio {simulation.MassFlow.AvoidanceTuning.DominantMassRatio:0.##}. response friendly/non-friendly/push = {simulation.MassFlow.AvoidanceTuning.FriendlyResponseScale:0.##}/{simulation.MassFlow.AvoidanceTuning.NonFriendlyResponseScale:0.##}/{simulation.MassFlow.AvoidanceTuning.DominantPushResponseScale:0.##}. neighbor budget {simulation.MassFlow.Semantics.Steering.MaxSeparationNeighborsPerUnit}/unit; target refresh budget {simulation.NavGroupRuntime.TargetRefreshBudget}/update.";
        MassNavigationShowcaseGuideRuntime guide = _guide ?? new MassNavigationShowcaseGuideRuntime();
        MassNavigationShowcaseStep guideStep = guide.CurrentStep;
        MassNavigationNavMeshGuideSample navMeshSample = guide.NavMeshSample;
        return new MassNavigationPanelState(
            Visible: visible,
            LastActionText: _lastActionText,
            ShowcaseId: guide.ShowcaseId,
            ShowcaseRootTitle: guide.ShowcaseTitle,
            ShowcasePlayerPerspective: guide.PlayerPerspective,
            ShowcaseModAuthorPerspective: guide.ModAuthorPerspective,
            ShowcaseFocusedPanel: guide.FocusedPanel,
            ShowcaseTitle: guideStep.Title,
            ShowcaseWho: guideStep.Who,
            ShowcaseWhat: guideStep.What,
            ShowcaseWhen: guideStep.When,
            ShowcaseWhere: guideStep.Where,
            ShowcaseWhy: guideStep.Why,
            ShowcaseHow: guideStep.How,
            ShowcasePlayerInput: guideStep.PlayerInput,
            ShowcasePlayerExpected: guideStep.PlayerExpected,
            ShowcaseReadablePassSignal: guideStep.ReadablePassSignal,
            ShowcaseDebugLegend: guideStep.DebugLegend,
            ShowcaseExpectedOutput: guideStep.ExpectedOutput,
            ShowcaseProductionGate: guideStep.ProductionGate,
                ShowcasePrimaryActionLabel: guide.PrimaryActionLabel,
                ShowcaseOperationMode: guide.OperationMode,
                ShowcaseOperationContract: guide.OperationContract,
                ShowcaseUserOperation: BuildShowcaseUserOperation(guide),
                ShowcaseLiveOutput: BuildShowcaseLiveOutput(simulation, guide),
                ShowcaseAcceptanceCheck: BuildShowcaseAcceptanceCheck(simulation, guide),
                ShowcaseLastActionText: guide.LastActionText,
            ShowcaseStepIndex: guide.CurrentStepIndex,
            ShowcaseStepCount: guide.StepCount,
            ShowcaseActionRevision: guide.ActionRevision,
            ShowcaseLastActionOrderDelta: guide.LastActionOrderDelta,
            ShowcaseDebugNavMeshEnabled: guide.DebugNavMeshEnabled,
            ShowcaseDebugHpaEnabled: guide.DebugHpaEnabled,
            ShowcaseDebugPathEnabled: guide.DebugPathEnabled,
            ShowcaseDebugLayerCostEnabled: guide.DebugLayerCostEnabled,
            ShowcaseDebugSlotsEnabled: guide.DebugSlotsEnabled,
            ShowcaseNavMeshSampleAvailable: navMeshSample.Available,
            ShowcaseNavMeshChunkX: navMeshSample.ChunkX,
            ShowcaseNavMeshChunkY: navMeshSample.ChunkY,
            ShowcaseNavMeshLayer: navMeshSample.Layer,
            ShowcaseNavMeshProfileId: navMeshSample.ProfileId,
            ShowcaseNavMeshTriangleCount: navMeshSample.TriangleCount,
            ShowcaseNavMeshPortalCount: navMeshSample.PortalCount,
            ShowcaseNavMeshMinPortalClearanceCm: navMeshSample.MinPortalClearanceCm,
            ShowcaseNavMeshAgentRadiusCm: navMeshSample.AgentRadiusCm,
            ShowcaseNavMeshBlockedCellCount: navMeshSample.BlockedCellCount,
            ShowcaseNavMeshHighCostCellCount: navMeshSample.HighCostCellCount,
            ShowcaseNavMeshWaterCellCount: navMeshSample.WaterCellCount,
            ShowcaseNavMeshRampCellCount: navMeshSample.RampCellCount,
            ShowcaseNavMeshAreaLegend: navMeshSample.AreaLegend,
            ShowcaseNavMeshLayerLegend: navMeshSample.LayerLegend,
            ShowcaseNavMeshBlockedSource: navMeshSample.BlockedSource,
            ShowcaseNavMeshOffMeshLinkSource: navMeshSample.OffMeshLinkSource,
            LogicHz: logicHz,
            SimulationBudgetMs: engine.SimulationBudgetMsPerFrame,
            SimulationSliceLimit: engine.SimulationMaxSlicesPerLogicFrame,
            PhysicsHz: physicsPolicy.TargetHz,
            PhysicsMaxStepsPerFixedTick: physicsPolicy.MaxStepsPerFixedTick,
            NavigationHz: navigationPolicy.TargetHz,
            NavigationMaxStepsPerFixedTick: navigationPolicy.MaxStepsPerFixedTick,
            MassNavigationSimulationHz: simulation.Cadence.SimulationHz,
            MassNavigationTargetUpdateHz: simulation.Cadence.TargetUpdateHz,
            MassNavigationFlowStepHz: simulation.Cadence.FlowStepHz,
            MassNavigationFlowCrowdStampHz: simulation.Cadence.FlowCrowdStampHz,
            MassNavigationFlowObstacleStampHz: simulation.Cadence.FlowObstacleStampHz,
            MassNavigationHardResolveHz: simulation.Cadence.HardResolveHz,
            MassNavigationEntitySyncHz: simulation.Cadence.EntitySyncHz,
            TeamCount: simulation.TeamCount,
            AgentsPerTeam: simulation.AgentsPerTeam,
            TotalAgents: simulation.AgentState.TotalAgents,
            ControllableAgents: simulation.AgentState.ControllableCount,
            Blockers: simulation.AgentState.BlockerCount,
            WorldMarkerCount: simulation.AgentState.WorldMarkerCount,
            SelectedTeamId: simulation.SelectedTeamId,
            SelectedCount: simulation.SelectedCount,
            SelectionRevision: simulation.SelectionRevision,
            PendingCommandCount: simulation.PendingCommandCount,
            CommandCountFrame: simulation.CommandCountFrame,
            FormationCount: simulation.NavGroupRuntime.ActiveGroupCount,
            FormationLabel: simulation.FormationMode.ToString(),
            FormationRotationDeg: simulation.NavGroupRuntime.SelectedRotationRadians * (180f / MathF.PI),
            FlowEnabled: simulation.FlowTuning.Enabled,
            FlowIterations: simulation.FlowTuning.IterationsPerStep,
            ArrivalRecoveryEnabled: simulation.MassFlow.ArrivalTuning.Enabled,
            ArrivalTimeoutMs: simulation.MassFlow.ArrivalTuning.TimeoutMs,
            ArrivalProgressCm: simulation.MassFlow.ArrivalTuning.ProgressDistanceCm,
            ArrivalWakePushCm: simulation.MassFlow.ArrivalTuning.WakePushDistanceCm,
            ArrivalMaxRetries: simulation.MassFlow.ArrivalTuning.MaxRetryCount,
            ArrivalSettledUnits: simulation.MassFlow.SettledUnitCount,
            Fps: fps,
            FrameMs: frameMs,
            PerformerEmitMs: performerEmitMs,
            PerformerTransformMs: performerTransformMs,
            PerformerMinimapMarkerMs: performerMinimapMarkerMs,
            MinimapProjectionMs: minimapProjectionMs,
            ViewportWidth: viewportResolution.X,
            ViewportHeight: viewportResolution.Y,
            SelectionSyncMs: MathF.Round(simulation.SelectionSyncMs, 1),
            CommandApplyMs: MathF.Round(simulation.CommandApplyMs, 1),
            FormationTargetMs: MathF.Round(simulation.FormationTargetMs, 1),
            FlowFieldRebuildMs: MathF.Round(simulation.FlowFieldRebuildMs > 0.001f ? simulation.FlowFieldRebuildMs : simulation.MassFlow.LastFlowFieldRebuildMs, 1),
            StepPrepMs: MathF.Round(simulation.StepPrepMs, 1),
            LocalSteeringMs: MathF.Round(simulation.LocalSteeringMs, 1),
            SimStepMs: MathF.Round(simulation.SimStepMs, 1),
            HardResolveMs: MathF.Round(simulation.HardResolveMs, 1),
            EntitySyncMs: MathF.Round(simulation.EntitySyncMs, 1),
            PerformerCommandMs: MathF.Round(simulation.PerformerCommandMs, 1),
            SelectionSyncHzObserved: MathF.Round(simulation.SelectionSyncHzObserved, 1),
            ControlHzObserved: MathF.Round(simulation.ControlHzObserved, 1),
            CommandHzObserved: MathF.Round(simulation.CommandHzObserved, 1),
            CommandDispatchHzObserved: MathF.Round(simulation.CommandDispatchHzObserved, 1),
            SimHzObserved: MathF.Round(simulation.SimHzObserved, 1),
            PerformerHzObserved: MathF.Round(simulation.PerformerHzObserved, 1),
            HudHzObserved: MathF.Round(simulation.HudHzObserved, 1),
            PanelHzObserved: MathF.Round(simulation.PanelHzObserved, 1),
            UiInputMs: uiInputMs,
            UiRenderMs: uiRenderMs,
            UiUploadMs: uiUploadMs,
            ScreenOverlayBuildMs: screenOverlayBuildMs,
            ScreenOverlayDrawMs: screenOverlayDrawMs,
            CameraCullingMs: cameraCullingMs,
            CameraPresenterMs: cameraPresenterMs,
            WorldHudProjectionMs: worldHudProjectionMs,
            RenderAccountedMs: renderAccountedMs,
            RenderUnaccountedMs: renderUnaccountedMs,
            PerformerMarkers: timing.PerformerMinimapMarkersLastFrame,
            PerformerMarkersDropped: timing.PerformerMinimapDroppedLastFrame,
            MinimapScreenMarkers: timing.MinimapScreenMarkersLastFrame,
            MinimapScreenMarkersDropped: timing.MinimapScreenMarkersDroppedLastFrame,
            EcsVisibleEntities: ecsVisibleEntities,
            CrowdInViewEntities: simulation.CrowdInViewCount,
            CrowdSubmittedEntities: simulation.CrowdSubmittedCount,
            SubmittedObstacles: simulation.ObstacleSubmittedCount,
                CompositeSkipCountLastSecond: timing.CompositeSkipCountLastSecond,
                WorldWidthCm: simulation.WorldWidthCm,
                WorldHeightCm: simulation.WorldHeightCm,
                SolverWindowWidthCm: (int)MathF.Round(simulation.SolverWindowWidthCm),
                SolverWindowHeightCm: (int)MathF.Round(simulation.SolverWindowHeightCm),
                SolverWindowCenterXCm: (int)MathF.Round(simulation.SolverWindowCenterXCm),
                SolverWindowCenterYCm: (int)MathF.Round(simulation.SolverWindowCenterYCm),
                FlowWorkAreaWidthCm: (int)MathF.Round(simulation.FlowWorkAreaWidthCm),
                FlowWorkAreaHeightCm: (int)MathF.Round(simulation.FlowWorkAreaHeightCm),
                FlowWorkAreaCenterXCm: (int)MathF.Round(simulation.FlowWorkAreaCenterXCm),
                FlowWorkAreaCenterYCm: (int)MathF.Round(simulation.FlowWorkAreaCenterYCm),
                FlowWorkAreaRevision: simulation.FlowWorkAreaRevision,
                FlowWorkAreaReason: simulation.FlowWorkAreaReason,
                CommandFocusTicksRemaining: simulation.CommandFocusTicksRemaining,
                LastCommandSelectionCount: simulation.LastCommandSelectionCount,
                CommandFocusActive: simulation.HasCommandFocus,
                CommandFocusX: simulation.CommandFocusXCm,
                CommandFocusY: simulation.CommandFocusYCm,
                StreamingChunkSizeCm: simulation.StreamingChunkSizeCm,
                LoadedChunkCount: simulation.LoadedChunkCount,
                StreamingWindowUpdatesFrame: simulation.StreamingWindowUpdatesFrame,
                CameraBudgetUpdatesFrame: simulation.CameraBudgetUpdatesFrame,
                CameraBudgetUpdatesTotal: simulation.CameraBudgetUpdatesTotal,
                SolverWindowMovesFrame: simulation.SolverWindowMovesFrame,
                SolverWindowMovesTotal: simulation.SolverWindowMovesTotal,
                ScenarioSpawnCount: simulation.ScenarioSpawnCount,
                SceneResetCount: simulation.SceneResetCount,
                CommandRejectsFrame: simulation.CommandRejectsFrame,
                CommandRejectsTotal: simulation.CommandRejectsTotal,
                LastRejectedCommandX: simulation.LastRejectedCommandXCm,
                LastRejectedCommandY: simulation.LastRejectedCommandYCm,
                SolverWindowDriver: simulation.SolverWindowDriver,
                StrategicWorldViewActive: MassNavigationRuntime.IsStrategicWorldCameraActive(engine),
                ObstacleSemanticsText: obstacleSemanticsText,
            TargetSemanticsText: targetSemanticsText,
            ArrivalSemanticsText: arrivalSemanticsText,
            YieldSemanticsText: yieldSemanticsText,
            CameraTargetX: camera.TargetCm.X,
            CameraTargetY: camera.TargetCm.Y,
            CameraDistanceCm: camera.DistanceCm,
            FirstAgentX: MathF.Round(firstAgentX, 0),
            FirstAgentZ: MathF.Round(firstAgentZ, 0),
            SelectionSnapshotsFrame: simulation.SelectionSnapshotCountFrame,
            StructuralChangesFrame: simulation.StructuralChangesFrame,
            FlowReconcileFrame: simulation.FlowReconcileCountFrame);
    }

    private static int ResolveMaxAgentsPerTeam(GameEngine engine)
    {
        return 40_000;
    }

    private static float ResolveFrameMs(PresentationTimingDiagnostics timing)
    {
        if (timing.WallFrameMs > 0.001f)
        {
            return timing.WallFrameMs;
        }

        if (timing.LastWallFrameMs > 0.001f)
        {
            return timing.LastWallFrameMs;
        }

        if (timing.LastFrameMs > 0.001f)
        {
            return timing.LastFrameMs;
        }

        return timing.FrameMs;
    }

    private UiElementBuilder BuildTeamTargetRow()
    {
        ReadOnlySpan<int> teamIds = _simulation != null
            ? _simulation.TeamIds
            : ReadOnlySpan<int>.Empty;
        if (teamIds.Length == 0)
        {
            return Ui.Text("No active teams.").FontSize(12f).Color("#8EA2BD");
        }

        var buttons = new UiElementBuilder[teamIds.Length];
        for (int i = 0; i < teamIds.Length; i++)
        {
            int teamId = teamIds[i];
            buttons[i] = BuildActionButton($"Team {teamId}", () => SetSelectedTeam(teamId));
        }

        return Ui.Row(buttons)
            .Wrap()
            .Gap(8f);
    }

    private UiElementBuilder BuildKnownContactRow()
    {
        if (_simulation == null)
        {
            return Ui.Text("No debug landmarks.").FontSize(12f).Color("#8EA2BD");
        }

        ReadOnlySpan<MassNavigationHotZoneConfig> hotZones = _simulation.HotZones;
        if (hotZones.Length == 0)
        {
            return Ui.Text("No debug landmarks.").FontSize(12f).Color("#8EA2BD");
        }

        var buttons = new UiElementBuilder[hotZones.Length];
        for (int i = 0; i < hotZones.Length; i++)
        {
            string contactId = hotZones[i].Id;
            string label = hotZones[i].Label;
            buttons[i] = BuildActionButton(label, () => JumpToKnownContact(contactId));
        }

        return Ui.Row(buttons)
            .Wrap()
            .Gap(8f);
    }
}


