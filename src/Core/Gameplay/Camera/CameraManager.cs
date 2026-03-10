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

        private PlayerInputHandler? _liveInput;
        private CameraBehaviorContext? _runtimeContext;
        private CompositeCameraController? _controller;
        private string _controllerCameraId = string.Empty;
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

        public bool IsRuntimeConfigured => _runtimeContext != null;

        /// <summary>
        /// World position (cm) of the authoritative follow target for the active virtual camera.
        /// Null means no valid follow target.
        /// </summary>
        public Vector2? FollowTargetPositionCm { get; private set; }

        public VirtualCameraBrain? VirtualCameraBrain { get; private set; }

        public CameraManager()
        {
            CopyState(State, PreviousState);
        }

        public void ConfigureRuntime(PlayerInputHandler input, Presentation.Camera.IViewController view)
        {
            _liveInput = input ?? throw new ArgumentNullException(nameof(input));
            _runtimeContext = new CameraBehaviorContext(_logicInput, view ?? throw new ArgumentNullException(nameof(view)));
            InvalidateController();
            ResetInputTracking();
            CaptureVisualInput(force: true);
            CopyState(State, PreviousState);
        }

        public void SetVirtualCameraRegistry(VirtualCameraRegistry registry)
        {
            VirtualCameraBrain = new VirtualCameraBrain(registry);
            InvalidateController();
            FollowTargetPositionCm = null;
        }

        public bool IsVirtualCameraActive(string id)
        {
            return VirtualCameraBrain != null && VirtualCameraBrain.IsActive(id);
        }

        public void ApplyPose(CameraPoseRequest? request)
        {
            if (request == null)
            {
                return;
            }

            if (VirtualCameraBrain != null && VirtualCameraBrain.ApplyPose(request))
            {
                return;
            }

            ApplyPoseToState(State, request);
        }

        public void ActivateVirtualCamera(
            string id,
            float? blendDurationSeconds = null,
            int? priorityOverride = null,
            ICameraFollowTarget? followTarget = null,
            bool snapToFollowTargetWhenAvailable = true,
            bool resetRuntimeState = true)
        {
            if (VirtualCameraBrain == null) throw new InvalidOperationException("VirtualCameraRegistry is not configured.");

            VirtualCameraBrain.Activate(
                id,
                State,
                blendDurationSeconds,
                priorityOverride,
                followTarget,
                snapToFollowTargetWhenAvailable,
                resetRuntimeState);

            ResetInputTracking();
            InvalidateController();
        }

        public bool DeactivateVirtualCamera(string id, float? blendDurationSeconds = null)
        {
            if (VirtualCameraBrain == null)
            {
                return false;
            }

            bool removed = VirtualCameraBrain.Deactivate(id, State, blendDurationSeconds);
            if (removed)
            {
                ResetInputTracking();
                InvalidateController();
                FollowTargetPositionCm = VirtualCameraBrain.ActiveFollowTargetPositionCm;
            }

            return removed;
        }

        public void ClearVirtualCamera()
        {
            if (VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                return;
            }

            DeactivateVirtualCamera(VirtualCameraBrain.ActiveCameraId);
        }

        public void ResetVirtualCameras()
        {
            if (VirtualCameraBrain == null)
            {
                return;
            }

            VirtualCameraBrain.ClearAll();
            ResetInputTracking();
            InvalidateController();
            FollowTargetPositionCm = null;
        }

        public bool SetFollowTarget(string virtualCameraId, ICameraFollowTarget? followTarget, bool snapToFollowTargetWhenAvailable = true)
        {
            if (VirtualCameraBrain == null)
            {
                return false;
            }

            return VirtualCameraBrain.SetFollowTarget(virtualCameraId, followTarget, snapToFollowTargetWhenAvailable);
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

            if (VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                FollowTargetPositionCm = null;
                return;
            }

            VirtualCameraBrain.ApplyToState(State, _logicInput, dt);
            EnsureController();

            if (_controller != null && VirtualCameraBrain.AllowsInput)
            {
                _controller.Update(State, dt);
                VirtualCameraBrain.CapturePostControllerState(State);
            }

            FollowTargetPositionCm = VirtualCameraBrain.ActiveFollowTargetPositionCm;
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
            if (_liveInput == null || VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                return;
            }

            if (!force && _liveInput.UpdateRevision == _lastCapturedInputRevision)
            {
                return;
            }

            _lastCapturedInputRevision = _liveInput.UpdateRevision;

            if (!VirtualCameraBrain.AllowsInput)
            {
                return;
            }

            var definition = VirtualCameraBrain.ActiveDefinition;
            if (definition == null)
            {
                return;
            }

            _pendingInput.CaptureContinuous(definition.MoveActionId, _liveInput.ReadAction<Vector2>(definition.MoveActionId));
            _pendingInput.AccumulateOneShot(definition.ZoomActionId, _liveInput.ReadAction<float>(definition.ZoomActionId));
            _pendingInput.CaptureContinuous(definition.PointerPosActionId, _liveInput.ReadAction<Vector2>(definition.PointerPosActionId));
            _pendingInput.AccumulateOneShot(definition.PointerDeltaActionId, _liveInput.ReadAction<Vector2>(definition.PointerDeltaActionId));
            _pendingInput.AccumulateOneShot(definition.LookActionId, _liveInput.ReadAction<Vector2>(definition.LookActionId));
            _pendingInput.CaptureContinuous(definition.RotateHoldActionId, _liveInput.ReadAction<bool>(definition.RotateHoldActionId));
            _pendingInput.CaptureContinuous(definition.RotateLeftActionId, _liveInput.ReadAction<bool>(definition.RotateLeftActionId));
            _pendingInput.CaptureContinuous(definition.RotateRightActionId, _liveInput.ReadAction<bool>(definition.RotateRightActionId));
            _pendingInput.CaptureContinuous(definition.GrabDragHoldActionId, _liveInput.ReadAction<bool>(definition.GrabDragHoldActionId));
            _pendingInput.CaptureContinuous(definition.FollowActionId, _liveInput.ReadAction<bool>(definition.FollowActionId));
        }

        private void EnsureController()
        {
            if (_runtimeContext == null || VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                InvalidateController();
                return;
            }

            var definition = VirtualCameraBrain.ActiveDefinition;
            if (definition == null)
            {
                InvalidateController();
                return;
            }

            if (_controller != null && string.Equals(_controllerCameraId, definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _controller = CameraControllerFactory.FromDefinition(definition, _runtimeContext);
            _controllerCameraId = definition.Id;
        }

        private void InvalidateController()
        {
            _controller = null;
            _controllerCameraId = string.Empty;
        }

        private void ResetInputTracking()
        {
            _pendingInput.Clear();
            _logicInput.Clear();
            _lastCapturedInputRevision = -1;
        }

        private static void ApplyPoseToState(CameraState state, CameraPoseRequest request)
        {
            if (request.TargetCm.HasValue) state.TargetCm = request.TargetCm.Value;
            if (request.Yaw.HasValue) state.Yaw = request.Yaw.Value;
            if (request.Pitch.HasValue) state.Pitch = request.Pitch.Value;
            if (request.DistanceCm.HasValue) state.DistanceCm = request.DistanceCm.Value;
            if (request.FovYDeg.HasValue) state.FovYDeg = request.FovYDeg.Value;
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
