using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class OrderEntityReferenceContractTests
    {
        [Test]
        public void Order_ParameterlessConstructor_InitializesOptionalEntityReferencesToNull()
        {
            var order = new Order();

            Assert.Multiple(() =>
            {
                Assert.That(order.Target, Is.EqualTo(Entity.Null));
                Assert.That(order.TargetContext, Is.EqualTo(Entity.Null));
                Assert.That(order.CommandSource, Is.EqualTo(Entity.Null));
            });
        }

        [Test]
        public void OrderQueue_AllAdmissionEntries_RejectUninitializedEntityReferencesBeforeMutation()
        {
            using var world = World.Create();
            Entity actorA = world.Create();
            Entity actorB = world.Create();
            Entity commandSource = world.Create();
            var queue = new OrderQueue(capacity: 64, new OrderAdmissionResultBuffer(64, 64));

            Order uninitializedSlot = default;
            uninitializedSlot.OrderTypeId = 1;
            InvalidOperationException slotError = Assert.Throws<InvalidOperationException>(
                () => queue.TryEnqueue(in uninitializedSlot))!;
            Assert.That(slotError.Message, Does.Contain(nameof(Order.Actor)));
            Assert.That(slotError.Message, Does.Contain("default(Entity)"));

            var batch = new[]
            {
                new Order { OrderTypeId = 1, Actor = actorA, TargetContext = default },
            };
            InvalidOperationException batchError = Assert.Throws<InvalidOperationException>(
                () => queue.TryEnqueueBatch(batch))!;
            Assert.That(batchError.Message, Does.Contain(nameof(Order.TargetContext)));

            var sharedBatch = new[]
            {
                new Order { OrderTypeId = 1, Actor = actorA, CommandSource = default },
                new Order { OrderTypeId = 1, Actor = actorB },
            };
            InvalidOperationException sharedError = Assert.Throws<InvalidOperationException>(
                () => queue.TryEnqueueSharedBatch(sharedBatch))!;
            Assert.That(sharedError.Message, Does.Contain(nameof(Order.CommandSource)));

            var clusteredBatch = new[]
            {
                new Order
                {
                    OrderTypeId = 1,
                    Actor = actorA,
                    Target = default,
                    CommandSource = commandSource,
                },
            };
            InvalidOperationException clusteredError = Assert.Throws<InvalidOperationException>(
                () => queue.TryEnqueueClusteredBatch(clusteredBatch))!;
            Assert.That(clusteredError.Message, Does.Contain(nameof(Order.Target)));

            Assert.Multiple(() =>
            {
                Assert.That(queue.Count, Is.Zero);
                Assert.That(batch[0].OrderId, Is.Zero);
                Assert.That(sharedBatch[0].OrderId, Is.Zero);
                Assert.That(sharedBatch[1].OrderId, Is.Zero);
                Assert.That(clusteredBatch[0].OrderId, Is.Zero);
            });
        }

        [Test]
        public void OrderSubmitter_PreviewAndSubmit_RejectUninitializedOptionalReferences()
        {
            using var world = World.Create();
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            var registry = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            registry.Register(new OrderTypeConfig { OrderTypeId = 1 });

            var previewOrder = new Order
            {
                OrderTypeId = 1,
                Actor = actor,
                Target = default,
            };
            InvalidOperationException previewError = Assert.Throws<InvalidOperationException>(() =>
                OrderSubmitter.Preview(
                    world,
                    actor,
                    in previewOrder,
                    registry,
                    orderRuleRegistry: null,
                    currentStep: 0,
                    stepRateHz: 30))!;
            Assert.That(previewError.Message, Does.Contain(nameof(Order.Target)));

            var submitOrder = new Order
            {
                OrderTypeId = 1,
                Actor = actor,
                TargetContext = default,
            };
            InvalidOperationException submitError = Assert.Throws<InvalidOperationException>(() =>
                OrderSubmitter.Submit(
                    world,
                    actor,
                    in submitOrder,
                    registry,
                    orderRuleRegistry: null,
                    currentStep: 0,
                    stepRateHz: 30))!;
            Assert.That(submitError.Message, Does.Contain(nameof(Order.TargetContext)));
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
        }

        [Test]
        public void OrderSubmitter_TargetlessOrder_DoesNotWriteEntityBlackboardEntry()
        {
            using var world = World.Create();
            Entity actor = world.Create(OrderBuffer.CreateEmpty(), new BlackboardEntityBuffer());
            var registry = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            registry.Register(new OrderTypeConfig
            {
                OrderTypeId = 1,
                EntityBlackboardKey = OrderBlackboardKeys.Generic_TargetEntity,
            });
            var order = new Order
            {
                OrderTypeId = 1,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
            };

            OrderSubmitResult result = OrderSubmitter.Submit(
                world,
                actor,
                in order,
                registry,
                orderRuleRegistry: null,
                currentStep: 0,
                stepRateHz: 30);

            BlackboardEntityBuffer entities = world.Get<BlackboardEntityBuffer>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(OrderSubmitResult.Activated));
                Assert.That(entities.HasKey(OrderBlackboardKeys.Generic_TargetEntity), Is.False);
                Assert.That(entities.Count, Is.Zero);
            });
        }
    }
}
