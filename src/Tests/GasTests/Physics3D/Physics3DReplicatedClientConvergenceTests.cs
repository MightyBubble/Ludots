using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet;
using Ludots.Core.Physics3DNet.Client;
using Ludots.Core.Physics3DNet.Input;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DReplicatedClientConvergenceTests
{
    private const ulong SessionEpoch = 17;

    [TestCase(false)]
    [TestCase(true)]
    public void OwnedAndRemoteBodies_CanSwapRolesInEitherPacketOrder(bool promoteFirst)
    {
        SessionSeatBinding seat = Seat();
        using var harness = Harness.Create(activeCapacity: 2, historyTicks: 8);
        BodyBinding owned = harness.CreateBody(new NetworkEntityHandle(10, 1), Physics3DBodyKind.Dynamic);
        BodyBinding remote = harness.CreateBody(new NetworkEntityHandle(11, 1), Physics3DBodyKind.Kinematic);
        harness.Apply(owned, locallyControlled: true, Physics3DNetLocalDrivenKind.Character, tick: 1);
        harness.Apply(remote, locallyControlled: false, default, tick: 1);

        ReplicationApplyContext context = Context(in seat, tick: 2);
        harness.Convergence.OnBatchValidationBeginning(in context);
        bool promoted;
        bool demoted;
        if (promoteFirst)
        {
            promoted = harness.Convergence.CanAccept(
                remote.Entity,
                remote.Handle,
                locallyControlled: true,
                Physics3DNetLocalDrivenKind.Vehicle);
            demoted = harness.Convergence.CanAccept(
                owned.Entity,
                owned.Handle,
                locallyControlled: false,
                default);
        }
        else
        {
            demoted = harness.Convergence.CanAccept(
                owned.Entity,
                owned.Handle,
                locallyControlled: false,
                default);
            promoted = harness.Convergence.CanAccept(
                remote.Entity,
                remote.Handle,
                locallyControlled: true,
                Physics3DNetLocalDrivenKind.Vehicle);
        }

        Assert.Multiple(() =>
        {
            Assert.That(promoted, Is.True);
            Assert.That(demoted, Is.True);
            Assert.That(harness.Convergence.CanCommitBatchValidation(), Is.True);
        });

        harness.Convergence.OnBatchCommitBeginning();
        if (promoteFirst)
        {
            harness.Apply(remote, locallyControlled: true, Physics3DNetLocalDrivenKind.Vehicle, tick: 2);
            harness.Apply(owned, locallyControlled: false, default, tick: 2);
        }
        else
        {
            harness.Apply(owned, locallyControlled: false, default, tick: 2);
            harness.Apply(remote, locallyControlled: true, Physics3DNetLocalDrivenKind.Vehicle, tick: 2);
        }

        harness.Convergence.OnBatchEnded(committed: true);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Convergence.LocalDrivenHandle, Is.EqualTo(remote.Handle));
            Assert.That(harness.Convergence.LocalDrivenKind, Is.EqualTo(Physics3DNetLocalDrivenKind.Vehicle));
            Assert.That(harness.Convergence.RemoteCount, Is.EqualTo(1));
            Assert.That(harness.Convergence.TrySampleRemote(owned.Handle, 2f, out _), Is.True);
        });
    }

    [Test]
    public void TwoOwnedBodiesInOneBatch_AreRejectedWithoutActivatingSessionOrTick()
    {
        SessionSeatBinding seat = Seat();
        using var harness = Harness.Create(activeCapacity: 2, historyTicks: 8);
        var first = new NetworkEntityHandle(10, 1);
        var second = new NetworkEntityHandle(11, 1);
        ReplicationApplyContext context = Context(in seat, tick: 1);

        harness.Convergence.OnBatchValidationBeginning(in context);
        Assert.That(
            harness.Convergence.CanAcceptCreate(
                in first,
                locallyControlled: true,
                Physics3DNetLocalDrivenKind.Character),
            Is.True);
        Assert.That(
            harness.Convergence.CanAcceptCreate(
                in second,
                locallyControlled: true,
                Physics3DNetLocalDrivenKind.Vehicle),
            Is.True);
        Assert.That(harness.Convergence.CanCommitBatchValidation(), Is.False);
        harness.Convergence.OnBatchEnded(committed: false);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Convergence.IsSessionActive, Is.False);
            Assert.That(harness.Convergence.LatestCommittedTick, Is.Zero);
            Assert.That(harness.Convergence.HasLocalDrivenBody, Is.False);
            Assert.That(harness.Convergence.RemoteCount, Is.Zero);
        });
    }

    [Test]
    public void RemoteCapacityFailure_DoesNotAdvanceCommittedTick()
    {
        using var harness = Harness.Create(activeCapacity: 1, historyTicks: 8);
        BodyBinding first = harness.CreateBody(new NetworkEntityHandle(1000, 1), Physics3DBodyKind.Kinematic);
        BodyBinding overflow = harness.CreateBody(new NetworkEntityHandle(1001, 1), Physics3DBodyKind.Kinematic);
        harness.Apply(first, locallyControlled: false, default, tick: 1);

        Assert.Throws<Physics3DNetCapacityExceededException>(
            () => harness.Apply(overflow, locallyControlled: false, default, tick: 2));
        Assert.Multiple(() =>
        {
            Assert.That(harness.Convergence.LatestCommittedTick, Is.EqualTo(1));
            Assert.That(harness.Convergence.RemoteCount, Is.EqualTo(1));
            Assert.That(harness.Convergence.TrySampleRemote(first.Handle, 1f, out _), Is.True);
            Assert.That(harness.Convergence.TrySampleRemote(overflow.Handle, 2f, out _), Is.False);
        });
    }

    [Test]
    public void FullInterestReplacement_UsesTwoTimesActivePlanningCapacity()
    {
        SessionSeatBinding seat = Seat();
        using var harness = Harness.Create(activeCapacity: 1, historyTicks: 8);
        BodyBinding leaving = harness.CreateBody(new NetworkEntityHandle(1000, 1), Physics3DBodyKind.Kinematic);
        BodyBinding entering = harness.CreateBody(new NetworkEntityHandle(2000, 1), Physics3DBodyKind.Kinematic);
        harness.Apply(leaving, locallyControlled: false, default, tick: 1);
        ReplicationApplyContext context = Context(in seat, tick: 2);

        harness.Convergence.OnBatchValidationBeginning(in context);
        Assert.That(harness.Convergence.CanRelease(leaving.Entity, leaving.Handle), Is.True);
        Assert.That(
            harness.Convergence.CanAcceptCreate(
                entering.Handle,
                locallyControlled: false,
                default),
            Is.True);
        Assert.That(harness.Convergence.CanCommitBatchValidation(), Is.True);

        harness.Convergence.OnBatchCommitBeginning();
        harness.Convergence.Release(leaving.Entity, leaving.Handle, in context);
        harness.Apply(entering, locallyControlled: false, default, tick: 2);
        harness.Convergence.OnBatchEnded(committed: true);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Convergence.RemoteCount, Is.EqualTo(1));
            Assert.That(harness.Convergence.TrySampleRemote(leaving.Handle, 2f, out _), Is.False);
            Assert.That(harness.Convergence.TrySampleRemote(entering.Handle, 2f, out _), Is.True);
        });
    }

    [Test]
    public void FullPredictionHistory_RejectsBeforeTheDriverMovesTheBody()
    {
        using var harness = Harness.Create(activeCapacity: 1, historyTicks: 4);
        BodyBinding owned = harness.CreateBody(new NetworkEntityHandle(0, 1), Physics3DBodyKind.Dynamic);
        harness.Apply(owned, locallyControlled: true, Physics3DNetLocalDrivenKind.Character, tick: 1);
        var payload = new byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        for (uint tick = 2; tick <= 5; tick++)
        {
            Assert.That(harness.Convergence.TrySample(tick, payload), Is.EqualTo(FixedInputPayloadSampleStatus.Sampled));
            Assert.That(harness.Convergence.TryCommit(tick, payload), Is.EqualTo(FixedInputPayloadCommitStatus.Committed));
        }

        Physics3DBodyState before = harness.Physics.GetBodyState(owned.Body);
        int stepsBefore = harness.Driver.StepCount;
        Assert.That(harness.Convergence.TrySample(6, payload), Is.EqualTo(FixedInputPayloadSampleStatus.Sampled));
        Assert.Throws<Physics3DNetCapacityExceededException>(() => harness.Convergence.TryCommit(6, payload));
        Physics3DBodyState after = harness.Physics.GetBodyState(owned.Body);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Driver.StepCount, Is.EqualTo(stepsBefore));
            Assert.That(after.PositionCm, Is.EqualTo(before.PositionCm));
            Assert.That(harness.Convergence.LocalHistory.Count, Is.EqualTo(4));
        });
    }

    [Test]
    public void RemotePresentation_StopsAtTheNewestAuthoritativePose()
    {
        using var harness = Harness.Create(activeCapacity: 1, historyTicks: 8);
        BodyBinding remote = harness.CreateBody(new NetworkEntityHandle(1, 1), Physics3DBodyKind.Kinematic);
        harness.Apply(remote, locallyControlled: false, default, tick: 10, positionX: 0f, velocityX: 500f);
        harness.Apply(remote, locallyControlled: false, default, tick: 12, positionX: 20f, velocityX: 500f);

        for (int frame = 0; frame < 60; frame++)
        {
            harness.Convergence.Update(1f / 30f);
        }

        Physics3DBodyState body = harness.Physics.GetBodyState(remote.Body);
        Physics3DPoseCm pose = harness.World.Get<Physics3DPoseCm>(remote.Entity);
        Assert.Multiple(() =>
        {
            Assert.That(body.PositionCm.X, Is.EqualTo(20f).Within(0.001f));
            Assert.That(body.LinearVelocityCmPerSecond, Is.EqualTo(Vector3.Zero));
            Assert.That(pose.Position.X, Is.EqualTo(20f).Within(0.001f));
            Assert.That(harness.Convergence.LastRenderTick, Is.EqualTo(12f).Within(0.001f));
        });
    }

    [Test]
    public void Dispose_ClearsInputPredictionAndRemoteInterpolation()
    {
        using var harness = Harness.Create(activeCapacity: 2, historyTicks: 8);
        BodyBinding owned = harness.CreateBody(new NetworkEntityHandle(0, 1), Physics3DBodyKind.Dynamic);
        BodyBinding remote = harness.CreateBody(new NetworkEntityHandle(4095, 1), Physics3DBodyKind.Kinematic);
        harness.Apply(owned, locallyControlled: true, Physics3DNetLocalDrivenKind.Character, tick: 1);
        harness.Apply(remote, locallyControlled: false, default, tick: 1);
        harness.Convergence.Dispose();

        var payload = new byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.Multiple(() =>
        {
            Assert.That(harness.Convergence.IsSessionActive, Is.False);
            Assert.That(harness.Convergence.TrySample(2, payload), Is.EqualTo(FixedInputPayloadSampleStatus.Failed));
            Assert.That(harness.Convergence.TryCommit(2, payload), Is.EqualTo(FixedInputPayloadCommitStatus.Failed));
            Assert.That(harness.Convergence.TrySampleRemote(remote.Handle, 1f, out _), Is.False);
        });
    }

    [Test]
    public void TeardownThenNewEpoch_RebindsTheSameConvergenceWithoutOldHistory()
    {
        SessionSeatBinding seat = Seat();
        using var harness = Harness.Create(activeCapacity: 1, historyTicks: 8);
        BodyBinding first = harness.CreateBody(new NetworkEntityHandle(0, 1), Physics3DBodyKind.Dynamic);
        harness.Apply(first, locallyControlled: true, Physics3DNetLocalDrivenKind.Character, tick: 1);
        var payload = new byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(harness.Convergence.TrySample(2, payload), Is.EqualTo(FixedInputPayloadSampleStatus.Sampled));
        Assert.That(harness.Convergence.TryCommit(2, payload), Is.EqualTo(FixedInputPayloadCommitStatus.Committed));
        harness.Convergence.Teardown(in seat, SessionEpoch);

        BodyBinding rebound = harness.CreateBody(new NetworkEntityHandle(0, 2), Physics3DBodyKind.Dynamic);
        var nextContext = new ReplicationApplyContext(
            in seat,
            SessionEpoch + 1,
            committedTick: 1,
            snapshotId: 1,
            ReplicationPacketKind.Full);
        harness.ApplyWithContext(
            rebound,
            locallyControlled: true,
            Physics3DNetLocalDrivenKind.Vehicle,
            in nextContext);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Convergence.SessionEpoch, Is.EqualTo(SessionEpoch + 1));
            Assert.That(harness.Convergence.LocalDrivenHandle, Is.EqualTo(rebound.Handle));
            Assert.That(harness.Convergence.LocalDrivenKind, Is.EqualTo(Physics3DNetLocalDrivenKind.Vehicle));
            Assert.That(harness.Convergence.LocalHistory.Count, Is.Zero);
            Assert.That(harness.Convergence.LocalHistory.ConfirmedTick, Is.EqualTo(1));
        });

        ReplicationApplyContext stale = Context(in seat, tick: 2);
        Assert.Throws<InvalidOperationException>(
            () => harness.ApplyWithContext(
                rebound,
                locallyControlled: true,
                Physics3DNetLocalDrivenKind.Vehicle,
                in stale));
        Assert.That(harness.Convergence.LatestCommittedTick, Is.EqualTo(1));
    }

    [Test]
    public void WarmedConvergencePath_HasZeroManagedAllocations()
    {
        using var harness = Harness.Create(activeCapacity: 2, historyTicks: 16);
        BodyBinding owned = harness.CreateBody(new NetworkEntityHandle(0, 1), Physics3DBodyKind.Dynamic);
        BodyBinding remote = harness.CreateBody(new NetworkEntityHandle(4095, 1), Physics3DBodyKind.Kinematic);
        harness.Apply(owned, locallyControlled: true, Physics3DNetLocalDrivenKind.Character, tick: 1);
        harness.Apply(remote, locallyControlled: false, default, tick: 1);
        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        for (uint tick = 2; tick <= 8; tick++)
        {
            DriveConvergenceTick(harness, owned, remote, tick, payload);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (uint tick = 9; tick <= 16; tick++)
        {
            DriveConvergenceTick(harness, owned, remote, tick, payload);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed convergence path allocated {allocated}B.");
    }

    private static void DriveConvergenceTick(
        Harness harness,
        BodyBinding owned,
        BodyBinding remote,
        uint tick,
        Span<byte> payload)
    {
        if (harness.Convergence.TrySample(tick, payload) != FixedInputPayloadSampleStatus.Sampled ||
            harness.Convergence.TryCommit(tick, payload) != FixedInputPayloadCommitStatus.Committed)
        {
            Assert.Fail($"Fixed input failed at tick {tick}.");
        }

        harness.Apply(owned, locallyControlled: true, Physics3DNetLocalDrivenKind.Character, tick);
        harness.Apply(remote, locallyControlled: false, default, tick, positionX: tick * 2f);
        harness.Convergence.Update(1f / 30f);
    }

    private static SessionSeatBinding Seat() => new(0, 1, new PlayerId(1));

    private static ReplicationApplyContext Context(in SessionSeatBinding seat, uint tick) =>
        new(in seat, SessionEpoch, tick, tick, tick == 1 ? ReplicationPacketKind.Full : ReplicationPacketKind.Delta);

    private readonly record struct BodyBinding(Entity Entity, Physics3DBodyId Body, NetworkEntityHandle Handle);

    private sealed class Harness : IDisposable
    {
        private readonly Physics3DShapeId _shape;
        private readonly SessionSeatBinding _seat;

        private Harness(
            World world,
            Physics3DWorld physics,
            CountingPredictionDriver driver,
            Physics3DReplicatedClientConvergence convergence,
            Physics3DShapeId shape,
            in SessionSeatBinding seat)
        {
            World = world;
            Physics = physics;
            Driver = driver;
            Convergence = convergence;
            _shape = shape;
            _seat = seat;
        }

        public World World { get; }
        public Physics3DWorld Physics { get; }
        public CountingPredictionDriver Driver { get; }
        public Physics3DReplicatedClientConvergence Convergence { get; }

        public static Harness Create(int activeCapacity, int historyTicks)
        {
            SessionSeatBinding seat = Seat();
            World world = World.Create();
            var physics = new Physics3DWorld(CreateWorldConfig(activeCapacity + 2));
            var driver = new CountingPredictionDriver(physics);
            var config = new Physics3DNetConfig
            {
                AuthoritativeHz = 30,
                SnapshotHz = 10,
                LocalPredictionHistoryTicks = historyTicks,
                RemoteInterpolationHistoryTicks = 8,
                ReplayEventCapacity = 32,
            };
            var convergence = new Physics3DReplicatedClientConvergence(
                world,
                physics,
                config,
                globalEntityCapacity: 8192,
                activeMirrorCapacity: activeCapacity,
                input: new ConstantInputSource(),
                driver: driver);
            Physics3DShapeId shape = physics.RegisterCapsuleShape(30f, 100f);
            return new Harness(world, physics, driver, convergence, shape, in seat);
        }

        public BodyBinding CreateBody(NetworkEntityHandle handle, Physics3DBodyKind kind)
        {
            var body = new Physics3DBodyCm { Kind = kind };
            var pose = new Physics3DPoseCm { Orientation = Quaternion.Identity };
            var previous = new PreviousPhysics3DPoseCm { Orientation = Quaternion.Identity };
            Entity entity = World.Create(in body, in pose, in previous);
            var description = new Physics3DBodyDescription(
                entity,
                kind,
                _shape,
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                kind == Physics3DBodyKind.Dynamic ? 80f : 0f,
                LayerMask.All,
                CreateMaterial(),
                Physics3DContinuousDetectionMode.Passive);
            Physics3DBodyId id = Physics.CreateBody(in description);
            body.Id = id;
            World.Set(entity, body);
            return new BodyBinding(entity, id, handle);
        }

        public void Apply(
            BodyBinding binding,
            bool locallyControlled,
            Physics3DNetLocalDrivenKind kind,
            uint tick,
            float positionX = 0f,
            float velocityX = 0f)
        {
            ReplicationApplyContext context = Context(in _seat, tick);
            ApplyWithContext(binding, locallyControlled, kind, in context, positionX, velocityX);
        }

        public void ApplyWithContext(
            BodyBinding binding,
            bool locallyControlled,
            Physics3DNetLocalDrivenKind kind,
            in ReplicationApplyContext context,
            float positionX = 0f,
            float velocityX = 0f)
        {
            var state = new Physics3DBodyState
            {
                PositionCm = new Vector3(positionX, 0f, 0f),
                Orientation = Quaternion.Identity,
                LinearVelocityCmPerSecond = new Vector3(velocityX, 0f, 0f),
                AngularVelocityRadiansPerSecond = Vector3.Zero,
                Awake = true,
            };
            Convergence.ApplyAuthoritative(
                binding.Entity,
                binding.Body,
                binding.Handle,
                locallyControlled,
                kind,
                in state,
                in context);
        }

        public void Dispose()
        {
            Convergence.Dispose();
            Physics.Dispose();
            World.Dispose();
        }
    }

    private sealed class ConstantInputSource : IPhysics3DClientInputSource
    {
        public bool TrySampleMovement(uint targetTick, out Vector2 movement)
        {
            movement = Vector2.UnitX;
            return targetTick > 0;
        }
    }

    private sealed class CountingPredictionDriver : IPhysics3DLocalPredictionDriver
    {
        private readonly IPhysics3DWorld _physics;

        public CountingPredictionDriver(IPhysics3DWorld physics)
        {
            _physics = physics;
        }

        public int StepCount { get; private set; }

        public bool Supports(Physics3DNetLocalDrivenKind kind) =>
            kind is Physics3DNetLocalDrivenKind.Character or Physics3DNetLocalDrivenKind.Vehicle;

        public bool TryStep(
            Entity entity,
            Physics3DBodyId body,
            Physics3DNetLocalDrivenKind kind,
            uint targetTick,
            in Physics3DFixedInputFrame input,
            out Physics3DBodyState predictedState)
        {
            if (entity == Entity.Null || targetTick == 0 || !Supports(kind) || !_physics.ContainsBody(body))
            {
                predictedState = default;
                return false;
            }

            StepCount++;
            predictedState = _physics.GetBodyState(body);
            predictedState.PositionCm += new Vector3(input.Movement.X, 0f, input.Movement.Y);
            predictedState.LinearVelocityCmPerSecond = new Vector3(input.Movement.X * 30f, 0f, input.Movement.Y * 30f);
            _physics.SetBodyState(body, in predictedState);
            return true;
        }
    }

    private static Physics3DMaterial CreateMaterial() => new(
        frictionCoefficient: 0.8f,
        maximumRecoveryVelocityCmPerSecond: 200f,
        springAngularFrequency: 30f,
        springTwiceDampingRatio: 1f);

    private static Physics3DWorldConfig CreateWorldConfig(int mobileCapacity) => new()
    {
        MobileBodyCapacity = mobileCapacity,
        StaticBodyCapacity = 1,
        ShapeCapacity = 8,
        InactiveIslandCapacity = Math.Max(1, mobileCapacity),
        ConstraintCapacity = 8,
        ConstraintsPerTypeBatchCapacity = 8,
        ConstraintCountPerBodyEstimate = 4,
        ContactPairCapacityPerWorker = 32,
        ActuationCommandCapacity = Math.Max(8, mobileCapacity * 4),
        WorkerCount = 1,
        FixedStepHz = 30,
        MaximumPhysicsStepsPerSourceTick = 1,
        SolverSubstepCount = 1,
        SolverVelocityIterationCount = 8,
        GravityCmPerSecondSquared = Vector3.Zero,
        LinearDamping = 0f,
        AngularDamping = 0f,
        MaximumSpeculativeMarginCm = 10f,
        SleepThreshold = 0.01f,
        MinimumTimestepCountUnderSleepThreshold = 32,
        ContinuousMinimumSweepTimestep = 0.001f,
        ContinuousSweepConvergenceThreshold = 0.001f,
        MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean,
    };
}
