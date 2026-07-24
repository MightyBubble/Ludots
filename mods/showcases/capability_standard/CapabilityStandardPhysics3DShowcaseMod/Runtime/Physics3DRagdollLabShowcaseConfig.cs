using System;
using System.Numerics;
using Ludots.Core.Physics3D;
using Ludots.Core.Ragdoll;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DRagdollLabShowcaseConfig
{
    public int RecipeStableId { get; set; }
    public uint CollisionAssemblyId { get; set; }
    public float TotalMass { get; set; }
    public int RecoveryOverlapHitCapacity { get; set; }
    public float RecoveryCharacterRadiusCm { get; set; }
    public float RecoveryCharacterCylinderLengthCm { get; set; }
    public RagdollRecoveryStrategy RecoveryStrategy { get; set; }
    public float RecoveryCenterOffsetXCm { get; set; }
    public float RecoveryCenterOffsetYCm { get; set; }
    public float RecoveryCenterOffsetZCm { get; set; }
    public float MaximumInheritedSpeedCmPerSecond { get; set; }
    public int StairCount { get; set; }
    public float StairWidthCm { get; set; }
    public float StairDepthCm { get; set; }
    public float StairHeightCm { get; set; }
    public float StairStartXCm { get; set; }
    public float RagdollStartHeightCm { get; set; }
    public float PendulumAnchorXCm { get; set; }
    public float PendulumAnchorYCm { get; set; }
    public float PendulumRopeLengthCm { get; set; }
    public float PendulumLaunchImpulse { get; set; }
    public Physics3DRagdollBoneShowcaseConfig[] Bones { get; set; } = Array.Empty<Physics3DRagdollBoneShowcaseConfig>();

    public void Validate(string path)
    {
        RequirePositive(RecipeStableId, $"{path}.{nameof(RecipeStableId)}");
        if (CollisionAssemblyId == 0) throw new InvalidOperationException($"{path}.{nameof(CollisionAssemblyId)} must be non-zero.");
        RequireFinitePositive(TotalMass, $"{path}.{nameof(TotalMass)}");
        RequirePositive(RecoveryOverlapHitCapacity, $"{path}.{nameof(RecoveryOverlapHitCapacity)}");
        RequireFinitePositive(RecoveryCharacterRadiusCm, $"{path}.{nameof(RecoveryCharacterRadiusCm)}");
        RequireFiniteNonNegative(RecoveryCharacterCylinderLengthCm, $"{path}.{nameof(RecoveryCharacterCylinderLengthCm)}");
        if (!Enum.IsDefined(RecoveryStrategy)) throw new InvalidOperationException($"{path}.{nameof(RecoveryStrategy)} is invalid.");
        RequireFinite(RecoveryCenterOffsetXCm, $"{path}.{nameof(RecoveryCenterOffsetXCm)}");
        RequireFinite(RecoveryCenterOffsetYCm, $"{path}.{nameof(RecoveryCenterOffsetYCm)}");
        RequireFinite(RecoveryCenterOffsetZCm, $"{path}.{nameof(RecoveryCenterOffsetZCm)}");
        RequireFiniteNonNegative(MaximumInheritedSpeedCmPerSecond, $"{path}.{nameof(MaximumInheritedSpeedCmPerSecond)}");
        RequirePositive(StairCount, $"{path}.{nameof(StairCount)}");
        RequireFinitePositive(StairWidthCm, $"{path}.{nameof(StairWidthCm)}");
        RequireFinitePositive(StairDepthCm, $"{path}.{nameof(StairDepthCm)}");
        RequireFinitePositive(StairHeightCm, $"{path}.{nameof(StairHeightCm)}");
        RequireFinite(StairStartXCm, $"{path}.{nameof(StairStartXCm)}");
        RequireFinitePositive(RagdollStartHeightCm, $"{path}.{nameof(RagdollStartHeightCm)}");
        RequireFinite(PendulumAnchorXCm, $"{path}.{nameof(PendulumAnchorXCm)}");
        RequireFinitePositive(PendulumAnchorYCm, $"{path}.{nameof(PendulumAnchorYCm)}");
        RequireFinitePositive(PendulumRopeLengthCm, $"{path}.{nameof(PendulumRopeLengthCm)}");
        RequireFinitePositive(PendulumLaunchImpulse, $"{path}.{nameof(PendulumLaunchImpulse)}");
        if (Bones == null || Bones.Length < 2 || Bones.Length > 32)
        {
            throw new InvalidOperationException($"{path}.{nameof(Bones)} must contain between 2 and 32 bones.");
        }

        for (int i = 0; i < Bones.Length; i++)
        {
            (Bones[i] ?? throw new InvalidOperationException($"{path}.{nameof(Bones)}[{i}] cannot be null.")).Validate($"{path}.{nameof(Bones)}[{i}]");
        }
    }

    public RagdollRecipeDefinition CreateRecipe()
    {
        var definitions = new RagdollBoneDefinition[Bones.Length];
        for (int i = 0; i < Bones.Length; i++)
        {
            definitions[i] = Bones[i].CreateDefinition();
        }

        return new RagdollRecipeDefinition
        {
            StableId = RecipeStableId,
            Recovery = new RagdollRecoverySettings(
                RecoveryStrategy,
                new Vector3(RecoveryCenterOffsetXCm, RecoveryCenterOffsetYCm, RecoveryCenterOffsetZCm),
                MaximumInheritedSpeedCmPerSecond),
            Bones = definitions
        };
    }

    private static void RequirePositive(int value, string path)
    {
        if (value <= 0) throw new InvalidOperationException($"{path} must be greater than zero.");
    }

    private static void RequireFinite(float value, string path)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException($"{path} must be finite.");
    }

    private static void RequireFinitePositive(float value, string path)
    {
        RequireFinite(value, path);
        if (value <= 0f) throw new InvalidOperationException($"{path} must be greater than zero.");
    }

    private static void RequireFiniteNonNegative(float value, string path)
    {
        RequireFinite(value, path);
        if (value < 0f) throw new InvalidOperationException($"{path} cannot be negative.");
    }
}

