using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Networking.Commands;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkStagedCommandFeedbackStoreTests
{
    private static readonly NetworkCommandSeat Seat = new(slot: 0, generation: 1, playerId: 1);

    [Test]
    public void CompletedFeedback_IsRetiredWithoutExternalDrain()
    {
        var store = new NetworkStagedCommandFeedbackStore(capacity: 2, maxActorsPerCommandBatch: 1);

        Complete(store, sequence: 1);
        Complete(store, sequence: 2);
        Complete(store, sequence: 3);

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(1, out _), Is.False);
            Assert.That(store.TryGet(2, out _), Is.True);
            Assert.That(store.TryGet(3, out NetworkStagedCommandFeedback latest), Is.True);
            Assert.That(latest.IsTerminal, Is.True);
            Assert.That(store.Count, Is.EqualTo(2));
            Assert.That(store.RetiredCompletedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void InFlightCapacity_IsNeverEvicted()
    {
        var store = new NetworkStagedCommandFeedbackStore(capacity: 2, maxActorsPerCommandBatch: 1);

        Assert.That(store.TryObserve(Network(sequence: 1)), Is.True);
        Assert.That(store.TryObserve(Network(sequence: 2)), Is.True);
        Assert.That(store.TryObserve(Network(sequence: 3)), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(1, out _), Is.True);
            Assert.That(store.TryGet(2, out _), Is.True);
            Assert.That(store.TryGet(3, out _), Is.False);
            Assert.That(store.RetiredCompletedCount, Is.Zero);
        });
    }

    [Test]
    public void EntityReplay_IsIdempotent_ButConflictingReplayIsRejected()
    {
        var store = new NetworkStagedCommandFeedbackStore(capacity: 1, maxActorsPerCommandBatch: 2);
        Assert.That(store.TryObserve(Network(sequence: 1, actorCount: 2)), Is.True);
        Assert.That(store.TryObserve(Global(sequence: 1, actorCount: 2)), Is.True);

        NetworkCommandAdmissionOutcome first = Entity(sequence: 1, actorCount: 2, index: 0, OrderSubmitResult.Activated);
        Assert.That(store.TryObserve(in first), Is.True);
        Assert.That(store.TryObserve(in first), Is.True);

        NetworkCommandAdmissionOutcome conflict = Entity(sequence: 1, actorCount: 2, index: 0, OrderSubmitResult.Blocked);
        Assert.That(store.TryObserve(in conflict), Is.False);
        Assert.That(store.TryGet(1, out NetworkStagedCommandFeedback feedback), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(feedback.EntityOutcomeCount, Is.EqualTo(1));
            Assert.That(feedback.ActivatedCount, Is.EqualTo(1));
            Assert.That(feedback.RejectedCount, Is.Zero);
            Assert.That(feedback.IsTerminal, Is.False);
        });
    }

    [Test]
    public void SteadyStateObserveAndRetire_AllocatesZeroManagedBytes()
    {
        var store = new NetworkStagedCommandFeedbackStore(capacity: 4, maxActorsPerCommandBatch: 1);
        bool accepted = true;
        for (ulong sequence = 1; sequence <= 8; sequence++)
        {
            accepted &= CompleteWithoutAssertions(store, sequence);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong sequence = 9; sequence <= 1008; sequence++)
        {
            accepted &= CompleteWithoutAssertions(store, sequence);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(allocated, Is.Zero);
        });
    }

    private static void Complete(NetworkStagedCommandFeedbackStore store, ulong sequence)
    {
        Assert.That(store.TryObserve(Network(sequence)), Is.True);
        Assert.That(store.TryObserve(Global(sequence)), Is.True);
        Assert.That(store.TryObserve(Entity(sequence, actorCount: 1, index: 0, OrderSubmitResult.Activated)), Is.True);
    }

    private static bool CompleteWithoutAssertions(NetworkStagedCommandFeedbackStore store, ulong sequence)
    {
        NetworkCommandAdmissionOutcome network = Network(sequence);
        NetworkCommandAdmissionOutcome global = Global(sequence);
        NetworkCommandAdmissionOutcome entity = Entity(
            sequence,
            actorCount: 1,
            index: 0,
            OrderSubmitResult.Activated);
        return store.TryObserve(in network) &&
            store.TryObserve(in global) &&
            store.TryObserve(in entity);
    }

    private static NetworkCommandAdmissionOutcome Network(ulong sequence, int actorCount = 1) =>
        new(
            in Seat,
            sequence,
            targetTick: checked((int)sequence),
            actorCount,
            orderId: 0,
            admissionBatchId: 0,
            OrderSubmitResult.NetworkScheduled,
            isReplay: false);

    private static NetworkCommandAdmissionOutcome Global(ulong sequence, int actorCount = 1) =>
        new(
            in Seat,
            sequence,
            targetTick: checked((int)sequence),
            actorCount,
            orderId: checked((int)sequence),
            admissionBatchId: checked((int)sequence),
            admissionBatchIndex: 0,
            OrderAdmissionStage.GlobalIntake,
            OrderSubmitResult.Queued,
            isReplay: false);

    private static NetworkCommandAdmissionOutcome Entity(
        ulong sequence,
        int actorCount,
        ushort index,
        OrderSubmitResult result) =>
        new(
            in Seat,
            sequence,
            targetTick: checked((int)sequence),
            actorCount,
            orderId: checked((int)sequence),
            admissionBatchId: checked((int)sequence),
            admissionBatchIndex: index,
            OrderAdmissionStage.EntityIntake,
            result,
            isReplay: false);
}
