using System;
using System.Numerics;
using Ludots.Core.Layers;

namespace Ludots.Core.Physics3D;

public interface IPhysics3DWorld : IDisposable
{
    int MobileBodyCapacity { get; }
    int ActiveBodyCount { get; }
    int ActiveMobileBodyCount { get; }
    int ActiveStaticBodyCount { get; }
    int AwakeBodyCount { get; }
    int RegisteredShapeCount { get; }
    int ContactPairCount { get; }
    int ContactEventCount { get; }
    int ActiveConstraintCount { get; }
    int ActuationCommandCapacity { get; }
    int PendingActuationCommandCount { get; }
    int WorkerCount { get; }
    int BodySlotCapacity { get; }
    long StepIndex { get; }
    Physics3DStepMetrics LastStepMetrics { get; }
    float FixedDeltaSeconds { get; }

    /// <summary>
    /// True after a Step advanced the Bepu simulation but failed contact finalization.
    /// The world rejects further Step and structural mutation; Dispose remains valid.
    /// No rollback or retry is supported.
    /// </summary>
    bool IsTerminalFaulted { get; }

    /// <summary>
    /// The original finalization failure that entered the terminal fault, or null when healthy.
    /// </summary>
    Exception? TerminalFault { get; }

    Physics3DShapeId RegisterBoxShape(Vector3 sizeCm);
    Physics3DShapeId RegisterSphereShape(float radiusCm);
    Physics3DShapeId RegisterCapsuleShape(float radiusCm, float cylinderLengthCm);
    Physics3DShapeId RegisterCylinderShape(float radiusCm, float lengthCm);
    Physics3DBodyId CreateBody(in Physics3DBodyDescription description);
    void DestroyBody(Physics3DBodyId body);
    bool ContainsBody(Physics3DBodyId body);
    Physics3DBodyKind GetBodyKind(Physics3DBodyId body);
    Physics3DBodyContactPolicy GetBodyContactPolicy(Physics3DBodyId body);
    Physics3DCollisionSubgroup GetBodyCollisionSubgroup(Physics3DBodyId body);
    Physics3DBodyState GetBodyState(Physics3DBodyId body);
    void SetBodyState(Physics3DBodyId body, in Physics3DBodyState state);
    void SetBodyAwake(Physics3DBodyId body, bool awake);
    void SetBodyVelocity(Physics3DBodyId body, Vector3 linearVelocityCmPerSecond, Vector3 angularVelocityRadiansPerSecond);
    void SetKinematicNextPose(Physics3DBodyId body, Vector3 nextPositionCm, Quaternion nextOrientation);
    Vector3 GetBodyVelocityAtWorldPoint(Physics3DBodyId body, Vector3 worldPointCm);
    void EnqueueForce(Physics3DBodyId body, Vector3 forceMassCmPerSecondSquared);
    void EnqueueAcceleration(Physics3DBodyId body, Vector3 accelerationCmPerSecondSquared);
    void EnqueueTorque(Physics3DBodyId body, Vector3 torqueMassCmSquaredPerSecondSquared);
    void EnqueueLinearImpulse(Physics3DBodyId body, Vector3 impulseMassCmPerSecond);
    void EnqueueAngularImpulse(Physics3DBodyId body, Vector3 impulseMassCmSquaredPerSecond);
    void EnqueueImpulseAtWorldPoint(
        Physics3DBodyId body,
        Vector3 impulseMassCmPerSecond,
        Vector3 worldPointCm);
    void ClearActuationCommands();
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
    Physics3DConstraintId CreatePointOnLineServoConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DPointOnLineServoDescription description);
    Physics3DConstraintId CreateLinearAxisServoConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DLinearAxisServoDescription description);
    Physics3DConstraintId CreateLinearAxisLimitConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DLinearAxisLimitDescription description);
    Physics3DConstraintId CreateAngularHingeConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DAngularHingeDescription description);
    Physics3DConstraintId CreateAngularAxisMotorConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DAngularAxisMotorDescription description);
    Physics3DConstraintId CreateSwingLimitConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DSwingLimitDescription description);
    Physics3DConstraintId CreateTwistLimitConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DTwistLimitDescription description);
    Physics3DConstraintId CreateAngularMotorConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DAngularMotorDescription description);
    Physics3DConstraintId CreateAngularServoConstraint(
        Physics3DBodyId bodyA,
        Physics3DBodyId bodyB,
        in Physics3DAngularServoDescription description);
    void UpdateLinearAxisServoTarget(Physics3DConstraintId constraint, float targetOffsetCm);
    void UpdateAngularHinge(
        Physics3DConstraintId constraint,
        in Physics3DAngularHingeDescription description);
    void UpdateAngularAxisMotor(
        Physics3DConstraintId constraint,
        in Physics3DAngularAxisMotorDescription description);
    void UpdateAngularAxisMotorTarget(Physics3DConstraintId constraint, float targetVelocityRadiansPerSecond);
    void UpdateAngularServoTarget(Physics3DConstraintId constraint, Quaternion targetRelativeRotationLocalA);
    void DestroyConstraint(Physics3DConstraintId constraint);
    bool ContainsConstraint(Physics3DConstraintId constraint);
    float GetConstraintImpulseMagnitude(Physics3DConstraintId constraint);
    int CopyActiveBodyIds(Span<Physics3DBodyId> destination);
    void CopyAwakeBodies(Physics3DAwakeBodyBuffer destination);
    int CopyContactPairs(Span<Physics3DContactPair> destination);
    int CopyContactEvents(Span<Physics3DContactEvent> destination);

    /// <summary>
    /// Collects all matching ray hits ordered by distance, then body slot.
    /// </summary>
    int Raycast(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DRaycastHit> hits);
    int Raycast(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, Span<Physics3DRaycastHit> hits);
    bool RaycastClosest(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, out Physics3DRaycastHit hit);
    bool RaycastAny(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter);
    void RaycastClosestBatch(
        ReadOnlySpan<Physics3DRaycastQuery> requests,
        Span<Physics3DBatchedRaycastClosestResult> results);

    /// <summary>
    /// Collects all matching box-cast hits ordered by distance, then body slot.
    /// </summary>
    int BoxCast(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int BoxCast(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, Span<Physics3DShapeCastHit> hits);
    bool BoxCastClosest(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, out Physics3DShapeCastHit hit);
    bool BoxCastAny(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter);
    void BoxCastClosestBatch(ReadOnlySpan<Physics3DBoxCastQuery> requests, Span<Physics3DBatchedShapeCastClosestResult> results);

    /// <summary>
    /// Collects all matching sphere-cast hits ordered by distance, then body slot.
    /// </summary>
    int SphereCast(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int SphereCast(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, Span<Physics3DShapeCastHit> hits);
    bool SphereCastClosest(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, out Physics3DShapeCastHit hit);
    bool SphereCastAny(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter);
    void SphereCastClosestBatch(ReadOnlySpan<Physics3DSphereCastQuery> requests, Span<Physics3DBatchedShapeCastClosestResult> results);

    /// <summary>
    /// Collects all matching capsule-cast hits ordered by distance, then body slot.
    /// </summary>
    int CapsuleCast(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int CapsuleCast(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, Span<Physics3DShapeCastHit> hits);
    bool CapsuleCastClosest(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter, out Physics3DShapeCastHit hit);
    bool CapsuleCastAny(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in Physics3DQueryFilter filter);
    void CapsuleCastClosestBatch(ReadOnlySpan<Physics3DCapsuleCastQuery> requests, Span<Physics3DBatchedShapeCastClosestResult> results);

    /// <summary>
    /// Appends every matching overlap in unspecified order. Capacity exhaustion throws instead of truncating.
    /// </summary>
    int OverlapBox(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);
    int OverlapBox(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, in Physics3DQueryFilter filter, Span<Physics3DOverlapHit> hits);
    int OverlapSphere(Vector3 centerCm, float radiusCm, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);
    int OverlapSphere(Vector3 centerCm, float radiusCm, in Physics3DQueryFilter filter, Span<Physics3DOverlapHit> hits);
    int OverlapCapsule(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);
    int OverlapCapsule(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, in Physics3DQueryFilter filter, Span<Physics3DOverlapHit> hits);
    void ExecuteParallelQueries(IPhysics3DParallelQueryBatch batch);
    int CopyLastParallelQueryWorkerAllocatedBytes(Span<long> destination);
    void Step();
    ulong ComputeObservableBodyStateHash();
}
