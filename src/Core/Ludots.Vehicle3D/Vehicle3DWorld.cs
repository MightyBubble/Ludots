using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Vehicle3D;

/// <summary>
/// Fixed-capacity Vehicle3D state and command producer. The caller owns the Physics3D step.
/// </summary>
public sealed class Vehicle3DWorld : IDisposable
{
    private const int ConstraintsPerPhysicalWheel = 5;
    private const float ActiveDriveInputThreshold = 1e-5f;

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
    private readonly byte[] _vehicleDriveWakeRequests;
    private readonly long[] _vehicleInputStepIndices;
    private readonly Vector3[] _vehiclePositionsCm;
    private readonly Quaternion[] _vehicleOrientations;

    private readonly byte[] _wheelActive;
    private readonly int[] _wheelGenerations;
    private readonly int[] _wheelFree;
    private readonly int[] _wheelVehicleSlots;
    private readonly Vehicle3DWheelKind[] _wheelKinds;
    private readonly Vehicle3DWheelQueryKind[] _wheelQueryKinds;
    private readonly Physics3DBodyId[] _wheelBodies;
    private readonly Vector3[] _wheelLocalRotationAxes;
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
    private readonly float[] _wheelAlignmentAngularFrequencies;
    private readonly float[] _wheelAlignmentTwiceDampingRatios;
    private readonly float[] _wheelAxleMotorMaximumForces;
    private readonly float[] _wheelAxleMotorSoftnesses;
    private readonly float[] _wheelAppliedSteeringAngles;
    private readonly float[] _wheelAppliedMotorTargetSpeeds;
    private readonly float[] _wheelAppliedMotorMaximumForces;

    private readonly Physics3DConstraintId[] _wheelLineConstraints;
    private readonly Physics3DConstraintId[] _wheelSuspensionServos;
    private readonly Physics3DConstraintId[] _wheelTravelLimits;
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
    private readonly Vector3[] _stageRotationAxleDirections;
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
    private long _lastPreparedStepIndex = -1;
    private bool _stepPrepared;
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
        _vehicleDriveWakeRequests = new byte[vehicleCapacity];
        _vehicleInputStepIndices = new long[vehicleCapacity];
        _vehiclePositionsCm = new Vector3[vehicleCapacity];
        _vehicleOrientations = new Quaternion[vehicleCapacity];

        _wheelActive = new byte[wheelCapacity];
        _wheelGenerations = new int[wheelCapacity];
        _wheelFree = new int[wheelCapacity];
        _wheelVehicleSlots = new int[wheelCapacity];
        _wheelKinds = new Vehicle3DWheelKind[wheelCapacity];
        _wheelQueryKinds = new Vehicle3DWheelQueryKind[wheelCapacity];
        _wheelBodies = new Physics3DBodyId[wheelCapacity];
        _wheelLocalRotationAxes = new Vector3[wheelCapacity];
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
        _wheelAlignmentAngularFrequencies = new float[wheelCapacity];
        _wheelAlignmentTwiceDampingRatios = new float[wheelCapacity];
        _wheelAxleMotorMaximumForces = new float[wheelCapacity];
        _wheelAxleMotorSoftnesses = new float[wheelCapacity];
        _wheelAppliedSteeringAngles = new float[wheelCapacity];
        _wheelAppliedMotorTargetSpeeds = new float[wheelCapacity];
        _wheelAppliedMotorMaximumForces = new float[wheelCapacity];

        _wheelLineConstraints = new Physics3DConstraintId[wheelCapacity];
        _wheelSuspensionServos = new Physics3DConstraintId[wheelCapacity];
        _wheelTravelLimits = new Physics3DConstraintId[wheelCapacity];
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
        _stageRotationAxleDirections = new Vector3[wheelCapacity];
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
            _vehicleInputStepIndices[i] = -1;
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
    public long LastPreparedStepIndex => _lastPreparedStepIndex;
    public bool IsFixedStepPrepared => _stepPrepared;

