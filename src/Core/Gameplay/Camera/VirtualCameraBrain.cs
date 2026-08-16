using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Tweening;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class VirtualCameraBrain
    {
        private readonly VirtualCameraRegistry _registry;
        private readonly Dictionary<string, RuntimeVirtualCamera> _active = new(StringComparer.OrdinalIgnoreCase);
        private CameraStateSnapshot _blendFrom;
        private TweenProgress _blendProgress;
        private long _activationSequence;
        private RuntimeVirtualCamera? _resolved;

        public VirtualCameraBrain(VirtualCameraRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _blendProgress.Complete();
        }

        public bool HasActiveCamera => _resolved != null;
        public int ActiveCameraCount => _active.Count;
        public bool IsBlending => _blendProgress.IsActive;
        public bool AllowsInput => _resolved != null && _resolved.Definition.AllowUserInput && !IsBlending;
        public string ActiveCameraId => _resolved?.Definition.Id ?? string.Empty;
        public VirtualCameraDefinition? ActiveDefinition => _resolved?.Definition;
        public Vector2? ActiveFollowTargetPositionCm => _resolved?.ResolvedFollowTargetTransform?.PositionCm;
        public CameraTargetTransformSnapshot? ActiveFollowTargetTransform => _resolved?.ResolvedFollowTargetTransform;

        public bool IsActive(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && _active.ContainsKey(id);
        }

        public void Activate(
            string id,
            CameraState currentState,
            float? blendDurationSeconds = null,
            int? priorityOverride = null,
            ICameraFollowTarget? followTarget = null,
            bool snapToFollowTargetWhenAvailable = true,
            bool resetRuntimeState = true)
        {
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            var currentOutput = CaptureCurrentOutputState(currentState);

            var definition = _registry.Get(id);
            if (!_active.TryGetValue(id, out var runtime))
            {
                runtime = new RuntimeVirtualCamera(definition, FromDefinition(definition, currentOutput));
                _active[id] = runtime;
            }
            else
            {
                runtime.Definition = definition;
                if (resetRuntimeState)
                {
                    runtime.RuntimeState = FromDefinition(definition, currentOutput);
                }
            }

            runtime.Priority = priorityOverride ?? definition.Priority;
            runtime.FollowTarget = followTarget;
            runtime.PendingFollowSnap = snapToFollowTargetWhenAvailable;
            runtime.ActivationSequence = ++_activationSequence;

            ResolveActiveCamera();
            BeginBlendFrom(currentOutput, blendDurationSeconds ?? definition.DefaultBlendDuration);
        }

        public bool Deactivate(string id, CameraState currentState, float? blendDurationSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(id) || currentState == null)
            {
                return false;
            }

            var currentOutput = CaptureCurrentOutputState(currentState);
            if (!_active.Remove(id))
            {
                return false;
            }

            string previousActiveId = ActiveCameraId;
            ResolveActiveCamera();

            if (_resolved == null)
            {
                _blendProgress.Complete();
                return true;
            }

            if (!string.Equals(previousActiveId, ActiveCameraId, StringComparison.OrdinalIgnoreCase))
            {
                BeginBlendFrom(currentOutput, blendDurationSeconds ?? _resolved.Definition.DefaultBlendDuration);
            }

            return true;
        }

        public void ClearAll()
        {
            _active.Clear();
            _resolved = null;
            _blendProgress.Complete();
        }

        public bool ApplyPose(CameraPoseRequest request)
        {
            if (request == null)
            {
                return false;
            }

            var runtime = ResolveTargetRuntime(request.VirtualCameraId);
            if (runtime == null)
            {
                return false;
            }

            ref var state = ref runtime.RuntimeState;
            if (request.TargetCm.HasValue) state.TargetCm = request.TargetCm.Value;
            if (request.TargetHeightCm.HasValue) state.TargetHeightCm = request.TargetHeightCm.Value;
            if (request.Yaw.HasValue) state.Yaw = request.Yaw.Value;
            if (request.Pitch.HasValue) state.Pitch = request.Pitch.Value;
            if (request.DistanceCm.HasValue) state.DistanceCm = request.DistanceCm.Value;
            if (request.FovYDeg.HasValue) state.FovYDeg = request.FovYDeg.Value;
            return true;
        }

        public bool TryGetActiveRuntimeTarget(
            CameraBehaviorInputState? behaviorInput,
            out VirtualCameraDefinition? definition,
            out Vector2 targetCm)
        {
            ResolveActiveCamera();
            if (_resolved == null)
            {
                definition = null;
                targetCm = default;
                return false;
            }

            ResolveRuntimeStates(behaviorInput);
            definition = _resolved.Definition;
            targetCm = _resolved.RuntimeState.TargetCm;
            return true;
        }

        public bool TryGetActiveRuntimeState(
            CameraBehaviorInputState? behaviorInput,
            out VirtualCameraDefinition? definition,
            out CameraStateSnapshot runtimeState)
        {
            ResolveActiveCamera();
            if (_resolved == null)
            {
                definition = null;
                runtimeState = default;
                return false;
            }

            ResolveRuntimeStates(behaviorInput);
            definition = _resolved.Definition;
            runtimeState = _resolved.RuntimeState;
            return true;
        }

        public bool SetActiveRuntimeTarget(Vector2 targetCm)
        {
            if (_resolved == null)
            {
                return false;
            }

            if (!float.IsFinite(targetCm.X) || !float.IsFinite(targetCm.Y))
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{_resolved.Definition.Id}' resolved a non-finite target.");
            }

            if (Vector2.DistanceSquared(_resolved.RuntimeState.TargetCm, targetCm) <= 0.0001f)
            {
                return false;
            }

            _resolved.RuntimeState.TargetCm = targetCm;
            return true;
        }

        public bool SetActiveRuntimeTargetHeight(float targetHeightCm)
        {
            if (_resolved == null)
            {
                return false;
            }

            if (!float.IsFinite(targetHeightCm))
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{_resolved.Definition.Id}' resolved a non-finite target height.");
            }

            if (MathF.Abs(_resolved.RuntimeState.TargetHeightCm - targetHeightCm) <= 0.0001f)
            {
                return false;
            }

            _resolved.RuntimeState.TargetHeightCm = targetHeightCm;
            return true;
        }

        public bool SetFollowTarget(string id, ICameraFollowTarget? followTarget, bool snapToFollowTargetWhenAvailable = true)
        {
            if (string.IsNullOrWhiteSpace(id) || !_active.TryGetValue(id, out var runtime))
            {
                return false;
            }

            runtime.FollowTarget = followTarget;
            runtime.PendingFollowSnap = snapToFollowTargetWhenAvailable;
            runtime.ResolvedFollowTargetTransform = ResolveFollowTargetTransform(followTarget);
            if (runtime.ResolvedFollowTargetTransform.HasValue && runtime.PendingFollowSnap)
            {
                ApplyFollowTransform(runtime, runtime.ResolvedFollowTargetTransform.Value, forceFacing: true);
                runtime.PendingFollowSnap = false;
            }
            return true;
        }

        public void ApplyToState(CameraState state, CameraBehaviorInputState? behaviorInput, float dt)
        {
            if (state == null)
            {
                return;
            }

            ResolveActiveCamera();
            if (_resolved == null)
            {
                return;
            }

            ResolveRuntimeStates(behaviorInput);
            var desired = _resolved.RuntimeState;

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
            if (_resolved == null || !AllowsInput || state == null)
            {
                return;
            }

            _resolved.RuntimeState = CameraStateSnapshot.FromState(state);
        }

        private RuntimeVirtualCamera? ResolveTargetRuntime(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && _active.TryGetValue(id, out var targeted))
            {
                return targeted;
            }

            return _resolved;
        }

        private void ResolveRuntimeStates(CameraBehaviorInputState? behaviorInput)
        {
            foreach (var pair in _active)
            {
                var runtime = pair.Value;
                runtime.ResolvedFollowTargetTransform = ResolveFollowTargetTransform(runtime.FollowTarget);

                if (runtime.ResolvedFollowTargetTransform.HasValue && runtime.PendingFollowSnap)
                {
                    ApplyFollowTransform(runtime, runtime.ResolvedFollowTargetTransform.Value, forceFacing: true);
                    runtime.PendingFollowSnap = false;
                }

                bool shouldFollow = ShouldFollow(runtime, behaviorInput);
                if (shouldFollow && runtime.ResolvedFollowTargetTransform.HasValue)
                {
                    ApplyFollowTransform(runtime, runtime.ResolvedFollowTargetTransform.Value, forceFacing: false);
                    runtime.RuntimeState.IsFollowing = true;
                }
                else
                {
                    runtime.RuntimeState.IsFollowing = false;
                }
            }
        }

        private bool ShouldFollow(RuntimeVirtualCamera runtime, CameraBehaviorInputState? behaviorInput)
        {
            if (runtime.ResolvedFollowTargetTransform == null)
            {
                return false;
            }

            if (runtime.Definition.TargetSource == VirtualCameraTargetSource.FollowTarget)
            {
                return true;
            }

            return runtime.Definition.FollowMode switch
            {
                CameraFollowMode.None => false,
                CameraFollowMode.AlwaysFollow => true,
                CameraFollowMode.HoldToLock => ReferenceEquals(runtime, _resolved)
                    && behaviorInput != null
                    && behaviorInput.FollowHold,
                _ => false
            };
        }

        private static CameraTargetTransformSnapshot? ResolveFollowTargetTransform(ICameraFollowTarget? followTarget)
        {
            if (followTarget != null && followTarget.TryGetTransform(out var resolved))
            {
                return resolved;
            }

            return null;
        }

        private static void ApplyFollowTransform(
            RuntimeVirtualCamera runtime,
            in CameraTargetTransformSnapshot transform,
            bool forceFacing)
        {
            runtime.RuntimeState.TargetCm = transform.PositionCm;
            if (transform.HasHeightCm)
            {
                runtime.RuntimeState.TargetHeightCm = transform.HeightCm;
            }

            if (transform.HasFacingYawRad &&
                (forceFacing || runtime.Definition.RotateMode == CameraRotateMode.None) &&
                (runtime.Definition.RigKind == CameraRigKind.FirstPerson ||
                 runtime.Definition.RigKind == CameraRigKind.ThirdPerson))
            {
                runtime.RuntimeState.Yaw = FacingRadToCameraYawDegrees(transform.FacingYawRad);
            }
        }

        private static float FacingRadToCameraYawDegrees(float facingRad)
        {
            return WorldPlane2D.NormalizeDegreesPositive(
                WorldPlane2D.RadToDegValue(facingRad - (MathF.PI * 0.5f)));
        }

        private void ResolveActiveCamera()
        {
            RuntimeVirtualCamera? best = null;
            foreach (var runtime in _active.Values)
            {
                if (best == null)
                {
                    best = runtime;
                    continue;
                }

                if (runtime.Priority > best.Priority ||
                    (runtime.Priority == best.Priority && runtime.ActivationSequence > best.ActivationSequence))
                {
                    best = runtime;
                }
            }

            _resolved = best;
        }

        private CameraStateSnapshot CaptureCurrentOutputState(CameraState currentState)
        {
            ResolveActiveCamera();
            if (_resolved == null)
            {
                return CameraStateSnapshot.FromState(currentState);
            }

            ResolveRuntimeStates(behaviorInput: null);
            var desired = _resolved.RuntimeState;
            return IsBlending
                ? CameraStateSnapshot.Lerp(_blendFrom, desired, _blendProgress.Progress)
                : desired;
        }

        private void BeginBlendFrom(CameraStateSnapshot currentOutput, float durationSeconds)
        {
            _blendFrom = currentOutput;
            if (durationSeconds <= 0f)
            {
                _blendProgress.Complete();
                return;
            }

            var easing = _resolved != null
                ? ToTweenEasing(_resolved.Definition.BlendCurve)
                : TweenEasing.Linear;
            _blendProgress.Start(durationSeconds, easing);
        }

        private static TweenEasing ToTweenEasing(CameraBlendCurve curve)
        {
            return curve switch
            {
                CameraBlendCurve.Cut => TweenEasing.Cut,
                CameraBlendCurve.Linear => TweenEasing.Linear,
                CameraBlendCurve.SmoothStep => TweenEasing.SmoothStep,
                _ => throw new InvalidOperationException(
                    $"Unsupported virtual camera blend curve '{curve}'.")
            };
        }

        private static CameraStateSnapshot FromDefinition(VirtualCameraDefinition definition, CameraStateSnapshot currentState)
        {
            return new CameraStateSnapshot
            {
                TargetCm = definition.TargetSource == VirtualCameraTargetSource.Fixed
                    ? definition.FixedTargetCm
                    : currentState.TargetCm,
                TargetHeightCm = currentState.TargetHeightCm,
                Yaw = definition.Yaw,
                Pitch = definition.Pitch,
                DistanceCm = definition.DistanceCm,
                FovYDeg = definition.FovYDeg,
                RigPivotOffsetCm = definition.RigPivotOffsetCm,
                RigCameraOffsetCm = definition.RigCameraOffsetCm,
                RigKind = definition.RigKind,
                ZoomLevel = currentState.ZoomLevel,
                IsFollowing = false
            };
        }

        private sealed class RuntimeVirtualCamera
        {
            public RuntimeVirtualCamera(VirtualCameraDefinition definition, CameraStateSnapshot runtimeState)
            {
                Definition = definition;
                RuntimeState = runtimeState;
            }

            public VirtualCameraDefinition Definition { get; set; }
            public CameraStateSnapshot RuntimeState;
            public int Priority;
            public ICameraFollowTarget? FollowTarget;
            public CameraTargetTransformSnapshot? ResolvedFollowTargetTransform;
            public bool PendingFollowSnap;
            public long ActivationSequence;
        }
    }
}
