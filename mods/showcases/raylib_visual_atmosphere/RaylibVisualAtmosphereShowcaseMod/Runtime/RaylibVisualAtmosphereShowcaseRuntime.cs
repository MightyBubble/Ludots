using System;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace RaylibVisualAtmosphereShowcaseMod.Runtime;

internal sealed class RaylibVisualAtmosphereShowcaseRuntime : IBenchmarkSceneController
{
    private GameEngine? _activeEngine;

    public bool IsActive =>
        _activeEngine != null &&
        RaylibVisualAtmosphereShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);

    public bool SupportsScatterControl => false;

    public bool IsCleanPerformanceScene => false;

    public bool SuppressHostDiagnosticUi => IsActive;

    public bool SuppressHostDebugGuides => IsActive;

    public int ScatterMin => 0;

    public int ScatterMax => 0;

    public int ScatterTarget => 0;

    public int ScatterAppliedTotal => 0;

    public void SetScatterTargetFromRatio(float ratio)
    {
    }

    public void ApplyScatterTarget()
    {
    }

    public void ApplyScatterLayout(int total)
    {
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!RaylibVisualAtmosphereShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            Disable(engine);
            return Task.CompletedTask;
        }

        _activeEngine = engine;
        engine.SetService(CoreServiceKeys.BenchmarkSceneController, (IBenchmarkSceneController)this);
        if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
        {
            renderDebug.DrawSkiaUi = false;
            renderDebug.DrawPrimitives = true;
            renderDebug.DrawTerrain = true;
        }

        ApplyCaptureCamera(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is GameEngine engine)
        {
            Disable(engine);
        }

        return Task.CompletedTask;
    }

    private void Disable(GameEngine engine)
    {
        if (ReferenceEquals(_activeEngine, engine))
        {
            _activeEngine = null;
        }
    }

    private void ApplyCaptureCamera(GameEngine engine)
    {
        CaptureShot shot = ResolveCaptureShot();
        CameraPoseRequest pose = ResolvePose(shot);
        engine.GameSession.Camera.ApplyPose(pose);
        engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
    }

    private static CaptureShot ResolveCaptureShot()
    {
        string? raw = Environment.GetEnvironmentVariable("LUDOTS_ATMOSPHERE_SHOT");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CaptureShot.Composition;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "01" or "01_sky_day" or "sky_day" => CaptureShot.SkyDay,
            "02" or "02_sky_night" or "sky_night" => CaptureShot.SkyNight,
            "03" or "03_cutout_vegetation" or "cutout" => CaptureShot.CutoutVegetation,
            "04" or "04_blend_modes" or "blend" => CaptureShot.BlendModes,
            "05" or "05_distance_fog" or "fog" => CaptureShot.DistanceFog,
            "06" or "06_water_reflect" or "water" => CaptureShot.WaterReflect,
            "composition" or "playable" => CaptureShot.Composition,
            _ => throw new InvalidOperationException(
                $"Unknown LUDOTS_ATMOSPHERE_SHOT '{raw}'. Expected 01..06 or composition."),
        };
    }

    private static CameraPoseRequest ResolvePose(CaptureShot shot)
    {
        float cx = RaylibVisualAtmosphereShowcaseIds.IslandCenterXCm;
        float cy = RaylibVisualAtmosphereShowcaseIds.IslandCenterYCm;

        return shot switch
        {
            CaptureShot.SkyDay or CaptureShot.SkyNight or CaptureShot.Composition => new CameraPoseRequest
            {
                TargetCm = new Vector2(cx + 4_000f, cy + 2_500f),
                TargetHeightCm = 900f,
                Yaw = 205f,
                Pitch = 26f,
                DistanceCm = 22_000f,
                FovYDeg = 55f,
            },
            CaptureShot.CutoutVegetation => new CameraPoseRequest
            {
                TargetCm = new Vector2(100_625f, 62_809f),
                TargetHeightCm = 350f,
                Yaw = 200f,
                Pitch = 8f,
                DistanceCm = 4_200f,
                FovYDeg = 40f,
            },
            CaptureShot.BlendModes => new CameraPoseRequest
            {
                TargetCm = new Vector2(99_500f, 65_500f),
                TargetHeightCm = 280f,
                Yaw = 210f,
                Pitch = 6f,
                DistanceCm = 3_800f,
                FovYDeg = 38f,
            },
            CaptureShot.DistanceFog => new CameraPoseRequest
            {
                TargetCm = new Vector2(cx - 12_000f, cy - 8_000f),
                TargetHeightCm = 1_400f,
                Yaw = 40f,
                Pitch = 12f,
                DistanceCm = 36_000f,
                FovYDeg = 58f,
            },
            CaptureShot.WaterReflect => new CameraPoseRequest
            {
                TargetCm = new Vector2(104_600f, 57_600f),
                TargetHeightCm = 120f,
                Yaw = 250f,
                Pitch = 6f,
                DistanceCm = 8_500f,
                FovYDeg = 48f,
            },
            _ => throw new InvalidOperationException($"Unhandled capture shot '{shot}'."),
        };
    }

    private enum CaptureShot
    {
        Composition,
        SkyDay,
        SkyNight,
        CutoutVegetation,
        BlendModes,
        DistanceFog,
        WaterReflect,
    }
}
