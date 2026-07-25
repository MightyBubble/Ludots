using System;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Ludots.Core.Layers;

namespace Ludots.Core.Physics3D;

public sealed class Physics3DWorld : IPhysics3DWorld
{
    private readonly Physics3DWorldConfig _config;
    private readonly BufferPool _bufferPool;
    private readonly IThreadDispatcher _threadDispatcher;
    private readonly IDisposable _threadDispatcherLifetime;
    private readonly Simulation _simulation;
    private readonly Physics3DBodyStore _bodies;
    private readonly Physics3DContactCollector _contacts;
    private readonly Physics3DConstraintStore _constraints;
    private readonly Physics3DShapeCatalog _shapes;
    private readonly Physics3DQueryEngine _queries;
    private readonly Physics3DActuationCommandBuffer _actuationCommands;
    private readonly bool _supportsContactSurfaceVelocity;
    private readonly Physics3DContactSurfaceTimestepper? _productionTimestepper;
    private bool _isStepping;
    private bool _disposed;
    private Exception? _terminalFault;

    public Physics3DWorld(Physics3DWorldConfig config)
        : this(config, CreateDefaultThreadDispatcher(config), null)
    {
    }

    internal Physics3DWorld(Physics3DWorldConfig config, IThreadDispatcher threadDispatcher)
        : this(config, threadDispatcher, null)
    {
    }

