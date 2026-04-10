using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod.Systems;

internal sealed class MassNavHudPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavHudPresentationSystem(GameEngine engine, MassNavSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavPlaygroundIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
        {
            return;
        }

        PresentationTimingDiagnostics? timing = _engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
        overlay.AddText(1320, 16, $"fps {timing?.RenderFps ?? 0f:0.0}", 20, new Vector4(0.92f, 0.96f, 1f, 1f));
        overlay.AddText(1320, 42, $"frame {timing?.RenderFrameMs ?? 0f:0.00} ms", 16, new Vector4(0.74f, 0.82f, 0.92f, 1f));
        overlay.AddText(1320, 64, $"selected {_simulation.SelectedCount}", 16, new Vector4(0.56f, 0.96f, 0.48f, 1f));
        overlay.AddText(1320, 86, $"agents {_simulation.AgentState.TotalAgents} formations {_simulation.FormationRuntime.ActiveGroupCount}", 16, new Vector4(1f, 0.82f, 0.45f, 1f));
        overlay.AddText(1320, 108, $"flow iter {_simulation.FlowTuning.IterationsPerStep} step {_simulation.FlowTuning.StepIntervalTicks}", 16, new Vector4(1f, 0.56f, 0.46f, 1f));
        overlay.AddText(1320, 130, $"instances {timing?.PrimitiveInstancesLastFrame ?? 0} batches {timing?.PrimitiveBatchesLastFrame ?? 0}", 16, new Vector4(1f, 0.56f, 0.46f, 1f));
        overlay.AddText(1320, 152, $"visible {timing?.VisibleEntitiesLastFrame ?? 0}", 16, new Vector4(1f, 0.56f, 0.46f, 1f));
    }
}
