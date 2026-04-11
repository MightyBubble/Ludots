using System;
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
    private readonly CachedHudLine[] _cachedLines = CreateHudLineCache();

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
        AddCachedText(overlay, 0, left, 16, 20, new Vector4(0.92f, 0.96f, 1f, 1f), (int)MathF.Round((timing?.RenderFps ?? 0f) * 10f), () => $"fps {timing?.RenderFps ?? 0f:0.0}");
        AddCachedText(overlay, 1, left, 42, 16, new Vector4(0.74f, 0.82f, 0.92f, 1f), (int)MathF.Round((timing?.RenderFrameMs ?? 0f) * 10f), () => $"frame {timing?.RenderFrameMs ?? 0f:0.0} ms");
        AddCachedText(overlay, 2, left, 64, 16, new Vector4(0.56f, 0.96f, 0.48f, 1f), _simulation.SelectedCount, () => $"selected {_simulation.SelectedCount}");
        AddCachedText(overlay, 3, left, 86, 16, new Vector4(1f, 0.82f, 0.45f, 1f), _simulation.AgentState.TotalAgents + (_simulation.NavGroupRuntime.ActiveGroupCount << 20), () => $"agents {_simulation.AgentState.TotalAgents} groups {_simulation.NavGroupRuntime.ActiveGroupCount}");
        AddCachedText(overlay, 4, left, 108, 16, new Vector4(1f, 0.56f, 0.46f, 1f), (int)MathF.Round((timing?.PrimitiveRenderMs ?? 0f) * 10f), () => $"primitive {timing?.PrimitiveRenderMs ?? 0f:0.0} ms");
        AddCachedText(overlay, 5, left, 130, 16, new Vector4(1f, 0.56f, 0.46f, 1f), (timing?.PrimitiveInstancesLastFrame ?? 0) ^ ((timing?.PrimitiveBatchesLastFrame ?? 0) << 1) ^ (primitiveDropped << 2), () => $"instances {timing?.PrimitiveInstancesLastFrame ?? 0} batches {timing?.PrimitiveBatchesLastFrame ?? 0} drop {primitiveDropped}");
        AddCachedText(overlay, 6, left, 152, 16, new Vector4(1f, 0.56f, 0.46f, 1f), ((int)MathF.Round(_simulation.StepPrepMs * 10f) << 16) ^ (int)MathF.Round(_simulation.LocalSteeringMs * 10f), () => $"prep {_simulation.StepPrepMs:0.0} steer {_simulation.LocalSteeringMs:0.0}");
        AddCachedText(overlay, 7, left, 174, 16, new Vector4(1f, 0.56f, 0.46f, 1f), ((int)MathF.Round(_simulation.HardResolveMs * 10f) << 16) ^ (int)MathF.Round(_simulation.EntitySyncMs * 10f), () => $"resolve {_simulation.HardResolveMs:0.0} sync {_simulation.EntitySyncMs:0.0}");
        AddCachedText(overlay, 8, left, 196, 16, new Vector4(1f, 0.56f, 0.46f, 1f), _simulation.CrowdInViewCount ^ (_simulation.CrowdSubmittedCount << 1) ^ (ecsVisible << 2), () => $"crowd {_simulation.CrowdInViewCount}/{_simulation.CrowdSubmittedCount} ecs {ecsVisible}");
    }

    private void AddCachedText(ScreenOverlayBuffer overlay, int cacheIndex, int x, int y, int fontSize, Vector4 color, int dirtySerial, Func<string> factory)
    {
        ref CachedHudLine cache = ref _cachedLines[cacheIndex];
        if (cache.DirtySerial != dirtySerial || cache.Text == null)
        {
            cache.DirtySerial = dirtySerial;
            cache.Text = factory();
        }

        overlay.AddText(x, y, cache.Text, fontSize, color, stableId: cacheIndex + 1, dirtySerial);
    }

    private static CachedHudLine[] CreateHudLineCache()
    {
        var cache = new CachedHudLine[9];
        for (int i = 0; i < cache.Length; i++)
        {
            cache[i].DirtySerial = int.MinValue;
            cache[i].Text = string.Empty;
        }

        return cache;
    }

    private struct CachedHudLine
    {
        public int DirtySerial;
        public string? Text;
    }
}
