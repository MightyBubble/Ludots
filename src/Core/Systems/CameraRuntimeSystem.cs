using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// Fixed-step authoritative camera system.
    /// Applies pending camera requests, freezes the latest sampled input, and advances camera logic.
    /// </summary>
    public sealed class CameraRuntimeSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly CameraManager _cameraManager;
        private readonly Dictionary<string, object> _globals;
        private readonly CameraPresetRegistry _presetRegistry;

        public CameraRuntimeSystem(
            World world,
            CameraManager cameraManager,
            Dictionary<string, object> globals,
            CameraPresetRegistry presetRegistry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _cameraManager = cameraManager ?? throw new ArgumentNullException(nameof(cameraManager));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _presetRegistry = presetRegistry ?? throw new ArgumentNullException(nameof(presetRegistry));
        }

        public void Initialize()
        {
        }

        public void Update(in float dt)
        {
            ApplyCameraPresetRequest();
            ApplyCameraPoseRequest();
            ApplyVirtualCameraRequest();
            _cameraManager.Update(dt);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private void ApplyCameraPresetRequest()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.CameraPresetRequest.Name, out var requestObj) ||
                requestObj is not CameraPresetRequest request)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(request.PresetId))
            {
                throw new InvalidOperationException("CameraPresetRequest.PresetId is required.");
            }

            if (!_presetRegistry.TryGet(request.PresetId, out var preset) || preset == null)
            {
                throw new InvalidOperationException($"Camera preset '{request.PresetId}' is not registered.");
            }

            if (request.ClearActiveVirtualCamera)
            {
                _cameraManager.ClearVirtualCamera();
            }

            _cameraManager.ApplyPreset(
                preset,
                CameraFollowTargetFactory.Build(_world, _globals, request.FollowTargetKindOverride ?? preset.FollowTargetKind),
                request.SnapToFollowTargetWhenAvailable);

            _globals.Remove(CoreServiceKeys.CameraPresetRequest.Name);
        }

        private void ApplyCameraPoseRequest()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.CameraPoseRequest.Name, out var requestObj) ||
                requestObj is not CameraPoseRequest request)
            {
                return;
            }

            _cameraManager.ApplyPose(request);
            _globals.Remove(CoreServiceKeys.CameraPoseRequest.Name);
        }

        private void ApplyVirtualCameraRequest()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.VirtualCameraRequest.Name, out var requestObj) ||
                requestObj is not VirtualCameraRequest request)
            {
                return;
            }

            if (request.Clear || string.IsNullOrWhiteSpace(request.Id))
            {
                _cameraManager.ClearVirtualCamera();
            }
            else
            {
                _cameraManager.ActivateVirtualCamera(request.Id, request.BlendDurationSeconds);
            }

            _globals.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
        }
    }
}
