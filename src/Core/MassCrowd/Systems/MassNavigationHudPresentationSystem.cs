using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.MassCrowd.Runtime;

namespace Ludots.Core.MassCrowd.Systems;

internal sealed class MassNavigationHudPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly CachedHudLine[] _cachedLines = CreateHudLineCache();

    public MassNavigationHudPresentationSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        _simulation.ObserveHudTick();

        ScreenOverlayBuffer overlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("MassCrowd runtime requires ScreenOverlayBuffer for diagnostics HUD.");
        PresentationTimingDiagnostics timing = _engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
            ?? throw new InvalidOperationException("MassCrowd runtime requires PresentationTimingDiagnostics for real FPS HUD.");
        IViewController viewport = _engine.GetService(CoreServiceKeys.ViewController)
            ?? throw new InvalidOperationException("MassCrowd runtime requires ViewController for diagnostics HUD layout.");
        Vector2 resolution = viewport.Resolution;
        int left = Math.Max(16, (int)resolution.X - 260);
        float frameMs = ResolveFrameMs(timing);
        float fps = frameMs > 0.001f ? 1000f / frameMs : 0f;
        AddCachedFpsText(overlay, 0, left, 16, 20, new Vector4(0.92f, 0.96f, 1f, 1f), (int)MathF.Round(fps * 10f), fps);
    }

    private void AddCachedFpsText(ScreenOverlayBuffer overlay, int cacheIndex, int x, int y, int fontSize, Vector4 color, int dirtySerial, float fps)
    {
        ref CachedHudLine cache = ref _cachedLines[cacheIndex];
        if (cache.DirtySerial != dirtySerial || cache.Text == null)
        {
            cache.DirtySerial = dirtySerial;
            cache.Text = $"fps {fps:0.0}";
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
