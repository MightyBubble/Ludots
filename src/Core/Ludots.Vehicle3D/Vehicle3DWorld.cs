using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Vehicle3D;

/// <summary>
/// Fixed-capacity Vehicle3D state and command producer. The caller owns the Physics3D step.
/// </summary>
public sealed class Vehicle3DWorld : IDisposable
{
    private const int ConstraintsPerPhysicalWheel = 7;

    private readonly IPhysics3DWorld _physics;
    private readonly float _fixedDeltaSeconds;

    private readonly byte[] _vehicleActive;
    private readonly int[] _vehicleGenerations;
    private readonly int[] _vehicleFree;
    private readonly Physics3DBodyId[] _vehicleChassis;
    private readonly int[] _vehicleWheelCounts;
    private readonly float[] _vehicleThrottle;
    private readonly float[] _vehicleBrake;
    private readonly float[] _vehicleSteering;
    private readonly Vector3[] _vehiclePositionsCm;
    private readonly Quaternion[] _vehicleOrientations;

    private readonly byte[] _wheelActive;
    private readonly int[] _wheelGenerations;
    private readonly int[] _wheelFree;
    private readonly int[] _wheelVehicleSlots;
    private readonly Vehicle3DWheelKind[] _wheelKinds;
    private readonly Vehicle3DWheelQueryKind[] _wheelQueryKinds;
    private readonly Physics3DBodyId[] _wheelCarrierBodies;
    private readonly Physics3DBodyId[] _wheelBodies;
    private readonly Vector3[] _wheelLocalMountsCm;
    private readonly Vector3[] _wheelLocalSuspensionDirections;
    private readonly Vector3[] _wheelLocalForwardDirections;
    private readonly float[] _wheelRadiiCm;
    private readonly float[] _wheelMinimumLengthsCm;
    private readonly float[] _wheelRestLengthsCm;
    private readonly float[] _wheelMaximumLengthsCm;
    private readonly float[] _wheelMaximumSteeringAngles;
    private readonly float[] _wheelSuspensionStiffness;
    private readonly float[] _wheelSuspensionDamping;
    private readonly float[] _wheelMaximumSuspensionForce;
    private readonly float[] _wheelLongitudinalGrip;
    private readonly float[] _wheelLateralGrip;
    private readonly float[] _wheelMaximumDriveForce;
    private readonly float[] _wheelMaximumBrakeForce;
    private readonly float[] _wheelMaximumLateralForce;
    private readonly float[] _wheelMaximumAngularSpeed;
    private readonly float[] _wheelSteeringScale;
    private readonly float[] _wheelDriveScale;
    private readonly float[] _wheelBrakeScale;
    private readonly Ludots.Core.Layers.LayerMask[] _wheelGroundLayers;

    private readonly Physics3DConstraintId[] _wheelLineConstraints;
    private readonly Physics3DConstraintId[] _wheelSuspensionServos;
    private readonly Physics3DConstraintId[] _wheelTravelLimits;
    private readonly Physics3DConstraintId[] _wheelSteeringServos;
    private readonly Physics3DConstraintId[] _wheelHubConstraints;
    private readonly Physics3DConstraintId[] _wheelAxleHinges;
    private readonly Physics3DConstraintId[] _wheelAxleMotors;

    private readonly byte[] _wheelGrounded;
    private readonly float[] _wheelSuspensionLengthsCm;
    private readonly float[] _wheelCompressionCm;
    private readonly Vector3[] _wheelContactPointsCm;
    private readonly Vector3[] _wheelContactNormals;
    private readonly Vector3[] _wheelSlipVelocities;
    private readonly float[] _wheelLongitudinalSpeeds;
    private readonly float[] _wheelLateralSpeeds;
    private readonly float[] _wheelSuspensionForces;
    private readonly float[] _wheelAngularSpeeds;

    private readonly Vector3[] _stageOriginsCm;
    private readonly Vector3[] _stageSuspensionDirections;
    private readonly Vector3[] _stageForwardDirections;
    private readonly Vector3[] _stageAxleDirections;
    private readonly byte[] _stageGrounded;
    private readonly Physics3DBodyId[] _stageGroundBodies;
    private readonly float[] _stageSuspensionLengthsCm;
    private readonly float[] _stageCompressionCm;
    private readonly Vector3[] _stageContactPointsCm;
    private readonly Vector3[] _stageContactNormals;
    private readonly Vector3[] _stageSlipVelocities;
    private readonly float[] _stageLongitudinalSpeeds;
    private readonly float[] _stageLateralSpeeds;
    private readonly float[] _stageSuspensionForces;
    private readonly float[] _stageAngularSpeeds;
    private readonly Vector3[] _stageImpulses;
    private readonly Physics3DBodyId[] _stageImpulseBodies;
    private readonly byte[] _stageGroundReactions;

    private readonly Physics3DRaycastQuery[] _rayRequests;
    private readonly Physics3DBatchedRaycastClosestResult[] _rayResults;
    private readonly int[] _rayWheelSlots;
    private readonly Physics3DSphereCastQuery[] _sphereRequests;
    private readonly Physics3DBatchedShapeCastClosestResult[] _sphereResults;
    private readonly int[] _sphereWheelSlots;

    private int _vehicleFreeCount;
    private int _wheelFreeCount;
    private int _activeVehicleCount;
    private int _activeWheelCount;
    private bool _disposed;

    public Vehicle3DWorld(IPhysics3DWorld physics, Vehicle3DConfig config)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        float expectedDelta = 1f / config.FixedStepHz;
        if (MathF.Abs(physics.FixedDeltaSeconds - expectedDelta) > 1e-6f)
        {
            throw new InvalidOperationException(
                $"Vehicle3D requires a {config.FixedStepHz}Hz Physics3D world, but fixed delta is {physics.FixedDeltaSeconds} seconds.");
        }

        _fixedDeltaSeconds = expectedDelta;
        int vehicleCapacity = config.VehicleCapacity;
        int wheelCapacity = config.WheelCapacity;

