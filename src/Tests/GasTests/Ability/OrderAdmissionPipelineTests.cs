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
        var queue = new OrderQueue(capacity: 64);
        var order = new Order { OrderTypeId = 1, PlayerId = 7 };

        bool accepted = queue.TryEnqueueAssigned(ref order, out OrderAdmissionOutcome acceptedOutcome);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(acceptedOutcome.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            Assert.That(acceptedOutcome.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(acceptedOutcome.OrderId, Is.EqualTo(order.OrderId));
            Assert.That(acceptedOutcome.PlayerId, Is.EqualTo(7));
        });

        var filler = new Order { OrderTypeId = 1 };
        while (queue.TryEnqueue(in filler))
        {
        }

        var rejected = new Order { OrderTypeId = 1, PlayerId = 7 };
        int countBefore = queue.Count;
        bool rejectedAccepted = queue.TryEnqueueAssigned(ref rejected, out OrderAdmissionOutcome rejectedOutcome);

        Assert.Multiple(() =>
        {
            Assert.That(rejectedAccepted, Is.False);
            Assert.That(rejectedOutcome.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            Assert.That(rejectedOutcome.Result, Is.EqualTo(OrderSubmitResult.QueueFull));
            Assert.That(rejectedOutcome.PlayerId, Is.EqualTo(7));
            Assert.That(rejected.OrderId, Is.Zero);
            Assert.That(queue.Count, Is.EqualTo(countBefore));
        });
    }

    [Test]
    public void EntityIntake_PublishesStartedAfterServerAcceptance()
    {
        using var world = World.Create();
        Entity actor = world.Create(OrderBuffer.CreateEmpty());
        var queue = new OrderQueue(capacity: 64);
        var results = new OrderAdmissionResultBuffer(capacity: 4);
        var orderTypes = new OrderTypeRegistry();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.move",
            OrderTypeId = 1,
            Priority = 100,
            CanInterruptSelf = true,
        });
        var order = new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor };
        Assert.That(queue.TryEnqueueAssigned(ref order, out OrderAdmissionOutcome accepted), Is.True);

        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            queue,
            admissionResults: results);
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
        var queue = new OrderQueue(capacity: 64);
        var results = new OrderAdmissionResultBuffer(capacity: 1);
        var orderTypes = new OrderTypeRegistry();
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

        var order = new Order { OrderTypeId = 1, PlayerId = 7, Actor = actor };
        Assert.That(queue.TryEnqueueAssigned(ref order, out _), Is.True);
        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            queue,
            admissionResults: results);

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
        var queue = new OrderQueue(capacity: 64);
        var results = new OrderAdmissionResultBuffer(capacity: 2);
        var orderTypes = new OrderTypeRegistry();
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
        Assert.That(queue.TryEnqueueSharedBatch(batch), Is.True);

        var system = new OrderBufferSystem(
            world,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            queue,
            admissionResults: results);
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
}
