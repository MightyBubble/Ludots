using System;
using System.Diagnostics;
using System.Numerics;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DScaleTests
{
    [Test]
    [Explicit("Server scale gate: allocates 50,000 registered bodies and keeps 2,000 awake.")]
    public void FiftyThousandRegisteredBodies_TwoThousandAwake_StayWithinFixedBuffers()
    {
        const int registeredBodyCount = 50_000;
        const int awakeBodyCount = 2_000;
        using var world = new Physics3DWorld(Physics3DWorldTests.CreateConfig(
            mobileCapacity: registeredBodyCount,
            staticCapacity: 0,
            shapeCapacity: 1,
            workerCount: Math.Min(8, Environment.ProcessorCount)));
        Physics3DShapeId shape = world.RegisterSphereShape(5f);
        var ids = new Physics3DBodyId[registeredBodyCount];
        for (int i = 0; i < registeredBodyCount; i++)
        {
            ids[i] = world.CreateBody(Physics3DWorldTests.CreateBody(
                Physics3DBodyKind.Dynamic,
                shape,
                new Vector3((i % 250) * 50f, 20_000f + (i / 250) * 50f, 0f)));
        }

        for (int i = awakeBodyCount; i < ids.Length; i++)
        {
            Physics3DBodyState state = world.GetBodyState(ids[i]);
            state.Awake = false;
            world.SetBodyState(ids[i], state);
        }

        var awake = new Physics3DAwakeBodyBuffer(awakeBodyCount);
        for (int i = 0; i < 30; i++)
        {
            world.Step();
        }

        world.CopyAwakeBodies(awake);
        Assert.That(world.ActiveBodyCount, Is.EqualTo(registeredBodyCount));
        Assert.That(awake.Count, Is.LessThanOrEqualTo(awakeBodyCount));
        Assert.That(world.RegisteredShapeCount, Is.EqualTo(1));
    }

    [Test]
    [Explicit("Server scale gate: advances 10,000 simultaneously awake bodies with dense local contacts.")]
    public void TenThousandAwakeBodies_DenseContactStep_IsZeroGcAfterWarmup()
    {
        const int bodyCount = 10_000;
        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            mobileCapacity: bodyCount,
            staticCapacity: 1,
            shapeCapacity: 2,
            workerCount: Math.Min(8, Environment.ProcessorCount),
            minimumTimestepCountUnderSleepThreshold: byte.MaxValue);
        var dispatcher = new TrackingThreadDispatcher(config.WorkerCount);
        var timestepper = new TrackingTimestepper();
        using var world = new Physics3DWorld(config, dispatcher, timestepper);
        Physics3DShapeId floor = world.RegisterBoxShape(new Vector3(20_000f, 20f, 20_000f));
        Physics3DShapeId box = world.RegisterBoxShape(new Vector3(20f));
        world.CreateBody(Physics3DWorldTests.CreateBody(Physics3DBodyKind.Static, floor, new Vector3(0f, -10f, 0f)));
        var ids = new Physics3DBodyId[bodyCount];
        for (int i = 0; i < bodyCount; i++)
        {
            int x = i % 100;
            int z = (i / 100) % 10;
            int y = i / 1_000;
            ids[i] = world.CreateBody(Physics3DWorldTests.CreateBody(
                Physics3DBodyKind.Dynamic,
                box,
                new Vector3((x - 50) * 20.5f, 10f + y * 20.5f, (z - 5) * 20.5f)));
        }

        for (int i = 0; i < 720; i++)
        {
            world.Step();
        }

        for (int i = 0; i < ids.Length; i++)
        {
            world.SetBodyAwake(ids[i], true);
        }

        Assert.That(world.AwakeBodyCount, Is.EqualTo(bodyCount));
        dispatcher.DispatchWorkers(static _ => { }, 0);
        dispatcher.DispatchWorkers(static _ => { }, 1);
        dispatcher.DispatchWorkers(static _ => { });
        var stepDurations = new long[120];
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int i = 0; i < 256; i++)
        {
            _ = GC.GetAllocatedBytesForCurrentThread();
        }

        long processBefore = GC.GetTotalAllocatedBytes(precise: true);
        long callingThreadBefore = GC.GetAllocatedBytesForCurrentThread();
        long backgroundWorkersBefore = dispatcher.BackgroundWorkerAllocatedBytes;
        long sleepBefore = timestepper.SleepAllocatedBytes;
        long predictBoundsBefore = timestepper.PredictBoundsAllocatedBytes;
        long collisionDetectionBefore = timestepper.CollisionDetectionAllocatedBytes;
        long solveBefore = timestepper.SolveAllocatedBytes;
        long optimizationBefore = timestepper.OptimizationAllocatedBytes;
        long timestamp = Stopwatch.GetTimestamp();
        int allocatingStepCount = 0;
        int firstAllocatingStep = -1;
        long maximumStepAllocation = 0;
        int minimumAwakeBodyCount = int.MaxValue;
        int peakContactPairCount = 0;
        for (int i = 0; i < 120; i++)
        {
            long stepTimestamp = Stopwatch.GetTimestamp();
            long stepBefore = GC.GetAllocatedBytesForCurrentThread();
            world.Step();
            long stepAllocation = GC.GetAllocatedBytesForCurrentThread() - stepBefore;
            stepDurations[i] = Stopwatch.GetTimestamp() - stepTimestamp;
            minimumAwakeBodyCount = Math.Min(minimumAwakeBodyCount, world.AwakeBodyCount);
            peakContactPairCount = Math.Max(peakContactPairCount, world.ContactPairCount);
            if (stepAllocation > 0)
            {
                allocatingStepCount++;
                firstAllocatingStep = firstAllocatingStep < 0 ? i : firstAllocatingStep;
                maximumStepAllocation = Math.Max(maximumStepAllocation, stepAllocation);
            }
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(timestamp);
        long callingThreadAllocated = GC.GetAllocatedBytesForCurrentThread() - callingThreadBefore;
        long backgroundWorkersAllocated = dispatcher.BackgroundWorkerAllocatedBytes - backgroundWorkersBefore;
        long processAllocated = GC.GetTotalAllocatedBytes(precise: true) - processBefore;
        stepDurations.AsSpan().Sort();
        double millisecondsPerTimestamp = 1_000d / Stopwatch.Frequency;
        double p50 = Percentile(stepDurations, 0.50) * millisecondsPerTimestamp;
        double p95 = Percentile(stepDurations, 0.95) * millisecondsPerTimestamp;
        double p99 = Percentile(stepDurations, 0.99) * millisecondsPerTimestamp;
        double p999 = Percentile(stepDurations, 0.999) * millisecondsPerTimestamp;
        TestContext.Out.WriteLine(
            $"120 dense-contact steps: {elapsed.TotalMilliseconds:F2} ms; " +
            $"step ms [P50={p50:F3}, P95={p95:F3}, P99={p99:F3}, P99.9={p999:F3}]; " +
            $"minimum awake={minimumAwakeBodyCount}; peak contacts={peakContactPairCount}; " +
            $"calling thread: {callingThreadAllocated} bytes across {allocatingStepCount} steps " +
            $"(first {firstAllocatingStep}, max {maximumStepAllocation}); " +
            $"background workers: {backgroundWorkersAllocated} bytes; " +
            $"stages [sleep={timestepper.SleepAllocatedBytes - sleepBefore}, " +
            $"predict={timestepper.PredictBoundsAllocatedBytes - predictBoundsBefore}, " +
            $"collision={timestepper.CollisionDetectionAllocatedBytes - collisionDetectionBefore}, " +
            $"solve={timestepper.SolveAllocatedBytes - solveBefore}, " +
            $"optimize={timestepper.OptimizationAllocatedBytes - optimizationBefore}]; " +
            $"unattributed test-host allocations: {processAllocated - callingThreadAllocated - backgroundWorkersAllocated} bytes");
        Assert.That(callingThreadAllocated, Is.Zero, "Physics3D calling thread allocated managed memory.");
        Assert.That(backgroundWorkersAllocated, Is.Zero, "Physics3D background workers allocated managed memory.");
        Assert.That(world.ActiveMobileBodyCount, Is.EqualTo(bodyCount));
        Assert.That(minimumAwakeBodyCount, Is.EqualTo(bodyCount));
    }

    private static double Percentile(ReadOnlySpan<long> sortedValues, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }
}
