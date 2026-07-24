using System;
using System.Numerics;

namespace Ludots.Core.Physics3D;

public sealed class Physics3DWorldConfig
{
    public int MobileBodyCapacity { get; init; }
    public int StaticBodyCapacity { get; init; }
    public int ShapeCapacity { get; init; }
    public int InactiveIslandCapacity { get; init; }
    public int ConstraintCapacity { get; init; }
    public int ConstraintsPerTypeBatchCapacity { get; init; }
    public int ConstraintCountPerBodyEstimate { get; init; }
    public int ContactPairCapacityPerWorker { get; init; }
    public int ActuationCommandCapacity { get; init; }
    public int WorkerCount { get; init; }
    public int ThreadMemoryPoolBlockAllocationSize { get; init; } = 16_384;
    public int MemoryPoolExpectedPooledResourceCount { get; init; } = 256;
    public int FixedStepHz { get; init; }
    public int MaximumPhysicsStepsPerSourceTick { get; init; }
    public int SolverSubstepCount { get; init; }
    public int SolverVelocityIterationCount { get; init; }
    public Vector3 GravityCmPerSecondSquared { get; init; }
    public float LinearDamping { get; init; }
    public float AngularDamping { get; init; }
    public float MaximumSpeculativeMarginCm { get; init; }
    public float SleepThreshold { get; init; }
    public byte MinimumTimestepCountUnderSleepThreshold { get; init; }
    public float ContinuousMinimumSweepTimestep { get; init; }
    public float ContinuousSweepConvergenceThreshold { get; init; }
    public Physics3DMaterialCombineMode MaterialCombineMode { get; init; }

    public float FixedDeltaSeconds => 1f / FixedStepHz;

    public void Validate()
    {
        RequirePositive(MobileBodyCapacity, nameof(MobileBodyCapacity));
        RequireNonNegative(StaticBodyCapacity, nameof(StaticBodyCapacity));
        RequirePositive(ShapeCapacity, nameof(ShapeCapacity));
        RequirePositive(InactiveIslandCapacity, nameof(InactiveIslandCapacity));
        RequirePositive(ConstraintCapacity, nameof(ConstraintCapacity));
        RequirePositive(ConstraintsPerTypeBatchCapacity, nameof(ConstraintsPerTypeBatchCapacity));
        RequirePositive(ConstraintCountPerBodyEstimate, nameof(ConstraintCountPerBodyEstimate));
        RequirePositive(ContactPairCapacityPerWorker, nameof(ContactPairCapacityPerWorker));
        RequirePositive(ActuationCommandCapacity, nameof(ActuationCommandCapacity));
        RequirePositive(WorkerCount, nameof(WorkerCount));
        RequirePowerOfTwo(ThreadMemoryPoolBlockAllocationSize, nameof(ThreadMemoryPoolBlockAllocationSize));
        RequirePositive(MemoryPoolExpectedPooledResourceCount, nameof(MemoryPoolExpectedPooledResourceCount));
        RequirePositive(FixedStepHz, nameof(FixedStepHz));
        RequirePositive(MaximumPhysicsStepsPerSourceTick, nameof(MaximumPhysicsStepsPerSourceTick));
        RequirePositive(SolverSubstepCount, nameof(SolverSubstepCount));
        RequirePositive(SolverVelocityIterationCount, nameof(SolverVelocityIterationCount));
        Physics3DValidation.RequireFinite(GravityCmPerSecondSquared, nameof(GravityCmPerSecondSquared));
        RequireUnitInterval(LinearDamping, nameof(LinearDamping));
        RequireUnitInterval(AngularDamping, nameof(AngularDamping));
        Physics3DValidation.RequireFinitePositive(MaximumSpeculativeMarginCm, nameof(MaximumSpeculativeMarginCm));
        Physics3DValidation.RequireFiniteNonNegative(SleepThreshold, nameof(SleepThreshold));
        if (MinimumTimestepCountUnderSleepThreshold == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumTimestepCountUnderSleepThreshold));
        }

        Physics3DValidation.RequireFinitePositive(ContinuousMinimumSweepTimestep, nameof(ContinuousMinimumSweepTimestep));
        Physics3DValidation.RequireFinitePositive(ContinuousSweepConvergenceThreshold, nameof(ContinuousSweepConvergenceThreshold));
        if (!Enum.IsDefined(MaterialCombineMode))
        {
            throw new ArgumentOutOfRangeException(nameof(MaterialCombineMode));
        }
    }

    private static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be greater than zero.");
        }
    }

    private static void RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be non-negative.");
        }
    }

    private static void RequirePowerOfTwo(int value, string parameterName)
    {
        if (value <= 0 || (value & (value - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be a positive power of two.");
        }
    }

    private static void RequireUnitInterval(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and in the inclusive range [0, 1].");
        }
    }
}
