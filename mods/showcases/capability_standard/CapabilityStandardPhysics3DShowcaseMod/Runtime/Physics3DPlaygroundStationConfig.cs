using System;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DScannerRangeShowcaseConfig
{
    public float CastOriginXCm { get; set; }
    public float OverlapOriginXCm { get; set; }
    public float OriginYCm { get; set; }
    public float FirstLaneZCm { get; set; }
    public float LaneSpacingCm { get; set; }
    public float FirstTargetXCm { get; set; }
    public float TargetSpacingCm { get; set; }
    public int TargetCount { get; set; }

    public void Validate(string path)
    {
        RequireFinite(CastOriginXCm, $"{path}.{nameof(CastOriginXCm)}");
        RequireFinite(OverlapOriginXCm, $"{path}.{nameof(OverlapOriginXCm)}");
        RequireFinite(OriginYCm, $"{path}.{nameof(OriginYCm)}");
        RequireFinite(FirstLaneZCm, $"{path}.{nameof(FirstLaneZCm)}");
        RequireFinitePositive(LaneSpacingCm, $"{path}.{nameof(LaneSpacingCm)}");
        RequireFinite(FirstTargetXCm, $"{path}.{nameof(FirstTargetXCm)}");
        RequireFinitePositive(TargetSpacingCm, $"{path}.{nameof(TargetSpacingCm)}");
        if (TargetCount < 3) throw new InvalidOperationException($"{path}.{nameof(TargetCount)} must be at least three.");
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
}

internal sealed class Physics3DConstraintForgeShowcaseConfig
{
    public uint CollisionAssemblyId { get; set; }
    public float FirstExhibitXCm { get; set; }
    public float ExhibitSpacingXCm { get; set; }
    public float AnchorYCm { get; set; }
    public float PairSeparationYCm { get; set; }
    public float LinearMinimumCm { get; set; }
    public float LinearMaximumCm { get; set; }
    public float LinearTargetCenterCm { get; set; }
    public float LinearTargetAmplitudeCm { get; set; }
    public float TargetCycleRadiansPerTick { get; set; }
    public float AxisMotorSpeedRadiansPerSecond { get; set; }
    public float AngularServoAmplitudeRadians { get; set; }
    public float SwingLimitRadians { get; set; }
    public float MinimumTwistRadians { get; set; }
    public float MaximumTwistRadians { get; set; }
    public float ServoMaximumSpeed { get; set; }
    public float ServoMaximumForce { get; set; }
    public float MotorMaximumForce { get; set; }
    public float MotorSoftness { get; set; }

    public void Validate(string path)
    {
        if (CollisionAssemblyId == 0) throw new InvalidOperationException($"{path}.{nameof(CollisionAssemblyId)} must be non-zero.");
        RequireFinite(FirstExhibitXCm, $"{path}.{nameof(FirstExhibitXCm)}");
        RequireFinitePositive(ExhibitSpacingXCm, $"{path}.{nameof(ExhibitSpacingXCm)}");
        RequireFinitePositive(AnchorYCm, $"{path}.{nameof(AnchorYCm)}");
        RequireFinitePositive(PairSeparationYCm, $"{path}.{nameof(PairSeparationYCm)}");
        RequireFinite(LinearMinimumCm, $"{path}.{nameof(LinearMinimumCm)}");
        RequireFinite(LinearMaximumCm, $"{path}.{nameof(LinearMaximumCm)}");
        if (LinearMaximumCm <= LinearMinimumCm) throw new InvalidOperationException($"{path} linear maximum must exceed its minimum.");
        RequireFinite(LinearTargetCenterCm, $"{path}.{nameof(LinearTargetCenterCm)}");
        RequireFinitePositive(LinearTargetAmplitudeCm, $"{path}.{nameof(LinearTargetAmplitudeCm)}");
        if (LinearTargetCenterCm - LinearTargetAmplitudeCm < LinearMinimumCm ||
            LinearTargetCenterCm + LinearTargetAmplitudeCm > LinearMaximumCm)
        {
            throw new InvalidOperationException($"{path} linear target range must remain inside its limit.");
        }

        RequireFinitePositive(TargetCycleRadiansPerTick, $"{path}.{nameof(TargetCycleRadiansPerTick)}");
        RequireFinitePositive(AxisMotorSpeedRadiansPerSecond, $"{path}.{nameof(AxisMotorSpeedRadiansPerSecond)}");
        RequireFinitePositive(AngularServoAmplitudeRadians, $"{path}.{nameof(AngularServoAmplitudeRadians)}");
        RequireAngle(SwingLimitRadians, $"{path}.{nameof(SwingLimitRadians)}", allowNegative: false);
        RequireAngle(MinimumTwistRadians, $"{path}.{nameof(MinimumTwistRadians)}", allowNegative: true);
        RequireAngle(MaximumTwistRadians, $"{path}.{nameof(MaximumTwistRadians)}", allowNegative: true);
        if (MaximumTwistRadians <= MinimumTwistRadians) throw new InvalidOperationException($"{path} twist maximum must exceed its minimum.");
        RequireFinitePositive(ServoMaximumSpeed, $"{path}.{nameof(ServoMaximumSpeed)}");
        RequireFinitePositive(ServoMaximumForce, $"{path}.{nameof(ServoMaximumForce)}");
        RequireFinitePositive(MotorMaximumForce, $"{path}.{nameof(MotorMaximumForce)}");
        RequireFiniteNonNegative(MotorSoftness, $"{path}.{nameof(MotorSoftness)}");
    }

    private static void RequireAngle(float value, string path, bool allowNegative)
    {
        RequireFinite(value, path);
        if ((!allowNegative && value < 0f) || value < -MathF.PI || value > MathF.PI)
        {
            throw new InvalidOperationException($"{path} must be inside the supported angular range.");
        }
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
