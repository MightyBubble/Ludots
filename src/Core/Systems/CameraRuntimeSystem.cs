using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// Fixed-step authoritative camera system.
    /// Applies pending camera requests to every registered LogicView camera (Epic #896).
    /// Session camera is used only before any LogicView exists (pre-map boot).
    /// </summary>
    public sealed class CameraRuntimeSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly CameraManager _fallbackCameraManager;
        private readonly Dictionary<string, object> _globals;
        private readonly VirtualCameraRegistry _virtualCameraRegistry;
        private readonly List<CameraManager> _cameraScratch = new(4);

        public CameraRuntimeSystem(
            World world,
            CameraManager fallbackCameraManager,
            Dictionary<string, object> globals,
            VirtualCameraRegistry virtualCameraRegistry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _fallbackCameraManager = fallbackCameraManager ?? throw new ArgumentNullException(nameof(fallbackCameraManager));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _virtualCameraRegistry = virtualCameraRegistry ?? throw new ArgumentNullException(nameof(virtualCameraRegistry));
        }

        public void Initialize()
        {
        }

        public void Update(in float dt)
        {
            CollectCameras();
            for (int i = 0; i < _cameraScratch.Count; i++)
            {
                ApplyVirtualCameraRequest(_cameraScratch[i]);
                ApplyCameraPoseRequest(_cameraScratch[i]);
                _cameraScratch[i].Update(dt);
            }

            _globals.Remove(CoreServiceKeys.CameraPoseRequest.Name);
            _globals.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private void CollectCameras()
        {
            _cameraScratch.Clear();
            if (_globals.TryGetValue(CoreServiceKeys.LogicViewRegistry.Name, out object? viewsObj) &&
                viewsObj is LogicViewRegistry views)
            {
                if (views.Count > 0)
                {
                    views.CopyCameras(_cameraScratch);
                    return;
                }

                if (_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) &&
                    seatsObj is ClientLocalSeatRegistry seats &&
                    seats.Count > 0)
                {
                    throw new InvalidOperationException(
                        "ClientLocalSeatRegistry has seats but LogicViewRegistry is empty — PresentBinding/LogicView publish is required.");
                }
            }

            _cameraScratch.Add(_fallbackCameraManager);
        }

        private void ApplyCameraPoseRequest(CameraManager cameraManager)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.CameraPoseRequest.Name, out var requestObj) ||
                requestObj is not CameraPoseRequest request)
            {
                return;
            }

            cameraManager.ApplyPose(request);
        }

        private void ApplyVirtualCameraRequest(CameraManager cameraManager)
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
                    cameraManager.ClearVirtualCamera();
                }
                else
                {
                    cameraManager.DeactivateVirtualCamera(request.Id, request.BlendDurationSeconds);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(request.Id))
            {
                throw new InvalidOperationException("VirtualCameraRequest.Id is required when Clear=false.");
            }

            if (request.ReplaceActiveStack)
            {
                cameraManager.ResetVirtualCameras();
            }

            var definition = _virtualCameraRegistry.Get(request.Id);
            var followTargetKind = request.FollowTargetKindOverride ?? definition.FollowTargetKind;
            string followCollectionKey = string.IsNullOrWhiteSpace(request.FollowCollectionKeyOverride)
                ? definition.FollowCollectionKey
                : request.FollowCollectionKeyOverride;
            var followTarget = CameraFollowTargetFactory.Build(
                _world,
                _globals,
                followTargetKind,
                request.FollowCollectionOwnerOverride,
                followCollectionKey);

            cameraManager.ActivateVirtualCamera(
                request.Id,
                request.BlendDurationSeconds,
                request.PriorityOverride,
                followTarget,
                request.SnapToFollowTargetWhenAvailable,
                request.ResetRuntimeState);
        }
    }
}
