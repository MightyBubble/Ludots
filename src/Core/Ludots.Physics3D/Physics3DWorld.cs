using System;
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
    private bool _isStepping;
    private bool _disposed;

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

            bufferPool = new BufferPool();
            _bodies = new Physics3DBodyStore(config.MobileBodyCapacity, config.StaticBodyCapacity);
            _contacts = new Physics3DContactCollector(config.WorkerCount, config.ContactPairCapacityPerWorker);
            _constraints = new Physics3DConstraintStore(
                config.ConstraintCapacity,
                checked(config.MobileBodyCapacity + config.StaticBodyCapacity));
            var allocationSizes = new SimulationAllocationSizes(
                config.MobileBodyCapacity,
                config.StaticBodyCapacity,
                config.InactiveIslandCapacity,
                config.ShapeCapacity,
                config.ConstraintCapacity,
                config.ConstraintsPerTypeBatchCapacity,
                config.ConstraintCountPerBodyEstimate);
            simulation = Simulation.Create(
                bufferPool,
                new Physics3DNarrowPhaseCallbacks(_bodies, _contacts, config.MaterialCombineMode),
                new Physics3DPoseIntegratorCallbacks(config.GravityCmPerSecondSquared, config.LinearDamping, config.AngularDamping),
                new SolveDescription(config.SolverVelocityIterationCount, config.SolverSubstepCount),
                timestepper: timestepper,
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
    public int WorkerCount => _threadDispatcher.ThreadCount;
    public long StepIndex { get; private set; }
    public float FixedDeltaSeconds => _config.FixedDeltaSeconds;

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
            SpringSettings = new SpringSettings(spring.AngularFrequency, spring.TwiceDampingRatio)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, description);
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
            SpringSettings = new SpringSettings(spring.AngularFrequency, spring.TwiceDampingRatio)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, description);
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
            SpringSettings = new SpringSettings(spring.AngularFrequency, spring.TwiceDampingRatio)
        };
        return AddConstraint(slotA, slotB, handleA, handleB, description);
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

    public int Raycast(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DRaycastHit> hits)
    {
        ThrowIfDisposed();
        return _queries.Raycast(originCm, direction, maximumDistanceCm, queryLayer, hits);
    }

    public int BoxCast(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
    {
        ThrowIfDisposed();
        return _queries.BoxCast(centerCm, sizeCm, orientation, direction, maximumDistanceCm, queryLayer, hits);
    }

    public int SphereCast(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
    {
        ThrowIfDisposed();
        return _queries.SphereCast(centerCm, radiusCm, direction, maximumDistanceCm, queryLayer, hits);
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
    {
        ThrowIfDisposed();
        return _queries.CapsuleCast(centerCm, radiusCm, cylinderLengthCm, orientation, direction, maximumDistanceCm, queryLayer, hits);
    }

    public int OverlapBox(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
    {
        ThrowIfDisposed();
        return _queries.OverlapBox(centerCm, sizeCm, orientation, queryLayer, hits);
    }

    public int OverlapSphere(
        Vector3 centerCm,
        float radiusCm,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
    {
        ThrowIfDisposed();
        return _queries.OverlapSphere(centerCm, radiusCm, queryLayer, hits);
    }

    public int OverlapCapsule(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
    {
        ThrowIfDisposed();
        return _queries.OverlapCapsule(centerCm, radiusCm, cylinderLengthCm, orientation, queryLayer, hits);
    }

    public void Step()
    {
        ThrowIfDisposed();
        if (_isStepping)
        {
            throw new InvalidOperationException("Physics3DWorld.Step is not reentrant.");
        }

        _isStepping = true;
        try
        {
            _contacts.BeginStep();
            _simulation.Timestep(_config.FixedDeltaSeconds, _threadDispatcher);
            StepIndex++;
            _contacts.CompleteStep(_bodies, _simulation, StepIndex);
        }
        finally
        {
            _isStepping = false;
        }
    }

    public ulong ComputeStateHash()
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

    private static ThreadDispatcher CreateDefaultThreadDispatcher(Physics3DWorldConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        return new ThreadDispatcher(config.WorkerCount);
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
            _constraints.Bind(slot, constraintHandle, slotA, slotB);
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

    private static void SetAwake(BodyReference bodyReference, bool awake)
    {
        bodyReference.Awake = awake;
        if (!awake)
        {
            return;
        }

        bodyReference.Activity.TimestepsUnderThresholdCount = 0;
        bodyReference.Activity.SleepCandidate = false;
    }

    private void RequireStructuralPhase()
    {
        ThrowIfDisposed();
        if (_isStepping)
        {
            throw new InvalidOperationException("Physics3D structural changes are forbidden during a simulation step.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
