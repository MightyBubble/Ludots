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

    [TestCase(4, false)]
    [TestCase(8, true)]
    [Explicit("Server scale matrix: advances 10,000 simultaneously awake bodies with dense local contacts.")]
    public void TenThousandAwakeBodies_DenseContactStep_IsZeroGcAfterWarmup(
        int workerCount,
        bool enforceThirtyHzBudget)
    {
        const int bodyCount = 10_000;
        Physics3DWorldConfig config = Physics3DWorldTests.CreateConfig(
            mobileCapacity: bodyCount,
            staticCapacity: 1,
            shapeCapacity: 2,
            workerCount: workerCount,
            minimumTimestepCountUnderSleepThreshold: byte.MaxValue,
            fixedStepHz: 30);
        using var world = new Physics3DWorld(config);
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
        const int sampleCount = 120;
        var stepDurations = new long[sampleCount];
        var totalStage = new StageSamples("total", sampleCount);
        var commandReplayStage = new StageSamples("command replay", sampleCount);
        var sleepStage = new StageSamples("sleep", sampleCount);
        var predictBoundsStage = new StageSamples("predict bounds", sampleCount);
        var collisionDetectionStage = new StageSamples("collision detection", sampleCount);
        var contactSurfaceStage = new StageSamples("contact surface", sampleCount);
        var solveStage = new StageSamples("solve", sampleCount);
        var optimizeStage = new StageSamples("optimize", sampleCount);
        var contactFinalizeStage = new StageSamples("contact finalize", sampleCount);
        StageSamples[] stages =
        [
            totalStage,
            commandReplayStage,
            sleepStage,
            predictBoundsStage,
            collisionDetectionStage,
            contactSurfaceStage,
            solveStage,
            optimizeStage,
            contactFinalizeStage
        ];
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int i = 0; i < 256; i++)
        {
            _ = GC.GetAllocatedBytesForCurrentThread();
        }

        long processBefore = GC.GetTotalAllocatedBytes(precise: true);
        long callingThreadBefore = GC.GetAllocatedBytesForCurrentThread();
        long timestamp = Stopwatch.GetTimestamp();
        int allocatingStepCount = 0;
        int firstAllocatingStep = -1;
        long maximumStepAllocation = 0;
        int minimumAwakeBodyCount = int.MaxValue;
        int peakContactPairCount = 0;
        bool hasKernelStageBreakdown = true;
        for (int i = 0; i < sampleCount; i++)
        {
            long stepTimestamp = Stopwatch.GetTimestamp();
            long stepBefore = GC.GetAllocatedBytesForCurrentThread();
            world.Step();
            long stepAllocation = GC.GetAllocatedBytesForCurrentThread() - stepBefore;
            stepDurations[i] = Stopwatch.GetTimestamp() - stepTimestamp;
            Physics3DStepMetrics metrics = world.LastStepMetrics;
            hasKernelStageBreakdown &= metrics.HasKernelStageBreakdown;
            totalStage.Record(i, metrics.Total);
            commandReplayStage.Record(i, metrics.CommandReplay);
            sleepStage.Record(i, metrics.Sleep);
            predictBoundsStage.Record(i, metrics.PredictBounds);
            collisionDetectionStage.Record(i, metrics.CollisionDetection);
            contactSurfaceStage.Record(i, metrics.ContactSurface);
            solveStage.Record(i, metrics.Solve);
            optimizeStage.Record(i, metrics.Optimize);
            contactFinalizeStage.Record(i, metrics.ContactFinalize);
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
            $"test calling thread: {callingThreadAllocated} bytes across {allocatingStepCount} steps " +
            $"(first {firstAllocatingStep}, max {maximumStepAllocation}); " +
            $"production metrics allocations [calling={totalStage.CallingThreadAllocatedBytes}, " +
            $"workers={totalStage.BackgroundWorkerAllocatedBytes}]; " +
            $"unattributed test-host allocations: " +
            $"{processAllocated - callingThreadAllocated - totalStage.BackgroundWorkerAllocatedBytes} bytes");
        TestContext.Out.WriteLine("Production stage metrics:");
        foreach (StageSamples stage in stages)
        {
            stage.WriteReport();
        }

        Assert.That(hasKernelStageBreakdown, Is.True, "Production kernel stage metrics were unavailable.");
        Assert.That(callingThreadAllocated, Is.Zero, "Physics3D calling thread allocated managed memory.");
        Assert.That(totalStage.CallingThreadAllocatedBytes, Is.Zero, "Physics3D production metrics reported calling-thread allocations.");
        Assert.That(totalStage.BackgroundWorkerAllocatedBytes, Is.Zero, "Physics3D background workers allocated managed memory.");
        Assert.That(world.ActiveMobileBodyCount, Is.EqualTo(bodyCount));
        Assert.That(minimumAwakeBodyCount, Is.EqualTo(bodyCount));
        if (enforceThirtyHzBudget)
        {
            double fixedStepBudgetMilliseconds = config.FixedDeltaSeconds * 1_000d;
            Assert.Multiple(() =>
            {
                Assert.That(p95, Is.LessThan(fixedStepBudgetMilliseconds), "Dense-contact P95 exceeded the 30Hz budget.");
                Assert.That(p99, Is.LessThan(fixedStepBudgetMilliseconds), "Dense-contact P99 exceeded the 30Hz budget.");
            });
        }
    }

    private static double Percentile(ReadOnlySpan<long> sortedValues, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }

    private static double Percentile(ReadOnlySpan<double> sortedValues, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }

    private sealed class StageSamples
    {
        private readonly string _name;
        private readonly double[] _elapsedMilliseconds;
        private readonly long[] _backgroundWorkerDispatchElapsedTimestampTicks;

        public StageSamples(string name, int sampleCount)
        {
            _name = name;
            _elapsedMilliseconds = new double[sampleCount];
            _backgroundWorkerDispatchElapsedTimestampTicks = new long[sampleCount];
        }

        public long CallingThreadAllocatedBytes { get; private set; }
        public long BackgroundWorkerAllocatedBytes { get; private set; }

        public void Record(int index, Physics3DStageMetrics metrics)
        {
            _elapsedMilliseconds[index] = metrics.ElapsedMilliseconds;
            _backgroundWorkerDispatchElapsedTimestampTicks[index] = metrics.BackgroundWorkerDispatchElapsedTimestampTicks;
            CallingThreadAllocatedBytes += metrics.CallingThreadAllocatedBytes;
            BackgroundWorkerAllocatedBytes += metrics.BackgroundWorkerAllocatedBytes;
        }

        public void WriteReport()
        {
            _elapsedMilliseconds.AsSpan().Sort();
            _backgroundWorkerDispatchElapsedTimestampTicks.AsSpan().Sort();
            double millisecondsPerTimestamp = 1_000d / Stopwatch.Frequency;
            TestContext.Out.WriteLine(
                $"  {_name}: wall ms [P50={Percentile(_elapsedMilliseconds, 0.50):F3}, " +
                $"P95={Percentile(_elapsedMilliseconds, 0.95):F3}, " +
                $"P99={Percentile(_elapsedMilliseconds, 0.99):F3}]; " +
                $"summed background dispatch wall ms [P50={Percentile(_backgroundWorkerDispatchElapsedTimestampTicks, 0.50) * millisecondsPerTimestamp:F3}, " +
                $"P95={Percentile(_backgroundWorkerDispatchElapsedTimestampTicks, 0.95) * millisecondsPerTimestamp:F3}, " +
                $"P99={Percentile(_backgroundWorkerDispatchElapsedTimestampTicks, 0.99) * millisecondsPerTimestamp:F3}]; " +
                $"allocations [calling={CallingThreadAllocatedBytes}, workers={BackgroundWorkerAllocatedBytes}]");
        }
    }
}
