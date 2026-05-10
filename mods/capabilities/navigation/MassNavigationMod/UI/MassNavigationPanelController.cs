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
    private const long PerfRefreshTicks = TimeSpan.TicksPerSecond / 4;

    private ReactivePage<MassNavigationPanelState>? _page;
    private MassNavigationPanelState _lastState = MassNavigationPanelState.Empty;
    private GameEngine? _engine;
    private MassNavigationSimulationRuntime? _simulation;
    private long _lastPerfCaptureTicks;
    private string _lastActionText = "MassNavigation runtime, flow, and arrival knobs hot-apply now. Physics/Nav buttons only touch engine policies. Agent count and Reset rebuild the scene.";

    public bool MountOrSync(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return false;
        }

        _engine = engine;
        _simulation = simulation;
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
        if (_lastPerfCaptureTicks != 0 && nowTicks - _lastPerfCaptureTicks < refreshTicks)
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
        _lastActionText = "MassNavigation runtime, flow, and arrival knobs hot-apply now. Physics/Nav buttons only touch engine policies. Agent count and Reset rebuild the scene.";
        _page?.SetState(_ => MassNavigationPanelState.Empty);
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
        string yieldSemanticsText = $"Yield semantics: nav mass light/heavy = {simulation.MassFlow.AvoidanceTuning.LightNavMass:0.##}/{simulation.MassFlow.AvoidanceTuning.HeavyNavMass:0.##}. dominant ratio {simulation.MassFlow.AvoidanceTuning.DominantMassRatio:0.##}. response friendly/non-friendly/push = {simulation.MassFlow.AvoidanceTuning.FriendlyResponseScale:0.##}/{simulation.MassFlow.AvoidanceTuning.NonFriendlyResponseScale:0.##}/{simulation.MassFlow.AvoidanceTuning.DominantPushResponseScale:0.##}.";
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
}


