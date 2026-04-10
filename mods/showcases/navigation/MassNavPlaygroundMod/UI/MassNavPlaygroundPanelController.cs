using System;
using Ludots.Core.Engine;
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

    public bool MountOrSync(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return false;
        }

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

    private static UiElementBuilder BuildRoot(ReactiveContext<MassNavPanelState> context)
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
                Ui.Text("New facade path: selection input -> revision cache -> group move bridge -> nav goals.").FontSize(12f).Color("#C7D4E5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Agents {state.TotalAgents}  Controllable {state.ControllableAgents}  Blockers {state.Blockers}").FontSize(13f).Color("#C7D4E5"),
                Ui.Text($"Selected {state.SelectedCount}  Rev {state.SelectionRevision}").FontSize(13f).Color("#A4F07A"),
                Ui.Text($"Render FPS {state.RenderFps:0.0}  Frame {state.RenderFrameMs:0.00} ms  Primitive {state.PrimitiveRenderMs:0.00} ms").FontSize(13f).Color("#F2D483").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Primitive buffer {state.PrimitiveBufferCount}  instances {state.PrimitiveInstances}  batches {state.PrimitiveBatches}  visible {state.VisibleEntities}").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Camera target ({state.CameraTargetX:0.0}, {state.CameraTargetY:0.0}) cm  distance {state.CameraDistanceCm:0.0} cm").FontSize(12f).Color("#D6E4F5").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"First agent ({state.FirstAgentX:0.0}, {state.FirstAgentZ:0.0}) m").FontSize(12f).Color("#D6E4F5"),
                Ui.Text($"Selection snapshots/frame {state.SelectionSnapshotsFrame}").FontSize(12f).Color("#DFAF6C"),
                Ui.Text($"Structural changes/frame {state.StructuralChangesFrame}").FontSize(12f).Color("#F18C7F"),
                Ui.Text($"Flow reconcile/frame {state.FlowReconcileFrame}").FontSize(12f).Color("#F18C7F"),
                Ui.Text("Use Ludots selection box to select green units, then right click to move.").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal))
            .Width(420f)
            .Padding(14f)
            .Gap(8f)
            .Radius(18f)
            .Background("#111C2A")
            .Absolute(16f, 16f)
            .ZIndex(20);
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
            TotalAgents: simulation.AgentState.TotalAgents,
            ControllableAgents: simulation.AgentState.ControllableCount,
            Blockers: simulation.AgentState.BlockerCount,
            SelectedCount: simulation.SelectedCount,
            SelectionRevision: simulation.SelectionRevision,
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