    internal Physics3DWorld(
        Physics3DWorldConfig config,
        IThreadDispatcher threadDispatcher,
        ITimestepper? timestepper)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(threadDispatcher);
        _threadDispatcherLifetime = threadDispatcher as IDisposable
            ?? throw new ArgumentException("Physics3D requires an owned disposable thread dispatcher.", nameof(threadDispatcher));
        _config = config;
        _threadDispatcher = threadDispatcher;
        BufferPool? bufferPool = null;
        Simulation? simulation = null;
        try
        {
            config.Validate();
            if (threadDispatcher.ThreadCount != config.WorkerCount)
            {
                throw new ArgumentException(
                    $"Physics3D dispatcher exposes '{threadDispatcher.ThreadCount}' workers, but config requires '{config.WorkerCount}'.",
                    nameof(threadDispatcher));
            }

            bufferPool = new BufferPool(
                expectedPooledResourceCount: config.MemoryPoolExpectedPooledResourceCount);
            _bodies = new Physics3DBodyStore(config.MobileBodyCapacity, config.StaticBodyCapacity);
            _contacts = new Physics3DContactCollector(config.WorkerCount, config.ContactPairCapacityPerWorker);
            _constraints = new Physics3DConstraintStore(
                config.ConstraintCapacity,
                checked(config.MobileBodyCapacity + config.StaticBodyCapacity));
            _actuationCommands = new Physics3DActuationCommandBuffer(
                config.ActuationCommandCapacity,
                checked(config.MobileBodyCapacity + config.StaticBodyCapacity));
            var allocationSizes = new SimulationAllocationSizes(
                config.MobileBodyCapacity,
                config.StaticBodyCapacity,
                config.InactiveIslandCapacity,
                config.ShapeCapacity,
                config.ConstraintCapacity,
                config.ConstraintsPerTypeBatchCapacity,
                config.ConstraintCountPerBodyEstimate);
            ITimestepper effectiveTimestepper;
            if (timestepper is null)
            {
                _productionTimestepper = new Physics3DContactSurfaceTimestepper(_bodies);
                effectiveTimestepper = _productionTimestepper;
                _supportsContactSurfaceVelocity = true;
            }
            else
            {
                _productionTimestepper = null;
                effectiveTimestepper = timestepper;
                _supportsContactSurfaceVelocity = false;
            }

            simulation = Simulation.Create(
                bufferPool,
                new Physics3DNarrowPhaseCallbacks(_bodies, _contacts, config.MaterialCombineMode),
                new Physics3DPoseIntegratorCallbacks(config.GravityCmPerSecondSquared, config.LinearDamping, config.AngularDamping),
                new SolveDescription(config.SolverVelocityIterationCount, config.SolverSubstepCount),
                timestepper: effectiveTimestepper,
                initialAllocationSizes: allocationSizes);
            simulation.Deterministic = true;
            _shapes = new Physics3DShapeCatalog(simulation, config.ShapeCapacity);
            _queries = new Physics3DQueryEngine(simulation, bufferPool, _bodies);
            _bufferPool = bufferPool;
            _simulation = simulation;
        }
        catch
        {
            simulation?.Dispose();
            bufferPool?.Clear();
            _threadDispatcherLifetime.Dispose();
            throw;
        }
    }

    public int ActiveBodyCount => _bodies.ActiveBodyCount;
    public int ActiveMobileBodyCount => _bodies.ActiveMobileBodyCount;
    public int ActiveStaticBodyCount => _bodies.ActiveStaticBodyCount;
    public int AwakeBodyCount => _simulation.Bodies.ActiveSet.Count;
    public int RegisteredShapeCount => _shapes.Count;
    public int ContactPairCount => _contacts.Count;
    public int ContactEventCount => _contacts.EventCount;
    public int ActiveConstraintCount => _constraints.Count;
    public int ActuationCommandCapacity => _actuationCommands.Capacity;
    public int PendingActuationCommandCount => _actuationCommands.Count;
    public int WorkerCount => _threadDispatcher.ThreadCount;
    public long StepIndex { get; private set; }
    public Physics3DStepMetrics LastStepMetrics { get; private set; }
    public float FixedDeltaSeconds => _config.FixedDeltaSeconds;
    public Vector3 GravityCmPerSecondSquared => _config.GravityCmPerSecondSquared;

    /// <inheritdoc cref="IPhysics3DWorld.IsTerminalFaulted"/>
    public bool IsTerminalFaulted => _terminalFault is not null;

    /// <inheritdoc cref="IPhysics3DWorld.TerminalFault"/>
    public Exception? TerminalFault => _terminalFault;

    public Physics3DShapeId RegisterBoxShape(Vector3 sizeCm)
    {
        RequireStructuralPhase();
        return _shapes.RegisterBox(sizeCm);
    }

    public Physics3DShapeId RegisterSphereShape(float radiusCm)
    {
        RequireStructuralPhase();
        return _shapes.RegisterSphere(radiusCm);
    }

    public Physics3DShapeId RegisterCapsuleShape(float radiusCm, float cylinderLengthCm)
    {
        RequireStructuralPhase();
        return _shapes.RegisterCapsule(radiusCm, cylinderLengthCm);
    }

    public Physics3DShapeId RegisterCylinderShape(float radiusCm, float lengthCm)
    {
        RequireStructuralPhase();
        return _shapes.RegisterCylinder(radiusCm, lengthCm);
    }

    public Physics3DBodyId CreateBody(in Physics3DBodyDescription description)
    {
        RequireStructuralPhase();
        ValidateBodyDescription(in description);
        TypedIndex shape = _shapes.RequireTypedIndex(description.Shape);
        Quaternion orientation = Physics3DValidation.NormalizeOrientation(description.Orientation, nameof(description.Orientation));
        int slot = _bodies.AllocateSlot(description.Kind);
        bool simulationObjectCreated = false;
        int bepuHandle = -1;
        try
        {
            RigidPose pose = new(description.PositionCm, orientation);
            ContinuousDetection continuity = CreateContinuity(description.ContinuousDetection);
            if (description.Kind == Physics3DBodyKind.Static)
            {
                StaticHandle handle = _simulation.Statics.Add(new StaticDescription(pose, shape, continuity));
                simulationObjectCreated = true;
                bepuHandle = handle.Value;
                _bodies.BindStatic(slot, handle, in description);
            }
            else
            {
                var velocity = new BodyVelocity(
                    description.LinearVelocityCmPerSecond,
                    description.AngularVelocityRadiansPerSecond);
                var collidable = new CollidableDescription(
                    shape,
                    0f,
                    _config.MaximumSpeculativeMarginCm,
                    continuity);
                var activity = new BodyActivityDescription(
                    _config.SleepThreshold,
                    _config.MinimumTimestepCountUnderSleepThreshold);
                BodyDescription bodyDescription = description.Kind == Physics3DBodyKind.Dynamic
                    ? BodyDescription.CreateDynamic(
                        pose,
                        velocity,
                        _shapes.ComputeInertia(description.Shape, description.Mass),
                        collidable,
                        activity)
                    : BodyDescription.CreateKinematic(pose, velocity, collidable, activity);
                BodyHandle handle = _simulation.Bodies.Add(bodyDescription);
                simulationObjectCreated = true;
                bepuHandle = handle.Value;
                _bodies.BindMobile(slot, handle, in description);
            }

            return _bodies.GetId(slot);
        }
        catch
        {
            if (simulationObjectCreated)
            {
                if (description.Kind == Physics3DBodyKind.Static)
                {
                    _simulation.Statics.Remove(new StaticHandle(bepuHandle));
                }
                else
                {
                    _simulation.Bodies.Remove(new BodyHandle(bepuHandle));
                }
            }

            _bodies.RollbackSlot(slot);
            throw;
        }
    }

    public void DestroyBody(Physics3DBodyId body)
    {
        RequireStructuralPhase();
        int slot = _bodies.RequireSlot(body);
        _contacts.RemoveBody(slot, StepIndex);
        _constraints.RemoveAllForBody(slot, _simulation);
        int handle = _bodies.GetBepuHandle(slot);
        if (_bodies.GetBodyKind(slot) == Physics3DBodyKind.Static)
        {
            _simulation.Statics.Remove(new StaticHandle(handle));
        }
        else
        {
            _simulation.Bodies.Remove(new BodyHandle(handle));
        }

        _bodies.Release(body);
    }

    public bool ContainsBody(Physics3DBodyId body)
    {
        ThrowIfDisposed();
        return _bodies.Contains(body);
    }

    public Physics3DBodyKind GetBodyKind(Physics3DBodyId body)
    {
        ThrowIfDisposed();
        return _bodies.GetBodyKind(_bodies.RequireSlot(body));
    }

    public Physics3DBodyContactPolicy GetBodyContactPolicy(Physics3DBodyId body)
    {
        ThrowIfDisposed();
        int slot = _bodies.RequireSlot(body);
        return _bodies.GetContactPolicy(slot);
    }

    public Physics3DCollisionSubgroup GetBodyCollisionSubgroup(Physics3DBodyId body)
    {
        ThrowIfDisposed();
        int slot = _bodies.RequireSlot(body);
        return _bodies.GetCollisionSubgroup(slot);
    }

    public Physics3DBodyState GetBodyState(Physics3DBodyId body)
    {
        ThrowIfDisposed();
        int slot = _bodies.RequireSlot(body);
        int handle = _bodies.GetBepuHandle(slot);
        if (_bodies.GetBodyKind(slot) == Physics3DBodyKind.Static)
        {
            StaticReference reference = _simulation.Statics.GetStaticReference(new StaticHandle(handle));
            return new Physics3DBodyState
            {
                PositionCm = reference.Pose.Position,
                Orientation = reference.Pose.Orientation,
                Awake = false
            };
        }

        BodyReference bodyReference = _simulation.Bodies.GetBodyReference(new BodyHandle(handle));
        return new Physics3DBodyState
        {
            PositionCm = bodyReference.Pose.Position,
            Orientation = bodyReference.Pose.Orientation,
            LinearVelocityCmPerSecond = bodyReference.Velocity.Linear,
            AngularVelocityRadiansPerSecond = bodyReference.Velocity.Angular,
            Awake = bodyReference.Awake
        };
    }

    public void SetBodyState(Physics3DBodyId body, in Physics3DBodyState state)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(state.PositionCm, nameof(state.PositionCm));
        Physics3DValidation.RequireFinite(state.LinearVelocityCmPerSecond, nameof(state.LinearVelocityCmPerSecond));
        Physics3DValidation.RequireFinite(state.AngularVelocityRadiansPerSecond, nameof(state.AngularVelocityRadiansPerSecond));
        Quaternion orientation = Physics3DValidation.NormalizeOrientation(state.Orientation, nameof(state.Orientation));
        int slot = _bodies.RequireSlot(body);
        int handle = _bodies.GetBepuHandle(slot);
        if (_bodies.GetBodyKind(slot) == Physics3DBodyKind.Static)
        {
            if (state.LinearVelocityCmPerSecond != Vector3.Zero || state.AngularVelocityRadiansPerSecond != Vector3.Zero || state.Awake)
            {
                throw new InvalidOperationException("Static Physics3D bodies cannot have velocity or an awake state.");
            }

            StaticReference reference = _simulation.Statics.GetStaticReference(new StaticHandle(handle));
            reference.GetDescription(out StaticDescription description);
            description.Pose = new RigidPose(state.PositionCm, orientation);
            reference.ApplyDescription(description);
            return;
        }

        BodyReference bodyReference = _simulation.Bodies.GetBodyReference(new BodyHandle(handle));
        SetAwake(bodyReference, true);
        bodyReference.Pose = new RigidPose(state.PositionCm, orientation);
        bodyReference.Velocity = new BodyVelocity(
            state.LinearVelocityCmPerSecond,
            state.AngularVelocityRadiansPerSecond);
        bodyReference.UpdateBounds();
        SetAwake(bodyReference, state.Awake);
    }

    public void SetBodyAwake(Physics3DBodyId body, bool awake)
    {
        RequireStructuralPhase();
        int slot = _bodies.RequireSlot(body);
        if (_bodies.GetBodyKind(slot) == Physics3DBodyKind.Static)
        {
            throw new InvalidOperationException("Static Physics3D bodies do not have an awake state.");
        }

        BodyReference bodyReference = _simulation.Bodies.GetBodyReference(new BodyHandle(_bodies.GetBepuHandle(slot)));
        SetAwake(bodyReference, awake);
    }

    public void SetBodyVelocity(
        Physics3DBodyId body,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(linearVelocityCmPerSecond, nameof(linearVelocityCmPerSecond));
        Physics3DValidation.RequireFinite(angularVelocityRadiansPerSecond, nameof(angularVelocityRadiansPerSecond));
        int slot = _bodies.RequireSlot(body);
        if (_bodies.GetBodyKind(slot) == Physics3DBodyKind.Static)
        {
            throw new InvalidOperationException("Static Physics3D bodies cannot have velocity.");
        }

        BodyReference bodyReference = _simulation.Bodies.GetBodyReference(new BodyHandle(_bodies.GetBepuHandle(slot)));
        SetAwake(bodyReference, true);
        bodyReference.Velocity = new BodyVelocity(linearVelocityCmPerSecond, angularVelocityRadiansPerSecond);
    }

    public void SetKinematicNextPose(
        Physics3DBodyId body,
        Vector3 nextPositionCm,
        Quaternion nextOrientation)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(nextPositionCm, nameof(nextPositionCm));
        Quaternion normalizedNextOrientation = Physics3DValidation.NormalizeOrientation(
            nextOrientation,
            nameof(nextOrientation));
        int slot = _bodies.RequireSlot(body);
        if (_bodies.GetBodyKind(slot) != Physics3DBodyKind.Kinematic)
        {
            throw new InvalidOperationException(
                $"Physics3D next-pose motion requires a kinematic body; '{body}' is '{_bodies.GetBodyKind(slot)}'.");
        }

        BodyReference bodyReference = _simulation.Bodies.GetBodyReference(
            new BodyHandle(_bodies.GetBepuHandle(slot)));
        Vector3 linearVelocity = (nextPositionCm - bodyReference.Pose.Position) / _config.FixedDeltaSeconds;
        Quaternion delta = Quaternion.Normalize(
            normalizedNextOrientation * Quaternion.Conjugate(bodyReference.Pose.Orientation));
        if (delta.W < 0f)
        {
            delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        }

        float sinHalfAngle = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
        Vector3 angularVelocity = sinHalfAngle <= 1e-7f
            ? new Vector3(delta.X, delta.Y, delta.Z) * (2f / _config.FixedDeltaSeconds)
            : new Vector3(delta.X, delta.Y, delta.Z) *
              (2f * MathF.Atan2(sinHalfAngle, delta.W) / (sinHalfAngle * _config.FixedDeltaSeconds));
        Physics3DValidation.RequireFinite(linearVelocity, nameof(linearVelocity));
        Physics3DValidation.RequireFinite(angularVelocity, nameof(angularVelocity));
        SetAwake(bodyReference, true);
        bodyReference.Velocity = new BodyVelocity(linearVelocity, angularVelocity);
    }

    public Vector3 GetBodyVelocityAtWorldPoint(Physics3DBodyId body, Vector3 worldPointCm)
    {
        ThrowIfDisposed();
        Physics3DValidation.RequireFinite(worldPointCm, nameof(worldPointCm));
        int slot = _bodies.RequireSlot(body);
        if (_bodies.GetBodyKind(slot) == Physics3DBodyKind.Static)
        {
            return Vector3.Zero;
        }

        BodyReference bodyReference = _simulation.Bodies.GetBodyReference(
            new BodyHandle(_bodies.GetBepuHandle(slot)));
        return bodyReference.Velocity.Linear + Vector3.Cross(
            bodyReference.Velocity.Angular,
            worldPointCm - bodyReference.Pose.Position);
    }

    public void EnqueueForce(Physics3DBodyId body, Vector3 forceMassCmPerSecondSquared)
    {
        EnqueueActuation(body, Physics3DActuationKind.Force, forceMassCmPerSecondSquared);
    }

    public void EnqueueAcceleration(Physics3DBodyId body, Vector3 accelerationCmPerSecondSquared)
    {
        EnqueueActuation(body, Physics3DActuationKind.Acceleration, accelerationCmPerSecondSquared);
    }

    public void EnqueueTorque(Physics3DBodyId body, Vector3 torqueMassCmSquaredPerSecondSquared)
    {
        EnqueueActuation(body, Physics3DActuationKind.Torque, torqueMassCmSquaredPerSecondSquared);
    }

    public void EnqueueLinearImpulse(Physics3DBodyId body, Vector3 impulseMassCmPerSecond)
    {
        EnqueueActuation(body, Physics3DActuationKind.LinearImpulse, impulseMassCmPerSecond);
    }

    public void EnqueueAngularImpulse(Physics3DBodyId body, Vector3 impulseMassCmSquaredPerSecond)
    {
        EnqueueActuation(body, Physics3DActuationKind.AngularImpulse, impulseMassCmSquaredPerSecond);
    }

    public void EnqueueImpulseAtWorldPoint(
        Physics3DBodyId body,
        Vector3 impulseMassCmPerSecond,
        Vector3 worldPointCm)
    {
        RequireStructuralPhase();
        RequireDynamicBody(body);
        Physics3DValidation.RequireFinite(impulseMassCmPerSecond, nameof(impulseMassCmPerSecond));
        Physics3DValidation.RequireFinite(worldPointCm, nameof(worldPointCm));
        _actuationCommands.Enqueue(
            body,
            Physics3DActuationKind.ImpulseAtWorldPoint,
            impulseMassCmPerSecond,
            worldPointCm);
    }

    public void ClearActuationCommands()
    {
        RequireStructuralPhase();
        _actuationCommands.Clear();
    }

    public Physics3DConstraintId CreateBallSocketConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        Vector3 localOffsetA,
        Vector3 localOffsetB,
        in Physics3DSpringSettings spring)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(localOffsetA, nameof(localOffsetA));
        Physics3DValidation.RequireFinite(localOffsetB, nameof(localOffsetB));
        spring.Validate(nameof(spring));
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var description = new BallSocket
        {
            LocalOffsetA = localOffsetA,
            LocalOffsetB = localOffsetB,
            SpringSettings = CreateSpringSettings(spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.BallSocket, description);
    }

    public Physics3DConstraintId CreateHingeConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        Vector3 localOffsetA,
        Vector3 localHingeAxisA,
        Vector3 localOffsetB,
        Vector3 localHingeAxisB,
        in Physics3DSpringSettings spring)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(localOffsetA, nameof(localOffsetA));
        Physics3DValidation.RequireFinite(localOffsetB, nameof(localOffsetB));
        Vector3 axisA = NormalizeDirection(localHingeAxisA, nameof(localHingeAxisA));
        Vector3 axisB = NormalizeDirection(localHingeAxisB, nameof(localHingeAxisB));
        spring.Validate(nameof(spring));
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var description = new Hinge
        {
            LocalOffsetA = localOffsetA,
            LocalHingeAxisA = axisA,
            LocalOffsetB = localOffsetB,
            LocalHingeAxisB = axisB,
            SpringSettings = CreateSpringSettings(spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.Hinge, description);
    }

    public Physics3DConstraintId CreateWeldConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        Vector3 localOffsetFromAToB,
        Quaternion localOrientationOfBInA,
        in Physics3DSpringSettings spring)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(localOffsetFromAToB, nameof(localOffsetFromAToB));
        Quaternion orientation = Physics3DValidation.NormalizeOrientation(localOrientationOfBInA, nameof(localOrientationOfBInA));
        spring.Validate(nameof(spring));
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var description = new Weld
        {
            LocalOffset = localOffsetFromAToB,
            LocalOrientation = orientation,
            SpringSettings = CreateSpringSettings(spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.Weld, description);
    }

    public Physics3DConstraintId CreatePointOnLineServoConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DPointOnLineServoDescription description)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(description.LocalOffsetACm, $"{nameof(description)}.{nameof(description.LocalOffsetACm)}");
        Physics3DValidation.RequireFinite(description.LocalOffsetBCm, $"{nameof(description)}.{nameof(description.LocalOffsetBCm)}");
        Vector3 direction = NormalizeDirection(description.LocalDirectionA, $"{nameof(description)}.{nameof(description.LocalDirectionA)}");
        description.Servo.Validate($"{nameof(description)}.{nameof(description.Servo)}");
        description.Spring.Validate($"{nameof(description)}.{nameof(description.Spring)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new PointOnLineServo
        {
            LocalOffsetA = description.LocalOffsetACm,
            LocalOffsetB = description.LocalOffsetBCm,
            LocalDirection = direction,
            ServoSettings = CreateServoSettings(description.Servo),
            SpringSettings = CreateSpringSettings(description.Spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.PointOnLineServo, bepuDescription);
    }

    public Physics3DConstraintId CreateLinearAxisServoConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DLinearAxisServoDescription description)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(description.LocalOffsetACm, $"{nameof(description)}.{nameof(description.LocalOffsetACm)}");
        Physics3DValidation.RequireFinite(description.LocalOffsetBCm, $"{nameof(description)}.{nameof(description.LocalOffsetBCm)}");
        Vector3 axis = NormalizeDirection(description.LocalAxisA, $"{nameof(description)}.{nameof(description.LocalAxisA)}");
        Physics3DValidation.RequireFinite(description.TargetOffsetCm, $"{nameof(description)}.{nameof(description.TargetOffsetCm)}");
        description.Servo.Validate($"{nameof(description)}.{nameof(description.Servo)}");
        description.Spring.Validate($"{nameof(description)}.{nameof(description.Spring)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new LinearAxisServo
        {
            LocalOffsetA = description.LocalOffsetACm,
            LocalOffsetB = description.LocalOffsetBCm,
            LocalPlaneNormal = axis,
            TargetOffset = description.TargetOffsetCm,
            ServoSettings = CreateServoSettings(description.Servo),
            SpringSettings = CreateSpringSettings(description.Spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.LinearAxisServo, bepuDescription);
    }

    public Physics3DConstraintId CreateLinearAxisLimitConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DLinearAxisLimitDescription description)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(description.LocalOffsetACm, $"{nameof(description)}.{nameof(description.LocalOffsetACm)}");
        Physics3DValidation.RequireFinite(description.LocalOffsetBCm, $"{nameof(description)}.{nameof(description.LocalOffsetBCm)}");
        Vector3 axis = NormalizeDirection(description.LocalAxisA, $"{nameof(description)}.{nameof(description.LocalAxisA)}");
        Physics3DValidation.RequireFinite(description.MinimumOffsetCm, $"{nameof(description)}.{nameof(description.MinimumOffsetCm)}");
        Physics3DValidation.RequireFinite(description.MaximumOffsetCm, $"{nameof(description)}.{nameof(description.MaximumOffsetCm)}");
        if (description.MaximumOffsetCm < description.MinimumOffsetCm)
        {
            throw new ArgumentOutOfRangeException(
                $"{nameof(description)}.{nameof(description.MaximumOffsetCm)}",
                description.MaximumOffsetCm,
                "Maximum offset must be greater than or equal to minimum offset.");
        }

        description.Spring.Validate($"{nameof(description)}.{nameof(description.Spring)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new LinearAxisLimit
        {
            LocalOffsetA = description.LocalOffsetACm,
            LocalOffsetB = description.LocalOffsetBCm,
            LocalAxis = axis,
            MinimumOffset = description.MinimumOffsetCm,
            MaximumOffset = description.MaximumOffsetCm,
            SpringSettings = CreateSpringSettings(description.Spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.LinearAxisLimit, bepuDescription);
    }

    public Physics3DConstraintId CreateAngularHingeConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DAngularHingeDescription description)
    {
        RequireStructuralPhase();
        Vector3 axisA = NormalizeDirection(description.LocalHingeAxisA, $"{nameof(description)}.{nameof(description.LocalHingeAxisA)}");
        Vector3 axisB = NormalizeDirection(description.LocalHingeAxisB, $"{nameof(description)}.{nameof(description.LocalHingeAxisB)}");
        description.Spring.Validate($"{nameof(description)}.{nameof(description.Spring)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new AngularHinge
        {
            LocalHingeAxisA = axisA,
            LocalHingeAxisB = axisB,
            SpringSettings = CreateSpringSettings(description.Spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.AngularHinge, bepuDescription);
    }

    public Physics3DConstraintId CreateAngularAxisMotorConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DAngularAxisMotorDescription description)
    {
        RequireStructuralPhase();
        Vector3 axis = NormalizeDirection(description.LocalAxisA, $"{nameof(description)}.{nameof(description.LocalAxisA)}");
        Physics3DValidation.RequireFinite(description.TargetVelocityRadiansPerSecond, $"{nameof(description)}.{nameof(description.TargetVelocityRadiansPerSecond)}");
        description.Motor.Validate($"{nameof(description)}.{nameof(description.Motor)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new AngularAxisMotor
        {
            LocalAxisA = axis,
            TargetVelocity = description.TargetVelocityRadiansPerSecond,
            Settings = CreateMotorSettings(description.Motor)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.AngularAxisMotor, bepuDescription);
    }

    public Physics3DConstraintId CreateSwingLimitConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DSwingLimitDescription description)
    {
        RequireStructuralPhase();
        Vector3 axisA = NormalizeDirection(description.LocalAxisA, $"{nameof(description)}.{nameof(description.LocalAxisA)}");
        Vector3 axisB = NormalizeDirection(description.LocalAxisB, $"{nameof(description)}.{nameof(description.LocalAxisB)}");
        Physics3DValidation.RequireFinite(description.MaximumSwingAngleRadians, $"{nameof(description)}.{nameof(description.MaximumSwingAngleRadians)}");
        if (description.MaximumSwingAngleRadians < 0f || description.MaximumSwingAngleRadians > MathF.PI)
        {
            throw new ArgumentOutOfRangeException(
                $"{nameof(description)}.{nameof(description.MaximumSwingAngleRadians)}",
                description.MaximumSwingAngleRadians,
                "Maximum swing angle must be between zero and PI radians inclusive.");
        }

        description.Spring.Validate($"{nameof(description)}.{nameof(description.Spring)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new SwingLimit
        {
            AxisLocalA = axisA,
            AxisLocalB = axisB,
            MaximumSwingAngle = description.MaximumSwingAngleRadians,
            SpringSettings = CreateSpringSettings(description.Spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.SwingLimit, bepuDescription);
    }

    public Physics3DConstraintId CreateTwistLimitConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DTwistLimitDescription description)
    {
        RequireStructuralPhase();
        Quaternion basisA = Physics3DValidation.NormalizeOrientation(description.LocalBasisA, $"{nameof(description)}.{nameof(description.LocalBasisA)}");
        Quaternion basisB = Physics3DValidation.NormalizeOrientation(description.LocalBasisB, $"{nameof(description)}.{nameof(description.LocalBasisB)}");
        Physics3DValidation.RequireFinite(description.MinimumAngleRadians, $"{nameof(description)}.{nameof(description.MinimumAngleRadians)}");
        Physics3DValidation.RequireFinite(description.MaximumAngleRadians, $"{nameof(description)}.{nameof(description.MaximumAngleRadians)}");
        if (description.MinimumAngleRadians < -MathF.PI || description.MinimumAngleRadians > MathF.PI)
        {
            throw new ArgumentOutOfRangeException(
                $"{nameof(description)}.{nameof(description.MinimumAngleRadians)}",
                description.MinimumAngleRadians,
                "Minimum twist angle must be between -PI and PI radians inclusive.");
        }

        if (description.MaximumAngleRadians < description.MinimumAngleRadians || description.MaximumAngleRadians > MathF.PI)
        {
            throw new ArgumentOutOfRangeException(
                $"{nameof(description)}.{nameof(description.MaximumAngleRadians)}",
                description.MaximumAngleRadians,
                "Maximum twist angle must be between the minimum angle and PI radians inclusive.");
        }

        description.Spring.Validate($"{nameof(description)}.{nameof(description.Spring)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new TwistLimit
        {
            LocalBasisA = basisA,
            LocalBasisB = basisB,
            MinimumAngle = description.MinimumAngleRadians,
            MaximumAngle = description.MaximumAngleRadians,
            SpringSettings = CreateSpringSettings(description.Spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.TwistLimit, bepuDescription);
    }

    public Physics3DConstraintId CreateAngularMotorConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DAngularMotorDescription description)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(
            description.TargetVelocityLocalARadiansPerSecond,
            $"{nameof(description)}.{nameof(description.TargetVelocityLocalARadiansPerSecond)}");
        description.Motor.Validate($"{nameof(description)}.{nameof(description.Motor)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new AngularMotor
        {
            TargetVelocityLocalA = description.TargetVelocityLocalARadiansPerSecond,
            Settings = CreateMotorSettings(description.Motor)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.AngularMotor, bepuDescription);
    }

    public Physics3DConstraintId CreateAngularServoConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DAngularServoDescription description)
    {
        RequireStructuralPhase();
        Quaternion target = Physics3DValidation.NormalizeOrientation(
            description.TargetRelativeRotationLocalA,
            $"{nameof(description)}.{nameof(description.TargetRelativeRotationLocalA)}");
        description.Servo.Validate($"{nameof(description)}.{nameof(description.Servo)}");
        description.Spring.Validate($"{nameof(description)}.{nameof(description.Spring)}");
        RequireConstraintBodies(bodyA, bodyB, out int slotA, out int slotB, out BodyHandle handleA, out BodyHandle handleB);
        var bepuDescription = new AngularServo
        {
            TargetRelativeRotationLocalA = target,
            ServoSettings = CreateServoSettings(description.Servo),
            SpringSettings = CreateSpringSettings(description.Spring)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, Physics3DConstraintType.AngularServo, bepuDescription);
    }

    public void UpdateLinearAxisServoTarget(Physics3DConstraintId constraint, float targetOffsetCm)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(targetOffsetCm, nameof(targetOffsetCm));
        ConstraintHandle handle = RequireConstraintHandle(constraint, Physics3DConstraintType.LinearAxisServo);
        _simulation.Solver.GetDescription(handle, out LinearAxisServo description);
        description.TargetOffset = targetOffsetCm;
        _simulation.Solver.ApplyDescription(handle, description);
    }

    public void UpdateAngularAxisMotorTarget(Physics3DConstraintId constraint, float targetVelocityRadiansPerSecond)
    {
        RequireStructuralPhase();
        Physics3DValidation.RequireFinite(targetVelocityRadiansPerSecond, nameof(targetVelocityRadiansPerSecond));
        ConstraintHandle handle = RequireConstraintHandle(constraint, Physics3DConstraintType.AngularAxisMotor);
        _simulation.Solver.GetDescription(handle, out AngularAxisMotor description);
        description.TargetVelocity = targetVelocityRadiansPerSecond;
        _simulation.Solver.ApplyDescription(handle, description);
    }

    public void UpdateAngularHinge(
        Physics3DConstraintId constraint,
        in Physics3DAngularHingeDescription description)
    {
        RequireStructuralPhase();
        Vector3 axisA = NormalizeDirection(
            description.LocalHingeAxisA,
            $"{nameof(description)}.{nameof(description.LocalHingeAxisA)}");
        Vector3 axisB = NormalizeDirection(
            description.LocalHingeAxisB,
            $"{nameof(description)}.{nameof(description.LocalHingeAxisB)}");
        description.Spring.Validate($"{nameof(description)}.{nameof(description.Spring)}");
        ConstraintHandle handle = RequireConstraintHandle(constraint, Physics3DConstraintType.AngularHinge);
        var bepuDescription = new AngularHinge
        {
            LocalHingeAxisA = axisA,
            LocalHingeAxisB = axisB,
            SpringSettings = CreateSpringSettings(description.Spring)
        };
        _simulation.Solver.ApplyDescription(handle, bepuDescription);
    }

    public void UpdateAngularAxisMotor(
        Physics3DConstraintId constraint,
        in Physics3DAngularAxisMotorDescription description)
    {
        RequireStructuralPhase();
        Vector3 axis = NormalizeDirection(
            description.LocalAxisA,
            $"{nameof(description)}.{nameof(description.LocalAxisA)}");
        Physics3DValidation.RequireFinite(
            description.TargetVelocityRadiansPerSecond,
            $"{nameof(description)}.{nameof(description.TargetVelocityRadiansPerSecond)}");
        description.Motor.Validate($"{nameof(description)}.{nameof(description.Motor)}");
        ConstraintHandle handle = RequireConstraintHandle(constraint, Physics3DConstraintType.AngularAxisMotor);
        var bepuDescription = new AngularAxisMotor
        {
            LocalAxisA = axis,
            TargetVelocity = description.TargetVelocityRadiansPerSecond,
            Settings = CreateMotorSettings(description.Motor)
        };
        _simulation.Solver.ApplyDescription(handle, bepuDescription);
    }

    public void UpdateAngularServoTarget(Physics3DConstraintId constraint, Quaternion targetRelativeRotationLocalA)
    {
        RequireStructuralPhase();
        Quaternion target = Physics3DValidation.NormalizeOrientation(targetRelativeRotationLocalA, nameof(targetRelativeRotationLocalA));
        ConstraintHandle handle = RequireConstraintHandle(constraint, Physics3DConstraintType.AngularServo);
        _simulation.Solver.GetDescription(handle, out AngularServo description);
        description.TargetRelativeRotationLocalA = target;
        _simulation.Solver.ApplyDescription(handle, description);
    }

    public void DestroyConstraint(Physics3DConstraintId constraint)
    {
        RequireStructuralPhase();
        _constraints.Remove(constraint, _simulation);
    }

    public bool ContainsConstraint(Physics3DConstraintId constraint)
    {
        ThrowIfDisposed();
        if (!_constraints.Contains(constraint))
        {
            return false;
        }

        return _simulation.Solver.ConstraintExists(_constraints.GetBepuHandle(constraint.Slot));
    }

    public float GetConstraintImpulseMagnitude(Physics3DConstraintId constraint)
    {
        ThrowIfDisposed();
        int slot = _constraints.RequireSlot(constraint);
        ConstraintHandle handle = _constraints.GetBepuHandle(slot);
        if (!_simulation.Solver.ConstraintExists(handle))
        {
            throw new InvalidOperationException($"Bepu constraint '{handle.Value}' is missing for '{constraint}'.");
        }

        return _simulation.Solver.GetAccumulatedImpulseMagnitude(handle);
    }

    public int CopyActiveBodyIds(Span<Physics3DBodyId> destination)
    {
        ThrowIfDisposed();
        if (destination.Length < ActiveBodyCount)
        {
            throw new Physics3DCapacityExceededException("active body id destination", destination.Length);
        }

        int count = 0;
        for (int slot = 0; slot < _bodies.TotalCapacity; slot++)
        {
            if (_bodies.IsActiveSlot(slot))
            {
                destination[count++] = _bodies.GetId(slot);
            }
        }

        return count;
    }

    public void CopyAwakeBodies(Physics3DAwakeBodyBuffer destination)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destination);
        ref BodySet activeSet = ref _simulation.Bodies.ActiveSet;
        if (activeSet.Count > destination.Capacity)
        {
            throw new Physics3DCapacityExceededException("awake body snapshot", destination.Capacity);
        }

        for (int index = 0; index < activeSet.Count; index++)
        {
            BodyHandle handle = activeSet.IndexToHandle[index];
            int slot = _bodies.RequireMobileSlot(handle);
            ref MotionState motion = ref activeSet.SolverStates[index].Motion;
            destination.Set(
                index,
                _bodies.GetId(slot),
                _bodies.GetEntity(slot),
                motion.Pose.Position,
                motion.Pose.Orientation,
                motion.Velocity.Linear,
                motion.Velocity.Angular,
                _bodies.GetBodyKind(slot));
        }

        destination.SetCount(activeSet.Count, StepIndex);
    }

    public int CopyContactPairs(Span<Physics3DContactPair> destination)
    {
        ThrowIfDisposed();
        return _contacts.CopyPairsTo(destination);
    }

    public int CopyContactEvents(Span<Physics3DContactEvent> destination)
    {
        ThrowIfDisposed();
        return _contacts.CopyEventsTo(destination);
    }

    public bool HasCurrentContact(Physics3DBodyId bodyA, Physics3DBodyId bodyB)
    {
        ThrowIfDisposed();
        int slotA = _bodies.RequireSlot(bodyA);
        int slotB = _bodies.RequireSlot(bodyB);
        return slotA != slotB && _contacts.ContainsPersistentPair(bodyA, bodyB);
    }

    public int Raycast(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DRaycastHit> hits)
        => Raycast(originCm, direction, maximumDistanceCm, new Physics3DQueryFilter(queryLayer), hits);

    public int Raycast(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DRaycastHit> hits)
    {
        ThrowIfDisposed();
        return _queries.Raycast(originCm, direction, maximumDistanceCm, filter, hits);
    }

    public bool RaycastClosest(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DRaycastHit hit)
    {
        ThrowIfDisposed();
        return _queries.RaycastClosest(originCm, direction, maximumDistanceCm, filter, out hit);
    }

    public bool RaycastAny(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        ThrowIfDisposed();
        return _queries.RaycastAny(originCm, direction, maximumDistanceCm, filter);
    }

    public void RaycastClosestBatch(
        ReadOnlySpan<Physics3DRaycastQuery> requests,
        Span<Physics3DBatchedRaycastClosestResult> results)
    {
        ThrowIfDisposed();
        _queries.RaycastClosestBatch(requests, results);
    }

    public int BoxCast(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
        => BoxCast(
            centerCm,
            sizeCm,
            orientation,
            direction,
            maximumDistanceCm,
            new Physics3DQueryFilter(queryLayer),
            hits);

    public int BoxCast(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DShapeCastHit> hits)
    {
        ThrowIfDisposed();
        return _queries.BoxCast(centerCm, sizeCm, orientation, direction, maximumDistanceCm, filter, hits);
    }

    public bool BoxCastClosest(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DShapeCastHit hit)
    {
        ThrowIfDisposed();
        return _queries.BoxCastClosest(centerCm, sizeCm, orientation, direction, maximumDistanceCm, filter, out hit);
    }

    public bool BoxCastAny(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        ThrowIfDisposed();
        return _queries.BoxCastAny(centerCm, sizeCm, orientation, direction, maximumDistanceCm, filter);
    }

    public void BoxCastClosestBatch(
        ReadOnlySpan<Physics3DBoxCastQuery> requests,
        Span<Physics3DBatchedShapeCastClosestResult> results)
    {
        ThrowIfDisposed();
        _queries.BoxCastClosestBatch(requests, results);
    }

    public int SphereCast(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
        => SphereCast(centerCm, radiusCm, direction, maximumDistanceCm, new Physics3DQueryFilter(queryLayer), hits);

    public int SphereCast(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DShapeCastHit> hits)
    {
        ThrowIfDisposed();
        return _queries.SphereCast(centerCm, radiusCm, direction, maximumDistanceCm, filter, hits);
    }

    public bool SphereCastClosest(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DShapeCastHit hit)
    {
        ThrowIfDisposed();
        return _queries.SphereCastClosest(centerCm, radiusCm, direction, maximumDistanceCm, filter, out hit);
    }

    public bool SphereCastAny(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        ThrowIfDisposed();
        return _queries.SphereCastAny(centerCm, radiusCm, direction, maximumDistanceCm, filter);
    }

    public void SphereCastClosestBatch(
        ReadOnlySpan<Physics3DSphereCastQuery> requests,
        Span<Physics3DBatchedShapeCastClosestResult> results)
    {
        ThrowIfDisposed();
        _queries.SphereCastClosestBatch(requests, results);
    }

    public int CapsuleCast(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
        => CapsuleCast(
            centerCm,
            radiusCm,
            cylinderLengthCm,
            orientation,
            direction,
            maximumDistanceCm,
            new Physics3DQueryFilter(queryLayer),
            hits);

    public int CapsuleCast(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DShapeCastHit> hits)
    {
        ThrowIfDisposed();
        return _queries.CapsuleCast(centerCm, radiusCm, cylinderLengthCm, orientation, direction, maximumDistanceCm, filter, hits);
    }

    public bool CapsuleCastClosest(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DShapeCastHit hit)
    {
        ThrowIfDisposed();
        return _queries.CapsuleCastClosest(
            centerCm,
            radiusCm,
            cylinderLengthCm,
            orientation,
            direction,
            maximumDistanceCm,
            filter,
            out hit);
    }

    public bool CapsuleCastAny(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        ThrowIfDisposed();
        return _queries.CapsuleCastAny(
            centerCm,
            radiusCm,
            cylinderLengthCm,
            orientation,
            direction,
            maximumDistanceCm,
            filter);
    }

    public void CapsuleCastClosestBatch(
        ReadOnlySpan<Physics3DCapsuleCastQuery> requests,
        Span<Physics3DBatchedShapeCastClosestResult> results)
    {
        ThrowIfDisposed();
        _queries.CapsuleCastClosestBatch(requests, results);
    }

    public int OverlapBox(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
        => OverlapBox(centerCm, sizeCm, orientation, new Physics3DQueryFilter(queryLayer), hits);

    public int OverlapBox(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        in Physics3DQueryFilter filter,
        Span<Physics3DOverlapHit> hits)
    {
        ThrowIfDisposed();
        return _queries.OverlapBox(centerCm, sizeCm, orientation, filter, hits);
    }

    public int OverlapSphere(
        Vector3 centerCm,
        float radiusCm,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
        => OverlapSphere(centerCm, radiusCm, new Physics3DQueryFilter(queryLayer), hits);

    public int OverlapSphere(
        Vector3 centerCm,
        float radiusCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DOverlapHit> hits)
    {
        ThrowIfDisposed();
        return _queries.OverlapSphere(centerCm, radiusCm, filter, hits);
    }

    public int OverlapCapsule(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
        => OverlapCapsule(
            centerCm,
            radiusCm,
            cylinderLengthCm,
            orientation,
            new Physics3DQueryFilter(queryLayer),
            hits);

    public int OverlapCapsule(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        in Physics3DQueryFilter filter,
        Span<Physics3DOverlapHit> hits)
    {
        ThrowIfDisposed();
        return _queries.OverlapCapsule(centerCm, radiusCm, cylinderLengthCm, orientation, filter, hits);
    }

    public void Step()
    {
        ThrowIfDisposed();
        ThrowIfTerminalFaulted();
        if (_isStepping)
        {
            throw new InvalidOperationException("Physics3DWorld.Step is not reentrant.");
        }

        _isStepping = true;
        try
        {
            Physics3DThreadDispatcher? metricsDispatcher = _threadDispatcher as Physics3DThreadDispatcher;
            metricsDispatcher?.BeginStepMetrics();
            long totalTimestamp = Stopwatch.GetTimestamp();
            long totalAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long totalBackgroundAllocationBefore = metricsDispatcher?.BackgroundWorkerAllocatedBytesCurrentStep ?? 0;
            long totalBackgroundDispatchElapsedBefore = metricsDispatcher?.BackgroundWorkerDispatchElapsedTimestampTicksCurrentStep ?? 0;

            long stageTimestamp = Stopwatch.GetTimestamp();
            long stageAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long stageBackgroundAllocationBefore = metricsDispatcher?.BackgroundWorkerAllocatedBytesCurrentStep ?? 0;
            long stageBackgroundDispatchElapsedBefore = metricsDispatcher?.BackgroundWorkerDispatchElapsedTimestampTicksCurrentStep ?? 0;
            _actuationCommands.Replay(_bodies, _simulation, _config.FixedDeltaSeconds);
            _contacts.BeginStep();
            Physics3DStageMetrics commandReplay = new(
                Stopwatch.GetElapsedTime(stageTimestamp).TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - stageAllocationBefore,
                (metricsDispatcher?.BackgroundWorkerAllocatedBytesCurrentStep ?? 0) - stageBackgroundAllocationBefore,
                (metricsDispatcher?.BackgroundWorkerDispatchElapsedTimestampTicksCurrentStep ?? 0) - stageBackgroundDispatchElapsedBefore);

            // Timestep mutates the Bepu simulation. After it returns, StepIndex advances and any
            // contact-finalization failure is terminal: callers must not catch-and-retry Step.
            _simulation.Timestep(_config.FixedDeltaSeconds, _threadDispatcher);
            StepIndex++;

            try
            {
                stageTimestamp = Stopwatch.GetTimestamp();
                stageAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
                stageBackgroundAllocationBefore = metricsDispatcher?.BackgroundWorkerAllocatedBytesCurrentStep ?? 0;
                stageBackgroundDispatchElapsedBefore = metricsDispatcher?.BackgroundWorkerDispatchElapsedTimestampTicksCurrentStep ?? 0;
                _contacts.CompleteStep(_bodies, _simulation, StepIndex);
                Physics3DStageMetrics contactFinalize = new(
                    Stopwatch.GetElapsedTime(stageTimestamp).TotalMilliseconds,
                    GC.GetAllocatedBytesForCurrentThread() - stageAllocationBefore,
                    (metricsDispatcher?.BackgroundWorkerAllocatedBytesCurrentStep ?? 0) - stageBackgroundAllocationBefore,
                    (metricsDispatcher?.BackgroundWorkerDispatchElapsedTimestampTicksCurrentStep ?? 0) - stageBackgroundDispatchElapsedBefore);

                Physics3DStageMetrics total = new(
                    Stopwatch.GetElapsedTime(totalTimestamp).TotalMilliseconds,
                    GC.GetAllocatedBytesForCurrentThread() - totalAllocationBefore,
                    (metricsDispatcher?.BackgroundWorkerAllocatedBytesCurrentStep ?? 0) - totalBackgroundAllocationBefore,
                    (metricsDispatcher?.BackgroundWorkerDispatchElapsedTimestampTicksCurrentStep ?? 0) - totalBackgroundDispatchElapsedBefore);
                Physics3DKernelStepMetrics kernel = _productionTimestepper?.LastStepMetrics ?? default;
                LastStepMetrics = new Physics3DStepMetrics(
                    StepIndex,
                    _productionTimestepper is not null,
                    total,
                    commandReplay,
                    kernel.Sleep,
                    kernel.PredictBounds,
                    kernel.CollisionDetection,
                    kernel.ContactSurface,
                    kernel.Solve,
                    kernel.Optimize,
                    contactFinalize);
            }
            catch (Exception ex)
            {
                EnterTerminalFault(ex);
                throw;
            }
        }
        finally
        {
            _isStepping = false;
        }
    }

    public ulong ComputeObservableBodyStateHash()
    {
        ThrowIfDisposed();
        ulong hash = 14695981039346656037UL;
        Hash(ref hash, StepIndex);
        for (int slot = 0; slot < _bodies.TotalCapacity; slot++)
        {
            if (!_bodies.IsActiveSlot(slot))
            {
                continue;
            }

            Physics3DBodyId id = _bodies.GetId(slot);
            Physics3DBodyState state = GetBodyState(id);
            Hash(ref hash, slot);
            Hash(ref hash, id.Generation);
            Hash(ref hash, (int)_bodies.GetBodyKind(slot));
            Hash(ref hash, state.PositionCm.X);
            Hash(ref hash, state.PositionCm.Y);
            Hash(ref hash, state.PositionCm.Z);
            Hash(ref hash, state.Orientation.X);
            Hash(ref hash, state.Orientation.Y);
            Hash(ref hash, state.Orientation.Z);
            Hash(ref hash, state.Orientation.W);
            Hash(ref hash, state.LinearVelocityCmPerSecond.X);
            Hash(ref hash, state.LinearVelocityCmPerSecond.Y);
            Hash(ref hash, state.LinearVelocityCmPerSecond.Z);
            Hash(ref hash, state.AngularVelocityRadiansPerSecond.X);
            Hash(ref hash, state.AngularVelocityRadiansPerSecond.Y);
            Hash(ref hash, state.AngularVelocityRadiansPerSecond.Z);
            Hash(ref hash, state.Awake ? 1 : 0);
        }

        return hash;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_isStepping)
        {
            throw new InvalidOperationException("Cannot dispose Physics3DWorld during a simulation step.");
        }

        try
        {
            _simulation.Dispose();
        }
        finally
        {
            try
            {
                _threadDispatcherLifetime.Dispose();
            }
            finally
            {
                _bufferPool.Clear();
                _disposed = true;
            }
        }
    }

    private static Physics3DThreadDispatcher CreateDefaultThreadDispatcher(Physics3DWorldConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        return new Physics3DThreadDispatcher(
            config.WorkerCount,
            config.ThreadMemoryPoolBlockAllocationSize,
            config.MemoryPoolExpectedPooledResourceCount);
    }

    private void ValidateBodyDescription(in Physics3DBodyDescription description)
    {
        if (!Enum.IsDefined(description.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(description.Kind));
        }

        if (!Enum.IsDefined(description.ContinuousDetection))
        {
            throw new ArgumentOutOfRangeException(nameof(description.ContinuousDetection));
        }

        _shapes.RequireTypedIndex(description.Shape);
        Physics3DValidation.RequireFinite(description.PositionCm, nameof(description.PositionCm));
        Physics3DValidation.NormalizeOrientation(description.Orientation, nameof(description.Orientation));
        Physics3DValidation.RequireFinite(description.LinearVelocityCmPerSecond, nameof(description.LinearVelocityCmPerSecond));
        Physics3DValidation.RequireFinite(description.AngularVelocityRadiansPerSecond, nameof(description.AngularVelocityRadiansPerSecond));
        description.Material.Validate(nameof(description.Material));
        description.ContactPolicy.Validate(nameof(description.ContactPolicy));
        if (description.ContactPolicy.Kind == Physics3DBodyContactPolicyKind.SurfaceVelocity)
        {
            if (description.Kind != Physics3DBodyKind.Kinematic)
            {
                throw new ArgumentException(
                    "Physics3D surface velocity requires a kinematic body so the contact solver can consume the velocity without changing dynamic authority.",
                    nameof(description));
            }

            if (!_supportsContactSurfaceVelocity)
            {
                throw new InvalidOperationException(
                    "Physics3D surface velocity is unavailable with an injected test timestepper.");
            }
        }

        description.CollisionSubgroup.Validate(nameof(description.CollisionSubgroup));
        if (description.Kind == Physics3DBodyKind.Dynamic)
        {
            Physics3DValidation.RequireFinitePositive(description.Mass, nameof(description.Mass));
        }
        else if (description.Mass != 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(description.Mass), description.Mass, "Kinematic and static Physics3D bodies must use zero mass.");
        }
    }

    private ContinuousDetection CreateContinuity(Physics3DContinuousDetectionMode mode)
    {
        return mode switch
        {
            Physics3DContinuousDetectionMode.Discrete => ContinuousDetection.Discrete,
            Physics3DContinuousDetectionMode.Passive => ContinuousDetection.Passive,
            Physics3DContinuousDetectionMode.Continuous => ContinuousDetection.Continuous(
                _config.ContinuousMinimumSweepTimestep,
                _config.ContinuousSweepConvergenceThreshold),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown continuous detection mode.")
        };
    }

    private Physics3DConstraintId AddConstraint<TDescription>(
        int slotA,
        int slotB,
        BodyHandle handleA,
        BodyHandle handleB,
        Physics3DConstraintType type,
        TDescription description)
        where TDescription : unmanaged, ITwoBodyConstraintDescription<TDescription>
    {
        int slot = _constraints.AllocateSlot();
        bool solverConstraintCreated = false;
        ConstraintHandle constraintHandle = default;
        try
        {
            constraintHandle = _simulation.Solver.Add(handleA, handleB, description);
            solverConstraintCreated = true;
            _constraints.Bind(slot, constraintHandle, slotA, slotB, type);
            return _constraints.GetId(slot);
        }
        catch
        {
            if (solverConstraintCreated && _simulation.Solver.ConstraintExists(constraintHandle))
            {
                _simulation.Solver.Remove(constraintHandle);
            }

            _constraints.Rollback(slot);
            throw;
        }
    }

    private ConstraintHandle RequireConstraintHandle(
        Physics3DConstraintId constraint,
        Physics3DConstraintType expectedType)
    {
        int slot = _constraints.RequireType(constraint, expectedType);
        ConstraintHandle handle = _constraints.GetBepuHandle(slot);
        if (!_simulation.Solver.ConstraintExists(handle))
        {
            throw new InvalidOperationException($"Bepu constraint '{handle.Value}' is missing for '{constraint}'.");
        }

        return handle;
    }

    private static SpringSettings CreateSpringSettings(in Physics3DSpringSettings settings)
    {
        return new SpringSettings
        {
            AngularFrequency = settings.AngularFrequency,
            TwiceDampingRatio = settings.TwiceDampingRatio
        };
    }

    private static ServoSettings CreateServoSettings(in Physics3DServoSettings settings)
        => new(settings.MaximumSpeed, settings.BaseSpeed, settings.MaximumForce);

    private static MotorSettings CreateMotorSettings(in Physics3DMotorSettings settings)
        => new(settings.MaximumForce, settings.Softness);

    private void RequireConstraintBodies(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        out int slotA,
        out int slotB,
        out BodyHandle handleA,
        out BodyHandle handleB)
    {
        slotA = _bodies.RequireSlot(bodyA);
        slotB = _bodies.RequireSlot(bodyB);
        if (slotA == slotB)
        {
            throw new InvalidOperationException("A Physics3D constraint requires two distinct bodies.");
        }

        if (_bodies.GetBodyKind(slotA) == Physics3DBodyKind.Static || _bodies.GetBodyKind(slotB) == Physics3DBodyKind.Static)
        {
            throw new InvalidOperationException("Bepu two-body constraints require mobile bodies; use a kinematic anchor instead of a static body.");
        }

        handleA = new BodyHandle(_bodies.GetBepuHandle(slotA));
        handleB = new BodyHandle(_bodies.GetBepuHandle(slotB));
    }

    private static Vector3 NormalizeDirection(Vector3 value, string parameterName)
    {
        Physics3DValidation.RequireFinite(value, parameterName);
        float lengthSquared = value.LengthSquared();
        if (!(lengthSquared > 1e-12f))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Direction length must be greater than zero.");
        }

        return Vector3.Normalize(value);
    }

    internal static void SetAwake(BodyReference bodyReference, bool awake)
    {
        bodyReference.Awake = awake;
        if (!awake)
        {
            return;
        }

        bodyReference.Activity.TimestepsUnderThresholdCount = 0;
        bodyReference.Activity.SleepCandidate = false;
    }

    private void EnqueueActuation(
        Physics3DBodyId body,
        Physics3DActuationKind kind,
        Vector3 value)
    {
        RequireStructuralPhase();
        RequireDynamicBody(body);
        Physics3DValidation.RequireFinite(value, nameof(value));
        _actuationCommands.Enqueue(body, kind, value);
    }

    private void RequireDynamicBody(Physics3DBodyId body)
    {
        int slot = _bodies.RequireSlot(body);
        Physics3DBodyKind kind = _bodies.GetBodyKind(slot);
        if (kind != Physics3DBodyKind.Dynamic)
        {
            throw new InvalidOperationException(
                $"Physics3D actuation commands require a dynamic body; '{body}' is '{kind}'.");
        }
    }

    private void RequireStructuralPhase()
    {
        ThrowIfDisposed();
        ThrowIfTerminalFaulted();
        if (_isStepping)
        {
            throw new InvalidOperationException("Physics3D structural changes are forbidden during a simulation step.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfTerminalFaulted()
    {
        if (_terminalFault is null)
        {
            return;
        }

        throw new Physics3DTerminalFaultException(_terminalFault, StepIndex);
    }

    private void EnterTerminalFault(Exception fault)
    {
        // Keep the first finalization failure as SSOT diagnostics. Later Step/mutation attempts
        // throw Physics3DTerminalFaultException instead of replaying or clearing this cause.
        _terminalFault ??= fault;
    }

    private static void Hash(ref ulong hash, int value) => Hash(ref hash, unchecked((uint)value));
    private static void Hash(ref ulong hash, long value) => Hash(ref hash, unchecked((ulong)value));
    private static void Hash(ref ulong hash, float value) => Hash(ref hash, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void Hash(ref ulong hash, uint value)
    {
        hash ^= value;
        hash *= 1099511628211UL;
    }

    private static void Hash(ref ulong hash, ulong value)
    {
        Hash(ref hash, unchecked((uint)value));
        Hash(ref hash, unchecked((uint)(value >> 32)));
    }

}
