namespace Ludots.Core.Physics3D;

public readonly struct Physics3DStageMetrics
{
    public Physics3DStageMetrics(
        double elapsedMilliseconds,
        long callingThreadAllocatedBytes,
        long backgroundWorkerAllocatedBytes,
        long backgroundWorkerCpuTimestampTicks)
    {
        ElapsedMilliseconds = elapsedMilliseconds;
        CallingThreadAllocatedBytes = callingThreadAllocatedBytes;
        BackgroundWorkerAllocatedBytes = backgroundWorkerAllocatedBytes;
        BackgroundWorkerCpuTimestampTicks = backgroundWorkerCpuTimestampTicks;
    }

    public double ElapsedMilliseconds { get; }
    public long CallingThreadAllocatedBytes { get; }
    public long BackgroundWorkerAllocatedBytes { get; }
    public long BackgroundWorkerCpuTimestampTicks { get; }
}

public readonly struct Physics3DStepMetrics
{
    public Physics3DStepMetrics(
        long stepIndex,
        bool hasKernelStageBreakdown,
        Physics3DStageMetrics total,
        Physics3DStageMetrics commandReplay,
        Physics3DStageMetrics sleep,
        Physics3DStageMetrics predictBounds,
        Physics3DStageMetrics collisionDetection,
        Physics3DStageMetrics contactSurface,
        Physics3DStageMetrics solve,
        Physics3DStageMetrics optimize,
        Physics3DStageMetrics contactFinalize)
    {
        StepIndex = stepIndex;
        HasKernelStageBreakdown = hasKernelStageBreakdown;
        Total = total;
        CommandReplay = commandReplay;
        Sleep = sleep;
        PredictBounds = predictBounds;
        CollisionDetection = collisionDetection;
        ContactSurface = contactSurface;
        Solve = solve;
        Optimize = optimize;
        ContactFinalize = contactFinalize;
    }

    public long StepIndex { get; }
    public bool HasKernelStageBreakdown { get; }
    public Physics3DStageMetrics Total { get; }
    public Physics3DStageMetrics CommandReplay { get; }
    public Physics3DStageMetrics Sleep { get; }
    public Physics3DStageMetrics PredictBounds { get; }
    public Physics3DStageMetrics CollisionDetection { get; }
    public Physics3DStageMetrics ContactSurface { get; }
    public Physics3DStageMetrics Solve { get; }
    public Physics3DStageMetrics Optimize { get; }
    public Physics3DStageMetrics ContactFinalize { get; }
}

internal readonly struct Physics3DKernelStepMetrics
{
    public Physics3DKernelStepMetrics(
        Physics3DStageMetrics sleep,
        Physics3DStageMetrics predictBounds,
        Physics3DStageMetrics collisionDetection,
        Physics3DStageMetrics contactSurface,
        Physics3DStageMetrics solve,
        Physics3DStageMetrics optimize)
    {
        Sleep = sleep;
        PredictBounds = predictBounds;
        CollisionDetection = collisionDetection;
        ContactSurface = contactSurface;
        Solve = solve;
        Optimize = optimize;
    }

    public Physics3DStageMetrics Sleep { get; }
    public Physics3DStageMetrics PredictBounds { get; }
    public Physics3DStageMetrics CollisionDetection { get; }
    public Physics3DStageMetrics ContactSurface { get; }
    public Physics3DStageMetrics Solve { get; }
    public Physics3DStageMetrics Optimize { get; }
}
