using System;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Composition-boundary validator for the Physics3D networked fixed-clock contract.
/// Engine FixedHz, network SimulationTickRateHz, and Physics3D FixedStepHz must all equal 30,
/// and Physics3D must take exactly one physics step per source tick.
/// </summary>
public static class Physics3DNetworkClockValidator
{
    public const int RequiredHz = 30;
    public const int RequiredMaximumPhysicsStepsPerSourceTick = 1;

    /// <summary>
    /// Validates the integer Hz contract and the Physics3D steps-per-source-tick contract.
    /// </summary>
    public static void Validate(
        int engineFixedHz,
        int networkSimulationTickRateHz,
        int physicsFixedStepHz,
        int maximumPhysicsStepsPerSourceTick)
    {
        RequireExactHz(engineFixedHz, "Engine FixedHz");
        RequireExactHz(networkSimulationTickRateHz, "Network SimulationTickRateHz");
        RequireExactHz(physicsFixedStepHz, "Physics3D FixedStepHz");

        if (maximumPhysicsStepsPerSourceTick != RequiredMaximumPhysicsStepsPerSourceTick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPhysicsStepsPerSourceTick),
                maximumPhysicsStepsPerSourceTick,
                $"Physics3D MaximumPhysicsStepsPerSourceTick must be {RequiredMaximumPhysicsStepsPerSourceTick} for the networked 30Hz contract.");
        }
    }

    /// <summary>
    /// Validates engine/network/physics configs against the hard 30Hz networked clock contract.
    /// Engine fixed delta must be exactly representable as 1/integer Hz and that Hz must be 30.
    /// </summary>
    public static void Validate(
        float engineFixedDeltaSeconds,
        NetworkRuntimeConfig networkConfig,
        Physics3DWorldConfig physicsConfig)
    {
        ArgumentNullException.ThrowIfNull(networkConfig);
        ArgumentNullException.ThrowIfNull(physicsConfig);

        int engineFixedHz = RequireRepresentableIntegerHz(engineFixedDeltaSeconds, nameof(engineFixedDeltaSeconds));
        Validate(
            engineFixedHz,
            networkConfig.SimulationTickRateHz,
            physicsConfig.FixedStepHz,
            physicsConfig.MaximumPhysicsStepsPerSourceTick);
    }

    /// <summary>
    /// Validates engine FixedHz together with networking and Physics3D world configs.
    /// </summary>
    public static void Validate(
        int engineFixedHz,
        NetworkRuntimeConfig networkConfig,
        Physics3DWorldConfig physicsConfig)
    {
        ArgumentNullException.ThrowIfNull(networkConfig);
        ArgumentNullException.ThrowIfNull(physicsConfig);

        Validate(
            engineFixedHz,
            networkConfig.SimulationTickRateHz,
            physicsConfig.FixedStepHz,
            physicsConfig.MaximumPhysicsStepsPerSourceTick);
    }

    public static int RequireRepresentableIntegerHz(float deltaSeconds, string paramName)
    {
        if (!(deltaSeconds > 0f) || !float.IsFinite(deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                deltaSeconds,
                "Fixed delta must be a finite positive duration representable as 1/integer Hz.");
        }

        int hz = (int)MathF.Round(1f / deltaSeconds);
        if (hz <= 0 || MathF.Abs((1f / hz) - deltaSeconds) > 1e-5f)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                deltaSeconds,
                $"Fixed delta '{deltaSeconds}' is not representable as 1/integer Hz.");
        }

        return hz;
    }

    private static void RequireExactHz(int hz, string label)
    {
        if (hz != RequiredHz)
        {
            throw new ArgumentOutOfRangeException(
                paramName: label,
                actualValue: hz,
                message: $"{label} must be {RequiredHz} for the Physics3D networked clock contract.");
        }
    }
}
