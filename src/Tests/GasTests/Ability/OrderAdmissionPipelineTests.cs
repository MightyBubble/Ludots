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
        var results = CreateAdmissionResults();
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
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.move",
            OrderTypeId = 1,
            Priority = 100,
            CanInterruptSelf = true,
        });
        var batch = new[] { new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor } };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch, OrderAdmissionSource.Network)), Is.True);
        Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.GlobalIntake, out OrderAdmissionOutcome accepted), Is.True);

        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);
        system.Update(0f);

        Assert.That(results.TryRead(out OrderAdmissionOutcome started), Is.True);
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
    public void EntityIntake_WhenResultBufferIsFull_AppliesBackpressureBeforeWorldMutation()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        var results = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 1);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.move",
            OrderTypeId = 1,
            Priority = 100,
            CanInterruptSelf = true,
        });

        var occupiedOrder = new Order { OrderId = 99, OrderTypeId = 1 };
        var occupied = new OrderAdmissionOutcome(
            in occupiedOrder,
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Activated);
        Assert.That(results.TryWrite(in occupied), Is.True);

        var batch = new[] { new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor } };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch, OrderAdmissionSource.Network)), Is.True);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);

        system.Update(0f);

        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(system.AdmissionBackpressureCount, Is.EqualTo(1));
        });

        Assert.That(results.TryRead(out _), Is.True);
        system.Update(0f);

        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.Zero);
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.True);
            Assert.That(results.TryRead(out OrderAdmissionOutcome started), Is.True);
            Assert.That(started.Result, Is.EqualTo(OrderSubmitResult.Activated));
        });
    }

    [Test]
    public void EntityIntake_AtomicBatchRejectsEveryMemberWithTypedOutcomes()
    {
        using var world = World.Create();
        Entity validActor = world.Create(OrderBuffer.CreateEmpty());
        Entity invalidActor = world.Create();
        var results = new OrderAdmissionResultBuffer(capacity: 2, rejectionCapacity: 2);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.move",
            OrderTypeId = 1,
            Priority = 100,
            CanInterruptSelf = true,
        });
        var batch = new[]
        {
            new Order { OrderTypeId = 1, PlayerId = 7, Actor = validActor },
            new Order { OrderTypeId = 1, PlayerId = 7, Actor = invalidActor },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch, OrderAdmissionSource.Network)), Is.True);

        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);
        system.Update(0f);

        Assert.That(results.TryRead(out OrderAdmissionOutcome validOutcome), Is.True);
        Assert.That(results.TryRead(out OrderAdmissionOutcome invalidOutcome), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(validActor).HasActive, Is.False);
            Assert.That(validOutcome.Result, Is.EqualTo(OrderSubmitResult.BatchRejected));
            Assert.That(invalidOutcome.Result, Is.EqualTo(OrderSubmitResult.InvalidEntity));
            Assert.That(validOutcome.OrderId, Is.EqualTo(invalidOutcome.OrderId));
            Assert.That(validOutcome.AdmissionBatchId, Is.Positive);
        });
    }

    [Test]
    public void EntityIntake_QueuedOrderPublishesActivationWithoutUsingGameplayRuntimeCursor()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        var results = new OrderAdmissionResultBuffer(capacity: 4, rejectionCapacity: 4);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.move",
            OrderTypeId = 1,
            Priority = 100,
            CanInterruptSelf = false,
            SameTypePolicy = SameTypePolicy.Queue,
            MaxQueueSize = 2,
        });

        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        var active = new Order { OrderId = 40, OrderTypeId = 1, Actor = actor };
        buffer.SetActiveDirect(in active, priority: 100);
        buffer.ActiveOrder.RuntimeInt0 = 3;

        var queued = new[]
        {
            new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(queued, OrderAdmissionSource.Network)), Is.True);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue);

        system.Update(0f);
        Assert.That(results.TryRead(out OrderAdmissionOutcome waiting), Is.True);
        OrderBuffer waitingBuffer = world.Get<OrderBuffer>(actor);
        Assert.Multiple(() =>
        {
            Assert.That(waiting.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(waitingBuffer.ActiveOrder.RuntimeInt0, Is.EqualTo(3));
        });

        OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes);
        system.Update(0f);

        Assert.That(results.TryRead(out OrderAdmissionOutcome started), Is.True);
        OrderBuffer startedBuffer = world.Get<OrderBuffer>(actor);
        Assert.Multiple(() =>
        {
            Assert.That(started.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(started.OrderId, Is.EqualTo(waiting.OrderId));
            Assert.That(startedBuffer.ActiveOrder.AdmissionActivationPublished, Is.EqualTo(1));
            Assert.That(startedBuffer.ActiveOrder.RuntimeInt0, Is.Zero);
        });
    }

    [Test]
    public void EntityIntake_LocalAtomicBatchDoesNotConsumeNetworkFeedbackCapacity()
    {
        using var world = World.Create();
        Entity first = world.Create(OrderBuffer.CreateEmpty());
        Entity second = world.Create(OrderBuffer.CreateEmpty());
        var results = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 1);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.local",
            OrderTypeId = 1,
            Priority = 100,
            AllowQueuedMode = true,
        });

        var occupiedOrder = new Order { OrderId = 99, OrderTypeId = 1 };
        var occupied = new OrderAdmissionOutcome(
            in occupiedOrder,
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Activated);
        Assert.That(results.TryWrite(in occupied), Is.True);

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

        system.Update(0f);
        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.Zero);
            Assert.That(world.Get<OrderBuffer>(first).QueuedCount, Is.EqualTo(1));
            Assert.That(world.Get<OrderBuffer>(second).QueuedCount, Is.EqualTo(1));
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(system.AdmissionBackpressureCount, Is.Zero);
        });

        OrderSubmitter.NotifyOrderComplete(world, first, orderTypes);
        OrderSubmitter.NotifyOrderComplete(world, second, orderTypes);
        system.Update(0f);

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(first).ActiveOrder.Order.OrderId, Is.EqualTo(localBatch[0].OrderId));
            Assert.That(world.Get<OrderBuffer>(second).ActiveOrder.Order.OrderId, Is.EqualTo(localBatch[1].OrderId));
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(system.AdmissionBackpressureCount, Is.Zero);
        });
    }

    [Test]
    public void EntityIntake_ExpiredQueuedOrderWaitsForResultCapacityAndPublishesOnce()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        var active = new Order { OrderId = 40, OrderTypeId = 1, Actor = actor };
        buffer.SetActiveDirect(in active, priority: 100);

        var clock = new DiscreteClock();
        var results = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 1);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.expiring",
            OrderTypeId = 1,
            Priority = 100,
            AllowQueuedMode = true,
            BufferWindowMs = 100,
        });
        var batch = new[]
        {
            new Order { OrderTypeId = 1, Actor = actor, SubmitMode = OrderSubmitMode.Queued },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch, OrderAdmissionSource.Network)), Is.True);
        var system = new OrderBufferSystem(
            world,
            clock,
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue,
            stepRateHz: 30);

        system.Update(0f);
        Assert.That(results.TryRead(out OrderAdmissionOutcome waiting), Is.True);
        Assert.That(waiting.Result, Is.EqualTo(OrderSubmitResult.Queued));

        var occupied = new OrderAdmissionOutcome(
            in active,
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Activated);
        Assert.That(results.TryWrite(in occupied), Is.True);
        clock.Advance(ClockDomainId.Step, 3);
        system.Update(0f);
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.EqualTo(1));
            Assert.That(system.AdmissionBackpressureCount, Is.EqualTo(1));
        });

        Assert.That(results.TryRead(out _), Is.True);
        system.Update(0f);
        Assert.That(results.TryRead(out OrderAdmissionOutcome expired), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(expired.Result, Is.EqualTo(OrderSubmitResult.Expired));
            Assert.That(expired.OrderId, Is.EqualTo(batch[0].OrderId));
            Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.Zero);
        });

        system.Update(0f);
        Assert.That(results.TryRead(out _), Is.False);
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
        var results = new OrderAdmissionResultBuffer(capacity: 2, rejectionCapacity: 2);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.persistent",
            OrderTypeId = 1,
            Priority = 100,
            AllowQueuedMode = true,
            BufferWindowMs = 100,
        });
        var batch = new[]
        {
            new Order { OrderTypeId = 1, Actor = actor, SubmitMode = OrderSubmitMode.PersistentQueued },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(batch, OrderAdmissionSource.Network)), Is.True);
        var system = new OrderBufferSystem(
            world,
            clock,
            orderTypes,
            new OrderRuleRegistry(),
            results,
            queue,
            stepRateHz: 30);

        system.Update(0f);
        Assert.That(results.TryRead(out OrderAdmissionOutcome waiting), Is.True);
        Assert.That(waiting.Result, Is.EqualTo(OrderSubmitResult.Queued));

        clock.Advance(ClockDomainId.Step, 240);
        system.Update(0f);
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.EqualTo(1));
            Assert.That(results.TryRead(out _), Is.False);
        });

        OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes);
        system.Update(0f);
        Assert.That(results.TryRead(out OrderAdmissionOutcome activated), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(activated.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(activated.OrderId, Is.EqualTo(batch[0].OrderId));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(batch[0].OrderId));
        });
    }

    [Test]
    public unsafe void EntityIntake_ClearQueueCancellationIsAtomicWithResultCapacity()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        var active = new Order { OrderId = 40, OrderTypeId = 1, Actor = actor };
        buffer.SetActiveDirect(in active, priority: 100);

        var results = new OrderAdmissionResultBuffer(capacity: 3, rejectionCapacity: 3);
        var queue = new OrderQueue(capacity: 64, results);
        var orderTypes = CreateOrderTypes();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.waiting",
            OrderTypeId = 1,
            Priority = 100,
            AllowQueuedMode = true,
        });
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.interrupt",
            OrderTypeId = 2,
            Priority = 200,
            ClearQueueOnActivate = true,
        });
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

        for (int i = 0; i < 2; i++)
        {
            var waitingBatch = new[]
            {
                new Order { OrderTypeId = 1, Actor = actor, SubmitMode = OrderSubmitMode.Queued },
            };
            Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(waitingBatch, OrderAdmissionSource.Network)), Is.True);
        }

        system.Update(0f);
        Assert.That(results.TryRead(out _), Is.True);
        Assert.That(results.TryRead(out _), Is.True);
        Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.EqualTo(2));

        var occupied = new OrderAdmissionOutcome(
            in active,
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Activated);
        Assert.That(results.TryWrite(in occupied), Is.True);
        var interruptBatch = new[]
        {
            new Order { OrderTypeId = 2, Actor = actor, SubmitMode = OrderSubmitMode.Immediate },
        };
        Assert.That(OrderSubmitResultSemantics.IsAccepted(queue.TryEnqueueSharedBatch(interruptBatch, OrderAdmissionSource.Network)), Is.True);

        system.Update(0f);
        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(active.OrderId));
            Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.EqualTo(2));
        });

        Assert.That(results.TryRead(out _), Is.True);
        system.Update(0f);
        Assert.That(results.TryRead(out OrderAdmissionOutcome firstCancelled), Is.True);
        Assert.That(results.TryRead(out OrderAdmissionOutcome secondCancelled), Is.True);
        Assert.That(results.TryRead(out OrderAdmissionOutcome interruptActivated), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(firstCancelled.Result, Is.EqualTo(OrderSubmitResult.Cancelled));
            Assert.That(secondCancelled.Result, Is.EqualTo(OrderSubmitResult.Cancelled));
            Assert.That(interruptActivated.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(interruptBatch[0].OrderId));
            Assert.That(world.Get<OrderBuffer>(actor).QueuedCount, Is.Zero);
        });
    }

    private static OrderAdmissionResultBuffer CreateAdmissionResults(int capacity = 64) =>
        new(capacity, capacity);

    private static OrderTypeRegistry CreateOrderTypes() =>
        new(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
}
