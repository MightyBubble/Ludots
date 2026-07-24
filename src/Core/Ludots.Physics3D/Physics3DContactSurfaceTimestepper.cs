using System;
using System.Diagnostics;
using System.Numerics;
using BepuPhysics;
using BepuUtilities;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DContactSurfaceTimestepper : ITimestepper
{
    private readonly Physics3DBodyStore _bodies;
    private readonly int[] _surfaceSlots;
    private readonly Vector3[] _surfaceVelocitiesWorld;

    public Physics3DContactSurfaceTimestepper(Physics3DBodyStore bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        _bodies = bodies;
        _surfaceSlots = new int[bodies.TotalCapacity];
        _surfaceVelocitiesWorld = new Vector3[bodies.TotalCapacity];
    }

    public event TimestepperStageHandler? BeforeCollisionDetection;
    public event TimestepperStageHandler? CollisionsDetected;

    public Physics3DKernelStepMetrics LastStepMetrics { get; private set; }

    public void Timestep(Simulation simulation, float dt, IThreadDispatcher? threadDispatcher = null)
    {
        Physics3DThreadDispatcher? metricsDispatcher = threadDispatcher as Physics3DThreadDispatcher;
        StageMeasurement measurement = BeginStage(metricsDispatcher);
        simulation.Sleep(threadDispatcher);
        Physics3DStageMetrics sleep = EndStage(metricsDispatcher, in measurement);

        measurement = BeginStage(metricsDispatcher);
        simulation.PredictBoundingBoxes(dt, threadDispatcher);
        BeforeCollisionDetection?.Invoke(dt, threadDispatcher!);
        Physics3DStageMetrics predictBounds = EndStage(metricsDispatcher, in measurement);

        measurement = BeginStage(metricsDispatcher);
        simulation.CollisionDetection(dt, threadDispatcher);
        CollisionsDetected?.Invoke(dt, threadDispatcher!);
        Physics3DStageMetrics collisionDetection = EndStage(metricsDispatcher, in measurement);

        measurement = BeginStage(metricsDispatcher);
        int surfaceCount = ApplySurfaceVelocities(simulation);
        Physics3DStageMetrics contactSurfaceBeforeSolve = EndStage(metricsDispatcher, in measurement);

        measurement = BeginStage(metricsDispatcher);
        simulation.Solve(dt, threadDispatcher);
        Physics3DStageMetrics solve = EndStage(metricsDispatcher, in measurement);

        measurement = BeginStage(metricsDispatcher);
        RestoreSurfaceVelocitiesAndGeometry(simulation, dt, surfaceCount);
        Physics3DStageMetrics contactSurfaceAfterSolve = EndStage(metricsDispatcher, in measurement);

        measurement = BeginStage(metricsDispatcher);
        simulation.IncrementallyOptimizeDataStructures(threadDispatcher);
        Physics3DStageMetrics optimize = EndStage(metricsDispatcher, in measurement);

        Physics3DStageMetrics contactSurface = Add(
            in contactSurfaceBeforeSolve,
            in contactSurfaceAfterSolve);
        LastStepMetrics = new Physics3DKernelStepMetrics(
            sleep,
            predictBounds,
            collisionDetection,
            contactSurface,
            solve,
            optimize);
    }

    private static StageMeasurement BeginStage(Physics3DThreadDispatcher? dispatcher)
        => new(
            Stopwatch.GetTimestamp(),
            GC.GetAllocatedBytesForCurrentThread(),
            dispatcher?.BackgroundWorkerAllocatedBytesCurrentStep ?? 0,
            dispatcher?.BackgroundWorkerCpuTimestampTicksCurrentStep ?? 0);

    private static Physics3DStageMetrics EndStage(
        Physics3DThreadDispatcher? dispatcher,
        in StageMeasurement measurement)
    {
        return new Physics3DStageMetrics(
            Stopwatch.GetElapsedTime(measurement.Timestamp).TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - measurement.CallingThreadAllocatedBytes,
            (dispatcher?.BackgroundWorkerAllocatedBytesCurrentStep ?? 0) - measurement.BackgroundWorkerAllocatedBytes,
            (dispatcher?.BackgroundWorkerCpuTimestampTicksCurrentStep ?? 0) - measurement.BackgroundWorkerCpuTimestampTicks);
    }

    private static Physics3DStageMetrics Add(
        in Physics3DStageMetrics left,
        in Physics3DStageMetrics right)
        => new(
            left.ElapsedMilliseconds + right.ElapsedMilliseconds,
            left.CallingThreadAllocatedBytes + right.CallingThreadAllocatedBytes,
            left.BackgroundWorkerAllocatedBytes + right.BackgroundWorkerAllocatedBytes,
            left.BackgroundWorkerCpuTimestampTicks + right.BackgroundWorkerCpuTimestampTicks);

    private readonly struct StageMeasurement
    {
        public StageMeasurement(
            long timestamp,
            long callingThreadAllocatedBytes,
            long backgroundWorkerAllocatedBytes,
            long backgroundWorkerCpuTimestampTicks)
        {
            Timestamp = timestamp;
            CallingThreadAllocatedBytes = callingThreadAllocatedBytes;
            BackgroundWorkerAllocatedBytes = backgroundWorkerAllocatedBytes;
            BackgroundWorkerCpuTimestampTicks = backgroundWorkerCpuTimestampTicks;
        }

        public long Timestamp { get; }
        public long CallingThreadAllocatedBytes { get; }
        public long BackgroundWorkerAllocatedBytes { get; }
        public long BackgroundWorkerCpuTimestampTicks { get; }
    }

    private int ApplySurfaceVelocities(Simulation simulation)
    {
        int count = 0;
        for (int slot = 0; slot < _bodies.TotalCapacity; slot++)
        {
            if (!_bodies.IsActiveSlot(slot) ||
                _bodies.GetBodyKind(slot) != Physics3DBodyKind.Kinematic)
            {
                continue;
            }

            ref readonly Physics3DBodyContactPolicy policy = ref _bodies.GetContactPolicy(slot);
            if (policy.Kind != Physics3DBodyContactPolicyKind.SurfaceVelocity)
            {
                continue;
            }

            BodyReference body = simulation.Bodies.GetBodyReference(new BodyHandle(_bodies.GetBepuHandle(slot)));
            Vector3 surfaceVelocityWorld = Vector3.Transform(
                policy.LocalSurfaceVelocityCmPerSecond,
                body.Pose.Orientation);
            BodyVelocity velocity = body.Velocity;
            velocity.Linear += surfaceVelocityWorld;
            body.Velocity = velocity;
            _surfaceSlots[count] = slot;
            _surfaceVelocitiesWorld[count] = surfaceVelocityWorld;
            count++;
        }

        return count;
    }

    private void RestoreSurfaceVelocitiesAndGeometry(Simulation simulation, float dt, int surfaceCount)
    {
        for (int index = 0; index < surfaceCount; index++)
        {
            int slot = _surfaceSlots[index];
            Vector3 surfaceVelocityWorld = _surfaceVelocitiesWorld[index];
            BodyReference body = simulation.Bodies.GetBodyReference(new BodyHandle(_bodies.GetBepuHandle(slot)));
            BodyVelocity velocity = body.Velocity;
            velocity.Linear -= surfaceVelocityWorld;
            body.Velocity = velocity;

            RigidPose pose = body.Pose;
            pose.Position -= surfaceVelocityWorld * dt;
            body.Pose = pose;
        }
    }
}
