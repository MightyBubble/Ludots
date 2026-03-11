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
        private readonly VirtualCameraRegistry _virtualCameraRegistry;

        public CameraRuntimeSystem(
            World world,
            CameraManager cameraManager,
            Dictionary<string, object> globals,
            VirtualCameraRegistry virtualCameraRegistry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _cameraManager = cameraManager ?? throw new ArgumentNullException(nameof(cameraManager));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _virtualCameraRegistry = virtualCameraRegistry ?? throw new ArgumentNullException(nameof(virtualCameraRegistry));
        }

        public void Initialize()
        {
        }

        public void Update(in float dt)
        {
            ApplyVirtualCameraRequest();
            ApplyCameraPoseRequest();
            _cameraManager.Update(dt);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

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

            if (request.Clear)
            {
                if (string.IsNullOrWhiteSpace(request.Id))
                {
                    _cameraManager.ClearVirtualCamera();
                }
                else
                {
                    _cameraManager.DeactivateVirtualCamera(request.Id, request.BlendDurationSeconds);
                }

                _globals.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.Id))
            {
                throw new InvalidOperationException("VirtualCameraRequest.Id is required when Clear=false.");
            }

            var definition = _virtualCameraRegistry.Get(request.Id);
            var followTarget = CameraFollowTargetFactory.Build(
                _world,
                _globals,
                request.FollowTargetKindOverride ?? definition.FollowTargetKind);

            _cameraManager.ActivateVirtualCamera(
                request.Id,
                request.BlendDurationSeconds,
                request.PriorityOverride,
                followTarget,
                request.SnapToFollowTargetWhenAvailable,
                request.ResetRuntimeState);

            _globals.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
        }
    }
}