        _vehicleActive = new byte[vehicleCapacity];
        _vehicleGenerations = new int[vehicleCapacity];
        _vehicleFree = new int[vehicleCapacity];
        _vehicleChassis = new Physics3DBodyId[vehicleCapacity];
        _vehicleWheelCounts = new int[vehicleCapacity];
        _vehicleThrottle = new float[vehicleCapacity];
        _vehicleBrake = new float[vehicleCapacity];
        _vehicleSteering = new float[vehicleCapacity];
        _vehiclePositionsCm = new Vector3[vehicleCapacity];
        _vehicleOrientations = new Quaternion[vehicleCapacity];

        _wheelActive = new byte[wheelCapacity];
        _wheelGenerations = new int[wheelCapacity];
        _wheelFree = new int[wheelCapacity];
        _wheelVehicleSlots = new int[wheelCapacity];
        _wheelKinds = new Vehicle3DWheelKind[wheelCapacity];
        _wheelQueryKinds = new Vehicle3DWheelQueryKind[wheelCapacity];
        _wheelCarrierBodies = new Physics3DBodyId[wheelCapacity];
        _wheelBodies = new Physics3DBodyId[wheelCapacity];
        _wheelLocalMountsCm = new Vector3[wheelCapacity];
        _wheelLocalSuspensionDirections = new Vector3[wheelCapacity];
        _wheelLocalForwardDirections = new Vector3[wheelCapacity];
        _wheelRadiiCm = new float[wheelCapacity];
        _wheelMinimumLengthsCm = new float[wheelCapacity];
        _wheelRestLengthsCm = new float[wheelCapacity];
        _wheelMaximumLengthsCm = new float[wheelCapacity];
        _wheelMaximumSteeringAngles = new float[wheelCapacity];
        _wheelSuspensionStiffness = new float[wheelCapacity];
        _wheelSuspensionDamping = new float[wheelCapacity];
        _wheelMaximumSuspensionForce = new float[wheelCapacity];
        _wheelLongitudinalGrip = new float[wheelCapacity];
        _wheelLateralGrip = new float[wheelCapacity];
        _wheelMaximumDriveForce = new float[wheelCapacity];
        _wheelMaximumBrakeForce = new float[wheelCapacity];
        _wheelMaximumLateralForce = new float[wheelCapacity];
        _wheelMaximumAngularSpeed = new float[wheelCapacity];
        _wheelSteeringScale = new float[wheelCapacity];
        _wheelDriveScale = new float[wheelCapacity];
        _wheelBrakeScale = new float[wheelCapacity];
        _wheelGroundLayers = new Ludots.Core.Layers.LayerMask[wheelCapacity];

        _wheelLineConstraints = new Physics3DConstraintId[wheelCapacity];
        _wheelSuspensionServos = new Physics3DConstraintId[wheelCapacity];
        _wheelTravelLimits = new Physics3DConstraintId[wheelCapacity];
        _wheelSteeringServos = new Physics3DConstraintId[wheelCapacity];
        _wheelHubConstraints = new Physics3DConstraintId[wheelCapacity];
        _wheelAxleHinges = new Physics3DConstraintId[wheelCapacity];
        _wheelAxleMotors = new Physics3DConstraintId[wheelCapacity];

        _wheelGrounded = new byte[wheelCapacity];
        _wheelSuspensionLengthsCm = new float[wheelCapacity];
        _wheelCompressionCm = new float[wheelCapacity];
        _wheelContactPointsCm = new Vector3[wheelCapacity];
        _wheelContactNormals = new Vector3[wheelCapacity];
        _wheelSlipVelocities = new Vector3[wheelCapacity];
        _wheelLongitudinalSpeeds = new float[wheelCapacity];
        _wheelLateralSpeeds = new float[wheelCapacity];
        _wheelSuspensionForces = new float[wheelCapacity];
        _wheelAngularSpeeds = new float[wheelCapacity];

        _stageOriginsCm = new Vector3[wheelCapacity];
        _stageSuspensionDirections = new Vector3[wheelCapacity];
        _stageForwardDirections = new Vector3[wheelCapacity];
        _stageAxleDirections = new Vector3[wheelCapacity];
        _stageGrounded = new byte[wheelCapacity];
        _stageGroundBodies = new Physics3DBodyId[wheelCapacity];
        _stageSuspensionLengthsCm = new float[wheelCapacity];
        _stageCompressionCm = new float[wheelCapacity];
        _stageContactPointsCm = new Vector3[wheelCapacity];
        _stageContactNormals = new Vector3[wheelCapacity];
        _stageSlipVelocities = new Vector3[wheelCapacity];
        _stageLongitudinalSpeeds = new float[wheelCapacity];
        _stageLateralSpeeds = new float[wheelCapacity];
        _stageSuspensionForces = new float[wheelCapacity];
        _stageAngularSpeeds = new float[wheelCapacity];
        _stageImpulses = new Vector3[wheelCapacity];
        _stageImpulseBodies = new Physics3DBodyId[wheelCapacity];
        _stageGroundReactions = new byte[wheelCapacity];

        _rayRequests = new Physics3DRaycastQuery[config.QueryBatchCapacity];
        _rayResults = new Physics3DBatchedRaycastClosestResult[config.QueryBatchCapacity];
        _rayWheelSlots = new int[config.QueryBatchCapacity];
        _sphereRequests = new Physics3DSphereCastQuery[config.QueryBatchCapacity];
        _sphereResults = new Physics3DBatchedShapeCastClosestResult[config.QueryBatchCapacity];
        _sphereWheelSlots = new int[config.QueryBatchCapacity];

        for (int i = 0; i < vehicleCapacity; i++)
        {
            _vehicleGenerations[i] = 1;
            _vehicleFree[i] = vehicleCapacity - 1 - i;
        }

        for (int i = 0; i < wheelCapacity; i++)
        {
            _wheelGenerations[i] = 1;
            _wheelFree[i] = wheelCapacity - 1 - i;
            _wheelVehicleSlots[i] = -1;
        }

