using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;

namespace Ludots.Core.Physics3D;

public enum Physics3DBodyKind : byte
{
    Dynamic = 1,
    Kinematic = 2,
    Static = 3
}

public enum Physics3DShapeKind : byte
{
    Box = 1,
    Sphere = 2,
    Capsule = 3
}

public enum Physics3DContinuousDetectionMode : byte
{
    Discrete = 1,
    Passive = 2,
    Continuous = 3
}

public enum Physics3DMaterialCombineMode : byte
{
    Minimum = 1,
    Maximum = 2,
    Average = 3,
    GeometricMean = 4
}

public enum Physics3DContactEventKind : byte
{
    Begin = 1,
    Stay = 2,
    End = 3
}

public readonly struct Physics3DBodyId : IEquatable<Physics3DBodyId>
{
    public Physics3DBodyId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public int Slot { get; }
    public int Generation { get; }
    public bool IsValid => Slot >= 0 && Generation > 0;

    public bool Equals(Physics3DBodyId other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is Physics3DBodyId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(Physics3DBodyId left, Physics3DBodyId right) => left.Equals(right);
    public static bool operator !=(Physics3DBodyId left, Physics3DBodyId right) => !left.Equals(right);
    public override string ToString() => $"Physics3DBodyId({Slot}:{Generation})";
}

public readonly struct Physics3DShapeId : IEquatable<Physics3DShapeId>
{
    public Physics3DShapeId(int value)
    {
        Value = value;
    }

    public int Value { get; }
    public bool IsValid => Value > 0;

    public bool Equals(Physics3DShapeId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Physics3DShapeId other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(Physics3DShapeId left, Physics3DShapeId right) => left.Value == right.Value;
    public static bool operator !=(Physics3DShapeId left, Physics3DShapeId right) => left.Value != right.Value;
    public override string ToString() => $"Physics3DShapeId({Value})";
}

public readonly struct Physics3DConstraintId : IEquatable<Physics3DConstraintId>
{
    public Physics3DConstraintId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public int Slot { get; }
    public int Generation { get; }
    public bool IsValid => Slot >= 0 && Generation > 0;

    public bool Equals(Physics3DConstraintId other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is Physics3DConstraintId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(Physics3DConstraintId left, Physics3DConstraintId right) => left.Equals(right);
    public static bool operator !=(Physics3DConstraintId left, Physics3DConstraintId right) => !left.Equals(right);
    public override string ToString() => $"Physics3DConstraintId({Slot}:{Generation})";
}

public readonly struct Physics3DSpringSettings
{
    public Physics3DSpringSettings(float angularFrequency, float twiceDampingRatio)
    {
        AngularFrequency = angularFrequency;
        TwiceDampingRatio = twiceDampingRatio;
    }

    public float AngularFrequency { get; }
    public float TwiceDampingRatio { get; }

    internal void Validate(string parameterName)
    {
        Physics3DValidation.RequireFinitePositive(AngularFrequency, $"{parameterName}.{nameof(AngularFrequency)}");
        Physics3DValidation.RequireFiniteNonNegative(TwiceDampingRatio, $"{parameterName}.{nameof(TwiceDampingRatio)}");
    }
}

public readonly struct Physics3DMaterial
{
    public Physics3DMaterial(
        float frictionCoefficient,
        float maximumRecoveryVelocityCmPerSecond,
        float springAngularFrequency,
        float springTwiceDampingRatio)
    {
        FrictionCoefficient = frictionCoefficient;
        MaximumRecoveryVelocityCmPerSecond = maximumRecoveryVelocityCmPerSecond;
        SpringAngularFrequency = springAngularFrequency;
        SpringTwiceDampingRatio = springTwiceDampingRatio;
    }

    public float FrictionCoefficient { get; }
    public float MaximumRecoveryVelocityCmPerSecond { get; }
    public float SpringAngularFrequency { get; }
    public float SpringTwiceDampingRatio { get; }

    internal void Validate(string parameterName)
    {
        Physics3DValidation.RequireFiniteNonNegative(FrictionCoefficient, $"{parameterName}.{nameof(FrictionCoefficient)}");
        Physics3DValidation.RequireFiniteNonNegative(MaximumRecoveryVelocityCmPerSecond, $"{parameterName}.{nameof(MaximumRecoveryVelocityCmPerSecond)}");
        Physics3DValidation.RequireFinitePositive(SpringAngularFrequency, $"{parameterName}.{nameof(SpringAngularFrequency)}");
        Physics3DValidation.RequireFiniteNonNegative(SpringTwiceDampingRatio, $"{parameterName}.{nameof(SpringTwiceDampingRatio)}");
    }
}

public readonly struct Physics3DBodyDescription
{
    public Physics3DBodyDescription(
        Entity entity,
        Physics3DBodyKind kind,
        Physics3DShapeId shape,
        Vector3 positionCm,
        Quaternion orientation,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond,
        float mass,
        LayerMask collisionLayer,
        Physics3DMaterial material,
        Physics3DContinuousDetectionMode continuousDetection)
    {
        Entity = entity;
        Kind = kind;
        Shape = shape;
        PositionCm = positionCm;
        Orientation = orientation;
        LinearVelocityCmPerSecond = linearVelocityCmPerSecond;
        AngularVelocityRadiansPerSecond = angularVelocityRadiansPerSecond;
        Mass = mass;
        CollisionLayer = collisionLayer;
        Material = material;
        ContinuousDetection = continuousDetection;
    }

    public Entity Entity { get; }
    public Physics3DBodyKind Kind { get; }
    public Physics3DShapeId Shape { get; }
    public Vector3 PositionCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 LinearVelocityCmPerSecond { get; }
    public Vector3 AngularVelocityRadiansPerSecond { get; }
    public float Mass { get; }
    public LayerMask CollisionLayer { get; }
    public Physics3DMaterial Material { get; }
    public Physics3DContinuousDetectionMode ContinuousDetection { get; }
}

public struct Physics3DBodyState
{
    public Vector3 PositionCm;
    public Quaternion Orientation;
    public Vector3 LinearVelocityCmPerSecond;
    public Vector3 AngularVelocityRadiansPerSecond;
    public bool Awake;
}

public readonly struct Physics3DRaycastHit
{
    public Physics3DRaycastHit(
        Physics3DBodyId body,
        Entity entity,
        Vector3 positionCm,
        Vector3 normal,
        float distanceCm)
    {
        Body = body;
        Entity = entity;
        PositionCm = positionCm;
        Normal = normal;
        DistanceCm = distanceCm;
    }

    public Physics3DBodyId Body { get; }
    public Entity Entity { get; }
    public Vector3 PositionCm { get; }
    public Vector3 Normal { get; }
    public float DistanceCm { get; }
}

public readonly struct Physics3DShapeCastHit
{
    public Physics3DShapeCastHit(
        Physics3DBodyId body,
        Entity entity,
        Vector3 positionCm,
        Vector3 normal,
        float distanceCm,
        bool startedOverlapping)
    {
        Body = body;
        Entity = entity;
        PositionCm = positionCm;
        Normal = normal;
        DistanceCm = distanceCm;
        StartedOverlapping = startedOverlapping;
    }

    public Physics3DBodyId Body { get; }
    public Entity Entity { get; }
    public Vector3 PositionCm { get; }
    public Vector3 Normal { get; }
    public float DistanceCm { get; }
    public bool StartedOverlapping { get; }
}

public readonly struct Physics3DOverlapHit
{
    public Physics3DOverlapHit(Physics3DBodyId body, Entity entity)
    {
        Body = body;
        Entity = entity;
    }

    public Physics3DBodyId Body { get; }
    public Entity Entity { get; }
}

public readonly struct Physics3DContactEvent
{
    public Physics3DContactEvent(
        Physics3DBodyId bodyA,
        Entity entityA,
        Physics3DBodyId bodyB,
        Entity entityB,
        Physics3DContactEventKind kind,
        long stepIndex)
    {
        BodyA = bodyA;
        EntityA = entityA;
        BodyB = bodyB;
        EntityB = entityB;
        Kind = kind;
        StepIndex = stepIndex;
    }

    public Physics3DBodyId BodyA { get; }
    public Entity EntityA { get; }
    public Physics3DBodyId BodyB { get; }
    public Entity EntityB { get; }
    public Physics3DContactEventKind Kind { get; }
    public long StepIndex { get; }
}

public sealed class Physics3DCapacityExceededException : InvalidOperationException
{
    public Physics3DCapacityExceededException(string resource, int capacity)
        : base($"Physics3D capacity exceeded for '{resource}' (configured capacity: {capacity}).")
    {
        Resource = resource;
        Capacity = capacity;
    }

    public string Resource { get; }
    public int Capacity { get; }
}

internal static class Physics3DValidation
{
    public static void RequireFinitePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and greater than zero.");
        }
    }

    public static void RequireFiniteNonNegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and non-negative.");
        }
    }

    public static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Vector components must be finite.");
        }
    }

    public static Quaternion NormalizeOrientation(Quaternion value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Quaternion components must be finite.");
        }

        float lengthSquared = value.LengthSquared();
        if (!(lengthSquared > 1e-12f))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Quaternion length must be greater than zero.");
        }

        return Quaternion.Normalize(value);
    }
}
