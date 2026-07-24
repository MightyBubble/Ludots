using System;
using Arch.Core;

namespace Ludots.Core.Physics3D;

public readonly struct Physics3DContactPair : IEquatable<Physics3DContactPair>
{
    public Physics3DContactPair(
        Physics3DBodyId bodyA,
        Entity entityA,
        Physics3DBodyId bodyB,
        Entity entityB,
        long stepIndex)
    {
        BodyA = bodyA;
        EntityA = entityA;
        BodyB = bodyB;
        EntityB = entityB;
        StepIndex = stepIndex;
    }

    public Physics3DBodyId BodyA { get; }
    public Entity EntityA { get; }
    public Physics3DBodyId BodyB { get; }
    public Entity EntityB { get; }
    public long StepIndex { get; }

    public bool Equals(Physics3DContactPair other) => BodyA == other.BodyA && BodyB == other.BodyB && StepIndex == other.StepIndex;
    public override bool Equals(object? obj) => obj is Physics3DContactPair other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(BodyA, BodyB, StepIndex);
}
