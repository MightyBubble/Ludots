using System;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DMaterialHillShowcaseConfig
{
    public float RampWidthCm { get; set; }
    public float RampLengthCm { get; set; }
    public float RampThicknessCm { get; set; }
    public float RampAngleDegrees { get; set; }
    public float RampCenterZCm { get; set; }
    public float RampBaseYCm { get; set; }
    public float BoxSizeCm { get; set; }
    public float BoxMass { get; set; }
    public float PushImpulseMassCmPerSecond { get; set; }
    public int CompletionTimeLimitTicks { get; set; }
    public int RequiredStableTicks { get; set; }
    public float StableMaximumLinearSpeedCmPerSecond { get; set; }
    public float StableMaximumAngularSpeedRadiansPerSecond { get; set; }
    public float SettlingLinearDragPerSecond { get; set; }
    public float SettlingAngularTorquePerAngularSpeed { get; set; }
    public float SettlingStartTravelCm { get; set; }
    public Physics3DMaterialHillLaneShowcaseConfig[] Lanes { get; set; } = Array.Empty<Physics3DMaterialHillLaneShowcaseConfig>();

    public void Validate(string path)
    {
        RequireFinitePositive(RampWidthCm, $"{path}.{nameof(RampWidthCm)}");
        RequireFinitePositive(RampLengthCm, $"{path}.{nameof(RampLengthCm)}");
        RequireFinitePositive(RampThicknessCm, $"{path}.{nameof(RampThicknessCm)}");
        RequireFinitePositive(RampAngleDegrees, $"{path}.{nameof(RampAngleDegrees)}");
        if (RampAngleDegrees >= 45f)
        {
            throw new InvalidOperationException($"{path}.{nameof(RampAngleDegrees)} must be less than 45 degrees.");
        }

        RequireFinite(RampCenterZCm, $"{path}.{nameof(RampCenterZCm)}");
        RequireFiniteNonNegative(RampBaseYCm, $"{path}.{nameof(RampBaseYCm)}");
        RequireFinitePositive(BoxSizeCm, $"{path}.{nameof(BoxSizeCm)}");
        RequireFinitePositive(BoxMass, $"{path}.{nameof(BoxMass)}");
        RequireFinitePositive(PushImpulseMassCmPerSecond, $"{path}.{nameof(PushImpulseMassCmPerSecond)}");
        RequirePositive(CompletionTimeLimitTicks, $"{path}.{nameof(CompletionTimeLimitTicks)}");
        RequirePositive(RequiredStableTicks, $"{path}.{nameof(RequiredStableTicks)}");
        if (RequiredStableTicks >= CompletionTimeLimitTicks)
        {
            throw new InvalidOperationException(
                $"{path}.{nameof(RequiredStableTicks)} must be less than {nameof(CompletionTimeLimitTicks)}.");
        }

        RequireFinitePositive(
            StableMaximumLinearSpeedCmPerSecond,
            $"{path}.{nameof(StableMaximumLinearSpeedCmPerSecond)}");
        RequireFinitePositive(
            StableMaximumAngularSpeedRadiansPerSecond,
            $"{path}.{nameof(StableMaximumAngularSpeedRadiansPerSecond)}");
        RequireFinitePositive(SettlingLinearDragPerSecond, $"{path}.{nameof(SettlingLinearDragPerSecond)}");
        RequireFinitePositive(
            SettlingAngularTorquePerAngularSpeed,
            $"{path}.{nameof(SettlingAngularTorquePerAngularSpeed)}");
        RequireFinitePositive(SettlingStartTravelCm, $"{path}.{nameof(SettlingStartTravelCm)}");
        if (SettlingStartTravelCm >= RampLengthCm)
        {
            throw new InvalidOperationException(
                $"{path}.{nameof(SettlingStartTravelCm)} must be less than {nameof(RampLengthCm)}.");
        }
        if (Lanes == null || Lanes.Length != 3)
        {
            throw new InvalidOperationException($"{path}.{nameof(Lanes)} must define exactly the ice, wood, and rubber lanes.");
        }

        for (int i = 0; i < Lanes.Length; i++)
        {
            (Lanes[i] ?? throw new InvalidOperationException($"{path}.{nameof(Lanes)}[{i}] cannot be null."))
                .Validate($"{path}.{nameof(Lanes)}[{i}]");
        }
    }

    internal static void RequireFinite(float value, string path)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException($"{path} must be finite.");
    }

    internal static void RequireFinitePositive(float value, string path)
    {
        RequireFinite(value, path);
        if (value <= 0f) throw new InvalidOperationException($"{path} must be greater than zero.");
    }

    internal static void RequireFiniteNonNegative(float value, string path)
    {
        RequireFinite(value, path);
        if (value < 0f) throw new InvalidOperationException($"{path} cannot be negative.");
    }

    private static void RequirePositive(int value, string path)
    {
        if (value <= 0) throw new InvalidOperationException($"{path} must be greater than zero.");
    }
}

internal sealed class Physics3DMaterialHillLaneShowcaseConfig
{
    public string Name { get; set; } = string.Empty;
    public float CenterXCm { get; set; }
    public float FrictionCoefficient { get; set; }
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }

    public void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException($"{path}.{nameof(Name)} is required.");
        Physics3DMaterialHillShowcaseConfig.RequireFinite(CenterXCm, $"{path}.{nameof(CenterXCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFiniteNonNegative(FrictionCoefficient, $"{path}.{nameof(FrictionCoefficient)}");
        RequireColor(ColorR, $"{path}.{nameof(ColorR)}");
        RequireColor(ColorG, $"{path}.{nameof(ColorG)}");
        RequireColor(ColorB, $"{path}.{nameof(ColorB)}");
    }

    private static void RequireColor(float value, string path)
    {
        Physics3DMaterialHillShowcaseConfig.RequireFinite(value, path);
        if (value < 0f || value > 1f) throw new InvalidOperationException($"{path} must be in [0, 1].");
    }
}