        _vehicleFreeCount = vehicleCapacity;
        _wheelFreeCount = wheelCapacity;
    }

    public int VehicleCapacity => _vehicleActive.Length;
    public int WheelCapacity => _wheelActive.Length;
    public int ActiveVehicleCount => _activeVehicleCount;
    public int ActiveWheelCount => _activeWheelCount;

    public Vehicle3DVehicleId RegisterVehicle(
        Physics3DBodyId chassisBody,
        ReadOnlySpan<Vehicle3DWheelDescription> wheels,
        Span<Vehicle3DWheelId> registeredWheels)
    {
        ThrowIfDisposed();
        if (wheels.Length <= 0)
        {
            throw new ArgumentException("A vehicle requires at least one wheel.", nameof(wheels));
        }

        if (registeredWheels.Length != wheels.Length)
        {
            throw new ArgumentException(
                $"Registered wheel output length {registeredWheels.Length} must equal wheel description length {wheels.Length}.",
                nameof(registeredWheels));
        }

        if (_vehicleFreeCount == 0)
        {
            throw new Vehicle3DCapacityExceededException("vehicles", VehicleCapacity, _activeVehicleCount + 1);
        }

        if (_wheelFreeCount < wheels.Length)
        {
            throw new Vehicle3DCapacityExceededException("wheels", WheelCapacity, _activeWheelCount + wheels.Length);
        }

        RequireDynamicBody(chassisBody, nameof(chassisBody));
        for (int i = 0; i < _vehicleActive.Length; i++)
        {
            if (_vehicleActive[i] != 0 && _vehicleChassis[i] == chassisBody)
            {
                throw new InvalidOperationException($"Chassis body {chassisBody} is already registered to vehicle slot {i}.");
            }
        }

        for (int i = 0; i < wheels.Length; i++)
        {
            Vehicle3DWheelDescription description = wheels[i];
            description.Validate($"{nameof(wheels)}[{i}]");
            if (!description.HasPhysicalWheel)
            {
                continue;
            }

            RequireDynamicBody(description.CarrierBody, $"{nameof(wheels)}[{i}].{nameof(description.CarrierBody)}");
            RequireDynamicBody(description.WheelBody, $"{nameof(wheels)}[{i}].{nameof(description.WheelBody)}");
            if (description.CarrierBody == chassisBody || description.WheelBody == chassisBody)
            {
                throw new ArgumentException("Physical carrier and wheel bodies must be distinct from the chassis body.", nameof(wheels));
            }

            EnsurePhysicalBodiesAreUnique(wheels, i, in description);
        }

        int vehicleSlot = _vehicleFree[_vehicleFreeCount - 1];
        try
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                int wheelSlot = _wheelFree[_wheelFreeCount - 1 - i];
                CopyDescriptionToSlot(vehicleSlot, wheelSlot, in wheels[i]);
                if (wheels[i].HasPhysicalWheel)
                {
                    CreatePhysicalWheelConstraints(chassisBody, wheelSlot, in wheels[i]);
                }
            }
        }
        catch
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                int wheelSlot = _wheelFree[_wheelFreeCount - 1 - i];
                DestroyWheelConstraints(wheelSlot);
                ClearWheelSlot(wheelSlot);
            }

            throw;
        }

        _vehicleFreeCount--;
        _vehicleActive[vehicleSlot] = 1;
        _vehicleChassis[vehicleSlot] = chassisBody;
        _vehicleWheelCounts[vehicleSlot] = wheels.Length;
        _activeVehicleCount++;

        for (int i = 0; i < wheels.Length; i++)
        {
            int wheelSlot = _wheelFree[_wheelFreeCount - 1 - i];
            _wheelActive[wheelSlot] = 1;
            registeredWheels[i] = new Vehicle3DWheelId(wheelSlot, _wheelGenerations[wheelSlot]);
        }

        _wheelFreeCount -= wheels.Length;
        _activeWheelCount += wheels.Length;
        return new Vehicle3DVehicleId(vehicleSlot, _vehicleGenerations[vehicleSlot]);
    }

    public void RemoveVehicle(Vehicle3DVehicleId vehicle)
    {
        ThrowIfDisposed();
        int vehicleSlot = RequireVehicleSlot(vehicle);
        for (int wheelSlot = _wheelActive.Length - 1; wheelSlot >= 0; wheelSlot--)
        {
            if (_wheelActive[wheelSlot] == 0 || _wheelVehicleSlots[wheelSlot] != vehicleSlot)
            {
                continue;
            }

            DestroyWheelConstraints(wheelSlot);
            _wheelActive[wheelSlot] = 0;
            _wheelGenerations[wheelSlot] = NextGeneration(_wheelGenerations[wheelSlot]);
            ClearWheelSlot(wheelSlot);
            _wheelFree[_wheelFreeCount++] = wheelSlot;
            _activeWheelCount--;
        }

        _vehicleActive[vehicleSlot] = 0;
        _vehicleGenerations[vehicleSlot] = NextGeneration(_vehicleGenerations[vehicleSlot]);
        _vehicleChassis[vehicleSlot] = default;
        _vehicleWheelCounts[vehicleSlot] = 0;
        _vehicleThrottle[vehicleSlot] = 0f;
        _vehicleBrake[vehicleSlot] = 0f;
        _vehicleSteering[vehicleSlot] = 0f;
        _vehicleFree[_vehicleFreeCount++] = vehicleSlot;
        _activeVehicleCount--;
    }

    public bool ContainsVehicle(Vehicle3DVehicleId vehicle)
    {
        ThrowIfDisposed();
        return vehicle.Slot >= 0 &&
               vehicle.Slot < _vehicleActive.Length &&
               _vehicleActive[vehicle.Slot] != 0 &&
               _vehicleGenerations[vehicle.Slot] == vehicle.Generation;
    }

    public void SetInput(Vehicle3DVehicleId vehicle, in Vehicle3DInput input)
    {
        ThrowIfDisposed();
        int slot = RequireVehicleSlot(vehicle);
        _vehicleThrottle[slot] = input.Throttle;
        _vehicleBrake[slot] = input.Brake;
        _vehicleSteering[slot] = input.Steering;
    }

    public Vehicle3DWheelState GetWheelState(Vehicle3DWheelId wheel)
    {
        ThrowIfDisposed();
        int slot = RequireWheelSlot(wheel);
        return CreateWheelState(slot);
    }

    public int CopyWheelStates(Span<Vehicle3DWheelState> destination)
    {
        ThrowIfDisposed();
        if (destination.Length < _activeWheelCount)
        {
            throw new Vehicle3DCapacityExceededException("wheel state destination", destination.Length, _activeWheelCount);
        }

        int count = 0;
        for (int i = 0; i < _wheelActive.Length; i++)
        {
            if (_wheelActive[i] != 0)
            {
                destination[count++] = CreateWheelState(i);
            }
        }

        return count;
    }

    public int CopyVehicleWheels(Vehicle3DVehicleId vehicle, Span<Vehicle3DWheelId> destination)
    {
        ThrowIfDisposed();
        int vehicleSlot = RequireVehicleSlot(vehicle);
        int required = _vehicleWheelCounts[vehicleSlot];
        if (destination.Length < required)
        {
            throw new Vehicle3DCapacityExceededException("vehicle wheel destination", destination.Length, required);
        }

        int count = 0;
        for (int i = 0; i < _wheelActive.Length; i++)
        {
            if (_wheelActive[i] != 0 && _wheelVehicleSlots[i] == vehicleSlot)
            {
                destination[count++] = new Vehicle3DWheelId(i, _wheelGenerations[i]);
            }
        }

        return count;
    }

    public void PrepareStep()
    {
        ThrowIfDisposed();
        CacheAndValidateBodies();
        BuildQueryBatches(out int rayCount, out int sphereCount);
        if (rayCount > 0)
        {
            _physics.RaycastClosestBatch(
                _rayRequests.AsSpan(0, rayCount),
                _rayResults.AsSpan(0, rayCount));
            for (int i = 0; i < rayCount; i++)
            {
                int wheelSlot = _rayWheelSlots[i];
                Physics3DBatchedRaycastClosestResult result = _rayResults[i];
                if (result.Hit)
                {
                    Physics3DRaycastHit hit = result.Value;
                    StageRayHit(wheelSlot, in hit);
                }
            }
        }

        if (sphereCount > 0)
        {
            _physics.SphereCastClosestBatch(
                _sphereRequests.AsSpan(0, sphereCount),
                _sphereResults.AsSpan(0, sphereCount));
            for (int i = 0; i < sphereCount; i++)
            {
                int wheelSlot = _sphereWheelSlots[i];
                Physics3DBatchedShapeCastClosestResult result = _sphereResults[i];
                if (result.Hit)
                {
                    Physics3DShapeCastHit hit = result.Value;
                    StageShapeHit(wheelSlot, in hit);
                }
            }
        }

        int requiredActuationCommands = PlanWheelActuation();
        int availableCommands = _physics.ActuationCommandCapacity - _physics.PendingActuationCommandCount;
        if (requiredActuationCommands > availableCommands)
        {
            throw new Vehicle3DCapacityExceededException(
                "Physics3D actuation commands",
                availableCommands,
                requiredActuationCommands);
        }

        UpdatePhysicalWheelTargets();
        EnqueuePlannedActuation();
        CommitWheelState();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int wheelSlot = _wheelActive.Length - 1; wheelSlot >= 0; wheelSlot--)
        {
            if (_wheelActive[wheelSlot] != 0)
            {
                DestroyWheelConstraints(wheelSlot);
            }
        }

        _disposed = true;
    }

    private void CacheAndValidateBodies()
    {
        for (int vehicleSlot = 0; vehicleSlot < _vehicleActive.Length; vehicleSlot++)
        {
            if (_vehicleActive[vehicleSlot] == 0)
            {
                continue;
            }

            Physics3DBodyId chassis = _vehicleChassis[vehicleSlot];
            RequireDynamicBodyAtRuntime(chassis, "chassis", vehicleSlot);
            Physics3DBodyState state = _physics.GetBodyState(chassis);
            _vehiclePositionsCm[vehicleSlot] = state.PositionCm;
            _vehicleOrientations[vehicleSlot] = state.Orientation;
        }

        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0 || !HasPhysicalWheel(wheelSlot))
            {
                continue;
            }

            RequireDynamicBodyAtRuntime(_wheelCarrierBodies[wheelSlot], "carrier", wheelSlot);
            RequireDynamicBodyAtRuntime(_wheelBodies[wheelSlot], "wheel", wheelSlot);
            RequireWheelConstraints(wheelSlot);
        }
    }

    private void BuildQueryBatches(out int rayCount, out int sphereCount)
    {
        rayCount = 0;
        sphereCount = 0;
        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0)
            {
                continue;
            }

            int vehicleSlot = _wheelVehicleSlots[wheelSlot];
            Quaternion chassisOrientation = _vehicleOrientations[vehicleSlot];
            Vector3 down = Vector3.Transform(_wheelLocalSuspensionDirections[wheelSlot], chassisOrientation);
            Vector3 baseForward = Vector3.Transform(_wheelLocalForwardDirections[wheelSlot], chassisOrientation);
            float steeringAngle = _vehicleSteering[vehicleSlot] *
                                  _wheelSteeringScale[wheelSlot] *
                                  _wheelMaximumSteeringAngles[wheelSlot];
            Quaternion steeringRotation = Quaternion.CreateFromAxisAngle(-down, steeringAngle);
            Vector3 forward = Vector3.Normalize(Vector3.Transform(baseForward, steeringRotation));
            Vector3 axle = Vector3.Normalize(Vector3.Cross(forward, down));
            Vector3 origin = _vehiclePositionsCm[vehicleSlot] +
                             Vector3.Transform(_wheelLocalMountsCm[wheelSlot], chassisOrientation);

            _stageOriginsCm[wheelSlot] = origin;
            _stageSuspensionDirections[wheelSlot] = down;
            _stageForwardDirections[wheelSlot] = forward;
            _stageAxleDirections[wheelSlot] = axle;
            ClearStagedWheelState(wheelSlot);

            if (_wheelQueryKinds[wheelSlot] == Vehicle3DWheelQueryKind.Raycast)
            {
                if (rayCount >= _rayRequests.Length)
                {
                    throw new Vehicle3DCapacityExceededException("raycast wheel batch", _rayRequests.Length, rayCount + 1);
                }

                float maximumDistance = _wheelMaximumLengthsCm[wheelSlot] + _wheelRadiiCm[wheelSlot];
                var filter = new Physics3DQueryFilter(_wheelGroundLayers[wheelSlot], _vehicleChassis[vehicleSlot]);
                _rayRequests[rayCount] = new Physics3DRaycastQuery(origin, down, maximumDistance, filter);
                _rayWheelSlots[rayCount] = wheelSlot;
                rayCount++;
            }
            else if (_wheelQueryKinds[wheelSlot] == Vehicle3DWheelQueryKind.SphereCast)
            {
                if (sphereCount >= _sphereRequests.Length)
                {
                    throw new Vehicle3DCapacityExceededException(
                        "sphere cast wheel batch",
                        _sphereRequests.Length,
                        sphereCount + 1);
                }

                var filter = new Physics3DQueryFilter(_wheelGroundLayers[wheelSlot], _vehicleChassis[vehicleSlot]);
                _sphereRequests[sphereCount] = new Physics3DSphereCastQuery(
                    _stageOriginsCm[wheelSlot],
                    _wheelRadiiCm[wheelSlot],
                    _stageSuspensionDirections[wheelSlot],
                    _wheelMaximumLengthsCm[wheelSlot],
                    filter);
                _sphereWheelSlots[sphereCount] = wheelSlot;
                sphereCount++;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Wheel slot {wheelSlot} has unsupported query kind '{_wheelQueryKinds[wheelSlot]}'.");
            }
        }
    }

    private int PlanWheelActuation()
    {
        int requiredCommands = 0;
        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0)
            {
                continue;
            }

            if (HasPhysicalWheel(wheelSlot))
            {
                Physics3DBodyState carrier = _physics.GetBodyState(_wheelCarrierBodies[wheelSlot]);
                Physics3DBodyState wheel = _physics.GetBodyState(_wheelBodies[wheelSlot]);
                _stageAngularSpeeds[wheelSlot] = Vector3.Dot(
                    wheel.AngularVelocityRadiansPerSecond - carrier.AngularVelocityRadiansPerSecond,
                    _stageAxleDirections[wheelSlot]);
            }
            else if (_stageGrounded[wheelSlot] == 0)
            {
                _stageAngularSpeeds[wheelSlot] = _wheelAngularSpeeds[wheelSlot];
            }

            if (_stageGrounded[wheelSlot] == 0)
            {
                continue;
            }

            Physics3DBodyId groundBody = _stageGroundBodies[wheelSlot];
            if (groundBody == _wheelCarrierBodies[wheelSlot] || groundBody == _wheelBodies[wheelSlot])
            {
                throw new InvalidOperationException(
                    $"Wheel slot {wheelSlot} hit its own assembly body {groundBody}; ground query layers must exclude vehicle bodies.");
            }

            int vehicleSlot = _wheelVehicleSlots[wheelSlot];
            Physics3DBodyId impulseBody = HasPhysicalWheel(wheelSlot)
                ? _wheelBodies[wheelSlot]
                : _vehicleChassis[vehicleSlot];
            Vector3 point = _stageContactPointsCm[wheelSlot];
            Vector3 sourceVelocity = _physics.GetBodyVelocityAtWorldPoint(impulseBody, point);
            Vector3 groundVelocity = _physics.GetBodyVelocityAtWorldPoint(groundBody, point);
            Vector3 relativeVelocity = sourceVelocity - groundVelocity;
            Vector3 down = _stageSuspensionDirections[wheelSlot];
            Vector3 forward = _stageForwardDirections[wheelSlot];
            Vector3 axle = _stageAxleDirections[wheelSlot];
            float compression = Math.Clamp(
                _wheelRestLengthsCm[wheelSlot] - _stageSuspensionLengthsCm[wheelSlot],
                0f,
                _wheelRestLengthsCm[wheelSlot] - _wheelMinimumLengthsCm[wheelSlot]);
            float compressionSpeed = Vector3.Dot(relativeVelocity, down);
            float suspensionForce = Math.Clamp(
                (_wheelSuspensionStiffness[wheelSlot] * compression) +
                (_wheelSuspensionDamping[wheelSlot] * compressionSpeed),
                0f,
                _wheelMaximumSuspensionForce[wheelSlot]);
            float longitudinalSpeed = Vector3.Dot(relativeVelocity, forward);
            float lateralSpeed = Vector3.Dot(relativeVelocity, axle);
            float driveForce = _vehicleThrottle[vehicleSlot] *
                               _wheelDriveScale[wheelSlot] *
                               _wheelMaximumDriveForce[wheelSlot];
            float brakeLimit = _vehicleBrake[vehicleSlot] *
                               _wheelBrakeScale[wheelSlot] *
                               _wheelMaximumBrakeForce[wheelSlot];
            float brakeForce = Math.Clamp(
                -longitudinalSpeed * _wheelLongitudinalGrip[wheelSlot],
                -brakeLimit,
                brakeLimit);
            float lateralForce = Math.Clamp(
                -lateralSpeed * _wheelLateralGrip[wheelSlot],
                -_wheelMaximumLateralForce[wheelSlot],
                _wheelMaximumLateralForce[wheelSlot]);
            Vector3 force = (forward * (driveForce + brakeForce)) + (axle * lateralForce);
            if (!HasPhysicalWheel(wheelSlot))
            {
                force -= down * suspensionForce;
            }

            Vector3 impulse = force * _fixedDeltaSeconds;
            _stageCompressionCm[wheelSlot] = compression;
            _stageSlipVelocities[wheelSlot] = (forward * longitudinalSpeed) + (axle * lateralSpeed);
            _stageLongitudinalSpeeds[wheelSlot] = longitudinalSpeed;
            _stageLateralSpeeds[wheelSlot] = lateralSpeed;
            _stageSuspensionForces[wheelSlot] = suspensionForce;
            _stageImpulseBodies[wheelSlot] = impulseBody;
            _stageImpulses[wheelSlot] = impulse;

            if (!HasPhysicalWheel(wheelSlot))
            {
                _stageAngularSpeeds[wheelSlot] = longitudinalSpeed / _wheelRadiiCm[wheelSlot];
            }

            if (impulse.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            requiredCommands++;
            if (_physics.GetBodyKind(groundBody) == Physics3DBodyKind.Dynamic && groundBody != impulseBody)
            {
                _stageGroundReactions[wheelSlot] = 1;
                requiredCommands++;
            }
        }

        return requiredCommands;
    }

    private void UpdatePhysicalWheelTargets()
    {
        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0 || !HasPhysicalWheel(wheelSlot))
            {
                continue;
            }

            int vehicleSlot = _wheelVehicleSlots[wheelSlot];
            float steeringAngle = _vehicleSteering[vehicleSlot] *
                                  _wheelSteeringScale[wheelSlot] *
                                  _wheelMaximumSteeringAngles[wheelSlot];
            Quaternion steeringTarget = Quaternion.CreateFromAxisAngle(
                -_wheelLocalSuspensionDirections[wheelSlot],
                steeringAngle);
            _physics.UpdateAngularServoTarget(_wheelSteeringServos[wheelSlot], steeringTarget);

            float targetAngularSpeed;
            if (_vehicleBrake[vehicleSlot] * _wheelBrakeScale[wheelSlot] > 0f)
            {
                targetAngularSpeed = 0f;
            }
            else if (MathF.Abs(_vehicleThrottle[vehicleSlot] * _wheelDriveScale[wheelSlot]) > 1e-5f)
            {
                targetAngularSpeed = _vehicleThrottle[vehicleSlot] *
                                     _wheelDriveScale[wheelSlot] *
                                     _wheelMaximumAngularSpeed[wheelSlot];
            }
            else
            {
                targetAngularSpeed = _stageAngularSpeeds[wheelSlot];
            }

            _physics.UpdateAngularAxisMotorTarget(_wheelAxleMotors[wheelSlot], targetAngularSpeed);
        }
    }

    private void EnqueuePlannedActuation()
    {
        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0 || _stageGrounded[wheelSlot] == 0)
            {
                continue;
            }

            Vector3 impulse = _stageImpulses[wheelSlot];
            if (impulse.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            Vector3 point = _stageContactPointsCm[wheelSlot];
            _physics.EnqueueImpulseAtWorldPoint(_stageImpulseBodies[wheelSlot], impulse, point);
            if (_stageGroundReactions[wheelSlot] != 0)
            {
                _physics.EnqueueImpulseAtWorldPoint(_stageGroundBodies[wheelSlot], -impulse, point);
            }
        }
    }

    private void CommitWheelState()
    {
        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0)
            {
                continue;
            }

            _wheelGrounded[wheelSlot] = _stageGrounded[wheelSlot];
            _wheelSuspensionLengthsCm[wheelSlot] = _stageSuspensionLengthsCm[wheelSlot];
            _wheelCompressionCm[wheelSlot] = _stageCompressionCm[wheelSlot];
            _wheelContactPointsCm[wheelSlot] = _stageContactPointsCm[wheelSlot];
            _wheelContactNormals[wheelSlot] = _stageContactNormals[wheelSlot];
            _wheelSlipVelocities[wheelSlot] = _stageSlipVelocities[wheelSlot];
            _wheelLongitudinalSpeeds[wheelSlot] = _stageLongitudinalSpeeds[wheelSlot];
            _wheelLateralSpeeds[wheelSlot] = _stageLateralSpeeds[wheelSlot];
            _wheelSuspensionForces[wheelSlot] = _stageSuspensionForces[wheelSlot];
            _wheelAngularSpeeds[wheelSlot] = _stageAngularSpeeds[wheelSlot];
        }
    }

    private void StageRayHit(int wheelSlot, in Physics3DRaycastHit hit)
    {
        _stageGrounded[wheelSlot] = 1;
        _stageGroundBodies[wheelSlot] = hit.Body;
        _stageSuspensionLengthsCm[wheelSlot] = MathF.Max(0f, hit.DistanceCm - _wheelRadiiCm[wheelSlot]);
        _stageContactPointsCm[wheelSlot] = hit.PositionCm;
        _stageContactNormals[wheelSlot] = hit.Normal;
    }

    private void StageShapeHit(int wheelSlot, in Physics3DShapeCastHit hit)
    {
        _stageGrounded[wheelSlot] = 1;
        _stageGroundBodies[wheelSlot] = hit.Body;
        _stageSuspensionLengthsCm[wheelSlot] = MathF.Max(0f, hit.DistanceCm);
        _stageContactPointsCm[wheelSlot] = hit.PositionCm;
        _stageContactNormals[wheelSlot] = hit.Normal;
    }

    private void ClearStagedWheelState(int wheelSlot)
    {
        _stageGrounded[wheelSlot] = 0;
        _stageGroundBodies[wheelSlot] = default;
        _stageSuspensionLengthsCm[wheelSlot] = _wheelMaximumLengthsCm[wheelSlot];
        _stageCompressionCm[wheelSlot] = 0f;
        _stageContactPointsCm[wheelSlot] = default;
        _stageContactNormals[wheelSlot] = default;
        _stageSlipVelocities[wheelSlot] = default;
        _stageLongitudinalSpeeds[wheelSlot] = 0f;
        _stageLateralSpeeds[wheelSlot] = 0f;
        _stageSuspensionForces[wheelSlot] = 0f;
        _stageAngularSpeeds[wheelSlot] = 0f;
        _stageImpulses[wheelSlot] = default;
        _stageImpulseBodies[wheelSlot] = default;
        _stageGroundReactions[wheelSlot] = 0;
    }

    private void CopyDescriptionToSlot(
        int vehicleSlot,
        int wheelSlot,
        in Vehicle3DWheelDescription description)
    {
        _wheelVehicleSlots[wheelSlot] = vehicleSlot;
        _wheelKinds[wheelSlot] = description.Kind;
        _wheelQueryKinds[wheelSlot] = description.QueryKind;
        _wheelCarrierBodies[wheelSlot] = description.CarrierBody;
        _wheelBodies[wheelSlot] = description.WheelBody;
        _wheelLocalMountsCm[wheelSlot] = description.LocalMountCm;
        _wheelLocalSuspensionDirections[wheelSlot] = description.LocalSuspensionDirection;
        _wheelLocalForwardDirections[wheelSlot] = description.LocalForwardDirection;
        _wheelRadiiCm[wheelSlot] = description.RadiusCm;
        _wheelMinimumLengthsCm[wheelSlot] = description.MinimumLengthCm;
        _wheelRestLengthsCm[wheelSlot] = description.RestLengthCm;
        _wheelMaximumLengthsCm[wheelSlot] = description.MaximumLengthCm;
        _wheelMaximumSteeringAngles[wheelSlot] = description.MaximumSteeringAngleRadians;
        _wheelSuspensionStiffness[wheelSlot] = description.SuspensionStiffness;
        _wheelSuspensionDamping[wheelSlot] = description.SuspensionDamping;
        _wheelMaximumSuspensionForce[wheelSlot] = description.MaximumSuspensionForce;
        _wheelLongitudinalGrip[wheelSlot] = description.LongitudinalGrip;
        _wheelLateralGrip[wheelSlot] = description.LateralGrip;
        _wheelMaximumDriveForce[wheelSlot] = description.MaximumDriveForce;
        _wheelMaximumBrakeForce[wheelSlot] = description.MaximumBrakeForce;
        _wheelMaximumLateralForce[wheelSlot] = description.MaximumLateralForce;
        _wheelMaximumAngularSpeed[wheelSlot] = description.MaximumWheelAngularSpeedRadiansPerSecond;
        _wheelSteeringScale[wheelSlot] = description.SteeringScale;
        _wheelDriveScale[wheelSlot] = description.DriveScale;
        _wheelBrakeScale[wheelSlot] = description.BrakeScale;
        _wheelGroundLayers[wheelSlot] = description.GroundLayer;
    }

    private void CreatePhysicalWheelConstraints(
        Physics3DBodyId chassis,
        int wheelSlot,
        in Vehicle3DWheelDescription description)
    {
        Physics3DBodyId carrier = description.CarrierBody;
        Physics3DBodyId wheel = description.WheelBody;
        Vector3 axle = Vector3.Normalize(Vector3.Cross(
            description.LocalForwardDirection,
            description.LocalSuspensionDirection));

        _wheelLineConstraints[wheelSlot] = _physics.CreatePointOnLineServoConstraint(
            chassis,
            carrier,
            new Physics3DPointOnLineServoDescription(
                description.LocalMountCm,
                Vector3.Zero,
                description.LocalSuspensionDirection,
                description.Joint.LineServo,
                description.Joint.AlignmentSpring));
        _wheelSuspensionServos[wheelSlot] = _physics.CreateLinearAxisServoConstraint(
            chassis,
            carrier,
            new Physics3DLinearAxisServoDescription(
                description.LocalMountCm,
                Vector3.Zero,
                description.LocalSuspensionDirection,
                description.RestLengthCm,
                description.Joint.LineServo,
                description.Joint.SuspensionSpring));
        _wheelTravelLimits[wheelSlot] = _physics.CreateLinearAxisLimitConstraint(
            chassis,
            carrier,
            new Physics3DLinearAxisLimitDescription(
                description.LocalMountCm,
                Vector3.Zero,
                description.LocalSuspensionDirection,
                description.MinimumLengthCm,
                description.MaximumLengthCm,
                description.Joint.LimitSpring));
        _wheelSteeringServos[wheelSlot] = _physics.CreateAngularServoConstraint(
            chassis,
            carrier,
            new Physics3DAngularServoDescription(
                Quaternion.Identity,
                description.Joint.SteeringServo,
                description.Joint.SteeringSpring));
        _wheelHubConstraints[wheelSlot] = _physics.CreateBallSocketConstraint(
            carrier,
            wheel,
            Vector3.Zero,
            Vector3.Zero,
            description.Joint.HubSpring);
        _wheelAxleHinges[wheelSlot] = _physics.CreateAngularHingeConstraint(
            carrier,
            wheel,
            new Physics3DAngularHingeDescription(axle, axle, description.Joint.AlignmentSpring));
        _wheelAxleMotors[wheelSlot] = _physics.CreateAngularAxisMotorConstraint(
            carrier,
            wheel,
            new Physics3DAngularAxisMotorDescription(axle, 0f, description.Joint.AxleMotor));
    }

    private void DestroyWheelConstraints(int wheelSlot)
    {
        DestroyConstraint(_wheelAxleMotors[wheelSlot]);
        DestroyConstraint(_wheelAxleHinges[wheelSlot]);
        DestroyConstraint(_wheelHubConstraints[wheelSlot]);
        DestroyConstraint(_wheelSteeringServos[wheelSlot]);
        DestroyConstraint(_wheelTravelLimits[wheelSlot]);
        DestroyConstraint(_wheelSuspensionServos[wheelSlot]);
        DestroyConstraint(_wheelLineConstraints[wheelSlot]);
        ClearConstraintIds(wheelSlot);
    }

    private void DestroyConstraint(Physics3DConstraintId constraint)
    {
        if (constraint.IsValid && _physics.ContainsConstraint(constraint))
        {
            _physics.DestroyConstraint(constraint);
        }
    }

    private void RequireWheelConstraints(int wheelSlot)
    {
        Span<Physics3DConstraintId> constraints = stackalloc Physics3DConstraintId[ConstraintsPerPhysicalWheel]
        {
            _wheelLineConstraints[wheelSlot],
            _wheelSuspensionServos[wheelSlot],
            _wheelTravelLimits[wheelSlot],
            _wheelSteeringServos[wheelSlot],
            _wheelHubConstraints[wheelSlot],
            _wheelAxleHinges[wheelSlot],
            _wheelAxleMotors[wheelSlot]
        };
        for (int i = 0; i < constraints.Length; i++)
        {
            if (!constraints[i].IsValid || !_physics.ContainsConstraint(constraints[i]))
            {
                throw new InvalidOperationException(
                    $"Physical wheel slot {wheelSlot} lost constraint {i} of {ConstraintsPerPhysicalWheel}.");
            }
        }
    }

    private void EnsurePhysicalBodiesAreUnique(
        ReadOnlySpan<Vehicle3DWheelDescription> descriptions,
        int currentIndex,
        in Vehicle3DWheelDescription current)
    {
        for (int i = 0; i < currentIndex; i++)
        {
            Vehicle3DWheelDescription previous = descriptions[i];
            if (!previous.HasPhysicalWheel)
            {
                continue;
            }

            if (current.CarrierBody == previous.CarrierBody ||
                current.CarrierBody == previous.WheelBody ||
                current.WheelBody == previous.CarrierBody ||
                current.WheelBody == previous.WheelBody)
            {
                throw new ArgumentException("Physical wheel carrier and wheel bodies must be unique within a vehicle.", nameof(descriptions));
            }
        }

        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0 || !HasPhysicalWheel(wheelSlot))
            {
                continue;
            }

            if (current.CarrierBody == _wheelCarrierBodies[wheelSlot] ||
                current.CarrierBody == _wheelBodies[wheelSlot] ||
                current.WheelBody == _wheelCarrierBodies[wheelSlot] ||
                current.WheelBody == _wheelBodies[wheelSlot])
            {
                throw new InvalidOperationException($"A physical wheel body is already owned by wheel slot {wheelSlot}.");
            }
        }
    }

    private Vehicle3DWheelState CreateWheelState(int wheelSlot)
    {
        int vehicleSlot = _wheelVehicleSlots[wheelSlot];
        return new Vehicle3DWheelState(
            new Vehicle3DWheelId(wheelSlot, _wheelGenerations[wheelSlot]),
            new Vehicle3DVehicleId(vehicleSlot, _vehicleGenerations[vehicleSlot]),
            _wheelKinds[wheelSlot],
            _wheelGrounded[wheelSlot] != 0,
            _wheelSuspensionLengthsCm[wheelSlot],
            _wheelCompressionCm[wheelSlot],
            _wheelContactPointsCm[wheelSlot],
            _wheelContactNormals[wheelSlot],
            _wheelSlipVelocities[wheelSlot],
            _wheelLongitudinalSpeeds[wheelSlot],
            _wheelLateralSpeeds[wheelSlot],
            _wheelSuspensionForces[wheelSlot],
            _wheelAngularSpeeds[wheelSlot]);
    }

    private void ClearWheelSlot(int wheelSlot)
    {
        _wheelVehicleSlots[wheelSlot] = -1;
        _wheelKinds[wheelSlot] = default;
        _wheelQueryKinds[wheelSlot] = default;
        _wheelCarrierBodies[wheelSlot] = default;
        _wheelBodies[wheelSlot] = default;
        _wheelLocalMountsCm[wheelSlot] = default;
        _wheelLocalSuspensionDirections[wheelSlot] = default;
        _wheelLocalForwardDirections[wheelSlot] = default;
        _wheelRadiiCm[wheelSlot] = 0f;
        _wheelMinimumLengthsCm[wheelSlot] = 0f;
        _wheelRestLengthsCm[wheelSlot] = 0f;
        _wheelMaximumLengthsCm[wheelSlot] = 0f;
        _wheelMaximumSteeringAngles[wheelSlot] = 0f;
        _wheelSuspensionStiffness[wheelSlot] = 0f;
        _wheelSuspensionDamping[wheelSlot] = 0f;
        _wheelMaximumSuspensionForce[wheelSlot] = 0f;
        _wheelLongitudinalGrip[wheelSlot] = 0f;
        _wheelLateralGrip[wheelSlot] = 0f;
        _wheelMaximumDriveForce[wheelSlot] = 0f;
        _wheelMaximumBrakeForce[wheelSlot] = 0f;
        _wheelMaximumLateralForce[wheelSlot] = 0f;
        _wheelMaximumAngularSpeed[wheelSlot] = 0f;
        _wheelSteeringScale[wheelSlot] = 0f;
        _wheelDriveScale[wheelSlot] = 0f;
        _wheelBrakeScale[wheelSlot] = 0f;
        _wheelGroundLayers[wheelSlot] = default;
        _wheelGrounded[wheelSlot] = 0;
        _wheelSuspensionLengthsCm[wheelSlot] = 0f;
        _wheelCompressionCm[wheelSlot] = 0f;
        _wheelContactPointsCm[wheelSlot] = default;
        _wheelContactNormals[wheelSlot] = default;
        _wheelSlipVelocities[wheelSlot] = default;
        _wheelLongitudinalSpeeds[wheelSlot] = 0f;
        _wheelLateralSpeeds[wheelSlot] = 0f;
        _wheelSuspensionForces[wheelSlot] = 0f;
        _wheelAngularSpeeds[wheelSlot] = 0f;
        ClearConstraintIds(wheelSlot);
    }

    private void ClearConstraintIds(int wheelSlot)
    {
        _wheelLineConstraints[wheelSlot] = default;
        _wheelSuspensionServos[wheelSlot] = default;
        _wheelTravelLimits[wheelSlot] = default;
        _wheelSteeringServos[wheelSlot] = default;
        _wheelHubConstraints[wheelSlot] = default;
        _wheelAxleHinges[wheelSlot] = default;
        _wheelAxleMotors[wheelSlot] = default;
    }

    private bool HasPhysicalWheel(int wheelSlot)
        => _wheelKinds[wheelSlot] is Vehicle3DWheelKind.Physical or Vehicle3DWheelKind.Box;

    private void RequireDynamicBody(Physics3DBodyId body, string parameterName)
    {
        if (!_physics.ContainsBody(body))
        {
            throw new InvalidOperationException($"{parameterName} references missing or stale body {body}.");
        }

        if (_physics.GetBodyKind(body) != Physics3DBodyKind.Dynamic)
        {
            throw new InvalidOperationException($"{parameterName} body {body} must be dynamic.");
        }
    }

    private void RequireDynamicBodyAtRuntime(Physics3DBodyId body, string role, int ownerSlot)
    {
        if (!_physics.ContainsBody(body))
        {
            throw new InvalidOperationException(
                $"Vehicle3D {role} body {body} for runtime slot {ownerSlot} is missing or stale.");
        }

        if (_physics.GetBodyKind(body) != Physics3DBodyKind.Dynamic)
        {
            throw new InvalidOperationException(
                $"Vehicle3D {role} body {body} for runtime slot {ownerSlot} must be dynamic.");
        }
    }

    private int RequireVehicleSlot(Vehicle3DVehicleId vehicle)
    {
        if (!ContainsVehicle(vehicle))
        {
            throw new InvalidOperationException($"Vehicle id {vehicle} is missing or stale.");
        }

        return vehicle.Slot;
    }

    private int RequireWheelSlot(Vehicle3DWheelId wheel)
    {
        if (wheel.Slot < 0 ||
            wheel.Slot >= _wheelActive.Length ||
            _wheelActive[wheel.Slot] == 0 ||
            _wheelGenerations[wheel.Slot] != wheel.Generation)
        {
            throw new InvalidOperationException($"Wheel id {wheel} is missing or stale.");
        }

        return wheel.Slot;
    }

    private static int NextGeneration(int generation) => generation == int.MaxValue ? 1 : generation + 1;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