internal sealed class Physics3DRagdollBoneShowcaseConfig
{
    public int StableId { get; set; }
    public int ParentIndex { get; set; } = -1;
    public float LocalPositionXCm { get; set; }
    public float LocalPositionYCm { get; set; }
    public float LocalPositionZCm { get; set; }
    public float LocalOrientationX { get; set; }
    public float LocalOrientationY { get; set; }
    public float LocalOrientationZ { get; set; }
    public float LocalOrientationW { get; set; } = 1f;
    public RagdollShapeKind ShapeKind { get; set; }
    public float ShapeXCm { get; set; }
    public float ShapeYCm { get; set; }
    public float ShapeZCm { get; set; }
    public float MassRatio { get; set; }
    public float ParentAnchorXCm { get; set; }
    public float ParentAnchorYCm { get; set; }
    public float ParentAnchorZCm { get; set; }
    public float BoneAnchorXCm { get; set; }
    public float BoneAnchorYCm { get; set; }
    public float BoneAnchorZCm { get; set; }
    public float JointFrameParentX { get; set; }
    public float JointFrameParentY { get; set; }
    public float JointFrameParentZ { get; set; }
    public float JointFrameParentW { get; set; } = 1f;
    public float JointFrameBoneX { get; set; }
    public float JointFrameBoneY { get; set; }
    public float JointFrameBoneZ { get; set; }
    public float JointFrameBoneW { get; set; } = 1f;
    public float MaximumSwingAngleRadians { get; set; }
    public float MinimumTwistAngleRadians { get; set; }
    public float MaximumTwistAngleRadians { get; set; }
    public float JointAngularFrequency { get; set; }
    public float JointTwiceDampingRatio { get; set; }
    public int CollisionSubgroupIndex { get; set; }
    public uint CollidesWithSubgroupsMask { get; set; }
    public float ActivePoseMaximumSpeed { get; set; }
    public float ActivePoseMaximumForce { get; set; }
    public float ActivePoseAngularFrequency { get; set; }
    public float ActivePoseTwiceDampingRatio { get; set; }

