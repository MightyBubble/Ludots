using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed partial class Physics3DShowcaseRuntime
{
    private bool ActivateStationFollowCamera(
        string cameraId,
        Func<CameraTargetTransformSnapshot> targetProvider,
        string stationName)
    {
        if (_engine == null)
        {
            return false;
        }

        VirtualCameraRegistry registry = _engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
            ?? throw new InvalidOperationException($"Physics3D {stationName} requires VirtualCameraRegistry.");
        VirtualCameraDefinition definition = registry.Get(cameraId);
        if (definition.TargetSource != VirtualCameraTargetSource.FollowTarget ||
            definition.FollowMode != CameraFollowMode.AlwaysFollow ||
            definition.PanMode != CameraPanMode.None)
        {
            throw new InvalidOperationException(
                $"Physics3D {stationName} camera '{cameraId}' must use FollowTarget, AlwaysFollow, and no keyboard pan.");
        }

        _engine.GameSession.Camera.ResetVirtualCameras();
        _engine.GameSession.Camera.ActivateVirtualCamera(
            cameraId,
            blendDurationSeconds: 0f,
            followTarget: new DirectTransformFollowTarget(targetProvider),
            snapToFollowTargetWhenAvailable: true,
            resetRuntimeState: true);
        _engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
        return true;
    }

    private void RestoreDefaultCamera(string stationName)
    {
        GameEngine engine = _engine
            ?? throw new InvalidOperationException($"Physics3D {stationName} lost GameEngine before camera release.");
        var cameraConfig = engine.CurrentMapSession?.MapConfig?.DefaultCamera
            ?? throw new InvalidOperationException($"Physics3D {stationName} requires map DefaultCamera for release.");
        if (string.IsNullOrWhiteSpace(cameraConfig.VirtualCameraId))
        {
            throw new InvalidOperationException($"Physics3D {stationName} requires an explicit default virtual camera id.");
        }

        VirtualCameraRegistry registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
            ?? throw new InvalidOperationException($"Physics3D {stationName} requires VirtualCameraRegistry for release.");
        VirtualCameraDefinition definition = registry.Get(cameraConfig.VirtualCameraId);
        engine.GameSession.Camera.ResetVirtualCameras();
        engine.GameSession.Camera.ActivateVirtualCamera(
            cameraConfig.VirtualCameraId,
            blendDurationSeconds: 0f,
            followTarget: CameraFollowTargetFactory.Build(
                engine.World,
                engine.GlobalContext,
                definition.FollowTargetKind,
                Entity.Null,
                definition.FollowCollectionKey),
            snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable,
            resetRuntimeState: true);
        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = cameraConfig.VirtualCameraId,
            TargetCm = cameraConfig.TargetXCm.HasValue || cameraConfig.TargetYCm.HasValue
                ? new Vector2(cameraConfig.TargetXCm ?? 0f, cameraConfig.TargetYCm ?? 0f)
                : null,
            Yaw = cameraConfig.Yaw,
            Pitch = cameraConfig.Pitch,
            DistanceCm = cameraConfig.DistanceCm,
            FovYDeg = cameraConfig.FovYDeg
        });
        engine.GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();
    }
}
