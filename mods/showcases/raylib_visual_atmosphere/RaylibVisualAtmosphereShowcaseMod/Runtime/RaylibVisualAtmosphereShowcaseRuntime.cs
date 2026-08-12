using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
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
    private bool _vegetationSpawned;
    private bool _decalsSpawned;

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

        if (!_vegetationSpawned)
        {
            int spawned = VegetationPlacementBootstrap.SpawnForActiveMap(engine);
            if (spawned <= 0)
            {
                throw new InvalidOperationException(
                    "Raylib visual atmosphere island requires vegetation placements to spawn.");
            }

            _vegetationSpawned = true;
        }

        if (!_decalsSpawned)
        {
            int decals = DecalPlacementBootstrap.SpawnForActiveMap(engine);
            if (decals <= 0)
            {
                throw new InvalidOperationException(
                    "Raylib visual atmosphere island requires textured Decal beach placements to spawn.");
            }

            _decalsSpawned = true;
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
            _vegetationSpawned = false;
            _decalsSpawned = false;
        }
    }

    private void ApplyCaptureCamera(GameEngine engine)
    {
        CaptureShot shot = ResolveCaptureShot();
        string cameraId = ResolveCameraId(shot);
        var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
            ?? throw new InvalidOperationException(
                "Raylib visual atmosphere showcase requires VirtualCameraRegistry.");

        if (!registry.TryGet(cameraId, out VirtualCameraDefinition? definition) || definition == null)
        {
            throw new InvalidOperationException(
                $"Raylib visual atmosphere showcase requires virtual camera '{cameraId}'.");
        }

        engine.GameSession.Camera.ResetVirtualCameras();
        engine.GameSession.Camera.ActivateVirtualCamera(
            cameraId,
            blendDurationSeconds: 0f,
            followTarget: CameraFollowTargetFactory.Build(
                engine.World,
                engine.GlobalContext,
                definition.FollowTargetKind,
                Entity.Null,
                definition.FollowCollectionKey),
            snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable,
            resetRuntimeState: true);

        Vector2? targetCm = definition.TargetSource == VirtualCameraTargetSource.Fixed
            ? definition.FixedTargetCm
            : null;
        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = cameraId,
            TargetCm = targetCm,
            Yaw = definition.Yaw,
            Pitch = definition.Pitch,
            DistanceCm = definition.DistanceCm,
            FovYDeg = definition.FovYDeg,
        });
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
            "01" or "01_sky_day" or "sky_day" or "cam_aerial__tod_morning" or "cam_aerial__tod_midday"
                => CaptureShot.SkyDay,
            "02" or "02_sky_night" or "sky_night" or "cam_aerial__tod_night"
                => CaptureShot.SkyNight,
            "03" or "03_cutout_vegetation" or "cutout" or "cam_veg__tod_midday"
                => CaptureShot.CutoutVegetation,
            "04" or "04_blend_modes" or "blend" => CaptureShot.BlendModes,
            "05" or "05_distance_fog" or "fog" => CaptureShot.DistanceFog,
            "06" or "06_water_reflect" or "water" or "cam_water__tod_midday"
                => CaptureShot.WaterReflect,
            "07" or "07_beach_decals" or "beach_decals" or "decals"
                => CaptureShot.BeachDecals,
            "cam_aerial__tod_dawn" or "cam_aerial__tod_afternoon" or "cam_aerial__tod_dusk"
                => CaptureShot.AerialTimed,
            "cam_orbit_ne__tod_morning" or "cam_orbit_ne__tod_midday"
            or "cam_orbit_ne__tod_dusk" or "cam_orbit_ne__tod_night"
                => CaptureShot.OrbitNe,
            "cam_orbit_sw__tod_morning" or "cam_orbit_sw__tod_midday"
            or "cam_orbit_sw__tod_dusk" or "cam_orbit_sw__tod_night"
                => CaptureShot.OrbitSw,
            "cam_shore__tod_morning" or "cam_shore__tod_midday"
            or "cam_shore__tod_dusk" or "cam_shore__tod_night"
                => CaptureShot.Shore,
            "cam_veg__tod_morning" or "cam_veg__tod_dusk" or "cam_veg__tod_night"
                => CaptureShot.CutoutVegetation,
            "cam_water__tod_morning" or "cam_water__tod_dusk" or "cam_water__tod_night"
                => CaptureShot.WaterReflect,
            "composition" or "playable" => CaptureShot.Composition,
            _ => throw new InvalidOperationException(
                $"Unknown LUDOTS_ATMOSPHERE_SHOT '{raw}'. Expected 01..07, cam_* matrix ids, or composition."),
        };
    }

    private static string ResolveCameraId(CaptureShot shot)
    {
        return shot switch
        {
            CaptureShot.SkyDay or CaptureShot.SkyNight or CaptureShot.Composition
            or CaptureShot.AerialTimed =>
                "raylib_visual_atmosphere.camera.composition",
            CaptureShot.OrbitNe => "raylib_visual_atmosphere.camera.orbit_ne",
            CaptureShot.OrbitSw => "raylib_visual_atmosphere.camera.orbit_sw",
            CaptureShot.Shore => "raylib_visual_atmosphere.camera.shore",
            CaptureShot.CutoutVegetation => "raylib_visual_atmosphere.camera.cutout",
            CaptureShot.BlendModes => "raylib_visual_atmosphere.camera.blend",
            CaptureShot.DistanceFog => "raylib_visual_atmosphere.camera.fog",
            CaptureShot.WaterReflect => "raylib_visual_atmosphere.camera.water",
            CaptureShot.BeachDecals => "raylib_visual_atmosphere.camera.beach_decals",
            _ => throw new InvalidOperationException($"Unhandled capture shot '{shot}'."),
        };
    }

    private enum CaptureShot
    {
        Composition,
        SkyDay,
        SkyNight,
        AerialTimed,
        OrbitNe,
        OrbitSw,
        Shore,
        CutoutVegetation,
        BlendModes,
        DistanceFog,
        WaterReflect,
        BeachDecals,
    }
}
