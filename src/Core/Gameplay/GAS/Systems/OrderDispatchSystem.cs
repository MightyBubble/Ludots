using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// OBSOLETE: Replaced by <see cref="OrderBufferSystem"/>.
    /// This class routes orders from a global queue to typed sub-queues.
    /// The new pipeline uses per-entity OrderBuffer with Tag+Blackboard activation.
    /// Retained only for backward-compatible tests during migration.
    /// </summary>
    [Obsolete("Use OrderBufferSystem instead. OrderDispatchSystem will be removed in a future release.")]
    public sealed class OrderDispatchSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;
        private readonly OrderQueue _incoming;
        private readonly OrderQueue _abilityOrders;
        private readonly OrderQueue _chainOrders;
        private readonly OrderQueue _commandOrders;
        private readonly int _castAbilityOrderTagId;
        private readonly int _respondChainOrderTagId;
        private readonly int _moveToOrderTagId;
        private readonly int _attackTargetOrderTagId;
        private readonly int _stopOrderTagId;
        
        /// <summary>
        /// Diagnostic counter: number of orders routed to the default (command) queue
        /// because their OrderTagId did not match any explicit route.
        /// </summary>
        public int DefaultRoutedCount { get; private set; }

        public OrderDispatchSystem(World world, IClock clock, OrderQueue incoming, OrderQueue abilityOrders, OrderQueue chainOrders, OrderQueue commandOrders, int castAbilityOrderTagId, int respondChainOrderTagId, int moveToOrderTagId, int attackTargetOrderTagId, int stopOrderTagId)
            : base(world)
        {
            _clock = clock;
            _incoming = incoming;
            _abilityOrders = abilityOrders;
            _chainOrders = chainOrders;
            _commandOrders = commandOrders;
            _castAbilityOrderTagId = castAbilityOrderTagId;
            _respondChainOrderTagId = respondChainOrderTagId;
            _moveToOrderTagId = moveToOrderTagId;
            _attackTargetOrderTagId = attackTargetOrderTagId;
            _stopOrderTagId = stopOrderTagId;
        }

        public override void Update(in float dt)
        {
            if (_incoming == null) return;

            while (_incoming.TryDequeue(out var order))
            {
                order.SubmitStep = _clock.Now(ClockDomainId.Step);
                if (order.OrderTagId == _castAbilityOrderTagId)
                {
                    _abilityOrders?.TryEnqueue(order);
                    continue;
                }
                if (_respondChainOrderTagId >= 0 && order.OrderTagId == _respondChainOrderTagId)
                {
                    _chainOrders?.TryEnqueue(order);
                    continue;
                }
                if (order.OrderTagId == _moveToOrderTagId || order.OrderTagId == _attackTargetOrderTagId || order.OrderTagId == _stopOrderTagId)
                {
                    _commandOrders?.TryEnqueue(order);
                    continue;
                }
                
                // Default route: forward unrecognized orders to command queue
                // instead of silently dropping them.
                DefaultRoutedCount++;
                _commandOrders?.TryEnqueue(order);
#if DEBUG
                Log.Warn(in LogChannels.GAS, $"Order with unrecognized TagId={order.OrderTagId} routed to command queue (total default-routed: {DefaultRoutedCount})");
#endif
            }
        }
    }
}
