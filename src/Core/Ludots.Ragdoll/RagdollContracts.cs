using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Ragdoll;

public enum RagdollShapeKind : byte
{
    Box = 1,
    Sphere = 2,
    Capsule = 3
}

public enum RagdollRecoveryStrategy : byte
{
    PreserveRootYaw = 1,
    FaceWorldForward = 2
}

public enum RagdollRecoveryState : byte
{
    None = 0,
    Blocked = 1,
    Ready = 2
}

public readonly struct RagdollRecipeId : IEquatable<RagdollRecipeId>
{
    public RagdollRecipeId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public int Slot { get; }
    public int Generation { get; }
    public bool IsValid => Slot >= 0 && Generation > 0;

    public bool Equals(RagdollRecipeId other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is RagdollRecipeId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(RagdollRecipeId left, RagdollRecipeId right) => left.Equals(right);
    public static bool operator !=(RagdollRecipeId left, RagdollRecipeId right) => !left.Equals(right);
    public override string ToString() => $"RagdollRecipeId({Slot}:{Generation})";
}

public readonly struct RagdollInstanceId : IEquatable<RagdollInstanceId>
{
    public RagdollInstanceId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public int Slot { get; }
    public int Generation { get; }
    public bool IsValid => Slot >= 0 && Generation > 0;

    public bool Equals(RagdollInstanceId other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is RagdollInstanceId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(RagdollInstanceId left, RagdollInstanceId right) => left.Equals(right);
    public static bool operator !=(RagdollInstanceId left, RagdollInstanceId right) => !left.Equals(right);
    public override string ToString() => $"RagdollInstanceId({Slot}:{Generation})";
}

public readonly struct RagdollShapeDefinition
{
    private RagdollShapeDefinition(RagdollShapeKind kind, Vector3 dimensionsCm)
    {
        Kind = kind;
        DimensionsCm = dimensionsCm;
    }

    public RagdollShapeKind Kind { get; }
    public Vector3 DimensionsCm { get; }

    public static RagdollShapeDefinition Box(Vector3 sizeCm) => new(RagdollShapeKind.Box, sizeCm);
    public static RagdollShapeDefinition Sphere(float radiusCm) => new(RagdollShapeKind.Sphere, new Vector3(radiusCm, 0f, 0f));
    public static RagdollShapeDefinition Capsule(float radiusCm, float cylinderLengthCm)
        => new(RagdollShapeKind.Capsule, new Vector3(radiusCm, cylinderLengthCm, 0f));
}

public sealed class RagdollBoneDefinition
{
    public int StableId { get; init; }
    public int ParentIndex { get; init; } = -1;
    public Vector3 LocalPositionCm { get; init; }
    public Quaternion LocalOrientation { get; init; } = Quaternion.Identity;
    public RagdollShapeDefinition Shape { get; init; }
    public float MassRatio { get; init; }
    public Vector3 ParentAnchorLocalCm { get; init; }
    public Vector3 BoneAnchorLocalCm { get; init; }
    public Quaternion JointFrameLocalParent { get; init; } = Quaternion.Identity;
    public Quaternion JointFrameLocalBone { get; init; } = Quaternion.Identity;
    public float MaximumSwingAngleRadians { get; init; }
    public float MinimumTwistAngleRadians { get; init; }
    public float MaximumTwistAngleRadians { get; init; }
    public Physics3DSpringSettings JointSpring { get; init; }
    public int CollisionSubgroupIndex { get; init; }
    public uint CollidesWithSubgroupsMask { get; init; }
    public Physics3DServoSettings ActivePoseServo { get; init; }
    public Physics3DSpringSettings ActivePoseSpring { get; init; }
}

public readonly struct RagdollRecoverySettings
{
    public RagdollRecoverySettings(
        RagdollRecoveryStrategy strategy,
        Vector3 characterCenterOffsetLocalCm,
        float maximumInheritedLinearSpeedCmPerSecond)
    {
        Strategy = strategy;
        CharacterCenterOffsetLocalCm = characterCenterOffsetLocalCm;
        MaximumInheritedLinearSpeedCmPerSecond = maximumInheritedLinearSpeedCmPerSecond;
    }

    public RagdollRecoveryStrategy Strategy { get; }
    public Vector3 CharacterCenterOffsetLocalCm { get; }
    public float MaximumInheritedLinearSpeedCmPerSecond { get; }
}

public sealed class RagdollRecipeDefinition
{
    public int StableId { get; init; }
    public RagdollBoneDefinition[] Bones { get; init; } = Array.Empty<RagdollBoneDefinition>();
    public RagdollRecoverySettings Recovery { get; init; }
}

public sealed class RagdollConfig
{
    public int RecipeCapacity { get; init; }
    public int RecipeBoneCapacity { get; init; }
    public int InstanceCapacity { get; init; }
    public int MaximumBonesPerInstance { get; init; }
    public int RecoveryOverlapHitCapacity { get; init; }
    public int FixedStepHz { get; init; } = 30;

    internal void Validate()
    {
        RequirePositive(RecipeCapacity, nameof(RecipeCapacity));
        RequirePositive(RecipeBoneCapacity, nameof(RecipeBoneCapacity));
        RequirePositive(InstanceCapacity, nameof(InstanceCapacity));
        RequirePositive(MaximumBonesPerInstance, nameof(MaximumBonesPerInstance));
        RequirePositive(RecoveryOverlapHitCapacity, nameof(RecoveryOverlapHitCapacity));
        if (MaximumBonesPerInstance > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumBonesPerInstance),
                MaximumBonesPerInstance,
                "Ragdoll collision subgroups support at most 32 bones per instance.");
        }

        if (RecipeBoneCapacity < MaximumBonesPerInstance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RecipeBoneCapacity),
                RecipeBoneCapacity,
                "Recipe bone capacity must hold at least one maximum-sized recipe.");
        }

        if (FixedStepHz != 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FixedStepHz),
                FixedStepHz,
                "Ragdoll authoritative simulation is fixed at 30Hz.");
        }
    }

    private static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be greater than zero.");
        }
    }
}

