using System;
using System.Diagnostics;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Engine.Physics2D;
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
    private ReactivePage<MassNavigationPanelState>? _page;
    private MassNavigationPanelState _lastState = MassNavigationPanelState.Empty;
    private GameEngine? _engine;
    private MassNavigationSimulationRuntime? _simulation;
    private long _lastPerfCaptureTicks;
    private long _perfRefreshStopwatchTicks;
    private string _lastActionText = "MassNavigation runtime, flow, and arrival knobs hot-apply now. Physics/Nav buttons only touch engine policies. Agent count and Reset rebuild the scene.";

    public bool MountOrSync(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return false;
        }

        _engine = engine;
        _simulation = simulation;
        _perfRefreshStopwatchTicks = ResolvePerfRefreshStopwatchTicks(simulation);
        var page = EnsurePage(engine);
        bool changed = false;
        if (!ReferenceEquals(root.Scene, page.Scene))
        {
            root.MountScene(page.Scene);
            root.IsDirty = true;
            changed = true;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        if (_lastPerfCaptureTicks != 0 && nowTicks - _lastPerfCaptureTicks < _perfRefreshStopwatchTicks)
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
        _lastState = MassNavigationPanelState.Empty;
        _lastPerfCaptureTicks = 0;
        _perfRefreshStopwatchTicks = 0;
        _lastActionText = "MassNavigation runtime, flow, and arrival knobs hot-apply now. Physics/Nav buttons only touch engine policies. Agent count and Reset rebuild the scene.";
        _page?.SetState(_ => MassNavigationPanelState.Empty);
    }

    private static long ResolvePerfRefreshStopwatchTicks(MassNavigationSimulationRuntime simulation)
    {
        MassNavigationPanelControlsConfig controls = simulation.Config.ScenarioRuntime.PanelControls
            ?? throw new InvalidOperationException("MassNavigation config requires scenarioRuntime.panelControls.");
        float intervalSeconds = controls.PanelRefreshIntervalSeconds;
        if (!(intervalSeconds > 0f))
        {
            throw new InvalidOperationException(
                "MassNavigation config requires scenarioRuntime.panelControls.panelRefreshIntervalSeconds > 0.");
        }

        double stopwatchTicks = Math.Ceiling(intervalSeconds * Stopwatch.Frequency);
        if (stopwatchTicks < 1d || stopwatchTicks > long.MaxValue)
        {
            throw new InvalidOperationException(
                "MassNavigation config scenarioRuntime.panelControls.panelRefreshIntervalSeconds cannot be represented by Stopwatch ticks.");
        }

        return (long)stopwatchTicks;
    }

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
        MassNavigationPanelControlsConfig controls = RequirePanelControls();
        return Ui.Card(
                Ui.Text("Mass Navigation").FontSize(20f).Bold().Color("#F8FBFF"),
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
                Ui.Text($"Teams {state.TeamCount}  Agents/team {state.AgentsPerTeam}  Total {state.TotalAgents}  Selectable {state.SelectableAgents}  Obstacles {state.Blockers}").FontSize(13f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Selected {state.SelectedCount}  Rev {state.SelectionRevision}  Commands/frame {state.CommandCountFrame}").FontSize(13f).Color("#A4F07A"),
                Ui.Text($"Team target {state.SelectedTeamId}  Formation {state.FormationLabel}  Groups {state.FormationCount}  Rotation {state.FormationRotationDeg:0.0} deg").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"FPS {state.Fps:0}  Frame {state.FrameMs:0.0} ms  Performer {state.PerformerEmitMs:0.0} ms  Minimap {state.MinimapProjectionMs:0.0} ms").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"InputCollection: select {state.SelectionSyncHzObserved:0.0} Hz  control {state.ControlHzObserved:0.0} Hz  capture {state.CommandHzObserved:0.0} Hz").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"PostMovement: mass {state.SimHzObserved:0.0}/{state.MassNavigationSimulationHz} Hz  Presentation: performer {state.PerformerHzObserved:0.0} Hz  hud {state.HudHzObserved:0.0} Hz  panel {state.PanelHzObserved:0.0} Hz").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Mass cadence: target {state.MassNavigationTargetUpdateHz} Hz  flow {state.MassNavigationFlowStepHz}/{state.MassNavigationFlowCrowdStampHz}/{state.MassNavigationFlowObstacleStampHz} Hz  resolve {state.MassNavigationHardResolveHz} Hz  sync {state.MassNavigationEntitySyncHz} Hz").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.LastActionText).FontSize(12f).Color("#8FE388").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("World Map").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Battlefield {state.WorldWidthCm / 100_000f:0.0}x{state.WorldHeightCm / 100_000f:0.0} km  landmarks {state.WorldMarkerCount}  active chunks {state.LoadedChunkCount} @ {state.StreamingChunkSizeCm} cm").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Flow area {state.FlowWorkAreaWidthCm / 100f:0}x{state.FlowWorkAreaHeightCm / 100f:0} m at ({state.FlowWorkAreaCenterXCm},{state.FlowWorkAreaCenterYCm}) cm  rev {state.FlowWorkAreaRevision}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Solver cache {state.SolverWindowWidthCm / 100f:0}x{state.SolverWindowHeightCm / 100f:0} m at ({state.SolverWindowCenterXCm},{state.SolverWindowCenterYCm}) cm  driver {state.SolverWindowDriver}").FontSize(12f).Color("#9FD8FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Camera target ({state.CameraTargetX:0},{state.CameraTargetY:0}) cm  distance {state.CameraDistanceCm:0}  chunk updates {state.StreamingWindowUpdatesFrame}").FontSize(12f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Culling focus {state.ViewResidencyMode}  probe {state.ActiveProbeLabel}  TTL {state.ViewResidencyRetainSeconds:0.0}s  radius {state.ViewResidencyRadiusCm / 100f:0}m  override {(state.CullingProbeActive ? "On" : "Off")}").FontSize(12f).Color("#9FD8FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Last command target ({state.CommandFocusX:0},{state.CommandFocusY:0}) cm  selected payload {state.LastCommandSelectionCount}  invalid orders {state.CommandRejectsFrame}/{state.CommandRejectsTotal}").FontSize(11f).Color(state.CommandRejectsFrame > 0 ? "#FF9A73" : "#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton("Full Map", RequestStrategicWorldCamera),
                        BuildActionButton("Field Camera", RequestCameraReset))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Cull Camera", UseCameraCullingFocus),
                        BuildActionButton("Cull Probe", UseProbeCullingFocus),
                        BuildActionButton($"TTL -{controls.ViewResidencyRetainSecondsStep:0.##}s", () => AdjustCullingRetainSeconds(-controls.ViewResidencyRetainSecondsStep)),
                        BuildActionButton($"TTL +{controls.ViewResidencyRetainSecondsStep:0.##}s", () => AdjustCullingRetainSeconds(controls.ViewResidencyRetainSecondsStep)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Debug Landmarks").FontSize(12f).Bold().Color("#F4C77D"),
                BuildKnownContactRow(),
                Ui.Text("Culling Probes").FontSize(12f).Bold().Color("#F4C77D"),
                BuildCameraProbeRow(),
                Ui.Text("Formation").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                        BuildActionButton("Rotate Left", () => QueueSelectionRotate(-1f)),
                        BuildActionButton("Rotate Right", () => QueueSelectionRotate(1f)))
                    .Wrap()
                    .Gap(8f),
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
                        BuildActionButton($"-{controls.SimulationBudgetStepMs} ms", () => AdjustSimulationBudget(-controls.SimulationBudgetStepMs)),
                        BuildActionButton($"+{controls.SimulationBudgetStepMs} ms", () => AdjustSimulationBudget(controls.SimulationBudgetStepMs)),
                        BuildActionButton($"-{controls.SimulationSliceStep} slice", () => AdjustSimulationSlices(-controls.SimulationSliceStep)),
                        BuildActionButton($"+{controls.SimulationSliceStep} slice", () => AdjustSimulationSlices(controls.SimulationSliceStep)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton(state.ArrivalRecoveryEnabled ? "Recovery On" : "Recovery Off", ToggleArrivalRecovery),
                        BuildActionButton($"Timeout -{controls.ArrivalTimeoutStepMs}", () => AdjustArrivalTimeoutMs(-controls.ArrivalTimeoutStepMs)),
                        BuildActionButton($"Timeout +{controls.ArrivalTimeoutStepMs}", () => AdjustArrivalTimeoutMs(controls.ArrivalTimeoutStepMs)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton($"Progress -{controls.ArrivalProgressStepCm}", () => AdjustArrivalProgressCm(-controls.ArrivalProgressStepCm)),
                        BuildActionButton($"Progress +{controls.ArrivalProgressStepCm}", () => AdjustArrivalProgressCm(controls.ArrivalProgressStepCm)),
                        BuildActionButton($"Wake -{controls.ArrivalWakePushStepCm}", () => AdjustArrivalWakePushCm(-controls.ArrivalWakePushStepCm)),
                        BuildActionButton($"Wake +{controls.ArrivalWakePushStepCm}", () => AdjustArrivalWakePushCm(controls.ArrivalWakePushStepCm)),
                        BuildActionButton($"Retry -{controls.ArrivalRetryStep}", () => AdjustArrivalMaxRetries(-controls.ArrivalRetryStep)),
                        BuildActionButton($"Retry +{controls.ArrivalRetryStep}", () => AdjustArrivalMaxRetries(controls.ArrivalRetryStep)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Engine Policy Only").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Physics {state.PhysicsHz} Hz / max {state.PhysicsMaxStepsPerFixedTick}  Nav {state.NavigationHz} Hz / max {state.NavigationMaxStepsPerFixedTick}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("LogicHz follows Engine/clock.json and drives the MassNavigation simulation. Physics/Nav buttons apply engine policy, while this mass crowd runtime uses its own configured cadence.").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton($"Phys -{controls.EnginePolicyHzStep}", () => AdjustPhysicsHz(-controls.EnginePolicyHzStep)),
                        BuildActionButton($"Phys +{controls.EnginePolicyHzStep}", () => AdjustPhysicsHz(controls.EnginePolicyHzStep)),
                        BuildActionButton($"Phys Max-{controls.EnginePolicyMaxStepsStep}", () => AdjustPhysicsMaxSteps(-controls.EnginePolicyMaxStepsStep)),
                        BuildActionButton($"Phys Max+{controls.EnginePolicyMaxStepsStep}", () => AdjustPhysicsMaxSteps(controls.EnginePolicyMaxStepsStep)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton($"Nav -{controls.EnginePolicyHzStep}", () => AdjustNavigationHz(-controls.EnginePolicyHzStep)),
                        BuildActionButton($"Nav +{controls.EnginePolicyHzStep}", () => AdjustNavigationHz(controls.EnginePolicyHzStep)),
                        BuildActionButton($"Nav Max-{controls.EnginePolicyMaxStepsStep}", () => AdjustNavigationMaxSteps(-controls.EnginePolicyMaxStepsStep)),
                        BuildActionButton($"Nav Max+{controls.EnginePolicyMaxStepsStep}", () => AdjustNavigationMaxSteps(controls.EnginePolicyMaxStepsStep)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("MassNavigation Flow").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Flow {(state.FlowEnabled ? "On" : "Off")}  Crowd budget {state.FlowIterations}  Hz step/crowd/obs {state.MassNavigationFlowStepHz}/{state.MassNavigationFlowCrowdStampHz}/{state.MassNavigationFlowObstacleStampHz}  resolve {state.MassNavigationHardResolveHz}  sync {state.MassNavigationEntitySyncHz}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Flow rebuild(last) {state.FlowFieldRebuildMs:0.0} ms  target-driven  rebuild/frame {state.FlowReconcileFrame}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Cadence knobs hot-apply through the MassFlow config scheduler; entity writeback uses a solver dirty queue at the configured sync Hz.").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton(state.FlowEnabled ? "Flow On" : "Flow Off", ToggleFlowEnabled),
                        BuildActionButton($"Iter -{controls.FlowIterationStep}", () => AdjustFlowIterations(-controls.FlowIterationStep)),
                        BuildActionButton($"Iter +{controls.FlowIterationStep}", () => AdjustFlowIterations(controls.FlowIterationStep)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton($"Step Hz -{controls.FlowCadenceHzStep}", () => AdjustFlowStepHz(-controls.FlowCadenceHzStep)),
                        BuildActionButton($"Step Hz +{controls.FlowCadenceHzStep}", () => AdjustFlowStepHz(controls.FlowCadenceHzStep)),
                        BuildActionButton($"Crowd Hz -{controls.FlowCadenceHzStep}", () => AdjustFlowCrowdHz(-controls.FlowCadenceHzStep)),
                        BuildActionButton($"Crowd Hz +{controls.FlowCadenceHzStep}", () => AdjustFlowCrowdHz(controls.FlowCadenceHzStep)),
                        BuildActionButton($"Obs Hz -{controls.FlowCadenceHzStep}", () => AdjustFlowObstacleHz(-controls.FlowCadenceHzStep)),
                        BuildActionButton($"Obs Hz +{controls.FlowCadenceHzStep}", () => AdjustFlowObstacleHz(controls.FlowCadenceHzStep)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton($"Resolve Hz -{controls.FlowCadenceHzStep}", () => AdjustHardResolveHz(-controls.FlowCadenceHzStep)),
                        BuildActionButton($"Resolve Hz +{controls.FlowCadenceHzStep}", () => AdjustHardResolveHz(controls.FlowCadenceHzStep)),
                        BuildActionButton($"Sync Hz -{controls.FlowCadenceHzStep}", () => AdjustEntitySyncHz(-controls.FlowCadenceHzStep)),
                        BuildActionButton($"Sync Hz +{controls.FlowCadenceHzStep}", () => AdjustEntitySyncHz(controls.FlowCadenceHzStep)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Scene Rebuild Required").FontSize(12f).Bold().Color("#F4C77D"),
                BuildSceneRebuildControls(),
                Ui.Text("Semantic Contract").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text(state.ObstacleSemanticsText).FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.TargetSemanticsText).FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.ArrivalSemanticsText).FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.YieldSemanticsText).FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Team Target").FontSize(12f).Bold().Color("#F4C77D"),
                BuildTeamTargetRow(),
                Ui.Text("Diagnostics").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Select {state.SelectionSyncMs:0.0} ms  Group {state.FormationTargetMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
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

    private static UiElementBuilder BuildActionButton(string label, Action onClick)
    {
        return Ui.Button(label, _ => onClick())
            .Background("#182436")
            .Color("#F8FBFF")
            .Padding(10f, 8f)
            .Radius(10f);
    }

    private UiElementBuilder BuildFormationButton(string label, MassNavigationFormationMode mode, string currentLabel)
    {
        bool active = currentLabel.Equals(mode.ToString(), StringComparison.Ordinal);
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

    private void AdjustTotalAgentsDown() => AdjustTotalAgents(-RequirePanelControls().TotalAgentStep);
    private void AdjustTotalAgentsUp() => AdjustTotalAgents(RequirePanelControls().TotalAgentStep);

    private void AdjustTotalAgents(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int currentTotal = checked(_simulation.AgentsPerTeam * _simulation.TeamCount);
        SetTotalAgents(currentTotal + delta);
    }

    private void SetTotalAgents(int totalAgents)
    {
        if (_engine == null || _simulation == null)
        {
            return;
        }

        if (totalAgents < 0)
        {
            throw new InvalidOperationException(
                $"MassNavigation panel requested negative totalAgents {totalAgents}; configure scenarioRuntime.panelControls.totalAgentStep for reachable values.");
        }

        int teamCount = _simulation.TeamCount;
        if (teamCount <= 0)
        {
            throw new InvalidOperationException("MassNavigation panel requires at least one configured team.");
        }

        MassNavigationPanelControlsConfig panelControls = RequirePanelControls();
        if (totalAgents % teamCount != 0)
        {
            throw new InvalidOperationException(
                $"MassNavigation panel requested totalAgents {totalAgents}, which does not divide evenly across {teamCount} teams.");
        }

        int perTeam = totalAgents / teamCount;
        if (perTeam > panelControls.MaxAgentsPerTeam)
        {
            throw new InvalidOperationException(
                $"MassNavigation panel requested agents/team {perTeam}, exceeding configured scenarioRuntime.panelControls.maxAgentsPerTeam {panelControls.MaxAgentsPerTeam}.");
        }

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

    private void QueueSelectionRotate(float direction)
    {
        if (_engine == null || _simulation == null)
        {
            return;
        }

        float deltaRadians = direction * _simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond;
        if (_simulation.RotateSelectedFormation(_engine.World, deltaRadians, ResolveLocalPlayerId()))
        {
            SetActionFeedback($"Applied rotate: selected formation rotation {deltaRadians * 180f / MathF.PI:0.0} deg.");
        }
    }

    private int ResolveLocalPlayerId()
    {
        if (_engine == null)
        {
            throw new InvalidOperationException("MassNavigation panel requires GameEngine before resolving local player ownership.");
        }

        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Arch.Core.Entity local ||
            !_engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("MassNavigation panel requires LocalPlayerEntity before rotating formations.");
        }

        if (!_engine.World.TryGet(local, out Ludots.Core.Gameplay.Components.PlayerOwner owner))
        {
            throw new InvalidOperationException("MassNavigation panel LocalPlayerEntity must author PlayerOwner before rotating formations.");
        }

        return owner.PlayerId;
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
        MassNavigationRuntime.RequestCameraJump(_engine, targetCm);
        MassNavigationRuntime.RequestMinimapStrategicWorldView(_engine);
        SetActionFeedback($"Camera moved to debug landmark {contact.Label}; camera budget updated without respawn or retarget.");
    }

    private void UseCameraCullingFocus()
    {
        if (_engine == null || _simulation == null)
        {
            return;
        }

        _simulation.SetViewResidencyMode("Camera");
        MassNavigationRuntime.ApplyCullingFocusOverride(_engine);
        SetActionFeedback("Hot apply: culling focus follows the real camera.");
    }

    private void UseProbeCullingFocus()
    {
        if (_engine == null || _simulation == null)
        {
            return;
        }

        _simulation.SetViewResidencyMode("Probe");
        MassNavigationRuntime.ApplyCullingFocusOverride(_engine);
        SetActionFeedback($"Hot apply: culling focus uses probe {_simulation.ViewResidency.ActiveProbe.Label}.");
    }

    private void JumpToCameraProbe(string probeId)
    {
        if (_engine == null || _simulation == null)
        {
            return;
        }

        _simulation.SetViewResidencyProbe(probeId);
        _simulation.SetViewResidencyMode("Probe");
        MassNavigationRuntime.ApplyCullingFocusOverride(_engine);
        MassNavigationCameraProbeConfig probe = _simulation.ViewResidency.ActiveProbe;
        SetActionFeedback($"Hot apply: culling probe = {probe.Label}. Real camera was not moved.");
    }

    private void AdjustCullingRetainSeconds(float deltaSeconds)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.AdjustViewResidencyRetainSeconds(deltaSeconds);
        SetActionFeedback($"Hot apply: culling chunk retention = {_simulation.ViewResidency.RetainSeconds:0.0}s.");
    }

    private void AdjustSimulationBudget(int delta)
    {
        if (_engine == null)
        {
            return;
        }

        MassNavigationPanelControlsConfig controls = RequirePanelControls();
        _engine.SimulationBudgetMsPerFrame = Math.Clamp(
            _engine.SimulationBudgetMsPerFrame + delta,
            controls.SimulationBudgetMinMs,
            controls.SimulationBudgetMaxMs);
        SetActionFeedback($"Hot apply: simulation budget = {_engine.SimulationBudgetMsPerFrame} ms/frame.");
    }

    private void AdjustSimulationSlices(int delta)
    {
        if (_engine == null)
        {
            return;
        }

        MassNavigationPanelControlsConfig controls = RequirePanelControls();
        _engine.SimulationMaxSlicesPerLogicFrame = Math.Clamp(
            _engine.SimulationMaxSlicesPerLogicFrame + delta,
            controls.SimulationSliceMin,
            controls.SimulationSliceMax);
        SetActionFeedback($"Hot apply: simulation slice limit = {_engine.SimulationMaxSlicesPerLogicFrame}.");
    }

    private void AdjustPhysicsHz(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Physics2DTickPolicy) is not Physics2DTickPolicy policy)
        {
            return;
        }

        MassNavigationPanelControlsConfig controls = RequirePanelControls();
        policy.SetTargetHz(Math.Clamp(policy.TargetHz + delta, controls.EnginePolicyHzMin, controls.EnginePolicyHzMax));
        SetActionFeedback($"Engine policy hot apply: physics = {policy.TargetHz} Hz. Current mass-navigation custom sim still runs on LogicHz/InputCollection.");
    }

    private void AdjustPhysicsMaxSteps(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Physics2DTickPolicy) is not Physics2DTickPolicy policy)
        {
            return;
        }

        MassNavigationPanelControlsConfig controls = RequirePanelControls();
        policy.SetMaxStepsPerFixedTick(Math.Clamp(
            policy.MaxStepsPerFixedTick + delta,
            controls.EnginePolicyMaxStepsMin,
            controls.EnginePolicyMaxStepsMax));
        SetActionFeedback($"Engine policy hot apply: physics max steps = {policy.MaxStepsPerFixedTick}. Current mass-navigation custom sim is not consuming Physics2D ticks.");
    }

    private void AdjustNavigationHz(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Navigation2DTickPolicy) is not Navigation2DTickPolicy policy)
        {
            return;
        }

        MassNavigationPanelControlsConfig controls = RequirePanelControls();
        policy.SetTargetHz(Math.Clamp(policy.TargetHz + delta, controls.EnginePolicyHzMin, controls.EnginePolicyHzMax));
        SetActionFeedback($"Engine policy hot apply: navigation = {policy.TargetHz} Hz. Current mass-navigation custom sim still runs on LogicHz/InputCollection.");
    }

    private void AdjustNavigationMaxSteps(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Navigation2DTickPolicy) is not Navigation2DTickPolicy policy)
        {
            return;
        }

        MassNavigationPanelControlsConfig controls = RequirePanelControls();
        policy.SetMaxStepsPerFixedTick(Math.Clamp(
            policy.MaxStepsPerFixedTick + delta,
            controls.EnginePolicyMaxStepsMin,
            controls.EnginePolicyMaxStepsMax));
        SetActionFeedback($"Engine policy hot apply: navigation max steps = {policy.MaxStepsPerFixedTick}. Current mass-navigation custom sim is not consuming Navigation2D ticks.");
    }

    private void ToggleFlowEnabled()
    {
        if (_simulation == null)
        {
            return;
        }

        bool enabled = _simulation.ToggleFlowEnabled();
        SetActionFeedback($"Hot apply: flow dynamic crowd stamp = {(enabled ? "On" : "Off")}.");
    }

    private void AdjustFlowIterations(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int iterations = _simulation.AdjustFlowIterations(delta);
        SetActionFeedback($"Hot apply: flow crowd stamp budget = {iterations}.");
    }

    private void AdjustFlowStepHz(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int hz = _simulation.AdjustFlowStepHz(delta);
        SetActionFeedback($"Hot apply: flow solve cadence = {hz} Hz.");
    }

    private void AdjustFlowCrowdHz(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int hz = _simulation.AdjustFlowCrowdStampHz(delta);
        SetActionFeedback($"Hot apply: flow crowd stamp cadence = {hz} Hz.");
    }

    private void AdjustFlowObstacleHz(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int hz = _simulation.AdjustFlowObstacleStampHz(delta);
        SetActionFeedback($"Hot apply: flow obstacle stamp cadence = {hz} Hz.");
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

        bool enabled = _simulation.ToggleArrivalRecovery();
        SetActionFeedback($"Hot apply: arrival recovery = {(enabled ? "On" : "Off")}.");
    }

    private void AdjustArrivalTimeoutMs(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int timeoutMs = _simulation.AdjustArrivalTimeoutMs(delta);
        SetActionFeedback($"Hot apply: arrival timeout = {timeoutMs} ms.");
    }

    private void AdjustArrivalProgressCm(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int distanceCm = _simulation.AdjustArrivalProgressDistanceCm(delta);
        SetActionFeedback($"Hot apply: arrival progress = {distanceCm} cm.");
    }

    private void AdjustArrivalWakePushCm(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int distanceCm = _simulation.AdjustArrivalWakePushDistanceCm(delta);
        SetActionFeedback($"Hot apply: arrival wake push = {distanceCm} cm.");
    }

    private void AdjustArrivalMaxRetries(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int retryCount = _simulation.AdjustArrivalMaxRetryCount(delta);
        SetActionFeedback($"Hot apply: arrival max retries = {retryCount}.");
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
        bool visible = engine.CurrentMapSession != null && MassNavigationIds.IsNavigationMap(engine, engine.CurrentMapSession.MapId.Value);
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
        if (simulation.AgentState.TryGetControllableEntity(0, out var first))
        {
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
        MassNavigationSolverDiagnostics solver = simulation.CaptureSolverDiagnostics();
        string obstacleSemanticsText = $"Obstacle semantics: visible radius = authored obstacle. hard block = visible + each agent body radius. soft push = visible + each agent body radius + {solver.ObstacleSoftPushPaddingCm:0} cm.";
        string targetSemanticsText = $"Target semantics: team target clear {solver.TeamTargetClearanceCm:0} cm. group center clear {solver.GroupCenterClearanceCm:0} cm. team slot clear {solver.TeamSlotClearanceCm:0} cm. loose/group slot clear {solver.LooseTargetClearanceCm:0}/{solver.GroupSlotClearanceCm:0} cm.";
        string arrivalSemanticsText = $"Arrival semantics: stop threshold {solver.UnitTargetStopThresholdCm:0} cm. settle timeout/progress/wake/retry = {solver.ArrivalTimeoutMs}/{solver.ArrivalProgressDistanceCm}/{solver.ArrivalWakePushDistanceCm}/{solver.ArrivalMaxRetryCount}. flow slow radius loose/group = {solver.GoalArrivalRadiusCm:0}/{solver.FormationFlowSlowRadiusCm:0} cm.";
        string yieldSemanticsText = $"Yield semantics: profile nav mass comes from agentProfiles. dominant ratio {solver.DominantMassRatio:0.##}. response friendly/non-friendly/push = {solver.FriendlyResponseScale:0.##}/{solver.NonFriendlyResponseScale:0.##}/{solver.DominantPushResponseScale:0.##}.";
        return new MassNavigationPanelState(
            Visible: visible,
            LastActionText: _lastActionText,
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
            SelectableAgents: simulation.AgentState.ControllableAgentCount,
            Blockers: simulation.AgentState.BlockerCount,
            WorldMarkerCount: simulation.AgentState.WorldMarkerCount,
            SelectedTeamId: simulation.SelectedTeamId,
            SelectedCount: simulation.SelectedCount,
            SelectionRevision: simulation.SelectionRevision,
            CommandCountFrame: simulation.CommandCountFrame,
            FormationCount: simulation.NavGroupRuntime.ActiveGroupCount,
            FormationLabel: simulation.FormationMode.ToString(),
            FormationRotationDeg: simulation.NavGroupRuntime.SelectedRotationRadians * (180f / MathF.PI),
            FlowEnabled: solver.FlowEnabled,
            FlowIterations: solver.FlowIterationsPerStep,
            ArrivalRecoveryEnabled: solver.ArrivalRecoveryEnabled,
            ArrivalTimeoutMs: solver.ArrivalTimeoutMs,
            ArrivalProgressCm: solver.ArrivalProgressDistanceCm,
            ArrivalWakePushCm: solver.ArrivalWakePushDistanceCm,
            ArrivalMaxRetries: solver.ArrivalMaxRetryCount,
            ArrivalSettledUnits: solver.ArrivalSettledUnitCount,
            Fps: fps,
            FrameMs: frameMs,
            PerformerEmitMs: performerEmitMs,
            PerformerTransformMs: performerTransformMs,
            PerformerMinimapMarkerMs: performerMinimapMarkerMs,
            MinimapProjectionMs: minimapProjectionMs,
            ViewportWidth: viewportResolution.X,
            ViewportHeight: viewportResolution.Y,
            SelectionSyncMs: MathF.Round(simulation.SelectionSyncMs, 1),
            FormationTargetMs: MathF.Round(simulation.FormationTargetMs, 1),
            FlowFieldRebuildMs: MathF.Round(solver.FlowFieldRebuildMs, 1),
            StepPrepMs: MathF.Round(simulation.StepPrepMs, 1),
            LocalSteeringMs: MathF.Round(simulation.LocalSteeringMs, 1),
            SimStepMs: MathF.Round(simulation.SimStepMs, 1),
            HardResolveMs: MathF.Round(simulation.HardResolveMs, 1),
            EntitySyncMs: MathF.Round(simulation.EntitySyncMs, 1),
            PerformerCommandMs: MathF.Round(simulation.PerformerCommandMs, 1),
            SelectionSyncHzObserved: MathF.Round(simulation.SelectionSyncHzObserved, 1),
            ControlHzObserved: MathF.Round(simulation.ControlHzObserved, 1),
            CommandHzObserved: MathF.Round(simulation.CommandHzObserved, 1),
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
                ViewResidencyMode: simulation.ViewResidency.Mode,
                ActiveProbeId: simulation.ViewResidency.ActiveProbe.Id,
                ActiveProbeLabel: simulation.ViewResidency.ActiveProbe.Label,
                ViewResidencyRetainSeconds: simulation.ViewResidency.RetainSeconds,
                ViewResidencyRadiusCm: simulation.ViewResidency.RadiusCm,
                CullingProbeActive: simulation.ViewResidency.UsesProbeFocus,
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

    private static float ResolveFrameMs(PresentationTimingDiagnostics timing)
    {
        if (timing.WallFrameMs > 0.001f)
        {
            return timing.WallFrameMs;
        }

        if (timing.FrameMs > 0.001f)
        {
            return timing.FrameMs;
        }

        if (timing.LastWallFrameMs > 0.001f)
        {
            return timing.LastWallFrameMs;
        }

        return timing.LastFrameMs;
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

    private UiElementBuilder BuildCameraProbeRow()
    {
        if (_simulation == null)
        {
            return Ui.Text("No culling probes.").FontSize(12f).Color("#8EA2BD");
        }

        MassNavigationCameraProbeConfig[] probes = _simulation.ViewResidency.CameraProbes;
        if (probes.Length == 0)
        {
            return Ui.Text("No culling probes.").FontSize(12f).Color("#8EA2BD");
        }

        var buttons = new UiElementBuilder[probes.Length];
        for (int i = 0; i < probes.Length; i++)
        {
            string probeId = probes[i].Id;
            string label = probes[i].Label;
            buttons[i] = BuildActionButton(label, () => JumpToCameraProbe(probeId));
        }

        return Ui.Row(buttons)
            .Wrap()
            .Gap(8f);
    }

    private UiElementBuilder BuildSceneRebuildControls()
    {
        if (_simulation == null || !_simulation.Config.ScenarioRuntime.AutoSpawnConfiguredScenario)
        {
            return Ui.Text("Externally-authored scenarios use their own authored agent config for unit counts.")
                .FontSize(11f)
                .Color("#8EA2BD")
                .WhiteSpace(UiWhiteSpace.Normal);
        }

        MassNavigationPanelControlsConfig panelControls = RequirePanelControls();
        int[] presets = panelControls.TotalAgentPresets;
        var buttons = new UiElementBuilder[presets.Length + 2];
        int nextButton = 0;
        buttons[nextButton++] = BuildActionButton(
            $"-{FormatAgentCount(panelControls.TotalAgentStep)}",
            AdjustTotalAgentsDown);
        for (int i = 0; i < presets.Length; i++)
        {
            int totalAgents = presets[i];
            buttons[nextButton++] = BuildActionButton(
                FormatAgentCount(totalAgents),
                () => SetTotalAgents(totalAgents));
        }

        buttons[nextButton] = BuildActionButton(
            $"+{FormatAgentCount(panelControls.TotalAgentStep)}",
            AdjustTotalAgentsUp);

        return Ui.Row(buttons)
            .Wrap()
            .Gap(8f);
    }

    private MassNavigationPanelControlsConfig RequirePanelControls()
    {
        if (_simulation == null)
        {
            throw new InvalidOperationException("MassNavigation panel controls require an active simulation.");
        }

        return _simulation.Config.ScenarioRuntime.PanelControls
            ?? throw new InvalidOperationException("MassNavigation config requires scenarioRuntime.panelControls.");
    }

    private static string FormatAgentCount(int totalAgents) => totalAgents.ToString();
}


