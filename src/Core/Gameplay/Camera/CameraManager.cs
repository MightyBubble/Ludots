using System;
using System.Numerics;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;

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
        private PlatformManagedCameraDriverRegistry? _platformManagedCameraDrivers;
        private string _controllerCameraId = string.Empty;
        private long _lastCapturedInputRevision = -1;
        private Func<WorldAabbCm>? _targetBoundsProvider;
        private Func<IVisualHeightmap?>? _visualHeightmapProvider;
        private bool _userInputSuppressed;

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

        public void ConfigureRuntime(
            PlayerInputHandler input,
            Presentation.Camera.IViewController view,
            Func<WorldAabbCm>? targetBoundsProvider = null,
            Func<IVisualHeightmap?>? visualHeightmapProvider = null)
        {
            _liveInput = input ?? throw new ArgumentNullException(nameof(input));
            _runtimeContext = new CameraBehaviorContext(_logicInput, view ?? throw new ArgumentNullException(nameof(view)));
            _targetBoundsProvider = targetBoundsProvider;
            _visualHeightmapProvider = visualHeightmapProvider;
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

        public void SetPlatformManagedCameraDriverRegistry(PlatformManagedCameraDriverRegistry registry)
        {
            _platformManagedCameraDrivers = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Suppresses controller-driven user input for the current runtime boundary.
        /// Virtual camera follow/blend logic still advances normally.
        /// </summary>
        public void SetUserInputSuppressed(bool suppressed)
        {
            _userInputSuppressed = suppressed;
            if (suppressed)
            {
                _pendingInput.Clear();
                _logicInput.Clear();
            }
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

        public void SynchronizeActiveVirtualCameraBoundsAndHeight()
        {
            if (VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                return;
            }

            ApplyActiveVirtualCameraBoundsAndHeight();
            VirtualCameraBrain.ApplyToState(State, _logicInput, 0f);
            CopyState(State, PreviousState);
            FollowTargetPositionCm = VirtualCameraBrain.ActiveFollowTargetPositionCm;
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

            bool updated = VirtualCameraBrain.SetFollowTarget(virtualCameraId, followTarget, snapToFollowTargetWhenAvailable);
            if (updated &&
                string.Equals(VirtualCameraBrain.ActiveCameraId, virtualCameraId, StringComparison.OrdinalIgnoreCase))
            {
                FollowTargetPositionCm = VirtualCameraBrain.ActiveFollowTargetPositionCm;
            }

            return updated;
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

            ApplyActiveVirtualCameraBoundsAndHeight();
            VirtualCameraBrain.ApplyToState(State, _logicInput, dt);
            var activeDefinition = VirtualCameraBrain.ActiveDefinition;
            bool allowsUserInput = VirtualCameraBrain.AllowsInput && !_userInputSuppressed;

            bool runtimeStateNeedsCapture = false;
            if (activeDefinition != null && activeDefinition.ControlMode == VirtualCameraControlMode.PlatformManaged)
            {
                InvalidateController();
                runtimeStateNeedsCapture = UpdatePlatformManagedCamera(activeDefinition, dt, allowsUserInput);
            }
            else
            {
                EnsureController();
                if (_controller != null && allowsUserInput)
                {
                    _controller.Update(State, dt);
                    runtimeStateNeedsCapture = true;
                }
            }

            if (runtimeStateNeedsCapture && VirtualCameraBrain.AllowsInput)
            {
                ApplyWorldBoundsConfineToState(activeDefinition);
                ApplyTargetHeightToState(activeDefinition);
                VirtualCameraBrain.ApplyPose(new CameraPoseRequest
                {
                    VirtualCameraId = activeDefinition?.Id ?? string.Empty,
                    TargetCm = State.TargetCm,
                    TargetHeightCm = State.TargetHeightCm
                });
                VirtualCameraBrain.CapturePostControllerState(State);
            }

            FollowTargetPositionCm = VirtualCameraBrain.ActiveFollowTargetPositionCm;
        }

        private bool UpdatePlatformManagedCamera(VirtualCameraDefinition definition, float dt, bool allowsUserInput)
        {
            if (_platformManagedCameraDrivers == null)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' requires platform-managed driver '{definition.PlatformDriverId}', but no driver registry is configured.");
            }

            if (string.IsNullOrWhiteSpace(definition.PlatformDriverId))
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' is marked PlatformManaged but did not declare PlatformDriverId.");
            }

            IPlatformManagedCameraDriver driver = _platformManagedCameraDrivers.Get(definition.PlatformDriverId);
            driver.PrimeDefinition(definition);
            return driver.Update(new PlatformManagedCameraUpdateContext(
                definition,
                State,
                _logicInput,
                dt,
                allowsUserInput));
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
                _pendingInput.Clear();
                return;
            }

            if (_userInputSuppressed)
            {
                _pendingInput.Clear();
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

        private bool TryResolveWorldBoundsConfine(
            VirtualCameraDefinition? definition,
            Vector2 targetCm,
            out Vector2 clamped)
        {
            clamped = targetCm;
            if (definition == null ||
                !definition.ConfineTargetToWorldBounds ||
                _targetBoundsProvider == null)
            {
                return false;
            }

            WorldAabbCm bounds = ExpandBounds(_targetBoundsProvider(), definition.ConfinePaddingCm);
            clamped = new Vector2(
                Math.Clamp(targetCm.X, bounds.Left, bounds.Right),
                Math.Clamp(targetCm.Y, bounds.Top, bounds.Bottom));

            return Vector2.DistanceSquared(clamped, targetCm) > 0.0001f;
        }

        private bool ApplyWorldBoundsConfineToState(VirtualCameraDefinition? definition)
        {
            if (!TryResolveWorldBoundsConfine(definition, State.TargetCm, out Vector2 clamped))
            {
                return false;
            }

            State.TargetCm = clamped;
            return true;
        }

        private void ApplyTargetHeightToState(VirtualCameraDefinition? definition)
        {
            State.TargetHeightCm = ResolveTargetHeight(definition, State.TargetCm);
        }

        private void ApplyActiveVirtualCameraBoundsAndHeight()
        {
            if (VirtualCameraBrain == null ||
                !VirtualCameraBrain.TryGetActiveRuntimeTarget(_logicInput, out var definition, out Vector2 targetCm))
            {
                return;
            }

            if (TryResolveWorldBoundsConfine(definition, targetCm, out Vector2 clampedTargetCm))
            {
                targetCm = clampedTargetCm;
                VirtualCameraBrain.SetActiveRuntimeTarget(targetCm);
            }

            float targetHeightCm = ResolveTargetHeight(definition, targetCm);
            VirtualCameraBrain.SetActiveRuntimeTargetHeight(targetHeightCm);
        }

        private float ResolveTargetHeight(VirtualCameraDefinition? definition, Vector2 targetCm)
        {
            if (definition == null)
            {
                return 0f;
            }

            float targetHeightCm = definition.TargetHeightMode switch
            {
                VirtualCameraTargetHeightMode.Flat => definition.TargetHeightOffsetCm,
                VirtualCameraTargetHeightMode.VisualHeightmap => SampleRequiredVisualHeightmapHeight(definition, targetCm) + definition.TargetHeightOffsetCm,
                _ => throw new InvalidOperationException($"Virtual camera '{definition.Id}' declares unsupported target height mode '{definition.TargetHeightMode}'."),
            };

            if (!float.IsFinite(targetHeightCm))
            {
                throw new InvalidOperationException($"Virtual camera '{definition.Id}' resolved a non-finite target height.");
            }

            return targetHeightCm;
        }

        private float SampleRequiredVisualHeightmapHeight(VirtualCameraDefinition definition, Vector2 targetCm)
        {
            IVisualHeightmap? heightmap = _visualHeightmapProvider?.Invoke();
            if (heightmap == null)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' requires CoreServiceKeys.VisualHeightmap for target height, but no focused map visual heightmap service is bound.");
            }

            if (!heightmap.TrySampleHeightCm(
                    targetCm.X,
                    targetCm.Y,
                    out float heightCm,
                    definition.TargetHeightLayerIndex))
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' could not sample VisualHeightmap target height at ({targetCm.X}, {targetCm.Y}) cm on layer {definition.TargetHeightLayerIndex}.");
            }

            return heightCm;
        }

        private static WorldAabbCm ExpandBounds(WorldAabbCm bounds, float paddingCm)
        {
            int padding = (int)MathF.Ceiling(MathF.Max(0f, paddingCm));
            return new WorldAabbCm(
                bounds.X - padding,
                bounds.Y - padding,
                bounds.Width + (padding * 2),
                bounds.Height + (padding * 2));
        }

        private static void ApplyPoseToState(CameraState state, CameraPoseRequest request)
        {
            if (request.TargetCm.HasValue) state.TargetCm = request.TargetCm.Value;
            if (request.TargetHeightCm.HasValue) state.TargetHeightCm = request.TargetHeightCm.Value;
            if (request.Yaw.HasValue) state.Yaw = request.Yaw.Value;
            if (request.Pitch.HasValue) state.Pitch = request.Pitch.Value;
            if (request.DistanceCm.HasValue) state.DistanceCm = request.DistanceCm.Value;
            if (request.FovYDeg.HasValue) state.FovYDeg = request.FovYDeg.Value;
        }

        private static void CopyState(CameraState source, CameraState destination)
        {
            destination.TargetCm = source.TargetCm;
            destination.TargetHeightCm = source.TargetHeightCm;
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
