using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Input.Orders;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// Plans composite command intents before they enter the authoritative order queue.
    /// Keeps input mapping focused on raw semantic orders while reusable planning
    /// decides whether a cast must become move-then-cast.
    /// </summary>
    public sealed class CompositeOrderPlanner
    {
        private enum MoveThenCastPlanState : byte
        {
            NotApplicable = 0,
            Planned = 1,
            Rejected = 2
        }

        private readonly struct MoveThenCastPlanResult
        {
            public MoveThenCastPlanResult(MoveThenCastPlanState state, OrderSubmitResult rejection)
            {
                State = state;
                Rejection = rejection;
            }

            public MoveThenCastPlanState State { get; }
            public OrderSubmitResult Rejection { get; }

            public static MoveThenCastPlanResult NotApplicable() => new(MoveThenCastPlanState.NotApplicable, default);
            public static MoveThenCastPlanResult Planned() => new(MoveThenCastPlanState.Planned, default);
            public static MoveThenCastPlanResult Rejected(OrderSubmitResult rejection) => new(MoveThenCastPlanState.Rejected, rejection);
        }

        private readonly World _world;
        private readonly OrderQueue _incomingOrders;
        private readonly AbilityDefinitionRegistry? _abilities;
        private readonly int _castAbilityOrderTypeId;
        private readonly int _moveToOrderTypeId;

        public CompositeOrderPlanner(
            World world,
            OrderQueue incomingOrders,
            AbilityDefinitionRegistry? abilities,
            int castAbilityOrderTypeId,
            int moveToOrderTypeId)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _incomingOrders = incomingOrders ?? throw new ArgumentNullException(nameof(incomingOrders));
            _abilities = abilities;
            _castAbilityOrderTypeId = castAbilityOrderTypeId;
            _moveToOrderTypeId = moveToOrderTypeId;
        }

        public OrderSubmitResult Submit(in Order order)
        {
            MoveThenCastPlanResult plan = BuildMoveThenCastPlan(in order, out var primaryMove, out var followUpCast);
            if (plan.State == MoveThenCastPlanState.NotApplicable)
            {
                var passthrough = order;
                return _incomingOrders.SubmitAssigned(ref passthrough);
            }

            if (plan.State == MoveThenCastPlanState.Rejected)
            {
                OrderSpatialPayloadOps.Release(_world, in order);
                return plan.Rejection;
            }

            if (!_world.IsAlive(order.Actor))
            {
                return OrderSubmitResult.RejectedInvalidActor;
            }

            OrderContinuationStateInstaller.RequireInstalled(_world, order.Actor);

            try
            {
                _incomingOrders.EnsureOrderId(ref followUpCast);
                _incomingOrders.EnsureOrderId(ref primaryMove);
            }
            catch
            {
                OrderSpatialPayloadOps.Release(_world, in followUpCast);
                throw;
            }

            ref var continuations = ref _world.Get<OrderContinuationBuffer>(order.Actor);
            if (!continuations.TryAdd(primaryMove.OrderId, in followUpCast))
            {
                OrderSpatialPayloadOps.Release(_world, in followUpCast);
                return OrderSubmitResult.RejectedQueueFull;
            }

            OrderSubmitResult result;
            try
            {
                result = _incomingOrders.SubmitAssigned(ref primaryMove);
            }
            catch
            {
                ReleaseRegisteredContinuations(ref continuations, primaryMove.OrderId);
                throw;
            }
            if (result == OrderSubmitResult.Queued)
            {
                return result;
            }

            ReleaseRegisteredContinuations(ref continuations, primaryMove.OrderId);
            return result;
        }

        private void ReleaseRegisteredContinuations(
            ref OrderContinuationBuffer continuations,
            int triggerOrderId)
        {
            Span<Order> removed = stackalloc Order[OrderContinuationBuffer.MAX_CONTINUATIONS];
            int count = continuations.Extract(triggerOrderId, removed);
            for (int i = 0; i < count; i++)
            {
                Order order = removed[i];
                OrderSpatialPayloadOps.Release(_world, in order);
            }
        }

        public OrderSubmitResult TrySubmitSharedBatch(Span<Order> orders)
        {
            for (int i = 0; i < orders.Length; i++)
            {
                MoveThenCastPlanResult plan = BuildMoveThenCastPlan(in orders[i], out _, out _);
                if (plan.State == MoveThenCastPlanState.Rejected)
                {
                    return plan.Rejection;
                }

                if (plan.State == MoveThenCastPlanState.Planned)
                {
                    throw new InvalidOperationException(
                        "CompositeOrderPlanner cannot split a shared order batch into move-then-cast continuations without breaking the shared OrderId boundary.");
                }
            }

            return _incomingOrders.TryEnqueueSharedBatch(orders);
        }

        public OrderSubmitResult TrySubmitClusteredBatch(Span<Order> orders)
        {
            for (int i = 0; i < orders.Length; i++)
            {
                MoveThenCastPlanResult plan = BuildMoveThenCastPlan(in orders[i], out _, out _);
                if (plan.State == MoveThenCastPlanState.Rejected)
                {
                    return plan.Rejection;
                }

                if (plan.State == MoveThenCastPlanState.Planned)
                {
                    throw new InvalidOperationException(
                        "CompositeOrderPlanner cannot split a clustered command batch into move-then-cast continuations.");
                }
            }

            return _incomingOrders.TryEnqueueClusteredBatch(orders);
        }

        private MoveThenCastPlanResult BuildMoveThenCastPlan(in Order order, out Order moveOrder, out Order followUpCast)
        {
            moveOrder = default;
            followUpCast = default;

            if (order.OrderTypeId != _castAbilityOrderTypeId)
            {
                return MoveThenCastPlanResult.NotApplicable();
            }

            if (_castAbilityOrderTypeId <= 0 ||
                _moveToOrderTypeId <= 0 ||
                _abilities == null)
            {
                return MoveThenCastPlanResult.Rejected(OrderSubmitResult.RejectedInvalidOrderType);
            }

            if (!_world.IsAlive(order.Actor))
            {
                return MoveThenCastPlanResult.Rejected(OrderSubmitResult.RejectedInvalidActor);
            }

            MoveThenCastPlanResult rangeResult = ResolveCastRangeCm(in order, out float castRangeCm);
            if (rangeResult.State != MoveThenCastPlanState.Planned)
            {
                return rangeResult;
            }

            if (castRangeCm <= 0f)
            {
                return MoveThenCastPlanResult.Rejected(OrderSubmitResult.RejectedValidation);
            }

            if (!TryResolvePlanningOrigin(in order, out var actorWorldCm))
            {
                return MoveThenCastPlanResult.Rejected(OrderSubmitResult.RejectedValidation);
            }

            if (!TryResolveCastTargetWorldCm(in order, out var targetWorldCm))
            {
                return MoveThenCastPlanResult.Rejected(OrderSubmitResult.RejectedValidation);
            }

            if (!TryResolveMoveAnchor(actorWorldCm, targetWorldCm, castRangeCm, out var moveAnchorWorldCm))
            {
                return MoveThenCastPlanResult.NotApplicable();
            }

            moveOrder = CreateMoveOrder(in order, moveAnchorWorldCm);
            followUpCast = order;
            followUpCast.SubmitMode = OrderSubmitMode.Queued;
            return MoveThenCastPlanResult.Planned();
        }

        private Order CreateMoveOrder(in Order castOrder, Vector3 moveAnchorWorldCm)
        {
            var moveArgs = new OrderArgs();
            moveArgs.Spatial.Kind = OrderSpatialKind.WorldCm;
            moveArgs.Spatial.Mode = OrderCollectionMode.Single;
            moveArgs.Spatial.WorldCm = moveAnchorWorldCm;

            return new Order
            {
                OrderTypeId = _moveToOrderTypeId,
                PlayerId = castOrder.PlayerId,
                Actor = castOrder.Actor,
                Target = default,
                TargetContext = castOrder.TargetContext,
                Args = moveArgs,
                SubmitMode = castOrder.SubmitMode
            };
        }

        private MoveThenCastPlanResult ResolveCastRangeCm(in Order order, out float rangeCm)
        {
            rangeCm = 0f;
            if (order.Args.I0 < 0 ||
                !_world.Has<AbilityStateBuffer>(order.Actor))
            {
                return MoveThenCastPlanResult.Rejected(OrderSubmitResult.RejectedInvalidActor);
            }

            ref var abilities = ref _world.Get<AbilityStateBuffer>(order.Actor);
            if ((uint)order.Args.I0 >= (uint)abilities.Count)
            {
                return MoveThenCastPlanResult.Rejected(OrderSubmitResult.RejectedInvalidActor);
            }

            bool hasForm = _world.Has<AbilityFormSlotBuffer>(order.Actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(order.Actor) : default;
            bool hasItemGranted = _world.Has<ItemGrantedSlotBuffer>(order.Actor);
            ItemGrantedSlotBuffer itemGrantedSlots = hasItemGranted ? _world.Get<ItemGrantedSlotBuffer>(order.Actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(order.Actor);
            GrantedSlotBuffer grantedSlots = hasGranted ? _world.Get<GrantedSlotBuffer>(order.Actor) : default;
            AbilitySlotState slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in itemGrantedSlots, hasItemGranted, in grantedSlots, hasGranted, order.Args.I0);

            if (slot.AbilityId <= 0 ||
                !_abilities!.TryGet(slot.AbilityId, out var definition))
            {
                return MoveThenCastPlanResult.Rejected(OrderSubmitResult.RejectedInvalidOrderType);
            }

            if (!definition.HasTargeting ||
                definition.Targeting.CastRangeCm <= 0f ||
                ShouldBypassMoveThenCastPlanning(in definition))
            {
                return MoveThenCastPlanResult.NotApplicable();
            }

            rangeCm = definition.Targeting.CastRangeCm;
            return MoveThenCastPlanResult.Planned();
        }

        private static bool ShouldBypassMoveThenCastPlanning(in AbilityDefinition definition)
        {
            return definition.HasInputBindingOverride &&
                   definition.InputBindingOverride.HasAutoTargetPolicy &&
                   definition.InputBindingOverride.AutoTargetPolicy != AutoTargetPolicy.None;
        }

        private bool TryResolvePlanningOrigin(in Order order, out Vector3 originWorldCm)
        {
            originWorldCm = default;
            if (!TryGetEntityWorldCm(order.Actor, out originWorldCm))
            {
                return false;
            }

            if (order.SubmitMode != OrderSubmitMode.Queued)
            {
                return true;
            }

            if (TryResolveProjectedQueuedOrigin(order.Actor, out var projectedWorldCm))
            {
                originWorldCm = projectedWorldCm;
            }

            return true;
        }

        private bool TryResolveProjectedQueuedOrigin(Entity actor, out Vector3 projectedWorldCm)
        {
            return OrderWorldSpatialResolver.TryResolveProjectedQueuedOrigin(_world, actor, _moveToOrderTypeId, out projectedWorldCm);
        }

        private bool TryResolveCastTargetWorldCm(in Order order, out Vector3 targetWorldCm)
        {
            if (_world.IsAlive(order.Target) && OrderWorldSpatialResolver.TryGetEntityWorldCm(_world, order.Target, out targetWorldCm))
            {
                return true;
            }

            return OrderWorldSpatialResolver.TryResolveSpatialTarget(_world, in order, out targetWorldCm);
        }

        private static bool TryResolveMoveAnchor(Vector3 actorWorldCm, Vector3 targetWorldCm, float castRangeCm, out Vector3 moveAnchorWorldCm)
        {
            moveAnchorWorldCm = default;
            Vector2 actor = new(actorWorldCm.X, actorWorldCm.Z);
            Vector2 target = new(targetWorldCm.X, targetWorldCm.Z);
            Vector2 delta = target - actor;
            float distanceCm = delta.Length();
            if (distanceCm <= castRangeCm + 0.01f || distanceCm <= 0.01f)
            {
                return false;
            }

            float travelCm = distanceCm - castRangeCm;
            Vector2 direction = delta / distanceCm;
            Vector2 movePoint = actor + (direction * travelCm);
            moveAnchorWorldCm = new Vector3(movePoint.X, actorWorldCm.Y, movePoint.Y);
            return true;
        }

        private bool TryGetEntityWorldCm(Entity entity, out Vector3 worldCm)
        {
            return OrderWorldSpatialResolver.TryGetEntityWorldCm(_world, entity, out worldCm);
        }
    }
}
