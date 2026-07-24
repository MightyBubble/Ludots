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
    int Raycast(Vector3 originCm, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DRaycastHit> hits);
    int BoxCast(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int SphereCast(Vector3 centerCm, float radiusCm, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int CapsuleCast(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, Vector3 direction, float maximumDistanceCm, in LayerMask queryLayer, Span<Physics3DShapeCastHit> hits);
    int OverlapBox(Vector3 centerCm, Vector3 sizeCm, Quaternion orientation, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);
    int OverlapSphere(Vector3 centerCm, float radiusCm, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);
    int OverlapCapsule(Vector3 centerCm, float radiusCm, float cylinderLengthCm, Quaternion orientation, in LayerMask queryLayer, Span<Physics3DOverlapHit> hits);
    void Step();
    ulong ComputeStateHash();
}
