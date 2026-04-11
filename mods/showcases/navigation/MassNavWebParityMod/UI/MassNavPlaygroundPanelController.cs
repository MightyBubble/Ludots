using System;
using System.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.UI;

internal sealed class MassNavWebParityPanelController
{
    private const long PerfRefreshTicks = TimeSpan.TicksPerSecond / 4;

    private ReactivePage<MassNavPanelState>? _page;
    private MassNavPanelState _lastState = MassNavPanelState.Empty;
    private GameEngine? _engine;
    private MassNavSimulationRuntime? _simulation;
    private long _lastPerfCaptureTicks;
    private string _lastActionText = "MassNav runtime knobs hot-apply now. Physics/Nav buttons only touch engine policies. Flow is config-only here. Agent count and Reset rebuild the scene.";

    public bool MountOrSync(GameEngine engine, MassNavSimulationRuntime simulation)
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

        MassNavPanelState next = CaptureState(engine, simulation);
        long nowTicks = Stopwatch.GetTimestamp();
        long refreshTicks = (long)(PerfRefreshTicks * (Stopwatch.Frequency / (double)TimeSpan.TicksPerSecond));
        if (_lastPerfCaptureTicks != 0 && nowTicks - _lastPerfCaptureTicks < refreshTicks)
        {
            next = next with
            {
                RenderFps = _lastState.RenderFps,
                RenderFrameMs = _lastState.RenderFrameMs,
                PrimitiveRenderMs = _lastState.PrimitiveRenderMs,
                SelectionSyncMs = _lastState.SelectionSyncMs,
                CommandApplyMs = _lastState.CommandApplyMs,
                FormationTargetMs = _lastState.FormationTargetMs,
                FlowFieldRebuildMs = _lastState.FlowFieldRebuildMs,
                StepPrepMs = _lastState.StepPrepMs,
                LocalSteeringMs = _lastState.LocalSteeringMs,
                SimStepMs = _lastState.SimStepMs,
                HardResolveMs = _lastState.HardResolveMs,
                EntitySyncMs = _lastState.EntitySyncMs,
                PrimitiveEmitMs = _lastState.PrimitiveEmitMs,
                SelectionSyncHzObserved = _lastState.SelectionSyncHzObserved,
                ControlHzObserved = _lastState.ControlHzObserved,
                CommandHzObserved = _lastState.CommandHzObserved,
                CommandDispatchHzObserved = _lastState.CommandDispatchHzObserved,
                SimHzObserved = _lastState.SimHzObserved,
                PrimitiveHzObserved = _lastState.PrimitiveHzObserved,
                HudHzObserved = _lastState.HudHzObserved,
                PanelHzObserved = _lastState.PanelHzObserved,
                UiInputMs = _lastState.UiInputMs,
                UiRenderMs = _lastState.UiRenderMs,
                UiUploadMs = _lastState.UiUploadMs,
                ScreenOverlayBuildMs = _lastState.ScreenOverlayBuildMs,
                ScreenOverlayDrawMs = _lastState.ScreenOverlayDrawMs,
                CameraCullingMs = _lastState.CameraCullingMs,
                CameraPresenterMs = _lastState.CameraPresenterMs,
                WorldHudProjectionMs = _lastState.WorldHudProjectionMs,
                RenderAccountedMs = _lastState.RenderAccountedMs,
                RenderUnaccountedMs = _lastState.RenderUnaccountedMs,
                PrimitiveBufferCount = _lastState.PrimitiveBufferCount,
                PrimitiveInstances = _lastState.PrimitiveInstances,
                PrimitiveBatches = _lastState.PrimitiveBatches,
                PrimitiveDropped = _lastState.PrimitiveDropped,
                EcsVisibleEntities = _lastState.EcsVisibleEntities,
                CrowdInViewEntities = _lastState.CrowdInViewEntities,
                CrowdSubmittedEntities = _lastState.CrowdSubmittedEntities,
                SubmittedObstacles = _lastState.SubmittedObstacles,
                CompositeSkipCountLastSecond = _lastState.CompositeSkipCountLastSecond,
                FirstAgentX = _lastState.FirstAgentX,
                FirstAgentZ = _lastState.FirstAgentZ,
            };
        }
        else
        {
            _lastPerfCaptureTicks = nowTicks;
        }

