using System;
using System.Numerics;
using Ludots.Core.Layers;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Character3D;

public readonly struct Character3DHandle : IEquatable<Character3DHandle>
{
    public Character3DHandle(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public int Slot { get; }
    public int Generation { get; }
    public bool IsValid => Slot >= 0 && Generation > 0;

    public bool Equals(Character3DHandle other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is Character3DHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(Character3DHandle left, Character3DHandle right) => left.Equals(right);
    public static bool operator !=(Character3DHandle left, Character3DHandle right) => !left.Equals(right);
    public override string ToString() => $"Character3DHandle({Slot}:{Generation})";
}

public enum Character3DLocomotionMode : byte
{
    Airborne = 1,
    Grounded = 2,
    Traversal = 3
}

public readonly struct Character3DProfile
{
    public Character3DProfile(
        float radiusCm,
        float cylinderLengthCm,
        float maximumGroundSpeedCmPerSecond,
        float maximumGroundAccelerationCmPerSecondSquared,
        float maximumAirSpeedCmPerSecond,
        float maximumAirAccelerationCmPerSecondSquared,
        float jumpSpeedCmPerSecond,
        float maximumSlopeDegrees,
        float supportProbeDistanceCm,
        float skinWidthCm,
        float maximumStepHeightCm,
        float stepForwardProbeDistanceCm,
        float stepAssistSpeedCmPerSecond,
        int coyoteTicks,
        in LayerMask queryLayer,
        in Physics3DServoSettings uprightServo,
        in Physics3DSpringSettings uprightSpring)
    {
        RadiusCm = radiusCm;
        CylinderLengthCm = cylinderLengthCm;
        MaximumGroundSpeedCmPerSecond = maximumGroundSpeedCmPerSecond;
        MaximumGroundAccelerationCmPerSecondSquared = maximumGroundAccelerationCmPerSecondSquared;
        MaximumAirSpeedCmPerSecond = maximumAirSpeedCmPerSecond;
        MaximumAirAccelerationCmPerSecondSquared = maximumAirAccelerationCmPerSecondSquared;
        JumpSpeedCmPerSecond = jumpSpeedCmPerSecond;
        MaximumSlopeDegrees = maximumSlopeDegrees;
        SupportProbeDistanceCm = supportProbeDistanceCm;
        SkinWidthCm = skinWidthCm;
        MaximumStepHeightCm = maximumStepHeightCm;
        StepForwardProbeDistanceCm = stepForwardProbeDistanceCm;
        StepAssistSpeedCmPerSecond = stepAssistSpeedCmPerSecond;
        CoyoteTicks = coyoteTicks;
        QueryLayer = queryLayer;
        UprightServo = uprightServo;
        UprightSpring = uprightSpring;
    }

    public float RadiusCm { get; }
    public float CylinderLengthCm { get; }
    public float MaximumGroundSpeedCmPerSecond { get; }
    public float MaximumGroundAccelerationCmPerSecondSquared { get; }
    public float MaximumAirSpeedCmPerSecond { get; }
    public float MaximumAirAccelerationCmPerSecondSquared { get; }
    public float JumpSpeedCmPerSecond { get; }
    public float MaximumSlopeDegrees { get; }
    public float SupportProbeDistanceCm { get; }
    public float SkinWidthCm { get; }
    public float MaximumStepHeightCm { get; }
    public float StepForwardProbeDistanceCm { get; }
    public float StepAssistSpeedCmPerSecond { get; }
    public int CoyoteTicks { get; }
    public LayerMask QueryLayer { get; }
    public Physics3DServoSettings UprightServo { get; }
    public Physics3DSpringSettings UprightSpring { get; }

    internal void Validate(string parameterName)
    {
        Character3DValidation.RequireFinitePositive(RadiusCm, $"{parameterName}.{nameof(RadiusCm)}");
        Character3DValidation.RequireFiniteNonNegative(CylinderLengthCm, $"{parameterName}.{nameof(CylinderLengthCm)}");
        Character3DValidation.RequireFinitePositive(MaximumGroundSpeedCmPerSecond, $"{parameterName}.{nameof(MaximumGroundSpeedCmPerSecond)}");
        Character3DValidation.RequireFinitePositive(MaximumGroundAccelerationCmPerSecondSquared, $"{parameterName}.{nameof(MaximumGroundAccelerationCmPerSecondSquared)}");
        Character3DValidation.RequireFinitePositive(MaximumAirSpeedCmPerSecond, $"{parameterName}.{nameof(MaximumAirSpeedCmPerSecond)}");
        Character3DValidation.RequireFinitePositive(MaximumAirAccelerationCmPerSecondSquared, $"{parameterName}.{nameof(MaximumAirAccelerationCmPerSecondSquared)}");
        Character3DValidation.RequireFinitePositive(JumpSpeedCmPerSecond, $"{parameterName}.{nameof(JumpSpeedCmPerSecond)}");
        if (!float.IsFinite(MaximumSlopeDegrees) || MaximumSlopeDegrees <= 0f || MaximumSlopeDegrees >= 90f)
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(MaximumSlopeDegrees)}",
                MaximumSlopeDegrees,
                "Maximum slope must be finite and in the exclusive range (0, 90) degrees.");
        }

        Character3DValidation.RequireFinitePositive(SupportProbeDistanceCm, $"{parameterName}.{nameof(SupportProbeDistanceCm)}");
        Character3DValidation.RequireFinitePositive(SkinWidthCm, $"{parameterName}.{nameof(SkinWidthCm)}");
        if (SkinWidthCm >= RadiusCm)
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(SkinWidthCm)}",
                SkinWidthCm,
                "Skin width must be smaller than the capsule radius.");
        }

        Character3DValidation.RequireFinitePositive(MaximumStepHeightCm, $"{parameterName}.{nameof(MaximumStepHeightCm)}");
        Character3DValidation.RequireFinitePositive(StepForwardProbeDistanceCm, $"{parameterName}.{nameof(StepForwardProbeDistanceCm)}");
        Character3DValidation.RequireFinitePositive(StepAssistSpeedCmPerSecond, $"{parameterName}.{nameof(StepAssistSpeedCmPerSecond)}");
        if (CoyoteTicks < 0)
        {
            throw new ArgumentOutOfRangeException($"{parameterName}.{nameof(CoyoteTicks)}", CoyoteTicks, "Coyote ticks cannot be negative.");
        }

        if (QueryLayer.Mask == 0u)
        {
            throw new ArgumentOutOfRangeException($"{parameterName}.{nameof(QueryLayer)}", "Character query layer must include at least one target layer.");
        }

        Physics3DServoSettings uprightServo = UprightServo;
        Physics3DSpringSettings uprightSpring = UprightSpring;
        Character3DValidation.ValidateServo(in uprightServo, $"{parameterName}.{nameof(UprightServo)}");
        Character3DValidation.ValidateSpring(in uprightSpring, $"{parameterName}.{nameof(UprightSpring)}");
    }
}

