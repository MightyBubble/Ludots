using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

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
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        _simulation.ObserveHudTick();

        if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
        {
            return;
        }

        PresentationTimingDiagnostics? timing = _engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
        int primitiveDropped = (_engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) as PrimitiveDrawBuffer)?.DroppedSinceClear ?? 0;
        int ecsVisible = (_engine.GetService(CoreServiceKeys.CameraCullingDebugState) as CameraCullingDebugState)?.VisibleEntityCount
            ?? timing?.VisibleEntitiesLastFrame
            ?? 0;
        Vector2 resolution = _engine.GetService(CoreServiceKeys.ViewController) is IViewController viewport
            ? viewport.Resolution
            : new Vector2(1280f, 720f);
        int left = Math.Max(16, (int)resolution.X - 260);
        overlay.AddText(left, 16, $"fps {timing?.RenderFps ?? 0f:0.0}", 20, new Vector4(0.92f, 0.96f, 1f, 1f), stableId: 1, dirtySerial: (int)MathF.Round(timing?.RenderFps ?? 0f));
        overlay.AddText(left, 42, $"frame {timing?.RenderFrameMs ?? 0f:0.0} ms", 16, new Vector4(0.74f, 0.82f, 0.92f, 1f), stableId: 2, dirtySerial: (int)MathF.Round(timing?.RenderFrameMs ?? 0f));
        overlay.AddText(left, 64, $"selected {_simulation.SelectedCount}", 16, new Vector4(0.56f, 0.96f, 0.48f, 1f), stableId: 3, dirtySerial: _simulation.SelectedCount);
        overlay.AddText(left, 86, $"agents {_simulation.AgentState.TotalAgents} groups {_simulation.NavGroupRuntime.ActiveGroupCount}", 16, new Vector4(1f, 0.82f, 0.45f, 1f), stableId: 4, dirtySerial: _simulation.AgentState.TotalAgents + (_simulation.NavGroupRuntime.ActiveGroupCount << 20));
        overlay.AddText(left, 108, $"primitive {timing?.PrimitiveRenderMs ?? 0f:0.0} ms", 16, new Vector4(1f, 0.56f, 0.46f, 1f), stableId: 5, dirtySerial: (int)MathF.Round((timing?.PrimitiveRenderMs ?? 0f) * 10f));
        overlay.AddText(left, 130, $"instances {timing?.PrimitiveInstancesLastFrame ?? 0} batches {timing?.PrimitiveBatchesLastFrame ?? 0} drop {primitiveDropped}", 16, new Vector4(1f, 0.56f, 0.46f, 1f), stableId: 6, dirtySerial: (timing?.PrimitiveInstancesLastFrame ?? 0) ^ ((timing?.PrimitiveBatchesLastFrame ?? 0) << 1) ^ (primitiveDropped << 2));
        overlay.AddText(left, 152, $"prep {_simulation.StepPrepMs:0.0} steer {_simulation.LocalSteeringMs:0.0}", 16, new Vector4(1f, 0.56f, 0.46f, 1f), stableId: 7, dirtySerial: ((int)MathF.Round(_simulation.StepPrepMs * 10f) << 16) ^ (int)MathF.Round(_simulation.LocalSteeringMs * 10f));
        overlay.AddText(left, 174, $"resolve {_simulation.HardResolveMs:0.0} sync {_simulation.EntitySyncMs:0.0}", 16, new Vector4(1f, 0.56f, 0.46f, 1f), stableId: 8, dirtySerial: ((int)MathF.Round(_simulation.HardResolveMs * 10f) << 16) ^ (int)MathF.Round(_simulation.EntitySyncMs * 10f));
        overlay.AddText(left, 196, $"crowd {_simulation.CrowdInViewCount}/{_simulation.CrowdSubmittedCount} ecs {ecsVisible}", 16, new Vector4(1f, 0.56f, 0.46f, 1f), stableId: 9, dirtySerial: _simulation.CrowdInViewCount ^ (_simulation.CrowdSubmittedCount << 1) ^ (ecsVisible << 2));
    }
}
