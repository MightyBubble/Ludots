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
    public Physics3DShowcaseQueryKind InitialQueryKind { get; set; }
    public Physics3DShowcaseQueryResultMode InitialResultMode { get; set; }
    public int InitialDistancePresetIndex { get; set; }
    public int InitialLayerFilterIndex { get; set; }
    public bool InitialIncludeSensors { get; set; }
    public bool InitialIgnoreSelf { get; set; }
    public bool InitialIgnoreAssembly { get; set; }
    public int CapsuleCastStartingOverlapTargetIndex { get; set; }
    public int SensorTargetIndex { get; set; }
    public uint SourceAssemblyId { get; set; }
    public float SourceMemberOffsetCm { get; set; }
    public int CastPlaybackDurationTicks { get; set; }
    public int OverlapPulseCycleTicks { get; set; }
    public float OverlapPulseMaximumScale { get; set; }
    public float ScanPathThicknessCm { get; set; }
    public float HitMarkerDiameterCm { get; set; }
    public float HitNumberHeightOffsetCm { get; set; }
    public float HitNumberHeightCm { get; set; }
    public float HitNumberThicknessCm { get; set; }
    public float[] DistancePresetsCm { get; set; } = Array.Empty<float>();
    public Physics3DScannerLayerShowcaseConfig[] Layers { get; set; } = Array.Empty<Physics3DScannerLayerShowcaseConfig>();
    public Physics3DScannerLayerFilterShowcaseConfig[] LayerFilters { get; set; } = Array.Empty<Physics3DScannerLayerFilterShowcaseConfig>();
    public int[] TargetLayerIndices { get; set; } = Array.Empty<int>();

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
        if (!Enum.IsDefined(InitialQueryKind)) throw new InvalidOperationException($"{path}.{nameof(InitialQueryKind)} is invalid.");
        if (!Enum.IsDefined(InitialResultMode)) throw new InvalidOperationException($"{path}.{nameof(InitialResultMode)} is invalid.");
        if ((uint)CapsuleCastStartingOverlapTargetIndex >= (uint)TargetCount)
        {
            throw new InvalidOperationException(
                $"{path}.{nameof(CapsuleCastStartingOverlapTargetIndex)} must select one authored target.");
        }
        if ((uint)SensorTargetIndex >= (uint)TargetCount)
        {
            throw new InvalidOperationException($"{path}.{nameof(SensorTargetIndex)} must select one authored target.");
        }
        if (SourceAssemblyId == 0u)
        {
            throw new InvalidOperationException($"{path}.{nameof(SourceAssemblyId)} must be non-zero.");
        }
        RequireFinitePositive(SourceMemberOffsetCm, $"{path}.{nameof(SourceMemberOffsetCm)}");
        RequirePositive(CastPlaybackDurationTicks, $"{path}.{nameof(CastPlaybackDurationTicks)}");
        if (OverlapPulseCycleTicks < 2)
        {
            throw new InvalidOperationException($"{path}.{nameof(OverlapPulseCycleTicks)} must be at least two fixed ticks.");
        }
        RequireFinite(OverlapPulseMaximumScale, $"{path}.{nameof(OverlapPulseMaximumScale)}");
        if (OverlapPulseMaximumScale <= 1f)
        {
            throw new InvalidOperationException($"{path}.{nameof(OverlapPulseMaximumScale)} must exceed one.");
        }
        RequireFinitePositive(ScanPathThicknessCm, $"{path}.{nameof(ScanPathThicknessCm)}");
        RequireFinitePositive(HitMarkerDiameterCm, $"{path}.{nameof(HitMarkerDiameterCm)}");
        RequireFinitePositive(HitNumberHeightOffsetCm, $"{path}.{nameof(HitNumberHeightOffsetCm)}");
        RequireFinitePositive(HitNumberHeightCm, $"{path}.{nameof(HitNumberHeightCm)}");
        RequireFinitePositive(HitNumberThicknessCm, $"{path}.{nameof(HitNumberThicknessCm)}");
        if (HitNumberThicknessCm >= HitNumberHeightCm * 0.5f)
        {
            throw new InvalidOperationException(
                $"{path}.{nameof(HitNumberThicknessCm)} must remain below half of {nameof(HitNumberHeightCm)}.");
        }
        if (DistancePresetsCm == null || DistancePresetsCm.Length < 2)
        {
            throw new InvalidOperationException($"{path}.{nameof(DistancePresetsCm)} must define at least two distance choices.");
        }

        for (int i = 0; i < DistancePresetsCm.Length; i++)
        {
            RequireFinitePositive(DistancePresetsCm[i], $"{path}.{nameof(DistancePresetsCm)}[{i}]");
            if (i > 0 && DistancePresetsCm[i] <= DistancePresetsCm[i - 1])
            {
                throw new InvalidOperationException($"{path}.{nameof(DistancePresetsCm)} must be strictly increasing.");
            }
        }

        if ((uint)InitialDistancePresetIndex >= (uint)DistancePresetsCm.Length)
        {
            throw new InvalidOperationException($"{path}.{nameof(InitialDistancePresetIndex)} is outside the configured choices.");
        }

        if (Layers == null || Layers.Length < 2)
        {
            throw new InvalidOperationException($"{path}.{nameof(Layers)} must define at least two real target layers.");
        }

        uint knownCategories = 0u;
        for (int i = 0; i < Layers.Length; i++)
        {
            Physics3DScannerLayerShowcaseConfig layer = Layers[i]
                ?? throw new InvalidOperationException($"{path}.{nameof(Layers)}[{i}] cannot be null.");
            layer.Validate($"{path}.{nameof(Layers)}[{i}]");
            if ((knownCategories & layer.Category) != 0u)
            {
                throw new InvalidOperationException($"{path}.{nameof(Layers)}[{i}] category must be unique.");
            }
            knownCategories |= layer.Category;
        }

        if (LayerFilters == null || LayerFilters.Length < 3)
        {
            throw new InvalidOperationException($"{path}.{nameof(LayerFilters)} must define both target layers and an all-targets choice.");
        }

        bool coversAllLayers = false;
        for (int i = 0; i < LayerFilters.Length; i++)
        {
            Physics3DScannerLayerFilterShowcaseConfig filter = LayerFilters[i]
                ?? throw new InvalidOperationException($"{path}.{nameof(LayerFilters)}[{i}] cannot be null.");
            filter.Validate($"{path}.{nameof(LayerFilters)}[{i}]", knownCategories);
            coversAllLayers |= (filter.Mask & knownCategories) == knownCategories;
        }

        if (!coversAllLayers)
        {
            throw new InvalidOperationException($"{path}.{nameof(LayerFilters)} must include one filter covering every configured target layer.");
        }

        if ((uint)InitialLayerFilterIndex >= (uint)LayerFilters.Length)
        {
            throw new InvalidOperationException($"{path}.{nameof(InitialLayerFilterIndex)} is outside the configured choices.");
        }

        if (TargetLayerIndices == null || TargetLayerIndices.Length != TargetCount)
        {
            throw new InvalidOperationException($"{path}.{nameof(TargetLayerIndices)} must contain one layer index per target.");
        }

        for (int i = 0; i < TargetLayerIndices.Length; i++)
        {
            if ((uint)TargetLayerIndices[i] >= (uint)Layers.Length)
            {
                throw new InvalidOperationException($"{path}.{nameof(TargetLayerIndices)}[{i}] is outside the configured layers.");
            }
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

    private static void RequirePositive(int value, string path)
    {
        if (value <= 0) throw new InvalidOperationException($"{path} must be greater than zero.");
    }
}

