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
    private const double FixedStepBudgetMilliseconds = 1_000d / 30d;

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
    [Explicit("Server scale gate: production Physics3DWorld advances 10,000 awake dense-contact bodies within one 30Hz step.")]
    public void TenThousandAwakeBodies_DenseContactStep_MeetsProductionThirtyHzBudget()
    {
        const int bodyCount = 10_000;
        const int sampleCount = 120;
        Physics3DWorldConfig config = CreateProductionDenseContactConfig(bodyCount);
        Assert.That(config.FixedStepHz, Is.EqualTo(30));
        Assert.That(config.MaximumPhysicsStepsPerSourceTick, Is.EqualTo(1));
        Assert.That(config.WorkerCount, Is.EqualTo(8));

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
        Assert.That(world.WorkerCount, Is.EqualTo(8));

        var stepDurationsMs = new double[sampleCount];
        var stageTotalsMs = new double[8];
        var stageCallingAlloc = new long[8];
        var stageWorkerAlloc = new long[8];
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int i = 0; i < 256; i++)
        {
            _ = GC.GetAllocatedBytesForCurrentThread();
        }

        long processBefore = GC.GetTotalAllocatedBytes(precise: true);
        long metricsCallingAllocated = 0;
        long metricsWorkerAllocated = 0;
        int allocatingStepCount = 0;
        int firstAllocatingStep = -1;
        long maximumStepAllocation = 0;
        int minimumAwakeBodyCount = int.MaxValue;
        int peakContactPairCount = 0;
        int missingKernelBreakdownCount = 0;
        long timestamp = Stopwatch.GetTimestamp();
        for (int i = 0; i < sampleCount; i++)
        {
            world.Step();
            Physics3DStepMetrics metrics = world.LastStepMetrics;
            if (!metrics.HasKernelStageBreakdown)
            {
                missingKernelBreakdownCount++;
            }

            stepDurationsMs[i] = metrics.Total.ElapsedMilliseconds;
            AccumulateStage(0, metrics.CommandReplay, stageTotalsMs, stageCallingAlloc, stageWorkerAlloc);
            AccumulateStage(1, metrics.Sleep, stageTotalsMs, stageCallingAlloc, stageWorkerAlloc);
            AccumulateStage(2, metrics.PredictBounds, stageTotalsMs, stageCallingAlloc, stageWorkerAlloc);
            AccumulateStage(3, metrics.CollisionDetection, stageTotalsMs, stageCallingAlloc, stageWorkerAlloc);
            AccumulateStage(4, metrics.ContactSurface, stageTotalsMs, stageCallingAlloc, stageWorkerAlloc);
            AccumulateStage(5, metrics.Solve, stageTotalsMs, stageCallingAlloc, stageWorkerAlloc);
            AccumulateStage(6, metrics.Optimize, stageTotalsMs, stageCallingAlloc, stageWorkerAlloc);
            AccumulateStage(7, metrics.ContactFinalize, stageTotalsMs, stageCallingAlloc, stageWorkerAlloc);
            metricsCallingAllocated += metrics.Total.CallingThreadAllocatedBytes;
            metricsWorkerAllocated += metrics.Total.BackgroundWorkerAllocatedBytes;
            minimumAwakeBodyCount = Math.Min(minimumAwakeBodyCount, world.AwakeBodyCount);
            peakContactPairCount = Math.Max(peakContactPairCount, world.ContactPairCount);
            long stepAllocation =
                metrics.Total.CallingThreadAllocatedBytes + metrics.Total.BackgroundWorkerAllocatedBytes;
            if (stepAllocation > 0)
            {
                allocatingStepCount++;
                firstAllocatingStep = firstAllocatingStep < 0 ? i : firstAllocatingStep;
                maximumStepAllocation = Math.Max(maximumStepAllocation, stepAllocation);
            }
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(timestamp);
        long processAllocated = GC.GetTotalAllocatedBytes(precise: true) - processBefore;
        Array.Sort(stepDurationsMs);
        double p50 = Percentile(stepDurationsMs, 0.50);
        double p95 = Percentile(stepDurationsMs, 0.95);
        double p99 = Percentile(stepDurationsMs, 0.99);
        double p999 = Percentile(stepDurationsMs, 0.999);
        double invSample = 1d / sampleCount;
        string dominantStage = FindDominantStage(stageTotalsMs);
        TestContext.Out.WriteLine(
            $"120 production dense-contact steps: {elapsed.TotalMilliseconds:F2} ms; " +
            $"step ms [P50={p50:F3}, P95={p95:F3}, P99={p99:F3}, P99.9={p999:F3}]; " +
            $"budget={FixedStepBudgetMilliseconds:F3} ms; " +
            $"minimum awake={minimumAwakeBodyCount}; peak contacts={peakContactPairCount}; " +
            $"dominant stage={dominantStage}; " +
            $"stage avg ms [cmd={stageTotalsMs[0] * invSample:F3}, sleep={stageTotalsMs[1] * invSample:F3}, " +
            $"predict={stageTotalsMs[2] * invSample:F3}, collision={stageTotalsMs[3] * invSample:F3}, " +
            $"surface={stageTotalsMs[4] * invSample:F3}, solve={stageTotalsMs[5] * invSample:F3}, " +
            $"optimize={stageTotalsMs[6] * invSample:F3}, finalize={stageTotalsMs[7] * invSample:F3}]; " +
            $"stage alloc bytes [cmd={stageCallingAlloc[0]}/{stageWorkerAlloc[0]}, " +
            $"sleep={stageCallingAlloc[1]}/{stageWorkerAlloc[1]}, " +
            $"predict={stageCallingAlloc[2]}/{stageWorkerAlloc[2]}, " +
            $"collision={stageCallingAlloc[3]}/{stageWorkerAlloc[3]}, " +
            $"surface={stageCallingAlloc[4]}/{stageWorkerAlloc[4]}, " +
            $"solve={stageCallingAlloc[5]}/{stageWorkerAlloc[5]}, " +
            $"optimize={stageCallingAlloc[6]}/{stageWorkerAlloc[6]}, " +
            $"finalize={stageCallingAlloc[7]}/{stageWorkerAlloc[7]}]; " +
            $"metrics alloc total calling={metricsCallingAllocated} worker={metricsWorkerAllocated}; " +
            $"allocating steps={allocatingStepCount} (first {firstAllocatingStep}, max {maximumStepAllocation}); " +
            $"process allocated during sample window: {processAllocated} bytes");

        Assert.That(missingKernelBreakdownCount, Is.Zero, "Production Physics3DWorld must publish kernel stage metrics.");
        Assert.That(metricsCallingAllocated, Is.Zero, "Physics3D main thread allocated managed memory.");
        Assert.That(metricsWorkerAllocated, Is.Zero, "Physics3D background workers allocated managed memory.");
        Assert.That(world.ActiveMobileBodyCount, Is.EqualTo(bodyCount));
        Assert.That(minimumAwakeBodyCount, Is.EqualTo(bodyCount));
        Assert.That(peakContactPairCount, Is.GreaterThan(0), "Dense-contact gate must exercise simultaneous contacts.");
        Assert.That(p95, Is.LessThan(FixedStepBudgetMilliseconds), $"P95 {p95:F3} ms exceeds one 30Hz physics step.");
        Assert.That(p99, Is.LessThan(FixedStepBudgetMilliseconds), $"P99 {p99:F3} ms exceeds one 30Hz physics step.");
    }

    private static Physics3DWorldConfig CreateProductionDenseContactConfig(int bodyCount)
    {
        Physics3DWorldConfig baseline = Physics3DWorldTests.CreateConfig(
            mobileCapacity: bodyCount,
            staticCapacity: 1,
            shapeCapacity: 2,
            workerCount: 8,
            minimumTimestepCountUnderSleepThreshold: byte.MaxValue);
        return new Physics3DWorldConfig
        {
            MobileBodyCapacity = baseline.MobileBodyCapacity,
            StaticBodyCapacity = baseline.StaticBodyCapacity,
            ShapeCapacity = baseline.ShapeCapacity,
            InactiveIslandCapacity = baseline.InactiveIslandCapacity,
            ConstraintCapacity = baseline.ConstraintCapacity,
            ConstraintsPerTypeBatchCapacity = baseline.ConstraintsPerTypeBatchCapacity,
            ConstraintCountPerBodyEstimate = baseline.ConstraintCountPerBodyEstimate,
            ContactPairCapacityPerWorker = 65_536,
            ActuationCommandCapacity = baseline.ActuationCommandCapacity,
            WorkerCount = 8,
            FixedStepHz = 30,
            MaximumPhysicsStepsPerSourceTick = 1,
            SolverSubstepCount = baseline.SolverSubstepCount,
            SolverVelocityIterationCount = baseline.SolverVelocityIterationCount,
            GravityCmPerSecondSquared = baseline.GravityCmPerSecondSquared,
            LinearDamping = baseline.LinearDamping,
            AngularDamping = baseline.AngularDamping,
            MaximumSpeculativeMarginCm = baseline.MaximumSpeculativeMarginCm,
            SleepThreshold = baseline.SleepThreshold,
            MinimumTimestepCountUnderSleepThreshold = baseline.MinimumTimestepCountUnderSleepThreshold,
            ContinuousMinimumSweepTimestep = baseline.ContinuousMinimumSweepTimestep,
            ContinuousSweepConvergenceThreshold = baseline.ContinuousSweepConvergenceThreshold,
            MaterialCombineMode = baseline.MaterialCombineMode
        };
    }

    private static void AccumulateStage(
        int index,
        in Physics3DStageMetrics stage,
        double[] totalsMs,
        long[] callingAlloc,
        long[] workerAlloc)
    {
        totalsMs[index] += stage.ElapsedMilliseconds;
        callingAlloc[index] += stage.CallingThreadAllocatedBytes;
        workerAlloc[index] += stage.BackgroundWorkerAllocatedBytes;
    }

    private static string FindDominantStage(ReadOnlySpan<double> stageTotalsMs)
    {
        ReadOnlySpan<string> names =
        [
            "commandReplay",
            "sleep",
            "predictBounds",
            "collisionDetection",
            "contactSurface",
            "solve",
            "optimize",
            "contactFinalize"
        ];
        int dominant = 0;
        for (int i = 1; i < stageTotalsMs.Length; i++)
        {
            if (stageTotalsMs[i] > stageTotalsMs[dominant])
            {
                dominant = i;
            }
        }

        return names[dominant];
    }

    private static double Percentile(ReadOnlySpan<double> sortedValues, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }
}