internal sealed class Physics3DWindTunnelShowcaseConfig
{
    public int FieldCapacity { get; set; }
    public int AwakeBodyCapacity { get; set; }
    public float ZoneWidthCm { get; set; }
    public float ZoneHeightCm { get; set; }
    public float ZoneDepthCm { get; set; }
    public float ZoneCenterYCm { get; set; }
    public float SteadyCenterXCm { get; set; }
    public float GustCenterXCm { get; set; }
    public float VortexCenterXCm { get; set; }
    public float ObjectPairSpacingZCm { get; set; }
    public float ObjectRadiusCm { get; set; }
    public float LightMass { get; set; }
    public float HeavyMass { get; set; }
    public float ForcePerRelativeSpeed { get; set; }
    public float SteadySpeedCmPerSecond { get; set; }
    public float GustBaseSpeedCmPerSecond { get; set; }
    public float GustPeakSpeedCmPerSecond { get; set; }
    public int GustAttackTicks { get; set; }
    public int GustHoldTicks { get; set; }
    public int GustReleaseTicks { get; set; }
    public int GustCalmTicks { get; set; }
    public float VortexRadiusCm { get; set; }
    public float VortexTangentialSpeedCmPerSecond { get; set; }
    public float VortexAxialSpeedCmPerSecond { get; set; }
    public Physics3DShowcaseWindZone InitialZone { get; set; }
    public Physics3DShowcaseDriveDirection InitialDirection { get; set; }

    public void Validate(string path)
    {
        RequirePositive(FieldCapacity, $"{path}.{nameof(FieldCapacity)}");
        RequirePositive(AwakeBodyCapacity, $"{path}.{nameof(AwakeBodyCapacity)}");
        if (FieldCapacity < 3) throw new InvalidOperationException($"{path}.{nameof(FieldCapacity)} must cover three wind fields.");
        if (AwakeBodyCapacity < 6) throw new InvalidOperationException($"{path}.{nameof(AwakeBodyCapacity)} must cover all six comparison bodies.");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(ZoneWidthCm, $"{path}.{nameof(ZoneWidthCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(ZoneHeightCm, $"{path}.{nameof(ZoneHeightCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(ZoneDepthCm, $"{path}.{nameof(ZoneDepthCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(ZoneCenterYCm, $"{path}.{nameof(ZoneCenterYCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinite(SteadyCenterXCm, $"{path}.{nameof(SteadyCenterXCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinite(GustCenterXCm, $"{path}.{nameof(GustCenterXCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinite(VortexCenterXCm, $"{path}.{nameof(VortexCenterXCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(ObjectPairSpacingZCm, $"{path}.{nameof(ObjectPairSpacingZCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(ObjectRadiusCm, $"{path}.{nameof(ObjectRadiusCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(LightMass, $"{path}.{nameof(LightMass)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(HeavyMass, $"{path}.{nameof(HeavyMass)}");
        if (HeavyMass <= LightMass) throw new InvalidOperationException($"{path}.{nameof(HeavyMass)} must exceed {nameof(LightMass)}.");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(ForcePerRelativeSpeed, $"{path}.{nameof(ForcePerRelativeSpeed)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(SteadySpeedCmPerSecond, $"{path}.{nameof(SteadySpeedCmPerSecond)}");
        Physics3DMaterialHillShowcaseConfig.RequireFiniteNonNegative(GustBaseSpeedCmPerSecond, $"{path}.{nameof(GustBaseSpeedCmPerSecond)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(GustPeakSpeedCmPerSecond, $"{path}.{nameof(GustPeakSpeedCmPerSecond)}");
        if (GustPeakSpeedCmPerSecond <= GustBaseSpeedCmPerSecond)
        {
            throw new InvalidOperationException($"{path}.{nameof(GustPeakSpeedCmPerSecond)} must exceed {nameof(GustBaseSpeedCmPerSecond)}.");
        }

        RequirePositive(GustAttackTicks, $"{path}.{nameof(GustAttackTicks)}");
        RequirePositive(GustHoldTicks, $"{path}.{nameof(GustHoldTicks)}");
        RequirePositive(GustReleaseTicks, $"{path}.{nameof(GustReleaseTicks)}");
        if (GustCalmTicks < 0) throw new InvalidOperationException($"{path}.{nameof(GustCalmTicks)} cannot be negative.");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(VortexRadiusCm, $"{path}.{nameof(VortexRadiusCm)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinitePositive(VortexTangentialSpeedCmPerSecond, $"{path}.{nameof(VortexTangentialSpeedCmPerSecond)}");
        Physics3DMaterialHillShowcaseConfig.RequireFinite(VortexAxialSpeedCmPerSecond, $"{path}.{nameof(VortexAxialSpeedCmPerSecond)}");
        if (!Enum.IsDefined(InitialZone)) throw new InvalidOperationException($"{path}.{nameof(InitialZone)} is invalid.");
        if (!Enum.IsDefined(InitialDirection)) throw new InvalidOperationException($"{path}.{nameof(InitialDirection)} is invalid.");
    }

    private static void RequirePositive(int value, string path)
    {
        if (value <= 0) throw new InvalidOperationException($"{path} must be greater than zero.");
    }
}
