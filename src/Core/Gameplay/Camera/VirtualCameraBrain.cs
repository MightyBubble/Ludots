using System;
using System.Numerics;
using Ludots.Core.Tweening;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class VirtualCameraBrain
    {
        private readonly VirtualCameraRegistry _registry;
        private RuntimeVirtualCamera? _active;
        private CameraStateSnapshot _blendFrom;
        private TweenProgress _blendProgress;

        public VirtualCameraBrain(VirtualCameraRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _blendProgress.Complete();
        }

        public bool HasActiveCamera => _active != null;
        public bool AllowsInput => _active != null && _active.Definition.AllowUserInput && !IsBlending;
        public bool IsBlending => _blendProgress.IsActive;
        public string ActiveCameraId => _active?.Definition.Id ?? string.Empty;

        public void Activate(string id, CameraState currentState, float? blendDurationSeconds = null)
        {
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            var definition = _registry.Get(id);
            _active = new RuntimeVirtualCamera(definition, FromDefinition(definition, currentState));
            _blendFrom = CameraStateSnapshot.FromState(currentState);
            _blendProgress.Start(
                Math.Max(0f, blendDurationSeconds ?? definition.DefaultBlendDuration),
                ToTweenEasing(definition.BlendCurve));
        }

        public void Clear()
        {
            _active = null;
            _blendProgress.Complete();
        }

        public void ApplyToState(CameraState state, Vector2? followTargetPositionCm, float dt)
        {
            if (state == null || _active == null)
            {
                return;
            }

            var desired = ResolveDesiredSnapshot(_active, followTargetPositionCm);
            if (IsBlending)
            {
                float t = _blendProgress.Tick(dt);
                var blended = CameraStateSnapshot.Lerp(_blendFrom, desired, t);
                blended.ApplyTo(state);
            }
            else
            {
                desired.ApplyTo(state);
            }
        }

        public void CapturePostControllerState(CameraState state)
        {
            if (_active == null || !AllowsInput)
            {
                return;
            }

            var captured = CameraStateSnapshot.FromState(state);
            ref var runtime = ref _active.RuntimeState;
            runtime.Yaw = captured.Yaw;
            runtime.Pitch = captured.Pitch;
            runtime.DistanceCm = captured.DistanceCm;
            runtime.FovYDeg = captured.FovYDeg;
            runtime.RigKind = captured.RigKind;

            if (_active.Definition.TargetSource == VirtualCameraTargetSource.Fixed)
            {
                runtime.TargetCm = captured.TargetCm;
            }
        }

        private static CameraStateSnapshot ResolveDesiredSnapshot(RuntimeVirtualCamera active, Vector2? followTargetPositionCm)
        {
            var desired = active.RuntimeState;
            if (active.Definition.TargetSource == VirtualCameraTargetSource.FollowTarget && followTargetPositionCm.HasValue)
            {
                desired.TargetCm = followTargetPositionCm.Value;
                desired.IsFollowing = true;
            }
            else
            {
                desired.IsFollowing = false;
            }

            return desired;
        }

        private static TweenEasing ToTweenEasing(CameraBlendCurve curve)
        {
            return curve switch
            {
                CameraBlendCurve.Cut => TweenEasing.Cut,
                CameraBlendCurve.Linear => TweenEasing.Linear,
                CameraBlendCurve.SmoothStep => TweenEasing.SmoothStep,
                _ => TweenEasing.Linear
            };
        }

        private sealed class RuntimeVirtualCamera
        {
            public RuntimeVirtualCamera(VirtualCameraDefinition definition, CameraStateSnapshot runtimeState)
            {
                Definition = definition;
                RuntimeState = runtimeState;
            }

            public VirtualCameraDefinition Definition { get; }
            public CameraStateSnapshot RuntimeState;
        }

        private static CameraStateSnapshot FromDefinition(VirtualCameraDefinition definition, CameraState currentState)
        {
            return new CameraStateSnapshot
            {
                TargetCm = definition.TargetSource == VirtualCameraTargetSource.Fixed
                    ? definition.FixedTargetCm
                    : currentState.TargetCm,
                Yaw = definition.Yaw,
                Pitch = definition.Pitch,
                DistanceCm = definition.DistanceCm,
                FovYDeg = definition.FovYDeg,
                RigKind = definition.RigKind,
                ZoomLevel = currentState.ZoomLevel,
                IsFollowing = false
            };
        }
    }
}
