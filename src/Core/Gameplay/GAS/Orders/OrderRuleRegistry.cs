using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public unsafe struct OrderRuleSet
    {
        public const int MAX_BLOCKED_ACTIVE_ORDER_TYPES = 8;
        public const int MAX_INTERRUPTS_ACTIVE_ORDER_TYPES = 8;

        public fixed int BlockedActiveOrderTypeIds[MAX_BLOCKED_ACTIVE_ORDER_TYPES];
        public int BlockedActiveCount;

        public fixed int InterruptsActiveOrderTypeIds[MAX_INTERRUPTS_ACTIVE_ORDER_TYPES];
        public int InterruptsActiveCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Blocks(int activeOrderTypeId)
        {
            if (activeOrderTypeId <= 0) return false;
            fixed (int* blocked = BlockedActiveOrderTypeIds)
            {
                for (int i = 0; i < BlockedActiveCount; i++)
                {
                    if (blocked[i] == activeOrderTypeId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Interrupts(int activeOrderTypeId)
        {
            if (activeOrderTypeId <= 0) return false;
            fixed (int* interrupts = InterruptsActiveOrderTypeIds)
            {
                for (int i = 0; i < InterruptsActiveCount; i++)
                {
                    if (interrupts[i] == activeOrderTypeId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public sealed class OrderRuleRegistry
    {
        private readonly OrderRuleSet[] _rules = new OrderRuleSet[OrderTypeRegistry.MaxOrderTypes];
        private readonly ulong[] _hasBits = new ulong[OrderTypeRegistry.MaxOrderTypes >> 6];

        public void Clear()
        {
            Array.Clear(_rules, 0, _rules.Length);
            Array.Clear(_hasBits, 0, _hasBits.Length);
        }

        public void Register(int orderTypeId, in OrderRuleSet ruleSet)
        {
            if (orderTypeId <= 0 || (uint)orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                throw new ArgumentOutOfRangeException(nameof(orderTypeId));
            }

            _rules[orderTypeId] = ruleSet;
            int word = orderTypeId >> 6;
            int bit = orderTypeId & 63;
            _hasBits[word] |= 1UL << bit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasRule(int orderTypeId)
        {
            if ((uint)orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                return false;
            }

            int word = orderTypeId >> 6;
            int bit = orderTypeId & 63;
            return (_hasBits[word] & (1UL << bit)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly OrderRuleSet Get(int orderTypeId)
        {
            if ((uint)orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                throw new ArgumentOutOfRangeException(nameof(orderTypeId));
            }

            return ref _rules[orderTypeId];
        }
    }
}
