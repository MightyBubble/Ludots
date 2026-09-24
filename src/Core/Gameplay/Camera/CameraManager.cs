using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.StructureCollision;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Manages the authoritative logic camera state.
    /// Camera logic advances on fixed-step ticks; render systems interpolate between PreviousState and State.
    /// </summary>
    public class CameraManager
    {
        private const int VisualHeightmapFootprintConfinePasses = 3;
        private const float TargetConfineEpsilonSq = 0.0001f;

        private CameraBehaviorInputState? _behaviorInput;
        private CameraBehaviorContext? _runtimeContext;
        private CompositeCameraController? _controller;
        private PlatformManagedCameraDriverRegistry? _platformManagedCameraDrivers;
        private CameraImpulseRuntime? _impulseRuntime;
        private string _controllerCameraId = string.Empty;
        private Func<WorldAabbCm>? _targetBoundsProvider;
        private Func<IVisualHeightmap?>? _visualHeightmapProvider;
        private Func<StructureCollisionAsset?>? _structureCollisionProvider;
        private Func<StructureCollisionRuntimeState?>? _structureCollisionRuntimeProvider;

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
            CameraBehaviorInputState behaviorInput,
            Presentation.Camera.IViewController view,
            Func<WorldAabbCm>? targetBoundsProvider = null,
            Func<IVisualHeightmap?>? visualHeightmapProvider = null,
            Func<StructureCollisionAsset?>? structureCollisionProvider = null,
            Func<StructureCollisionRuntimeState?>? structureCollisionRuntimeProvider = null)
        {
            _behaviorInput = behaviorInput ?? throw new ArgumentNullException(nameof(behaviorInput));
            _runtimeContext = new CameraBehaviorContext(_behaviorInput, view ?? throw new ArgumentNullException(nameof(view)));
            _targetBoundsProvider = targetBoundsProvider;
            _visualHeightmapProvider = visualHeightmapProvider;
            _structureCollisionProvider = structureCollisionProvider;
            _structureCollisionRuntimeProvider = structureCollisionRuntimeProvider;
            InvalidateController();
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

        public void SetImpulseRuntime(CameraImpulseRuntime runtime)
        {
            _impulseRuntime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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

            ClearCameraCollisionState();
            ApplyActiveVirtualCameraBoundsAndHeight();
            VirtualCameraBrain.ApplyToState(State, _behaviorInput, 0f);
            ApplyWorldBoundsAndHeightToState(VirtualCameraBrain.ActiveDefinition);
            ApplyCameraCollisionState(VirtualCameraBrain.ActiveDefinition);
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
        /// Advances the authoritative camera logic by one fixed-step tick.
        /// </summary>
        public void Update(float dt)
        {
            CopyState(State, PreviousState);
            ClearCameraCollisionState();

            if (VirtualCameraBrain == null || !VirtualCameraBrain.HasActiveCamera)
            {
                FollowTargetPositionCm = null;
                ClearImpulseState();
                ClearCameraCollisionState();
                return;
            }

            ApplyActiveVirtualCameraBoundsAndHeight();
            VirtualCameraBrain.ApplyToState(State, _behaviorInput, dt);
            var activeDefinition = VirtualCameraBrain.ActiveDefinition;
            ApplyWorldBoundsAndHeightToState(activeDefinition);
            bool allowsUserInput = VirtualCameraBrain.AllowsInput;

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
                ApplyWorldBoundsAndHeightToState(activeDefinition);
                VirtualCameraBrain.ApplyPose(new CameraPoseRequest
                {
                    VirtualCameraId = activeDefinition?.Id ?? string.Empty,
                    TargetCm = State.TargetCm,
                    TargetHeightCm = State.TargetHeightCm
                });
                VirtualCameraBrain.CapturePostControllerState(State);
            }

            ApplyImpulseState(dt);
            ApplyCameraCollisionState(activeDefinition);
            FollowTargetPositionCm = VirtualCameraBrain.ActiveFollowTargetPositionCm;
        }

        private void ApplyImpulseState(float dt)
        {
            ClearImpulseState();
            if (_impulseRuntime == null)
            {
                return;
            }

            CameraImpulseSample sample = _impulseRuntime.Sample(
                new CameraImpulseListener(State.TargetCm, State.TargetHeightCm, State.Yaw),
                dt);
            State.ImpulsePositionOffsetCm = sample.PositionOffsetCm;
            State.ImpulseYawOffsetDeg = sample.YawOffsetDeg;
            State.ImpulsePitchOffsetDeg = sample.PitchOffsetDeg;
        }

        private void ClearImpulseState()
        {
            State.ImpulsePositionOffsetCm = Vector3.Zero;
            State.ImpulseYawOffsetDeg = 0f;
            State.ImpulsePitchOffsetDeg = 0f;
        }

        private void ClearCameraCollisionState()
        {
            State.CameraCollisionPositionOffsetCm = Vector3.Zero;
            State.CameraCollisionCorrectionCm = 0f;
        }

        private void ApplyCameraCollisionState(VirtualCameraDefinition? definition)
        {
            if (definition == null ||
                (!definition.ConfineCameraToWorldBounds &&
                 !definition.AvoidCameraStructureObstruction &&
                 !definition.AvoidCameraGroundPenetration))
            {
                ClearCameraCollisionState();
                return;
            }

            ClearCameraCollisionState();
            CameraRenderState3D render = CameraViewportUtil.StateToRenderState(State);
            Vector3 desiredPosition = render.Position;
            Vector3 adjustedPosition = desiredPosition;

            if (definition.ConfineCameraToWorldBounds)
            {
                adjustedPosition = ResolveCameraWorldBoundsConfine(definition, adjustedPosition);
            }

            if (definition.AvoidCameraStructureObstruction)
            {
                adjustedPosition = ResolveStructureObstruction(definition, render.Target, adjustedPosition);
            }

            if (definition.AvoidCameraGroundPenetration)
            {
                adjustedPosition = ResolveGroundClearance(definition, adjustedPosition);
            }

            Vector3 adjustmentMeters = adjustedPosition - desiredPosition;
            if (adjustmentMeters.LengthSquared() <= 0.0000001f)
            {
                ClearCameraCollisionState();
                return;
            }

            State.CameraCollisionPositionOffsetCm = WorldUnits.MToCm(adjustmentMeters);
            State.CameraCollisionCorrectionCm = Vector3.Distance(desiredPosition, adjustedPosition) * WorldUnits.CmPerMeter;
        }

        private Vector3 ResolveCameraWorldBoundsConfine(VirtualCameraDefinition definition, Vector3 positionMeters)
        {
            if (_targetBoundsProvider == null)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' enables camera world confine, but no target bounds provider is configured.");
            }

            WorldAabbCm bounds = _targetBoundsProvider();
            Vector2 positionCm = WorldPlane2D.VisualMetersToLogicCm(in positionMeters);
            float clampedX = Math.Clamp(positionCm.X, bounds.Left, bounds.Right);
            float clampedY = Math.Clamp(positionCm.Y, bounds.Top, bounds.Bottom);
            if (MathF.Abs(clampedX - positionCm.X) <= 0.001f &&
                MathF.Abs(clampedY - positionCm.Y) <= 0.001f)
            {
                return positionMeters;
            }

            return new Vector3(
                WorldUnits.CmToM(clampedX),
                positionMeters.Y,
                WorldUnits.CmToM(clampedY));
        }

        private Vector3 ResolveGroundClearance(VirtualCameraDefinition definition, Vector3 positionMeters)
        {
            IVisualHeightmap heightmap = RequireVisualHeightmap(definition);
            Vector2 positionCm = WorldPlane2D.VisualMetersToLogicCm(in positionMeters);
            if (!heightmap.TrySampleHeightCm(
                    positionCm.X,
                    positionCm.Y,
                    out float groundHeightCm,
                    definition.TargetHeightLayerIndex))
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' could not sample VisualHeightmap camera ground clearance at ({positionCm.X}, {positionCm.Y}) cm on layer {definition.TargetHeightLayerIndex}.");
            }

            float minHeightMeters = WorldUnits.CmToM(groundHeightCm + definition.CameraGroundClearanceCm);
            if (positionMeters.Y >= minHeightMeters)
            {
                return positionMeters;
            }

            return new Vector3(positionMeters.X, minHeightMeters, positionMeters.Z);
        }

        private Vector3 ResolveStructureObstruction(
            VirtualCameraDefinition definition,
            Vector3 targetMeters,
            Vector3 desiredPositionMeters)
        {
            StructureCollisionAsset asset = RequireStructureCollisionAsset(definition);
            StructureCollisionRuntimeState? runtimeState = _structureCollisionRuntimeProvider?.Invoke();
            Vector3 boom = desiredPositionMeters - targetMeters;
            float boomLengthMeters = boom.Length();
            if (!float.IsFinite(boomLengthMeters) || boomLengthMeters <= 0.0001f)
            {
                return desiredPositionMeters;
            }

            Vector3 direction = boom / boomLengthMeters;
            float stepMeters = WorldUnits.CmToM(definition.CameraObstructionProbeStepCm);
            float clearanceMeters = WorldUnits.CmToM(definition.CameraObstructionClearanceCm);
            float targetRadiusMeters = WorldUnits.CmToM(definition.CameraObstructionTargetRadiusCm);
            float startMeters = Math.Clamp(targetRadiusMeters, 0f, boomLengthMeters);

            for (float distanceMeters = startMeters; distanceMeters <= boomLengthMeters; distanceMeters += stepMeters)
            {
                Vector3 sample = targetMeters + (direction * distanceMeters);
                if (!IsStructureObstructionPoint(asset, runtimeState, sample))
                {
                    continue;
                }

                float resolvedDistance = Math.Clamp(distanceMeters - clearanceMeters, startMeters, boomLengthMeters);
                return targetMeters + (direction * resolvedDistance);
            }

            Vector3 finalSample = desiredPositionMeters;
            if (IsStructureObstructionPoint(asset, runtimeState, finalSample))
            {
                float resolvedDistance = Math.Clamp(boomLengthMeters - clearanceMeters, startMeters, boomLengthMeters);
                return targetMeters + (direction * resolvedDistance);
            }

            return desiredPositionMeters;
        }

        private static bool IsStructureObstructionPoint(
            StructureCollisionAsset asset,
            StructureCollisionRuntimeState? runtimeState,
            Vector3 pointMeters)
        {
            Vector2 pointCm = WorldPlane2D.VisualMetersToLogicCm(in pointMeters);
            if (!asset.TryGetChunkIndex(pointCm.X, pointCm.Y, out int chunkIndex))
            {
                return false;
            }

            StructureChunkIndexEntry chunk = asset.Chunks[chunkIndex];
            float heightCm = pointMeters.Y * WorldUnits.CmPerMeter;
            int end = chunk.BlockerStart + chunk.BlockerCount;
            for (int i = chunk.BlockerStart; i < end; i++)
            {
                int surfaceIndex = asset.ChunkBlockerIndices[i];
                if (runtimeState != null && !runtimeState.IsSurfaceEnabled(surfaceIndex))
                {
                    continue;
                }

                if ((asset.Surfaces.Flags[surfaceIndex] & StructureSurfaceFlags.BlocksVision) == 0)
                {
                    continue;
                }

                if (asset.ContainsSurfaceVolumePoint(surfaceIndex, pointCm.X, pointCm.Y, heightCm))
                {
                    return true;
                }
            }

            return false;
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
                _behaviorInput ?? throw new InvalidOperationException("Camera behavior input state is not configured."),
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

        private bool TryResolveWorldBoundsConfine(
            VirtualCameraDefinition? definition,
            in CameraStateSnapshot state,
            out Vector2 clamped)
        {
            clamped = state.TargetCm;
            if (definition == null ||
                !definition.ConfineTargetToWorldBounds ||
                _targetBoundsProvider == null)
            {
                return false;
            }

            WorldAabbCm bounds = ExpandBounds(_targetBoundsProvider(), definition.ConfinePaddingCm);
            var candidate = state;
            candidate.TargetCm = ClampTargetToBounds(candidate.TargetCm, in bounds);
            bool changed = Vector2.DistanceSquared(candidate.TargetCm, state.TargetCm) > TargetConfineEpsilonSq;

            if (definition.TargetHeightMode == VirtualCameraTargetHeightMode.VisualHeightmap &&
                UsesVisualHeightmapLookFootprintConfine(definition))
            {
                candidate.TargetHeightCm = ResolveTargetHeight(definition, candidate.TargetCm);
                if (TryResolveVisualHeightmapFootprintConfine(
                        definition,
                        in bounds,
                        candidate,
                        out Vector2 footprintClamped))
                {
                    candidate.TargetCm = footprintClamped;
                    changed = true;
                }
            }

            clamped = candidate.TargetCm;
            return changed;
        }

        private static bool UsesVisualHeightmapLookFootprintConfine(VirtualCameraDefinition definition)
        {
            return definition.RigKind == CameraRigKind.Orbit ||
                   definition.RigKind == CameraRigKind.TopDown;
        }

        private void ApplyWorldBoundsAndHeightToState(VirtualCameraDefinition? definition)
        {
            CameraStateSnapshot snapshot = CameraStateSnapshot.FromState(State);
            if (TryResolveWorldBoundsConfine(definition, in snapshot, out Vector2 clamped))
            {
                State.TargetCm = clamped;
            }

            State.TargetHeightCm = ResolveTargetHeight(definition, State.TargetCm);
        }

        private void ApplyActiveVirtualCameraBoundsAndHeight()
        {
            if (VirtualCameraBrain == null ||
                !VirtualCameraBrain.TryGetActiveRuntimeState(_behaviorInput, out var definition, out CameraStateSnapshot runtimeState) ||
                definition == null)
            {
                return;
            }

            if (TryResolveWorldBoundsConfine(definition, in runtimeState, out Vector2 clampedTargetCm))
            {
                runtimeState.TargetCm = clampedTargetCm;
            }

            runtimeState.TargetHeightCm = ResolveTargetHeight(definition, runtimeState.TargetCm);
            VirtualCameraBrain.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = definition.Id,
                TargetCm = runtimeState.TargetCm,
                TargetHeightCm = runtimeState.TargetHeightCm
            });
        }

        private bool TryResolveVisualHeightmapFootprintConfine(
            VirtualCameraDefinition definition,
            in WorldAabbCm bounds,
            CameraStateSnapshot initialState,
            out Vector2 clamped)
        {
            clamped = initialState.TargetCm;
            var candidate = initialState;
            bool changed = false;

            for (int pass = 0; pass < VisualHeightmapFootprintConfinePasses; pass++)
            {
                ResolveVisualHeightmapFootprintAabb(
                    definition,
                    in candidate,
                    out float minX,
                    out float minY,
                    out float maxX,
                    out float maxY);

                var correction = new Vector2(
                    ResolveBoundsCorrection(minX, maxX, bounds.Left, bounds.Right),
                    ResolveBoundsCorrection(minY, maxY, bounds.Top, bounds.Bottom));
                if (correction.LengthSquared() <= TargetConfineEpsilonSq)
                {
                    break;
                }

                Vector2 nextTarget = ClampTargetToBounds(candidate.TargetCm + correction, in bounds);
                if (Vector2.DistanceSquared(nextTarget, candidate.TargetCm) <= TargetConfineEpsilonSq)
                {
                    break;
                }

                candidate.TargetCm = nextTarget;
                candidate.TargetHeightCm = ResolveTargetHeight(definition, candidate.TargetCm);
                changed = true;
            }

            clamped = candidate.TargetCm;
            return changed;
        }

        private void ResolveVisualHeightmapFootprintAabb(
            VirtualCameraDefinition definition,
            in CameraStateSnapshot state,
            out float minX,
            out float minY,
            out float maxX,
            out float maxY)
        {
            IVisualHeightmap heightmap = RequireVisualHeightmap(definition);
            if (_runtimeContext == null)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' requires a configured camera viewport to clamp VisualHeightmap look footprint.");
            }

            Vector2 resolution = _runtimeContext.Viewport.Resolution;
            if (!float.IsFinite(resolution.X) ||
                !float.IsFinite(resolution.Y) ||
                resolution.X <= 0f ||
                resolution.Y <= 0f)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' cannot clamp VisualHeightmap look footprint because the active viewport resolution is invalid.");
            }

            float aspectRatio = _runtimeContext.Viewport.AspectRatio;
            if (!float.IsFinite(aspectRatio) || aspectRatio <= 0f)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' cannot clamp VisualHeightmap look footprint because the active viewport aspect ratio is invalid.");
            }

            CameraRenderState3D camera = CameraViewportUtil.StateToRenderState(in state);
            float right = MathF.Max(0f, resolution.X - 1f);
            float bottom = MathF.Max(0f, resolution.Y - 1f);

            minX = float.PositiveInfinity;
            minY = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            maxY = float.NegativeInfinity;
            int hitCount = 0;

            if (TryAccumulateVisualHeightmapFootprintCorner(
                definition,
                heightmap,
                in camera,
                resolution,
                aspectRatio,
                new Vector2(0f, 0f),
                ref minX,
                ref minY,
                ref maxX,
                ref maxY))
            {
                hitCount++;
            }

            if (TryAccumulateVisualHeightmapFootprintCorner(
                definition,
                heightmap,
                in camera,
                resolution,
                aspectRatio,
                new Vector2(right, 0f),
                ref minX,
                ref minY,
                ref maxX,
                ref maxY))
            {
                hitCount++;
            }

            if (TryAccumulateVisualHeightmapFootprintCorner(
                definition,
                heightmap,
                in camera,
                resolution,
                aspectRatio,
                new Vector2(right, bottom),
                ref minX,
                ref minY,
                ref maxX,
                ref maxY))
            {
                hitCount++;
            }

            if (TryAccumulateVisualHeightmapFootprintCorner(
                definition,
                heightmap,
                in camera,
                resolution,
                aspectRatio,
                new Vector2(0f, bottom),
                ref minX,
                ref minY,
                ref maxX,
                ref maxY))
            {
                hitCount++;
            }

            if (hitCount == 0)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' could not raycast any VisualHeightmap look footprint sample on layer {definition.TargetHeightLayerIndex}.");
            }
        }

        private bool TryAccumulateVisualHeightmapFootprintCorner(
            VirtualCameraDefinition definition,
            IVisualHeightmap heightmap,
            in CameraRenderState3D camera,
            Vector2 resolution,
            float aspectRatio,
            Vector2 screenPoint,
            ref float minX,
            ref float minY,
            ref float maxX,
            ref float maxY)
        {
            ScreenRay ray = CameraViewportUtil.ScreenToRay(
                screenPoint,
                in camera,
                resolution,
                aspectRatio);

            if (!heightmap.TryRaycastGround(in ray, out VisualGroundHit hit, definition.TargetHeightLayerIndex) ||
                !float.IsFinite(hit.WorldXCm) ||
                !float.IsFinite(hit.WorldYCm))
            {
                return false;
            }

            minX = MathF.Min(minX, hit.WorldXCm);
            minY = MathF.Min(minY, hit.WorldYCm);
            maxX = MathF.Max(maxX, hit.WorldXCm);
            maxY = MathF.Max(maxY, hit.WorldYCm);
            return true;
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
            IVisualHeightmap heightmap = RequireVisualHeightmap(definition);

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

        private IVisualHeightmap RequireVisualHeightmap(VirtualCameraDefinition definition)
        {
            IVisualHeightmap? heightmap = _visualHeightmapProvider?.Invoke();
            if (heightmap == null)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' requires CoreServiceKeys.VisualHeightmap for target height, but no focused map visual heightmap service is bound.");
            }

            return heightmap;
        }

        private StructureCollisionAsset RequireStructureCollisionAsset(VirtualCameraDefinition definition)
        {
            StructureCollisionAsset? asset = _structureCollisionProvider?.Invoke();
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Virtual camera '{definition.Id}' enables structure obstruction avoidance, but no focused map structure collision asset service is bound.");
            }

            return asset;
        }

        private static Vector2 ClampTargetToBounds(Vector2 targetCm, in WorldAabbCm bounds)
        {
            return new Vector2(
                Math.Clamp(targetCm.X, bounds.Left, bounds.Right),
                Math.Clamp(targetCm.Y, bounds.Top, bounds.Bottom));
        }

        private static float ResolveBoundsCorrection(float min, float max, float boundsMin, float boundsMax)
        {
            if (!float.IsFinite(min) || !float.IsFinite(max))
            {
                throw new InvalidOperationException("Camera VisualHeightmap footprint resolved non-finite bounds.");
            }

            float span = max - min;
            float allowed = boundsMax - boundsMin;
            if (span <= allowed)
            {
                if (min < boundsMin)
                {
                    return boundsMin - min;
                }

                if (max > boundsMax)
                {
                    return boundsMax - max;
                }

                return 0f;
            }

            return ((boundsMin + boundsMax) * 0.5f) - ((min + max) * 0.5f);
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
            destination.RigPivotOffsetCm = source.RigPivotOffsetCm;
            destination.RigCameraOffsetCm = source.RigCameraOffsetCm;
            destination.ImpulsePositionOffsetCm = source.ImpulsePositionOffsetCm;
            destination.ImpulseYawOffsetDeg = source.ImpulseYawOffsetDeg;
            destination.ImpulsePitchOffsetDeg = source.ImpulsePitchOffsetDeg;
            destination.CameraCollisionPositionOffsetCm = source.CameraCollisionPositionOffsetCm;
            destination.CameraCollisionCorrectionCm = source.CameraCollisionCorrectionCm;
            destination.IsFollowing = source.IsFollowing;
        }
    }
}
