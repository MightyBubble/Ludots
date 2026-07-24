namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal enum Physics3DScaleCityPerformanceStatus : byte
{
    Warming = 0,
    Pass = 1,
    OverBudget = 2
}

internal readonly record struct Physics3DScaleCityShowcaseState(
    int InteractiveBodies,
    int SparseBodies,
    int ContactPairs,
    float WindAccelerationXCmPerSecondSquared,
    int LastLauncherWaveIndex,
    int InteractiveRelaunchedBodiesLastStep,
    int SparseRecycledBodiesLastStep,
    int PerformanceSampleCount,
    int PerformanceWindowCapacity,
    double StepP50Milliseconds,
    double StepP95Milliseconds,
    double StepP99Milliseconds,
    double PerformanceBudgetMilliseconds,
    Physics3DScaleCityPerformanceStatus PerformanceStatus)
{
    public static Physics3DScaleCityShowcaseState Empty { get; } = new(
        InteractiveBodies: 0,
        SparseBodies: 0,
        ContactPairs: 0,
        WindAccelerationXCmPerSecondSquared: 0f,
        LastLauncherWaveIndex: -1,
        InteractiveRelaunchedBodiesLastStep: 0,
        SparseRecycledBodiesLastStep: 0,
        PerformanceSampleCount: 0,
        PerformanceWindowCapacity: 0,
        StepP50Milliseconds: 0d,
        StepP95Milliseconds: 0d,
        StepP99Milliseconds: 0d,
        PerformanceBudgetMilliseconds: 0d,
        PerformanceStatus: Physics3DScaleCityPerformanceStatus.Warming);

    public int TotalBodies => InteractiveBodies + SparseBodies;
    public int TotalRelaunchedBodiesLastStep =>
        InteractiveRelaunchedBodiesLastStep + SparseRecycledBodiesLastStep;
}
