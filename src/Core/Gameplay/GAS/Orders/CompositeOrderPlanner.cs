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
            if (!TryBuildMoveThenCastPlan(in order, out var primaryMove, out var followUpCast))
            {
                var passthrough = order;
                return _incomingOrders.SubmitAssigned(ref passthrough);
            }

            if (!_world.IsAlive(order.Actor))
            {
                return OrderSubmitResult.RejectedInvalidActor;
            }

            if (!_world.Has<OrderContinuationBuffer>(order.Actor))
            {
                _world.Add(order.Actor, new OrderContinuationBuffer());
            }

            _incomingOrders.EnsureOrderId(ref primaryMove);

            ref var continuations = ref _world.Get<OrderContinuationBuffer>(order.Actor);
            if (!continuations.TryAdd(primaryMove.OrderId, in followUpCast))
            {
                return OrderSubmitResult.RejectedQueueFull;
            }

            OrderSubmitResult result = _incomingOrders.SubmitAssigned(ref primaryMove);
            if (result == OrderSubmitResult.Queued)
            {
                return result;
            }

            continuations.RemoveByTrigger(primaryMove.OrderId);
            return result;
        }

        private bool TryBuildMoveThenCastPlan(in Order order, out Order moveOrder, out Order followUpCast)
        {
            moveOrder = default;
            followUpCast = default;

            if (_abilities == null ||
                _castAbilityOrderTypeId <= 0 ||
                _moveToOrderTypeId <= 0 ||
                order.OrderTypeId != _castAbilityOrderTypeId ||
                !_world.IsAlive(order.Actor))
            {
                return false;
            }

            if (!TryResolveCastRangeCm(in order, out float castRangeCm) ||
                castRangeCm <= 0f ||
                !TryResolvePlanningOrigin(in order, out var actorWorldCm) ||
                !TryResolveCastTargetWorldCm(in order, out var targetWorldCm) ||
                !TryResolveMoveAnchor(actorWorldCm, targetWorldCm, castRangeCm, out var moveAnchorWorldCm))
            {
                return false;
            }

            moveOrder = CreateMoveOrder(in order, moveAnchorWorldCm);
            followUpCast = order;
            followUpCast.SubmitMode = OrderSubmitMode.Queued;
            return true;
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

        private bool TryResolveCastRangeCm(in Order order, out float rangeCm)
        {
            rangeCm = 0f;
            if (order.Args.I0 < 0 ||
                !_world.Has<AbilityStateBuffer>(order.Actor))
            {
                return false;
            }

            ref var abilities = ref _world.Get<AbilityStateBuffer>(order.Actor);
            if ((uint)order.Args.I0 >= (uint)abilities.Count)
            {
                return false;
            }

            bool hasForm = _world.Has<AbilityFormSlotBuffer>(order.Actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(order.Actor) : default;
            bool hasItemGranted = _world.Has<ItemGrantedSlotBuffer>(order.Actor);
            ItemGrantedSlotBuffer itemGrantedSlots = hasItemGranted ? _world.Get<ItemGrantedSlotBuffer>(order.Actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(order.Actor);
            GrantedSlotBuffer grantedSlots = hasGranted ? _world.Get<GrantedSlotBuffer>(order.Actor) : default;
            AbilitySlotState slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in itemGrantedSlots, hasItemGranted, in grantedSlots, hasGranted, order.Args.I0);

            if (slot.AbilityId <= 0 ||
                !_abilities!.TryGet(slot.AbilityId, out var definition) ||
                !definition.HasTargeting ||
                ShouldBypassMoveThenCastPlanning(in definition))
            {
                return false;
            }

            rangeCm = definition.Targeting.CastRangeCm;
            return rangeCm > 0f;
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
