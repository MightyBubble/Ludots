using System;
using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Systems
{
    public sealed class VirtualCameraFollowBinding
    {
        public VirtualCameraFollowBinding(string virtualCameraId, ICameraFollowTarget followTarget, bool snapToFollowTargetWhenAvailable = true)
        {
            if (string.IsNullOrWhiteSpace(virtualCameraId))
            {
                throw new ArgumentException("Virtual camera id is required.", nameof(virtualCameraId));
            }

            VirtualCameraId = virtualCameraId;
            FollowTarget = followTarget ?? throw new ArgumentNullException(nameof(followTarget));
            SnapToFollowTargetWhenAvailable = snapToFollowTargetWhenAvailable;
        }

        public string VirtualCameraId { get; }
        public ICameraFollowTarget FollowTarget { get; }
        public bool SnapToFollowTargetWhenAvailable { get; }
    }

    /// <summary>
    /// Rebinds explicit follow targets when a virtual camera becomes active.
    /// Selection/local-player semantics stay in composition layers, not in camera core.
    /// </summary>
    public sealed class VirtualCameraFollowBindingSystem : ISystem<float>
    {
        private readonly CameraManager _camera;
        private readonly VirtualCameraFollowBinding[] _bindings;
        private readonly Dictionary<string, bool> _activeStates = new(StringComparer.OrdinalIgnoreCase);

        public VirtualCameraFollowBindingSystem(CameraManager camera, params VirtualCameraFollowBinding[] bindings)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _bindings = bindings ?? Array.Empty<VirtualCameraFollowBinding>();
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                var binding = _bindings[i];
                bool isActive = _camera.IsVirtualCameraActive(binding.VirtualCameraId);
                bool wasActive = _activeStates.TryGetValue(binding.VirtualCameraId, out var cached) && cached;

                if (isActive && !wasActive)
                {
                    _camera.SetFollowTarget(
                        binding.VirtualCameraId,
                        binding.FollowTarget,
                        binding.SnapToFollowTargetWhenAvailable);
                }

                if (isActive)
                {
                    _camera.SyncVirtualCameraFollowState(binding.VirtualCameraId);
                }

                _activeStates[binding.VirtualCameraId] = isActive;
            }
        }
    }
}
