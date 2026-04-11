using System;
using System.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
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
    private string _lastActionText = "Budget/Physics/Nav/Flow/Arrival buttons hot-apply immediately. Agent count and Reset Scene rebuild the scene.";

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
                FormationTargetMs = _lastState.FormationTargetMs,
                SimStepMs = _lastState.SimStepMs,
                EntitySyncMs = _lastState.EntitySyncMs,
                PrimitiveEmitMs = _lastState.PrimitiveEmitMs,
                PrimitiveBufferCount = _lastState.PrimitiveBufferCount,
                PrimitiveInstances = _lastState.PrimitiveInstances,
                PrimitiveBatches = _lastState.PrimitiveBatches,
                VisibleEntities = _lastState.VisibleEntities,
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
        _lastActionText = "Budget/Physics/Nav/Flow/Arrival buttons hot-apply immediately. Agent count and Reset Scene rebuild the scene.";
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
                Ui.Text("Web parity baseline: SoA sim -> Ludots selection -> formation targets -> presentation primitives.").FontSize(12f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Teams {state.TeamCount}  Agents/team {state.AgentsPerTeam}  Total {state.TotalAgents}  Selectable {state.ControllableAgents}  Obstacles {state.Blockers}").FontSize(13f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Selected {state.SelectedCount}  Rev {state.SelectionRevision}").FontSize(13f).Color("#A4F07A"),
                Ui.Text($"Team target {state.SelectedTeamId}  Formation {state.FormationLabel}  Groups {state.FormationCount}  Rotation {state.FormationRotationDeg:0.0} deg").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Render FPS {state.RenderFps:0}  Frame {state.RenderFrameMs:0.0} ms  Primitive {state.PrimitiveRenderMs:0.0} ms").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Logic {state.LogicHz} Hz  Budget {state.SimulationBudgetMs} ms  Slice {state.SimulationSliceLimit}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Physics {state.PhysicsHz} Hz / max {state.PhysicsMaxStepsPerFixedTick}  Nav {state.NavigationHz} Hz / max {state.NavigationMaxStepsPerFixedTick}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Flow {(state.FlowEnabled ? "On" : "Off")}  Iter {state.FlowIterations}  Step {state.FlowStepInterval}  Crowd {state.FlowCrowdInterval}  Obs {state.FlowObstacleInterval}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Arrival {(state.ArrivalFallbackEnabled ? "On" : "Off")}  Timeout {state.ArrivalTimeoutMs} ms  Progress {state.ArrivalProgressCm} cm  Wake {state.ArrivalWakePushCm} cm  Retry {state.ArrivalMaxRetries}  Settled {state.ArrivalSettledUnits}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.LastActionText).FontSize(12f).Color("#8FE388").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Select {state.SelectionSyncMs:0.0} ms  Formation {state.FormationTargetMs:0.0} ms  Sim {state.SimStepMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Entity sync {state.EntitySyncMs:0.0} ms  Primitive emit {state.PrimitiveEmitMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Primitive buffer {state.PrimitiveBufferCount}  instances {state.PrimitiveInstances}  batches {state.PrimitiveBatches}  visible {state.VisibleEntities}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Camera target ({state.CameraTargetX:0.0}, {state.CameraTargetY:0.0}) cm  distance {state.CameraDistanceCm:0.0} cm").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Frame Budget").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                        BuildActionButton("-1 ms", () => AdjustSimulationBudget(-1)),
                        BuildActionButton("+1 ms", () => AdjustSimulationBudget(1)),
                        BuildActionButton("-30 slice", () => AdjustSimulationSlices(-30)),
                        BuildActionButton("+30 slice", () => AdjustSimulationSlices(30)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Tick Rates").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text("LogicHz follows Engine/clock.json and is read-only at runtime. Physics/Nav Hz below are hot-adjustable.").FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
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
                Ui.Text("Flow Budget").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                        BuildActionButton(state.FlowEnabled ? "Flow On" : "Flow Off", ToggleFlowEnabled),
                        BuildActionButton("Iter -512", () => AdjustFlowIterations(-512)),
                        BuildActionButton("Iter +512", () => AdjustFlowIterations(512)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Step -1", () => AdjustFlowStepInterval(-1)),
                        BuildActionButton("Step +1", () => AdjustFlowStepInterval(1)),
                        BuildActionButton("Crowd -1", () => AdjustFlowCrowdInterval(-1)),
                        BuildActionButton("Crowd +1", () => AdjustFlowCrowdInterval(1)),
                        BuildActionButton("Obs -1", () => AdjustFlowObstacleInterval(-1)),
                        BuildActionButton("Obs +1", () => AdjustFlowObstacleInterval(1)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text("Arrival Fallback").FontSize(12f).Bold().Color("#F4C77D"),
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
                Ui.Text("Scene Scale").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                        BuildActionButton("-2k", AdjustTotalAgentsDown),
                        BuildActionButton("5k", () => SetTotalAgents(5_000)),
                        BuildActionButton("10k", () => SetTotalAgents(10_000)),
                        BuildActionButton("20k", () => SetTotalAgents(20_000)),
                        BuildActionButton("40k", () => SetTotalAgents(40_000)),
                        BuildActionButton("+2k", AdjustTotalAgentsUp))
                    .Wrap()
                    .Gap(8f),
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
                Ui.Text($"Selection snapshots/frame {state.SelectionSnapshotsFrame}").FontSize(12f).Color("#DFAF6C"),
                Ui.Text($"Structural changes/frame {state.StructuralChangesFrame}").FontSize(12f).Color("#F18C7F"),
                Ui.Text($"Flow reconcile/frame {state.FlowReconcileFrame}").FontSize(12f).Color("#F18C7F"),
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
        SetActionFeedback($"Hot apply: physics = {policy.TargetHz} Hz.");
    }

    private void AdjustPhysicsMaxSteps(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Physics2DTickPolicy) is not Physics2DTickPolicy policy)
        {
            return;
        }

        policy.SetMaxStepsPerFixedTick(Math.Clamp(policy.MaxStepsPerFixedTick + delta, 1, 32));
        SetActionFeedback($"Hot apply: physics max steps = {policy.MaxStepsPerFixedTick}.");
    }

    private void AdjustNavigationHz(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Navigation2DTickPolicy) is not Navigation2DTickPolicy policy)
        {
            return;
        }

        policy.SetTargetHz(Math.Clamp(policy.TargetHz + delta, 0, 240));
        SetActionFeedback($"Hot apply: navigation = {policy.TargetHz} Hz.");
    }

    private void AdjustNavigationMaxSteps(int delta)
    {
        if (_engine?.GetService(CoreServiceKeys.Navigation2DTickPolicy) is not Navigation2DTickPolicy policy)
        {
            return;
        }

        policy.SetMaxStepsPerFixedTick(Math.Clamp(policy.MaxStepsPerFixedTick + delta, 1, 32));
        SetActionFeedback($"Hot apply: navigation max steps = {policy.MaxStepsPerFixedTick}.");
    }

    private void ToggleFlowEnabled()
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.FlowTuning.Enabled = !_simulation.FlowTuning.Enabled;
        SetActionFeedback($"Hot apply: flow = {(_simulation.FlowTuning.Enabled ? "On" : "Off")}.");
    }

    private void AdjustFlowIterations(int delta)
    {
        _simulation?.FlowTuning.AdjustIterations(delta);
        if (_simulation != null)
        {
            SetActionFeedback($"Hot apply: flow iterations = {_simulation.FlowTuning.IterationsPerStep}.");
        }
    }

    private void AdjustFlowStepInterval(int delta)
    {
        _simulation?.FlowTuning.AdjustStepInterval(delta);
        if (_simulation != null)
        {
            SetActionFeedback($"Hot apply: flow step interval = {_simulation.FlowTuning.StepIntervalTicks}.");
        }
    }

    private void AdjustFlowCrowdInterval(int delta)
    {
        _simulation?.FlowTuning.AdjustCrowdStampInterval(delta);
        if (_simulation != null)
        {
            SetActionFeedback($"Hot apply: flow crowd interval = {_simulation.FlowTuning.CrowdStampIntervalTicks}.");
        }
    }

    private void AdjustFlowObstacleInterval(int delta)
    {
        _simulation?.FlowTuning.AdjustObstacleStampInterval(delta);
        if (_simulation != null)
        {
            SetActionFeedback($"Hot apply: flow obstacle interval = {_simulation.FlowTuning.ObstacleStampIntervalTicks}.");
        }
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
        int primitiveBufferCount = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)?.Count ?? 0;
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
            RenderFrameMs: MathF.Round(timing?.RenderFrameMs ?? 0f, 1),
            PrimitiveRenderMs: MathF.Round(timing?.PrimitiveRenderMs ?? 0f, 1),
            SelectionSyncMs: MathF.Round(simulation.SelectionSyncMs, 1),
            FormationTargetMs: MathF.Round(simulation.FormationTargetMs, 1),
            SimStepMs: MathF.Round(simulation.SimStepMs, 1),
            EntitySyncMs: MathF.Round(simulation.EntitySyncMs, 1),
            PrimitiveEmitMs: MathF.Round(simulation.PrimitiveEmitMs, 1),
            PrimitiveBufferCount: primitiveBufferCount,
            PrimitiveInstances: timing?.PrimitiveInstancesLastFrame ?? 0,
            PrimitiveBatches: timing?.PrimitiveBatchesLastFrame ?? 0,
            VisibleEntities: timing?.VisibleEntitiesLastFrame ?? 0,
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
