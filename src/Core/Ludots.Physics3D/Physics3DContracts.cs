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

public enum Physics3DContactKind : byte
{
    Solid = 1,
    Sensor = 2
}

public enum Physics3DBodyContactPolicyKind : byte
{
    Solid = 0,
    Sensor = 1,
    OneWayPlatform = 2,
    SurfaceVelocity = 3
}

public readonly struct Physics3DBodyContactPolicy
{
    private Physics3DBodyContactPolicy(
        Physics3DBodyContactPolicyKind kind,
        Vector3 localPlatformNormal,
        float minimumNormalAlignment,
        float backfaceToleranceCm,
        float maximumPassThroughRelativeSpeedCmPerSecond,
        Vector3 localSurfaceVelocityCmPerSecond)
    {
        Kind = kind;
        LocalPlatformNormal = localPlatformNormal;
        MinimumNormalAlignment = minimumNormalAlignment;
        BackfaceToleranceCm = backfaceToleranceCm;
        MaximumPassThroughRelativeSpeedCmPerSecond = maximumPassThroughRelativeSpeedCmPerSecond;
        LocalSurfaceVelocityCmPerSecond = localSurfaceVelocityCmPerSecond;
    }

    public Physics3DBodyContactPolicyKind Kind { get; }
    public Vector3 LocalPlatformNormal { get; }
    public float MinimumNormalAlignment { get; }
    public float BackfaceToleranceCm { get; }
    public float MaximumPassThroughRelativeSpeedCmPerSecond { get; }
    public Vector3 LocalSurfaceVelocityCmPerSecond { get; }

    public static Physics3DBodyContactPolicy Solid => default;

    public static Physics3DBodyContactPolicy Sensor()
        => new(Physics3DBodyContactPolicyKind.Sensor, default, 0f, 0f, 0f, default);

    public static Physics3DBodyContactPolicy SurfaceVelocity(Vector3 localSurfaceVelocityCmPerSecond)
    {
        Physics3DValidation.RequireFinite(localSurfaceVelocityCmPerSecond, nameof(localSurfaceVelocityCmPerSecond));
        if (!(localSurfaceVelocityCmPerSecond.LengthSquared() > 1e-12f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(localSurfaceVelocityCmPerSecond),
                localSurfaceVelocityCmPerSecond,
                "Surface velocity length must be greater than zero.");
        }

        return new Physics3DBodyContactPolicy(
            Physics3DBodyContactPolicyKind.SurfaceVelocity,
            default,
            0f,
            0f,
            0f,
            localSurfaceVelocityCmPerSecond);
    }

    public static Physics3DBodyContactPolicy OneWayPlatform(
        Vector3 localPlatformNormal,
        float minimumNormalAlignment = 0.5f,
        float backfaceToleranceCm = 0f,
        float maximumPassThroughRelativeSpeedCmPerSecond = 0f)
    {
        Physics3DValidation.RequireFinite(localPlatformNormal, nameof(localPlatformNormal));
        float lengthSquared = localPlatformNormal.LengthSquared();
        if (!(lengthSquared > 1e-12f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(localPlatformNormal),
                localPlatformNormal,
                "Platform normal length must be greater than zero.");
        }

        Physics3DValidation.RequireFinite(minimumNormalAlignment, nameof(minimumNormalAlignment));
        if (minimumNormalAlignment < 0f || minimumNormalAlignment > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumNormalAlignment),
                minimumNormalAlignment,
                "Minimum normal alignment must be in the inclusive range [0, 1].");
        }

        Physics3DValidation.RequireFiniteNonNegative(backfaceToleranceCm, nameof(backfaceToleranceCm));
        Physics3DValidation.RequireFiniteNonNegative(
            maximumPassThroughRelativeSpeedCmPerSecond,
            nameof(maximumPassThroughRelativeSpeedCmPerSecond));
        return new Physics3DBodyContactPolicy(
            Physics3DBodyContactPolicyKind.OneWayPlatform,
            Vector3.Normalize(localPlatformNormal),
            minimumNormalAlignment,
            backfaceToleranceCm,
            maximumPassThroughRelativeSpeedCmPerSecond,
            default);
    }

    internal void Validate(string parameterName)
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException($"{parameterName}.{nameof(Kind)}", Kind, "Unknown Physics3D contact policy.");
        }

        if (Kind == Physics3DBodyContactPolicyKind.OneWayPlatform)
        {
            Physics3DValidation.RequireFinite(LocalPlatformNormal, $"{parameterName}.{nameof(LocalPlatformNormal)}");
            float lengthSquared = LocalPlatformNormal.LengthSquared();
            if (!(lengthSquared > 0.999f && lengthSquared < 1.001f))
            {
                throw new ArgumentOutOfRangeException(
                    $"{parameterName}.{nameof(LocalPlatformNormal)}",
                    LocalPlatformNormal,
                    "One-way platform normal must be normalized.");
            }

            Physics3DValidation.RequireFinite(MinimumNormalAlignment, $"{parameterName}.{nameof(MinimumNormalAlignment)}");
            if (MinimumNormalAlignment < 0f || MinimumNormalAlignment > 1f)
            {
                throw new ArgumentOutOfRangeException($"{parameterName}.{nameof(MinimumNormalAlignment)}");
            }

            Physics3DValidation.RequireFiniteNonNegative(BackfaceToleranceCm, $"{parameterName}.{nameof(BackfaceToleranceCm)}");
            Physics3DValidation.RequireFiniteNonNegative(
                MaximumPassThroughRelativeSpeedCmPerSecond,
                $"{parameterName}.{nameof(MaximumPassThroughRelativeSpeedCmPerSecond)}");
        }
        else if (Kind == Physics3DBodyContactPolicyKind.SurfaceVelocity)
        {
            Physics3DValidation.RequireFinite(
                LocalSurfaceVelocityCmPerSecond,
                $"{parameterName}.{nameof(LocalSurfaceVelocityCmPerSecond)}");
            if (!(LocalSurfaceVelocityCmPerSecond.LengthSquared() > 1e-12f))
            {
                throw new ArgumentOutOfRangeException(
                    $"{parameterName}.{nameof(LocalSurfaceVelocityCmPerSecond)}",
                    LocalSurfaceVelocityCmPerSecond,
                    "Surface velocity length must be greater than zero.");
            }
        }
    }
}

