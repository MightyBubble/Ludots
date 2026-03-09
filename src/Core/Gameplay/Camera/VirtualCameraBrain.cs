using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class VirtualCameraBrain
    {
        private readonly VirtualCameraRegistry _registry;
        private RuntimeVirtualCamera? _active;
        private CameraStateSnapshot _blendFrom;
        private float _blendDuration;
        private float _blendElapsed;
        private CameraBlendCurve _blendCurve;

        public VirtualCameraBrain(VirtualCameraRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public bool HasActiveCamera => _active != null;
        public bool AllowsInput => _active != null && _active.Definition.AllowUserInput && !IsBlending;
        public bool IsBlending { get; private set; }
        public string ActiveCameraId => _active?.Definition.Id ?? string.Empty;

        public void Activate(string id, CameraState currentState, float? blendDurationSeconds = null)
        {
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            var definition = _registry.Get(id);
            _active = new RuntimeVirtualCamera(definition, CameraStateSnapshot.FromDefinition(definition, currentState));
            _blendFrom = CameraStateSnapshot.FromState(currentState);
            _blendDuration = Math.Max(0f, blendDurationSeconds ?? definition.DefaultBlendDuration);
            _blendElapsed = 0f;
            _blendCurve = definition.BlendCurve;
            IsBlending = _blendDuration > 0f && _blendCurve != CameraBlendCurve.Cut;
        }

        public void Clear()
        {
            _active = null;
            _blendDuration = 0f;
            _blendElapsed = 0f;
            IsBlending = false;
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
                _blendElapsed += Math.Max(0f, dt);
                float rawT = _blendDuration <= 0f ? 1f : Math.Clamp(_blendElapsed / _blendDuration, 0f, 1f);
                float t = EvaluateBlend(rawT, _blendCurve);
                var blended = CameraStateSnapshot.Lerp(_blendFrom, desired, t);
                blended.ApplyTo(state);
                if (rawT >= 1f)
                {
                    IsBlending = false;
                }
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

        private static float EvaluateBlend(float t, CameraBlendCurve curve)
        {
            t = Math.Clamp(t, 0f, 1f);
            return curve switch
            {
                CameraBlendCurve.Cut => 1f,
                CameraBlendCurve.Linear => t,
                CameraBlendCurve.SmoothStep => t * t * (3f - (2f * t)),
                _ => t
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

        private struct CameraStateSnapshot
        {
            public Vector2 TargetCm;
            public float Yaw;
            public float Pitch;
            public float DistanceCm;
            public float FovYDeg;
            public CameraRigKind RigKind;
            public bool IsFollowing;

            public static CameraStateSnapshot FromState(CameraState state)
            {
                return new CameraStateSnapshot
                {
                    TargetCm = state.TargetCm,
                    Yaw = state.Yaw,
                    Pitch = state.Pitch,
                    DistanceCm = state.DistanceCm,
                    FovYDeg = state.FovYDeg,
                    RigKind = state.RigKind,
                    IsFollowing = state.IsFollowing
                };
            }

            public static CameraStateSnapshot FromDefinition(VirtualCameraDefinition definition, CameraState currentState)
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
                    IsFollowing = false
                };
            }

            public static CameraStateSnapshot Lerp(in CameraStateSnapshot from, in CameraStateSnapshot to, float t)
            {
                return new CameraStateSnapshot
                {
                    TargetCm = Vector2.Lerp(from.TargetCm, to.TargetCm, t),
                    Yaw = LerpAngleDeg(from.Yaw, to.Yaw, t),
                    Pitch = LerpScalar(from.Pitch, to.Pitch, t),
                    DistanceCm = LerpScalar(from.DistanceCm, to.DistanceCm, t),
                    FovYDeg = LerpScalar(from.FovYDeg, to.FovYDeg, t),
                    RigKind = to.RigKind,
                    IsFollowing = to.IsFollowing
                };
            }

            public void ApplyTo(CameraState state)
            {
                state.TargetCm = TargetCm;
                state.Yaw = Yaw;
                state.Pitch = Pitch;
                state.DistanceCm = DistanceCm;
                state.FovYDeg = FovYDeg;
                state.RigKind = RigKind;
                state.IsFollowing = IsFollowing;
            }

            private static float LerpScalar(float from, float to, float t)
            {
                return from + ((to - from) * t);
            }

            private static float LerpAngleDeg(float from, float to, float t)
            {
                float delta = ((to - from + 540f) % 360f) - 180f;
                return Normalize360(from + (delta * t));
            }

            private static float Normalize360(float degrees)
            {
                degrees %= 360f;
                if (degrees < 0f) degrees += 360f;
                return degrees;
            }
        }
    }
}