public readonly struct Character3DIntent
{
    public Character3DIntent(Vector2 planarMove, bool jumpRequested)
        : this(planarMove, jumpRequested, false, default, 0f)
    {
    }

    private Character3DIntent(
        Vector2 planarMove,
        bool jumpRequested,
        bool hasVelocityOverride,
        Vector3 targetVelocityCmPerSecond,
        float maximumOverrideAccelerationCmPerSecondSquared)
    {
        PlanarMove = planarMove;
        JumpRequested = jumpRequested;
        HasVelocityOverride = hasVelocityOverride;
        TargetVelocityCmPerSecond = targetVelocityCmPerSecond;
        MaximumOverrideAccelerationCmPerSecondSquared = maximumOverrideAccelerationCmPerSecondSquared;
    }

    public Vector2 PlanarMove { get; }
    public bool JumpRequested { get; }
    public bool HasVelocityOverride { get; }
    public Vector3 TargetVelocityCmPerSecond { get; }
    public float MaximumOverrideAccelerationCmPerSecondSquared { get; }

    public static Character3DIntent TraversalVelocity(
        Vector3 targetVelocityCmPerSecond,
        float maximumAccelerationCmPerSecondSquared)
        => new(default, false, true, targetVelocityCmPerSecond, maximumAccelerationCmPerSecondSquared);

    internal void Validate(string parameterName)
    {
        if (!float.IsFinite(PlanarMove.X) || !float.IsFinite(PlanarMove.Y))
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(PlanarMove)}",
                PlanarMove,
                "Planar movement must be finite.");
        }

        if (PlanarMove.LengthSquared() > 1.0001f)
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(PlanarMove)}",
                PlanarMove,
                "Planar movement magnitude cannot exceed one.");
        }

        if (!HasVelocityOverride)
        {
            return;
        }

        if (!float.IsFinite(TargetVelocityCmPerSecond.X) ||
            !float.IsFinite(TargetVelocityCmPerSecond.Y) ||
            !float.IsFinite(TargetVelocityCmPerSecond.Z))
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(TargetVelocityCmPerSecond)}",
                TargetVelocityCmPerSecond,
                "Target velocity must be finite.");
        }

        if (!float.IsFinite(MaximumOverrideAccelerationCmPerSecondSquared) ||
            MaximumOverrideAccelerationCmPerSecondSquared <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(MaximumOverrideAccelerationCmPerSecondSquared)}",
                MaximumOverrideAccelerationCmPerSecondSquared,
                "Maximum override acceleration must be finite and greater than zero.");
        }
    }
}

