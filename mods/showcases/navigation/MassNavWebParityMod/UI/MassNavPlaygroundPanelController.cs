using System;
using System.Diagnostics;
using Ludots.Core.Engine;
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
                Ui.Text($"Agents/team {state.AgentsPerTeam}  Total {state.TotalAgents}  Selectable {state.ControllableAgents}  Obstacles {state.Blockers}").FontSize(13f).Color("#C7D4E5"),
                Ui.Text($"Selected {state.SelectedCount}  Rev {state.SelectionRevision}").FontSize(13f).Color("#A4F07A"),
                Ui.Text($"Team target {state.SelectedTeamId}  Formation {state.FormationLabel}  Groups {state.FormationCount}  Rotation {state.FormationRotationDeg:0.0} deg").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Render FPS {state.RenderFps:0}  Frame {state.RenderFrameMs:0.0} ms  Primitive {state.PrimitiveRenderMs:0.0} ms").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Select {state.SelectionSyncMs:0.0} ms  Formation {state.FormationTargetMs:0.0} ms  Sim {state.SimStepMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Entity sync {state.EntitySyncMs:0.0} ms  Primitive emit {state.PrimitiveEmitMs:0.0} ms").FontSize(12f).Color("#F18C7F").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Primitive buffer {state.PrimitiveBufferCount}  instances {state.PrimitiveInstances}  batches {state.PrimitiveBatches}  visible {state.VisibleEntities}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Camera target ({state.CameraTargetX:0.0}, {state.CameraTargetY:0.0}) cm  distance {state.CameraDistanceCm:0.0} cm").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
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
                Ui.Row(
                        BuildActionButton("Team 0", () => SetSelectedTeam(0)),
                        BuildActionButton("Team 1", () => SetSelectedTeam(1)))
                    .Wrap()
                    .Gap(8f),
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
            .Width(420f)
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
    }

    private void RequestCameraReset()
    {
        if (_engine == null || !MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        MassNavWebParityRuntime.RequestTacticalCameraReset(_engine);
    }

    private void AdjustTotalAgentsDown() => AdjustTotalAgents(-2_000);
    private void AdjustTotalAgentsUp() => AdjustTotalAgents(2_000);

    private void AdjustTotalAgents(int delta)
    {
        if (_simulation == null)
        {
            return;
        }

        int currentTotal = _simulation.AgentsPerTeam * 2;
        SetTotalAgents(currentTotal + delta);
    }

    private void SetTotalAgents(int totalAgents)
    {
        if (_engine == null || _simulation == null)
        {
            return;
        }

        int maxPerTeam = ResolveMaxAgentsPerTeam(_engine);
        int perTeam = Math.Clamp(Math.Max(0, totalAgents / 2), 0, maxPerTeam);
        _simulation.SetAgentsPerTeam(perTeam);
    }

    private void SetSelectedTeam(int teamId)
    {
        _simulation?.SetSelectedTeam(teamId);
    }

    private void SetFormationMode(MassNavFormationMode mode)
    {
        _simulation?.SetFormationMode(mode);
    }

    private static MassNavPanelState CaptureState(GameEngine engine, MassNavSimulationRuntime simulation)
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
        return new MassNavPanelState(
            Visible: visible,
            AgentsPerTeam: simulation.AgentsPerTeam,
            TotalAgents: simulation.AgentState.TotalAgents,
            ControllableAgents: simulation.AgentState.ControllableCount,
            Blockers: simulation.AgentState.BlockerCount,
            SelectedTeamId: simulation.SelectedTeamId,
            SelectedCount: simulation.SelectedCount,
            SelectionRevision: simulation.SelectionRevision,
            FormationCount: simulation.FormationRuntime.ActiveGroupCount,
            FormationLabel: simulation.FormationMode.ToString(),
            FormationRotationDeg: simulation.FormationRuntime.SelectedRotationRadians * (180f / MathF.PI),
            FlowEnabled: simulation.FlowTuning.Enabled,
            FlowIterations: simulation.FlowTuning.IterationsPerStep,
            FlowStepInterval: simulation.FlowTuning.StepIntervalTicks,
            FlowCrowdInterval: simulation.FlowTuning.CrowdStampIntervalTicks,
            FlowObstacleInterval: simulation.FlowTuning.ObstacleStampIntervalTicks,
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
}
