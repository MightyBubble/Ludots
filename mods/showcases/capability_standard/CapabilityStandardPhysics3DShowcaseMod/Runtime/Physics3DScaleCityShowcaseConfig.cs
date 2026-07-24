using System;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DScaleCityShowcaseConfig
{
    public int InteractiveBodyLimit { get; set; }
    public int InteractiveColumns { get; set; }
    public int InteractiveRows { get; set; }
    public float InteractiveSpacingCm { get; set; }
    public float InteractiveBaseHeightCm { get; set; }
    public float InteractiveLayerSpacingCm { get; set; }
    public float WindAccelerationCmPerSecondSquared { get; set; }
    public int WindCycleTicks { get; set; }
    public int LauncherWaveCount { get; set; }
    public int LauncherIntervalTicks { get; set; }
    public float LauncherUpSpeedCmPerSecond { get; set; }
    public float LauncherOutwardSpeedCmPerSecond { get; set; }
    public int PerformanceWindowSampleCount { get; set; }

    public void Validate(string parameterName, int bodySizeCm, int smallestPreset)
    {
        RequirePositive(InteractiveBodyLimit, nameof(InteractiveBodyLimit));
        RequirePositive(InteractiveColumns, nameof(InteractiveColumns));
        RequirePositive(InteractiveRows, nameof(InteractiveRows));
        RequireFinitePositive(InteractiveSpacingCm, nameof(InteractiveSpacingCm));
        RequireFinitePositive(InteractiveBaseHeightCm, nameof(InteractiveBaseHeightCm));
        RequireFinitePositive(InteractiveLayerSpacingCm, nameof(InteractiveLayerSpacingCm));
        RequireFinitePositive(WindAccelerationCmPerSecondSquared, nameof(WindAccelerationCmPerSecondSquared));
        RequirePositive(WindCycleTicks, nameof(WindCycleTicks));
        RequirePositive(LauncherWaveCount, nameof(LauncherWaveCount));
        RequirePositive(LauncherIntervalTicks, nameof(LauncherIntervalTicks));
        RequireFinitePositive(LauncherUpSpeedCmPerSecond, nameof(LauncherUpSpeedCmPerSecond));
        RequireFinitePositive(LauncherOutwardSpeedCmPerSecond, nameof(LauncherOutwardSpeedCmPerSecond));
        RequirePositive(PerformanceWindowSampleCount, nameof(PerformanceWindowSampleCount));

        if (InteractiveBodyLimit >= smallestPreset)
        {
            throw new InvalidOperationException(
                $"{parameterName}.interactiveBodyLimit must leave sparse activity in the smallest Scale City preset.");
        }

        if (InteractiveSpacingCm <= bodySizeCm)
        {
            throw new InvalidOperationException(
                $"{parameterName}.interactiveSpacingCm must exceed bodySizeCm so the city starts without body overlap.");
        }

        if (InteractiveLayerSpacingCm <= bodySizeCm)
        {
            throw new InvalidOperationException(
                $"{parameterName}.interactiveLayerSpacingCm must exceed bodySizeCm so stacked layers start without overlap.");
        }

        if (LauncherWaveCount > InteractiveBodyLimit)
        {
            throw new InvalidOperationException(
                $"{parameterName}.launcherWaveCount cannot exceed interactiveBodyLimit.");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Scale City requires {name} > 0.");
        }
    }

    private static void RequireFinitePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new InvalidOperationException($"Scale City requires finite {name} > 0.");
        }
    }
}