public readonly struct Character3DState
{
    public Character3DState(
        Physics3DBodyId body,
        Character3DLocomotionMode locomotionMode,
        Vector3 positionCm,
        Vector3 linearVelocityCmPerSecond,
        Physics3DBodyId supportBody,
        Vector3 supportPointCm,
        Vector3 supportNormal,
        Vector3 supportVelocityCmPerSecond,
        bool stepAssistActive,
        int ticksSinceSupport)
    {
        Body = body;
        LocomotionMode = locomotionMode;
        PositionCm = positionCm;
        LinearVelocityCmPerSecond = linearVelocityCmPerSecond;
        SupportBody = supportBody;
        SupportPointCm = supportPointCm;
        SupportNormal = supportNormal;
        SupportVelocityCmPerSecond = supportVelocityCmPerSecond;
        StepAssistActive = stepAssistActive;
        TicksSinceSupport = ticksSinceSupport;
    }

    public Physics3DBodyId Body { get; }
    public Character3DLocomotionMode LocomotionMode { get; }
    public Vector3 PositionCm { get; }
    public Vector3 LinearVelocityCmPerSecond { get; }
    public Physics3DBodyId SupportBody { get; }
    public Vector3 SupportPointCm { get; }
    public Vector3 SupportNormal { get; }
    public Vector3 SupportVelocityCmPerSecond { get; }
    public bool StepAssistActive { get; }
    public int TicksSinceSupport { get; }
    public bool IsGrounded => LocomotionMode == Character3DLocomotionMode.Grounded;
}

public readonly struct Character3DGeometry
{
    public Character3DGeometry(
        Physics3DBodyId body,
        float radiusCm,
        float cylinderLengthCm,
        in LayerMask queryLayer)
    {
        Body = body;
        RadiusCm = radiusCm;
        CylinderLengthCm = cylinderLengthCm;
        QueryLayer = queryLayer;
    }

    public Physics3DBodyId Body { get; }
    public float RadiusCm { get; }
    public float CylinderLengthCm { get; }
    public LayerMask QueryLayer { get; }
    public float HalfHeightCm => RadiusCm + (CylinderLengthCm * 0.5f);
}

public sealed class Character3DCapacityExceededException : InvalidOperationException
{
    public Character3DCapacityExceededException(string resource, int capacity)
        : base($"Character3D capacity exceeded for '{resource}' (configured capacity: {capacity}).")
    {
        Resource = resource;
        Capacity = capacity;
    }

    public string Resource { get; }
    public int Capacity { get; }
}

internal static class Character3DValidation
{
    public static void RequireFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite.");
        }
    }

    public static void RequireFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Vector must be finite.");
        }
    }

    public static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Vector must be finite.");
        }
    }

    public static void RequireFinitePositive(float value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be greater than zero.");
        }
    }

    public static void RequireFiniteNonNegative(float value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }

    public static void ValidateServo(in Physics3DServoSettings value, string parameterName)
    {
        RequireFiniteNonNegative(value.MaximumSpeed, $"{parameterName}.{nameof(value.MaximumSpeed)}");
        RequireFiniteNonNegative(value.BaseSpeed, $"{parameterName}.{nameof(value.BaseSpeed)}");
        RequireFiniteNonNegative(value.MaximumForce, $"{parameterName}.{nameof(value.MaximumForce)}");
    }

    public static void ValidateSpring(in Physics3DSpringSettings value, string parameterName)
    {
        RequireFinitePositive(value.AngularFrequency, $"{parameterName}.{nameof(value.AngularFrequency)}");
        RequireFiniteNonNegative(value.TwiceDampingRatio, $"{parameterName}.{nameof(value.TwiceDampingRatio)}");
    }
}
