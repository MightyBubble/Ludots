using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Input.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.OrderLifecycle
{
    [TestFixture]
    public sealed class OrderCompositePlannerTests
    {
        private const int CastAbilityOrderTypeId = 100;
        private const int MoveToOrderTypeId = 101;
        private const int TestAbilityId = 900;

        [Test]
        public void OrderWorldSpatialResolver_VisualOnlyEntity_IsNotGameplayPositionTruth()
        {
            using var world = World.Create();
            Entity entity = world.Create(new Ludots.Core.Presentation.Components.VisualTransform
            {
                Position = new Vector3(12f, 0f, 34f),
            });

            bool resolved = OrderWorldSpatialResolver.TryGetEntityWorldCm(world, entity, out Vector3 worldCm);

            Assert.That(resolved, Is.False);
            Assert.That(worldCm, Is.EqualTo(Vector3.Zero));
        }

        [Test]
        public void CompositeOrderPlanner_ImmediateOutOfRangeCast_EnqueuesMoveAndContinuation()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                abilities,
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer());

            var castOrder = CreateCastOrder(actor, targetXcm: 900, submitMode: OrderSubmitMode.Immediate);

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(orderQueue.TryDequeue(out var moveOrder), Is.True);
            Assert.That(moveOrder.OrderTypeId, Is.EqualTo(MoveToOrderTypeId));
            Assert.That(moveOrder.SubmitMode, Is.EqualTo(OrderSubmitMode.Immediate));
            Assert.That(moveOrder.Args.Spatial.WorldCm.X, Is.EqualTo(400f).Within(0.01f));
            Assert.That(moveOrder.Args.Spatial.WorldCm.Z, Is.EqualTo(0f).Within(0.01f));

            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Span<Order> extracted = stackalloc Order[OrderContinuationBuffer.MAX_CONTINUATIONS];
            int continuationCount = continuations.Extract(moveOrder.OrderId, extracted);

            Assert.That(continuationCount, Is.EqualTo(1));
            Assert.That(extracted[0].OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Assert.That(moveOrder.OrderId, Is.GreaterThan(0));
            Assert.That(extracted[0].OrderId, Is.GreaterThan(0));
            Assert.That(extracted[0].OrderId, Is.Not.EqualTo(moveOrder.OrderId));
            Assert.That(extracted[0].SubmitMode, Is.EqualTo(OrderSubmitMode.Queued));
            Assert.That(extracted[0].Args.I0, Is.EqualTo(0));
        }

        [Test]
        public void CompositeOrderPlanner_MissingContinuationState_FailsBeforeAssigningOrderIds()
        {
            using var world = World.Create();
            var admissionResults = new OrderAdmissionResultBuffer(64, 64);
            var orderQueue = new OrderQueue(64, admissionResults);
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId);
            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                abilities,
                OrderBuffer.CreateEmpty());
            var castOrder = CreateCastOrder(actor, targetXcm: 900, submitMode: OrderSubmitMode.Immediate);

            var error = Assert.Throws<InvalidOperationException>(() => planner.Submit(in castOrder));

            Assert.That(error!.Message, Does.StartWith(OrderContinuationStateInstaller.MissingStateError));
            Assert.That(castOrder.OrderId, Is.Zero);
            Assert.That(orderQueue.Count, Is.Zero);
            Assert.That(admissionResults.Count, Is.Zero);
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);

            world.Add(actor, new OrderContinuationBuffer());
            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(orderQueue.TryDequeue(out Order moveOrder), Is.True);
            Assert.That(moveOrder.OrderId, Is.EqualTo(2));
            ref OrderContinuationBuffer continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Span<Order> extracted = stackalloc Order[OrderContinuationBuffer.MAX_CONTINUATIONS];
            Assert.That(continuations.Extract(moveOrder.OrderId, extracted), Is.EqualTo(1));
            Assert.That(extracted[0].OrderId, Is.EqualTo(1));
        }

        [Test]
        public void CompositeOrderPlanner_ContinuationCapacityFull_ReleasesFollowUpPayload()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId);
            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                abilities,
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer(),
                new OrderSpatialPayloadBuffer());
            ref OrderContinuationBuffer continuations = ref world.Get<OrderContinuationBuffer>(actor);
            for (int i = 0; i < OrderContinuationBuffer.MAX_CONTINUATIONS; i++)
            {
                Assert.That(
                    continuations.TryAdd(100 + i, new Order { OrderId = 200 + i, OrderTypeId = CastAbilityOrderTypeId }),
                    Is.True);
            }
            Order castOrder = CreatePayloadCastOrder(world, actor, targetXcm: 900);
            OrderSpatialPayloadHandle payloadHandle = castOrder.Args.Spatial.Payload;

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.RejectedQueueFull));

            Assert.That(orderQueue.Count, Is.Zero);
            Assert.That(continuations.Count, Is.EqualTo(OrderContinuationBuffer.MAX_CONTINUATIONS));
            Assert.Throws<InvalidOperationException>(() =>
                world.Get<OrderSpatialPayloadBuffer>(actor).GetPointCount(in payloadHandle));
        }

        [Test]
        public void CompositeOrderPlanner_PrimaryAdmissionRejected_ReleasesRegisteredFollowUpPayload()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(1, new OrderAdmissionResultBuffer(64, 64));
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId);
            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                abilities,
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer(),
                new OrderSpatialPayloadBuffer());
            Assert.That(
                orderQueue.Submit(new Order { OrderTypeId = MoveToOrderTypeId, Actor = actor }),
                Is.EqualTo(OrderSubmitResult.Queued));
            Order castOrder = CreatePayloadCastOrder(world, actor, targetXcm: 900);
            OrderSpatialPayloadHandle payloadHandle = castOrder.Args.Spatial.Payload;

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.RejectedQueueFull));

            Assert.That(world.Get<OrderContinuationBuffer>(actor).HasEntries, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                world.Get<OrderSpatialPayloadBuffer>(actor).GetPointCount(in payloadHandle));
        }

        [Test]
        public void CompositeOrderPlanner_QueuedCast_UsesProjectedMoveEndpoint()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                abilities,
                OrderBuffer.CreateEmpty());

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(CreateMoveOrder(actor, 500f), priority: 60);

            var queuedCast = CreateCastOrder(actor, targetXcm: 900, submitMode: OrderSubmitMode.Queued);

            Assert.That(planner.Submit(in queuedCast), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(orderQueue.TryDequeue(out var submittedOrder), Is.True);
            Assert.That(submittedOrder.OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Assert.That(submittedOrder.SubmitMode, Is.EqualTo(OrderSubmitMode.Queued));
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);
        }

        [Test]
        public void CompositeOrderPlanner_AutoTargetAbility_BypassesMoveThenCastPlanning()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f, autoTargetPolicy: AutoTargetPolicy.NearestEnemyInRange),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                abilities,
                OrderBuffer.CreateEmpty());

            var castOrder = CreateCastOrder(actor, targetXcm: 900, submitMode: OrderSubmitMode.Immediate);

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(orderQueue.TryDequeue(out var submittedOrder), Is.True);
            Assert.That(submittedOrder.OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Assert.That(submittedOrder.SubmitMode, Is.EqualTo(OrderSubmitMode.Immediate));
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);
        }

        [Test]
        public void OrderContinuationSystem_CompletedTrigger_QueuesFollowUp()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            clock.Advance(ClockDomainId.Step, 12);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });

            var rules = new OrderRuleRegistry();

            var active = new Order
            {
                OrderId = 7,
                OrderTypeId = MoveToOrderTypeId,
                Actor = default,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = CreateWorldTargetArgs(400f)
            };
            var orderBuffer = OrderBuffer.CreateEmpty();
            orderBuffer.SetActiveDirect(in active, priority: 60);
            Entity actor = world.Create(
                orderBuffer,
                new OrderContinuationBuffer(),
                new OrderSpatialPayloadBuffer());

            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Order followUp = CreatePayloadOrder(world, actor, orderId: 8);
            followUp.SubmitMode = OrderSubmitMode.Queued;
            followUp.Args.I0 = 0;
            continuations.TryAdd(7, in followUp);

            Assert.That(OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes), Is.True);

            var system = new OrderContinuationSystem(world, clock, orderTypes, rules);
            system.Update(0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            Assert.That(buffer.QueuedCount, Is.EqualTo(1));
            Assert.That(buffer.GetQueued(0).Order.OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Order queuedFollowUp = buffer.GetQueued(0).Order;
            Assert.That(
                OrderWorldSpatialResolver.GetSpatialPointCount(world, in queuedFollowUp),
                Is.EqualTo(3));
            Assert.That(continuations.HasEntries, Is.False);
        }

        [Test]
        public void OrderContinuationSystem_RejectedFollowUp_PublishesTerminalFailure()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var terminalResults = new OrderTerminalResultBuffer(capacity: 4);
            var orderTypes = new OrderTypeRegistry(terminalResults);
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                AllowQueuedMode = true,
                QueuedModeMaxSize = 0,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });

            var trigger = new Order
            {
                OrderId = 71,
                OrderTypeId = MoveToOrderTypeId,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in trigger, priority: 60);
            Entity actor = world.Create(
                buffer,
                new OrderContinuationBuffer(),
                new OrderSpatialPayloadBuffer());
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            var followUp = new Order
            {
                OrderId = 72,
                OrderTypeId = CastAbilityOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Queued,
            };
            Span<int> pointX = stackalloc int[OrderSpatial.MaxInlinePoints + 1];
            Span<int> pointY = stackalloc int[OrderSpatial.MaxInlinePoints + 1];
            for (int i = 0; i < pointX.Length; i++)
            {
                pointX[i] = i * 100;
                pointY[i] = i * 50;
            }
            OrderSpatialPayloadOps.SetPath(world, actor, ref followUp, pointX, pointY, pointX.Length);
            OrderSpatialPayloadHandle payloadHandle = followUp.Args.Spatial.Payload;
            continuations.TryAdd(trigger.OrderId, in followUp);
            Order downstream = CreatePayloadOrder(world, actor, orderId: 73);
            OrderSpatialPayloadHandle downstreamPayloadHandle = downstream.Args.Spatial.Payload;
            continuations.TryAdd(followUp.OrderId, in downstream);

            Assert.That(OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes), Is.True);

            var system = new OrderContinuationSystem(world, clock, orderTypes, new OrderRuleRegistry());
            system.Update(0f);

            Assert.That(continuations.HasEntries, Is.False);
            Assert.That(world.Get<OrderBuffer>(actor).IsEmpty, Is.True);
            Assert.That(terminalResults.Count, Is.EqualTo(2));
            Assert.That(terminalResults[1].OrderId, Is.EqualTo(72));
            Assert.That(terminalResults[1].State, Is.EqualTo(OrderTerminalState.Failed));
            Assert.That(terminalResults[1].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionQueueFull));
            Assert.Throws<InvalidOperationException>(() =>
                world.Get<OrderSpatialPayloadBuffer>(actor).GetPointCount(in payloadHandle));
            Assert.Throws<InvalidOperationException>(() =>
                world.Get<OrderSpatialPayloadBuffer>(actor).GetPointCount(in downstreamPayloadHandle));
        }

        [Test]
        public void OrderContinuationSystem_TerminalCapacityFull_LeavesFollowUpAttachedForHardFailure()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var terminalResults = new OrderTerminalResultBuffer(capacity: 1);
            var orderTypes = new OrderTypeRegistry(terminalResults);
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                AllowQueuedMode = true,
                QueuedModeMaxSize = 0,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });

            var trigger = new Order { OrderId = 81, OrderTypeId = MoveToOrderTypeId };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in trigger, priority: 60);
            Entity actor = world.Create(
                buffer,
                new OrderContinuationBuffer(),
                new OrderSpatialPayloadBuffer());
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Order followUp = CreatePayloadOrder(world, actor, orderId: 82);
            followUp.SubmitMode = OrderSubmitMode.Queued;
            continuations.TryAdd(trigger.OrderId, in followUp);
            Assert.That(OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes), Is.True);

            var system = new OrderContinuationSystem(world, clock, orderTypes, new OrderRuleRegistry());
            var error = Assert.Throws<InvalidOperationException>(() => system.Update(0f));

            Assert.That(error!.Message, Does.StartWith("ORDER.TERMINAL.ERR.ResultCapacityExceeded"));
            Assert.That(continuations.CountByTrigger(trigger.OrderId), Is.EqualTo(1));
            Assert.That(OrderWorldSpatialResolver.GetSpatialPointCount(world, in followUp), Is.EqualTo(3));
            Assert.That(world.Get<OrderBuffer>(actor).IsEmpty, Is.True);
            Assert.That(terminalResults.Count, Is.EqualTo(1));
        }

        [TestCase(OrderSubmitResult.Activated, true)]
        [TestCase(OrderSubmitResult.Queued, true)]
        [TestCase(OrderSubmitResult.Pending, true)]
        [TestCase(OrderSubmitResult.RejectedQueueFull, false)]
        [TestCase(OrderSubmitResult.RejectedByRule, false)]
        [TestCase(OrderSubmitResult.RejectedValidation, false)]
        [TestCase(OrderSubmitResult.RejectedInvalidActor, false)]
        [TestCase(OrderSubmitResult.RejectedInvalidOrderType, false)]
        [TestCase(OrderSubmitResult.RejectedBlackboardCapacity, false)]
        [TestCase(OrderSubmitResult.RejectedMissingBlackboard, false)]
        [TestCase(OrderSubmitResult.RejectedAdmissionCapacity, false)]
        public void OrderSubmitResultSemantics_ClassifiesEveryPublishedResult(
            OrderSubmitResult result,
            bool expectedAccepted)
        {
            Assert.That(OrderSubmitResultSemantics.IsAccepted(result), Is.EqualTo(expectedAccepted));
        }

        [Test]
        public void FinalizeCurrent_Failed_RemovesContinuationAndOnlyFinalizesOnce()
        {
            using var world = World.Create();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });

            var active = new Order { OrderId = 17, OrderTypeId = CastAbilityOrderTypeId };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in active, 100);
            Entity actor = world.Create(buffer, new OrderContinuationBuffer());
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            continuations.TryAdd(17, new Order { OrderId = 18, OrderTypeId = CastAbilityOrderTypeId });

            bool first = OrderSubmitter.FinalizeCurrent(
                world,
                actor,
                orderTypes,
                OrderTerminalState.Failed,
                OrderFailureReason.AbilityDefinitionMissing);
            bool second = OrderSubmitter.FinalizeCurrent(
                world,
                actor,
                orderTypes,
                OrderTerminalState.Completed);

            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(continuations.HasEntries, Is.False);
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            ref readonly var terminal = ref orderTypes.TerminalResults[0];
            Assert.That(terminal.OrderId, Is.EqualTo(17));
            Assert.That(terminal.Actor, Is.EqualTo(actor));
            Assert.That(terminal.State, Is.EqualTo(OrderTerminalState.Failed));
            Assert.That(terminal.FailureReason, Is.EqualTo(OrderFailureReason.AbilityDefinitionMissing));
        }

        [Test]
        public void FinalizeCurrent_TwoOrdersForSameActor_PublishesBothTerminalOutcomes()
        {
            using var world = World.Create();
            var orderTypes = CreateCastOrderTypes();

            var first = new Order { OrderId = 21, OrderTypeId = CastAbilityOrderTypeId };
            var second = new Order { OrderId = 22, OrderTypeId = CastAbilityOrderTypeId };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in first, priority: 100);
            buffer.Enqueue(in second, priority: 100, expireStep: -1, insertStep: 1);
            Entity actor = world.Create(buffer);

            Assert.That(OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes), Is.True);
            Assert.That(
                OrderSubmitter.FinalizeCurrent(
                    world,
                    actor,
                    orderTypes,
                    OrderTerminalState.Failed,
                    OrderFailureReason.ActivationBlocked),
                Is.True);

            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(2));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(21));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Completed));
            Assert.That(orderTypes.TerminalResults[1].OrderId, Is.EqualTo(22));
            Assert.That(orderTypes.TerminalResults[1].State, Is.EqualTo(OrderTerminalState.Failed));
        }

        [Test]
        public void Submit_OrdinaryActorWithoutContinuation_PublishesObservableTerminalOutcome()
        {
            using var world = World.Create();
            var orderTypes = CreateCastOrderTypes();
            Entity actor = world.Create(OrderBuffer.CreateEmpty());
            var order = new Order
            {
                OrderId = 23,
                OrderTypeId = CastAbilityOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate
            };

            Assert.That(
                OrderSubmitter.Submit(
                    world,
                    actor,
                    in order,
                    orderTypes,
                    orderRuleRegistry: null,
                    currentStep: 1,
                    stepRateHz: 30),
                Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes), Is.True);

            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(23));
            Assert.That(orderTypes.TerminalResults[0].Actor, Is.EqualTo(actor));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Completed));
        }

        [Test]
        public void Submit_InterruptsActiveOrder_PublishesCancelledInterruptedOutcome()
        {
            using var world = World.Create();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                CanInterruptSelf = true,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });

            var active = new Order { OrderId = 31, OrderTypeId = CastAbilityOrderTypeId };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in active, priority: 100);
            Entity actor = world.Create(buffer);
            var replacement = new Order
            {
                OrderId = 32,
                OrderTypeId = CastAbilityOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate
            };

            OrderSubmitResult result = OrderSubmitter.Submit(
                world,
                actor,
                in replacement,
                orderTypes,
                orderRuleRegistry: null,
                currentStep: 1,
                stepRateHz: 30);

            Assert.That(result, Is.EqualTo(OrderSubmitResult.Activated));
            ref var updated = ref world.Get<OrderBuffer>(actor);
            Assert.That(updated.ActiveOrder.Order.OrderId, Is.EqualTo(32));
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(31));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Cancelled));
            Assert.That(orderTypes.TerminalResults[0].FailureReason, Is.EqualTo(OrderFailureReason.Interrupted));
        }

        [Test]
        public void CancelAll_ActiveOrder_PublishesCancelledOutcomeAndRemovesContinuation()
        {
            using var world = World.Create();
            var orderTypes = CreateCastOrderTypes();
            var active = new Order { OrderId = 41, OrderTypeId = CastAbilityOrderTypeId };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in active, priority: 100);
            Entity actor = world.Create(
                buffer,
                new OrderContinuationBuffer(),
                new OrderSpatialPayloadBuffer());
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Order followUp = CreatePayloadOrder(world, actor, orderId: 42);
            continuations.TryAdd(41, in followUp);

            OrderSubmitter.CancelAll(world, actor, orderTypes);

            Assert.That(world.Get<OrderBuffer>(actor).IsEmpty, Is.True);
            Assert.That(continuations.HasEntries, Is.False);
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(41));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Cancelled));
            Assert.That(orderTypes.TerminalResults[0].FailureReason, Is.EqualTo(OrderFailureReason.None));
            AssertPayloadIsStale(world, in followUp);
        }

        [Test]
        public void OrderContinuationSystem_FailedTrigger_DoesNotSubmitFollowUp()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orderTypes = CreateCastOrderTypes();
            var rules = new OrderRuleRegistry();
            var active = new Order { OrderId = 51, OrderTypeId = CastAbilityOrderTypeId };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in active, priority: 100);
            Entity actor = world.Create(
                buffer,
                new OrderContinuationBuffer(),
                new OrderSpatialPayloadBuffer());
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Order followUp = CreatePayloadOrder(world, actor, orderId: 52);
            continuations.TryAdd(51, in followUp);

            Assert.That(
                OrderSubmitter.FinalizeCurrent(
                    world,
                    actor,
                    orderTypes,
                    OrderTerminalState.Failed,
                    OrderFailureReason.PreconditionFailed),
                Is.True);

            var system = new OrderContinuationSystem(world, clock, orderTypes, rules);
            system.Update(0f);

            Assert.That(continuations.HasEntries, Is.False);
            Assert.That(world.Get<OrderBuffer>(actor).IsEmpty, Is.True);
            AssertPayloadIsStale(world, in followUp);
        }

        [Test]
        public void FinalizeCurrent_WhenTerminalResultCapacityIsFull_HardStopsBeforeMutatingNextOrder()
        {
            using var world = World.Create();
            var terminalResults = new OrderTerminalResultBuffer(capacity: 1);
            var orderTypes = new OrderTypeRegistry(terminalResults);
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });

            var first = new Order { OrderId = 61, OrderTypeId = CastAbilityOrderTypeId };
            var second = new Order { OrderId = 62, OrderTypeId = CastAbilityOrderTypeId };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in first, priority: 100);
            buffer.Enqueue(in second, priority: 100, expireStep: -1, insertStep: 1);
            Entity actor = world.Create(buffer);
            Assert.That(OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes), Is.True);

            var error = Assert.Throws<InvalidOperationException>(
                () => OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes));

            Assert.That(error!.Message, Does.Contain("ORDER.TERMINAL.ERR.ResultCapacityExceeded"));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(62));
            Assert.That(terminalResults.Count, Is.EqualTo(1));
        }

        [Test]
        public void GasBudgetResetSystem_ClearsPreviousStepTerminalSnapshot()
        {
            using var world = World.Create();
            var terminalResults = new OrderTerminalResultBuffer(capacity: 4);
            var orderTypes = new OrderTypeRegistry(terminalResults);
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100
            });
            var active = new Order { OrderId = 71, OrderTypeId = CastAbilityOrderTypeId };
            var buffer = OrderBuffer.CreateEmpty();
            buffer.SetActiveDirect(in active, priority: 100);
            Entity actor = world.Create(buffer);
            Assert.That(OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes), Is.True);
            uint generation = terminalResults.Generation;

            var reset = new GasBudgetResetSystem(new GasBudget(), terminalResults);
            reset.Update(0f);

            Assert.That(terminalResults.Count, Is.Zero);
            Assert.That(terminalResults.Generation, Is.EqualTo(generation + 1));
        }

        [Test]
        public void OrderSpatialPayloadLifecycle_FinalizeCurrent_ReleasesActivePayload()
        {
            using var world = World.Create();
            var orderTypes = CreateCastOrderTypes();
            Entity actor = world.Create(OrderBuffer.CreateEmpty(), new OrderSpatialPayloadBuffer());
            Order active = CreatePayloadOrder(world, actor, orderId: 81);
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in active, priority: 100);

            Assert.That(OrderSubmitter.NotifyOrderComplete(world, actor, orderTypes), Is.True);

            var stale = Assert.Throws<InvalidOperationException>(
                () => OrderWorldSpatialResolver.GetSpatialPointCount(world, in active));
            Assert.That(stale!.Message, Does.Contain("StalePayloadHandle"));
        }

        [Test]
        public void OrderSpatialPayloadLifecycle_CancelAll_ReleasesEveryOwnedPayload()
        {
            using var world = World.Create();
            var orderTypes = CreateCastOrderTypes();
            Entity actor = world.Create(OrderBuffer.CreateEmpty(), new OrderSpatialPayloadBuffer());
            Order active = CreatePayloadOrder(world, actor, orderId: 91);
            Order queued = CreatePayloadOrder(world, actor, orderId: 92);
            Order pending = CreatePayloadOrder(world, actor, orderId: 93);
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in active, priority: 100);
            Assert.That(buffer.Enqueue(in queued, priority: 100, expireStep: -1, insertStep: 1), Is.True);
            buffer.SetPending(in pending, priority: 100, expireStep: -1, insertStep: 2);

            OrderSubmitter.CancelAll(world, actor, orderTypes);

            Assert.That(buffer.IsEmpty, Is.True);
            Assert.That(buffer.HasPending, Is.False);
            AssertPayloadIsStale(world, in active);
            AssertPayloadIsStale(world, in queued);
            AssertPayloadIsStale(world, in pending);
        }

        [Test]
        public void OrderSpatialPayloadLifecycle_ReplacePending_ReleasesOnlyReplacedPayload()
        {
            using var world = World.Create();
            Entity actor = world.Create(OrderBuffer.CreateEmpty(), new OrderSpatialPayloadBuffer());
            Order first = CreatePayloadOrder(world, actor, orderId: 101);
            Order replacement = CreatePayloadOrder(world, actor, orderId: 102);
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetPending(in first, priority: 100, expireStep: -1, insertStep: 1);

            OrderSubmitter.ReplacePending(
                world,
                ref buffer,
                in replacement,
                priority: 100,
                expireStep: -1,
                insertStep: 2);

            AssertPayloadIsStale(world, in first);
            Assert.That(
                OrderWorldSpatialResolver.GetSpatialPointCount(world, in buffer.PendingOrder.Order),
                Is.EqualTo(3));
        }

        private static OrderTypeRegistry CreateCastOrderTypes()
        {
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1,
            });
            return orderTypes;
        }

        private static Order CreatePayloadOrder(World world, Entity actor, int orderId)
        {
            var order = new Order
            {
                OrderId = orderId,
                OrderTypeId = CastAbilityOrderTypeId,
                Actor = actor,
            };
            int[] x = { 0, 100, 200 };
            int[] y = { 0, 50, 100 };
            OrderSpatialPayloadOps.SetPath(world, actor, ref order, x, y, x.Length);
            return order;
        }

        private static Order CreatePayloadCastOrder(World world, Entity actor, int targetXcm)
        {
            Order order = CreateCastOrder(actor, targetXcm, OrderSubmitMode.Immediate);
            int[] x = { 0, targetXcm / 2, targetXcm };
            int[] y = { 0, 0, 0 };
            OrderSpatialPayloadOps.SetPath(world, actor, ref order, x, y, x.Length);
            return order;
        }

        private static void AssertPayloadIsStale(World world, in Order order)
        {
            Order captured = order;
            var stale = Assert.Throws<InvalidOperationException>(
                () => OrderWorldSpatialResolver.GetSpatialPointCount(world, in captured));
            Assert.That(stale!.Message, Does.Contain("StalePayloadHandle"));
        }

        private static AbilityDefinitionRegistry CreateAbilityRegistry(
            float rangeCm,
            AutoTargetPolicy autoTargetPolicy = AutoTargetPolicy.None)
        {
            var definition = new AbilityDefinition
            {
                HasTargeting = true,
                Targeting = new AbilityTargetingConfig
                {
                    CastRangeCm = rangeCm
                }
            };

            if (autoTargetPolicy != AutoTargetPolicy.None)
            {
                definition.HasInputBindingOverride = true;
                definition.InputBindingOverride = new AbilityInputBindingOverride
                {
                    HasAutoTargetPolicy = true,
                    AutoTargetPolicy = autoTargetPolicy
                };
            }

            var registry = new AbilityDefinitionRegistry();
            registry.Register(TestAbilityId, in definition);
            return registry;
        }

        private static Order CreateCastOrder(Entity actor, float targetXcm, OrderSubmitMode submitMode)
        {
            return new Order
            {
                OrderTypeId = CastAbilityOrderTypeId,
                PlayerId = 1,
                Actor = actor,
                SubmitMode = submitMode,
                Args = new OrderArgs
                {
                    I0 = 0,
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(targetXcm, 0f, 0f)
                    }
                }
            };
        }

        private static Order CreateMoveOrder(Entity actor, float targetXcm)
        {
            return new Order
            {
                OrderId = 7,
                OrderTypeId = MoveToOrderTypeId,
                PlayerId = 1,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Queued,
                Args = CreateWorldTargetArgs(targetXcm)
            };
        }

        private static OrderArgs CreateWorldTargetArgs(float targetXcm)
        {
            return new OrderArgs
            {
                Spatial = new OrderSpatial
                {
                    Kind = OrderSpatialKind.WorldCm,
                    Mode = OrderCollectionMode.Single,
                    WorldCm = new Vector3(targetXcm, 0f, 0f)
                }
            };
        }
    }
}