public readonly struct Physics3DCollisionSubgroup
{
    public Physics3DCollisionSubgroup(
        uint assemblyId,
        int subgroupIndex,
        uint collidesWithSubgroups)
    {
        if (assemblyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(assemblyId), assemblyId, "Assembly id zero is reserved for ungrouped bodies.");
        }

        if (subgroupIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subgroupIndex), subgroupIndex, "Subgroup index cannot be negative.");
        }

        if (subgroupIndex >= 32)
        {
            throw new Physics3DCapacityExceededException("collision subgroups per assembly", 32);
        }

        AssemblyId = assemblyId;
        SubgroupBit = 1u << subgroupIndex;
        CollidesWithSubgroups = collidesWithSubgroups;
    }

    public uint AssemblyId { get; }
    public uint SubgroupBit { get; }
    public uint CollidesWithSubgroups { get; }
    public bool IsGrouped => AssemblyId != 0;

    internal void Validate(string parameterName)
    {
        if (AssemblyId == 0)
        {
            if (SubgroupBit != 0 || CollidesWithSubgroups != 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Ungrouped bodies must use the default collision subgroup value.");
            }

            return;
        }

        if (SubgroupBit == 0 || (SubgroupBit & (SubgroupBit - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException($"{parameterName}.{nameof(SubgroupBit)}", SubgroupBit, "Subgroup must contain exactly one bit.");
        }
    }

    internal static bool AllowCollision(
        in Physics3DCollisionSubgroup a,
        in Physics3DCollisionSubgroup b)
    {
        if (a.AssemblyId == 0 || b.AssemblyId == 0 || a.AssemblyId != b.AssemblyId)
        {
            return true;
        }

        return (a.CollidesWithSubgroups & b.SubgroupBit) != 0 &&
               (b.CollidesWithSubgroups & a.SubgroupBit) != 0;
    }
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

public readonly struct Physics3DServoSettings
{
    public Physics3DServoSettings(float maximumSpeed, float baseSpeed, float maximumForce)
    {
        MaximumSpeed = maximumSpeed;
        BaseSpeed = baseSpeed;
        MaximumForce = maximumForce;
    }

    public float MaximumSpeed { get; }
    public float BaseSpeed { get; }
    public float MaximumForce { get; }

    internal void Validate(string parameterName)
    {
        Physics3DValidation.RequireFiniteNonNegative(MaximumSpeed, $"{parameterName}.{nameof(MaximumSpeed)}");
        Physics3DValidation.RequireFiniteNonNegative(BaseSpeed, $"{parameterName}.{nameof(BaseSpeed)}");
        Physics3DValidation.RequireFiniteNonNegative(MaximumForce, $"{parameterName}.{nameof(MaximumForce)}");
    }
}

public readonly struct Physics3DMotorSettings
{
    public Physics3DMotorSettings(float maximumForce, float softness)
    {
        MaximumForce = maximumForce;
        Softness = softness;
    }

    public float MaximumForce { get; }
    public float Softness { get; }

    internal void Validate(string parameterName)
    {
        Physics3DValidation.RequireFiniteNonNegative(MaximumForce, $"{parameterName}.{nameof(MaximumForce)}");
        Physics3DValidation.RequireFiniteNonNegative(Softness, $"{parameterName}.{nameof(Softness)}");
    }
}

public readonly struct Physics3DPointOnLineServoDescription
{
    public Physics3DPointOnLineServoDescription(
        Vector3 localOffsetACm,
        Vector3 localOffsetBCm,
        Vector3 localDirectionA,
        in Physics3DServoSettings servo,
        in Physics3DSpringSettings spring)
    {
        LocalOffsetACm = localOffsetACm;
        LocalOffsetBCm = localOffsetBCm;
        LocalDirectionA = localDirectionA;
        Servo = servo;
        Spring = spring;
    }

    public Vector3 LocalOffsetACm { get; }
    public Vector3 LocalOffsetBCm { get; }
    public Vector3 LocalDirectionA { get; }
    public Physics3DServoSettings Servo { get; }
    public Physics3DSpringSettings Spring { get; }
}

public readonly struct Physics3DLinearAxisServoDescription
{
    public Physics3DLinearAxisServoDescription(
        Vector3 localOffsetACm,
        Vector3 localOffsetBCm,
        Vector3 localAxisA,
        float targetOffsetCm,
        in Physics3DServoSettings servo,
        in Physics3DSpringSettings spring)
    {
        LocalOffsetACm = localOffsetACm;
        LocalOffsetBCm = localOffsetBCm;
        LocalAxisA = localAxisA;
        TargetOffsetCm = targetOffsetCm;
        Servo = servo;
        Spring = spring;
    }

    public Vector3 LocalOffsetACm { get; }
    public Vector3 LocalOffsetBCm { get; }
    public Vector3 LocalAxisA { get; }
    public float TargetOffsetCm { get; }
    public Physics3DServoSettings Servo { get; }
    public Physics3DSpringSettings Spring { get; }
}

public readonly struct Physics3DLinearAxisLimitDescription
{
    public Physics3DLinearAxisLimitDescription(
        Vector3 localOffsetACm,
        Vector3 localOffsetBCm,
        Vector3 localAxisA,
        float minimumOffsetCm,
        float maximumOffsetCm,
        in Physics3DSpringSettings spring)
    {
        LocalOffsetACm = localOffsetACm;
        LocalOffsetBCm = localOffsetBCm;
        LocalAxisA = localAxisA;
        MinimumOffsetCm = minimumOffsetCm;
        MaximumOffsetCm = maximumOffsetCm;
        Spring = spring;
    }

    public Vector3 LocalOffsetACm { get; }
    public Vector3 LocalOffsetBCm { get; }
    public Vector3 LocalAxisA { get; }
    public float MinimumOffsetCm { get; }
    public float MaximumOffsetCm { get; }
    public Physics3DSpringSettings Spring { get; }
}

public readonly struct Physics3DAngularHingeDescription
{
    public Physics3DAngularHingeDescription(
        Vector3 localHingeAxisA,
        Vector3 localHingeAxisB,
        in Physics3DSpringSettings spring)
    {
        LocalHingeAxisA = localHingeAxisA;
        LocalHingeAxisB = localHingeAxisB;
        Spring = spring;
    }

    public Vector3 LocalHingeAxisA { get; }
    public Vector3 LocalHingeAxisB { get; }
    public Physics3DSpringSettings Spring { get; }
}

public readonly struct Physics3DAngularAxisMotorDescription
{
    public Physics3DAngularAxisMotorDescription(
        Vector3 localAxisA,
        float targetVelocityRadiansPerSecond,
        in Physics3DMotorSettings motor)
    {
        LocalAxisA = localAxisA;
        TargetVelocityRadiansPerSecond = targetVelocityRadiansPerSecond;
        Motor = motor;
    }

    public Vector3 LocalAxisA { get; }
    public float TargetVelocityRadiansPerSecond { get; }
    public Physics3DMotorSettings Motor { get; }
}

public readonly struct Physics3DSwingLimitDescription
{
    public Physics3DSwingLimitDescription(
        Vector3 localAxisA,
        Vector3 localAxisB,
        float maximumSwingAngleRadians,
        in Physics3DSpringSettings spring)
    {
        LocalAxisA = localAxisA;
        LocalAxisB = localAxisB;
        MaximumSwingAngleRadians = maximumSwingAngleRadians;
        Spring = spring;
    }

    public Vector3 LocalAxisA { get; }
    public Vector3 LocalAxisB { get; }
    public float MaximumSwingAngleRadians { get; }
    public Physics3DSpringSettings Spring { get; }
}

public readonly struct Physics3DTwistLimitDescription
{
    public Physics3DTwistLimitDescription(
        Quaternion localBasisA,
        Quaternion localBasisB,
        float minimumAngleRadians,
        float maximumAngleRadians,
        in Physics3DSpringSettings spring)
    {
        LocalBasisA = localBasisA;
        LocalBasisB = localBasisB;
        MinimumAngleRadians = minimumAngleRadians;
        MaximumAngleRadians = maximumAngleRadians;
        Spring = spring;
    }

    public Quaternion LocalBasisA { get; }
    public Quaternion LocalBasisB { get; }
    public float MinimumAngleRadians { get; }
    public float MaximumAngleRadians { get; }
    public Physics3DSpringSettings Spring { get; }
}

public readonly struct Physics3DAngularMotorDescription
{
    public Physics3DAngularMotorDescription(
        Vector3 targetVelocityLocalARadiansPerSecond,
        in Physics3DMotorSettings motor)
    {
        TargetVelocityLocalARadiansPerSecond = targetVelocityLocalARadiansPerSecond;
        Motor = motor;
    }

    public Vector3 TargetVelocityLocalARadiansPerSecond { get; }
    public Physics3DMotorSettings Motor { get; }
}

public readonly struct Physics3DAngularServoDescription
{
    public Physics3DAngularServoDescription(
        Quaternion targetRelativeRotationLocalA,
        in Physics3DServoSettings servo,
        in Physics3DSpringSettings spring)
    {
        TargetRelativeRotationLocalA = targetRelativeRotationLocalA;
        Servo = servo;
        Spring = spring;
    }

    public Quaternion TargetRelativeRotationLocalA { get; }
    public Physics3DServoSettings Servo { get; }
    public Physics3DSpringSettings Spring { get; }
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
        Physics3DContinuousDetectionMode continuousDetection,
        Physics3DBodyContactPolicy contactPolicy = default,
        Physics3DCollisionSubgroup collisionSubgroup = default)
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
        ContactPolicy = contactPolicy;
        CollisionSubgroup = collisionSubgroup;
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
    public Physics3DBodyContactPolicy ContactPolicy { get; }
    public Physics3DCollisionSubgroup CollisionSubgroup { get; }
}

public struct Physics3DBodyState
{
    public Vector3 PositionCm;
    public Quaternion Orientation;
    public Vector3 LinearVelocityCmPerSecond;
    public Vector3 AngularVelocityRadiansPerSecond;
    public bool Awake;
}

/// <summary>
/// Immutable collision-query filter. A default <see cref="Physics3DBodyId"/> means no ignored body.
/// Sensors are excluded by default and can be included explicitly without changing layer or ignored-body filtering.
/// </summary>
public readonly struct Physics3DQueryFilter
{
    public Physics3DQueryFilter(in LayerMask layerMask)
        : this(layerMask, ignoredBody: default, includeSensors: false, ignoredAssemblyId: 0)
    {
    }

    public Physics3DQueryFilter(in LayerMask layerMask, Physics3DBodyId ignoredBody)
        : this(layerMask, ignoredBody, includeSensors: false, ignoredAssemblyId: 0)
    {
    }

    public Physics3DQueryFilter(
        in LayerMask layerMask,
        Physics3DBodyId ignoredBody,
        bool includeSensors,
        uint ignoredAssemblyId = 0)
    {
        LayerMask = layerMask;
        IgnoredBody = ignoredBody;
        IncludeSensors = includeSensors;
        IgnoredAssemblyId = ignoredAssemblyId;
    }

    public LayerMask LayerMask { get; }

    /// <summary>
    /// Optional body excluded from results. A valid but stale id fails when the query is validated.
    /// </summary>
    public Physics3DBodyId IgnoredBody { get; }

    /// <summary>
    /// Whether sensor bodies participate in results. Solid bodies always participate when other filters allow them.
    /// </summary>
    public bool IncludeSensors { get; }

    /// <summary>
    /// Optional collision assembly excluded from results. Zero means no ignored assembly.
    /// </summary>
    public uint IgnoredAssemblyId { get; }
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

/// <summary>
/// One independently filtered ray in a closest-hit batch.
/// </summary>
public readonly struct Physics3DRaycastQuery
{
    public Physics3DRaycastQuery(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        OriginCm = originCm;
        Direction = direction;
        MaximumDistanceCm = maximumDistanceCm;
        Filter = filter;
    }

    public Vector3 OriginCm { get; }
    public Vector3 Direction { get; }
    public float MaximumDistanceCm { get; }
    public Physics3DQueryFilter Filter { get; }
}

/// <summary>
/// Closest-hit result aligned one-to-one with a <see cref="Physics3DRaycastQuery"/> batch entry.
/// </summary>
public readonly struct Physics3DBatchedRaycastClosestResult
{
    public Physics3DBatchedRaycastClosestResult(bool hit, in Physics3DRaycastHit value)
    {
        Hit = hit;
        Value = value;
    }

    public bool Hit { get; }
    public Physics3DRaycastHit Value { get; }
}

public readonly struct Physics3DBoxCastQuery
{
    public Physics3DBoxCastQuery(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        CenterCm = centerCm;
        SizeCm = sizeCm;
        Orientation = orientation;
        Direction = direction;
        MaximumDistanceCm = maximumDistanceCm;
        Filter = filter;
    }

    public Vector3 CenterCm { get; }
    public Vector3 SizeCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 Direction { get; }
    public float MaximumDistanceCm { get; }
    public Physics3DQueryFilter Filter { get; }
}

public readonly struct Physics3DSphereCastQuery
{
    public Physics3DSphereCastQuery(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        CenterCm = centerCm;
        RadiusCm = radiusCm;
        Direction = direction;
        MaximumDistanceCm = maximumDistanceCm;
        Filter = filter;
    }

    public Vector3 CenterCm { get; }
    public float RadiusCm { get; }
    public Vector3 Direction { get; }
    public float MaximumDistanceCm { get; }
    public Physics3DQueryFilter Filter { get; }
}

public readonly struct Physics3DCapsuleCastQuery
{
    public Physics3DCapsuleCastQuery(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        CenterCm = centerCm;
        RadiusCm = radiusCm;
        CylinderLengthCm = cylinderLengthCm;
        Orientation = orientation;
        Direction = direction;
        MaximumDistanceCm = maximumDistanceCm;
        Filter = filter;
    }

    public Vector3 CenterCm { get; }
    public float RadiusCm { get; }
    public float CylinderLengthCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 Direction { get; }
    public float MaximumDistanceCm { get; }
    public Physics3DQueryFilter Filter { get; }
}

public readonly struct Physics3DBatchedShapeCastClosestResult
{
    public Physics3DBatchedShapeCastClosestResult(bool hit, in Physics3DShapeCastHit value)
    {
        Hit = hit;
        Value = value;
    }

    public bool Hit { get; }
    public Physics3DShapeCastHit Value { get; }
}

public readonly struct Physics3DContactEvent
{
    public Physics3DContactEvent(
        Physics3DBodyId bodyA,
        Entity entityA,
        Physics3DBodyId bodyB,
        Entity entityB,
        Physics3DContactEventKind kind,
        Physics3DContactKind contactKind,
        long stepIndex)
    {
        BodyA = bodyA;
        EntityA = entityA;
        BodyB = bodyB;
        EntityB = entityB;
        Kind = kind;
        ContactKind = contactKind;
        StepIndex = stepIndex;
    }

    public Physics3DBodyId BodyA { get; }
    public Entity EntityA { get; }
    public Physics3DBodyId BodyB { get; }
    public Entity EntityB { get; }
    public Physics3DContactEventKind Kind { get; }
    public Physics3DContactKind ContactKind { get; }
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

/// <summary>
/// Thrown when a caller attempts Step or structural mutation on a Physics3DWorld that already entered
/// a terminal fault. The original finalization failure remains available as <see cref="Exception.InnerException"/>
/// and on <see cref="IPhysics3DWorld.TerminalFault"/>. No rollback or retry is supported; Dispose remains valid.
/// </summary>
public sealed class Physics3DTerminalFaultException : InvalidOperationException
{
    public Physics3DTerminalFaultException(Exception terminalFault, long stepIndex)
        : base(
            $"Physics3DWorld is in a terminal fault state after step {stepIndex}. " +
            "The Bepu simulation advanced but contact finalization failed; further Step and structural mutation are rejected. " +
            "Dispose remains valid. No rollback or retry is supported.",
            terminalFault)
    {
        ArgumentNullException.ThrowIfNull(terminalFault);
        TerminalFault = terminalFault;
        StepIndex = stepIndex;
    }

    public Exception TerminalFault { get; }
    public long StepIndex { get; }
}

internal static class Physics3DValidation
{
    public static void RequireFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite.");
        }
    }

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
