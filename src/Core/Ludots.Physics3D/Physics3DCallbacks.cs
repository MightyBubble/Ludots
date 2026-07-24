using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace Ludots.Core.Physics3D;

internal struct Physics3DNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    private readonly Physics3DBodyStore _bodies;
    private readonly Physics3DContactCollector _contacts;
    private readonly Physics3DMaterialCombineMode _combineMode;
    private Simulation? _simulation;

    public Physics3DNarrowPhaseCallbacks(
        Physics3DBodyStore bodies,
        Physics3DContactCollector contacts,
        Physics3DMaterialCombineMode combineMode)
    {
        _bodies = bodies;
        _contacts = contacts;
        _combineMode = combineMode;
        _simulation = null;
    }

    public void Initialize(Simulation simulation)
    {
        _simulation = simulation;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(
        int workerIndex,
        CollidableReference a,
        CollidableReference b,
        ref float speculativeMargin)
    {
        if (a.Mobility != CollidableMobility.Dynamic &&
            b.Mobility != CollidableMobility.Dynamic &&
            !_bodies.IsSensor(_bodies.RequireSlot(a)) &&
            !_bodies.IsSensor(_bodies.RequireSlot(b)))
        {
            return false;
        }

        return _bodies.AllowCollision(a, b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold<TManifold>(
        int workerIndex,
        CollidablePair pair,
        ref TManifold manifold,
        out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        int slotA = _bodies.RequireSlot(pair.A);
        int slotB = _bodies.RequireSlot(pair.B);
        ref readonly Physics3DMaterial materialA = ref _bodies.GetMaterial(slotA);
        ref readonly Physics3DMaterial materialB = ref _bodies.GetMaterial(slotB);
        pairMaterial = new PairMaterialProperties(
            Combine(materialA.FrictionCoefficient, materialB.FrictionCoefficient),
            Combine(materialA.MaximumRecoveryVelocityCmPerSecond, materialB.MaximumRecoveryVelocityCmPerSecond),
            new SpringSettings
            {
                AngularFrequency = Combine(materialA.SpringAngularFrequency, materialB.SpringAngularFrequency),
                TwiceDampingRatio = Combine(materialA.SpringTwiceDampingRatio, materialB.SpringTwiceDampingRatio)
            });
        bool createConstraint = ShouldCreateConstraint(pair, slotA, slotB, ref manifold);
        if (manifold.Count > 0 &&
            (createConstraint || _bodies.IsSensor(slotA) || _bodies.IsSensor(slotB)))
        {
            _contacts.Record(workerIndex, slotA, slotB);
        }

        return createConstraint;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(
        int workerIndex,
        CollidablePair pair,
        int childIndexA,
        int childIndexB,
        ref ConvexContactManifold manifold)
    {
        int slotA = _bodies.RequireSlot(pair.A);
        int slotB = _bodies.RequireSlot(pair.B);
        bool createConstraint = ShouldCreateConstraint(pair, slotA, slotB, ref manifold);
        if (manifold.Count > 0 &&
            (createConstraint || _bodies.IsSensor(slotA) || _bodies.IsSensor(slotB)))
        {
            _contacts.Record(workerIndex, slotA, slotB);
        }

        return createConstraint;
    }

    public void Dispose()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float Combine(float a, float b)
    {
        return _combineMode switch
        {
            Physics3DMaterialCombineMode.Minimum => MathF.Min(a, b),
            Physics3DMaterialCombineMode.Maximum => MathF.Max(a, b),
            Physics3DMaterialCombineMode.Average => (a + b) * 0.5f,
            Physics3DMaterialCombineMode.GeometricMean => MathF.Sqrt(a * b),
            _ => throw new InvalidOperationException($"Unknown Physics3D material combine mode '{_combineMode}'.")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldCreateConstraint<TManifold>(
        CollidablePair pair,
        int slotA,
        int slotB,
        ref TManifold manifold)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        ref readonly Physics3DBodyContactPolicy policyA = ref _bodies.GetContactPolicy(slotA);
        ref readonly Physics3DBodyContactPolicy policyB = ref _bodies.GetContactPolicy(slotB);
        if (policyA.Kind == Physics3DBodyContactPolicyKind.Sensor ||
            policyB.Kind == Physics3DBodyContactPolicyKind.Sensor)
        {
            return false;
        }

        if (manifold.Count == 0)
        {
            return false;
        }

        Vector3 contactNormal = manifold.GetNormal(ref manifold, 0);
        return (policyA.Kind != Physics3DBodyContactPolicyKind.OneWayPlatform ||
                AllowsOneWayContact(in policyA, slotA, slotB, pair.A, pair.B, platformIsA: true, contactNormal)) &&
               (policyB.Kind != Physics3DBodyContactPolicyKind.OneWayPlatform ||
                AllowsOneWayContact(in policyB, slotB, slotA, pair.B, pair.A, platformIsA: false, contactNormal));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AllowsOneWayContact(
        in Physics3DBodyContactPolicy policy,
        int platformSlot,
        int otherSlot,
        CollidableReference platform,
        CollidableReference other,
        bool platformIsA,
        in Vector3 contactNormal)
    {
        Simulation simulation = _simulation
            ?? throw new InvalidOperationException("Physics3D narrow phase callbacks were not initialized.");
        GetMotion(simulation, platform, out RigidPose platformPose, out BodyVelocity platformVelocity);
        GetMotion(simulation, other, out RigidPose otherPose, out BodyVelocity otherVelocity);
        Vector3 platformNormal = Vector3.Transform(policy.LocalPlatformNormal, platformPose.Orientation);
        float manifoldAlignment = Vector3.Dot(
            platformIsA ? -contactNormal : contactNormal,
            platformNormal);
        if (manifoldAlignment < policy.MinimumNormalAlignment)
        {
            return false;
        }

        float signedCenterDistance = Vector3.Dot(otherPose.Position - platformPose.Position, platformNormal);
        if (signedCenterDistance < -policy.BackfaceToleranceCm)
        {
            return false;
        }

        if (_contacts.ContainsPersistentPair(platformSlot, otherSlot))
        {
            return true;
        }

        float relativeNormalSpeed = Vector3.Dot(
            otherVelocity.Linear - platformVelocity.Linear,
            platformNormal);
        return relativeNormalSpeed <= policy.MaximumPassThroughRelativeSpeedCmPerSecond;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetMotion(
        Simulation simulation,
        CollidableReference collidable,
        out RigidPose pose,
        out BodyVelocity velocity)
    {
        if (collidable.Mobility == CollidableMobility.Static)
        {
            pose = simulation.Statics.GetStaticReference(collidable.StaticHandle).Pose;
            velocity = default;
            return;
        }

        BodyReference body = simulation.Bodies.GetBodyReference(collidable.BodyHandle);
        pose = body.Pose;
        velocity = body.Velocity;
    }
}

internal struct Physics3DPoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    private readonly Vector3 _gravity;
    private readonly float _linearDamping;
    private readonly float _angularDamping;
    private Vector3Wide _gravityWideDt;
    private Vector<float> _linearDampingDt;
    private Vector<float> _angularDampingDt;

    public Physics3DPoseIntegratorCallbacks(Vector3 gravity, float linearDamping, float angularDamping)
    {
        _gravity = gravity;
        _linearDamping = linearDamping;
        _angularDamping = angularDamping;
        _gravityWideDt = default;
        _linearDampingDt = default;
        _angularDampingDt = default;
    }

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation)
    {
    }

    public void PrepareForIntegration(float dt)
    {
        _linearDampingDt = new Vector<float>(MathF.Pow(Math.Clamp(1f - _linearDamping, 0f, 1f), dt));
        _angularDampingDt = new Vector<float>(MathF.Pow(Math.Clamp(1f - _angularDamping, 0f, 1f), dt));
        _gravityWideDt = Vector3Wide.Broadcast(_gravity * dt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IntegrateVelocity(
        Vector<int> bodyIndices,
        Vector3Wide position,
        QuaternionWide orientation,
        BodyInertiaWide localInertia,
        Vector<int> integrationMask,
        int workerIndex,
        Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        velocity.Linear = (velocity.Linear + _gravityWideDt) * _linearDampingDt;
        velocity.Angular *= _angularDampingDt;
    }
}
