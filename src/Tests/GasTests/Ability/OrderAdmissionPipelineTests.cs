using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class OrderAdmissionPipelineTests
{
    [Test]
    public void ServerIntake_ReturnsAcceptedOrExplicitCapacityRejection()
    {
        using var world = World.Create();
        Entity actor = world.Create();
        var results = CreateAdmissionResults(capacity: 256);
        var queue = new OrderQueue(capacity: 64, results);
        var order = new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor };

        bool accepted = queue.TryEnqueueAssigned(ref order);

        Assert.That(accepted, Is.True);
        Assert.That(results.TryGet(order.OrderId, OrderAdmissionStage.GlobalIntake, out OrderAdmissionOutcome acceptedOutcome), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(acceptedOutcome.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            Assert.That(acceptedOutcome.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(acceptedOutcome.OrderId, Is.EqualTo(order.OrderId));
            Assert.That(acceptedOutcome.PlayerId, Is.EqualTo(7));
        });

        var filler = new Order { OrderTypeId = 1, Actor = actor };
        while (queue.TryEnqueue(in filler))
        {
        }

        var rejected = new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor };
        int countBefore = queue.Count;
        bool rejectedAccepted = queue.TryEnqueueAssigned(ref rejected);

        Assert.That(rejectedAccepted, Is.False);
        Assert.That(results.TryGet(rejected.OrderId, OrderAdmissionStage.GlobalIntake, out OrderAdmissionOutcome rejectedOutcome), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rejectedOutcome.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            Assert.That(rejectedOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(rejectedOutcome.PlayerId, Is.EqualTo(7));
            Assert.That(rejected.OrderId, Is.GreaterThan(0));
            Assert.That(queue.Count, Is.EqualTo(countBefore));
        });
    }

    [Test]
    public void EntityIntake_PublishesStartedAfterServerAcceptance()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        var results = new OrderAdmissionResultBuffer(capacity: 4, rejectionCapacity: 4);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(CreateOrderType(1, "test.move", canInterruptSelf: true));
        var batch = new[] { new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor } };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch)), Is.True);
        Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.GlobalIntake, out OrderAdmissionOutcome accepted), Is.True);

        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);
        RunAdmissionStep(results, system);

        Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.EntityIntake, out OrderAdmissionOutcome started), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(accepted.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            Assert.That(started.Stage, Is.EqualTo(OrderAdmissionStage.EntityIntake));
            Assert.That(started.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(started.OrderId, Is.EqualTo(accepted.OrderId));
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.True);
        });
    }

    [Test]
    public void EntityIntake_WhenResultBufferIsFull_RejectsWithoutWorldMutation()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        // Dual-generation needs room for GlobalIntake carry-forward + EntityIntake pair.
        // Capacity 1 leaves only the carried GlobalIntake slot, so EntityIntake capacity-fails.
        var results = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 1);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(CreateOrderType(1, "test.move", canInterruptSelf: true));

        var batch = new[] { new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor } };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch)), Is.True);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);

        RunAdmissionStep(results, system);

        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.Zero);
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(
                results.TryGet(batch[0].OrderId, OrderAdmissionStage.EntityIntake, out OrderAdmissionOutcome rejected),
                Is.True);
            Assert.That(rejected.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
        });
    }

    [Test]
    public void EntityIntake_AtomicBatchRejectsEveryMemberWithTypedOutcomes()
    {
        using var world = World.Create();
        Entity validActor = world.Create(OrderBuffer.CreateEmpty());
        Entity invalidActor = world.Create();
        var results = new OrderAdmissionResultBuffer(capacity: 4, rejectionCapacity: 4);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(CreateOrderType(1, "test.move", canInterruptSelf: true));
        var batch = new[]
        {
            new Order { OrderTypeId = 1, PlayerId = 7, Actor = validActor },
            new Order { OrderTypeId = 1, PlayerId = 7, Actor = invalidActor },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch)), Is.True);

        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);
        RunAdmissionStep(results, system);

        // Atomic shared-batch failure rewrites every accepted row to the concrete failure result.
        Assert.That(FindEntityOutcome(results, batch[0].OrderId, OrderSubmitResult.RejectedInvalidActor, out OrderAdmissionOutcome first), Is.True);
        Assert.That(FindEntityOutcome(results, batch[0].OrderId, OrderSubmitResult.RejectedInvalidActor, out OrderAdmissionOutcome second), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(validActor).HasActive, Is.False);
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
            Assert.That(second.Result, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
            Assert.That(first.OrderId, Is.EqualTo(second.OrderId));
            Assert.That(first.AdmissionBatchId, Is.Positive);
            Assert.That(CountEntityOutcomes(results, batch[0].OrderId, OrderSubmitResult.RejectedInvalidActor), Is.EqualTo(2));
        });
    }

    [Test]
    public void EntityIntake_QueuedOrderPromotesWithoutRepublishingAdmission()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        var results = new OrderAdmissionResultBuffer(capacity: 4, rejectionCapacity: 4);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(CreateOrderType(
            1,
            "test.move",
            canInterruptSelf: false,
            sameTypePolicy: SameTypePolicy.Queue,
            maxQueueSize: 2));

        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        var active = new Order { OrderId = 40, OrderTypeId = 1, Actor = actor };
        buffer.SetActiveDirect(in active, priority: 100);
        buffer.ActiveRuntimeInt0 = 3;

        var queued = new[]
        {
            new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(queued)), Is.True);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);

        RunAdmissionStep(results, system);
        Assert.That(results.TryGet(queued[0].OrderId, OrderAdmissionStage.EntityIntake, out OrderAdmissionOutcome waiting), Is.True);
        OrderBuffer waitingBuffer = world.Get<OrderBuffer>(actor);
        Assert.Multiple(() =>
        {
            Assert.That(waiting.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(waitingBuffer.ActiveRuntimeInt0, Is.EqualTo(3));
        });

        OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes);
        RunAdmissionStep(results, system);

        OrderBuffer startedBuffer = world.Get<OrderBuffer>(actor);
        Assert.Multiple(() =>
        {
            Assert.That(startedBuffer.ActiveOrder.Order.OrderId, Is.EqualTo(waiting.OrderId));
            Assert.That(startedBuffer.ActiveRuntimeInt0, Is.Zero);
            Assert.That(
                CountEntityOutcomes(results, queued[0].OrderId, OrderSubmitResult.Activated),
                Is.Zero,
                "Promotion is execution progress, not a second admission result.");
        });
    }

    [Test]
    public void EntityIntake_LocalSharedBatchStillPublishesTypedEntityOutcomes()
    {
        using var world = World.Create();
        Entity first = world.Create(OrderBuffer.CreateEmpty());
        Entity second = world.Create(OrderBuffer.CreateEmpty());
        // Shared-batch GlobalIntake + EntityIntake pairs require 2 slots per row.
        var results = new OrderAdmissionResultBuffer(capacity: 4, rejectionCapacity: 4);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(CreateOrderType(1, "test.local", allowQueuedMode: true));

        ref OrderBuffer firstBuffer = ref world.Get<OrderBuffer>(first);
        ref OrderBuffer secondBuffer = ref world.Get<OrderBuffer>(second);
        var firstActive = new Order { OrderId = 40, OrderTypeId = 1, Actor = first };
        var secondActive = new Order { OrderId = 41, OrderTypeId = 1, Actor = second };
        firstBuffer.SetActiveDirect(in firstActive, priority: 100);
        secondBuffer.SetActiveDirect(in secondActive, priority: 100);

        var localBatch = new[]
        {
            new Order { OrderTypeId = 1, Actor = first, SubmitMode = OrderSubmitMode.Queued },
            new Order { OrderTypeId = 1, Actor = second, SubmitMode = OrderSubmitMode.Queued },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(localBatch)), Is.True);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);

        RunAdmissionStep(results, system);
        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.Zero);
            Assert.That(world.Get<OrderBuffer>(first).QueuedCount, Is.EqualTo(1));
            Assert.That(world.Get<OrderBuffer>(second).QueuedCount, Is.EqualTo(1));
            Assert.That(FindEntityOutcome(results, localBatch[0].OrderId, OrderSubmitResult.Queued, out _), Is.True);
        });

        OrderSubmitter.NotifyOrderComplete(world, first, orderTypes);
        OrderSubmitter.NotifyOrderComplete(world, second, orderTypes);
        RunAdmissionStep(results, system);

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(first).ActiveOrder.Order.OrderId, Is.EqualTo(localBatch[0].OrderId));
            Assert.That(world.Get<OrderBuffer>(second).ActiveOrder.Order.OrderId, Is.EqualTo(localBatch[1].OrderId));
        });
    }

    [Test]
    public void EntityIntake_ExpiredQueuedOrderPublishesTerminalCancelledOnce()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        var active = new Order { OrderId = 40, OrderTypeId = 1, Actor = actor };
        buffer.SetActiveDirect(in active, priority: 100);

        var clock = new DiscreteClock();
        var results = new OrderAdmissionResultBuffer(capacity: 4, rejectionCapacity: 4);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(CreateOrderType(1, "test.expiring", allowQueuedMode: true, bufferWindowMs: 100));
        var batch = new[]
        {
            new Order { OrderTypeId = 1, Actor = actor, SubmitMode = OrderSubmitMode.Queued },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch)), Is.True);
        var system = new OrderBufferSystem(
            world,
            clock,
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue,
            stepRateHz: 30);

        RunAdmissionStep(results, system);
        Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.EntityIntake, out OrderAdmissionOutcome waiting), Is.True);
        Assert.That(waiting.Result, Is.EqualTo(OrderSubmitResult.Queued));

        clock.Advance(ClockDomainId.Step, 3);
        orderTypes.TerminalResults.Clear();
        RunAdmissionStep(results, system);
        Assert.Multiple(() =>
        {
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Cancelled));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(batch[0].OrderId));
            Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.Zero);
        });

        orderTypes.TerminalResults.Clear();
        RunAdmissionStep(results, system);
        Assert.That(orderTypes.TerminalResults.Count, Is.Zero);
    }

    [Test]
    public void EntityIntake_PersistentQueuedOrderSurvivesBufferWindowAndActivatesAfterCompletion()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        var active = new Order { OrderId = 40, OrderTypeId = 1, Actor = actor };
        buffer.SetActiveDirect(in active, priority: 100);

        var clock = new DiscreteClock();
        var results = new OrderAdmissionResultBuffer(capacity: 4, rejectionCapacity: 4);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(CreateOrderType(1, "test.persistent", allowQueuedMode: true, bufferWindowMs: 100));
        var batch = new[]
        {
            new Order { OrderTypeId = 1, Actor = actor, SubmitMode = OrderSubmitMode.PersistentQueued },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch)), Is.True);
        var system = new OrderBufferSystem(
            world,
            clock,
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue,
            stepRateHz: 30);

        RunAdmissionStep(results, system);
        Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.EntityIntake, out OrderAdmissionOutcome waiting), Is.True);
        Assert.That(waiting.Result, Is.EqualTo(OrderSubmitResult.Queued));

        clock.Advance(ClockDomainId.Step, 240);
        RunAdmissionStep(results, system);
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.EqualTo(1));
        });

        OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes);
        RunAdmissionStep(results, system);
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(batch[0].OrderId));
            Assert.That(
                CountEntityOutcomes(results, batch[0].OrderId, OrderSubmitResult.Activated),
                Is.Zero,
                "Persistent queue promotion is not a second admission.");
        });
    }

    [Test]
    public unsafe void EntityIntake_ClearQueueCancellationPublishesTerminalCancelled()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        var active = new Order { OrderId = 40, OrderTypeId = 1, Actor = actor };
        buffer.SetActiveDirect(in active, priority: 100);

        var results = new OrderAdmissionResultBuffer(capacity: 8, rejectionCapacity: 8);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(CreateOrderType(1, "test.waiting", allowQueuedMode: true));
        orderTypes.Register(CreateOrderType(2, "test.interrupt", clearQueueOnActivate: true, priority: 200));
        var rules = new OrderRuleRegistry();
        var interruptRules = new OrderRuleSet { InterruptsActiveCount = 1 };
        interruptRules.InterruptsActiveOrderTypeIds[0] = 1;
        rules.Register(2, in interruptRules);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            rules,
            results,
            queue);

        int firstWaitingOrderId = 0;
        int secondWaitingOrderId = 0;
        for (int i = 0; i < 2; i++)
        {
            var waitingBatch = new[]
            {
                new Order { OrderTypeId = 1, Actor = actor, SubmitMode = OrderSubmitMode.Queued },
            };
            Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(waitingBatch)), Is.True);
            if (i == 0)
            {
                firstWaitingOrderId = waitingBatch[0].OrderId;
            }
            else
            {
                secondWaitingOrderId = waitingBatch[0].OrderId;
            }
        }

        RunAdmissionStep(results, system);
        Assert.That(FindEntityOutcome(results, firstWaitingOrderId, OrderSubmitResult.Queued, out _), Is.True);
        Assert.That(FindEntityOutcome(results, secondWaitingOrderId, OrderSubmitResult.Queued, out _), Is.True);
        Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.EqualTo(2));

        var interruptBatch = new[]
        {
            new Order { OrderTypeId = 2, Actor = actor, SubmitMode = OrderSubmitMode.Immediate },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(interruptBatch)), Is.True);

        orderTypes.TerminalResults.Clear();
        RunAdmissionStep(results, system);
        Assert.Multiple(() =>
        {
            // Interrupt cancels the displaced active order plus both waiting queued orders.
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(3));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Cancelled));
            Assert.That(orderTypes.TerminalResults[1].State, Is.EqualTo(OrderTerminalState.Cancelled));
            Assert.That(orderTypes.TerminalResults[2].State, Is.EqualTo(OrderTerminalState.Cancelled));
            Assert.That(
                results.TryGet(interruptBatch[0].OrderId, OrderAdmissionStage.EntityIntake, out OrderAdmissionOutcome interruptActivated),
                Is.True);
            Assert.That(interruptActivated.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(interruptBatch[0].OrderId));
            Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.Zero);
        });
    }

    private static OrderAdmissionResultBuffer CreateAdmissionResults(int capacity = 64) =>
        new(capacity, capacity);

    private static OrderTypeRegistry CreateOrderTypes() =>
        new(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));

    private static OrderTypeConfig CreateOrderType(
        int orderTypeId,
        string key,
        bool canInterruptSelf = false,
        bool allowQueuedMode = true,
        bool clearQueueOnActivate = true,
        int priority = 100,
        int bufferWindowMs = 500,
        SameTypePolicy sameTypePolicy = SameTypePolicy.Queue,
        int maxQueueSize = 3) =>
        new()
        {
            Key = key,
            OrderTypeId = orderTypeId,
            Priority = priority,
            CanInterruptSelf = canInterruptSelf,
            AllowQueuedMode = allowQueuedMode,
            ClearQueueOnActivate = clearQueueOnActivate,
            BufferWindowMs = bufferWindowMs,
            SameTypePolicy = sameTypePolicy,
            MaxQueueSize = maxQueueSize,
            SpatialBlackboardKey = -1,
            EntityBlackboardKey = -1,
            IntArg0BlackboardKey = -1,
        };

    private static void RunAdmissionStep(OrderAdmissionResultBuffer results, OrderBufferSystem system)
    {
        results.BeginLogicStep();
        system.Update(0f);
        if (results.EntityIntakeOpen)
        {
            results.EndEntityIntake();
        }

        results.EndLogicStep();
    }

    private static bool FindEntityOutcome(
        OrderAdmissionResultBuffer results,
        int orderId,
        OrderSubmitResult expected,
        out OrderAdmissionOutcome outcome)
    {
        for (int i = 0; i < results.Count; i++)
        {
            ref readonly OrderAdmissionOutcome candidate = ref results[i];
            if (candidate.OrderId == orderId &&
                candidate.Stage == OrderAdmissionStage.EntityIntake &&
                candidate.Result == expected)
            {
                outcome = candidate;
                return true;
            }
        }

        outcome = default;
        return false;
    }

    private static int CountEntityOutcomes(
        OrderAdmissionResultBuffer results,
        int orderId,
        OrderSubmitResult expected)
    {
        int count = 0;
        for (int i = 0; i < results.Count; i++)
        {
            ref readonly OrderAdmissionOutcome candidate = ref results[i];
            if (candidate.OrderId == orderId &&
                candidate.Stage == OrderAdmissionStage.EntityIntake &&
                candidate.Result == expected)
            {
                count++;
            }
        }

        return count;
    }
}
