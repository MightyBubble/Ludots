using System;
using System.Numerics;
using Ludots.Core.Layers;

namespace Ludots.Core.Physics3D;

public interface IPhysics3DWorld : IDisposable
{
    int ActiveBodyCount { get; }
    int ActiveMobileBodyCount { get; }
    int ActiveStaticBodyCount { get; }
    int AwakeBodyCount { get; }
    int RegisteredShapeCount { get; }
    int ContactPairCount { get; }
    int ContactEventCount { get; }
    int ActiveConstraintCount { get; }
    int WorkerCount { get; }
    long StepIndex { get; }
    float FixedDeltaSeconds { get; }

    Physics3DShapeId RegisterBoxShape(Vector3 sizeCm);
    Physics3DShapeId RegisterSphereShape(float radiusCm);
    Physics3DShapeId RegisterCapsuleShape(float radiusCm, float cylinderLengthCm);
    Physics3DBodyId CreateBody(in Physics3DBodyDescription description);
    void DestroyBody(Physics3DBodyId body);
    bool ContainsBody(Physics3DBodyId body);
    Physics3DBodyKind GetBodyKind(Physics3DBodyId body);
    Physics3DBodyState GetBodyState(Physics3DBodyId body);
    void SetBodyState(Physics3DBodyId body, in Physics3DBodyState state);
    void SetBodyAwake(Physics3DBodyId body, bool awake);
    void SetBodyVelocity(Physics3DBodyId body, Vector3 linearVelocityCmPerSecond, Vector3 angularVelocityRadiansPerSecond);
    Physics3DConstraintId CreateBallSocketConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        Vector3 localOffsetA,
        Vector3 localOffsetB,
        in Physics3DSpringSettings spring);
    Physics3DConstraintId CreateHingeConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        Vector3 localOffsetA,
        Vector3 localHingeAxisA,
        Vector3 localOffsetB,
        Vector3 localHingeAxisB,
        in Physics3DSpringSettings spring);
    Physics3DConstraintId CreateWeldConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        Vector3 localOffsetFromAToB,
        Quaternion localOrientationOfBInA,
        in Physics3DSpringSettings spring);
    void DestroyConstraint(Physics3DConstraintId constraint);
    bool ContainsConstraint(Physics3DConstraintId constraint);
    float GetConstraintImpulseMagnitude(Physics3DConstraintId constraint);
    int CopyActiveBodyIds(Span<Physics3DBodyId> destination);
    void CopyAwakeBodies(Physics3DAwakeBodyBuffer destination);
    int CopyContactPairs(Span<Physics3DContactPair> destination);
    int CopyContactEvents(Span<Physics3DContactEvent> destination);

    /// <summary>
    /// Collects all ray hits into <paramref name="hits"/> ordered by ascending distance, then ascending body slot.
    /// Capacity overflow throws <see cref="Physics3DCapacityExceededException"/> (never silent truncation).
    /// </summary>
    int Raycast(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DRaycastHit> hits);

    /// <inheritdoc cref="Raycast(Vector3, Vector3, float, in LayerMask, Span{Physics3DRaycastHit})"/>
    int Raycast(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, Span<Physics3DRaycastHit> hits);

    bool RaycastClosest(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, out Physics3DRaycastHit hit);
    bool RaycastAny(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter);

    /// <summary>
    /// Closest-hit ray batch. Request and result spans must have equal length; results are aligned 1:1 by index.
    /// Uses Bepu <c>SimulationRayBatcher</c> without introducing parallel dispatch infrastructure.
    /// Shape-cast/overlap batch APIs are intentionally omitted: Bepu 2.4 has no Simulation-level sweep/overlap batcher.
    /// </summary>
    void RaycastClosestBatch(
        ReadOnlySpan<Physics3DRaycastQuery> requests,
        in Physics3DQueryFilter filter,
        Span<Physics3DBatchedRaycastClosestResult> results);

    int BoxCast(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int BoxCast(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, Span<Physics3DShapeCastHit> hits);
    bool BoxCastClosest(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, out Physics3DShapeCastHit hit);
    bool BoxCastAny(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter);

    int SphereCast(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int SphereCast(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, Span<Physics3DShapeCastHit> hits);
    bool SphereCastClosest(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, out Physics3DShapeCastHit hit);
    bool SphereCastAny(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter);

    int CapsuleCast(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int CapsuleCast(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, Span<Physics3DShapeCastHit> hits);
    bool CapsuleCastClosest(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, out Physics3DShapeCastHit hit);
    bool CapsuleCastAny(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter);

    /// <summary>
    /// Collects overlapping bodies with unordered semantics (append-only; no O(n^2) insertion).
    /// Capacity overflow throws <see cref="Physics3DCapacityExceededException"/> (never silent truncation / empty-as-miss).
    /// </summary>
    int OverlapBox(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);

    /// <inheritdoc cref="OverlapBox(Vector3, Vector3, Quaternion, in LayerMask, Span{Physics3DOverlapHit})"/>
    int OverlapBox(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, in Physics3DQueryFilter filter, Span<Physics3DOverlapHit> hits);

    /// <inheritdoc cref="OverlapBox(Vector3, Vector3, Quaternion, in LayerMask, Span{Physics3DOverlapHit})"/>
    int OverlapSphere(Vector3 centerCm, float radiusCm, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);

    /// <inheritdoc cref="OverlapBox(Vector3, Vector3, Quaternion, in LayerMask, Span{Physics3DOverlapHit})"/>
    int OverlapSphere(Vector3 centerCm, float radiusCm, in Physics3DQueryFilter filter, Span<Physics3DOverlapHit> hits);

    /// <inheritdoc cref="OverlapBox(Vector3, Vector3, Quaternion, in LayerMask, Span{Physics3DOverlapHit})"/>
    int OverlapCapsule(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);

    /// <inheritdoc cref="OverlapBox(Vector3, Vector3, Quaternion, in LayerMask, Span{Physics3DOverlapHit})"/>
    int OverlapCapsule(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, in Physics3DQueryFilter filter, Span<Physics3DOverlapHit> hits);

    void Step();
    ulong ComputeStateHash();
}
