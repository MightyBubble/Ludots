using System;
using System.Numerics;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Manages the active camera state and behavior pipeline.
    /// Acts as the central service for camera logic within the GameSession.
    /// No ECS dependency â€?follow target position is set externally by systems/triggers.
    /// </summary>
    public class CameraManager
    {
        /// <summary>
        /// The current state of the camera (position, rotation, zoom).
        /// </summary>
        public CameraState State { get; private set; } = new CameraState();

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

        private CameraBehaviorContext? _runtimeContext;
        private CompositeCameraController? _controller;
        private bool _pendingFollowSnap;
        private readonly CameraState _preVirtualState = new CameraState();

        public void ConfigureRuntime(PlayerInputHandler input, Presentation.Camera.IViewController view)
        {
            _runtimeContext = new CameraBehaviorContext(input, view);
            if (ActivePreset != null)
            {
                _controller = CameraControllerFactory.FromPreset(ActivePreset, _runtimeContext);
            }
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
            TrySnapToFollowTarget();
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
        /// Updates the camera state using the active Core behavior pipeline.
        /// Should be called once per frame by the GameSession.
        /// </summary>
        public void Update(float dt)
        {
            Vector2? followTargetPosition = ResolveFollowTargetPosition();
            UpdateFollowState(followTargetPosition);

            if (_controller != null && (VirtualCameraBrain == null || VirtualCameraBrain.AllowsInput))
            {
                _controller.Update(State, dt);
            }

            if (VirtualCameraBrain != null)
            {
                CopyState(State, _preVirtualState);
                VirtualCameraBrain.ApplyToState(State, followTargetPosition, dt);
                VirtualCameraBrain.CapturePostControllerState(State);
            }
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
                }
            }
            else
            {
                FollowTargetPositionCm = null;
            }

            if (FollowMode == CameraFollowMode.None)
            {
                State.IsFollowing = false;
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
                return;
            }

            State.TargetCm = followTargetPosition.Value;
            State.IsFollowing = true;
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
            }
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

