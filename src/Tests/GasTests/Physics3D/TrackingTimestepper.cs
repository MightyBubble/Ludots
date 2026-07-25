using BepuPhysics;
using BepuUtilities;

namespace Ludots.Tests.Physics3D;

internal sealed class TrackingTimestepper : ITimestepper
{
    public event TimestepperStageHandler? BeforeCollisionDetection;
    public event TimestepperStageHandler? CollisionsDetected;

    public long SleepAllocatedBytes { get; private set; }
    public long PredictBoundsAllocatedBytes { get; private set; }
    public long CollisionDetectionAllocatedBytes { get; private set; }
    public long SolveAllocatedBytes { get; private set; }
    public long OptimizationAllocatedBytes { get; private set; }

    public void Timestep(Simulation simulation, float dt, IThreadDispatcher? threadDispatcher = null)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        simulation.Sleep(threadDispatcher);
        SleepAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        simulation.PredictBoundingBoxes(dt, threadDispatcher);
        PredictBoundsAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - before;
        BeforeCollisionDetection?.Invoke(dt, threadDispatcher!);

        before = GC.GetAllocatedBytesForCurrentThread();
        simulation.CollisionDetection(dt, threadDispatcher);
        CollisionDetectionAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - before;
        CollisionsDetected?.Invoke(dt, threadDispatcher!);

        before = GC.GetAllocatedBytesForCurrentThread();
        simulation.Solve(dt, threadDispatcher);
        SolveAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        simulation.IncrementallyOptimizeDataStructures(threadDispatcher);
        OptimizationAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