public readonly struct RagdollBoneHandoff
{
    public RagdollBoneHandoff(
        Entity entity,
        Vector3 positionCm,
        Quaternion orientation,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond)
    {
        Entity = entity;
        PositionCm = positionCm;
        Orientation = orientation;
        LinearVelocityCmPerSecond = linearVelocityCmPerSecond;
        AngularVelocityRadiansPerSecond = angularVelocityRadiansPerSecond;
    }

    public Entity Entity { get; }
    public Vector3 PositionCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 LinearVelocityCmPerSecond { get; }
    public Vector3 AngularVelocityRadiansPerSecond { get; }
}

public readonly struct RagdollActivationDescription
{
    public RagdollActivationDescription(
        uint collisionAssemblyId,
        float totalMass,
        in LayerMask collisionLayer,
        in Physics3DMaterial material,
        Physics3DContinuousDetectionMode continuousDetection,
        bool activePoseEnabled)
    {
        CollisionAssemblyId = collisionAssemblyId;
        TotalMass = totalMass;
        CollisionLayer = collisionLayer;
        Material = material;
        ContinuousDetection = continuousDetection;
        ActivePoseEnabled = activePoseEnabled;
    }

    public uint CollisionAssemblyId { get; }
    public float TotalMass { get; }
    public LayerMask CollisionLayer { get; }
    public Physics3DMaterial Material { get; }
    public Physics3DContinuousDetectionMode ContinuousDetection { get; }
    public bool ActivePoseEnabled { get; }
}

public readonly struct RagdollBonePose
{
    public RagdollBonePose(int stableId, Vector3 positionCm, Quaternion orientation)
    {
        StableId = stableId;
        PositionCm = positionCm;
        Orientation = orientation;
    }

    public int StableId { get; }
    public Vector3 PositionCm { get; }
    public Quaternion Orientation { get; }
}

public readonly struct RagdollBoneState
{
    public RagdollBoneState(int stableId, Physics3DBodyId body, in Physics3DBodyState state)
    {
        StableId = stableId;
        Body = body;
        State = state;
    }

    public int StableId { get; }
    public Physics3DBodyId Body { get; }
    public Physics3DBodyState State { get; }
}

public readonly struct RagdollInstanceState
{
    public RagdollInstanceState(
        RagdollRecipeId recipe,
        int boneCount,
        uint collisionAssemblyId,
        bool activePoseEnabled,
        RagdollRecoveryState recoveryState,
        int recoveryBlockerCount)
    {
        Recipe = recipe;
        BoneCount = boneCount;
        CollisionAssemblyId = collisionAssemblyId;
        ActivePoseEnabled = activePoseEnabled;
        RecoveryState = recoveryState;
        RecoveryBlockerCount = recoveryBlockerCount;
    }

    public RagdollRecipeId Recipe { get; }
    public int BoneCount { get; }
    public uint CollisionAssemblyId { get; }
    public bool ActivePoseEnabled { get; }
    public RagdollRecoveryState RecoveryState { get; }
    public int RecoveryBlockerCount { get; }
}

public readonly struct RagdollRecoveryCandidate
{
    public RagdollRecoveryCandidate(
        bool isClear,
        int blockerCount,
        Vector3 characterCenterCm,
        Quaternion characterOrientation,
        Vector3 inheritedLinearVelocityCmPerSecond)
    {
        IsClear = isClear;
        BlockerCount = blockerCount;
        CharacterCenterCm = characterCenterCm;
        CharacterOrientation = characterOrientation;
        InheritedLinearVelocityCmPerSecond = inheritedLinearVelocityCmPerSecond;
    }

    public bool IsClear { get; }
    public int BlockerCount { get; }
    public Vector3 CharacterCenterCm { get; }
    public Quaternion CharacterOrientation { get; }
    public Vector3 InheritedLinearVelocityCmPerSecond { get; }
}

public sealed class RagdollCapacityExceededException : InvalidOperationException
{
    public RagdollCapacityExceededException(string resource, int capacity, int required)
        : base($"Ragdoll capacity exceeded for '{resource}' (configured capacity: {capacity}, required: {required}).")
    {
        Resource = resource;
        Capacity = capacity;
        Required = required;
    }

    public string Resource { get; }
    public int Capacity { get; }
    public int Required { get; }
}

public sealed class RagdollStateException : InvalidOperationException
{
    public RagdollStateException(RagdollInstanceId instance, int boneIndex, long tick, string detail)
        : base($"Ragdoll state is invalid at tick {tick} for instance {instance}, bone {boneIndex}: {detail}")
    {
        Instance = instance;
        BoneIndex = boneIndex;
        Tick = tick;
    }

    public RagdollInstanceId Instance { get; }
    public int BoneIndex { get; }
    public long Tick { get; }
}
