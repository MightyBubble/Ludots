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

    public Physics3DNarrowPhaseCallbacks(
        Physics3DBodyStore bodies,
        Physics3DContactCollector contacts,
        Physics3DMaterialCombineMode combineMode)
    {
        _bodies = bodies;
        _contacts = contacts;
        _combineMode = combineMode;
    }

    public void Initialize(Simulation simulation)
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(
        int workerIndex,
        CollidableReference a,
        CollidableReference b,
        ref float speculativeMargin)
    {
        if (a.Mobility != CollidableMobility.Dynamic && b.Mobility != CollidableMobility.Dynamic)
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
            new SpringSettings(
                Combine(materialA.SpringAngularFrequency, materialB.SpringAngularFrequency),
                Combine(materialA.SpringTwiceDampingRatio, materialB.SpringTwiceDampingRatio)));
        if (manifold.Count > 0)
        {
            _contacts.Record(workerIndex, slotA, slotB);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(
        int workerIndex,
        CollidablePair pair,
        int childIndexA,
        int childIndexB,
        ref ConvexContactManifold manifold)
    {
        if (manifold.Count > 0)
        {
            _contacts.Record(workerIndex, _bodies.RequireSlot(pair.A), _bodies.RequireSlot(pair.B));
        }

        return true;
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
