using System.Numerics;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Layers;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Client;
using Ludots.Core.Physics3DNet.Input;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DReplicatedClientConvergenceTests
{
    private const int SchemaId = 41;
    private const ulong SessionEpoch = 11;

    [TestCase(Physics3DNetworkControlKinds.PlayerBody, Physics3DNetLocalDrivenKind.Character)]
    [TestCase(Physics3DNetworkControlKinds.Vehicle, Physics3DNetLocalDrivenKind.Vehicle)]
    public void LocalOwnership_InputCommitPredicts_ThenAuthoritativeCorrectionReplaysOnlyUnacked(
        uint controlKind,
        Physics3DNetLocalDrivenKind expectedKind)
    {
        SessionSeatBinding seat = Seat(0, 1);
        var handle = new NetworkEntityHandle(0, 1);
        using var harness = Harness.Create(seat, globalCapacity: 4, activeCapacity: 4, mobileCapacity: 2);
        Assert.That(
            harness.ApplyOwned(handle, seat, controlKind, tick: 1, snapshotId: 1, new Vector3(0f, 0f, 0f)),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(harness.Convergence.HasLocalDrivenBody, Is.True);
        Assert.That(harness.Convergence.LocalDrivenKind, Is.EqualTo(expectedKind));

        harness.Input.Movement = Vector2.UnitX;
        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(harness.Convergence.TrySample(2, payload), Is.EqualTo(FixedInputPayloadSampleStatus.Sampled));
        Assert.That(harness.Convergence.TryCommit(2, payload), Is.EqualTo(FixedInputPayloadCommitStatus.Committed));
        Assert.That(harness.Convergence.TrySample(3, payload), Is.EqualTo(FixedInputPayloadSampleStatus.Sampled));
        Assert.That(harness.Convergence.TryCommit(3, payload), Is.EqualTo(FixedInputPayloadCommitStatus.Committed));
        Assert.That(harness.Convergence.LocalHistory.Count, Is.EqualTo(2));

        Assert.That(
            harness.ApplyOwned(handle, seat, controlKind, tick: 2, snapshotId: 2, new Vector3(10f, 0f, 0f)),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(harness.Convergence.LocalHistory.ConfirmedTick, Is.EqualTo(2));
        Assert.That(harness.Convergence.LocalHistory.Count, Is.EqualTo(1));
        Assert.That(
            harness.Convergence.LocalHistory.TryGet(3, out Physics3DNetPredictedPose replayed, payload, out _),
            Is.True);
        Assert.That(replayed.PositionCm.X, Is.GreaterThan(10f));
    }

    [Test]
    public void DualLocalOwnershipInOnePacket_IsRejectedWithZeroPartialPublication()
    {
        SessionSeatBinding seat = Seat(0, 1);
        using var harness = Harness.Create(seat, globalCapacity: 4, activeCapacity: 4, mobileCapacity: 2);
        var first = new NetworkEntityHandle(0, 1);
        var second = new NetworkEntityHandle(1, 1);
        var ownership = new ReplicationControlOwnership(seat.Slot, seat.Generation, Physics3DNetworkControlKinds.PlayerBody);
        Assert.That(
            harness.ApplyMany(
                tick: 1,
                snapshotId: 1,
                new[]
                {
                    State(first, ownership, new Vector3(0f, 0f, 0f)),
                    State(second, ownership, new Vector3(100f, 0f, 0f)),
                }),
            Is.EqualTo(ReplicationBridgeResult.SchemaApplyRejected));
        Assert.That(harness.Bridge.TryResolve(first, out _), Is.False);
        Assert.That(harness.Bridge.TryResolve(second, out _), Is.False);
        Assert.That(harness.Convergence.HasLocalDrivenBody, Is.False);
        Assert.That(harness.Convergence.RemoteCount, Is.Zero);
        Assert.That(harness.Physics.ActiveMobileBodyCount, Is.Zero);
    }

    [Test]
    public void RemoteMidpointInterpolation_AndNoExtrapolationPastNewestIncludingNonZeroVelocity()
    {
        SessionSeatBinding seat = Seat(0, 1);
        using var harness = Harness.Create(seat, globalCapacity: 4, activeCapacity: 4, mobileCapacity: 2);
        var remote = new NetworkEntityHandle(3, 1);
        Assert.That(
            harness.ApplyRemote(remote, tick: 10, snapshotId: 1, new Vector3(0f, 0f, 0f), new Vector3(50f, 0f, 0f)),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(
            harness.ApplyRemote(remote, tick: 12, snapshotId: 2, new Vector3(20f, 0f, 0f), new Vector3(50f, 0f, 0f)),
            Is.EqualTo(ReplicationBridgeResult.Success));

        Assert.That(harness.Convergence.TrySampleRemote(in remote, 11f, out Physics3DNetInterpolationSample mid), Is.True);
        Assert.That(mid.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Sampled));
        Assert.That(mid.PositionCm.X, Is.EqualTo(10f).Within(0.001f));

        harness.Convergence.Update(1f / 30f);
        Assert.That(harness.Bridge.TryResolve(remote, out Entity entity), Is.True);
        Physics3DBodyCm body = harness.World.Get<Physics3DBodyCm>(entity);
        Physics3DBodyState frozen = harness.Physics.GetBodyState(body.Id);
        Assert.That(frozen.LinearVelocityCmPerSecond, Is.EqualTo(Vector3.Zero));
        Assert.That(harness.Convergence.TrySampleRemote(in remote, 100f, out Physics3DNetInterpolationSample over), Is.True);
        Assert.That(over.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Overflow));
        Assert.That(over.PositionCm.X, Is.EqualTo(20f).Within(0.001f));
    }

    [Test]
    public void Teardown_ClearsPredictionInterpolationAndIdentitySymmetrically()
    {
        SessionSeatBinding seat = Seat(0, 1);
        using var harness = Harness.Create(seat, globalCapacity: 4, activeCapacity: 4, mobileCapacity: 2);
        var local = new NetworkEntityHandle(0, 1);
        var remote = new NetworkEntityHandle(1, 1);
        var ownership = new ReplicationControlOwnership(seat.Slot, seat.Generation, Physics3DNetworkControlKinds.PlayerBody);
        Assert.That(
            harness.ApplyMany(
                tick: 1,
                snapshotId: 1,
                new[]
                {
                    State(local, ownership, Vector3.Zero),
                    State(remote, ReplicationControlOwnership.Unowned, new Vector3(5f, 0f, 0f)),
                }),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(harness.Convergence.TrySample(2, payload), Is.EqualTo(FixedInputPayloadSampleStatus.Sampled));
        Assert.That(harness.Convergence.TryCommit(2, payload), Is.EqualTo(FixedInputPayloadCommitStatus.Committed));

        harness.Observer.OnClientReplicationTornDown(in seat, SessionEpoch);
        Assert.That(harness.Convergence.IsSessionActive, Is.False);
        Assert.That(harness.Convergence.HasLocalDrivenBody, Is.False);
        Assert.That(harness.Convergence.RemoteCount, Is.Zero);
        Assert.That(harness.Convergence.LocalHistory.IsBound, Is.False);
        Assert.That(harness.Convergence.TrySampleRemote(in remote, 1f, out _), Is.False);
    }

    [Test]
    public void SameEpochDifferentGeneration_IsIsolated_AndNewEpochRebinds()
    {
        SessionSeatBinding gen1 = Seat(0, 1);
        SessionSeatBinding gen2 = Seat(0, 2);
        using var first = Harness.Create(gen1, globalCapacity: 2, activeCapacity: 2, mobileCapacity: 1);
        var handle = new NetworkEntityHandle(0, 1);
        Assert.That(
            first.ApplyOwned(handle, gen1, Physics3DNetworkControlKinds.PlayerBody, 1, 1, Vector3.Zero),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.Throws<InvalidOperationException>(
            () => first.Convergence.Teardown(in gen2, SessionEpoch));

        first.Observer.OnClientReplicationTornDown(in gen1, SessionEpoch);
        using var second = Harness.Create(gen2, globalCapacity: 2, activeCapacity: 2, mobileCapacity: 1, sessionEpoch: SessionEpoch + 1);
        Assert.That(
            second.ApplyOwned(handle, gen2, Physics3DNetworkControlKinds.Vehicle, 1, 1, new Vector3(3f, 0f, 0f)),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(second.Convergence.LocalDrivenKind, Is.EqualTo(Physics3DNetLocalDrivenKind.Vehicle));
        Assert.That(second.Convergence.SessionEpoch, Is.EqualTo(SessionEpoch + 1));
    }

    [Test]
    public void HighGlobalSlotWithTinyActiveCapacity_TracksAndCapacityFailureStaysAtomic()
    {
        SessionSeatBinding seat = Seat(0, 1);
        using var harness = Harness.Create(seat, globalCapacity: 1024, activeCapacity: 1, mobileCapacity: 2);
        var high = new NetworkEntityHandle(1000, 1);
        Assert.That(
            harness.ApplyRemote(high, tick: 1, snapshotId: 1, new Vector3(1f, 0f, 0f), Vector3.Zero),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(harness.Convergence.RemoteCount, Is.EqualTo(1));
        Assert.That(harness.Bridge.TryResolve(high, out Entity mirror), Is.True);

        var context = new ReplicationApplyContext(in seat, SessionEpoch, committedTick: 2, snapshotId: 2, ReplicationPacketKind.Delta);
        var overflow = new NetworkEntityHandle(1001, 1);
        Entity overflowEntity = harness.World.Create(
            new ReplicationMirrorIdentity(overflow),
            new ReplicationMirrorState(SchemaId, 1, default, ReplicationControlOwnership.Unowned),
            new Physics3DPoseCm { Position = new Vector3(2f, 0f, 0f), Orientation = Quaternion.Identity },
            new PreviousPhysics3DPoseCm { Position = new Vector3(2f, 0f, 0f), Orientation = Quaternion.Identity });
        var bodyDesc = new Physics3DBodyDescription(
            overflowEntity,
            Physics3DBodyKind.Kinematic,
            harness.Physics.RegisterCapsuleShape(30f, 100f),
            new Vector3(2f, 0f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            mass: 0f,
            LayerMask.All,
            CreateBodyConfig().Material,
            Physics3DContinuousDetectionMode.Passive);
        Physics3DBodyId overflowBody = harness.Physics.CreateBody(in bodyDesc);
        Assert.Throws<Physics3DNetCapacityExceededException>(
            () => harness.Convergence.ApplyAuthoritative(
                overflowEntity,
                overflowBody,
                in overflow,
                locallyControlled: false,
                default,
                new Physics3DBodyState
                {
                    PositionCm = new Vector3(2f, 0f, 0f),
                    Orientation = Quaternion.Identity,
                    LinearVelocityCmPerSecond = Vector3.Zero,
                    AngularVelocityRadiansPerSecond = Vector3.Zero,
                    Awake = true,
                },
                in context));
        Assert.That(harness.Convergence.RemoteCount, Is.EqualTo(1));
        Assert.That(harness.Convergence.TrySampleRemote(in high, 1f, out _), Is.True);
        Assert.That(harness.World.IsAlive(mirror), Is.True);
        harness.Physics.DestroyBody(overflowBody);
        harness.World.Destroy(overflowEntity);
    }

    [Test]
    public void DualRemoteCreateBeyondActiveCapacity_IsRejectedAtomically()
    {
        SessionSeatBinding seat = Seat(0, 1);
        using var harness = Harness.Create(
            seat,
            globalCapacity: 64,
            activeCapacity: 2,
            mobileCapacity: 2,
            convergenceActiveCapacity: 1);
        var first = new NetworkEntityHandle(10, 1);
        var second = new NetworkEntityHandle(11, 1);
        Assert.That(
            harness.ApplyMany(
                tick: 1,
                snapshotId: 1,
                new[]
                {
                    State(first, ReplicationControlOwnership.Unowned, new Vector3(1f, 0f, 0f)),
                    State(second, ReplicationControlOwnership.Unowned, new Vector3(2f, 0f, 0f)),
                }),
            Is.EqualTo(ReplicationBridgeResult.SchemaApplyRejected));
        Assert.That(harness.Bridge.TryResolve(first, out _), Is.False);
        Assert.That(harness.Bridge.TryResolve(second, out _), Is.False);
        Assert.That(harness.Convergence.RemoteCount, Is.Zero);
        Assert.That(harness.Physics.ActiveMobileBodyCount, Is.Zero);
        Assert.That(harness.Bridge.LastSnapshotId, Is.EqualTo(0));
    }

    [Test]
    public void UnacknowledgedHistoryOverwrite_FailsLoudly()
    {
        Physics3DNetConfig config = new()
        {
            AuthoritativeHz = 30,
            SnapshotHz = 10,
            LocalPredictionHistoryTicks = 4,
            RemoteInterpolationHistoryTicks = 4,
            ReplayEventCapacity = 16,
        };
        var history = new Physics3DNetLocalPredictionHistory(config);
        history.BindLocalDriven(1, 1, Physics3DNetLocalDrivenKind.Character);
        var payload = new byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(Physics3DFixedInputFrameCodec.TryEncode(Vector2.UnitX, payload), Is.True);
        for (long tick = 1; tick <= 4; tick++)
        {
            history.Record(
                new Physics3DNetPredictedPose(tick, new Vector3(tick, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero),
                payload);
        }

        try
        {
            history.Record(
                new Physics3DNetPredictedPose(5, new Vector3(5f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero),
                payload);
            Assert.Fail("Expected unacknowledged history overwrite to fail.");
        }
        catch (Physics3DNetCapacityExceededException)
        {
        }

        try
        {
            history.Record(
                new Physics3DNetPredictedPose(4, new Vector3(4f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero),
                payload);
            Assert.Fail("Expected duplicate tick record to fail.");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(history.Count, Is.EqualTo(4));
    }

    [Test]
    public void WarmedTrySampleTryCommitApplyAuthoritativeUpdate_HaveZeroManagedAllocations()
    {
        SessionSeatBinding seat = Seat(0, 1);
        using var harness = Harness.Create(seat, globalCapacity: 8, activeCapacity: 4, mobileCapacity: 4);
        var local = new NetworkEntityHandle(0, 1);
        var remote = new NetworkEntityHandle(1, 1);
        var ownership = new ReplicationControlOwnership(seat.Slot, seat.Generation, Physics3DNetworkControlKinds.PlayerBody);
        Assert.That(
            harness.ApplyMany(
                1,
                1,
                new[]
                {
                    State(local, ownership, Vector3.Zero),
                    State(remote, ReplicationControlOwnership.Unowned, Vector3.Zero, new Vector3(10f, 0f, 0f)),
                }),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(harness.Bridge.TryResolve(local, out Entity localEntity), Is.True);
        Assert.That(harness.Bridge.TryResolve(remote, out Entity remoteEntity), Is.True);
        Physics3DBodyId localBody = harness.World.Get<Physics3DBodyCm>(localEntity).Id;
        Physics3DBodyId remoteBody = harness.World.Get<Physics3DBodyCm>(remoteEntity).Id;

        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        for (uint tick = 2; tick <= 8; tick++)
        {
            Assert.That(harness.Convergence.TrySample(tick, payload), Is.EqualTo(FixedInputPayloadSampleStatus.Sampled));
            Assert.That(harness.Convergence.TryCommit(tick, payload), Is.EqualTo(FixedInputPayloadCommitStatus.Committed));
            var context = new ReplicationApplyContext(
                in seat,
                SessionEpoch,
                committedTick: tick,
                snapshotId: tick,
                ReplicationPacketKind.Delta);
            harness.Convergence.ApplyAuthoritative(
                localEntity,
                localBody,
                in local,
                locallyControlled: true,
                Physics3DNetLocalDrivenKind.Character,
                new Physics3DBodyState
                {
                    PositionCm = new Vector3(tick, 0f, 0f),
                    Orientation = Quaternion.Identity,
                    LinearVelocityCmPerSecond = Vector3.Zero,
                    AngularVelocityRadiansPerSecond = Vector3.Zero,
                    Awake = true,
                },
                in context);
            harness.Convergence.ApplyAuthoritative(
                remoteEntity,
                remoteBody,
                in remote,
                locallyControlled: false,
                default,
                new Physics3DBodyState
                {
                    PositionCm = new Vector3(tick * 2f, 0f, 0f),
                    Orientation = Quaternion.Identity,
                    LinearVelocityCmPerSecond = new Vector3(10f, 0f, 0f),
                    AngularVelocityRadiansPerSecond = Vector3.Zero,
                    Awake = true,
                },
                in context);
            harness.Convergence.Update(1f / 30f);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = GC.GetAllocatedBytesForCurrentThread();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (uint tick = 9; tick <= 16; tick++)
        {
            if (harness.Convergence.TrySample(tick, payload) != FixedInputPayloadSampleStatus.Sampled ||
                harness.Convergence.TryCommit(tick, payload) != FixedInputPayloadCommitStatus.Committed)
            {
                Assert.Fail("Warmed sample/commit failed.");
            }

            var context = new ReplicationApplyContext(
                in seat,
                SessionEpoch,
                committedTick: tick,
                snapshotId: tick,
                ReplicationPacketKind.Delta);
            harness.Convergence.ApplyAuthoritative(
                localEntity,
                localBody,
                in local,
                locallyControlled: true,
                Physics3DNetLocalDrivenKind.Character,
                new Physics3DBodyState
                {
                    PositionCm = new Vector3(tick, 0f, 0f),
                    Orientation = Quaternion.Identity,
                    LinearVelocityCmPerSecond = Vector3.Zero,
                    AngularVelocityRadiansPerSecond = Vector3.Zero,
                    Awake = true,
                },
                in context);
            harness.Convergence.ApplyAuthoritative(
                remoteEntity,
                remoteBody,
                in remote,
                locallyControlled: false,
                default,
                new Physics3DBodyState
                {
                    PositionCm = new Vector3(tick * 2f, 0f, 0f),
                    Orientation = Quaternion.Identity,
                    LinearVelocityCmPerSecond = new Vector3(10f, 0f, 0f),
                    AngularVelocityRadiansPerSecond = Vector3.Zero,
                    Awake = true,
                },
                in context);
            harness.Convergence.Update(1f / 30f);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed convergence path allocated {allocated}B.");
    }

    private static ReplicatedEntityState State(
        NetworkEntityHandle handle,
        in ReplicationControlOwnership ownership,
        Vector3 position,
        Vector3 linearVelocity = default)
    {
        var body = new Physics3DBodyState
        {
            PositionCm = position,
            Orientation = Quaternion.Identity,
            LinearVelocityCmPerSecond = linearVelocity,
            AngularVelocityRadiansPerSecond = Vector3.Zero,
            Awake = true,
        };
        Assert.That(
            Physics3DReplicationStateCodec.TryEncode(
                in body,
                Physics3DBodyKind.Dynamic,
                new Physics3DReplicationQuantizationConfig(),
                out ReplicationStateVector values),
            Is.True);
        return new ReplicatedEntityState(handle, SchemaId, revision: 1, values, ownership);
    }

    private static SessionSeatBinding Seat(int slot, uint generation) =>
        new(slot, generation, new PlayerId(slot + 1));

    private sealed class Harness : IDisposable
    {
        private readonly AuthoritativeReplicationChannel _channel;
        private readonly ReplicationPacketBuffer _packet;
        private readonly ulong _sessionEpoch;

        private Harness(
            World world,
            Physics3DWorld physics,
            Physics3DReplicatedClientConvergence convergence,
            ClientWorldReplicationBridge bridge,
            Physics3DClientNetworkRuntimeObserver observer,
            ScriptedInputSource input,
            AuthoritativeReplicationChannel channel,
            ReplicationPacketBuffer packet,
            ulong sessionEpoch)
        {
            World = world;
            Physics = physics;
            Convergence = convergence;
            Bridge = bridge;
            Observer = observer;
            Input = input;
            _channel = channel;
            _packet = packet;
            _sessionEpoch = sessionEpoch;
        }

        public World World { get; }
        public Physics3DWorld Physics { get; }
        public Physics3DReplicatedClientConvergence Convergence { get; }
        public ClientWorldReplicationBridge Bridge { get; }
        public Physics3DClientNetworkRuntimeObserver Observer { get; }
        public ScriptedInputSource Input { get; }

        public static Harness Create(
            SessionSeatBinding seat,
            int globalCapacity,
            int activeCapacity,
            int mobileCapacity,
            ulong sessionEpoch = SessionEpoch,
            int? convergenceActiveCapacity = null)
        {
            int convergenceCapacity = convergenceActiveCapacity ?? activeCapacity;
            var world = World.Create();
            var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity));
            var input = new ScriptedInputSource();
            var convergence = new Physics3DReplicatedClientConvergence(
                world,
                physics,
                new Physics3DNetConfig(),
                globalCapacity,
                convergenceCapacity,
                input,
                new ScriptedPredictionDriver(physics));
            var appliers = new ClientReplicationSchemaApplierRegistry(SchemaId);
            Assert.That(
                appliers.Register(
                    SchemaId,
                    new Physics3DClientBodyReplicationApplier(
                        physics,
                        SchemaId,
                        new Physics3DReplicationQuantizationConfig(),
                        CreateBodyConfig(),
                        convergence)),
                Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            ClientWorldReplicationBridge bridge = new ClientReplicationBridgeFactory(
                world,
                globalCapacity,
                activeCapacity,
                appliers).Create(in seat, sessionEpoch);
            var channel = new AuthoritativeReplicationChannel(
                new NetworkEntityTable(capacity: globalCapacity),
                replicationEntityCapacityPerSeat: activeCapacity,
                baselineCapacity: activeCapacity,
                new ReplicationDisclosureChangeLog(capacity: activeCapacity * 2));
            return new Harness(
                world,
                physics,
                convergence,
                bridge,
                new Physics3DClientNetworkRuntimeObserver(convergence),
                input,
                channel,
                new ReplicationPacketBuffer(entityCapacity: activeCapacity),
                sessionEpoch);
        }

        public ReplicationBridgeResult ApplyOwned(
            NetworkEntityHandle handle,
            SessionSeatBinding seat,
            uint controlKind,
            uint tick,
            ulong snapshotId,
            Vector3 position)
        {
            var ownership = new ReplicationControlOwnership(seat.Slot, seat.Generation, controlKind);
            return ApplyMany(tick, snapshotId, new[] { State(handle, ownership, position) });
        }

        public ReplicationBridgeResult ApplyRemote(
            NetworkEntityHandle handle,
            uint tick,
            ulong snapshotId,
            Vector3 position,
            Vector3 linearVelocity)
        {
            return ApplyMany(
                tick,
                snapshotId,
                new[] { State(handle, ReplicationControlOwnership.Unowned, position, linearVelocity) });
        }

        public ReplicationBridgeResult ApplyMany(uint tick, ulong snapshotId, ReplicatedEntityState[] states)
        {
            var disclosures = new ReplicationDisclosureInput[states.Length];
            for (int i = 0; i < states.Length; i++)
            {
                disclosures[i] = new ReplicationDisclosureInput(states[i].Entity, KnowledgePresence.LiveVisible);
            }

            ReplicationBuildResult build = Bridge.LastSnapshotId == 0
                ? _channel.BuildFull(_sessionEpoch, tick, snapshotId, states, disclosures, _packet)
                : _channel.BuildDelta(
                    _sessionEpoch,
                    tick,
                    snapshotId,
                    Bridge.LastSnapshotId,
                    states,
                    disclosures,
                    _packet);
            Assert.That(build, Is.EqualTo(ReplicationBuildResult.Success));
            return Bridge.Apply(_packet);
        }

        public void Dispose()
        {
            Convergence.Dispose();
            Physics.Dispose();
            World.Dispose();
        }
    }

    private sealed class ScriptedInputSource : IPhysics3DClientInputSource
    {
        public Vector2 Movement { get; set; } = Vector2.UnitX;

        public bool TrySampleMovement(uint targetTick, out Vector2 movement)
        {
            movement = Movement;
            return targetTick > 0;
        }
    }

    private sealed class ScriptedPredictionDriver : IPhysics3DLocalPredictionDriver
    {
        private readonly IPhysics3DWorld _physics;

        public ScriptedPredictionDriver(IPhysics3DWorld physics)
        {
            _physics = physics;
        }

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
            if (!_physics.ContainsBody(body))
            {
                predictedState = default;
                return false;
            }

            predictedState = _physics.GetBodyState(body);
            predictedState.LinearVelocityCmPerSecond = new Vector3(input.Movement.X * 100f, 0f, input.Movement.Y * 100f);
            predictedState.PositionCm += predictedState.LinearVelocityCmPerSecond / 30f;
            _physics.SetBodyState(body, in predictedState);
            return true;
        }
    }

    private static Physics3DNetworkPlayerBodyConfig CreateBodyConfig() => new()
    {
        RadiusCm = 30f,
        CylinderLengthCm = 100f,
        Mass = 80f,
        CollisionLayer = LayerMask.All,
        Material = new Physics3DMaterial(
            frictionCoefficient: 0.8f,
            maximumRecoveryVelocityCmPerSecond: 200f,
            springAngularFrequency: 30f,
            springTwiceDampingRatio: 1f),
        ContinuousDetection = Physics3DContinuousDetectionMode.Passive,
    };

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
