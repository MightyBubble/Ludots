using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
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
            var orderQueue = new OrderQueue();
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

            Assert.That(planner.TrySubmit(in castOrder), Is.True);
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
        public void CompositeOrderPlanner_QueuedCast_UsesProjectedMoveEndpoint()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue();
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

            Assert.That(planner.TrySubmit(in queuedCast), Is.True);
            Assert.That(orderQueue.TryDequeue(out var submittedOrder), Is.True);
            Assert.That(submittedOrder.OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Assert.That(submittedOrder.SubmitMode, Is.EqualTo(OrderSubmitMode.Queued));
            Assert.That(world.Has<OrderContinuationBuffer>(actor), Is.False);
        }

        [Test]
        public void OrderContinuationSystem_QueuesFollowUpAheadOfLaterQueuedCommands()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            clock.Advance(ClockDomainId.Step, 12);

            var orderTypes = new OrderTypeRegistry();
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
                new OrderContinuationBuffer(),
                new CompletedOrderSignal { OrderId = 7, OrderTypeId = MoveToOrderTypeId });

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

            var system = new OrderContinuationSystem(world, clock, orderTypes, rules);
            system.Update(0f);

            ref var updatedSignal = ref world.Get<CompletedOrderSignal>(actor);
            Assert.That(updatedSignal.OrderId, Is.EqualTo(0));
            Assert.That(buffer.QueuedCount, Is.EqualTo(2));
            Assert.That(buffer.GetQueued(0).Order.OrderTypeId, Is.EqualTo(CastAbilityOrderTypeId));
            Assert.That(buffer.GetQueued(1).Order.OrderTypeId, Is.EqualTo(MoveToOrderTypeId));
        }

        private static AbilityDefinitionRegistry CreateAbilityRegistry(float rangeCm)
        {
            var registry = new AbilityDefinitionRegistry();
            registry.Register(TestAbilityId, new AbilityDefinition
            {
                HasTargeting = true,
                Targeting = new AbilityTargetingConfig
                {
                    CastRangeCm = rangeCm
                }
            });
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