    public void Validate(string path)
    {
        if (StableId <= 0) throw new InvalidOperationException($"{path}.{nameof(StableId)} must be greater than zero.");
        if (!Enum.IsDefined(ShapeKind)) throw new InvalidOperationException($"{path}.{nameof(ShapeKind)} is invalid.");
        RequireFinite(LocalPositionXCm, $"{path}.{nameof(LocalPositionXCm)}");
        RequireFinite(LocalPositionYCm, $"{path}.{nameof(LocalPositionYCm)}");
        RequireFinite(LocalPositionZCm, $"{path}.{nameof(LocalPositionZCm)}");
        RequireFinite(LocalOrientationX, $"{path}.{nameof(LocalOrientationX)}");
        RequireFinite(LocalOrientationY, $"{path}.{nameof(LocalOrientationY)}");
        RequireFinite(LocalOrientationZ, $"{path}.{nameof(LocalOrientationZ)}");
        RequireFinite(LocalOrientationW, $"{path}.{nameof(LocalOrientationW)}");
        RequireFinitePositive(ShapeXCm, $"{path}.{nameof(ShapeXCm)}");
        if (ShapeKind != RagdollShapeKind.Sphere) RequireFiniteNonNegative(ShapeYCm, $"{path}.{nameof(ShapeYCm)}");
        if (ShapeKind == RagdollShapeKind.Box) RequireFinitePositive(ShapeZCm, $"{path}.{nameof(ShapeZCm)}");
        RequireFinitePositive(MassRatio, $"{path}.{nameof(MassRatio)}");
        RequireFinite(MaximumSwingAngleRadians, $"{path}.{nameof(MaximumSwingAngleRadians)}");
        RequireFinite(MinimumTwistAngleRadians, $"{path}.{nameof(MinimumTwistAngleRadians)}");
        RequireFinite(MaximumTwistAngleRadians, $"{path}.{nameof(MaximumTwistAngleRadians)}");
        RequireUnitQuaternion(
            new Quaternion(JointFrameParentX, JointFrameParentY, JointFrameParentZ, JointFrameParentW),
            $"{path}.JointFrameParent");
        RequireUnitQuaternion(
            new Quaternion(JointFrameBoneX, JointFrameBoneY, JointFrameBoneZ, JointFrameBoneW),
            $"{path}.JointFrameBone");
        RequireFinitePositive(JointAngularFrequency, $"{path}.{nameof(JointAngularFrequency)}");
        RequireFiniteNonNegative(JointTwiceDampingRatio, $"{path}.{nameof(JointTwiceDampingRatio)}");
        RequireFiniteNonNegative(ActivePoseMaximumSpeed, $"{path}.{nameof(ActivePoseMaximumSpeed)}");
        RequireFinitePositive(ActivePoseMaximumForce, $"{path}.{nameof(ActivePoseMaximumForce)}");
        RequireFinitePositive(ActivePoseAngularFrequency, $"{path}.{nameof(ActivePoseAngularFrequency)}");
        RequireFiniteNonNegative(ActivePoseTwiceDampingRatio, $"{path}.{nameof(ActivePoseTwiceDampingRatio)}");
    }

    public RagdollBoneDefinition CreateDefinition()
    {
        RagdollShapeDefinition shape = ShapeKind switch
        {
            RagdollShapeKind.Box => RagdollShapeDefinition.Box(new Vector3(ShapeXCm, ShapeYCm, ShapeZCm)),
            RagdollShapeKind.Sphere => RagdollShapeDefinition.Sphere(ShapeXCm),
            RagdollShapeKind.Capsule => RagdollShapeDefinition.Capsule(ShapeXCm, ShapeYCm),
            _ => throw new InvalidOperationException($"Unsupported ragdoll shape {ShapeKind}.")
        };
        return new RagdollBoneDefinition
        {
            StableId = StableId,
            ParentIndex = ParentIndex,
            LocalPositionCm = new Vector3(LocalPositionXCm, LocalPositionYCm, LocalPositionZCm),
            LocalOrientation = new Quaternion(LocalOrientationX, LocalOrientationY, LocalOrientationZ, LocalOrientationW),
            Shape = shape,
            MassRatio = MassRatio,
            ParentAnchorLocalCm = new Vector3(ParentAnchorXCm, ParentAnchorYCm, ParentAnchorZCm),
            BoneAnchorLocalCm = new Vector3(BoneAnchorXCm, BoneAnchorYCm, BoneAnchorZCm),
            JointFrameLocalParent = new Quaternion(JointFrameParentX, JointFrameParentY, JointFrameParentZ, JointFrameParentW),
            JointFrameLocalBone = new Quaternion(JointFrameBoneX, JointFrameBoneY, JointFrameBoneZ, JointFrameBoneW),
            MaximumSwingAngleRadians = MaximumSwingAngleRadians,
            MinimumTwistAngleRadians = MinimumTwistAngleRadians,
            MaximumTwistAngleRadians = MaximumTwistAngleRadians,
            JointSpring = new Physics3DSpringSettings(JointAngularFrequency, JointTwiceDampingRatio),
            CollisionSubgroupIndex = CollisionSubgroupIndex,
            CollidesWithSubgroupsMask = CollidesWithSubgroupsMask,
            ActivePoseServo = new Physics3DServoSettings(ActivePoseMaximumSpeed, 0f, ActivePoseMaximumForce),
            ActivePoseSpring = new Physics3DSpringSettings(ActivePoseAngularFrequency, ActivePoseTwiceDampingRatio)
        };
    }

    private static void RequireFinite(float value, string path)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException($"{path} must be finite.");
    }

    private static void RequireFinitePositive(float value, string path)
    {
        RequireFinite(value, path);
        if (value <= 0f) throw new InvalidOperationException($"{path} must be greater than zero.");
    }

    private static void RequireFiniteNonNegative(float value, string path)
    {
        RequireFinite(value, path);
        if (value < 0f) throw new InvalidOperationException($"{path} cannot be negative.");
    }

    private static void RequireUnitQuaternion(Quaternion value, string path)
    {
        RequireFinite(value.X, path);
        RequireFinite(value.Y, path);
        RequireFinite(value.Z, path);
        RequireFinite(value.W, path);
        if (MathF.Abs(value.LengthSquared() - 1f) > 0.001f)
        {
            throw new InvalidOperationException($"{path} must be normalized.");
        }
    }
}
