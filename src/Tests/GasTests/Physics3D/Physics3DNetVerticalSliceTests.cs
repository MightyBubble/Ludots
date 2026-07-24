using System;
using System.Numerics;
using System.Reflection;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DNetVerticalSliceTests
{
    private static Physics3DNetConfig CreateConfig(
        int playerCapacity = 150,
        int snapshotEntityCapacity = 64,
        int aoiEntityCapacityPerClient = 8,
        int localPredictionHistoryTicks = 16,
        int remoteInterpolationHistoryTicks = 8,
        int replayEventCapacity = 128,
        int inputHistoryTicksPerPlayer = 16,
        int maxFutureInputTicks = 8,
        int clientCapacity = -1)
    {
        var config = new Physics3DNetConfig
        {
            AuthoritativeHz = 30,
            SnapshotHz = 10,
            PlayerCapacity = playerCapacity,
            ClientCapacity = clientCapacity < 0 ? playerCapacity : clientCapacity,
            SnapshotEntityCapacity = snapshotEntityCapacity,
            AoiEntityCapacityPerClient = aoiEntityCapacityPerClient,
            LocalPredictionHistoryTicks = localPredictionHistoryTicks,
            RemoteInterpolationHistoryTicks = remoteInterpolationHistoryTicks,
            ReplayEventCapacity = replayEventCapacity,
            InputHistoryTicksPerPlayer = inputHistoryTicksPerPlayer,
            MaxFutureInputTicks = maxFutureInputTicks
        };
        config.Validate();
        return config;
    }

    private static void AdvanceLifecycleCommitted(Physics3DNetTickLifecycle life, long committedInclusive)
    {
        while (life.CommittedTick < committedInclusive)
        {
            life.BeginExecute(life.CommittedTick + 1);
            life.Commit();
        }
    }

    [Test]
    public void Config_RequiresHard30Hz_AndHistoryCoversFutureWindowPlusCommittedCell()
    {
        var ok = new Physics3DNetConfig { AuthoritativeHz = 30, SnapshotHz = 10 };
        Assert.That(ok.SnapshotIntervalTicks, Is.EqualTo(3));

        var badHz = new Physics3DNetConfig { AuthoritativeHz = 60, SnapshotHz = 10 };
        Assert.Throws<ArgumentOutOfRangeException>(() => badHz.Validate());

        var badDivisor = new Physics3DNetConfig { AuthoritativeHz = 30, SnapshotHz = 7 };
        Assert.Throws<ArgumentOutOfRangeException>(() => badDivisor.Validate());

        var tooSmallHistory = new Physics3DNetConfig
        {
            AuthoritativeHz = 30,
            SnapshotHz = 10,
            MaxFutureInputTicks = 8,
            InputHistoryTicksPerPlayer = 8
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => tooSmallHistory.Validate());

        var exactMinimum = new Physics3DNetConfig
        {
            AuthoritativeHz = 30,
            SnapshotHz = 10,
            MaxFutureInputTicks = 8,
            InputHistoryTicksPerPlayer = 9
        };
        Assert.DoesNotThrow(() => exactMinimum.Validate());
    }

    [Test]
    public void TickLifecycle_IsMonotonicExecutingCommittedSnapshot()
    {
        var life = new Physics3DNetTickLifecycle(CreateConfig());
        Assert.That(life.ExecutingTick, Is.EqualTo(0));
        Assert.That(life.CommittedTick, Is.EqualTo(0));
        Assert.That(life.SnapshotTick, Is.EqualTo(0));

        life.BeginExecute(1);
        Assert.That(life.ExecutingTick, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => life.PublishSnapshot(1));
        life.Commit();
        Assert.That(life.CommittedTick, Is.EqualTo(1));
        Assert.That(life.ExecutingTick, Is.EqualTo(0));

        for (long tick = 2; tick <= 3; tick++)
        {
            life.BeginExecute(tick);
            life.Commit();
        }

        life.PublishSnapshot(3);
        Assert.That(life.SnapshotTick, Is.EqualTo(3));
        Assert.Throws<InvalidOperationException>(() => life.PublishSnapshot(3));
        Assert.Throws<InvalidOperationException>(() => life.BeginExecute(5));
    }

    [Test]
    public void InputRing_Supports150Players_AndClassifiesReorderDuplicateLateMissingConflict()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 150, inputHistoryTicksPerPlayer: 16, maxFutureInputTicks: 4);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        Assert.That(ring.PlayerCapacity, Is.EqualTo(150));

        for (int i = 0; i < 150; i++)
        {
            ring.RegisterPlayer(networkPlayerId: 1000 + i, generation: 1, playerSlot: i);
        }

        Assert.That(ring.CountRegisteredPlayers(), Is.EqualTo(150));
        AdvanceLifecycleCommitted(life, 10);

        Physics3DNetInputArrivalResult accepted = ring.Submit(MakeInput(tick: 11, player: 1000, sequence: 1));
        Assert.That(accepted.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Accepted));

        Physics3DNetInputArrivalResult duplicate = ring.Submit(MakeInput(tick: 11, player: 1000, sequence: 1));
        Assert.That(duplicate.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Duplicate));

        Physics3DNetInputArrivalResult late = ring.Submit(MakeInput(tick: 10, player: 1000, sequence: 2));
        Assert.That(late.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Late));

        Physics3DNetInputArrivalResult tooFar = ring.Submit(MakeInput(tick: 20, player: 1000, sequence: 2));
        Assert.That(tooFar.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.TooFarFuture));

        Assert.That(ring.Submit(MakeInput(tick: 12, player: 1000, sequence: 2)).Accepted, Is.True);
        Physics3DNetInputArrivalResult outOfOrder = ring.Submit(MakeInput(tick: 11, player: 1000, sequence: 3, buttons: 9));
        // Cell already occupied by seq 1 / buttons 7 → Conflict, never overwrite.
        Assert.That(outOfOrder.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Conflict));
        Assert.That(ring.TryGet(0, 11, out Physics3DNetInputFrameView kept), Is.EqualTo(Physics3DNetInputLookupResult.Present));
        Assert.That(kept.Buttons, Is.EqualTo(7u));
        Assert.That(kept.Sequence, Is.EqualTo(1u));

        Assert.That(
            ring.TryGet(0, 13, out _),
            Is.EqualTo(Physics3DNetInputLookupResult.Missing));
        Assert.That(ring.MissingLookupCount, Is.EqualTo(1));
        Assert.That(ring.DuplicateCount, Is.EqualTo(1));
        Assert.That(ring.LateCount, Is.EqualTo(1));
        Assert.That(ring.TooFarFutureCount, Is.EqualTo(1));
        Assert.That(ring.ConflictCount, Is.EqualTo(1));

        for (int i = 1; i < 150; i++)
        {
            Assert.That(ring.Submit(MakeInput(tick: 11, player: 1000 + i, sequence: 1)).Accepted, Is.True);
        }

        Assert.That(ring.TryBeginAuthoritativeExecute(11, stackalloc int[150], out Physics3DNetInputExecuteGateResult gate), Is.True);
        Assert.That(gate.Kind, Is.EqualTo(Physics3DNetInputExecuteGateResultKind.BeganExecute));
        life.Commit();
        ring.AcknowledgeInputFramesAfterCommit(11);
        Assert.That(ring.TryGet(0, 11, out Physics3DNetInputFrameView frame), Is.EqualTo(Physics3DNetInputLookupResult.Present));
        Assert.That(frame.ConfirmationTick, Is.EqualTo(11));
        Assert.That(life.CommittedTick, Is.EqualTo(11));
    }

    [Test]
    public void InputRing_AcceptsEmptyCellReorder_AsAcceptedOutOfOrder()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 2, inputHistoryTicksPerPlayer: 16, maxFutureInputTicks: 8);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        ring.RegisterPlayer(1, 1, 0);
        AdvanceLifecycleCommitted(life, 10);

        Assert.That(ring.Submit(MakeInput(12, 1, 2)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Accepted));
        Physics3DNetInputArrivalResult reorder = ring.Submit(MakeInput(11, 1, 1));
        Assert.That(reorder.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.AcceptedOutOfOrder));
        Assert.That(reorder.Accepted, Is.True);
        Assert.That(ring.AcceptedOutOfOrderCount, Is.EqualTo(1));
        Assert.That(ring.TryGet(0, 11, out Physics3DNetInputFrameView frame), Is.EqualTo(Physics3DNetInputLookupResult.Present));
        Assert.That(frame.Sequence, Is.EqualTo(1u));
    }

    [Test]
    public void InputRing_Conflict_DoesNotOverwrite_AndIsNotDuplicate()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 2, inputHistoryTicksPerPlayer: 16, maxFutureInputTicks: 8);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        ring.RegisterPlayer(1, 1, 0);

        ring.Submit(MakeInput(1, 1, sequence: 1, buttons: 7));
        Physics3DNetInputArrivalResult seqConflict = ring.Submit(MakeInput(1, 1, sequence: 2, buttons: 7));
        Assert.That(seqConflict.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Conflict));

        Physics3DNetInputArrivalResult payloadConflict = ring.Submit(MakeInput(1, 1, sequence: 1, buttons: 99));
        Assert.That(payloadConflict.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Conflict));

        Assert.That(ring.TryGet(0, 1, out Physics3DNetInputFrameView frame), Is.EqualTo(Physics3DNetInputLookupResult.Present));
        Assert.That(frame.Sequence, Is.EqualTo(1u));
        Assert.That(frame.Buttons, Is.EqualTo(7u));
        Assert.That(ring.ConflictCount, Is.EqualTo(2));
        Assert.That(ring.DuplicateCount, Is.EqualTo(0));
    }

    [Test]
    public void InputRing_ExecutionCutoffRejectsSameTickDuplicateConflictAndEmptyCell_ButAllowsFutureTick()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 3, inputHistoryTicksPerPlayer: 16, maxFutureInputTicks: 8);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        for (int player = 0; player < 3; player++)
        {
            ring.RegisterPlayer(player, generation: 1, playerSlot: player);
            Assert.That(ring.Submit(MakeInput(1, player, sequence: 1)).Accepted, Is.True);
        }

        Assert.That(ring.TryBeginAuthoritativeExecute(1, stackalloc int[3], out _), Is.True);

        Physics3DNetInputArrivalResult duplicate = ring.Submit(MakeInput(1, player: 0, sequence: 1));
        Physics3DNetInputArrivalResult conflict = ring.Submit(MakeInput(1, player: 1, sequence: 2, buttons: 99));

        ring.UnregisterPlayer(2);
        ring.RegisterPlayer(networkPlayerId: 2, generation: 1, playerSlot: 2);
        Assert.That(ring.TryGet(2, 1, out _), Is.EqualTo(Physics3DNetInputLookupResult.Missing));
        Physics3DNetInputArrivalResult emptyCell = ring.Submit(MakeInput(1, player: 2, sequence: 2));

        Physics3DNetInputArrivalResult future = ring.Submit(MakeInput(2, player: 0, sequence: 2));

        Assert.Multiple(() =>
        {
            Assert.That(duplicate.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.RejectedAtExecutionCutoff));
            Assert.That(conflict.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.RejectedAtExecutionCutoff));
            Assert.That(emptyCell.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.RejectedAtExecutionCutoff));
            Assert.That(future.Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Accepted));
            Assert.That(ring.ExecutionCutoffRejectionCount, Is.EqualTo(3));
            Assert.That(ring.DuplicateCount, Is.Zero);
            Assert.That(ring.ConflictCount, Is.Zero);
            Assert.That(life.ExecutingTick, Is.EqualTo(1));
        });
    }

    [Test]
    public void InputRing_PlayerUniqueness_UnregisterClearsHistory_AndSlotReuseRequiresNewGeneration()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 4, inputHistoryTicksPerPlayer: 8, maxFutureInputTicks: 4);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        ring.RegisterPlayer(10, generation: 1, playerSlot: 0);
        Assert.Throws<InvalidOperationException>(() => ring.RegisterPlayer(10, generation: 2, playerSlot: 1));

        ring.Submit(MakeInput(1, 10, 1));
        Assert.That(ring.TryGet(0, 1, out _), Is.EqualTo(Physics3DNetInputLookupResult.Present));

        ring.UnregisterPlayer(0);
        Assert.That(ring.TryGet(0, 1, out _), Is.EqualTo(Physics3DNetInputLookupResult.UnregisteredPlayer));
        Assert.That(ring.CountRegisteredPlayers(), Is.EqualTo(0));

        ring.RegisterPlayer(10, generation: 2, playerSlot: 0);
        Assert.That(ring.TryGet(0, 1, out _), Is.EqualTo(Physics3DNetInputLookupResult.Missing));
        Assert.That(ring.Submit(MakeInput(1, 10, 1, generation: 2)).Accepted, Is.True);
    }

    [Test]
    public void InputRing_HasNoSecondCommitPath_AndAcknowledgementNeverAdvancesAuthority()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 2, inputHistoryTicksPerPlayer: 16, maxFutureInputTicks: 8);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        ring.RegisterPlayer(1, 1, 0);
        ring.RegisterPlayer(2, 1, 1);

        FieldInfo[] fields = typeof(Physics3DNetInputRing).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (FieldInfo field in fields)
        {
            Assert.That(
                field.Name.Contains("committed", StringComparison.OrdinalIgnoreCase),
                Is.False,
                $"InputRing must not own a second committed-tick authority field '{field.Name}'.");
        }

        Assert.That(
            typeof(Physics3DNetInputRing).GetMethod("SetCommittedTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            Is.Null);
        Assert.That(
            typeof(Physics3DNetInputRing).GetMethod("TryConfirmTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            Is.Null);
        Assert.That(
            typeof(Physics3DNetInputRing).GetMethod("ConfirmTick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            Is.Null);

        Assert.That(ring.Lifecycle, Is.SameAs(life));
        ring.Submit(MakeInput(1, 1, 1));
        ring.Submit(MakeInput(1, 2, 1));
        Assert.That(ring.TryBeginAuthoritativeExecute(1, stackalloc int[2], out _), Is.True);
        Assert.That(life.ExecutingTick, Is.EqualTo(1));
        Assert.That(life.CommittedTick, Is.EqualTo(0));

        Assert.Throws<InvalidOperationException>(() => ring.AcknowledgeInputFramesAfterCommit(1));
        Assert.That(life.CommittedTick, Is.EqualTo(0));
        Assert.That(life.ExecutingTick, Is.EqualTo(1));
        Assert.That(ring.TryGet(0, 1, out Physics3DNetInputFrameView before), Is.EqualTo(Physics3DNetInputLookupResult.Present));
        Assert.That(before.ConfirmationTick, Is.EqualTo(0));

        life.Commit();
        Assert.That(life.CommittedTick, Is.EqualTo(1));
        ring.AcknowledgeInputFramesAfterCommit(1);
        Assert.That(life.CommittedTick, Is.EqualTo(1));
        Assert.That(life.ExecutingTick, Is.EqualTo(0));
        Assert.That(ring.TryGet(0, 1, out Physics3DNetInputFrameView after), Is.EqualTo(Physics3DNetInputLookupResult.Present));
        Assert.That(after.ConfirmationTick, Is.EqualTo(1));

        ring.AcknowledgeInputFramesAfterCommit(1);
        Assert.That(life.CommittedTick, Is.EqualTo(1), "Repeated acknowledgement must not advance authority.");
    }

    [Test]
    public void MissingInputs_PreventBeginAuthoritativeExecute_Atomically()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 150, inputHistoryTicksPerPlayer: 16, maxFutureInputTicks: 8);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        for (int i = 0; i < 150; i++)
        {
            ring.RegisterPlayer(i, 1, i);
        }

        for (int i = 0; i < 149; i++)
        {
            ring.Submit(MakeInput(1, player: i, sequence: 1));
        }

        Span<int> missing = stackalloc int[150];
        bool ok = ring.TryBeginAuthoritativeExecute(1, missing, out Physics3DNetInputExecuteGateResult result);
        Assert.That(ok, Is.False);
        Assert.That(result.Kind, Is.EqualTo(Physics3DNetInputExecuteGateResultKind.MissingInputs));
        Assert.That(result.MissingCount, Is.EqualTo(1));
        Assert.That(missing[0], Is.EqualTo(149));
        Assert.That(life.ExecutingTick, Is.EqualTo(0));
        Assert.That(life.CommittedTick, Is.EqualTo(0));
        Assert.That(ring.TryGet(0, 1, out Physics3DNetInputFrameView frame), Is.EqualTo(Physics3DNetInputLookupResult.Present));
        Assert.That(frame.ConfirmationTick, Is.EqualTo(0));

        var missingEx = Assert.Throws<Physics3DNetMissingInputException>(() => ring.BeginAuthoritativeExecute(1));
        Assert.That(missingEx!.MissingCount, Is.EqualTo(1));
        Assert.That(life.ExecutingTick, Is.EqualTo(0));
        Assert.That(life.CommittedTick, Is.EqualTo(0));

        Assert.Throws<Physics3DNetCapacityExceededException>(
            () => ring.TryValidateInputsForExecute(1, Span<int>.Empty, out _));

        ring.Submit(MakeInput(1, player: 149, sequence: 1));
        Assert.That(ring.TryBeginAuthoritativeExecute(1, missing, out result), Is.True);
        Assert.That(result.Kind, Is.EqualTo(Physics3DNetInputExecuteGateResultKind.BeganExecute));
        Assert.That(life.ExecutingTick, Is.EqualTo(1));
        Assert.That(life.CommittedTick, Is.EqualTo(0));
    }

    [Test]
    public void AcknowledgeBeforeCommit_IsRejected_AndArrivalWindowAdvancesOnlyViaLifecycleCommit()
    {
        Physics3DNetConfig config = CreateConfig(playerCapacity: 2, inputHistoryTicksPerPlayer: 16, maxFutureInputTicks: 4);
        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        ring.RegisterPlayer(1, 1, 0);

        Assert.That(ring.Submit(MakeInput(5, 1, 1)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.TooFarFuture));
        Assert.That(ring.Submit(MakeInput(1, 1, 1)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Accepted));

        ring.BeginAuthoritativeExecute(1);
        Assert.Throws<InvalidOperationException>(() => ring.AcknowledgeInputFramesAfterCommit(1));
        Assert.That(life.CommittedTick, Is.EqualTo(0));
        Assert.That(ring.Submit(MakeInput(5, 1, 2)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.TooFarFuture));

        life.Commit();
        Assert.That(life.CommittedTick, Is.EqualTo(1));
        ring.AcknowledgeInputFramesAfterCommit(1);

        Assert.That(ring.Submit(MakeInput(1, 1, 3)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Late));
        Assert.That(ring.Submit(MakeInput(5, 1, 2)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Accepted));
        Assert.That(ring.Submit(MakeInput(6, 1, 3)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.TooFarFuture));

        AdvanceLifecycleCommitted(life, 2);
        Assert.That(life.CommittedTick, Is.EqualTo(2));
        Assert.That(ring.Submit(MakeInput(6, 1, 3)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.Accepted));
        Assert.That(ring.Submit(MakeInput(7, 1, 4)).Disposition, Is.EqualTo(Physics3DNetInputArrivalDisposition.TooFarFuture));
    }

    [Test]
    public void SnapshotStore_CapacityOverflow_IsAtomic_AndDoesNotTruncate()
    {
        Physics3DNetConfig config = CreateConfig(snapshotEntityCapacity: 2);
        var store = new Physics3DNetAuthoritativeSnapshotStore(config);
        store.ReplaceAll(
            snapshotTick: 3,
            baselineId: 1,
            [
                MakeSnapshotEntity(1, 1, Physics3DNetReplicationOp.Spawn, 1),
                MakeSnapshotEntity(2, 1, Physics3DNetReplicationOp.Update, 1)
            ]);
        Assert.That(store.Count, Is.EqualTo(2));
        Assert.That(store.Get(0).NetworkEntityId, Is.EqualTo(1));

        Physics3DNetSnapshotEntityWrite[] overflow =
        [
            MakeSnapshotEntity(10, 1, Physics3DNetReplicationOp.Spawn, 2),
            MakeSnapshotEntity(11, 1, Physics3DNetReplicationOp.Spawn, 2),
            MakeSnapshotEntity(12, 1, Physics3DNetReplicationOp.Spawn, 2)
        ];
        var ex = Assert.Throws<Physics3DNetCapacityExceededException>(
            () => store.ReplaceAll(6, 2, overflow));
        Assert.That(ex!.Resource, Is.EqualTo("authoritative snapshot entities"));
        Assert.That(ex.Capacity, Is.EqualTo(2));
        Assert.That(ex.Tick, Is.EqualTo(6));

        Assert.That(store.Count, Is.EqualTo(2));
        Assert.That(store.SnapshotTick, Is.EqualTo(3));
        Assert.That(store.Get(0).NetworkEntityId, Is.EqualTo(1));
        Assert.That(store.Get(1).NetworkEntityId, Is.EqualTo(2));
    }

    [Test]
    public void SnapshotStore_StreamingEndWrite_AndReplaceAllPrevalidate_LeavePriorIntact()
    {
        Physics3DNetConfig config = CreateConfig(snapshotEntityCapacity: 4);
        var store = new Physics3DNetAuthoritativeSnapshotStore(config);
        store.ReplaceAll(3, 1, [MakeSnapshotEntity(1, 1, Physics3DNetReplicationOp.Spawn, 1)]);

        store.BeginWrite(6, 2, expectedEntityCount: 2);
        store.Write(MakeSnapshotEntity(10, 1, Physics3DNetReplicationOp.Spawn, 2));
        Assert.Throws<InvalidOperationException>(() => store.EndWrite());
        Assert.That(store.IsWriting, Is.False);
        Assert.That(store.SnapshotTick, Is.EqualTo(3));
        Assert.That(store.Count, Is.EqualTo(1));

        store.BeginWrite(6, 2, expectedEntityCount: 1);
        store.Write(MakeSnapshotEntity(10, 1, Physics3DNetReplicationOp.Spawn, 2));
        Assert.Throws<InvalidOperationException>(() => store.Write(MakeSnapshotEntity(11, 1, Physics3DNetReplicationOp.Spawn, 2)));
        Assert.That(store.SnapshotTick, Is.EqualTo(3));

        Assert.Throws<ArgumentException>(
            () => store.ReplaceAll(
                9,
                3,
                [
                    MakeSnapshotEntity(5, 1, Physics3DNetReplicationOp.Spawn, 3),
                    MakeSnapshotEntity(5, 1, Physics3DNetReplicationOp.Update, 3)
                ]));
        Assert.That(store.SnapshotTick, Is.EqualTo(3));

        Assert.Throws<ArgumentException>(
            () => store.ReplaceAll(
                9,
                3,
                [MakeSnapshotEntity(5, 1, Physics3DNetReplicationOp.Spawn, baselineId: 99)]));
        Assert.That(store.SnapshotTick, Is.EqualTo(3));

        store.BeginWrite(9, 3, 2);
        store.Write(MakeSnapshotEntity(4, 1, Physics3DNetReplicationOp.Spawn, 3));
        store.Write(MakeSnapshotEntity(8, 1, Physics3DNetReplicationOp.Update, 3));
        store.EndWrite();
        Assert.That(store.SnapshotTick, Is.EqualTo(9));
        Assert.That(store.Count, Is.EqualTo(2));
        Assert.That(store.Get(0).NetworkEntityId, Is.EqualTo(4));
    }

    [Test]
    public void AoiDelta_EnterLeaveReenter_PreservesGeneration_AndBaselineLossRequiresFullSnapshot()
    {
        Physics3DNetConfig config = CreateConfig(aoiEntityCapacityPerClient: 4, playerCapacity: 2);
        var aoi = new Physics3DNetAoiDeltaBuilder(config);
        Span<Physics3DNetSnapshotEntityWrite> destination = stackalloc Physics3DNetSnapshotEntityWrite[8];

        Physics3DNetAoiDeltaBuildResult missing = aoi.BuildDelta(
            clientSlot: 0,
            snapshotTick: 3,
            requiredBaselineId: 1,
            currentInterest: [MakeInterest(100, generation: 1)],
            destination);
        Assert.That(missing.Kind, Is.EqualTo(Physics3DNetAoiDeltaResultKind.BaselineMissing));
        Assert.That(missing.RequiresFullSnapshot, Is.True);

        aoi.AcknowledgeBaseline(0, baselineId: 1);
        Physics3DNetAoiDeltaBuildResult enter = aoi.BuildDelta(
            0,
            3,
            1,
            [MakeInterest(100, generation: 1, positionX: 10f)],
            destination);
        Assert.That(enter.Kind, Is.EqualTo(Physics3DNetAoiDeltaResultKind.Built));
        Assert.That(enter.WrittenCount, Is.EqualTo(1));
        Assert.That(destination[0].Op, Is.EqualTo(Physics3DNetReplicationOp.Spawn));

        Physics3DNetAoiDeltaBuildResult update = aoi.BuildDelta(
            0,
            6,
            1,
            [MakeInterest(100, generation: 1, positionX: 20f)],
            destination);
        Assert.That(update.WrittenCount, Is.EqualTo(1));
        Assert.That(destination[0].Op, Is.EqualTo(Physics3DNetReplicationOp.Update));

        Physics3DNetAoiDeltaBuildResult leave = aoi.BuildDelta(
            0,
            9,
            1,
            ReadOnlySpan<Physics3DNetAoiInterest>.Empty,
            destination);
        Assert.That(leave.WrittenCount, Is.EqualTo(1));
        Assert.That(destination[0].Op, Is.EqualTo(Physics3DNetReplicationOp.Despawn));
        Assert.That(aoi.IsTracked(0, 100, out _), Is.False);

        Physics3DNetAoiDeltaBuildResult reenter = aoi.BuildDelta(
            0,
            12,
            1,
            [MakeInterest(100, generation: 2, positionX: 30f)],
            destination);
        Assert.That(reenter.WrittenCount, Is.EqualTo(1));
        Assert.That(destination[0].Op, Is.EqualTo(Physics3DNetReplicationOp.Spawn));
        Assert.That(destination[0].Generation, Is.EqualTo(2));
        Assert.That(aoi.IsTracked(0, 100, out int generation), Is.True);
        Assert.That(generation, Is.EqualTo(2));

        aoi.InvalidateBaseline(0);
        Physics3DNetAoiDeltaBuildResult lost = aoi.BuildDelta(
            0,
            15,
            1,
            [MakeInterest(100, generation: 2)],
            destination);
        Assert.That(lost.RequiresFullSnapshot, Is.True);
    }

    [Test]
    public void AoiDelta_GenerationReplacement_EmitsDespawnThenSpawn_AndPreflightCountsBoth()
    {
        Physics3DNetConfig config = CreateConfig(aoiEntityCapacityPerClient: 4, playerCapacity: 2);
        var aoi = new Physics3DNetAoiDeltaBuilder(config);
        aoi.AcknowledgeBaseline(0, 1);
        var destination = new Physics3DNetSnapshotEntityWrite[2];

        aoi.BuildDelta(0, 3, 1, [MakeInterest(100, 1)], destination);

        Assert.Throws<Physics3DNetCapacityExceededException>(
            () => aoi.BuildDelta(0, 6, 1, [MakeInterest(100, 2)], destination.AsSpan(0, 1)));
        Assert.That(aoi.IsTracked(0, 100, out int generation), Is.True);
        Assert.That(generation, Is.EqualTo(1));

        Physics3DNetAoiDeltaBuildResult replaced = aoi.BuildDelta(0, 6, 1, [MakeInterest(100, 2)], destination);
        Assert.That(replaced.WrittenCount, Is.EqualTo(2));
        Assert.That(destination[0].Op, Is.EqualTo(Physics3DNetReplicationOp.Despawn));
        Assert.That(destination[0].Generation, Is.EqualTo(1));
        Assert.That(destination[1].Op, Is.EqualTo(Physics3DNetReplicationOp.Spawn));
        Assert.That(destination[1].Generation, Is.EqualTo(2));
        Assert.That(aoi.IsTracked(0, 100, out generation), Is.True);
        Assert.That(generation, Is.EqualTo(2));
    }

    [Test]
    public void AoiDelta_RejectsDuplicateOrUnsortedInterest_WithoutMutatingTrackedState()
    {
        Physics3DNetConfig config = CreateConfig(aoiEntityCapacityPerClient: 4, playerCapacity: 2);
        var aoi = new Physics3DNetAoiDeltaBuilder(config);
        aoi.AcknowledgeBaseline(0, 1);
        var destination = new Physics3DNetSnapshotEntityWrite[8];
        aoi.BuildDelta(0, 3, 1, [MakeInterest(10, 1)], destination);

        Assert.Throws<ArgumentException>(
            () => aoi.BuildDelta(
                0,
                6,
                1,
                [MakeInterest(20, 1), MakeInterest(10, 1)],
                destination));
        Assert.That(aoi.IsTracked(0, 10, out int generation), Is.True);
        Assert.That(generation, Is.EqualTo(1));
        Assert.That(aoi.IsTracked(0, 20, out _), Is.False);
    }

    [Test]
    public void AoiDelta_4096EntityMerge_IsLinearStableAndWarmedZeroAllocation()
    {
        const int entityCount = 4096;
        const int retainedCount = entityCount / 2;
        const int replacementId = 1024;
        Physics3DNetConfig config = CreateConfig(
            playerCapacity: 1,
            snapshotEntityCapacity: entityCount,
            aoiEntityCapacityPerClient: entityCount,
            clientCapacity: 1);
        var aoi = new Physics3DNetAoiDeltaBuilder(config);
        aoi.AcknowledgeBaseline(0, 1);

        var interest = new Physics3DNetAoiInterest[entityCount];
        var destination = new Physics3DNetSnapshotEntityWrite[entityCount * 2];
        for (int i = 0; i < entityCount; i++)
        {
            interest[i] = MakeInterest(i, generation: 1, positionX: i);
        }

        Physics3DNetAoiDeltaBuildResult initial = aoi.BuildDelta(0, 3, 1, interest, destination);
        Assert.That(initial.WrittenCount, Is.EqualTo(entityCount));
        Assert.That(aoi.GetLastBuildEntityIdComparisonCount(0), Is.Zero);

        for (int i = 0; i < retainedCount; i++)
        {
            int generation = i == replacementId ? 2 : 1;
            interest[i] = MakeInterest(i, generation, positionX: i + 10f);
        }

        for (int i = retainedCount; i < entityCount; i++)
        {
            interest[i] = MakeInterest(i + retainedCount, generation: 1, positionX: i + 10f);
        }

        Physics3DNetAoiDeltaBuildResult merged = aoi.BuildDelta(0, 6, 1, interest, destination);
        Assert.That(merged.WrittenCount, Is.EqualTo(entityCount + 1 + retainedCount));

        for (int i = 0; i < retainedCount; i++)
        {
            Assert.That(destination[i].Op, Is.EqualTo(Physics3DNetReplicationOp.Despawn));
            Assert.That(destination[i].NetworkEntityId, Is.EqualTo(i + retainedCount));
        }

        int replacementWriteIndex = retainedCount + replacementId;
        Assert.That(destination[replacementWriteIndex].Op, Is.EqualTo(Physics3DNetReplicationOp.Despawn));
        Assert.That(destination[replacementWriteIndex].NetworkEntityId, Is.EqualTo(replacementId));
        Assert.That(destination[replacementWriteIndex].Generation, Is.EqualTo(1));
        Assert.That(destination[replacementWriteIndex + 1].Op, Is.EqualTo(Physics3DNetReplicationOp.Spawn));
        Assert.That(destination[replacementWriteIndex + 1].NetworkEntityId, Is.EqualTo(replacementId));
        Assert.That(destination[replacementWriteIndex + 1].Generation, Is.EqualTo(2));

        int firstNewEntityWriteIndex = retainedCount + retainedCount + 1;
        Assert.That(destination[firstNewEntityWriteIndex].Op, Is.EqualTo(Physics3DNetReplicationOp.Spawn));
        Assert.That(destination[firstNewEntityWriteIndex].NetworkEntityId, Is.EqualTo(entityCount));
        Assert.That(destination[merged.WrittenCount - 1].NetworkEntityId, Is.EqualTo(entityCount + retainedCount - 1));
        Assert.That(aoi.IsTracked(0, retainedCount - 1, out int retainedGeneration), Is.True);
        Assert.That(retainedGeneration, Is.EqualTo(1));
        Assert.That(aoi.IsTracked(0, retainedCount, out _), Is.False);
        Assert.That(aoi.IsTracked(0, entityCount + retainedCount - 1, out int newGeneration), Is.True);
        Assert.That(newGeneration, Is.EqualTo(1));

        int comparisonCount = aoi.GetLastBuildEntityIdComparisonCount(0);
        Assert.That(comparisonCount, Is.LessThanOrEqualTo(2 * (entityCount + entityCount)));

        for (int i = 0; i < 8; i++)
        {
            aoi.BuildDelta(0, 9 + (i * 3), 1, interest, destination);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
        {
            aoi.BuildDelta(0, 33 + (i * 3), 1, interest, destination);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"4096-entity warmed AOI merge allocated {allocated} managed bytes.");
        Assert.That(aoi.GetLastBuildEntityIdComparisonCount(0), Is.LessThanOrEqualTo(entityCount * 4));
    }

    [Test]
    public void LocalPrediction_CorrectsOnlyBoundLocalEntity_AndRejectsRemoteRollback()
    {
        Physics3DNetConfig config = CreateConfig(localPredictionHistoryTicks: 8);
        var history = new Physics3DNetLocalPredictionHistory(config);
        history.BindLocalDriven(networkEntityId: 42, generation: 1, Physics3DNetLocalDrivenKind.Vehicle);

        for (long tick = 1; tick <= 5; tick++)
        {
            history.Record(
                new Physics3DNetPredictedPose(
                    tick,
                    new Vector3(tick * 10f, 0f, 0f),
                    Quaternion.Identity,
                    Vector3.UnitX,
                    Vector3.Zero),
                MakeInputFrame(tick, player: 1, sequence: (uint)tick));
        }

        Assert.Throws<InvalidOperationException>(() => history.RejectRemoteOrWorldRollback(99, 1));

        var poses = new Physics3DNetPredictedPose[8];
        var inputs = new Physics3DNetInputFrameView[8];
        Physics3DNetCorrectionReplayRange range = history.BeginCorrectionReplay(
            networkEntityId: 42,
            generation: 1,
            authoritativeConfirmedTick: 3,
            poses,
            inputs);
        Assert.That(range.FromTickInclusive, Is.EqualTo(4));
        Assert.That(range.ToTickInclusive, Is.EqualTo(5));
        Assert.That(range.FrameCount, Is.EqualTo(2));
        Assert.That(inputs[0].Tick, Is.EqualTo(4));
        Assert.That(inputs[1].Tick, Is.EqualTo(5));

        Assert.Throws<InvalidOperationException>(
            () => history.BeginCorrectionReplay(99, 1, 3, poses, inputs));
    }

    [Test]
    public void RemoteInterpolation_ReportsUnderflowAndOverflowExplicitly()
    {
        Physics3DNetConfig config = CreateConfig(remoteInterpolationHistoryTicks: 4);
        var buffer = new Physics3DNetRemoteInterpolationBuffer(config, remoteEntityCapacity: 4);
        buffer.Track(7, generation: 1);
        buffer.Push(7, 1, new Physics3DNetRemoteSample(10, new Vector3(0f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        buffer.Push(7, 1, new Physics3DNetRemoteSample(12, new Vector3(20f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));

        Physics3DNetInterpolationSample under = buffer.Sample(7, renderTick: 9f);
        Assert.That(under.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Underflow));

        Physics3DNetInterpolationSample mid = buffer.Sample(7, renderTick: 11f);
        Assert.That(mid.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Sampled));
        Assert.That(mid.PositionCm.X, Is.EqualTo(10f).Within(0.001f));

        Physics3DNetInterpolationSample over = buffer.Sample(7, renderTick: 13f);
        Assert.That(over.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Overflow));

        var temporal = Assert.Throws<Physics3DNetTemporalOrderException>(
            () => buffer.Push(7, 1, new Physics3DNetRemoteSample(11, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero)));
        Assert.That(temporal!.NewestTick, Is.EqualTo(12));
        Assert.That(temporal.AttemptedTick, Is.EqualTo(11));
    }

    [Test]
    public void RemoteInterpolation_TickJumpPurgesStaleWindowSamples()
    {
        Physics3DNetConfig config = CreateConfig(remoteInterpolationHistoryTicks: 4);
        var buffer = new Physics3DNetRemoteInterpolationBuffer(config, remoteEntityCapacity: 2);
        buffer.Track(1, 1);
        for (long tick = 1; tick <= 4; tick++)
        {
            buffer.Push(1, 1, new Physics3DNetRemoteSample(tick, new Vector3(tick, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        }

        buffer.Push(1, 1, new Physics3DNetRemoteSample(100, new Vector3(100f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Assert.That(buffer.GetSampleCount(1), Is.EqualTo(1));
        Assert.That(buffer.TryGetSampleTick(1, 1, out _), Is.False);
        Assert.That(buffer.TryGetSampleTick(1, 2, out _), Is.False);
        Assert.That(buffer.TryGetSampleTick(1, 3, out _), Is.False);
        Assert.That(buffer.TryGetSampleTick(1, 4, out _), Is.False);
        Assert.That(buffer.TryGetSampleTick(1, 100, out Physics3DNetRemoteSample kept), Is.True);
        Assert.That(kept.PositionCm.X, Is.EqualTo(100f));

        Physics3DNetInterpolationSample sample = buffer.Sample(1, 100f);
        Assert.That(sample.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Sampled));
        Assert.That(sample.LowerTick, Is.EqualTo(100));
    }

    [Test]
    public void PublicStateConstructors_RejectNonFiniteAndNonUnitQuaternion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Physics3DNetRemoteSample(1, new Vector3(float.NaN, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Physics3DNetRemoteSample(1, Vector3.Zero, default, Vector3.Zero, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Physics3DNetRemoteSample(
                1,
                Vector3.Zero,
                new Quaternion(2f, 0f, 0f, 0f),
                Vector3.Zero,
                Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Physics3DNetPredictedPose(0, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MakeSnapshotEntity(1, 1, (Physics3DNetReplicationOp)99, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MakeInput(tick: 0, player: 1, sequence: 1));
    }

    [Test]
    public void ReplayTimeline_ReportsFirstDivergentTick_AsDeterminismCheckNotWorldRollback()
    {
        Physics3DNetConfig config = CreateConfig(replayEventCapacity: 16);
        var timeline = new Physics3DNetReplayTimeline(config);
        timeline.RecordInputAccepted(1);
        timeline.RecordSnapshotPublished(3);
        timeline.RecordHashComparison(3, leftHash: 0x11, rightHash: 0x11);
        timeline.RecordHashComparison(6, leftHash: 0x22, rightHash: 0x33);
        timeline.RecordHashComparison(9, leftHash: 0x44, rightHash: 0x55);

        Physics3DNetReplayDivergence divergence = timeline.FindFirstDivergence();
        Assert.That(divergence.Found, Is.True);
        Assert.That(divergence.FirstDivergentTick, Is.EqualTo(6));
        Assert.That(divergence.LeftHash, Is.EqualTo(0x22ul));
        Assert.That(divergence.RightHash, Is.EqualTo(0x33ul));
    }

    [Test]
    public void CompatibilityGate_RejectsExactReplayTakeoverOnMismatch()
    {
        Physics3DNetConfig config = CreateConfig();
        var required = new Physics3DNetCompatibilityFingerprint(
            buildId: "build-a",
            configHash: Physics3DNetCompatibilityFingerprint.HashConfig(config),
            kernelId: "bepu-2.4",
            simdProfile: "avx2",
            workerCount: 4,
            scenarioId: "vertical-slice");
        var gate = new Physics3DNetCompatibilityGate(required);

        var mismatched = new Physics3DNetCompatibilityFingerprint(
            buildId: "build-b",
            configHash: required.ConfigHash,
            kernelId: "bepu-2.4",
            simdProfile: "avx2",
            workerCount: 4,
            scenarioId: "vertical-slice");

        var ex = Assert.Throws<Physics3DNetCompatibilityMismatchException>(() => gate.RequireMatch(mismatched));
        Assert.That(ex!.Expected, Is.EqualTo(required));
        Assert.That(ex.Actual, Is.EqualTo(mismatched));

        gate.RequireMatch(required);
    }

    [Test]
    public void WorldRestorePort_IsExplicitlyUnsupported_WithoutFallback_AndReportOwnsCopy()
    {
        var port = new Physics3DNetUnsupportedWorldRestorePort();
        Assert.That(port.IsSupported, Is.False);
        Assert.That(port.Coverage.AllSupported, Is.False);
        Assert.That(port.Coverage.Items.Length, Is.EqualTo(7));

        var mutable =
            new[]
            {
                new Physics3DNetWorldRestoreCoverageItem("bodies", supported: true, reason: "temp"),
                new Physics3DNetWorldRestoreCoverageItem("stable slots", supported: true, reason: "temp")
            };
        var report = new Physics3DNetWorldRestoreCoverageReport(mutable);
        Assert.That(report.AllSupported, Is.True);
        mutable[0] = new Physics3DNetWorldRestoreCoverageItem("bodies", supported: false, reason: "mutated");
        Assert.That(report.Items[0].Supported, Is.True);

        var ex = Assert.Throws<Physics3DNetWorldRestoreUnsupportedException>(
            () => port.RestoreExactWorldState(30));
        Assert.That(ex!.SnapshotTick, Is.EqualTo(30));
        Assert.That(ex.Message, Does.Contain("unsupported"));
    }

    [Test]
    public void Assembly_DoesNotReferenceBepuPackages()
    {
        Assembly assembly = typeof(Physics3DNetInputRing).Assembly;
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            Assert.That(
                reference.Name,
                Is.Not.EqualTo("BepuPhysics").And.Not.EqualTo("BepuUtilities"),
                $"Physics3DNet must not reference Bepu package '{reference.Name}'.");
        }
    }

    [Test]
    public void WarmedNetworkingHotPaths_HaveZeroManagedAllocationsOnCallingThread()
    {
        Physics3DNetConfig config = CreateConfig(
            playerCapacity: 150,
            snapshotEntityCapacity: 64,
            aoiEntityCapacityPerClient: 16,
            localPredictionHistoryTicks: 16,
            remoteInterpolationHistoryTicks: 8,
            replayEventCapacity: 256,
            inputHistoryTicksPerPlayer: 16,
            maxFutureInputTicks: 8);

        var life = new Physics3DNetTickLifecycle(config);
        var ring = new Physics3DNetInputRing(config, life);
        var store = new Physics3DNetAuthoritativeSnapshotStore(config);
        var aoi = new Physics3DNetAoiDeltaBuilder(config);
        var prediction = new Physics3DNetLocalPredictionHistory(config);
        var remote = new Physics3DNetRemoteInterpolationBuffer(config, remoteEntityCapacity: 16);
        var timeline = new Physics3DNetReplayTimeline(config);

        for (int i = 0; i < 150; i++)
        {
            ring.RegisterPlayer(i, generation: 1, playerSlot: i);
        }

        prediction.BindLocalDriven(0, 1, Physics3DNetLocalDrivenKind.Character);
        remote.Track(1, 1);
        remote.Track(2, 1);
        aoi.AcknowledgeBaseline(0, 1);

        Physics3DNetSnapshotEntityWrite[] entities = new Physics3DNetSnapshotEntityWrite[8];
        Physics3DNetAoiInterest[] interest = new Physics3DNetAoiInterest[4];
        Physics3DNetSnapshotEntityWrite[] delta = new Physics3DNetSnapshotEntityWrite[16];
        Physics3DNetPredictedPose[] poses = new Physics3DNetPredictedPose[16];
        Physics3DNetInputFrameView[] inputs = new Physics3DNetInputFrameView[16];
        int[] missingSlots = new int[150];

        void DriveTick(long tick, bool includeReorder, bool generationReplace)
        {
            if (includeReorder)
            {
                _ = ring.Submit(MakeInput(tick + 1, player: 0, sequence: (uint)(tick + 1), buttons: 0));
            }

            for (int p = 0; p < 150; p++)
            {
                _ = ring.Submit(MakeInput(tick, player: p, sequence: (uint)tick, buttons: (uint)p));
            }

            // Phase 1: missing-input gate then BeginExecute.
            if (!ring.TryBeginAuthoritativeExecute(tick, missingSlots, out _))
            {
                throw new InvalidOperationException($"Expected complete inputs for tick {tick}.");
            }

            life.Commit();
            // Phase 2: acknowledgement metadata only after lifecycle Commit.
            ring.AcknowledgeInputFramesAfterCommit(tick);
            timeline.RecordInputAccepted(tick);

            // Validate-only preflight for the next tick (incomplete for most players).
            _ = ring.TryValidateInputsForExecute(tick + 1, missingSlots, out _);

            if (life.IsSnapshotBoundary(tick))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    entities[i] = MakeSnapshotEntity(i, 1, Physics3DNetReplicationOp.Update, baselineId: 1, x: i + tick);
                }

                store.ReplaceAll(tick, baselineId: 1, entities);
                for (int i = 0; i < interest.Length; i++)
                {
                    int generation = generationReplace && i == 0 ? 2 : 1;
                    interest[i] = MakeInterest(i, generation, positionX: i + tick);
                }

                aoi.BuildDelta(0, tick, 1, interest, delta);
                life.PublishSnapshot(tick);
                timeline.RecordSnapshotPublished(tick);
                timeline.RecordHashComparison(tick, leftHash: (ulong)tick, rightHash: (ulong)tick);
            }

            prediction.Record(
                new Physics3DNetPredictedPose(tick, new Vector3(tick, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero),
                MakeInputFrame(tick, 0, (uint)tick));
            remote.Push(1, 1, new Physics3DNetRemoteSample(tick, new Vector3(tick, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
            _ = remote.Sample(1, tick - 0.5f);
        }

        for (long tick = 1; tick <= 32; tick++)
        {
            DriveTick(tick, includeReorder: true, generationReplace: false);
        }

        // Warm wrap/gap path on a dedicated remote entity so sequential entity 1 stays monotonic.
        remote.Push(2, 1, new Physics3DNetRemoteSample(1, new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        remote.Push(2, 1, new Physics3DNetRemoteSample(2, new Vector3(2f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        remote.Push(2, 1, new Physics3DNetRemoteSample(3, new Vector3(3f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        remote.Push(2, 1, new Physics3DNetRemoteSample(4, new Vector3(4f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        remote.Push(2, 1, new Physics3DNetRemoteSample(100, new Vector3(100f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        _ = remote.Sample(2, 100f);

        prediction.BeginCorrectionReplay(0, 1, 28, poses, inputs);
        _ = timeline.FindFirstDivergence();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (long tick = 33; tick <= 64; tick++)
        {
            DriveTick(tick, includeReorder: true, generationReplace: true);
        }

        remote.Push(2, 1, new Physics3DNetRemoteSample(200, new Vector3(200f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        _ = remote.Sample(2, 200f);
        _ = timeline.FindFirstDivergence();

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Physics3DNet warmed paths allocated {allocated} managed bytes.");
    }

    private static Physics3DNetInputSubmit MakeInput(
        long tick,
        int player,
        uint sequence,
        uint buttons = 7,
        int generation = 1)
    {
        return new Physics3DNetInputSubmit(
            tick,
            networkPlayerId: player,
            generation,
            sequence,
            new Physics3DNetQuantizedAxes2(100, -100),
            new Physics3DNetQuantizedAxes2(50, -50),
            buttons);
    }

    private static Physics3DNetInputFrameView MakeInputFrame(long tick, int player, uint sequence)
    {
        return new Physics3DNetInputFrameView(
            tick,
            player,
            generation: 1,
            sequence,
            new Physics3DNetQuantizedAxes2(1, 2),
            new Physics3DNetQuantizedAxes2(3, 4),
            buttons: 0,
            confirmationTick: 0);
    }

    private static Physics3DNetSnapshotEntityWrite MakeSnapshotEntity(
        int id,
        int generation,
        Physics3DNetReplicationOp op,
        long baselineId,
        float x = 0f)
    {
        return new Physics3DNetSnapshotEntityWrite(
            id,
            generation,
            op,
            baselineId,
            new Vector3(x, 0f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DBodyKind.Dynamic,
            Physics3DNetReplicationMode.RigidBody);
    }

    private static Physics3DNetAoiInterest MakeInterest(int id, int generation, float positionX = 0f)
    {
        return new Physics3DNetAoiInterest(
            id,
            generation,
            new Vector3(positionX, 0f, 0f),
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DBodyKind.Dynamic,
            Physics3DNetReplicationMode.Character);
    }
}
