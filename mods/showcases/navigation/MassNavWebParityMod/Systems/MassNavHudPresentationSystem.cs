using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
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

        ScreenOverlayBuffer overlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires ScreenOverlayBuffer for diagnostics HUD.");
        PresentationTimingDiagnostics timing = _engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires PresentationTimingDiagnostics for real FPS HUD.");
        IViewController viewport = _engine.GetService(CoreServiceKeys.ViewController)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires ViewController for diagnostics HUD layout.");
        int ecsVisible = (_engine.GetService(CoreServiceKeys.CameraCullingDebugState) as CameraCullingDebugState)?.VisibleEntityCount
            ?? timing.VisibleEntitiesLastFrame;
        Vector2 resolution = viewport.Resolution;
        int left = Math.Max(16, (int)resolution.X - 260);
        float frameMs = ResolveFrameMs(timing);
        float fps = frameMs > 0.001f ? 1000f / frameMs : 0f;
        AddCachedText(overlay, 0, left, 16, 20, new Vector4(0.92f, 0.96f, 1f, 1f), (int)MathF.Round(fps * 10f), () => $"fps {fps:0.0}");
        AddCachedText(overlay, 1, left, 42, 16, new Vector4(0.74f, 0.82f, 0.92f, 1f), (int)MathF.Round(frameMs * 10f), () => $"frame {frameMs:0.0} ms");
        AddCachedText(overlay, 2, left, 64, 16, new Vector4(0.56f, 0.96f, 0.48f, 1f), _simulation.SelectedCount, () => $"selected {_simulation.SelectedCount}");
        AddCachedText(overlay, 3, left, 86, 16, new Vector4(1f, 0.82f, 0.45f, 1f), _simulation.AgentState.TotalAgents + (_simulation.NavGroupRuntime.ActiveGroupCount << 20), () => $"agents {_simulation.AgentState.TotalAgents} groups {_simulation.NavGroupRuntime.ActiveGroupCount}");
        AddCachedText(overlay, 4, left, 108, 16, new Vector4(1f, 0.56f, 0.46f, 1f), (int)MathF.Round(timing.PerformerEmitMs * 10f), () => $"performer {timing.PerformerEmitMs:0.0} ms");
        AddCachedText(overlay, 5, left, 130, 16, new Vector4(1f, 0.56f, 0.46f, 1f), timing.PerformerMinimapMarkersLastFrame ^ (timing.MinimapScreenMarkersLastFrame << 1) ^ (timing.PerformerMinimapDroppedLastFrame << 2), () => $"mini markers {timing.PerformerMinimapMarkersLastFrame} screen {timing.MinimapScreenMarkersLastFrame} drop {timing.PerformerMinimapDroppedLastFrame}");
        AddCachedText(overlay, 6, left, 152, 16, new Vector4(1f, 0.56f, 0.46f, 1f), ((int)MathF.Round(_simulation.StepPrepMs * 10f) << 16) ^ (int)MathF.Round(_simulation.LocalSteeringMs * 10f), () => $"prep {_simulation.StepPrepMs:0.0} steer {_simulation.LocalSteeringMs:0.0}");
        AddCachedText(overlay, 7, left, 174, 16, new Vector4(1f, 0.56f, 0.46f, 1f), ((int)MathF.Round(_simulation.HardResolveMs * 10f) << 16) ^ (int)MathF.Round(_simulation.EntitySyncMs * 10f), () => $"resolve {_simulation.HardResolveMs:0.0} sync {_simulation.EntitySyncMs:0.0}");
        AddCachedText(overlay, 8, left, 196, 16, new Vector4(1f, 0.56f, 0.46f, 1f), _simulation.CrowdInViewCount ^ (_simulation.CrowdSubmittedCount << 1) ^ (ecsVisible << 2), () => $"crowd {_simulation.CrowdInViewCount}/{_simulation.CrowdSubmittedCount} ecs {ecsVisible}");
        AddCachedText(overlay, 9, left, 218, 16, new Vector4(0.62f, 0.85f, 1f, 1f), _simulation.LoadedChunkCount ^ (_simulation.StreamingChunkSizeCm << 8) ^ (_simulation.StreamingWindowUpdatesFrame << 16), () => $"world 64km chunks {_simulation.LoadedChunkCount} updates {_simulation.StreamingWindowUpdatesFrame}");
        AddCachedText(overlay, 10, left, 240, 16, new Vector4(1f, 0.58f, 0.42f, 1f), _simulation.CommandRejectsFrame ^ (_simulation.CommandRejectsTotal << 8), () => $"invalid orders {_simulation.CommandRejectsFrame}/{_simulation.CommandRejectsTotal}");
        AddCachedText(overlay, 11, left, 262, 15, new Vector4(0.74f, 0.84f, 0.94f, 1f), _simulation.ScenarioSpawnCount ^ (_simulation.SceneResetCount << 10), () => $"spawn {_simulation.ScenarioSpawnCount} reset {_simulation.SceneResetCount}");
        AddCachedText(overlay, 12, left, 282, 15, new Vector4(0.74f, 0.84f, 0.94f, 1f), _simulation.CameraBudgetUpdatesFrame ^ (_simulation.CameraBudgetUpdatesTotal << 10) ^ (_simulation.SolverWindowMovesFrame << 20), () => $"camera budget {_simulation.CameraBudgetUpdatesFrame}/{_simulation.CameraBudgetUpdatesTotal} solver move {_simulation.SolverWindowMovesFrame}/{_simulation.SolverWindowMovesTotal}");

        int bottomLeftY = Math.Max(356, (int)resolution.Y - 92);
        var serialHash = new HashCode();
        serialHash.Add((int)MathF.Round(_simulation.SolverWindowCenterXCm));
        serialHash.Add((int)MathF.Round(_simulation.SolverWindowCenterYCm));
        serialHash.Add((int)MathF.Round(_simulation.SolverWindowWidthCm));
        serialHash.Add((int)MathF.Round(_simulation.SolverWindowHeightCm));
        serialHash.Add((int)MathF.Round(_simulation.FlowWorkAreaCenterXCm));
        serialHash.Add((int)MathF.Round(_simulation.FlowWorkAreaCenterYCm));
        serialHash.Add((int)MathF.Round(_simulation.FlowWorkAreaWidthCm));
        serialHash.Add((int)MathF.Round(_simulation.FlowWorkAreaHeightCm));
        serialHash.Add(_simulation.FlowWorkAreaRevision);
        serialHash.Add(_simulation.LoadedChunkCount);
        serialHash.Add(_simulation.SolverWindowMovesTotal);
        serialHash.Add(_simulation.CameraBudgetUpdatesTotal);
        int hotZoneSerial = serialHash.ToHashCode();
        AddCachedText(overlay, 13, 500, bottomLeftY, 16, new Vector4(0.54f, 0.92f, 1f, 1f), hotZoneSerial, () => $"work area {_simulation.FlowWorkAreaWidthCm / 100f:0}x{_simulation.FlowWorkAreaHeightCm / 100f:0}m center ({_simulation.FlowWorkAreaCenterXCm:0},{_simulation.FlowWorkAreaCenterYCm:0})");
        AddCachedText(overlay, 14, 500, bottomLeftY + 22, 15, new Vector4(0.74f, 0.84f, 0.94f, 1f), hotZoneSerial, () => $"solver cache ({_simulation.SolverWindowCenterXCm:0},{_simulation.SolverWindowCenterYCm:0}) cm  driver {_simulation.SolverWindowDriver}");
        AddCachedText(overlay, 15, 500, bottomLeftY + 44, 15, new Vector4(0.74f, 0.84f, 0.94f, 1f), hotZoneSerial, () => $"budget driver {_simulation.FlowWorkAreaReason}; camera never respawns units");
        AddCachedText(overlay, 16, 500, bottomLeftY + 66, 15, new Vector4(0.74f, 0.84f, 0.94f, 1f), hotZoneSerial, () => "RTS minimap: click anywhere to move camera; right-click selected units to any world point");
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

    private static CachedHudLine[] CreateHudLineCache()
    {
        var cache = new CachedHudLine[17];
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
