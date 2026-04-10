using System;
using Ludots.Core.Engine;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod.UI;

internal sealed class MassNavPlaygroundPanelController
{
    private ReactivePage<MassNavPanelState>? _page;
    private MassNavPanelState _lastState = MassNavPanelState.Empty;
    private GameEngine? _engine;
    private MassNavSimulationRuntime? _simulation;

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
            return Ui.Card(Ui.Text("Mass Nav Playground").FontSize(20f).Bold().Color("#F8FBFF"))
                .Width(420f)
                .Padding(14f)
                .Radius(18f)
                .Background("#111C2A")
                .Absolute(16f, 16f)
                .ZIndex(20);
        }

        return Ui.Card(
                Ui.Text("Mass Nav Playground").FontSize(20f).Bold().Color("#F8FBFF"),
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
                Ui.Text("New facade path: Ludots selection -> persistent square formation runtime -> nav goals -> presentation primitives.").FontSize(12f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Agents/team {state.AgentsPerTeam}  Total {state.TotalAgents}  Controllable {state.ControllableAgents}  Blockers {state.Blockers}").FontSize(13f).Color("#C7D4E5"),
                Ui.Text($"Selected {state.SelectedCount}  Rev {state.SelectionRevision}").FontSize(13f).Color("#A4F07A"),
                Ui.Text($"Formations {state.FormationCount}  Rotation {state.FormationRotationDeg:0.0} deg  Hold Q/E to rotate").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Render FPS {state.RenderFps:0.0}  Frame {state.RenderFrameMs:0.00} ms  Primitive {state.PrimitiveRenderMs:0.00} ms").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Primitive buffer {state.PrimitiveBufferCount}  instances {state.PrimitiveInstances}  batches {state.PrimitiveBatches}  visible {state.VisibleEntities}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Camera target ({state.CameraTargetX:0.0}, {state.CameraTargetY:0.0}) cm  distance {state.CameraDistanceCm:0.0} cm").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"First agent ({state.FirstAgentX:0.0}, {state.FirstAgentZ:0.0}) m").FontSize(12f).Color("#D6E4F5"),
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
                Ui.Text("Flow").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text($"Enabled {state.FlowEnabled}  Iter {state.FlowIterations}  Step {state.FlowStepInterval}  Crowd {state.FlowCrowdInterval}  Obstacle {state.FlowObstacleInterval}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildActionButton(state.FlowEnabled ? "Flow On" : "Flow Off", ToggleFlowEnabled),
                        BuildActionButton("Iter -", () => AdjustFlowIterations(-512)),
                        BuildActionButton("Iter +", () => AdjustFlowIterations(512)),
                        BuildActionButton("Step -", () => AdjustFlowStep(-1)),
                        BuildActionButton("Step +", () => AdjustFlowStep(1)))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        BuildActionButton("Crowd -", () => AdjustFlowCrowdInterval(-1)),
                        BuildActionButton("Crowd +", () => AdjustFlowCrowdInterval(1)),
                        BuildActionButton("Obstacle -", () => AdjustFlowObstacleInterval(-1)),
                        BuildActionButton("Obstacle +", () => AdjustFlowObstacleInterval(1)))
                    .Wrap()
                    .Gap(8f),
                Ui.Text($"Selection snapshots/frame {state.SelectionSnapshotsFrame}").FontSize(12f).Color("#DFAF6C"),
                Ui.Text($"Structural changes/frame {state.StructuralChangesFrame}").FontSize(12f).Color("#F18C7F"),
                Ui.Text($"Flow reconcile/frame {state.FlowReconcileFrame}").FontSize(12f).Color("#F18C7F"),
                Ui.Text("Use Ludots box selection to grab green units, right click to place a square formation, hold Q/E to rotate, R to hard reset the scene.").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal))
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
        if (_engine == null || !MassNavPlaygroundIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        MassNavPlaygroundRuntime.RequestTacticalCameraReset(_engine);
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

    private void ToggleFlowEnabled()
    {
        if (_simulation == null)
        {
            return;
        }

        _simulation.FlowTuning.Enabled = !_simulation.FlowTuning.Enabled;
    }

    private void AdjustFlowIterations(int delta)
    {
        _simulation?.FlowTuning.AdjustIterations(delta);
    }

    private void AdjustFlowStep(int delta)
    {
        _simulation?.FlowTuning.AdjustStepInterval(delta);
    }

    private void AdjustFlowCrowdInterval(int delta)
    {
        _simulation?.FlowTuning.AdjustCrowdStampInterval(delta);
    }

    private void AdjustFlowObstacleInterval(int delta)
    {
        _simulation?.FlowTuning.AdjustObstacleStampInterval(delta);
    }

    private static int ResolveMaxAgentsPerTeam(GameEngine engine)
    {
        Navigation2DRuntime? runtime = engine.GetService(CoreServiceKeys.Navigation2DRuntime);
        int maxAgents = runtime?.Config.MaxAgents ?? 40_000;
        return Math.Max(0, maxAgents / 2);
    }

    private static MassNavPanelState CaptureState(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        bool visible = engine.CurrentMapSession != null && MassNavPlaygroundIds.IsPlaygroundMap(engine.CurrentMapSession.MapId.Value);
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
            SelectedCount: simulation.SelectedCount,
            SelectionRevision: simulation.SelectionRevision,
            FormationCount: simulation.FormationRuntime.ActiveGroupCount,
            FormationRotationDeg: simulation.FormationRuntime.SelectedRotationRadians * (180f / MathF.PI),
            FlowEnabled: simulation.FlowTuning.Enabled,
            FlowIterations: simulation.FlowTuning.IterationsPerStep,
            FlowStepInterval: simulation.FlowTuning.StepIntervalTicks,
            FlowCrowdInterval: simulation.FlowTuning.CrowdStampIntervalTicks,
            FlowObstacleInterval: simulation.FlowTuning.ObstacleStampIntervalTicks,
            RenderFps: timing?.RenderFps ?? 0f,
            RenderFrameMs: timing?.RenderFrameMs ?? 0f,
            PrimitiveRenderMs: timing?.PrimitiveRenderMs ?? 0f,
            PrimitiveBufferCount: primitiveBufferCount,
            PrimitiveInstances: timing?.PrimitiveInstancesLastFrame ?? 0,
            PrimitiveBatches: timing?.PrimitiveBatchesLastFrame ?? 0,
            VisibleEntities: timing?.VisibleEntitiesLastFrame ?? 0,
            CameraTargetX: camera.TargetCm.X,
            CameraTargetY: camera.TargetCm.Y,
            CameraDistanceCm: camera.DistanceCm,
            FirstAgentX: firstAgentX,
            FirstAgentZ: firstAgentZ,
            SelectionSnapshotsFrame: simulation.SelectionSnapshotCountFrame,
            StructuralChangesFrame: simulation.StructuralChangesFrame,
            FlowReconcileFrame: simulation.FlowReconcileCountFrame);
    }
}
