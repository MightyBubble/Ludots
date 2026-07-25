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
    int PulseCount,
    int PulsedForegroundBodiesLastPulse,
    int PhysicsPerformanceSampleCount,
    int FramePerformanceSampleCount,
    int PerformanceWindowCapacity,
    double PhysicsStepP50Milliseconds,
    double PhysicsStepP95Milliseconds,
    double PhysicsStepP99Milliseconds,
    double FullFrameP50Milliseconds,
    double FullFrameP95Milliseconds,
    double FullFrameP99Milliseconds,
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
        PulseCount: 0,
        PulsedForegroundBodiesLastPulse: 0,
        PhysicsPerformanceSampleCount: 0,
        FramePerformanceSampleCount: 0,
        PerformanceWindowCapacity: 0,
        PhysicsStepP50Milliseconds: 0d,
        PhysicsStepP95Milliseconds: 0d,
        PhysicsStepP99Milliseconds: 0d,
        FullFrameP50Milliseconds: 0d,
        FullFrameP95Milliseconds: 0d,
        FullFrameP99Milliseconds: 0d,
        PerformanceBudgetMilliseconds: 0d,
        PerformanceStatus: Physics3DScaleCityPerformanceStatus.Warming);

    public int TotalBodies => InteractiveBodies + SparseBodies;
    public int TotalRelaunchedBodiesLastStep =>
        InteractiveRelaunchedBodiesLastStep + SparseRecycledBodiesLastStep;
}
