using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class ReactionSystem : BaseSystem<World, float>
    {
        public const string ActivationCapacityExceededError = "GAS.REACTION.ERR.ActivationCapacityExceeded";
        public const string OrderSubmitRejectedError = "GAS.REACTION.ERR.OrderSubmitRejected";

        private readonly OrderQueue _orderQueue;
        private readonly int _castAbilityOrderTypeId;
        private readonly GameplayEventBus _eventBus;
        private readonly Activation[] _activations;
        private int _activationCount;

        public ReactionSystem(
            World world,
            OrderQueue orderQueue,
            int castAbilityOrderTypeId,
            GameplayEventBus eventBus,
            int activationCapacity = 4096) : base(world)
        {
            _orderQueue = orderQueue ?? throw new System.ArgumentNullException(nameof(orderQueue));
            if (castAbilityOrderTypeId <= 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(castAbilityOrderTypeId),
                    castAbilityOrderTypeId,
                    "castAbilityOrderTypeId must be positive.");
            }

            if (activationCapacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(activationCapacity),
                    activationCapacity,
                    "activationCapacity must be positive.");
            }

            _castAbilityOrderTypeId = castAbilityOrderTypeId;
            _eventBus = eventBus ?? throw new System.ArgumentNullException(nameof(eventBus));
            _activations = new Activation[activationCapacity];
        }

        public override unsafe void Update(in float dt)
        {
            var events = _eventBus.Events;
            _activationCount = 0;

            // Direct iteration is actually optimal for small N (N < 10000).
            // Complexity is O(Events). Inner loop is O(Reactions per Entity), usually very small (< 5).
            // The previous issue might be cache misses or repeated World.Has/Get calls.
            // Optimization: Skip World.IsAlive check if we trust the EventBus source (or check it once).
            // Optimization: Use `TryGet` to avoid double lookup (Has + Get).

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                
                // Optimized: Use Has + Get to get ref instead of TryGet which returns a copy
                // This avoids value type copying overhead for ReactionBuffer struct
                if (World.IsAlive(evt.Target) && World.Has<ReactionBuffer>(evt.Target))
                {
                    ref var reactions = ref World.Get<ReactionBuffer>(evt.Target);
                    
                    for (int j = 0; j < reactions.Count; j++)
                    {
                        if (reactions.EventTagIds[j] == evt.TagId)
                        {
                            if (_activationCount >= _activations.Length)
                            {
                                throw new System.InvalidOperationException(
                                    $"{ActivationCapacityExceededError}: capacity={_activations.Length}, eventIndex={i}, reactionIndex={j}, actor={evt.Target.Id}, eventTagId={evt.TagId}.");
                            }

                            _activations[_activationCount++] = new Activation
                            {
                                Caster = evt.Target,
                                SlotIndex = reactions.AbilitySlots[j],
                                Source = evt.Source,
                                EventTagId = evt.TagId,
                            };
                        }
                    }
                }
            }

            for (int i = 0; i < _activationCount; i++)
            {
                var activation = _activations[i];
                Order order = OrderBuilder.CreateCastAbility(
                    _castAbilityOrderTypeId,
                    playerId: 0,
                    actor: activation.Caster,
                    target: activation.Source,
                    targetContext: Entity.Null,
                    abilitySlotIndex: activation.SlotIndex,
                    submitMode: OrderSubmitMode.Immediate,
                    submitStep: 0);
                OrderSubmitResult result = _orderQueue.SubmitAssigned(ref order);
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    throw new System.InvalidOperationException(
                        $"{OrderSubmitRejectedError}: result={result}, orderId={order.OrderId}, actor={activation.Caster.Id}, eventTagId={activation.EventTagId}, slot={activation.SlotIndex}.");
                }
            }
        }

        private struct Activation
        {
            public Entity Caster;
            public int SlotIndex;
            public Entity Source;
            public int EventTagId;
        }
    }
}