internal sealed class Physics3DScannerLayerShowcaseConfig
{
    public string Name { get; set; } = string.Empty;
    public uint Category { get; set; }
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }

    public void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException($"{path}.{nameof(Name)} is required.");
        if (Category == 0u || (Category & (Category - 1u)) != 0u)
        {
            throw new InvalidOperationException($"{path}.{nameof(Category)} must contain exactly one category bit.");
        }
        RequireColor(ColorR, $"{path}.{nameof(ColorR)}");
        RequireColor(ColorG, $"{path}.{nameof(ColorG)}");
        RequireColor(ColorB, $"{path}.{nameof(ColorB)}");
    }

    private static void RequireColor(float value, string path)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            throw new InvalidOperationException($"{path} must be finite and inside [0, 1].");
        }
    }
}

internal sealed class Physics3DScannerLayerFilterShowcaseConfig
{
    public string Name { get; set; } = string.Empty;
    public uint Mask { get; set; }

    public void Validate(string path, uint knownCategories)
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException($"{path}.{nameof(Name)} is required.");
        if (Mask == 0u || (Mask & knownCategories) == 0u)
        {
            throw new InvalidOperationException($"{path}.{nameof(Mask)} must include at least one configured target layer.");
        }
        if ((Mask & ~knownCategories) != 0u)
        {
            throw new InvalidOperationException($"{path}.{nameof(Mask)} contains an unknown target layer.");
        }
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
    public bool InitialDriveEnabled { get; set; }
    public Physics3DShowcaseDriveDirection InitialDriveDirection { get; set; }

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
        if (!Enum.IsDefined(InitialDriveDirection))
        {
            throw new InvalidOperationException($"{path}.{nameof(InitialDriveDirection)} is invalid.");
        }
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