        if (!_lastState.Equals(next))
        {
            _lastState = next;
            page.SetState(_ => next);
            root.IsDirty = true;
            changed = true;
        }

        return changed;
    }

    private ReactivePage<MassNavPanelState> EnsurePage(GameEngine engine)
    {
        if (_page != null)
        {
            return _page;
        }

        var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
        var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
        _page = new ReactivePage<MassNavPanelState>(textMeasurer, imageSizeProvider, MassNavPanelState.Empty, BuildRoot);
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
        _lastState = MassNavPanelState.Empty;
        _lastPerfCaptureTicks = 0;
        _lastActionText = "MassNav runtime knobs hot-apply now. Physics/Nav buttons only touch engine policies. Flow is config-only here. Agent count and Reset rebuild the scene.";
        _page?.SetState(_ => MassNavPanelState.Empty);
    }

    private UiElementBuilder BuildRoot(ReactiveContext<MassNavPanelState> context)
    {
        var state = context.State;
        if (!state.Visible)
        {
            return Ui.Card(Ui.Text("Mass Nav Web Parity").FontSize(20f).Bold().Color("#F8FBFF"))
                .Width(420f)
                .Padding(14f)
                .Radius(18f)
                .Background("#111C2A")
                .Absolute(16f, 16f)
                .ZIndex(20);
        }

        return Ui.Card(
                Ui.Text("Mass Nav Web Parity").FontSize(20f).Bold().Color("#F8FBFF"),
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
                Ui.Text("Web parity baseline: SoA sim owns the hot path. Ludots owns selection, command ingress, presentation, and diagnostics.").FontSize(12f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Teams {state.TeamCount}  Agents/team {state.AgentsPerTeam}  Total {state.TotalAgents}  Selectable {state.ControllableAgents}  Obstacles {state.Blockers}").FontSize(13f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Selected {state.SelectedCount}  Rev {state.SelectionRevision}  Pending cmds {state.PendingCommandCount}").FontSize(13f).Color("#A4F07A"),
                Ui.Text($"Team target {state.SelectedTeamId}  Formation {state.FormationLabel}  Groups {state.FormationCount}  Rotation {state.FormationRotationDeg:0.0} deg").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Render FPS {state.RenderFps:0}  Frame {state.RenderFrameMs:0.0} ms  Primitive {state.PrimitiveRenderMs:0.0} ms").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"InputCollection: select {state.SelectionSyncHzObserved:0.0} Hz  control {state.ControlHzObserved:0.0} Hz  capture {state.CommandHzObserved:0.0} Hz").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"PostMovement: command {state.CommandDispatchHzObserved:0.0} Hz  sim {state.SimHzObserved:0.0} Hz  Presentation: primitive {state.PrimitiveHzObserved:0.0} Hz  hud {state.HudHzObserved:0.0} Hz  panel {state.PanelHzObserved:0.0} Hz").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.LastActionText).FontSize(12f).Color("#8FE388").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("MassNav Runtime").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Immediate knobs: Logic {state.LogicHz} Hz  Budget {state.SimulationBudgetMs} ms  Slice {state.SimulationSliceLimit}  Arrival {(state.ArrivalFallbackEnabled ? "On" : "Off")}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton("-1 ms", () => AdjustSimulationBudget(-1)),
                        BuildActionButton("+1 ms", () => AdjustSimulationBudget(1)),
                        BuildActionButton("-30 slice", () => AdjustSimulationSlices(-30)),
                        BuildActionButton("+30 slice", () => AdjustSimulationSlices(30)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton(state.ArrivalFallbackEnabled ? "Arrival On" : "Arrival Off", ToggleArrivalFallback),
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
                Ui.Text("LogicHz follows Engine/clock.json and drives the custom mass-nav sim. Physics/Nav buttons are honest engine-policy hot apply, but this playground's crowd sim does not consume them yet.").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
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
                Ui.Text("Config Snapshot Only").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Flow {(state.FlowEnabled ? "On" : "Off")}  Iter {state.FlowIterations}  Step {state.FlowStepInterval}  Crowd {state.FlowCrowdInterval}  Obs {state.FlowObstacleInterval}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Flow rebuild(last) {state.FlowFieldRebuildMs:0.0} ms  target-driven  rebuild/frame {state.FlowReconcileFrame}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Flow knobs stay visible for parity bookkeeping, but they are intentionally read-only here until the custom sim actually consumes them.").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
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
                Ui.Text("Formation").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                        BuildActionButton("None", () => SetFormationMode(MassNavFormationMode.None)),
                        BuildActionButton("Line", () => SetFormationMode(MassNavFormationMode.Line)),
                        BuildActionButton("Square", () => SetFormationMode(MassNavFormationMode.Square)),
                        BuildActionButton("Circle", () => SetFormationMode(MassNavFormationMode.Circle)),
                        BuildActionButton("Wedge", () => SetFormationMode(MassNavFormationMode.Wedge)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Diagnostics").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Select {state.SelectionSyncMs:0.0} ms  Command {state.CommandApplyMs:0.0} ms  Group {state.FormationTargetMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Prep {state.StepPrepMs:0.0} ms  Steer {state.LocalSteeringMs:0.0} ms  Resolve {state.HardResolveMs:0.0} ms  Sim {state.SimStepMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Entity sync {state.EntitySyncMs:0.0} ms  Primitive emit {state.PrimitiveEmitMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Primitive buffer {state.PrimitiveBufferCount}  instances {state.PrimitiveInstances}  batches {state.PrimitiveBatches}  dropped {state.PrimitiveDropped}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Crowd in view {state.CrowdInViewEntities}  submitted {state.CrowdSubmittedEntities}  obs {state.SubmittedObstacles}  ECS visible {state.EcsVisibleEntities}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Cull {state.CameraCullingMs:0.0} ms  Presenter {state.CameraPresenterMs:0.0} ms  HUD proj {state.WorldHudProjectionMs:0.0} ms").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"UI in {state.UiInputMs:0.0} ms  UI render {state.UiRenderMs:0.0} ms  upload {state.UiUploadMs:0.0} ms  overlay {state.ScreenOverlayBuildMs:0.0}/{state.ScreenOverlayDrawMs:0.0} ms").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Render accounted {state.RenderAccountedMs:0.0} ms  leftover {state.RenderUnaccountedMs:0.0} ms  composite skip {state.CompositeSkipCountLastSecond}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Camera target ({state.CameraTargetX:0.0}, {state.CameraTargetY:0.0}) cm  distance {state.CameraDistanceCm:0.0} cm").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Selection snapshots/frame {state.SelectionSnapshotsFrame}  commands/frame {state.CommandCountFrame}").FontSize(12f).Color("#DFAF6C"),
                Ui.Text($"Structural changes/frame {state.StructuralChangesFrame}").FontSize(12f).Color("#F18C7F"),
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

    private void RequestSceneReset()
    {
        _simulation?.RequestSceneReset();
        SetActionFeedback("Queued reset: scene rebuild requested.");
    }

    private void RequestCameraReset()
    {
        if (_engine == null || !MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        MassNavWebParityRuntime.RequestTacticalCameraReset(_engine);
        SetActionFeedback("Hot apply: camera reset requested.");
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

    private void SetFormationMode(MassNavFormationMode mode)
    {
        _simulation?.SetFormationMode(mode);
        SetActionFeedback($"Hot apply: formation = {mode}.");
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
        SetActionFeedback($"Engine policy hot apply: physics = {policy.TargetHz} Hz. Current mass-nav custom sim still runs on LogicHz/InputCollection.");
    }

    private void AdjustPhysicsMaxSteps(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Physics2DTickPolicy) is not Physics2DTickPolicy policy)
        {
            return;
        }

        policy.SetMaxStepsPerFixedTick(Math.Clamp(policy.MaxStepsPerFixedTick + delta, 1, 32));
        SetActionFeedback($"Engine policy hot apply: physics max steps = {policy.MaxStepsPerFixedTick}. Current mass-nav custom sim is not consuming Physics2D ticks.");
    }

    private void AdjustNavigationHz(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Navigation2DTickPolicy) is not Navigation2DTickPolicy policy)
        {
            return;
        }

        policy.SetTargetHz(Math.Clamp(policy.TargetHz + delta, 0, 240));
        SetActionFeedback($"Engine policy hot apply: navigation = {policy.TargetHz} Hz. Current mass-nav custom sim still runs on LogicHz/InputCollection.");
    }

    private void AdjustNavigationMaxSteps(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Navigation2DTickPolicy) is not Navigation2DTickPolicy policy)
        {
            return;
        }

        policy.SetMaxStepsPerFixedTick(Math.Clamp(policy.MaxStepsPerFixedTick + delta, 1, 32));
        SetActionFeedback($"Engine policy hot apply: navigation max steps = {policy.MaxStepsPerFixedTick}. Current mass-nav custom sim is not consuming Navigation2D ticks.");
    }

    private void ToggleArrivalFallback()
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.WebParity.ArrivalTuning.Enabled = !_simulation.WebParity.ArrivalTuning.Enabled;
        SetActionFeedback($"Hot apply: arrival fallback = {(_simulation.WebParity.ArrivalTuning.Enabled ? "On" : "Off")}.");
    }

    private void AdjustArrivalTimeoutMs(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.WebParity.ArrivalTuning.AdjustTimeoutMs(delta);
        SetActionFeedback($"Hot apply: arrival timeout = {_simulation.WebParity.ArrivalTuning.TimeoutMs} ms.");
    }

    private void AdjustArrivalProgressCm(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.WebParity.ArrivalTuning.AdjustProgressDistanceCm(delta);
        SetActionFeedback($"Hot apply: arrival progress = {_simulation.WebParity.ArrivalTuning.ProgressDistanceCm} cm.");
    }

    private void AdjustArrivalWakePushCm(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.WebParity.ArrivalTuning.AdjustWakePushDistanceCm(delta);
        SetActionFeedback($"Hot apply: arrival wake push = {_simulation.WebParity.ArrivalTuning.WakePushDistanceCm} cm.");
    }

    private void AdjustArrivalMaxRetries(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.WebParity.ArrivalTuning.AdjustMaxRetryCount(delta);
        SetActionFeedback($"Hot apply: arrival max retries = {_simulation.WebParity.ArrivalTuning.MaxRetryCount}.");
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

        MassNavPanelState next = CaptureState(_engine, _simulation);
        _lastState = next;
        _page.SetState(_ => next);
        if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            root.IsDirty = true;
        }
    }

    private MassNavPanelState CaptureState(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        bool visible = engine.CurrentMapSession != null && MassNavWebParityIds.IsPlaygroundMap(engine.CurrentMapSession.MapId.Value);
        PresentationTimingDiagnostics? timing = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
        PrimitiveDrawBuffer? primitiveBuffer = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
        int primitiveBufferCount = primitiveBuffer?.Count ?? 0;
        int primitiveDropped = primitiveBuffer?.DroppedSinceClear ?? 0;
        int ecsVisibleEntities = (engine.GetService(CoreServiceKeys.CameraCullingDebugState) as CameraCullingDebugState)?.VisibleEntityCount
            ?? timing?.VisibleEntitiesLastFrame
            ?? 0;
        float renderFrameMs = MathF.Round(timing?.RenderFrameMs ?? 0f, 1);
        float primitiveRenderMs = MathF.Round(timing?.PrimitiveRenderMs ?? 0f, 1);
        float uiInputMs = MathF.Round(timing?.UiInputMs ?? 0f, 1);
        float uiRenderMs = MathF.Round(timing?.UiRenderMs ?? 0f, 1);
        float uiUploadMs = MathF.Round(timing?.UiUploadMs ?? 0f, 1);
        float screenOverlayBuildMs = MathF.Round(timing?.ScreenOverlayBuildMs ?? 0f, 1);
        float screenOverlayDrawMs = MathF.Round(timing?.ScreenOverlayDrawMs ?? 0f, 1);
        float cameraCullingMs = MathF.Round(timing?.CameraCullingMs ?? 0f, 1);
        float cameraPresenterMs = MathF.Round(timing?.CameraPresenterMs ?? 0f, 1);
        float worldHudProjectionMs = MathF.Round(timing?.WorldHudProjectionMs ?? 0f, 1);
        float renderAccountedMs = MathF.Round(
            primitiveRenderMs +
            uiInputMs +
            uiRenderMs +
            uiUploadMs +
            screenOverlayBuildMs +
            screenOverlayDrawMs +
            cameraCullingMs +
            cameraPresenterMs +
            worldHudProjectionMs,
            1);
        float renderUnaccountedMs = MathF.Max(0f, MathF.Round(renderFrameMs - renderAccountedMs, 1));
        float firstAgentX = 0f;
        float firstAgentZ = 0f;
        if (simulation.AgentState.ControllableCount > 0)
        {
            var first = simulation.AgentState.ControllableAgents[0];
            if (engine.World.IsAlive(first) && engine.World.TryGet(first, out VisualTransform transform))
            {
                firstAgentX = transform.Position.X;
                firstAgentZ = transform.Position.Z;
            }
        }

        var camera = engine.GameSession.Camera.State;
        int logicHz = Time.FixedDeltaTime > 0.000001f ? (int)MathF.Round(1f / Time.FixedDeltaTime) : 0;
        var physicsPolicy = engine.GetService(CoreServiceKeys.Physics2DTickPolicy);
        var navigationPolicy = engine.GetService(CoreServiceKeys.Navigation2DTickPolicy);
        string obstacleSemanticsText = $"Obstacle semantics: visible radius = authored obstacle. hard block = visible + body {simulation.WebParity.Semantics.Obstacle.AgentBodyRadiusCm:0} cm. soft push = visible + {simulation.WebParity.Semantics.Obstacle.SoftPushPaddingCm:0} cm.";
        string targetSemanticsText = $"Target semantics: team target clear {simulation.WebParity.Semantics.TargetProjection.TeamTargetClearanceCm:0} cm. group center clear {simulation.WebParity.Semantics.TargetProjection.GroupCenterClearanceCm:0} cm. team slot clear {simulation.WebParity.Semantics.TargetProjection.TeamSlotClearanceCm:0} cm. loose/group slot clear {simulation.WebParity.Semantics.TargetProjection.LooseTargetClearanceCm:0}/{simulation.WebParity.Semantics.TargetProjection.GroupSlotClearanceCm:0} cm.";
        string arrivalSemanticsText = $"Arrival semantics: stop threshold {simulation.WebParity.Semantics.Group.UnitTargetStopThresholdCm:0} cm. settle timeout/progress/wake/retry = {simulation.WebParity.ArrivalTuning.TimeoutMs}/{simulation.WebParity.ArrivalTuning.ProgressDistanceCm}/{simulation.WebParity.ArrivalTuning.WakePushDistanceCm}/{simulation.WebParity.ArrivalTuning.MaxRetryCount}. flow slow radius loose/group = {simulation.WebParity.Semantics.Steering.GoalArrivalRadiusCm:0}/{simulation.WebParity.Semantics.Group.FormationFlowSlowRadiusCm:0} cm.";
        string yieldSemanticsText = $"Yield semantics: nav mass light/heavy = {simulation.WebParity.AvoidanceTuning.LightNavMass:0.##}/{simulation.WebParity.AvoidanceTuning.HeavyNavMass:0.##}. dominant ratio {simulation.WebParity.AvoidanceTuning.DominantMassRatio:0.##}. response friendly/non-friendly/push = {simulation.WebParity.AvoidanceTuning.FriendlyResponseScale:0.##}/{simulation.WebParity.AvoidanceTuning.NonFriendlyResponseScale:0.##}/{simulation.WebParity.AvoidanceTuning.DominantPushResponseScale:0.##}.";
        return new MassNavPanelState(
            Visible: visible,
            LastActionText: _lastActionText,
            LogicHz: logicHz,
            SimulationBudgetMs: engine.SimulationBudgetMsPerFrame,
            SimulationSliceLimit: engine.SimulationMaxSlicesPerLogicFrame,
            PhysicsHz: physicsPolicy?.TargetHz ?? 0,
            PhysicsMaxStepsPerFixedTick: physicsPolicy?.MaxStepsPerFixedTick ?? 0,
            NavigationHz: navigationPolicy?.TargetHz ?? 0,
            NavigationMaxStepsPerFixedTick: navigationPolicy?.MaxStepsPerFixedTick ?? 0,
            TeamCount: simulation.TeamCount,
            AgentsPerTeam: simulation.AgentsPerTeam,
            TotalAgents: simulation.AgentState.TotalAgents,
            ControllableAgents: simulation.AgentState.ControllableCount,
            Blockers: simulation.AgentState.BlockerCount,
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
            FlowStepInterval: simulation.FlowTuning.StepIntervalTicks,
            FlowCrowdInterval: simulation.FlowTuning.CrowdStampIntervalTicks,
            FlowObstacleInterval: simulation.FlowTuning.ObstacleStampIntervalTicks,
            ArrivalFallbackEnabled: simulation.WebParity.ArrivalTuning.Enabled,
            ArrivalTimeoutMs: simulation.WebParity.ArrivalTuning.TimeoutMs,
            ArrivalProgressCm: simulation.WebParity.ArrivalTuning.ProgressDistanceCm,
            ArrivalWakePushCm: simulation.WebParity.ArrivalTuning.WakePushDistanceCm,
            ArrivalMaxRetries: simulation.WebParity.ArrivalTuning.MaxRetryCount,
            ArrivalSettledUnits: simulation.WebParity.SettledUnitCount,
            RenderFps: MathF.Round(timing?.RenderFps ?? 0f),
            RenderFrameMs: renderFrameMs,
            PrimitiveRenderMs: primitiveRenderMs,
            SelectionSyncMs: MathF.Round(simulation.SelectionSyncMs, 1),
            CommandApplyMs: MathF.Round(simulation.CommandApplyMs, 1),
            FormationTargetMs: MathF.Round(simulation.FormationTargetMs, 1),
            FlowFieldRebuildMs: MathF.Round(simulation.FlowFieldRebuildMs > 0.001f ? simulation.FlowFieldRebuildMs : simulation.WebParity.LastFlowFieldRebuildMs, 1),
            StepPrepMs: MathF.Round(simulation.StepPrepMs, 1),
            LocalSteeringMs: MathF.Round(simulation.LocalSteeringMs, 1),
            SimStepMs: MathF.Round(simulation.SimStepMs, 1),
            HardResolveMs: MathF.Round(simulation.HardResolveMs, 1),
            EntitySyncMs: MathF.Round(simulation.EntitySyncMs, 1),
            PrimitiveEmitMs: MathF.Round(simulation.PrimitiveEmitMs, 1),
            SelectionSyncHzObserved: MathF.Round(simulation.SelectionSyncHzObserved, 1),
            ControlHzObserved: MathF.Round(simulation.ControlHzObserved, 1),
            CommandHzObserved: MathF.Round(simulation.CommandHzObserved, 1),
            CommandDispatchHzObserved: MathF.Round(simulation.CommandDispatchHzObserved, 1),
            SimHzObserved: MathF.Round(simulation.SimHzObserved, 1),
            PrimitiveHzObserved: MathF.Round(simulation.PrimitiveHzObserved, 1),
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
            PrimitiveBufferCount: primitiveBufferCount,
            PrimitiveInstances: timing?.PrimitiveInstancesLastFrame ?? 0,
            PrimitiveBatches: timing?.PrimitiveBatchesLastFrame ?? 0,
            PrimitiveDropped: primitiveDropped,
            EcsVisibleEntities: ecsVisibleEntities,
            CrowdInViewEntities: simulation.CrowdInViewCount,
            CrowdSubmittedEntities: simulation.CrowdSubmittedCount,
            SubmittedObstacles: simulation.ObstacleSubmittedCount,
            CompositeSkipCountLastSecond: timing?.CompositeSkipCountLastSecond ?? 0,
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
}
