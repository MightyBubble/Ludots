using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Input.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class OrderCompositePlannerTests
    {
        private const int CastAbilityOrderTypeId = 100;
        private const int MoveToOrderTypeId = 101;
        private const int TestAbilityId = 900;

        [Test]
        public void CompositeOrderPlanner_ImmediateOutOfRangeCast_EnqueuesMoveAndContinuation()
        {
            using var world = World.Create();
            var orderQueue = CreateOrderQueue();
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
                new OrderContinuationBuffer(),
                OrderBuffer.CreateEmpty());

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
            Assert.That(extracted[0].SubmitMode, Is.EqualTo(OrderSubmitMode.Queued));
            Assert.That(extracted[0].Args.I0, Is.EqualTo(0));
        }

        [Test]
        public void CompositeOrderPlanner_ItemGrantedHighSlot_EnqueuesMoveAndContinuation()
        {
            using var world = World.Create();
            var orderQueue = CreateOrderQueue();
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            var itemGranted = new ItemGrantedSlotBuffer();
            itemGranted.SetOverride(slotIndex: 4, abilityId: TestAbilityId, sourceItem: Entity.Null);
            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new AbilityStateBuffer(),
                itemGranted,
                new OrderContinuationBuffer(),
                OrderBuffer.CreateEmpty());

            Order castOrder = CreateCastOrder(actor, targetXcm: 900, submitMode: OrderSubmitMode.Immediate);
            castOrder.Args.I0 = 4;

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(orderQueue.TryDequeue(out Order moveOrder), Is.True);
            Assert.That(moveOrder.OrderTypeId, Is.EqualTo(MoveToOrderTypeId));
            Assert.That(moveOrder.Args.Spatial.WorldCm.X, Is.EqualTo(400f).Within(0.01f));

            ref OrderContinuationBuffer continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Span<Order> extracted = stackalloc Order[OrderContinuationBuffer.MAX_CONTINUATIONS];
            int continuationCount = continuations.Extract(moveOrder.OrderId, extracted);

            Assert.That(continuationCount, Is.EqualTo(1));
            Assert.That(extracted[0].OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Assert.That(extracted[0].Args.I0, Is.EqualTo(4));
        }

        [Test]
        public void CompositeOrderPlanner_QueuedCast_UsesProjectedMoveEndpoint()
        {
            using var world = World.Create();
            var orderQueue = CreateOrderQueue();
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
        public void CompositeOrderPlanner_TargetedAbility_UsesMoveThenCastPlanningWithoutAbilityInputBypass()
        {
            using var world = World.Create();
            var orderQueue = CreateOrderQueue();
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
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.True);
        }

        [Test]
        public void CompositeOrderPlanner_ZeroRangeSelfAbility_BypassesMoveThenCastPlanning()
        {
            using var world = World.Create();
            var orderQueue = CreateOrderQueue();
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 0f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                abilities,
                OrderBuffer.CreateEmpty());

            var castOrder = CreateCastOrder(actor, targetXcm: 0f, submitMode: OrderSubmitMode.Immediate);
            castOrder.Args.Spatial = default;

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(orderQueue.TryDequeue(out var submittedOrder), Is.True);
            Assert.That(submittedOrder.OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Assert.That(submittedOrder.SubmitMode, Is.EqualTo(OrderSubmitMode.Immediate));
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);
        }

        [Test]
        public void CompositeOrderPlanner_OutOfRangeCastWithMissingAbilityDefinition_ReturnsTypedRejection()
        {
            using var world = World.Create();
            var orderQueue = CreateOrderQueue();
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId + 1);
            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                abilities,
                OrderBuffer.CreateEmpty());

            var castOrder = CreateCastOrder(actor, targetXcm: 900, submitMode: OrderSubmitMode.Immediate);

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.RejectedInvalidOrderType));
            Assert.That(orderQueue.Count, Is.Zero);
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);
        }

        [Test]
        public void CompositeOrderPlanner_OutOfRangeCastWithoutAuthoritativeActorPosition_ReturnsTypedRejection()
        {
            using var world = World.Create();
            var orderQueue = CreateOrderQueue();
            var planner = new CompositeOrderPlanner(
                world,
                orderQueue,
                CreateAbilityRegistry(rangeCm: 500f),
                CastAbilityOrderTypeId,
                MoveToOrderTypeId);

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(TestAbilityId);
            Entity actor = world.Create(
                abilities,
                OrderBuffer.CreateEmpty());

            var castOrder = CreateCastOrder(actor, targetXcm: 900, submitMode: OrderSubmitMode.Immediate);

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.RejectedValidation));
            Assert.That(orderQueue.Count, Is.Zero);
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);
        }

        [Test]
        public void CompositeOrderPlanner_OutOfRangeCastWithoutResolvableTarget_ReturnsTypedRejection()
        {
            using var world = World.Create();
            var orderQueue = CreateOrderQueue();
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
            castOrder.Args.Spatial = default;

            Assert.That(planner.Submit(in castOrder), Is.EqualTo(OrderSubmitResult.RejectedValidation));
            Assert.That(orderQueue.Count, Is.Zero);
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);
        }

        [Test]
        public void OrderContinuationSystem_QueuesFollowUpAheadOfLaterQueuedCommands()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            clock.Advance(ClockDomainId.Step, 12);

            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });

            var rules = new OrderRuleRegistry();

            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer());
            var completed = new OrderTerminalOutcome(
                7,
                MoveToOrderTypeId,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                actor);
            orderTypes.PublishTerminalResult(in completed);

            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            continuations.TryAdd(7, new Order
            {
                OrderId = 8,
                OrderTypeId = CastAbilityOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Queued,
                Args = new OrderArgs { I0 = 0 }
            });

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.Enqueue(
                new Order
                {
                    OrderId = 9,
                    OrderTypeId = MoveToOrderTypeId,
                    Actor = actor,
                    SubmitMode = OrderSubmitMode.Queued,
                    Args = CreateWorldTargetArgs(1200f)
                },
                priority: 60,
                expireStep: -1,
                insertStep: 3);

            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            admissionResults.BeginLogicStep();
            var system = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);
            system.Update(0f);

            Assert.That(buffer.QueuedCount, Is.EqualTo(2));
            Assert.That(buffer.GetQueued(0).Order.OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Assert.That(buffer.GetQueued(1).Order.OrderTypeId, Is.EqualTo(MoveToOrderTypeId));
        }

        [Test]
        public void OrderContinuationSystem_PublishesEntityAdmissionForFollowUpOrder()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            admissionResults.BeginLogicStep();
            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            var rules = new OrderRuleRegistry();
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer());
            var completed = new OrderTerminalOutcome(
                7,
                MoveToOrderTypeId,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                actor);
            orderTypes.PublishTerminalResult(in completed);
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Assert.That(continuations.TryAdd(7, new Order
            {
                OrderId = 8,
                OrderTypeId = CastAbilityOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Queued,
                Args = new OrderArgs { I0 = 0 }
            }), Is.True);
            var system = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);

            system.Update(0f);

            Assert.That(
                admissionResults.TryGet(8, OrderAdmissionStage.EntityIntake, out var outcome),
                Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.Queued));
        }

        [Test]
        public void OrderContinuationSystem_InvalidFollowUpOrderTypeFailsTypedWithoutRetry()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            admissionResults.BeginLogicStep();
            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            var rules = new OrderRuleRegistry();
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer());
            var completed = new OrderTerminalOutcome(
                7,
                MoveToOrderTypeId,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                actor);
            orderTypes.PublishTerminalResult(in completed);
            const int lateRegisteredOrderTypeId = 200;
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Assert.That(continuations.TryAdd(7, new Order
            {
                OrderId = 8,
                OrderTypeId = lateRegisteredOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Queued,
                Args = new OrderArgs { I0 = 0 }
            }), Is.True);
            var system = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);

            system.Update(0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            Assert.That(buffer.QueuedCount, Is.Zero);
            Assert.That(world.Get<OrderContinuationBuffer>(actor).HasEntries, Is.False);
            Assert.That(
                admissionResults.TryGet(8, OrderAdmissionStage.EntityIntake, out var outcome),
                Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedInvalidOrderType));
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(2));
            Assert.That(orderTypes.TerminalResults[1].OrderId, Is.EqualTo(8));
            Assert.That(orderTypes.TerminalResults[1].State, Is.EqualTo(OrderTerminalState.Failed));
            Assert.That(orderTypes.TerminalResults[1].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionInvalidOrderType));
        }

        [Test]
        public void OrderContinuationSystem_AdmissionCapacityMiss_FailsWholeFollowUpBatchWithTypedTerminal()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 8);
            admissionResults.BeginLogicStep();
            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            var rules = new OrderRuleRegistry();
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer());
            var completed = new OrderTerminalOutcome(
                7,
                MoveToOrderTypeId,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                actor);
            orderTypes.PublishTerminalResult(in completed);
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Assert.That(continuations.TryAdd(7, CreateFollowUpOrder(actor, 8)), Is.True);
            Assert.That(continuations.TryAdd(7, CreateFollowUpOrder(actor, 9)), Is.True);
            var system = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);

            system.Update(0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            Assert.That(buffer.QueuedCount, Is.Zero);
            Assert.That(world.Get<OrderContinuationBuffer>(actor).HasEntries, Is.False);
            Assert.That(admissionResults.TryGet(8, OrderAdmissionStage.EntityIntake, out var first), Is.True);
            Assert.That(admissionResults.TryGet(9, OrderAdmissionStage.EntityIntake, out var second), Is.True);
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            Assert.That(second.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(3));
            Assert.That(orderTypes.TerminalResults[1].OrderId, Is.EqualTo(8));
            Assert.That(orderTypes.TerminalResults[1].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionAdmissionCapacity));
            Assert.That(orderTypes.TerminalResults[2].OrderId, Is.EqualTo(9));
            Assert.That(orderTypes.TerminalResults[2].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionAdmissionCapacity));
        }

        [Test]
        public void OrderContinuationSystem_ProjectedQueueFullRejectsWholeFollowUpBatch()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            admissionResults.BeginLogicStep();
            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            var rules = new OrderRuleRegistry();
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer());
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            for (int i = 0; i < 7; i++)
            {
                Assert.That(
                    buffer.Enqueue(CreateFollowUpOrder(actor, 100 + i, MoveToOrderTypeId), priority: 60, expireStep: -1, insertStep: i),
                    Is.True);
            }

            var completed = new OrderTerminalOutcome(
                7,
                MoveToOrderTypeId,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                actor);
            orderTypes.PublishTerminalResult(in completed);
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Assert.That(continuations.TryAdd(7, CreateFollowUpOrder(actor, 8)), Is.True);
            Assert.That(continuations.TryAdd(7, CreateFollowUpOrder(actor, 9)), Is.True);
            var system = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);

            system.Update(0f);

            Assert.That(buffer.QueuedCount, Is.EqualTo(7));
            Assert.That(world.Get<OrderContinuationBuffer>(actor).HasEntries, Is.False);
            Assert.That(admissionResults.TryGet(8, OrderAdmissionStage.EntityIntake, out var first), Is.True);
            Assert.That(admissionResults.TryGet(9, OrderAdmissionStage.EntityIntake, out var second), Is.True);
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(second.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(3));
            Assert.That(orderTypes.TerminalResults[1].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionQueueFull));
            Assert.That(orderTypes.TerminalResults[2].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionQueueFull));
        }

        [Test]
        public void OrderContinuationSystem_DestroyedActorFailsOwnedFollowUpsWithTerminalAndAdmission()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            admissionResults.BeginLogicStep();
            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            var rules = new OrderRuleRegistry();
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer());
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Assert.That(continuations.TryAdd(7, new Order
            {
                OrderId = 8,
                OrderTypeId = CastAbilityOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Queued,
                Args = new OrderArgs { I0 = 0 }
            }), Is.True);
            _ = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);

            world.Destroy(actor);

            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(8));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Failed));
            Assert.That(orderTypes.TerminalResults[0].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionInvalidActor));
            Assert.That(
                admissionResults.TryGet(8, OrderAdmissionStage.EntityIntake, out var outcome),
                Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
        }

        [Test]
        public void OrderContinuationSystem_PreflightThrow_CancelsReservationsSoLogicStepCanEnd()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            admissionResults.BeginLogicStep();
            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8,
                SpatialBlackboardKey = 3
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            var rules = new OrderRuleRegistry();
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer(),
                new BlackboardSpatialBuffer(),
                new OrderSpatialPayloadBuffer());
            var completed = new OrderTerminalOutcome(
                7,
                MoveToOrderTypeId,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                actor);
            orderTypes.PublishTerminalResult(in completed);

            var followUp = new Order
            {
                OrderId = 8,
                OrderTypeId = CastAbilityOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.List,
                        PointCount = OrderSpatial.MaxInlinePoints + 1
                    }
                }
            };
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Assert.That(continuations.TryAdd(7, in followUp), Is.True);
            var system = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);

            Assert.Throws<InvalidOperationException>(() => system.Update(0f));
            Assert.That(admissionResults.ReservedCount, Is.EqualTo(0));
            Assert.That(continuations.HasEntries, Is.True);
            Assert.That(continuations.CountByTrigger(7), Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(7));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Completed));
            Assert.That(admissionResults.TryGet(8, OrderAdmissionStage.EntityIntake, out _), Is.False);
            Assert.DoesNotThrow(() => admissionResults.EndEntityIntake());
            Assert.DoesNotThrow(() => admissionResults.EndLogicStep());
        }

        [Test]
        public void OrderContinuationSystem_DestroyedActorAdmissionCapacityMiss_FailsOwnedFollowUpsWithoutPartialReservation()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 8);
            admissionResults.BeginLogicStep();
            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            var rules = new OrderRuleRegistry();
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new OrderContinuationBuffer());
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Assert.That(continuations.TryAdd(7, CreateFollowUpOrder(actor, 8)), Is.True);
            Assert.That(continuations.TryAdd(7, CreateFollowUpOrder(actor, 9)), Is.True);
            _ = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);

            world.Destroy(actor);

            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(2));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(8));
            Assert.That(orderTypes.TerminalResults[0].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionAdmissionCapacity));
            Assert.That(orderTypes.TerminalResults[1].OrderId, Is.EqualTo(9));
            Assert.That(orderTypes.TerminalResults[1].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionAdmissionCapacity));
            Assert.That(admissionResults.TryGet(8, OrderAdmissionStage.EntityIntake, out var first), Is.True);
            Assert.That(admissionResults.TryGet(9, OrderAdmissionStage.EntityIntake, out var second), Is.True);
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            Assert.That(second.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
        }

        [Test]
        public void OrderBufferSystem_EntityIntakeRejectAfterGlobalIntakeQueued_PublishesFailedTerminalAndClearsContinuation()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            admissionResults.BeginLogicStep();
            var orderQueue = new OrderQueue(8, admissionResults);
            var orderTypes = CreateOrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastAbilityOrderTypeId,
                Label = "Cast",
                Priority = 100,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            var rules = new OrderRuleRegistry();
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
                new OrderContinuationBuffer(),
                OrderBuffer.CreateEmpty());

            Assert.That(
                planner.Submit(CreateCastOrder(actor, targetXcm: 900, submitMode: OrderSubmitMode.Immediate)),
                Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(orderQueue.TryPeek(out Order moveOrder), Is.True);
            Assert.That(moveOrder.OrderTypeId, Is.EqualTo(MoveToOrderTypeId));
            ref var continuations = ref world.Get<OrderContinuationBuffer>(actor);
            Assert.That(continuations.CountByTrigger(moveOrder.OrderId), Is.EqualTo(1));

            // GlobalIntake already accepted; EntityIntake must fail with a Failed terminal so follow-ups clear.
            world.Remove<OrderBuffer>(actor);

            var bufferSystem = new OrderBufferSystem(
                world,
                clock,
                orderTypes,
                rules,
                admissionResults,
                orderQueue,
                stepRateHz: 30,
                closeEntityIntakeOnUpdate: false);
            bufferSystem.Update(0f);

            Assert.That(orderQueue.Count, Is.EqualTo(0));
            Assert.That(
                admissionResults.TryGet(moveOrder.OrderId, OrderAdmissionStage.EntityIntake, out var intake),
                Is.True);
            Assert.That(intake.Result, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
            Assert.That(orderTypes.TerminalResults.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(moveOrder.OrderId));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Failed));

            var continuationSystem = new OrderContinuationSystem(world, clock, orderTypes, rules, admissionResults);
            continuationSystem.Update(0f);

            Assert.That(world.Get<OrderContinuationBuffer>(actor).HasEntries, Is.False);
            Assert.That(orderTypes.TerminalResults.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(orderTypes.TerminalResults[1].State, Is.EqualTo(OrderTerminalState.Failed));
            Assert.DoesNotThrow(() => admissionResults.EndEntityIntake());
            Assert.DoesNotThrow(() => admissionResults.EndLogicStep());
        }

        private static OrderQueue CreateOrderQueue(int capacity = 64)
        {
            return new OrderQueue(capacity, new OrderAdmissionResultBuffer(capacity, capacity));
        }

        private static OrderTypeRegistry CreateOrderTypeRegistry()
        {
            return new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
        }

        private static AbilityDefinitionRegistry CreateAbilityRegistry(float rangeCm)
        {
            var definition = new AbilityDefinition
            {
                HasTargeting = true,
                Targeting = new AbilityTargetingConfig
                {
                    CastRangeCm = rangeCm
                }
            };

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

        private static Order CreateFollowUpOrder(
            Entity actor,
            int orderId,
            int orderTypeId = CastAbilityOrderTypeId)
        {
            return new Order
            {
                OrderId = orderId,
                OrderTypeId = orderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Queued,
                Args = new OrderArgs { I0 = 0 }
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
