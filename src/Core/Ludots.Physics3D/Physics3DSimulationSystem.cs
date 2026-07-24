using System;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.TimeFlow;

namespace Ludots.Core.Physics3D;

public sealed class Physics3DSimulationSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription BodyQuery = new QueryDescription()
        .WithAll<Physics3DBodyCm, Physics3DPoseCm, PreviousPhysics3DPoseCm>();

    private readonly IPhysics3DWorld _physicsWorld;
    private readonly int _sourceFixedStepHz;
    private readonly DiscreteRateTickDistributor _tickDistributor;
    private int _requestedManualSteps;

    public Physics3DSimulationSystem(
        World world,
        IPhysics3DWorld physicsWorld,
        int sourceFixedStepHz,
        int maximumPhysicsStepsPerSourceTick)
        : base(world)
    {
        _physicsWorld = physicsWorld ?? throw new ArgumentNullException(nameof(physicsWorld));
        if (sourceFixedStepHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceFixedStepHz));
        }

        _sourceFixedStepHz = sourceFixedStepHz;
        int physicsFixedStepHz = FixedHzFromDeltaTime(physicsWorld.FixedDeltaSeconds);
        _tickDistributor = new DiscreteRateTickDistributor(
            sourceFixedStepHz,
            physicsFixedStepHz,
            maximumPhysicsStepsPerSourceTick);
    }

    public bool Enabled { get; set; } = true;
    public int PhysicsStepsLastUpdate { get; private set; }
    public long TotalPhysicsSteps { get; private set; }
    public double PhysicsUpdateMillisecondsLastUpdate { get; private set; }
    public double MaximumStepMillisecondsLastUpdate { get; private set; }
    public float InterpolationAlpha => _tickDistributor.InterpolationAlpha;

    public void RequestManualSteps(int stepCount = 1)
    {
        if (stepCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepCount));
        }

        _requestedManualSteps = checked(_requestedManualSteps + stepCount);
    }

    public override void Update(in float deltaTime)
    {
        int actualSourceHz = FixedHzFromDeltaTime(deltaTime);
        if (actualSourceHz != _sourceFixedStepHz)
        {
            throw new InvalidOperationException(
                $"Physics3D source fixed step changed from configured {_sourceFixedStepHz}Hz to {actualSourceHz}Hz.");
        }

        int stepCount;
        if (Enabled)
        {
            stepCount = _tickDistributor.NextStepCount();
            _requestedManualSteps = 0;
        }
        else
        {
            stepCount = _requestedManualSteps;
            _requestedManualSteps = 0;
        }

        PhysicsStepsLastUpdate = stepCount;
        PhysicsUpdateMillisecondsLastUpdate = 0d;
        MaximumStepMillisecondsLastUpdate = 0d;
        for (int step = 0; step < stepCount; step++)
        {
            PushKinematicBodies();
            long timestamp = Stopwatch.GetTimestamp();
            _physicsWorld.Step();
            double elapsedMilliseconds = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
            PhysicsUpdateMillisecondsLastUpdate += elapsedMilliseconds;
            MaximumStepMillisecondsLastUpdate = Math.Max(MaximumStepMillisecondsLastUpdate, elapsedMilliseconds);
            TotalPhysicsSteps++;
            PullDynamicBodies();
        }
    }

    private void PushKinematicBodies()
    {
        foreach (ref Chunk chunk in World.Query(in BodyQuery))
        {
            chunk.GetSpan<Physics3DBodyCm, Physics3DPoseCm>(out Span<Physics3DBodyCm> bodies, out Span<Physics3DPoseCm> poses);
            foreach (int index in chunk)
            {
                ref Physics3DBodyCm body = ref bodies[index];
                if (body.Kind != Physics3DBodyKind.Kinematic)
                {
                    continue;
                }

                RequireMatchingKind(in body);
                ref Physics3DPoseCm pose = ref poses[index];
                _physicsWorld.SetBodyState(body.Id, new Physics3DBodyState
                {
                    PositionCm = pose.Position,
                    Orientation = pose.Orientation,
                    LinearVelocityCmPerSecond = pose.LinearVelocity,
                    AngularVelocityRadiansPerSecond = pose.AngularVelocity,
                    Awake = true
                });
            }
        }
    }

    private void PullDynamicBodies()
    {
        foreach (ref Chunk chunk in World.Query(in BodyQuery))
        {
            chunk.GetSpan<Physics3DBodyCm, Physics3DPoseCm, PreviousPhysics3DPoseCm>(
                out Span<Physics3DBodyCm> bodies,
                out Span<Physics3DPoseCm> poses,
                out Span<PreviousPhysics3DPoseCm> previousPoses);
            foreach (int index in chunk)
            {
                ref Physics3DBodyCm body = ref bodies[index];
                if (body.Kind != Physics3DBodyKind.Dynamic)
                {
                    continue;
                }

                RequireMatchingKind(in body);
                ref Physics3DPoseCm pose = ref poses[index];
                previousPoses[index].Position = pose.Position;
                previousPoses[index].Orientation = pose.Orientation;
                Physics3DBodyState state = _physicsWorld.GetBodyState(body.Id);
                pose.Position = state.PositionCm;
                pose.Orientation = state.Orientation;
                pose.LinearVelocity = state.LinearVelocityCmPerSecond;
                pose.AngularVelocity = state.AngularVelocityRadiansPerSecond;
            }
        }
    }

    private void RequireMatchingKind(in Physics3DBodyCm body)
    {
        Physics3DBodyKind worldKind = _physicsWorld.GetBodyKind(body.Id);
        if (worldKind != body.Kind)
        {
            throw new InvalidOperationException(
                $"Physics3D ECS body kind '{body.Kind}' does not match world kind '{worldKind}' for '{body.Id}'.");
        }
    }

    private static int FixedHzFromDeltaTime(float deltaTime)
    {
        if (!(deltaTime > 0f) || !float.IsFinite(deltaTime))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }

        int hz = (int)MathF.Round(1f / deltaTime);
        if (hz <= 0 || MathF.Abs((1f / hz) - deltaTime) > 1e-5f)
        {
            throw new InvalidOperationException(
                $"Fixed delta time '{deltaTime}' is not representable as 1/integer Hz.");
        }

        return hz;
    }
}
