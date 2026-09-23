using System;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DStationCameraShowcaseConfig
{
    public Physics3DShowcaseScene Scene { get; set; }
    public float TargetXCm { get; set; }
    public float TargetZCm { get; set; }
    public float TargetHeightCm { get; set; }
    public float DistanceCm { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float FovYDeg { get; set; }

    public void Validate(string parameterName)
    {
        if (!Enum.IsDefined(Scene))
        {
            throw new InvalidOperationException($"{parameterName} has an invalid scene.");
        }

        RequireFinite(TargetXCm, nameof(TargetXCm), parameterName);
        RequireFinite(TargetZCm, nameof(TargetZCm), parameterName);
        RequireFinite(TargetHeightCm, nameof(TargetHeightCm), parameterName);
        RequireFinite(Yaw, nameof(Yaw), parameterName);
        RequireRange(DistanceCm, 1f, 100_000f, nameof(DistanceCm), parameterName);
        RequireRange(Pitch, 1f, 89f, nameof(Pitch), parameterName);
        RequireRange(FovYDeg, 20f, 100f, nameof(FovYDeg), parameterName);
    }

    private static void RequireFinite(float value, string propertyName, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidOperationException($"{parameterName}.{propertyName} must be finite.");
        }
    }

    private static void RequireRange(
        float value,
        float minimum,
        float maximum,
        string propertyName,
        string parameterName)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{parameterName}.{propertyName} must be inside [{minimum}, {maximum}].");
        }
    }
}
