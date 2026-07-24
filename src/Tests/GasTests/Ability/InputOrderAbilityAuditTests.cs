using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Physics2D.Components;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS.Features.InputRouting
{
    /// <summary>
    /// Unit tests covering the features introduced by the Input/Order/Ability audit:
    ///   - OrderBuffer PendingBuffer (SetPending, ClearPending, ExpirePending)
    ///   - GrantedSlotBuffer + AbilitySlotResolver
    ///   - AbilityToggleSpec registration
    ///   - GraphExecutor.ExecuteValidation
    /// </summary>
    [TestFixture]
    public class InputOrderAbilityAuditTests
    {
        // ════════════════════════════════════════════════════════════════════
        // Region: OrderBuffer / PendingBuffer
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OrderSpatialPayloadBuffer_ReadsMaximumPath_AndRejectsStaleHandle()
        {
            using var world = World.Create();
            Entity actor = world.Create(new OrderSpatialPayloadBuffer());
            var order = new Order { Actor = actor, OrderTypeId = 171 };
            var x = new int[OrderSpatial.MaxPoints];
            var y = new int[OrderSpatial.MaxPoints];
            for (int i = 0; i < x.Length; i++)
            {
                x[i] = i * 10;
                y[i] = i * -20;
            }

            OrderSpatialPayloadOps.SetPath(world, actor, ref order, x, y, x.Length);

            That(OrderWorldSpatialResolver.GetSpatialPointCount(world, in order), Is.EqualTo(OrderSpatial.MaxPoints));
            That(OrderWorldSpatialResolver.TryResolveMoveWaypoint(world, in order, 63, out Vector3 last), Is.True);
            That(last.X, Is.EqualTo(630));
            That(last.Z, Is.EqualTo(-1260));

            OrderSpatialPayloadHandle firstHandle = order.Args.Spatial.Payload;
            OrderSpatialPayloadOps.Release(world, in order);
            var stale = Throws<InvalidOperationException>(
                () => OrderWorldSpatialResolver.GetSpatialPointCount(world, in order));
            That(stale!.Message, Does.Contain("StalePayloadHandle"));

            order.Args.Spatial = default;
            OrderSpatialPayloadOps.SetPath(world, actor, ref order, x, y, x.Length);
            That(order.Args.Spatial.Payload.Generation, Is.Not.EqualTo(firstHandle.Generation));
        }

        [Test]
        public void OrderSpatialPayloadBuffer_FullCapacityFailsExplicitly()
        {
            using var world = World.Create();
            Entity actor = world.Create(new OrderSpatialPayloadBuffer());
            int[] x = { 0, 10, 20 };
            int[] y = { 0, 10, 20 };
            var orders = new Order[OrderSpatialPayloadBuffer.SlotCapacity];
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i] = new Order { Actor = actor, OrderTypeId = 171 };
                OrderSpatialPayloadOps.SetPath(world, actor, ref orders[i], x, y, x.Length);
            }

            var overflow = new Order { Actor = actor, OrderTypeId = 171 };
            var ex = Throws<InvalidOperationException>(
                () => OrderSpatialPayloadOps.SetPath(world, actor, ref overflow, x, y, x.Length));
            That(ex!.Message, Does.Contain("PayloadCapacity"));
        }

        [Test]
        public void PendingBuffer_SetPending_StoresOrderCorrectly()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 42, PlayerId = 1 };
            buffer.SetPending(in order, priority: 5, expireStep: 100, insertStep: 10);

            That(buffer.HasPending, Is.True);
            That(buffer.PendingOrder.Order.OrderTypeId, Is.EqualTo(42));
            That(buffer.PendingOrder.Priority, Is.EqualTo(5));
            That(buffer.PendingOrder.ExpireStep, Is.EqualTo(100));
            That(buffer.PendingOrder.InsertStep, Is.EqualTo(10));
        }

        [Test]
        public void PendingBuffer_ClearPending_ResetsSlot()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 42 };
            buffer.SetPending(in order, 5, 100, 10);
            That(buffer.HasPending, Is.True);

            buffer.ClearPending();
            That(buffer.HasPending, Is.False);
            That(buffer.PendingOrder.Order.OrderTypeId, Is.EqualTo(0));
        }

        [Test]
        public void PendingBuffer_ExpirePending_ExpiresWhenStepReached()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 7 };
            buffer.SetPending(in order, 5, expireStep: 50, insertStep: 10);

            // Before expiration: should not expire.
            bool expired = buffer.ExpirePending(currentStep: 49);
            That(expired, Is.False);
            That(buffer.HasPending, Is.True);

            // At expiration step: should expire.
            expired = buffer.ExpirePending(currentStep: 50);
            That(expired, Is.True);
            That(buffer.HasPending, Is.False);
        }

        [Test]
        public void PendingBuffer_ExpirePending_DoesNothingWhenEmpty()
        {
            var buffer = OrderBuffer.CreateEmpty();
            bool expired = buffer.ExpirePending(currentStep: 999);
            That(expired, Is.False);
            That(buffer.HasPending, Is.False);
        }

        [Test]
        public void PendingBuffer_ExpirePending_NoExpirationNegativeOne()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 1 };
            buffer.SetPending(in order, 5, expireStep: -1, insertStep: 0);

            // -1 = no expiration; should never expire
            bool expired = buffer.ExpirePending(currentStep: 999999);
            That(expired, Is.False);
            That(buffer.HasPending, Is.True);
        }

        [Test]
        public void PendingBuffer_SetPending_LastWriteWins()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order1 = new Order { OrderTypeId = 1 };
            var order2 = new Order { OrderTypeId = 2 };

            buffer.SetPending(in order1, 5, 100, 10);
            buffer.SetPending(in order2, 3, 200, 20);

            That(buffer.HasPending, Is.True);
            That(buffer.PendingOrder.Order.OrderTypeId, Is.EqualTo(2), "Last-write-wins: order2 should overwrite order1");
            That(buffer.PendingOrder.Priority, Is.EqualTo(3));
        }

        [Test]
        public void PendingBuffer_Clear_AlsoClearsPending()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 5 };
            buffer.SetPending(in order, 1, 50, 0);
            buffer.Enqueue(in order, 1, -1, 0);

            buffer.Clear();
            That(buffer.HasPending, Is.False, "Clear() should reset pending");
            That(buffer.HasQueued, Is.False, "Clear() should reset queue");
            That(buffer.HasActive, Is.False, "Clear() should reset active");
        }

        // ════════════════════════════════════════════════════════════════════
        // Region: GrantedSlotBuffer + AbilitySlotResolver
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OrderTypeRegistry_GetUnknownType_Throws()
        {
            var registry = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));

            var ex = Throws<KeyNotFoundException>(() => registry.Get(999));

            That(ex!.Message, Does.Contain("999"));
        }

        [Test]
        public void OrderQueue_RequiresExplicitAdmissionResultBuffer()
        {
            var constructor = typeof(OrderQueue).GetConstructor(new[]
            {
                typeof(int),
                typeof(OrderAdmissionResultBuffer),
            });

            That(constructor, Is.Not.Null);
            That(constructor!.GetParameters()[1].IsOptional, Is.False);
        }

        [Test]
        public void OrderBufferSystem_RequiresExplicitAdmissionResultBuffer()
        {
            var constructor = typeof(OrderBufferSystem).GetConstructors()[0];
            var parameters = constructor.GetParameters();
            int dependencyIndex = Array.FindIndex(
                parameters,
                parameter => parameter.ParameterType == typeof(OrderAdmissionResultBuffer));

            That(dependencyIndex, Is.GreaterThanOrEqualTo(0));
            That(parameters[dependencyIndex].IsOptional, Is.False);
        }

        [Test]
        public void OrderTypeRegistry_RequiresExplicitTerminalResultBuffer()
        {
            That(typeof(OrderTypeRegistry).GetConstructor(Type.EmptyTypes), Is.Null);
            That(typeof(OrderTypeRegistry).GetConstructor(new[] { typeof(OrderTerminalResultBuffer) }), Is.Not.Null);
        }

        [Test]
        public void OrderBufferSystem_RejectsAdmissionResultBufferDifferentFromIncomingQueue()
        {
            using var world = World.Create();
            var queueResults = new OrderAdmissionResultBuffer(4, 4);
            var systemResults = new OrderAdmissionResultBuffer(4, 4);
            var incoming = new OrderQueue(4, queueResults);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(4));

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                new OrderBufferSystem(
                    world,
                    new DiscreteClock(),
                    orderTypes,
                    new OrderRuleRegistry(),
                    systemResults,
                    incoming))!;

            That(error.Message, Does.Contain("AdmissionResultBufferMismatch"));
        }

        [Test]
        public void OrderQueue_InvalidOrderTypeId_PublishesTypedRejection()
        {
            var results = new OrderAdmissionResultBuffer(4, 4);
            var queue = new OrderQueue(64, results);
            var order = new Order { OrderTypeId = 0 };

            bool accepted = queue.TryEnqueue(in order);

            That(accepted, Is.False);
            ref readonly OrderAdmissionOutcome outcome = ref results[0];
            That(outcome.OrderId, Is.GreaterThan(0));
            That(outcome.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedInvalidOrderType));
        }

        [Test]
        public void OrderAdmissionResultBuffer_WhenFull_CountsOverflowWithoutAllocatingOrGrowing()
        {
            var results = new OrderAdmissionResultBuffer(1, 1);
            var first = new OrderAdmissionOutcome(1, 2, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.Queued);
            var second = new OrderAdmissionOutcome(2, 2, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.RejectedQueueFull);

            That(results.TryWrite(in first), Is.True);
            That(results.TryWrite(in second), Is.False);

            That(results.Capacity, Is.EqualTo(1));
            That(results.Count, Is.EqualTo(1));
            That(results.HighWatermark, Is.EqualTo(1));
            That(results.OverflowCount, Is.EqualTo(1));
        }

        [Test]
        public void OrderQueue_WhenAdmissionCapacityIsExhausted_DoesNotEnqueueAnUnobservableOrder()
        {
            var results = new OrderAdmissionResultBuffer(1, 1);
            var queue = new OrderQueue(64, results);
            var first = new Order { OrderTypeId = 2 };
            var second = new Order { OrderTypeId = 2 };

            That(queue.SubmitAssigned(ref first), Is.EqualTo(OrderSubmitResult.Queued));

            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                queue.SubmitAssigned(ref second))!;

            That(ex.Message, Does.StartWith(OrderAdmissionResultBuffer.CapacityExceededError));
            That(queue.Count, Is.EqualTo(1));
            That(second.OrderId, Is.GreaterThan(0));
            That(results.Count, Is.EqualTo(2));
            That(results[0].OrderId, Is.EqualTo(first.OrderId));
            That(results.TryGet(second.OrderId, OrderAdmissionStage.GlobalIntake, out var capacityOutcome), Is.True);
            That(capacityOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            That(results.ReservedCount, Is.Zero);
            That(results.OverflowCount, Is.EqualTo(1));

            results.BeginLogicStep();

            That(results.TryGet(first.OrderId, OrderAdmissionStage.GlobalIntake, out _), Is.True);
            That(results.TryGet(second.OrderId, OrderAdmissionStage.GlobalIntake, out capacityOutcome), Is.True);
            That(capacityOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
        }

        [Test]
        public void OrderQueue_WhenMultipleOrdersExceedAdmissionCapacity_PublishesEveryRejectedOrderId()
        {
            var results = new OrderAdmissionResultBuffer(1, 2);
            var queue = new OrderQueue(64, results);
            var first = new Order { OrderTypeId = 2 };
            var second = new Order { OrderTypeId = 2 };
            var third = new Order { OrderTypeId = 2 };

            That(queue.SubmitAssigned(ref first), Is.EqualTo(OrderSubmitResult.Queued));

            InvalidOperationException secondError = Throws<InvalidOperationException>(() =>
                queue.SubmitAssigned(ref second))!;
            InvalidOperationException thirdError = Throws<InvalidOperationException>(() =>
                queue.SubmitAssigned(ref third))!;

            That(secondError.Message, Does.StartWith(OrderAdmissionResultBuffer.CapacityExceededError));
            That(thirdError.Message, Does.StartWith(OrderAdmissionResultBuffer.CapacityExceededError));
            That(queue.Count, Is.EqualTo(1));
            That(second.OrderId, Is.GreaterThan(0));
            That(third.OrderId, Is.GreaterThan(second.OrderId));
            That(results.TryGet(second.OrderId, OrderAdmissionStage.GlobalIntake, out var secondOutcome), Is.True);
            That(secondOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            That(results.TryGet(third.OrderId, OrderAdmissionStage.GlobalIntake, out var thirdOutcome), Is.True);
            That(thirdOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            That(results.GetObservedCount(OrderSubmitResult.RejectedAdmissionCapacity), Is.EqualTo(2));
            That(results.Count, Is.EqualTo(3));
            That(results.OverflowCount, Is.EqualTo(2));

            results.BeginLogicStep();

            That(results.TryGet(second.OrderId, OrderAdmissionStage.GlobalIntake, out secondOutcome), Is.True);
            That(secondOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            That(results.TryGet(third.OrderId, OrderAdmissionStage.GlobalIntake, out thirdOutcome), Is.True);
            That(thirdOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
        }

        [Test]
        public void OrderQueue_WhenRejectionCapacityIsExhausted_TerminatesAdmissionBeforeAssigningFurtherIds()
        {
            var results = new OrderAdmissionResultBuffer(1, 1);
            var queue = new OrderQueue(64, results);
            var first = new Order { OrderTypeId = 2 };
            var second = new Order { OrderTypeId = 2 };
            var terminalTrigger = new Order { OrderTypeId = 2 };
            var afterFault = new Order { OrderTypeId = 2 };

            That(queue.SubmitAssigned(ref first), Is.EqualTo(OrderSubmitResult.Queued));
            InvalidOperationException capacityError = Throws<InvalidOperationException>(() =>
                queue.SubmitAssigned(ref second))!;
            InvalidOperationException terminalError = Throws<InvalidOperationException>(() =>
                queue.SubmitAssigned(ref terminalTrigger))!;
            InvalidOperationException repeatedError = Throws<InvalidOperationException>(() =>
                queue.SubmitAssigned(ref afterFault))!;

            That(capacityError.Message, Does.StartWith(OrderAdmissionResultBuffer.CapacityExceededError));
            That(terminalError.Message, Does.StartWith(OrderAdmissionResultBuffer.TerminalFaultedError));
            That(repeatedError.Message, Is.EqualTo(terminalError.Message));
            That(results.IsTerminalFaulted, Is.True);
            That(terminalTrigger.OrderId, Is.Zero);
            That(afterFault.OrderId, Is.Zero);
            That(queue.Count, Is.EqualTo(1));
            That(results.TryGet(second.OrderId, OrderAdmissionStage.GlobalIntake, out var rejection), Is.True);
            That(rejection.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));

            var explicitOrder = new Order { OrderId = 99, OrderTypeId = 2 };
            InvalidOperationException explicitError = Throws<InvalidOperationException>(() =>
                queue.SubmitAssigned(ref explicitOrder))!;
            That(explicitError.Message, Is.EqualTo(terminalError.Message));
            That(queue.Count, Is.EqualTo(1));

            results.BeginLogicStep();
            results.EndEntityIntake();
            results.EndLogicStep();
            var nextGenerationOrder = new Order { OrderTypeId = 2 };
            InvalidOperationException nextGenerationError = Throws<InvalidOperationException>(() =>
                queue.SubmitAssigned(ref nextGenerationOrder))!;
            That(nextGenerationError.Message, Is.EqualTo(terminalError.Message));
            That(nextGenerationOrder.OrderId, Is.Zero);
        }

        [Test]
        public void OrderBufferSystem_WhenEntityAdmissionCapacityIsExhausted_LeavesQueueAndActorStateUntouched()
        {
            using var world = World.Create();
            var results = new OrderAdmissionResultBuffer(1, 1);
            results.BeginLogicStep();
            var incoming = new OrderQueue(64, results);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 2, AllowQueuedMode = true });
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            var order = new Order
            {
                Actor = actor,
                OrderTypeId = 2,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            That(incoming.SubmitAssigned(ref order), Is.EqualTo(OrderSubmitResult.Queued));
            var intake = new OrderBufferSystem(
                world,
                new DiscreteClock(),
                orderTypes,
                new OrderRuleRegistry(),
                results,
                incoming);

            InvalidOperationException ex = Throws<InvalidOperationException>(() => intake.Update(0f))!;

            That(ex.Message, Does.StartWith(OrderAdmissionResultBuffer.CapacityExceededError));
            That(incoming.Count, Is.EqualTo(1));
            ref OrderBuffer actorOrders = ref world.Get<OrderBuffer>(actor);
            That(actorOrders.HasActive, Is.False);
            That(actorOrders.HasQueued, Is.False);
            That(actorOrders.HasPending, Is.False);
            That(results.Count, Is.EqualTo(2));
            That(results[0].OrderId, Is.EqualTo(order.OrderId));
            That(results[0].Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            That(results.TryGet(order.OrderId, OrderAdmissionStage.EntityIntake, out var capacityOutcome), Is.True);
            That(capacityOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            That(results.ReservedCount, Is.Zero);
            That(results.OverflowCount, Is.EqualTo(1));
        }

        [Test]
        public void OrderQueues_SharingAdmissionResults_AssignGloballyUniqueOrderIds()
        {
            var results = new OrderAdmissionResultBuffer(4, 4);
            var gameplayOrders = new OrderQueue(64, results);
            var responseOrders = new OrderQueue(64, results);
            var gameplayOrder = new Order { OrderTypeId = 2 };
            var responseOrder = new Order { OrderTypeId = 3 };

            That(gameplayOrders.SubmitAssigned(ref gameplayOrder), Is.EqualTo(OrderSubmitResult.Queued));
            That(responseOrders.SubmitAssigned(ref responseOrder), Is.EqualTo(OrderSubmitResult.Queued));

            That(responseOrder.OrderId, Is.Not.EqualTo(gameplayOrder.OrderId));
            That(results.TryGet(gameplayOrder.OrderId, OrderAdmissionStage.GlobalIntake, out var gameplayOutcome), Is.True);
            That(gameplayOutcome.OrderTypeId, Is.EqualTo(gameplayOrder.OrderTypeId));
            That(results.TryGet(responseOrder.OrderId, OrderAdmissionStage.GlobalIntake, out var responseOutcome), Is.True);
            That(responseOutcome.OrderTypeId, Is.EqualTo(responseOrder.OrderTypeId));
        }

        [Test]
        public void OrderBufferSystem_DirectSubmitReservesAdmissionBeforeChangingActorState()
        {
            using var world = World.Create();
            var results = new OrderAdmissionResultBuffer(1, 1);
            results.BeginLogicStep();
            var occupied = new OrderAdmissionOutcome(
                orderId: 1,
                orderTypeId: 2,
                OrderAdmissionStage.EntityIntake,
                OrderSubmitResult.Activated);
            That(results.TryWrite(in occupied), Is.True);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 2, AllowQueuedMode = true });
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            var intake = new OrderBufferSystem(
                world,
                new DiscreteClock(),
                orderTypes,
                new OrderRuleRegistry(),
                results);
            var order = new Order
            {
                OrderId = 2,
                Actor = actor,
                OrderTypeId = 2,
                SubmitMode = OrderSubmitMode.Immediate,
            };

            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                intake.SubmitOrder(actor, in order))!;

            That(ex.Message, Does.StartWith(OrderAdmissionResultBuffer.CapacityExceededError));
            ref OrderBuffer actorOrders = ref world.Get<OrderBuffer>(actor);
            That(actorOrders.HasActive, Is.False);
            That(actorOrders.HasQueued, Is.False);
            That(actorOrders.HasPending, Is.False);
            That(results.TryGet(order.OrderId, OrderAdmissionStage.EntityIntake, out var capacityOutcome), Is.True);
            That(capacityOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            That(results.ReservedCount, Is.Zero);
            That(results.OverflowCount, Is.EqualTo(1));
        }

        [TestCase(17)]
        [TestCase(32)]
        [TestCase(64)]
        public void OrderBufferSystem_WhenImmediatePathExceedsBlackboardCapacity_PreservesOldOrderAndReleasesPayload(int pointCount)
        {
            using var world = World.Create();
            const int orderTypeId = 20;
            const int spatialKey = 3;
            var results = new OrderAdmissionResultBuffer(8, 8);
            results.BeginLogicStep();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = orderTypeId,
                Priority = 100,
                CanInterruptSelf = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = spatialKey,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardSpatialBuffer(),
                new OrderSpatialPayloadBuffer());
            ref OrderBuffer orders = ref world.Get<OrderBuffer>(actor);
            var oldOrder = new Order { OrderId = 11, Actor = actor, OrderTypeId = orderTypeId };
            var queuedOrder = new Order { OrderId = 12, Actor = actor, OrderTypeId = orderTypeId };
            orders.SetActiveDirect(in oldOrder, priority: 100);
            Assert.That(orders.Enqueue(in queuedOrder, priority: 90, expireStep: -1, insertStep: 0), Is.True);
            ref BlackboardSpatialBuffer blackboard = ref world.Get<BlackboardSpatialBuffer>(actor);
            blackboard.SetPoint(spatialKey, new Vector3(123f, 0f, 456f));

            int[] pointXcm = new int[pointCount];
            int[] pointYcm = new int[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                pointXcm[i] = 1000 + i;
                pointYcm[i] = 2000 + i;
            }
            var newOrder = new Order
            {
                OrderId = 13,
                Actor = actor,
                OrderTypeId = orderTypeId,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            OrderSpatialPayloadOps.SetPath(world, actor, ref newOrder, pointXcm, pointYcm, pointCount);
            OrderSpatialPayloadHandle rejectedHandle = newOrder.Args.Spatial.Payload;
            var system = new OrderBufferSystem(
                world,
                new DiscreteClock(),
                orderTypes,
                new OrderRuleRegistry(),
                results);

            OrderSubmitResult result = system.SubmitOrder(actor, in newOrder);

            Assert.That(result, Is.EqualTo(OrderSubmitResult.RejectedBlackboardCapacity));
            Assert.That(results.TryGet(newOrder.OrderId, OrderAdmissionStage.EntityIntake, out var outcome), Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedBlackboardCapacity));
            Assert.That(orders.ActiveOrder.Order.OrderId, Is.EqualTo(oldOrder.OrderId));
            Assert.That(orders.QueuedCount, Is.EqualTo(1));
            Assert.That(orders.GetQueued(0).Order.OrderId, Is.EqualTo(queuedOrder.OrderId));
            Assert.That(blackboard.GetPointCount(spatialKey), Is.EqualTo(1));
            Assert.That(blackboard.TryGetPoint(spatialKey, out Vector3 oldPoint), Is.True);
            Assert.That(oldPoint, Is.EqualTo(new Vector3(123f, 0f, 456f)));
            Assert.That(orderTypes.TerminalResults.Count, Is.Zero);

            var replacementOrder = new Order { Actor = actor, OrderTypeId = orderTypeId };
            OrderSpatialPayloadOps.SetPath(world, actor, ref replacementOrder, pointXcm, pointYcm, pointCount);
            Assert.That(replacementOrder.Args.Spatial.Payload.Slot, Is.EqualTo(rejectedHandle.Slot));
            OrderSpatialPayloadOps.Release(world, in replacementOrder);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void OrderBufferSystem_WhenRequiredBlackboardComponentIsMissing_PreservesOldOrderAndQueue(int missingComponent)
        {
            using var world = World.Create();
            const int orderTypeId = 21;
            var results = new OrderAdmissionResultBuffer(8, 8);
            results.BeginLogicStep();
            var config = new OrderTypeConfig
            {
                OrderTypeId = orderTypeId,
                Priority = 100,
                CanInterruptSelf = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = missingComponent == 0 ? 3 : -1,
                EntityBlackboardKey = missingComponent == 1 ? 4 : -1,
                IntArg0BlackboardKey = missingComponent == 2 ? 5 : -1,
            };
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(config);
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            ref OrderBuffer orders = ref world.Get<OrderBuffer>(actor);
            var oldOrder = new Order { OrderId = 21, Actor = actor, OrderTypeId = orderTypeId };
            var queuedOrder = new Order { OrderId = 22, Actor = actor, OrderTypeId = orderTypeId };
            orders.SetActiveDirect(in oldOrder, priority: 100);
            Assert.That(orders.Enqueue(in queuedOrder, priority: 90, expireStep: -1, insertStep: 0), Is.True);
            Entity target = world.Create();
            var newOrder = new Order
            {
                OrderId = 23,
                Actor = actor,
                Target = target,
                OrderTypeId = orderTypeId,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs { I0 = 7 },
            };
            if (missingComponent == 0)
            {
                newOrder.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
                newOrder.Args.Spatial.Mode = OrderCollectionMode.Single;
                newOrder.Args.Spatial.WorldCm = new Vector3(10f, 0f, 20f);
            }
            var system = new OrderBufferSystem(
                world,
                new DiscreteClock(),
                orderTypes,
                new OrderRuleRegistry(),
                results);

            OrderSubmitResult result = system.SubmitOrder(actor, in newOrder);

            Assert.That(result, Is.EqualTo(OrderSubmitResult.RejectedMissingBlackboard));
            Assert.That(results.TryGet(newOrder.OrderId, OrderAdmissionStage.EntityIntake, out var outcome), Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedMissingBlackboard));
            Assert.That(orders.ActiveOrder.Order.OrderId, Is.EqualTo(oldOrder.OrderId));
            Assert.That(orders.QueuedCount, Is.EqualTo(1));
            Assert.That(orders.GetQueued(0).Order.OrderId, Is.EqualTo(queuedOrder.OrderId));
            Assert.That(orderTypes.TerminalResults.Count, Is.Zero);
        }

        [Test]
        public unsafe void OrderBufferSystem_WhenReplacingActiveSpatialKey_ReusesReleasedBlackboardEntry()
        {
            using var world = World.Create();
            const int activeOrderTypeId = 22;
            const int replacementOrderTypeId = 23;
            const int activeSpatialKey = 0;
            const int replacementSpatialKey = BlackboardSpatialBuffer.MAX_ENTRIES;
            var results = new OrderAdmissionResultBuffer(4, 4);
            results.BeginLogicStep();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = activeOrderTypeId,
                SpatialBlackboardKey = activeSpatialKey,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = replacementOrderTypeId,
                SpatialBlackboardKey = replacementSpatialKey,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            var rules = new OrderRuleSet { InterruptsActiveCount = 1 };
            rules.InterruptsActiveOrderTypeIds[0] = activeOrderTypeId;
            var orderRules = new OrderRuleRegistry();
            orderRules.Register(replacementOrderTypeId, in rules);
            Entity actor = world.Create(OrderBuffer.CreateEmpty(), new BlackboardSpatialBuffer());
            ref BlackboardSpatialBuffer blackboard = ref world.Get<BlackboardSpatialBuffer>(actor);
            for (int key = 0; key < BlackboardSpatialBuffer.MAX_ENTRIES; key++)
            {
                blackboard.SetPoint(key, new Vector3(key, 0f, key));
            }
            var activeOrder = new Order
            {
                OrderId = 31,
                Actor = actor,
                OrderTypeId = activeOrderTypeId,
            };
            world.Get<OrderBuffer>(actor).SetActiveDirect(in activeOrder, priority: 100);
            var replacementOrder = new Order
            {
                OrderId = 32,
                Actor = actor,
                OrderTypeId = replacementOrderTypeId,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(900f, 0f, 700f),
                    },
                },
            };
            var system = new OrderBufferSystem(
                world,
                new DiscreteClock(),
                orderTypes,
                orderRules,
                results);

            OrderSubmitResult result = system.SubmitOrder(actor, in replacementOrder);

            Assert.That(result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(results.TryGet(replacementOrder.OrderId, OrderAdmissionStage.EntityIntake, out var outcome), Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(blackboard.EntryCount, Is.EqualTo(BlackboardSpatialBuffer.MAX_ENTRIES));
            Assert.That(blackboard.HasKey(activeSpatialKey), Is.False);
            Assert.That(blackboard.TryGetPoint(replacementSpatialKey, out Vector3 replacementPoint), Is.True);
            Assert.That(replacementPoint, Is.EqualTo(new Vector3(900f, 0f, 700f)));
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(activeOrder.OrderId));
        }

        [Test]
        public void OrderAdmissionResults_PresentationGlobalAndNextStepEntityRemainReadable()
        {
            using var world = World.Create();
            var results = new OrderAdmissionResultBuffer(2, 2);
            var terminalResults = new OrderTerminalResultBuffer(2);
            var reset = new GasBudgetResetSystem(new GasBudget(), terminalResults, results);
            var end = new OrderAdmissionGenerationEndSystem(results);
            var incoming = new OrderQueue(64, results);
            var clock = new DiscreteClock();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 2, AllowQueuedMode = true });
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            var intake = new OrderBufferSystem(
                world,
                clock,
                orderTypes,
                new OrderRuleRegistry(),
                results,
                incoming);

            float dt = 0f;
            reset.Update(in dt);
            intake.Update(in dt);
            end.Update(in dt);

            var presentationOrder = new Order
            {
                Actor = actor,
                OrderTypeId = 2,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            That(incoming.TryEnqueueAssigned(ref presentationOrder), Is.True);
            That(results.Count, Is.EqualTo(1));

            reset.Update(in dt);
            That(results.Generation, Is.EqualTo(2));
            intake.Update(in dt);

            That(results.Count, Is.EqualTo(2));
            That(results[0].OrderId, Is.EqualTo(presentationOrder.OrderId));
            That(results[0].Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            That(results[1].OrderId, Is.EqualTo(presentationOrder.OrderId));
            That(results[1].Stage, Is.EqualTo(OrderAdmissionStage.EntityIntake));
            That(results.HighWatermark, Is.EqualTo(2));
            That(results.OverflowCount, Is.Zero);
        }

        [Test]
        public void OrderAdmissionResults_GlobalSubmittedAfterEntityIntake_RemainsReadableWithNextStepEntityOutcome()
        {
            using var world = World.Create();
            var results = new OrderAdmissionResultBuffer(4, 4);
            var reset = new GasBudgetResetSystem(new GasBudget(), orderAdmissionResults: results);
            var end = new OrderAdmissionGenerationEndSystem(results);
            var incoming = new OrderQueue(64, results);
            var clock = new DiscreteClock();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 2, AllowQueuedMode = true });
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            var intake = new OrderBufferSystem(
                world,
                clock,
                orderTypes,
                new OrderRuleRegistry(),
                results,
                incoming);
            float dt = 0f;

            reset.Update(in dt);
            intake.Update(in dt);

            var lateOrder = new Order
            {
                Actor = actor,
                OrderTypeId = 2,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            That(incoming.TryEnqueueAssigned(ref lateOrder), Is.True);
            end.Update(in dt);

            reset.Update(in dt);
            intake.Update(in dt);

            That(results.TryGet(lateOrder.OrderId, OrderAdmissionStage.GlobalIntake, out var global), Is.True);
            That(global.Result, Is.EqualTo(OrderSubmitResult.Queued));
            That(results.TryGet(lateOrder.OrderId, OrderAdmissionStage.EntityIntake, out var entity), Is.True);
            That(entity.Result, Is.EqualTo(OrderSubmitResult.Activated));
        }

        [Test]
        public void OrderAdmissionResults_GlobalIntakeDuringOpenWindow_SurvivesUntilEntityIntakeOnNextStep()
        {
            using var world = World.Create();
            var results = new OrderAdmissionResultBuffer(4, 4);
            var reset = new GasBudgetResetSystem(new GasBudget(), orderAdmissionResults: results);
            var end = new OrderAdmissionGenerationEndSystem(results);
            var incoming = new OrderQueue(64, results);
            var clock = new DiscreteClock();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 2, AllowQueuedMode = true });
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            var intake = new OrderBufferSystem(
                world,
                clock,
                orderTypes,
                new OrderRuleRegistry(),
                results,
                incoming,
                closeEntityIntakeOnUpdate: false);
            var intakeEnd = new OrderAdmissionEntityIntakeEndSystem(results);
            float dt = 0f;

            reset.Update(in dt);
            intake.Update(in dt);

            // Enqueue after OrderBufferSystem already drained this step, while entity intake is still open.
            var lateOrder = new Order
            {
                Actor = actor,
                OrderTypeId = 2,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            That(incoming.TryEnqueueAssigned(ref lateOrder), Is.True);
            That(results.TryGet(lateOrder.OrderId, OrderAdmissionStage.GlobalIntake, out _), Is.True);

            intakeEnd.Update(in dt);
            end.Update(in dt);

            reset.Update(in dt);
            That(results.TryGet(lateOrder.OrderId, OrderAdmissionStage.GlobalIntake, out var carried), Is.True);
            That(carried.Result, Is.EqualTo(OrderSubmitResult.Queued));
            intake.Update(in dt);

            That(results.TryGet(lateOrder.OrderId, OrderAdmissionStage.GlobalIntake, out _), Is.True);
            That(results.TryGet(lateOrder.OrderId, OrderAdmissionStage.EntityIntake, out var entityOutcome), Is.True);
            That(entityOutcome.Result, Is.EqualTo(OrderSubmitResult.Activated));
        }

        [Test]
        public void OrderBufferSystem_SharedBatch_RejectsBeforeMutatingActorsWhenBlackboardCapacityFails()
        {
            using var world = World.Create();
            const int orderTypeId = 40;
            const int spatialKey = 1;
            var results = new OrderAdmissionResultBuffer(16, 16);
            results.BeginLogicStep();
            var incoming = new OrderQueue(64, results);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = orderTypeId,
                Priority = 100,
                CanInterruptSelf = true,
                SpatialBlackboardKey = spatialKey,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            Entity first = world.Create(OrderBuffer.CreateEmpty(), new BlackboardSpatialBuffer(), new OrderSpatialPayloadBuffer());
            Entity second = world.Create(OrderBuffer.CreateEmpty(), new BlackboardSpatialBuffer(), new OrderSpatialPayloadBuffer());
            ref BlackboardSpatialBuffer secondBoard = ref world.Get<BlackboardSpatialBuffer>(second);
            for (int key = 0; key < BlackboardSpatialBuffer.MAX_ENTRIES; key++)
            {
                secondBoard.SetPoint(key == spatialKey ? key + BlackboardSpatialBuffer.MAX_ENTRIES : key, new Vector3(key, 0f, key));
            }

            int pointCount = BlackboardSpatialBuffer.MAX_POINTS_PER_ENTRY + 1;
            var pointX = new int[pointCount];
            var pointY = new int[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                pointX[i] = 100 + i;
                pointY[i] = 200 + i;
            }

            var batch = new[]
            {
                new Order { Actor = first, OrderTypeId = orderTypeId, SubmitMode = OrderSubmitMode.Immediate },
                new Order { Actor = second, OrderTypeId = orderTypeId, SubmitMode = OrderSubmitMode.Immediate },
            };
            OrderSpatialPayloadOps.SetPath(world, first, ref batch[0], pointX, pointY, 1);
            OrderSpatialPayloadOps.SetPath(world, second, ref batch[1], pointX, pointY, pointCount);
            That(incoming.TryEnqueueSharedBatch(batch), Is.EqualTo(OrderSubmitResult.Queued));

            var intake = new OrderBufferSystem(
                world,
                new DiscreteClock(),
                orderTypes,
                new OrderRuleRegistry(),
                results,
                incoming);
            intake.Update(0f);

            That(world.Get<OrderBuffer>(first).HasActive, Is.False);
            That(world.Get<OrderBuffer>(second).HasActive, Is.False);
            That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.EntityIntake, out var firstOutcome), Is.True);
            That(results.TryGet(batch[1].OrderId, OrderAdmissionStage.EntityIntake, out var secondOutcome), Is.True);
            That(firstOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedBlackboardCapacity));
            That(secondOutcome.Result, Is.EqualTo(OrderSubmitResult.RejectedBlackboardCapacity));
            That(incoming.Count, Is.Zero);
        }

        [Test]
        public void OrderSubmitter_QueueCleanupPaths_PublishCancelledTerminalOutcomes()
        {
            using var world = World.Create();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 2,
                Priority = 50,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8,
                BufferWindowMs = 1000,
                SameTypePolicy = SameTypePolicy.Replace,
            });
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
            var queued = new Order { OrderId = 101, Actor = actor, OrderTypeId = 2, SubmitMode = OrderSubmitMode.Queued };
            var pending = new Order { OrderId = 102, Actor = actor, OrderTypeId = 2 };
            That(buffer.Enqueue(in queued, priority: 50, expireStep: 1, insertStep: 0), Is.True);
            buffer.SetPending(in pending, priority: 50, expireStep: 1, insertStep: 0);

            OrderSubmitter.CancelAll(world, actor, orderTypes);

            That(orderTypes.TerminalResults.Count, Is.EqualTo(2));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(101).Or.EqualTo(102));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Cancelled));
            That(orderTypes.TerminalResults[1].OrderId, Is.EqualTo(101).Or.EqualTo(102));
            That(orderTypes.TerminalResults[1].State, Is.EqualTo(OrderTerminalState.Cancelled));
            That(buffer.HasQueued, Is.False);
            That(buffer.HasPending, Is.False);
        }

        [Test]
        public void OrderBufferSystem_ExpiredQueuedOrder_PublishesCancelledTerminalAndAllowsLaterOrders()
        {
            using var world = World.Create();
            var results = new OrderAdmissionResultBuffer(8, 8);
            results.BeginLogicStep();
            var clock = new DiscreteClock();
            clock.Advance(ClockDomainId.Step, 5);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 2, AllowQueuedMode = true, Priority = 10 });
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
            var expired = new Order { OrderId = 201, Actor = actor, OrderTypeId = 2 };
            That(buffer.Enqueue(in expired, priority: 10, expireStep: 1, insertStep: 0), Is.True);
            var intake = new OrderBufferSystem(
                world,
                clock,
                orderTypes,
                new OrderRuleRegistry(),
                results);

            intake.Update(0f);

            That(buffer.HasQueued, Is.False);
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(201));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Cancelled));
        }

        [Test]
        public void OrderBufferSystem_QueuedPromotionFailure_IsTypedAndClearsBadOrder()
        {
            using var world = World.Create();
            const int orderTypeId = 41;
            const int spatialKey = 2;
            var results = new OrderAdmissionResultBuffer(8, 8);
            results.BeginLogicStep();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = orderTypeId,
                Priority = 20,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 4,
                SpatialBlackboardKey = spatialKey,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
            var badQueued = new Order
            {
                OrderId = 301,
                Actor = actor,
                OrderTypeId = orderTypeId,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(1f, 0f, 2f),
                    },
                },
            };
            That(buffer.Enqueue(in badQueued, priority: 20, expireStep: -1, insertStep: 0), Is.True);
            var intake = new OrderBufferSystem(
                world,
                new DiscreteClock(),
                orderTypes,
                new OrderRuleRegistry(),
                results,
                closeEntityIntakeOnUpdate: false);

            Assert.DoesNotThrow(() => intake.Update(0f));

            That(buffer.HasActive, Is.False);
            That(buffer.HasQueued, Is.False);
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(301));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Failed));
            That(orderTypes.TerminalResults[0].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionMissingBlackboard));
            That(results.TryGet(301, OrderAdmissionStage.EntityIntake, out var outcome), Is.True);
            That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedMissingBlackboard));

            world.Add(actor, new BlackboardSpatialBuffer());
            var good = new Order
            {
                OrderId = 302,
                Actor = actor,
                OrderTypeId = orderTypeId,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = badQueued.Args,
            };
            That(intake.SubmitOrder(actor, in good), Is.EqualTo(OrderSubmitResult.Activated));
            ref OrderBuffer bufferAfter = ref world.Get<OrderBuffer>(actor);
            That(bufferAfter.HasActive, Is.True);
            That(bufferAfter.ActiveOrder.Order.OrderId, Is.EqualTo(302));
        }

        [Test]
        public void OrderAdmissionResults_EntityIntakeAfterCutoff_FailsBeforeChangingActorState()
        {
            using var world = World.Create();
            var results = new OrderAdmissionResultBuffer(4, 4);
            results.BeginLogicStep();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 2, AllowQueuedMode = true });
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            var intake = new OrderBufferSystem(
                world,
                new DiscreteClock(),
                orderTypes,
                new OrderRuleRegistry(),
                results);
            intake.Update(0f);
            var order = new Order
            {
                OrderId = 91,
                Actor = actor,
                OrderTypeId = 2,
                SubmitMode = OrderSubmitMode.Immediate,
            };

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                intake.SubmitOrder(actor, in order))!;

            That(error.Message, Is.EqualTo(OrderAdmissionResultBuffer.EntityIntakeClosedError));
            ref OrderBuffer actorOrders = ref world.Get<OrderBuffer>(actor);
            That(actorOrders.HasActive, Is.False);
            That(actorOrders.HasQueued, Is.False);
            That(actorOrders.HasPending, Is.False);
            That(results.TryGet(order.OrderId, OrderAdmissionStage.EntityIntake, out _), Is.False);
        }

        [Test]
        public void OrderQueue_BatchAdmission_IsAtomicWhenCapacityIsInsufficient()
        {
            var results = new OrderAdmissionResultBuffer(16, 16);
            var queue = new OrderQueue(capacity: 4, results);
            var seed = new Order { OrderTypeId = 1 };
            for (int i = 0; i < 3; i++)
            {
                Assert.That(queue.TryEnqueue(in seed), Is.True);
            }

            var batch = new[]
            {
                new Order { OrderTypeId = 1 },
                new Order { OrderTypeId = 1 },
            };

            Assert.That(queue.TryEnqueueBatch(batch), Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(queue.Count, Is.EqualTo(3));
            Assert.That(batch[0].OrderId, Is.GreaterThan(0));
            Assert.That(batch[1].OrderId, Is.GreaterThan(batch[0].OrderId));
            Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.GlobalIntake, out var first), Is.True);
            Assert.That(results.TryGet(batch[1].OrderId, OrderAdmissionStage.GlobalIntake, out var second), Is.True);
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(second.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
        }

        [Test]
        public void OrderQueue_BatchAdmission_PublishesEveryAssignedIdWhenAdmissionCapacityIsInsufficient()
        {
            var results = new OrderAdmissionResultBuffer(2, 2);
            var queue = new OrderQueue(capacity: 1, results);
            var seed = new Order { OrderTypeId = 1 };
            Assert.That(queue.TryEnqueue(in seed), Is.True);
            var batch = new[]
            {
                new Order { OrderTypeId = 1 },
                new Order { OrderTypeId = 1 },
            };

            Assert.That(queue.TryEnqueueBatch(batch), Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(results.GetObservedCount(OrderSubmitResult.RejectedQueueFull), Is.EqualTo(0));
            Assert.That(results.GetObservedCount(OrderSubmitResult.RejectedAdmissionCapacity), Is.EqualTo(2));
            Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.GlobalIntake, out var first), Is.True);
            Assert.That(results.TryGet(batch[1].OrderId, OrderAdmissionStage.GlobalIntake, out var second), Is.True);
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            Assert.That(second.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
        }

        [Test]
        public void OrderQueue_SharedBatch_AssignsOneQueueOwnedId()
        {
            using var world = World.Create();
            Entity firstActor = world.Create();
            Entity secondActor = world.Create();
            var queue = new OrderQueue(capacity: 64, new OrderAdmissionResultBuffer(64, 64));
            var batch = new[]
            {
                new Order { OrderTypeId = 1, Actor = firstActor },
                new Order { OrderTypeId = 1, Actor = secondActor },
            };

            Assert.That(queue.TryEnqueueSharedBatch(batch), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(batch[0].OrderId, Is.GreaterThan(0));
            Assert.That(batch[1].OrderId, Is.EqualTo(batch[0].OrderId));
            Assert.That(queue.TryDequeue(out Order first), Is.True);
            Assert.That(queue.TryDequeue(out Order second), Is.True);
            Assert.That(second.OrderId, Is.EqualTo(first.OrderId));
        }

        [Test]
        public void OrderQueue_SharedBatch_WhenCapacityIsInsufficient_PublishesOneRejectedSharedId()
        {
            using var world = World.Create();
            Entity firstActor = world.Create();
            Entity secondActor = world.Create();
            var results = new OrderAdmissionResultBuffer(8, 8);
            var queue = new OrderQueue(capacity: 1, results);
            var seed = new Order { OrderTypeId = 1 };
            Assert.That(queue.TryEnqueue(in seed), Is.True);
            var batch = new[]
            {
                new Order { OrderTypeId = 1, Actor = firstActor },
                new Order { OrderTypeId = 1, Actor = secondActor },
            };

            Assert.That(queue.TryEnqueueSharedBatch(batch), Is.EqualTo(OrderSubmitResult.RejectedQueueFull));

            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(batch[0].OrderId, Is.GreaterThan(0));
            Assert.That(batch[1].OrderId, Is.EqualTo(batch[0].OrderId));
            Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.GlobalIntake, out var outcome), Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
        }

        [Test]
        public void OrderQueue_SharedBatch_PublishesAssignedSharedIdWhenAdmissionCapacityIsInsufficient()
        {
            using var world = World.Create();
            Entity firstActor = world.Create();
            Entity secondActor = world.Create();
            var results = new OrderAdmissionResultBuffer(2, 2);
            var queue = new OrderQueue(capacity: 1, results);
            var seed = new Order { OrderTypeId = 1 };
            Assert.That(queue.TryEnqueue(in seed), Is.True);
            var batch = new[]
            {
                new Order { OrderTypeId = 1, Actor = firstActor },
                new Order { OrderTypeId = 1, Actor = secondActor },
            };

            Assert.That(queue.TryEnqueueSharedBatch(batch), Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));

            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(batch[0].OrderId, Is.GreaterThan(0));
            Assert.That(batch[1].OrderId, Is.EqualTo(batch[0].OrderId));
            Assert.That(results.GetObservedCount(OrderSubmitResult.RejectedAdmissionCapacity), Is.EqualTo(2));
            Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.GlobalIntake, out var outcome), Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
        }

        [Test]
        public void OrderQueue_ClusteredBatch_PublishesEveryAssignedIdWhenAdmissionCapacityIsInsufficient()
        {
            using var world = World.Create();
            Entity firstActor = world.Create();
            Entity secondActor = world.Create();
            Entity firstSource = world.Create();
            Entity secondSource = world.Create();
            var results = new OrderAdmissionResultBuffer(2, 2);
            var queue = new OrderQueue(capacity: 1, results);
            var seed = new Order { OrderTypeId = 1 };
            Assert.That(queue.TryEnqueue(in seed), Is.True);
            var batch = new[]
            {
                new Order { OrderTypeId = 1, Actor = firstActor, CommandSource = firstSource },
                new Order { OrderTypeId = 1, Actor = secondActor, CommandSource = secondSource },
            };

            Assert.That(queue.TryEnqueueClusteredBatch(batch), Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));

            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(batch[0].OrderId, Is.GreaterThan(0));
            Assert.That(batch[1].OrderId, Is.GreaterThan(batch[0].OrderId));
            Assert.That(results.GetObservedCount(OrderSubmitResult.RejectedAdmissionCapacity), Is.EqualTo(2));
            Assert.That(results.TryGet(batch[0].OrderId, OrderAdmissionStage.GlobalIntake, out var first), Is.True);
            Assert.That(results.TryGet(batch[1].OrderId, OrderAdmissionStage.GlobalIntake, out var second), Is.True);
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            Assert.That(second.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
        }

        [Test]
        public void PlanExecutor_UnregisteredOrderTypeId_Throws()
        {
            var queue = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            var spec = new ActionOrderSpec(orderTypeId: 42, submitMode: OrderSubmitMode.Immediate);
            var ints = new BlackboardIntBuffer();
            var entities = new BlackboardEntityBuffer();

            var ex = Throws<InvalidOperationException>(() =>
                PlanExecutor.TrySubmitOrder(
                    in spec,
                    ReadOnlySpan<ActionBinding>.Empty,
                    Entity.Null,
                    ref ints,
                    ref entities,
                    submitStep: 0,
                    queue,
                    orderTypes));

            That(ex!.Message, Does.Contain("unregistered order type id 42"));
        }

        [Test]
        public void GrantedSlotBuffer_Grant_OverridesSlot()
        {
            var granted = new GrantedSlotBuffer();
            granted.Grant(slotIndex: 2, abilityId: 99, sourceTagId: 10);

            That(granted.HasOverride(2), Is.True);
            var slot = granted.GetOverride(2);
            That(slot.AbilityId, Is.EqualTo(99));
        }

        [Test]
        public void GrantedSlotBuffer_Revoke_ClearsSlot()
        {
            var granted = new GrantedSlotBuffer();
            granted.Grant(0, 50, 10);
            That(granted.HasOverride(0), Is.True);

            granted.Revoke(0);
            That(granted.HasOverride(0), Is.False);
        }

        [Test]
        public void GrantedSlotBuffer_RevokeBySource_RemovesAllMatchingSlots()
        {
            var granted = new GrantedSlotBuffer();
            granted.Grant(0, 10, sourceTagId: 5);
            granted.Grant(1, 20, sourceTagId: 5);
            granted.Grant(2, 30, sourceTagId: 7);

            int revoked = granted.RevokeBySource(sourceTagId: 5);
            That(revoked, Is.EqualTo(2));
            That(granted.HasOverride(0), Is.False);
            That(granted.HasOverride(1), Is.False);
            That(granted.HasOverride(2), Is.True, "Source 7 should be unaffected");
        }

        [Test]
        public void GrantedSlotBuffer_OutOfBounds_Ignored()
        {
            var granted = new GrantedSlotBuffer();
            granted.Grant(-1, 1, 1);
            granted.Grant(GrantedSlotBuffer.CAPACITY, 1, 1);
            That(granted.HasOverride(-1), Is.False);
            That(granted.HasOverride(GrantedSlotBuffer.CAPACITY), Is.False);
        }

        [Test]
        public void AbilitySlotResolver_ReturnsGrantedWhenOverrideExists()
        {
            var baseSlots = new AbilityStateBuffer();
            baseSlots.AddAbility(100); // slot 0
            baseSlots.AddAbility(200); // slot 1

            var granted = new GrantedSlotBuffer();
            granted.Grant(0, abilityId: 999, sourceTagId: 1);
            var form = default(AbilityFormSlotBuffer);
            var itemGranted = default(ItemGrantedSlotBuffer);

            var resolved = AbilitySlotResolver.Resolve(in baseSlots, in form, hasForm: false, in itemGranted, hasItemGranted: false, in granted, hasGranted: true, slotIndex: 0);
            That(resolved.AbilityId, Is.EqualTo(999), "Should return granted override");

            var resolvedBase = AbilitySlotResolver.Resolve(in baseSlots, in form, hasForm: false, in itemGranted, hasItemGranted: false, in granted, hasGranted: true, slotIndex: 1);
            That(resolvedBase.AbilityId, Is.EqualTo(200), "Slot 1 has no override, should return base");
        }

        [Test]
        public void AbilitySlotResolver_IgnoresGrantedWhenHasGrantedIsFalse()
        {
            var baseSlots = new AbilityStateBuffer();
            baseSlots.AddAbility(100);

            var granted = new GrantedSlotBuffer();
            granted.Grant(0, abilityId: 999, sourceTagId: 1);
            var form = default(AbilityFormSlotBuffer);
            var itemGranted = default(ItemGrantedSlotBuffer);

            var resolved = AbilitySlotResolver.Resolve(in baseSlots, in form, hasForm: false, in itemGranted, hasItemGranted: false, in granted, hasGranted: false, slotIndex: 0);
            That(resolved.AbilityId, Is.EqualTo(100), "hasGranted=false should skip granted buffer");
        }

        // ════════════════════════════════════════════════════════════════════
        // Region: AbilityToggleSpec
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AbilityToggleSpec_RegisterAndRetrieve()
        {
            var registry = new AbilityDefinitionRegistry();

            var toggleSpec = new AbilityToggleSpec
            {
                ToggleTagId = 42
            };

            var def = new AbilityDefinition
            {
                HasToggleSpec = true,
                ToggleSpec = toggleSpec
            };

            registry.Register(1, in def);
            That(registry.TryGet(1, out var retrieved), Is.True);
            That(retrieved.HasToggleSpec, Is.True);
            That(retrieved.ToggleSpec.ToggleTagId, Is.EqualTo(42));
        }

        [Test]
        public void AbilityToggleSpec_NonToggle_HasToggleSpecIsFalse()
        {
            var registry = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition
            {
                HasToggleSpec = false
            };

            registry.Register(2, in def);
            That(registry.TryGet(2, out var retrieved), Is.True);
            That(retrieved.HasToggleSpec, Is.False);
        }

        [Test]
        public void AbilityExecLoader_CompileAbility_ParsesToggleSpecAndTargeting()
        {
            TagRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            EffectTemplateIdRegistry.Register("Effect.Toggle.GuardAura");
            EffectTemplateIdRegistry.Register("Effect.Guard.Impact");

            var obj = JsonNode.Parse(
                """
                {
                  "exec": {
                    "clockId": "FixedFrame",
                    "items": [
                      { "kind": "End", "tick": 0 }
                    ]
                  },
                  "toggleSpec": {
                    "toggleTag": "State.Guarding",
                    "activeEffects": ["Effect.Toggle.GuardAura"],
                    "deactivateExec": {
                      "clockId": "FixedFrame",
                      "items": [
                        { "kind": "End", "tick": 0 }
                      ]
                    }
                  },
                  "targeting": {
                    "castRangeCm": 620,
                    "impactEffect": "Effect.Guard.Impact"
                  }
                }
                """)!.AsObject();

            var def = Ludots.Core.Gameplay.GAS.Config.AbilityExecLoader.CompileAbility(obj, "Ability.Test.Guard", "GAS/abilities.json");

            That(def.HasToggleSpec, Is.True);
            That(def.ToggleSpec.ToggleTagId, Is.EqualTo(TagRegistry.Register("State.Guarding")));
            That(def.ToggleSpec.ActiveEffectCount, Is.EqualTo(1));
            unsafe
            {
                That(def.ToggleSpec.ActiveEffectTemplateIds[0], Is.EqualTo(EffectTemplateIdRegistry.GetId("Effect.Toggle.GuardAura")));
            }

            That(def.HasTargeting, Is.True);
            That(def.Targeting.CastRangeCm, Is.EqualTo(620f));
            That(def.Targeting.ImpactEffectTemplateId, Is.EqualTo(EffectTemplateIdRegistry.GetId("Effect.Guard.Impact")));
        }

        [Test]
        public void AbilityExecLoader_CompileAbility_RejectsRemovedIndicatorField()
        {
            var obj = JsonNode.Parse(
                """
                {
                  "indicator": {
                    "shape": "Ring",
                    "radius": 180
                  }
                }
                """)!.AsObject();

            var ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Gameplay.GAS.Config.AbilityExecLoader.CompileAbility(obj, "Ability.Test.LegacyIndicator", "GAS/abilities.json"));

            That(ex!.Message, Does.Contain("field 'indicator' is removed"));
        }

        [Test]
        public void AbilityExecLoader_CompileAbility_RejectsRemovedTargetingAimVisualField()
        {
            EffectTemplateIdRegistry.Clear();
            EffectTemplateIdRegistry.Register("Effect.Guard.Impact");
            var obj = JsonNode.Parse(
                """
                {
                  "targeting": {
                    "castRangeCm": 620,
                    "impactEffect": "Effect.Guard.Impact",
                    "aimVisual": {
                      "areaPerformerId": "performer.aim.area"
                    }
                  }
                }
                """)!.AsObject();

            var ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Gameplay.GAS.Config.AbilityExecLoader.CompileAbility(obj, "Ability.Test.LegacyAimVisual", "GAS/abilities.json"));

            That(ex!.Message, Does.Contain("field 'targeting.aimVisual' is removed"));
        }

        [Test]
        public void AbilityExecLoader_CompileAbility_RejectsRemovedTopLevelPreviewPerformerField()
        {
            EffectTemplateIdRegistry.Clear();
            EffectTemplateIdRegistry.Register("Effect.Guard.Impact");
            var obj = JsonNode.Parse(
                """
                {
                  "previewPerformerId": "performer.aim.preview",
                  "targeting": {
                    "castRangeCm": 620,
                    "impactEffect": "Effect.Guard.Impact"
                  }
                }
                """)!.AsObject();

            var ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Gameplay.GAS.Config.AbilityExecLoader.CompileAbility(obj, "Ability.Test.LegacyPreviewPerformer", "GAS/abilities.json"));

            That(ex!.Message, Does.Contain("field 'previewPerformerId' is removed"));
        }

        [Test]
        public void AbilityExecLoader_CompileAbility_ParsesActivationPrecondition()
        {
            GraphIdRegistry.Clear();
            int validationGraphId = GraphIdRegistry.Register("Graph.Ability.ManaEnough");

            var obj = JsonNode.Parse(
                """
                {
                  "exec": {
                    "clockId": "FixedFrame",
                    "items": [
                      { "kind": "End", "tick": 0 }
                    ]
                  },
                  "activationPrecondition": {
                    "validationGraph": "Graph.Ability.ManaEnough"
                  }
                }
                """)!.AsObject();

            var def = Ludots.Core.Gameplay.GAS.Config.AbilityExecLoader.CompileAbility(obj, "Ability.Test.ManaCheck", "GAS/abilities.json");

            That(def.HasActivationPrecondition, Is.True);
            That(def.ActivationPrecondition.ValidationGraphId, Is.EqualTo(validationGraphId));
        }

        [Test]
        public void AbilityDefinitionRegistry_RegisterFromEntity_CopiesActivationPrecondition()
        {
            using var world = World.Create();
            var template = world.Create(
                new AbilityTemplate(),
                new AbilityExecSpec(),
                new AbilityActivationPrecondition { ValidationGraphId = 77 });

            var defs = new AbilityDefinitionRegistry();
            defs.RegisterFromEntity(world, template, abilityId: 6010);

            That(defs.TryGet(6010, out var def), Is.True);
            That(def.HasActivationPrecondition, Is.True);
            That(def.ActivationPrecondition.ValidationGraphId, Is.EqualTo(77));
        }

        [Test]
        public void AbilityDefinitionRegistry_RegisterFromEntity_CopiesProgressionRequirements()
        {
            using var world = World.Create();
            var template = world.Create(
                new AbilityTemplate(),
                new AbilityExecSpec(),
                new AbilityProgressionRequirements
                {
                    UseRequirementId = 1201,
                    ShowRequirementId = 1202
                });

            var defs = new AbilityDefinitionRegistry();
            defs.RegisterFromEntity(world, template, abilityId: 6011);

            That(defs.TryGet(6011, out var def), Is.True);
            That(def.HasUseProgressionRequirement, Is.True);
            That(def.UseProgressionRequirementId, Is.EqualTo(1201));
            That(def.HasShowProgressionRequirement, Is.True);
            That(def.ShowProgressionRequirementId, Is.EqualTo(1202));
        }

        [Test]
        public void OrderBufferSystem_PromoteQueued_WritesBlackboard()
        {
            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer());
            var target = world.Create();

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 10,
                AllowQueuedMode = true,
                ClearQueueOnActivate = false,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex
            });

            var orderRules = new OrderRuleRegistry();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            admissionResults.BeginLogicStep();
            var system = new OrderBufferSystem(world, clock, orderTypes, orderRules, admissionResults);

            var order = new Order
            {
                Actor = actor,
                Target = target,
                OrderTypeId = 10,
                SubmitMode = OrderSubmitMode.Queued,
                Args = new OrderArgs { I0 = 2 }
            };

            var submit = OrderSubmitter.Submit(world, actor, in order, orderTypes, orderRules, currentStep: 0, stepRateHz: 30);
            That(submit, Is.EqualTo(OrderSubmitResult.Queued));

            system.Update(0);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            ref var bbE = ref world.Get<BlackboardEntityBuffer>(actor);

            That(buffer.HasActive, Is.True);
            That(buffer.HasQueued, Is.False);
            That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(10));

            That(bbI.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex), Is.True);
            That(slotIndex, Is.EqualTo(2));

            That(bbE.TryGet(OrderBlackboardKeys.Cast_TargetEntity, out Entity bbTarget), Is.True);
            That(bbTarget, Is.EqualTo(target));
        }

        [Test]
        public void OrderSubmitter_QueuedMode_UsesBufferWindowExpiry()
        {
            using var world = World.Create();
            var actor = world.Create(OrderBuffer.CreateEmpty(), new GameplayTagContainer());

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 11,
                AllowQueuedMode = true,
                BufferWindowMs = 300,
                ClearQueueOnActivate = false,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });

            var order = new Order
            {
                Actor = actor,
                OrderTypeId = 11,
                SubmitMode = OrderSubmitMode.Queued
            };

            var submit = OrderSubmitter.Submit(world, actor, in order, orderTypes, orderRuleRegistry: null, currentStep: 0, stepRateHz: 30);
            That(submit, Is.EqualTo(OrderSubmitResult.Queued));

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            That(buffer.QueuedCount, Is.EqualTo(1));
            That(buffer.GetQueued(0).ExpireStep, Is.EqualTo(9));
        }

        // ════════════════════════════════════════════════════════════════════
        [Test]
        public void AbilityExecSystem_ActiveCastOrderFromOrderBuffer_DoesNotRequireGameplayTag()
        {
            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(9001);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 7,
                Actor = actor,
                OrderTypeId = 100,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition();
            defs.Register(9001, in def);

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                4096,
                defs,
                castAbilityOrderTypeId: 100,
                orderTypeRegistry: new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity)),
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
            system.MaxWorkUnitsPerSlice = 1;

            bool completed = system.UpdateSlice(0f, int.MaxValue);

            That(completed, Is.False, "Budget should stop after Phase 1 so the spawned exec can be inspected.");
            That(world.Has<AbilityExecInstance>(actor), Is.True, "Cast ability should start from OrderBuffer active order without any gameplay order tag.");

            ref var exec = ref world.Get<AbilityExecInstance>(actor);
            That(exec.AbilityId, Is.EqualTo(9001));
            That(exec.OrderId, Is.EqualTo(7));
            That(exec.AbilitySlot, Is.EqualTo(0));
        }

        [Test]
        public void AbilityExecSystem_SourceDispatch_PreservesOriginalTargetAsContext()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9002;
            const int sourceEffectTemplateId = 7001;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer());
            var target = world.Create();

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 8,
                Actor = actor,
                Target = target,
                OrderTypeId = castAbilityOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);
            ref var bbE = ref world.Get<BlackboardEntityBuffer>(actor);
            bbE.Set(OrderBlackboardKeys.Cast_TargetEntity, target);

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(
                0,
                ExecItemKind.EffectSignal,
                tick: 0,
                templateId: sourceEffectTemplateId,
                payloadA: (int)ExecEffectDispatchTarget.Source);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition { ExecSpec = spec };
            defs.Register(abilityId, in def);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100
            });

            var effectRequests = new EffectRequestQueue();
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                effectRequests,
                4096,
                defs,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                orderTypeRegistry: orderTypes,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            system.Update(0f);

            That(effectRequests.Count, Is.EqualTo(1));
            That(effectRequests[0].Source, Is.EqualTo(actor));
            That(effectRequests[0].Target, Is.EqualTo(actor));
            That(effectRequests[0].TargetContext, Is.EqualTo(target));
        }

        [Test]
        public void AbilityExecSystem_ValidCast_PublishesCastCommittedPresentationEvent()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9003;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 10,
                Actor = actor,
                OrderTypeId = castAbilityOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition();
            defs.Register(abilityId, in def);

            var presentationEvents = new GasPresentationEventBuffer(8);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                4096,
                defs,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity)),
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
            system.MaxWorkUnitsPerSlice = 1;

            bool completed = system.UpdateSlice(0f, int.MaxValue);

            That(completed, Is.False, "Budget should stop after Phase 1 so activation events can be inspected.");
            var events = presentationEvents.Events;
            That(events.Length, Is.EqualTo(2));
            That(events[0].Kind, Is.EqualTo(GasPresentationEventKind.CastStarted));
            That(events[1].Kind, Is.EqualTo(GasPresentationEventKind.CastCommitted));
            That(events[1].Actor, Is.EqualTo(actor));
            That(events[1].AbilitySlot, Is.EqualTo(0));
            That(events[1].AbilityId, Is.EqualTo(abilityId));
        }

        [TestCase(true, OrderFailureReason.AbilitySlotOutOfRange, AbilityCastFailReason.InvalidSlot)]
        [TestCase(false, OrderFailureReason.AbilityDefinitionMissing, AbilityCastFailReason.InvalidSlot)]
        public void AbilityExecSystem_Phase2StateDrift_FailsOrderAndPublishesOneCastFailed(
            bool removeSlot,
            OrderFailureReason expectedOrderReason,
            AbilityCastFailReason expectedPresentationReason)
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9004;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            var order = new Order { OrderId = 12, Actor = actor, OrderTypeId = castAbilityOrderTypeId };
            ref var orders = ref world.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(in order, priority: 100);
            ref var blackboard = ref world.Get<BlackboardIntBuffer>(actor);
            blackboard.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition());
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                EntityBlackboardKey = -1,
                SpatialBlackboardKey = -1,
            });
            var presentationEvents = new GasPresentationEventBuffer(8);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                16,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()),
                maxWorkUnitsPerSlice: 1);

            That(system.UpdateSlice(0f, int.MaxValue), Is.False);
            That(world.Has<AbilityExecInstance>(actor), Is.True);
            if (removeSlot)
            {
                world.Get<AbilityStateBuffer>(actor).Count = 0;
            }
            else
            {
                definitions.Clear();
            }

            system.MaxWorkUnitsPerSlice = int.MaxValue;
            That(system.UpdateSlice(0f, int.MaxValue), Is.True);

            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(12));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Failed));
            That(orderTypes.TerminalResults[0].FailureReason, Is.EqualTo(expectedOrderReason));
            int failedCount = 0;
            int finishedCount = 0;
            foreach (ref readonly var evt in presentationEvents.Events)
            {
                if (evt.Kind == GasPresentationEventKind.CastFailed)
                {
                    failedCount++;
                    That(evt.FailReason, Is.EqualTo(expectedPresentationReason));
                }
                else if (evt.Kind == GasPresentationEventKind.CastFinished)
                {
                    finishedCount++;
                }
            }
            That(failedCount, Is.EqualTo(1));
            That(finishedCount, Is.Zero);
        }

        [Test]
        public void AbilityExecSystem_ToggleDeactivate_BypassesBlockedAnyCooldown_AndClearsTagCount()
        {
            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags());

            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9002;
            const int toggleTagId = 41;
            const int cooldownTagId = 42;

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 8,
                Actor = actor,
                OrderTypeId = castAbilityOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
            ref var tags = ref world.Get<GameplayTagContainer>(actor);
            ref var counts = ref world.Get<TagCountContainer>(actor);
            ref var dirty = ref world.Get<DirtyFlags>(actor);
            tagOps.AddTag(ref tags, ref counts, toggleTagId, ref dirty);
            tagOps.AddTag(ref tags, ref counts, cooldownTagId, ref dirty);

            var blockTags = new AbilityActivationBlockTags();
            blockTags.BlockedAny.AddTag(cooldownTagId);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition
            {
                HasActivationBlockTags = true,
                ActivationBlockTags = blockTags,
                HasToggleSpec = true,
                ToggleSpec = new AbilityToggleSpec
                {
                    ToggleTagId = toggleTagId
                }
            };
            defs.Register(abilityId, in def);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                EntityBlackboardKey = -1,
                SpatialBlackboardKey = -1,
            });

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                4096,
                defs,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                orderTypeRegistry: orderTypes,
                tagOps: tagOps);

            bool completed = system.UpdateSlice(0f, int.MaxValue);

            That(completed, Is.True);
            That(world.Get<GameplayTagContainer>(actor).HasTag(toggleTagId), Is.False, "Toggle should turn off even when reactivate cooldown is present.");
            That(world.Get<TagCountContainer>(actor).GetCount(toggleTagId), Is.EqualTo(0), "Toggle removal must clear TagCountContainer as well as the bitset.");
            That(world.Get<GameplayTagContainer>(actor).HasTag(cooldownTagId), Is.True, "Turning off a toggle should not remove the reactivation cooldown tag.");
            That(world.Get<TagCountContainer>(actor).GetCount(cooldownTagId), Is.EqualTo(1));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(8));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Completed));
        }

        [Test]
        public void AbilityExecSystem_ToggleDeactivateTimeline_PreservesOrderIdentityUntilCompletion()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9005;
            const int toggleTagId = 43;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);
            var order = new Order { OrderId = 13, Actor = actor, OrderTypeId = castAbilityOrderTypeId };
            ref var orders = ref world.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(in order, priority: 100);
            ref var blackboard = ref world.Get<BlackboardIntBuffer>(actor);
            blackboard.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
            ref var tags = ref world.Get<GameplayTagContainer>(actor);
            ref var counts = ref world.Get<TagCountContainer>(actor);
            ref var dirty = ref world.Get<DirtyFlags>(actor);
            tagOps.AddTag(ref tags, ref counts, toggleTagId, ref dirty);

            var deactivateSpec = default(AbilityExecSpec);
            deactivateSpec.ClockId = GasClockId.Step;
            deactivateSpec.SetItem(0, ExecItemKind.End, tick: 0);
            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                HasToggleSpec = true,
                ToggleSpec = new AbilityToggleSpec
                {
                    ToggleTagId = toggleTagId,
                    DeactivateExecSpec = deactivateSpec,
                }
            });
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                EntityBlackboardKey = -1,
                SpatialBlackboardKey = -1,
            });
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                16,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                orderTypeRegistry: orderTypes,
                tagOps: tagOps);

            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(13));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Completed));
        }

        [Test]
        public void AbilityExecSystem_ToggleActivate_DoesNotCompleteWhenActiveEffectQueueMissing()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9006;
            const int toggleTagId = 44;
            const int activeEffectTemplateId = 77001;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);
            var order = new Order { OrderId = 21, Actor = actor, OrderTypeId = castAbilityOrderTypeId };
            ref var orders = ref world.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(in order, priority: 100);
            ref var blackboard = ref world.Get<BlackboardIntBuffer>(actor);
            blackboard.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var activateSpec = default(AbilityExecSpec);
            activateSpec.ClockId = GasClockId.Step;
            activateSpec.SetItem(0, ExecItemKind.End, tick: 0);

            var toggleSpec = new AbilityToggleSpec
            {
                ToggleTagId = toggleTagId,
                ActiveEffectCount = 1,
            };
            unsafe
            {
                toggleSpec.ActiveEffectTemplateIds[0] = activeEffectTemplateId;
            }

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = activateSpec,
                HasToggleSpec = true,
                ToggleSpec = toggleSpec,
            });
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                EntityBlackboardKey = -1,
                SpatialBlackboardKey = -1,
            });
            var presentationEvents = new GasPresentationEventBuffer(capacity: 32);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                effectRequests: null,
                16,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes,
                tagOps: tagOps);

            var error = Throws<InvalidOperationException>(() => system.Update(0f));

            That(error!.Message, Does.StartWith(AbilityExecSystem.ToggleActiveEffectQueueMissingError));
            That(world.Has<AbilityExecInstance>(actor), Is.True, "Terminal exec must remain for retry when toggle side effects cannot commit.");
            That(world.Get<AbilityExecInstance>(actor).State, Is.EqualTo(AbilityExecRunState.Finished));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.True, "Order must stay active until the whole activation transaction can succeed.");
            That(world.Get<GameplayTagContainer>(actor).HasTag(toggleTagId), Is.False, "Toggle tag must not land without required active effects.");
            That(orderTypes.TerminalResults.Count, Is.EqualTo(0), "Completed must not publish before toggle side effects succeed.");
            int finishedCount = 0;
            foreach (ref readonly var evt in presentationEvents.Events)
            {
                if (evt.Kind == GasPresentationEventKind.CastFinished)
                {
                    finishedCount++;
                }
            }
            That(finishedCount, Is.Zero);
        }

        [Test]
        public void AbilityExecSystem_ToggleActivate_DoesNotCompleteWhenActiveEffectQueueFull()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9007;
            const int toggleTagId = 45;
            const int activeEffectTemplateId = 77002;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);
            var order = new Order { OrderId = 22, Actor = actor, OrderTypeId = castAbilityOrderTypeId };
            ref var orders = ref world.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(in order, priority: 100);
            world.Get<BlackboardIntBuffer>(actor).Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var activateSpec = default(AbilityExecSpec);
            activateSpec.ClockId = GasClockId.Step;
            activateSpec.SetItem(0, ExecItemKind.End, tick: 0);

            var toggleSpec = new AbilityToggleSpec
            {
                ToggleTagId = toggleTagId,
                ActiveEffectCount = 1,
            };
            unsafe
            {
                toggleSpec.ActiveEffectTemplateIds[0] = activeEffectTemplateId;
            }

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = activateSpec,
                HasToggleSpec = true,
                ToggleSpec = toggleSpec,
            });
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                EntityBlackboardKey = -1,
                SpatialBlackboardKey = -1,
            });
            var effectRequests = new EffectRequestQueue();
            while (effectRequests.AvailableCapacity > 0)
            {
                effectRequests.Publish(new EffectRequest { TemplateId = 1 });
            }

            var presentationEvents = new GasPresentationEventBuffer(capacity: 32);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                effectRequests,
                16,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes,
                tagOps: tagOps);

            var error = Throws<InvalidOperationException>(() => system.Update(0f));

            That(error!.Message, Does.StartWith(AbilityExecSystem.ToggleActiveEffectQueueFullError));
            That(world.Has<AbilityExecInstance>(actor), Is.True);
            That(world.Get<OrderBuffer>(actor).HasActive, Is.True);
            That(world.Get<GameplayTagContainer>(actor).HasTag(toggleTagId), Is.False);
            That(orderTypes.TerminalResults.Count, Is.EqualTo(0));
            int finishedCount = 0;
            foreach (ref readonly var evt in presentationEvents.Events)
            {
                if (evt.Kind == GasPresentationEventKind.CastFinished)
                {
                    finishedCount++;
                }
            }
            That(finishedCount, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AbilityExecSystem_ActivationTagsRejectCast_AndFailOrder(bool requiredAllMissing)
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9100;
            const int activationTagId = 73;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer(),
                new GameplayTagContainer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);
            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = requiredAllMissing ? 10 : 11,
                Actor = actor,
                OrderTypeId = castAbilityOrderTypeId,
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);
            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var blockTags = new AbilityActivationBlockTags();
            if (requiredAllMissing)
            {
                blockTags.RequiredAll.AddTag(activationTagId);
            }
            else
            {
                blockTags.BlockedAny.AddTag(activationTagId);
                ref var actorTags = ref world.Get<GameplayTagContainer>(actor);
                actorTags.AddTag(activationTagId);
            }

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                HasActivationBlockTags = true,
                ActivationBlockTags = blockTags,
            });

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                AllowQueuedMode = false,
                ClearQueueOnActivate = true,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
            });
            var presentationEvents = new GasPresentationEventBuffer(8);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                4096,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()),
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes);

            That(system.UpdateSlice(0f, int.MaxValue), Is.True);
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(presentationEvents.Count, Is.EqualTo(1));
            That(presentationEvents.Events[0].FailReason, Is.EqualTo(AbilityCastFailReason.BlockedByTag));
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            ref readonly OrderTerminalOutcome terminal = ref orderTypes.TerminalResults[0];
            That(terminal.OrderId, Is.EqualTo(order.OrderId));
            That(terminal.State, Is.EqualTo(OrderTerminalState.Failed));
            That(terminal.FailureReason, Is.EqualTo(OrderFailureReason.ActivationBlocked));
        }

        [Test]
        public void AbilityExecSystem_ActivationPreconditionGraphRejectsCast_AndFailsOrder()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9101;
            const int validationGraphId = 301;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 9,
                Actor = actor,
                OrderTypeId = castAbilityOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition
            {
                HasActivationPrecondition = true,
                ActivationPrecondition = new AbilityActivationPrecondition
                {
                    ValidationGraphId = validationGraphId
                }
            };
            defs.Register(abilityId, in def);

            var graphPrograms = new GraphProgramRegistry();
            graphPrograms.Register(validationGraphId, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstBool,
                    Dst = 0,
                    Imm = 0
                }
            });

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                AllowQueuedMode = false,
                ClearQueueOnActivate = true,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex
            });

            var presentationEvents = new GasPresentationEventBuffer(8);
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                4096,
                defs,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                graphPrograms: graphPrograms,
                graphApi: graphApi,
                orderTypeRegistry: orderTypes,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            bool completed = system.UpdateSlice(0f, int.MaxValue);

            That(completed, Is.True);
            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False, "Rejected casts must fail the active order so queued orders can promote.");
            That(presentationEvents.Count, Is.EqualTo(1));
            That(presentationEvents.Events[0].Kind, Is.EqualTo(GasPresentationEventKind.CastFailed));
            That(presentationEvents.Events[0].FailReason, Is.EqualTo(AbilityCastFailReason.PreconditionFailed));
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            ref readonly OrderTerminalOutcome terminal = ref orderTypes.TerminalResults[0];
            That(terminal.OrderId, Is.EqualTo(order.OrderId));
            That(terminal.State, Is.EqualTo(OrderTerminalState.Failed));
            That(terminal.FailureReason, Is.EqualTo(OrderFailureReason.PreconditionFailed));
        }

        [Test]
        public void AbilitySystem_RegistryAbility_ActivationPreconditionGraphRejectsActivation()
        {
            using var world = World.Create();
            const int abilityId = 9201;
            const int validationGraphId = 302;

            var actor = world.Create(new AbilityStateBuffer());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            var effects = new AbilityOnActivateEffects();
            effects.Add(4001);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition
            {
                HasOnActivateEffects = true,
                OnActivateEffects = effects,
                HasActivationPrecondition = true,
                ActivationPrecondition = new AbilityActivationPrecondition
                {
                    ValidationGraphId = validationGraphId
                }
            };
            defs.Register(abilityId, in def);

            var graphPrograms = new GraphProgramRegistry();
            graphPrograms.Register(validationGraphId, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstBool,
                    Dst = 0,
                    Imm = 0
                }
            });

            var effectRequests = new EffectRequestQueue();
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: effectRequests);
            var system = new AbilitySystem(
                world,
                effectRequests,
                defs,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()),
                graphPrograms: graphPrograms,
                graphApi: graphApi);

            bool activated = system.TryActivateAbility(actor, 0);

            That(activated, Is.False);
            That(effectRequests.Count, Is.EqualTo(0));
        }

        [Test]
        public void AbilitySystem_TemplateAbility_ActivationPreconditionGraphRejectsActivation()
        {
            using var world = World.Create();
            const int validationGraphId = 303;

            var templateEffects = new AbilityOnActivateEffects();
            templateEffects.Add(4002);

            var template = world.Create(
                new AbilityTemplate(),
                templateEffects,
                new AbilityActivationPrecondition { ValidationGraphId = validationGraphId });

            var actor = world.Create(new AbilityStateBuffer());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(template);

            var graphPrograms = new GraphProgramRegistry();
            graphPrograms.Register(validationGraphId, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstBool,
                    Dst = 0,
                    Imm = 0
                }
            });

            var effectRequests = new EffectRequestQueue();
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: effectRequests);
            var system = new AbilitySystem(
                world,
                effectRequests,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()),
                graphPrograms: graphPrograms,
                graphApi: graphApi);

            bool activated = system.TryActivateAbility(actor, 0);

            That(activated, Is.False);
            That(effectRequests.Count, Is.EqualTo(0));
        }

        // Region: GraphExecutor.ExecuteValidation
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ExecuteValidation_EmptyProgram_ReturnsTrue()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            // Empty program: B[0] starts at 1 (pass), no instructions change it.
            ReadOnlySpan<GraphInstruction> program = ReadOnlySpan<GraphInstruction>.Empty;
            bool result = GasGraphExecutor.ExecuteValidation(world, caster, target, default, program, null!);
            That(result, Is.True, "Empty validation program should pass by default (B[0]=1)");
        }

        [Test]
        public void ExecuteValidation_SetBoolFalse_ReturnsFalse()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            // Create a program with a single instruction: ConstBool B[0] = 0 (reject)
            var instruction = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.ConstBool,
                Dst = 0,  // register index B[0]
                Imm = 0   // value = false
            };
            ReadOnlySpan<GraphInstruction> program = new[] { instruction };
            bool result = GasGraphExecutor.ExecuteValidation(world, caster, target, default, program, null!);
            That(result, Is.False, "ConstBool B[0]=0 should cause validation to fail");
        }

        // ════════════════════════════════════════════════════════════════════
        // Region: OrderBuffer queue stress
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OrderBuffer_Enqueue_RespectsPriorityOrdering()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTypeId = 1 }, priority: 1, -1, insertStep: 0);
            buffer.Enqueue(new Order { OrderTypeId = 2 }, priority: 3, -1, insertStep: 1);
            buffer.Enqueue(new Order { OrderTypeId = 3 }, priority: 2, -1, insertStep: 2);

            That(buffer.QueuedCount, Is.EqualTo(3));
            That(buffer.GetQueued(0).Order.OrderTypeId, Is.EqualTo(2), "Highest priority first");
            That(buffer.GetQueued(1).Order.OrderTypeId, Is.EqualTo(3), "Second priority");
            That(buffer.GetQueued(2).Order.OrderTypeId, Is.EqualTo(1), "Lowest priority last");
        }

        [Test]
        public void OrderBuffer_Enqueue_FIFOWithinSamePriority()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTypeId = 1 }, priority: 5, -1, insertStep: 10);
            buffer.Enqueue(new Order { OrderTypeId = 2 }, priority: 5, -1, insertStep: 20);
            buffer.Enqueue(new Order { OrderTypeId = 3 }, priority: 5, -1, insertStep: 30);

            That(buffer.GetQueued(0).Order.OrderTypeId, Is.EqualTo(1), "FIFO: first inserted comes first");
            That(buffer.GetQueued(1).Order.OrderTypeId, Is.EqualTo(2));
            That(buffer.GetQueued(2).Order.OrderTypeId, Is.EqualTo(3));
        }

        [Test]
        public void OrderBuffer_Enqueue_FullQueueReturnsFalse()
        {
            var buffer = OrderBuffer.CreateEmpty();
            for (int i = 0; i < OrderBuffer.MAX_QUEUED_ORDERS; i++)
            {
                bool ok = buffer.Enqueue(new Order { OrderTypeId = i }, 0, -1, i);
                That(ok, Is.True, $"Enqueue {i} should succeed");
            }

            bool overflow = buffer.Enqueue(new Order { OrderTypeId = 999 }, 0, -1, 100);
            That(overflow, Is.False, "Queue full: should reject");
            That(buffer.QueuedCount, Is.EqualTo(OrderBuffer.MAX_QUEUED_ORDERS));
        }

        [Test]
        public void OrderBuffer_RemoveExpired_CleansUpCorrectly()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTypeId = 1 }, 0, expireStep: 10, insertStep: 0);
            buffer.Enqueue(new Order { OrderTypeId = 2 }, 0, expireStep: 50, insertStep: 1);
            buffer.Enqueue(new Order { OrderTypeId = 3 }, 0, expireStep: -1, insertStep: 2); // no expiration

            int removed = buffer.RemoveExpired(currentStep: 30);
            That(removed, Is.EqualTo(1), "Only order with expireStep=10 should be expired");
            That(buffer.QueuedCount, Is.EqualTo(2));
        }

        [Test]
        public void OrderBuffer_PromoteNext_MovesFirstQueuedToActive()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTypeId = 1 }, priority: 10, -1, 0);
            buffer.Enqueue(new Order { OrderTypeId = 2 }, priority: 5, -1, 1);

            bool promoted = buffer.PromoteNext();
            That(promoted, Is.True);
            That(buffer.HasActive, Is.True);
            That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(1), "Highest priority promoted");
            That(buffer.QueuedCount, Is.EqualTo(1), "One remaining in queue");
        }

        [Test]
        public void StopOrderSystem_ActiveStopOrder_DoesNotRequireGameplayTagContainer()
        {
            TagRegistry.Clear();

            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new AbilityExecInstance());

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var stopOrder = new Order { OrderId = 1, Actor = actor, OrderTypeId = 103 };
            buffer.SetActiveDirect(in stopOrder, priority: 200);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 103, AllowQueuedMode = false, ClearQueueOnActivate = true });

            var system = new StopOrderSystem(world, orderTypes, 103);
            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_ActiveMoveToOrder_MovesAndCompletes()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 300f);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition
            });

            var args = new OrderArgs();
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = new Vector3(90f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order { OrderId = 1, Actor = actor, OrderTypeId = 101, Args = args };
            buffer.SetActiveDirect(in order, priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, stopRadiusCm: 5f);
            system.Update(0.10f);

            var firstStepPos = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            That(firstStepPos.X, Is.EqualTo(30).Within(1));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.True);

            system.Update(0.10f);
            system.Update(0.10f);

            var finalPos = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            That(finalPos.X, Is.EqualTo(90).Within(1));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_PhysicsBodyWritesVelocityInsteadOfWorldPosition()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 300f);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition
            });

            var args = new OrderArgs();
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = new Vector3(90f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order { Actor = actor, OrderTypeId = 101, Args = args };
            buffer.SetActiveDirect(in order, priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, stopRadiusCm: 5f);
            system.Update(0.10f);

            var worldPos = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            That(worldPos.X, Is.EqualTo(0), "Physics-backed move orders must let Physics2D own position writes.");
            That(world.Get<Velocity2D>(actor).Linear.X.ToFloat(), Is.EqualTo(300f).Within(0.01f));
            That(world.Get<Velocity2D>(actor).Linear.Y.ToFloat(), Is.EqualTo(0f).Within(0.01f));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.True);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_PhysicsBodyPreservesSolverSeparationVelocity()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                Velocity2D.FromCmPerSec(0f, 180f),
                Mass2D.FromFloat(1f, 1f),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 300f);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition
            });

            var args = new OrderArgs();
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = new Vector3(90f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order { Actor = actor, OrderTypeId = 101, Args = args };
            buffer.SetActiveDirect(in order, priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, stopRadiusCm: 5f);
            system.Update(0.10f);

            var velocity = world.Get<Velocity2D>(actor).Linear;
            That(velocity.X.ToFloat(), Is.GreaterThan(0f), "Move order should continue driving toward the target.");
            That(velocity.Y.ToFloat(), Is.GreaterThan(0f), "Physics-backed move orders must preserve solver-authored separation velocity.");
            That(velocity.Length().ToFloat(), Is.EqualTo(300f).Within(0.01f), "Preserved separation velocity must not raise total speed above the authored move speed.");
        }

        [Test]
        public void MoveToWorldCmOrderSystem_PhysicsBodyPreservesSolverRetreatVelocity()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                Velocity2D.FromCmPerSec(-180f, 60f),
                Mass2D.FromFloat(1f, 1f),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 300f);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition
            });

            var args = new OrderArgs();
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = new Vector3(90f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order { Actor = actor, OrderTypeId = 101, Args = args };
            buffer.SetActiveDirect(in order, priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, stopRadiusCm: 5f);
            system.Update(0.10f);

            var velocity = world.Get<Velocity2D>(actor).Linear;
            That(velocity.X.ToFloat(), Is.LessThan(0f), "Move order must not immediately cancel a solver-authored retreat velocity.");
            That(velocity.Y.ToFloat(), Is.GreaterThan(0f), "Retreat preservation should keep the solver's lateral correction too.");
            That(velocity.Length().ToFloat(), Is.LessThanOrEqualTo(300.01f), "Preserved solver velocity must remain bounded by authored move speed.");
        }

        [Test]
        public void MoveToWorldCmOrderSystem_Arrival_PromotesQueuedWaypoint()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty(),
                new BlackboardSpatialBuffer());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 300f);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });

            var firstArgs = new OrderArgs();
            firstArgs.Spatial.Kind = OrderSpatialKind.WorldCm;
            firstArgs.Spatial.Mode = OrderCollectionMode.Single;
            firstArgs.Spatial.WorldCm = new Vector3(30f, 0f, 0f);

            var secondArgs = new OrderArgs();
            secondArgs.Spatial.Kind = OrderSpatialKind.WorldCm;
            secondArgs.Spatial.Mode = OrderCollectionMode.Single;
            secondArgs.Spatial.WorldCm = new Vector3(60f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var firstOrder = new Order { OrderId = 1, Actor = actor, OrderTypeId = 101, Args = firstArgs };
            var secondOrder = new Order { OrderId = 2, Actor = actor, OrderTypeId = 101, Args = secondArgs, SubmitMode = OrderSubmitMode.Queued };
            buffer.SetActiveDirect(in firstOrder, priority: 60);
            buffer.Enqueue(in secondOrder, priority: 60, expireStep: -1, insertStep: 1);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, stopRadiusCm: 5f);
            system.Update(0.10f);

            ref var promotedBuffer = ref world.Get<OrderBuffer>(actor);
            That(promotedBuffer.HasActive, Is.True);
            That(promotedBuffer.ActiveOrder.Order.Args.Spatial.WorldCm.X, Is.EqualTo(60f).Within(0.001f));
            That(promotedBuffer.QueuedCount, Is.EqualTo(0));

            system.Update(0.10f);

            var finalPos = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            That(finalPos.X, Is.EqualTo(60).Within(1));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_NonMovableOrderBufferEntity_IgnoresMoveTo()
        {
            using var world = World.Create();

            // Structure-like entity: keeps OrderBuffer for production/ability queues but declares no
            // movement capability (no positive MoveSpeed attribute, no physics body).
            var structure = world.Create(
                WorldPositionCm.FromCm(0, 0),
                OrderBuffer.CreateEmpty());

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition
            });

            var args = new OrderArgs();
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = new Vector3(90f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(structure);
            var order = new Order { Actor = structure, OrderTypeId = 101, Args = args };
            buffer.SetActiveDirect(in order, priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, stopRadiusCm: 5f);
            system.Update(0.10f);
            system.Update(0.10f);

            var pos = world.Get<WorldPositionCm>(structure).ToWorldCmInt2();
            That(pos.X, Is.EqualTo(0), "OrderBuffer alone must not let core move a non-movable entity.");
            That(pos.Y, Is.EqualTo(0));
            That(world.Get<OrderBuffer>(structure).HasActive, Is.True,
                "moveTo is ignored (not consumed) for entities without movement capability.");
        }

        [Test]
        public void MoveToWorldCmOrderSystem_ZeroMoveSpeed_IgnoresMoveToWithoutDefaultFallback()
        {
            using var world = World.Create();

            // Entity declares an explicit MoveSpeed of 0 (intentionally immovable). With no implicit
            // default-speed fallback, it must not move.
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 0f);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition
            });

            var args = new OrderArgs();
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = new Vector3(90f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order { Actor = actor, OrderTypeId = 101, Args = args };
            buffer.SetActiveDirect(in order, priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, stopRadiusCm: 5f);
            system.Update(0.10f);
            system.Update(0.10f);

            var pos = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            That(pos.X, Is.EqualTo(0), "MoveSpeed of 0 must not fall back to a default speed.");
            That(pos.Y, Is.EqualTo(0));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.True);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_MovementSuppressed_DoesNotAdvance()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty(),
                new MovementSuppressed2D());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 300f);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "moveTo",
                OrderTypeId = 101,
                AllowQueuedMode = true,
            });

            var args = new OrderArgs();
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = new Vector3(90f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order { OrderId = 1, Actor = actor, OrderTypeId = 101, Args = args };
            buffer.SetActiveDirect(in order, priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, stopRadiusCm: 5f);
            system.Update(0.10f);

            That(world.Get<WorldPositionCm>(actor).Value, Is.EqualTo(Fix64Vec2.Zero));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.True);
        }

        private static GraphInstruction[] RejectValidationProgram()
        {
            return new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstBool,
                    Dst = 0,
                    Imm = 0
                }
            };
        }

    }
}