    public Vehicle3DVehicleId RegisterVehicle(
        Physics3DBodyId chassisBody,
        ReadOnlySpan<Vehicle3DWheelDescription> wheels,
        Span<Vehicle3DWheelId> registeredWheels)
    {
        ThrowIfDisposed();
        ThrowIfTopologyChangeDuringPreparedStep("register a vehicle");
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

            RequireDynamicBody(description.WheelBody, $"{nameof(wheels)}[{i}].{nameof(description.WheelBody)}");
            if (description.WheelBody == chassisBody)
            {
                throw new ArgumentException("A physical wheel body must be distinct from the chassis body.", nameof(wheels));
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
        ThrowIfTopologyChangeDuringPreparedStep("remove a vehicle");
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
        _vehicleInputStepIndices[vehicleSlot] = -1;
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
        if (_stepPrepared)
        {
            throw new InvalidOperationException(
                $"Vehicle3D input for Physics3D step {_physics.StepIndex} cannot change because that step is already prepared.");
        }

        int slot = RequireVehicleSlot(vehicle);
        _vehicleThrottle[slot] = input.Throttle;
        _vehicleBrake[slot] = input.Brake;
        _vehicleSteering[slot] = input.Steering;
        _vehicleInputStepIndices[slot] = _physics.StepIndex;
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

    public void PrepareFixedStep()
    {
        ThrowIfDisposed();
        if (_stepPrepared)
        {
            string failure = _physics.StepIndex == _lastPreparedStepIndex
                ? $"Vehicle3D Physics3D step {_lastPreparedStepIndex} is already prepared."
                : $"Vehicle3D Physics3D step {_lastPreparedStepIndex} was prepared but not observed.";
            throw new InvalidOperationException(failure);
        }

        if (_lastPreparedStepIndex >= 0 && _physics.StepIndex != _lastPreparedStepIndex + 1)
        {
            throw new InvalidOperationException(
                $"Vehicle3D expected Physics3D step {_lastPreparedStepIndex + 1}, but current step is {_physics.StepIndex}.");
        }

        ValidateInputSubmissions();
        CacheAndValidateBodies();
        KeepDrivenVehiclesAwake();
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
        _lastPreparedStepIndex = _physics.StepIndex;
        _stepPrepared = true;
    }

    public void ObserveFixedStep()
    {
        ThrowIfDisposed();
        if (!_stepPrepared)
        {
            throw new InvalidOperationException("Vehicle3D fixed step is not prepared.");
        }

        if (_physics.StepIndex <= _lastPreparedStepIndex)
        {
            throw new InvalidOperationException(
                $"Vehicle3D observation requires Physics3D to advance beyond prepared step {_lastPreparedStepIndex}; current step is {_physics.StepIndex}.");
        }

        if (_physics.StepIndex != _lastPreparedStepIndex + 1)
        {
            throw new InvalidOperationException(
                $"Vehicle3D observation expected Physics3D step {_lastPreparedStepIndex + 1}, but current step is {_physics.StepIndex}.");
        }

        _stepPrepared = false;
    }

    private void ValidateInputSubmissions()
    {
        for (int vehicleSlot = 0; vehicleSlot < _vehicleActive.Length; vehicleSlot++)
        {
            if (_vehicleActive[vehicleSlot] != 0 && _vehicleInputStepIndices[vehicleSlot] != _physics.StepIndex)
            {
                throw new InvalidOperationException(
                    $"Vehicle3D vehicle slot {vehicleSlot} has no input for Physics3D step {_physics.StepIndex}.");
            }
        }
    }

    private void ThrowIfTopologyChangeDuringPreparedStep(string operation)
    {
        if (_stepPrepared)
        {
            throw new InvalidOperationException(
                $"Vehicle3D cannot {operation} because Physics3D step {_lastPreparedStepIndex} was prepared but not observed.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_stepPrepared)
        {
            throw new InvalidOperationException(
                $"Vehicle3D Physics3D step {_lastPreparedStepIndex} was prepared but not observed; disposal is not allowed.");
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
            _stageRotationAxleDirections[wheelSlot] = axle;
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
                float radiusCm = _wheelRadiiCm[wheelSlot];
                _sphereRequests[sphereCount] = new Physics3DSphereCastQuery(
                    _stageOriginsCm[wheelSlot] - (_stageSuspensionDirections[wheelSlot] * radiusCm),
                    radiusCm,
                    _stageSuspensionDirections[wheelSlot],
                    _wheelMaximumLengthsCm[wheelSlot] + radiusCm,
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

    private void KeepDrivenVehiclesAwake()
    {
        Array.Clear(_vehicleDriveWakeRequests);
        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0 || !HasPhysicalWheel(wheelSlot))
            {
                continue;
            }

            int vehicleSlot = _wheelVehicleSlots[wheelSlot];
            float wheelBrake = _vehicleBrake[vehicleSlot] * _wheelBrakeScale[wheelSlot];
            float wheelThrottle = _vehicleThrottle[vehicleSlot] * _wheelDriveScale[wheelSlot];
            if (wheelBrake <= 0f && MathF.Abs(wheelThrottle) > ActiveDriveInputThreshold)
            {
                _vehicleDriveWakeRequests[vehicleSlot] = 1;
            }
        }

        for (int vehicleSlot = 0; vehicleSlot < _vehicleActive.Length; vehicleSlot++)
        {
            if (_vehicleActive[vehicleSlot] == 0 || _vehicleDriveWakeRequests[vehicleSlot] == 0)
            {
                continue;
            }

            _physics.SetBodyAwake(_vehicleChassis[vehicleSlot], true);
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
                Physics3DBodyState chassis = _physics.GetBodyState(_vehicleChassis[_wheelVehicleSlots[wheelSlot]]);
                Physics3DBodyState wheel = _physics.GetBodyState(_wheelBodies[wheelSlot]);
                _stageAngularSpeeds[wheelSlot] = Vector3.Dot(
                    wheel.AngularVelocityRadiansPerSecond - chassis.AngularVelocityRadiansPerSecond,
                    _stageRotationAxleDirections[wheelSlot]);
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
            if (groundBody == _wheelBodies[wheelSlot])
            {
                throw new InvalidOperationException(
                    $"Wheel slot {wheelSlot} hit its own assembly body {groundBody}; ground query layers must exclude vehicle bodies.");
            }

            int vehicleSlot = _wheelVehicleSlots[wheelSlot];
            bool physicalWheel = HasPhysicalWheel(wheelSlot);
            Physics3DBodyId impulseBody = physicalWheel
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
            _stageCompressionCm[wheelSlot] = compression;
            _stageSlipVelocities[wheelSlot] = (forward * longitudinalSpeed) + (axle * lateralSpeed);
            _stageLongitudinalSpeeds[wheelSlot] = longitudinalSpeed;
            _stageLateralSpeeds[wheelSlot] = lateralSpeed;
            _stageSuspensionForces[wheelSlot] = suspensionForce;

            if (physicalWheel)
            {
                // Physical and box wheels are driven by the axle motor and Bepu contact friction.
                // Their queries provide telemetry only; adding a tire impulse here would duplicate contact forces.
                continue;
            }

            float wheelBrake = _vehicleBrake[vehicleSlot] * _wheelBrakeScale[wheelSlot];
            float wheelThrottle = wheelBrake > 0f
                ? 0f
                : _vehicleThrottle[vehicleSlot] * _wheelDriveScale[wheelSlot];
            float driveForceLimit = MathF.Abs(wheelThrottle) * _wheelMaximumDriveForce[wheelSlot];
            float targetLongitudinalSpeed = wheelThrottle *
                                            _wheelMaximumAngularSpeed[wheelSlot] *
                                            _wheelRadiiCm[wheelSlot];
            float driveForce = Math.Clamp(
                (targetLongitudinalSpeed - longitudinalSpeed) * _wheelLongitudinalGrip[wheelSlot],
                -driveForceLimit,
                driveForceLimit);
            float brakeLimit = wheelBrake * _wheelMaximumBrakeForce[wheelSlot];
            float brakeForce = Math.Clamp(
                -longitudinalSpeed * _wheelLongitudinalGrip[wheelSlot],
                -brakeLimit,
                brakeLimit);
            float lateralForce = Math.Clamp(
                -lateralSpeed * _wheelLateralGrip[wheelSlot],
                -_wheelMaximumLateralForce[wheelSlot],
                _wheelMaximumLateralForce[wheelSlot]);
            Vector3 force = (forward * (driveForce + brakeForce)) +
                            (axle * lateralForce) -
                            (down * suspensionForce);
            Vector3 impulse = force * _fixedDeltaSeconds;
            _stageImpulseBodies[wheelSlot] = impulseBody;
            _stageImpulses[wheelSlot] = impulse;
            _stageAngularSpeeds[wheelSlot] = longitudinalSpeed / _wheelRadiiCm[wheelSlot];

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
            if (steeringAngle != _wheelAppliedSteeringAngles[wheelSlot])
            {
                Quaternion steeringRotation = Quaternion.CreateFromAxisAngle(
                    -_wheelLocalSuspensionDirections[wheelSlot],
                    steeringAngle);
                Vector3 localAxle = Vector3.Normalize(Vector3.Cross(
                    Vector3.Transform(_wheelLocalForwardDirections[wheelSlot], steeringRotation),
                    _wheelLocalSuspensionDirections[wheelSlot]));
                var alignmentSpring = new Physics3DSpringSettings(
                    _wheelAlignmentAngularFrequencies[wheelSlot],
                    _wheelAlignmentTwiceDampingRatios[wheelSlot]);
                var hinge = new Physics3DAngularHingeDescription(
                    localAxle,
                    _wheelLocalRotationAxes[wheelSlot],
                    alignmentSpring);
                _physics.UpdateAngularHinge(_wheelAxleHinges[wheelSlot], hinge);
                _wheelAppliedSteeringAngles[wheelSlot] = steeringAngle;
            }

            float targetAngularSpeed;
            float maximumMotorForce;
            float wheelBrake = _vehicleBrake[vehicleSlot] * _wheelBrakeScale[wheelSlot];
            float wheelThrottle = _vehicleThrottle[vehicleSlot] * _wheelDriveScale[wheelSlot];
            if (wheelBrake > 0f)
            {
                targetAngularSpeed = 0f;
                maximumMotorForce = wheelBrake *
                                    _wheelMaximumBrakeForce[wheelSlot] *
                                    _wheelRadiiCm[wheelSlot];
            }
            else if (MathF.Abs(wheelThrottle) > ActiveDriveInputThreshold)
            {
                targetAngularSpeed = wheelThrottle * _wheelMaximumAngularSpeed[wheelSlot];
                maximumMotorForce = MathF.Abs(wheelThrottle) *
                                    _wheelMaximumDriveForce[wheelSlot] *
                                    _wheelRadiiCm[wheelSlot];
            }
            else
            {
                targetAngularSpeed = 0f;
                maximumMotorForce = 0f;
            }

            maximumMotorForce = MathF.Min(maximumMotorForce, _wheelAxleMotorMaximumForces[wheelSlot]);
            if (targetAngularSpeed != _wheelAppliedMotorTargetSpeeds[wheelSlot] ||
                maximumMotorForce != _wheelAppliedMotorMaximumForces[wheelSlot])
            {
                var motor = new Physics3DAngularAxisMotorDescription(
                    _wheelLocalRotationAxes[wheelSlot],
                    targetAngularSpeed,
                    new Physics3DMotorSettings(maximumMotorForce, _wheelAxleMotorSoftnesses[wheelSlot]));
                _physics.UpdateAngularAxisMotor(_wheelAxleMotors[wheelSlot], motor);
                _wheelAppliedMotorTargetSpeeds[wheelSlot] = targetAngularSpeed;
                _wheelAppliedMotorMaximumForces[wheelSlot] = maximumMotorForce;
            }
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
        StageContactBasis(wheelSlot, hit.Normal);
    }

    private void StageShapeHit(int wheelSlot, in Physics3DShapeCastHit hit)
    {
        _stageGrounded[wheelSlot] = 1;
        _stageGroundBodies[wheelSlot] = hit.Body;
        _stageSuspensionLengthsCm[wheelSlot] = MathF.Max(0f, hit.DistanceCm - _wheelRadiiCm[wheelSlot]);
        _stageContactPointsCm[wheelSlot] = hit.PositionCm;
        StageContactBasis(wheelSlot, hit.Normal);
    }

    private void StageContactBasis(int wheelSlot, Vector3 contactNormal)
    {
        float normalLengthSquared = contactNormal.LengthSquared();
        if (!float.IsFinite(normalLengthSquared) || normalLengthSquared <= 1e-12f)
        {
            throw new InvalidOperationException(
                $"Wheel slot {wheelSlot} received an invalid contact normal '{contactNormal}'.");
        }

        Vector3 normal = contactNormal / MathF.Sqrt(normalLengthSquared);
        Vector3 chassisForward = _stageForwardDirections[wheelSlot];
        Vector3 tangentForward = chassisForward - (normal * Vector3.Dot(chassisForward, normal));
        float tangentLengthSquared = tangentForward.LengthSquared();
        if (!float.IsFinite(tangentLengthSquared) || tangentLengthSquared <= 1e-12f)
        {
            throw new InvalidOperationException(
                $"Wheel slot {wheelSlot} forward direction '{chassisForward}' is degenerate on contact normal '{normal}'.");
        }

        tangentForward /= MathF.Sqrt(tangentLengthSquared);
        Vector3 tangentAxle = Vector3.Cross(normal, tangentForward);
        float axleLengthSquared = tangentAxle.LengthSquared();
        if (!float.IsFinite(axleLengthSquared) || axleLengthSquared <= 1e-12f)
        {
            throw new InvalidOperationException(
                $"Wheel slot {wheelSlot} could not construct a contact-plane axle from normal '{normal}' and forward '{tangentForward}'.");
        }

        _stageContactNormals[wheelSlot] = normal;
        _stageForwardDirections[wheelSlot] = tangentForward;
        _stageAxleDirections[wheelSlot] = tangentAxle / MathF.Sqrt(axleLengthSquared);
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
        _wheelAlignmentAngularFrequencies[wheelSlot] = description.Joint.AlignmentSpring.AngularFrequency;
        _wheelAlignmentTwiceDampingRatios[wheelSlot] = description.Joint.AlignmentSpring.TwiceDampingRatio;
        _wheelAxleMotorMaximumForces[wheelSlot] = description.Joint.AxleMotor.MaximumForce;
        _wheelAxleMotorSoftnesses[wheelSlot] = description.Joint.AxleMotor.Softness;
    }

    private void CreatePhysicalWheelConstraints(
        Physics3DBodyId chassis,
        int wheelSlot,
        in Vehicle3DWheelDescription description)
    {
        Physics3DBodyId wheel = description.WheelBody;
        Vector3 axle = Vector3.Normalize(Vector3.Cross(
            description.LocalForwardDirection,
            description.LocalSuspensionDirection));
        Physics3DBodyState chassisState = _physics.GetBodyState(chassis);
        Physics3DBodyState wheelState = _physics.GetBodyState(wheel);
        Vector3 worldAxle = Vector3.Transform(axle, chassisState.Orientation);
        Vector3 localWheelAxle = Vector3.Normalize(Vector3.Transform(
            worldAxle,
            Quaternion.Conjugate(wheelState.Orientation)));
        _wheelLocalRotationAxes[wheelSlot] = localWheelAxle;

        _wheelLineConstraints[wheelSlot] = _physics.CreatePointOnLineServoConstraint(
            chassis,
            wheel,
            new Physics3DPointOnLineServoDescription(
                description.LocalMountCm,
                Vector3.Zero,
                description.LocalSuspensionDirection,
                description.Joint.LineServo,
                description.Joint.AlignmentSpring));
        _wheelSuspensionServos[wheelSlot] = _physics.CreateLinearAxisServoConstraint(
            chassis,
            wheel,
            new Physics3DLinearAxisServoDescription(
                description.LocalMountCm,
                Vector3.Zero,
                description.LocalSuspensionDirection,
                description.RestLengthCm,
                description.Joint.LineServo,
                description.Joint.SuspensionSpring));
        _wheelTravelLimits[wheelSlot] = _physics.CreateLinearAxisLimitConstraint(
            chassis,
            wheel,
            new Physics3DLinearAxisLimitDescription(
                description.LocalMountCm,
                Vector3.Zero,
                description.LocalSuspensionDirection,
                description.MinimumLengthCm,
                description.MaximumLengthCm,
                description.Joint.LimitSpring));
        _wheelAxleHinges[wheelSlot] = _physics.CreateAngularHingeConstraint(
            chassis,
            wheel,
            new Physics3DAngularHingeDescription(axle, localWheelAxle, description.Joint.AlignmentSpring));
        _wheelAxleMotors[wheelSlot] = _physics.CreateAngularAxisMotorConstraint(
            wheel,
            chassis,
            new Physics3DAngularAxisMotorDescription(localWheelAxle, 0f, default));
    }

    private void DestroyWheelConstraints(int wheelSlot)
    {
        DestroyConstraint(_wheelAxleMotors[wheelSlot]);
        DestroyConstraint(_wheelAxleHinges[wheelSlot]);
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

            if (current.WheelBody == previous.WheelBody)
            {
                throw new ArgumentException("Physical wheel bodies must be unique within a vehicle.", nameof(descriptions));
            }
        }

        for (int wheelSlot = 0; wheelSlot < _wheelActive.Length; wheelSlot++)
        {
            if (_wheelActive[wheelSlot] == 0 || !HasPhysicalWheel(wheelSlot))
            {
                continue;
            }

            if (current.WheelBody == _wheelBodies[wheelSlot])
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
        _wheelBodies[wheelSlot] = default;
        _wheelLocalRotationAxes[wheelSlot] = default;
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
        _wheelAlignmentAngularFrequencies[wheelSlot] = 0f;
        _wheelAlignmentTwiceDampingRatios[wheelSlot] = 0f;
        _wheelAxleMotorMaximumForces[wheelSlot] = 0f;
        _wheelAxleMotorSoftnesses[wheelSlot] = 0f;
        _wheelAppliedSteeringAngles[wheelSlot] = 0f;
        _wheelAppliedMotorTargetSpeeds[wheelSlot] = 0f;
        _wheelAppliedMotorMaximumForces[wheelSlot] = 0f;
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
