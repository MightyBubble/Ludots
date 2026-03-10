using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Tweening;

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
        public bool IsBlending => _blendProgress.IsActive;
        public bool AllowsInput => _resolved != null && _resolved.Definition.AllowUserInput && !IsBlending;
        public string ActiveCameraId => _resolved?.Definition.Id ?? string.Empty;
        public VirtualCameraDefinition? ActiveDefinition => _resolved?.Definition;
        public Vector2? ActiveFollowTargetPositionCm => _resolved?.ResolvedFollowTargetPositionCm;

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
            if (request.Yaw.HasValue) state.Yaw = request.Yaw.Value;
            if (request.Pitch.HasValue) state.Pitch = request.Pitch.Value;
            if (request.DistanceCm.HasValue) state.DistanceCm = request.DistanceCm.Value;
            if (request.FovYDeg.HasValue) state.FovYDeg = request.FovYDeg.Value;
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
            return true;
        }

        public bool TryApplyFollowState(string id, CameraState state, IInputActionReader? input, out Vector2? followTargetPositionCm)
        {
            followTargetPositionCm = null;
            if (state == null)
            {
                return false;
            }

            ResolveActiveCamera();
            if (_resolved == null ||
                !string.Equals(_resolved.Definition.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ResolveRuntimeStates(input);
            followTargetPositionCm = _resolved.ResolvedFollowTargetPositionCm;

            bool shouldFollow = ShouldFollow(_resolved, input);
            if (shouldFollow && followTargetPositionCm.HasValue)
            {
                state.TargetCm = _resolved.RuntimeState.TargetCm;
                state.IsFollowing = true;
            }
            else
            {
                state.IsFollowing = false;
            }

            return true;
        }

        public void ApplyToState(CameraState state, IInputActionReader? input, float dt)
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

            ResolveRuntimeStates(input);
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

        private void ResolveRuntimeStates(IInputActionReader? input)
        {
            foreach (var pair in _active)
            {
                var runtime = pair.Value;
                runtime.ResolvedFollowTargetPositionCm = ResolveFollowTargetPosition(runtime.FollowTarget);

                if (runtime.ResolvedFollowTargetPositionCm.HasValue && runtime.PendingFollowSnap)
                {
                    runtime.RuntimeState.TargetCm = runtime.ResolvedFollowTargetPositionCm.Value;
                    runtime.PendingFollowSnap = false;
                }

                bool shouldFollow = ShouldFollow(runtime, input);
                if (shouldFollow && runtime.ResolvedFollowTargetPositionCm.HasValue)
                {
                    runtime.RuntimeState.TargetCm = runtime.ResolvedFollowTargetPositionCm.Value;
                    runtime.RuntimeState.IsFollowing = true;
                }
                else
                {
                    runtime.RuntimeState.IsFollowing = false;
                }
            }
        }

        private bool ShouldFollow(RuntimeVirtualCamera runtime, IInputActionReader? input)
        {
            if (runtime.ResolvedFollowTargetPositionCm == null)
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
                    && input != null
                    && input.ReadAction<bool>(runtime.Definition.FollowActionId),
                _ => false
            };
        }

        private static Vector2? ResolveFollowTargetPosition(ICameraFollowTarget? followTarget)
        {
            if (followTarget != null && followTarget.TryGetPosition(out var resolved))
            {
                return resolved;
            }

            return null;
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

            ResolveRuntimeStates(input: null);
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
                _ => TweenEasing.Linear
            };
        }

        private static CameraStateSnapshot FromDefinition(VirtualCameraDefinition definition, CameraStateSnapshot currentState)
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
            public Vector2? ResolvedFollowTargetPositionCm;
            public bool PendingFollowSnap;
            public long ActivationSequence;
        }
    }
}
