using System;
using System.Numerics;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Manages the authoritative logic camera state.
    /// Camera logic advances on fixed-step ticks; render systems interpolate between PreviousState and State.
    /// </summary>
    public class CameraManager
    {
        private readonly CameraInputAccumulator _pendingInput = new();
        private readonly FrozenInputActionReader _logicInput = new();
        private readonly CameraState _preVirtualState = new();

        private PlayerInputHandler? _liveInput;
        private CameraBehaviorContext? _runtimeContext;
        private CompositeCameraController? _controller;
        private bool _pendingFollowSnap;
        private long _lastCapturedInputRevision = -1;

        /// <summary>
        /// The current fixed-step logic state of the camera.
        /// </summary>
        public CameraState State { get; } = new();

        /// <summary>
        /// The previous fixed-step logic state of the camera.
        /// Presentation systems interpolate between PreviousState and State.
        /// </summary>
        public CameraState PreviousState { get; } = new();

        public CameraPreset? ActivePreset { get; private set; }

        public bool IsRuntimeConfigured => _runtimeContext != null;

        /// <summary>
        /// Camera follow mode from the active preset.
        /// </summary>
        public CameraFollowMode FollowMode { get; set; }

        public string FollowActionId { get; private set; } = "CameraLock";

        /// <summary>
        /// World position (cm) of the follow target, set externally each frame.
        /// Null means no valid follow target.
        /// </summary>
        public Vector2? FollowTargetPositionCm { get; set; }

        public ICameraFollowTarget? FollowTarget { get; private set; }

        public VirtualCameraBrain? VirtualCameraBrain { get; private set; }

        public CameraManager()
        {
            CopyState(State, PreviousState);
        }

        public void ConfigureRuntime(PlayerInputHandler input, Presentation.Camera.IViewController view)
        {
            _liveInput = input ?? throw new ArgumentNullException(nameof(input));
            _runtimeContext = new CameraBehaviorContext(_logicInput, view ?? throw new ArgumentNullException(nameof(view)));
            _controller = ActivePreset != null ? CameraControllerFactory.FromPreset(ActivePreset, _runtimeContext) : null;
            ResetInputTracking();
            CaptureVisualInput();
            CopyState(State, PreviousState);
        }

        public void SetVirtualCameraRegistry(VirtualCameraRegistry registry)
        {
            VirtualCameraBrain = new VirtualCameraBrain(registry);
        }

        public void ApplyPreset(CameraPreset preset, ICameraFollowTarget? followTarget = null, bool snapToFollowTargetWhenAvailable = true)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));

            ActivePreset = preset;
            State.RigKind = preset.RigKind;
            State.DistanceCm = preset.DistanceCm;
            State.Pitch = preset.Pitch;
            State.FovYDeg = preset.FovYDeg;
            State.Yaw = preset.Yaw;
            FollowMode = preset.FollowMode;
            FollowActionId = string.IsNullOrWhiteSpace(preset.FollowActionId) ? "CameraLock" : preset.FollowActionId;
            FollowTarget = followTarget;
            _pendingFollowSnap = snapToFollowTargetWhenAvailable;
            _controller = _runtimeContext != null ? CameraControllerFactory.FromPreset(preset, _runtimeContext) : null;
            ResetInputTracking();
            TrySnapToFollowTarget();
            SyncVirtualBaseState();
        }

        public void ApplyPose(CameraPoseRequest? request)
        {
            if (request == null)
            {
                return;
            }

            if (request.TargetCm.HasValue) State.TargetCm = request.TargetCm.Value;
            if (request.Yaw.HasValue) State.Yaw = request.Yaw.Value;
            if (request.Pitch.HasValue) State.Pitch = request.Pitch.Value;
            if (request.DistanceCm.HasValue) State.DistanceCm = request.DistanceCm.Value;
            if (request.FovYDeg.HasValue) State.FovYDeg = request.FovYDeg.Value;
            SyncVirtualBaseState();
        }

        public void SetFollowTarget(ICameraFollowTarget? followTarget, bool snapToFollowTargetWhenAvailable = true)
        {
            FollowTarget = followTarget;
            _pendingFollowSnap = snapToFollowTargetWhenAvailable;
            TrySnapToFollowTarget();
        }

        public void ActivateVirtualCamera(string id, float? blendDurationSeconds = null)
        {
            if (VirtualCameraBrain == null) throw new InvalidOperationException("VirtualCameraRegistry is not configured.");
            CopyState(State, _preVirtualState);
            VirtualCameraBrain.Activate(id, State, blendDurationSeconds);
        }

        public void ClearVirtualCamera()
        {
            if (VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                return;
            }

            VirtualCameraBrain.Clear();
            CopyState(_preVirtualState, State);
        }

        /// <summary>
        /// Captures the latest visual-frame input sample.
        /// This should run once per render-frame after PlayerInputHandler.Update().
        /// </summary>
        public void CaptureVisualInput()
        {
            CaptureVisualInput(force: false);
        }

        /// <summary>
        /// Advances the authoritative camera logic by one fixed-step tick.
        /// </summary>
        public void Update(float dt)
        {
            CaptureVisualInput(force: false);
            _pendingInput.BuildTickSnapshot(_logicInput);
            CopyState(State, PreviousState);

            Vector2? followTargetPosition = ResolveFollowTargetPosition();
            UpdateFollowState(followTargetPosition);

            if (_controller != null && (VirtualCameraBrain == null || VirtualCameraBrain.AllowsInput))
            {
                _controller.Update(State, dt);
            }

            if (VirtualCameraBrain != null)
            {
                if (!VirtualCameraBrain.HasActiveCamera || VirtualCameraBrain.AllowsInput)
                {
                    CopyState(State, _preVirtualState);
                }

                VirtualCameraBrain.ApplyToState(State, followTargetPosition, dt);
                VirtualCameraBrain.CapturePostControllerState(State);
            }
        }

        public CameraStateSnapshot GetInterpolatedState(float alpha)
        {
            alpha = Math.Clamp(alpha, 0f, 1f);
            var previous = CameraStateSnapshot.FromState(PreviousState);
            var current = CameraStateSnapshot.FromState(State);
            return CameraStateSnapshot.Lerp(previous, current, alpha);
        }

        private void CaptureVisualInput(bool force)
        {
            if (_liveInput == null)
            {
                return;
            }

            if (!force && _liveInput.UpdateRevision == _lastCapturedInputRevision)
            {
                return;
            }

            _lastCapturedInputRevision = _liveInput.UpdateRevision;

            if (ActivePreset != null)
            {
                var preset = ActivePreset;
                _pendingInput.CaptureContinuous(preset.MoveActionId, _liveInput.ReadAction<Vector2>(preset.MoveActionId));
                _pendingInput.AccumulateOneShot(preset.ZoomActionId, _liveInput.ReadAction<float>(preset.ZoomActionId));
                _pendingInput.CaptureContinuous(preset.PointerPosActionId, _liveInput.ReadAction<Vector2>(preset.PointerPosActionId));
                _pendingInput.CaptureContinuous(preset.RotateHoldActionId, _liveInput.ReadAction<bool>(preset.RotateHoldActionId));
                _pendingInput.CaptureContinuous(preset.RotateLeftActionId, _liveInput.ReadAction<bool>(preset.RotateLeftActionId));
                _pendingInput.CaptureContinuous(preset.RotateRightActionId, _liveInput.ReadAction<bool>(preset.RotateRightActionId));
                _pendingInput.CaptureContinuous(preset.GrabDragHoldActionId, _liveInput.ReadAction<bool>(preset.GrabDragHoldActionId));
            }

            _pendingInput.CaptureContinuous(FollowActionId, _liveInput.ReadAction<bool>(FollowActionId));
        }

        private void UpdateFollowState(Vector2? followTargetPosition)
        {
            if (followTargetPosition.HasValue)
            {
                FollowTargetPositionCm = followTargetPosition.Value;
                if (_pendingFollowSnap)
                {
                    State.TargetCm = followTargetPosition.Value;
                    _pendingFollowSnap = false;
                    SyncVirtualBaseFollowState(targetCm: followTargetPosition.Value, isFollowing: false);
                }
            }
            else
            {
                FollowTargetPositionCm = null;
            }

            if (FollowMode == CameraFollowMode.None)
            {
                State.IsFollowing = false;
                SyncVirtualBaseFollowState(isFollowing: false);
                return;
            }

            bool shouldFollow = FollowMode == CameraFollowMode.AlwaysFollow;
            if (!shouldFollow && _runtimeContext != null)
            {
                shouldFollow = _runtimeContext.Input.ReadAction<bool>(FollowActionId);
            }

            if (!shouldFollow || !followTargetPosition.HasValue)
            {
                State.IsFollowing = false;
                SyncVirtualBaseFollowState(isFollowing: false);
                return;
            }

            State.TargetCm = followTargetPosition.Value;
            State.IsFollowing = true;
            SyncVirtualBaseFollowState(targetCm: followTargetPosition.Value, isFollowing: true);
        }

        private Vector2? ResolveFollowTargetPosition()
        {
            if (FollowTarget != null && FollowTarget.TryGetPosition(out var resolved))
            {
                return resolved;
            }

            return FollowTargetPositionCm;
        }

        private void TrySnapToFollowTarget()
        {
            if (!_pendingFollowSnap)
            {
                return;
            }

            if (FollowTarget != null && FollowTarget.TryGetPosition(out var resolved))
            {
                State.TargetCm = resolved;
                FollowTargetPositionCm = resolved;
                _pendingFollowSnap = false;
                SyncVirtualBaseState();
            }
        }

        private void SyncVirtualBaseState()
        {
            if (VirtualCameraBrain != null && VirtualCameraBrain.HasActiveCamera)
            {
                CopyState(State, _preVirtualState);
            }
        }

        private void SyncVirtualBaseFollowState(Vector2 targetCm, bool isFollowing)
        {
            if (VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                return;
            }

            _preVirtualState.TargetCm = targetCm;
            _preVirtualState.IsFollowing = isFollowing;
        }

        private void SyncVirtualBaseFollowState(bool isFollowing)
        {
            if (VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                return;
            }

            _preVirtualState.IsFollowing = isFollowing;
        }

        private void ResetInputTracking()
        {
            _pendingInput.Clear();
            _logicInput.Clear();
            _lastCapturedInputRevision = -1;
        }

        private static void CopyState(CameraState source, CameraState destination)
        {
            destination.TargetCm = source.TargetCm;
            destination.Yaw = source.Yaw;
            destination.Pitch = source.Pitch;
            destination.DistanceCm = source.DistanceCm;
            destination.RigKind = source.RigKind;
            destination.ZoomLevel = source.ZoomLevel;
            destination.FovYDeg = source.FovYDeg;
            destination.IsFollowing = source.IsFollowing;
        }
    }
}
