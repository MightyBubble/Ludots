using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Networking.Commands;
using NUnit.Framework;
using Arch.Core;

namespace Ludots.Tests.GAS.Networking;

[TestFixture]
public sealed class NetworkCommandIngressTests
{
    [Test]
    public void Submit_UsesBoundSeatPlayerIdInsteadOfPayloadPlayerId()
    {
        using World world = World.Create();
        Entity actor = world.Create();
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 8);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);
        var orders = new[]
        {
            new Order
            {
                OrderTypeId = 1,
                PlayerId = 99,
                Actor = actor,
            },
        };

        NetworkCommandAdmissionOutcome outcome = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            orders);

        var dequeued = new Order[1];
        Assert.That(queue.TryDequeueBatch(dequeued, out int count), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(dequeued[0].PlayerId, Is.EqualTo(7));
            Assert.That(outcome.PlayerId, Is.EqualTo(7));
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(outcome.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            Assert.That(outcome.IsReplay, Is.False);
        });
    }

    [Test]
    public void Submit_EnforcesConfiguredPerSeatBatchRate()
    {
        using World world = World.Create();
        Entity actor = world.Create();
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 40);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);

        for (ulong sequence = 1; sequence <= 32; sequence++)
        {
            Order[] orders = CreateSingleOrder(actor);
            NetworkCommandAdmissionOutcome accepted = ingress.Submit(
                in seat,
                sequence,
                targetTick: 10,
                serverTick: 10,
                orders);
            Assert.That(accepted.Result, Is.EqualTo(OrderSubmitResult.Queued));
        }

        Order[] limitedOrders = CreateSingleOrder(actor);
        NetworkCommandAdmissionOutcome limited = ingress.Submit(
            in seat,
            clientBatchSequence: 33,
            targetTick: 10,
            serverTick: 10,
            limitedOrders);
        Order[] replayOrders = CreateSingleOrder(actor);
        NetworkCommandAdmissionOutcome replay = ingress.Submit(
            in seat,
            clientBatchSequence: 33,
            targetTick: 40,
            serverTick: 40,
            replayOrders);

        Assert.Multiple(() =>
        {
            Assert.That(limited.Result, Is.EqualTo(OrderSubmitResult.NetworkRateLimited));
            Assert.That(limited.Stage, Is.EqualTo(OrderAdmissionStage.NetworkIntake));
            Assert.That(replay.Result, Is.EqualTo(limited.Result));
            Assert.That(replay.IsReplay, Is.True);
            Assert.That(queue.Count, Is.EqualTo(32));
            Assert.That(results.Count, Is.EqualTo(34));
        });
    }

    [Test]
    public void Submit_ReplayReturnsOriginalOutcomeWithoutSecondQueueEntry()
    {
        using World world = World.Create();
        Entity actor = world.Create();
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 8);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);

        Order[] firstOrders = CreateSingleOrder(actor);
        NetworkCommandAdmissionOutcome first = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            firstOrders);
        Order[] replayOrders = CreateSingleOrder(actor);
        NetworkCommandAdmissionOutcome replay = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            replayOrders);

        Assert.Multiple(() =>
        {
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(replay.Result, Is.EqualTo(first.Result));
            Assert.That(replay.OrderId, Is.EqualTo(first.OrderId));
            Assert.That(replay.AdmissionBatchId, Is.EqualTo(first.AdmissionBatchId));
            Assert.That(replay.IsReplay, Is.True);
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(results.Count, Is.EqualTo(2));
        });
    }

    [TestCase(6, OrderSubmitResult.NetworkTargetTickExpired)]
    [TestCase(17, OrderSubmitResult.NetworkTargetTickTooFarAhead)]
    public void Submit_RejectsTargetTickOutsideConfiguredWindow(
        int targetTick,
        OrderSubmitResult expectedResult)
    {
        using World world = World.Create();
        Entity actor = world.Create();
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 4);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);
        Order[] orders = CreateSingleOrder(actor);

        NetworkCommandAdmissionOutcome outcome = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick,
            serverTick: 10,
            orders);
        NetworkCommandAdmissionOutcome replay = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick,
            serverTick: 11,
            CreateSingleOrder(actor));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Result, Is.EqualTo(expectedResult));
            Assert.That(outcome.Stage, Is.EqualTo(OrderAdmissionStage.NetworkIntake));
            Assert.That(replay.Result, Is.EqualTo(expectedResult));
            Assert.That(replay.IsReplay, Is.True);
            Assert.That(queue.Count, Is.Zero);
            Assert.That(results.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void Submit_RejectsWholeBatchAboveConfiguredActorLimit()
    {
        using World world = World.Create();
        var queue = new OrderQueue(capacity: 256);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 4);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);
        var orders = new Order[129];
        for (int i = 0; i < orders.Length; i++)
        {
            orders[i] = new Order
            {
                OrderTypeId = 1,
                Actor = world.Create(),
            };
        }

        NetworkCommandAdmissionOutcome outcome = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            orders);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.NetworkActorLimitExceeded));
            Assert.That(outcome.ActorCount, Is.EqualTo(129));
            Assert.That(queue.Count, Is.Zero);
        });
    }

    [Test]
    public void Submit_RejectsEmptyBatchExplicitly()
    {
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 2);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);

        NetworkCommandAdmissionOutcome outcome = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            Array.Empty<Order>());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.ValidationRejected));
            Assert.That(outcome.Stage, Is.EqualTo(OrderAdmissionStage.NetworkIntake));
            Assert.That(queue.Count, Is.Zero);
        });
    }

    [Test]
    public void Submit_WhenResultBufferIsFull_BackpressuresBeforeQueueOrSequenceMutation()
    {
        using World world = World.Create();
        Entity actor = world.Create();
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 1);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);
        var occupied = new NetworkCommandAdmissionOutcome(
            in seat,
            clientBatchSequence: 99,
            targetTick: 10,
            actorCount: 1,
            orderId: 99,
            admissionBatchId: 99,
            OrderSubmitResult.Queued,
            isReplay: false);
        Assert.That(results.TryWrite(in occupied), Is.True);
        Order[] firstAttemptOrders = CreateSingleOrder(actor);

        NetworkCommandAdmissionOutcome backpressured = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            firstAttemptOrders);

        Assert.Multiple(() =>
        {
            Assert.That(backpressured.Result, Is.EqualTo(OrderSubmitResult.NetworkAdmissionBackpressured));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(firstAttemptOrders[0].OrderId, Is.Zero);
        });

        Assert.That(results.TryRead(out _), Is.True);
        Order[] retryOrders = CreateSingleOrder(actor);
        NetworkCommandAdmissionOutcome retry = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            retryOrders);
        Assert.That(retry.Result, Is.EqualTo(OrderSubmitResult.Queued));
    }

    [Test]
    public void Submit_RejectsStaleConnectionSeatGeneration()
    {
        using World world = World.Create();
        Entity actor = world.Create();
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 4);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var currentSeat = new NetworkCommandSeat(slot: 0, generation: 2, playerId: 7);
        var staleSeat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in currentSeat, serverTick: 10);
        Order[] orders = CreateSingleOrder(actor);

        NetworkCommandAdmissionOutcome outcome = ingress.Submit(
            in staleSeat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            orders);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.NetworkInvalidConnectionSeat));
            Assert.That(outcome.Stage, Is.EqualTo(OrderAdmissionStage.NetworkIntake));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(results.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Submit_RejectsSequenceGapUntilMissingBatchArrives()
    {
        using World world = World.Create();
        Entity actor = world.Create();
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 8);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);

        NetworkCommandAdmissionOutcome gap = ingress.Submit(
            in seat,
            clientBatchSequence: 2,
            targetTick: 10,
            serverTick: 10,
            CreateSingleOrder(actor));
        NetworkCommandAdmissionOutcome first = ingress.Submit(
            in seat,
            clientBatchSequence: 1,
            targetTick: 10,
            serverTick: 10,
            CreateSingleOrder(actor));
        NetworkCommandAdmissionOutcome second = ingress.Submit(
            in seat,
            clientBatchSequence: 2,
            targetTick: 10,
            serverTick: 10,
            CreateSingleOrder(actor));

        Assert.Multiple(() =>
        {
            Assert.That(gap.Result, Is.EqualTo(OrderSubmitResult.NetworkSequenceGap));
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(second.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(queue.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void Submit_SteadyStateAllocatesZeroManagedBytes()
    {
        using World world = World.Create();
        Entity actor = world.Create();
        var queue = new OrderQueue(capacity: 128);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 1);
        NetworkCommandIngressConfig config = CreateConfig();
        var ingress = new NetworkCommandIngress(in config, queue, results);
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 7);
        ingress.BindSeat(in seat, serverTick: 10);
        Span<Order> submit = stackalloc Order[1];
        Span<Order> drain = stackalloc Order[1];

        for (ulong sequence = 1; sequence <= 16; sequence++)
        {
            SubmitAndDrain(ingress, in seat, actor, sequence, 9 + (int)sequence, submit, queue, results, drain);
        }

        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong sequence = 17; sequence <= 1016; sequence++)
        {
            SubmitAndDrain(ingress, in seat, actor, sequence, 9 + (int)sequence, submit, queue, results, drain);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    private static NetworkCommandIngressConfig CreateConfig()
    {
        return new NetworkCommandIngressConfig(
            seatCapacity: 2,
            simulationTickRateHz: 30,
            maxBatchesPerSecond: 32,
            burstBatchCapacity: 32,
            maxActorsPerBatch: 128,
            sequenceHistoryCapacity: 64,
            maxPastTargetTicks: 3,
            maxFutureTargetTicks: 6);
    }

    private static Order[] CreateSingleOrder(Entity actor)
    {
        return
        [
            new Order
            {
                OrderTypeId = 1,
                Actor = actor,
            },
        ];
    }

    private static void SubmitAndDrain(
        NetworkCommandIngress ingress,
        in NetworkCommandSeat seat,
        Entity actor,
        ulong sequence,
        int tick,
        Span<Order> submit,
        OrderQueue queue,
        NetworkCommandAdmissionResultBuffer results,
        Span<Order> drain)
    {
        submit[0] = new Order { OrderTypeId = 1, Actor = actor };
        NetworkCommandAdmissionOutcome outcome = ingress.Submit(
            in seat,
            sequence,
            tick,
            tick,
            submit);
        if (outcome.Result != OrderSubmitResult.Queued ||
            !queue.TryDequeueBatch(drain, out int count) ||
            count != 1 ||
            !results.TryRead(out _))
        {
            throw new InvalidOperationException("Network command hot-path test setup failed.");
        }
    }
}
