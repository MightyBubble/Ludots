using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public interface IOrderAdmissionValidator
    {
        OrderSubmitResult Validate(
            World world,
            Entity entity,
            in Order order,
            in OrderBuffer buffer);
    }

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
        public const int MaxAdmissionValidatorsPerOrderType = 8;

        private readonly OrderRuleSet[] _rules = new OrderRuleSet[OrderTypeRegistry.MaxOrderTypes];
        private readonly ulong[] _hasBits = new ulong[OrderTypeRegistry.MaxOrderTypes >> 6];
        private readonly IOrderAdmissionValidator?[] _admissionValidators =
            new IOrderAdmissionValidator[OrderTypeRegistry.MaxOrderTypes * MaxAdmissionValidatorsPerOrderType];
        private readonly byte[] _admissionValidatorCounts = new byte[OrderTypeRegistry.MaxOrderTypes];

        public void Clear()
        {
            Array.Clear(_rules, 0, _rules.Length);
            Array.Clear(_hasBits, 0, _hasBits.Length);
            Array.Clear(_admissionValidators, 0, _admissionValidators.Length);
            Array.Clear(_admissionValidatorCounts, 0, _admissionValidatorCounts.Length);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Interrupts(int orderTypeId, int activeOrderTypeId)
        {
            if (!HasRule(orderTypeId))
            {
                return false;
            }

            return _rules[orderTypeId].Interrupts(activeOrderTypeId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Blocks(int orderTypeId, int activeOrderTypeId)
        {
            if (!HasRule(orderTypeId))
            {
                return false;
            }

            return _rules[orderTypeId].Blocks(activeOrderTypeId);
        }

        public void RegisterAdmissionValidator(
            int orderTypeId,
            IOrderAdmissionValidator validator)
        {
            if (orderTypeId <= 0 || (uint)orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                throw new ArgumentOutOfRangeException(nameof(orderTypeId));
            }

            ArgumentNullException.ThrowIfNull(validator);
            int count = _admissionValidatorCounts[orderTypeId];
            int offset = orderTypeId * MaxAdmissionValidatorsPerOrderType;
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(_admissionValidators[offset + i], validator))
                {
                    throw new InvalidOperationException(
                        $"Order type {orderTypeId} already contains this admission validator instance.");
                }
            }

            if (count >= MaxAdmissionValidatorsPerOrderType)
            {
                throw new InvalidOperationException(
                    $"Order type {orderTypeId} exceeds the admission validator capacity " +
                    $"{MaxAdmissionValidatorsPerOrderType}.");
            }

            _admissionValidators[offset + count] = validator;
            _admissionValidatorCounts[orderTypeId] = checked((byte)(count + 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OrderSubmitResult ValidateAdmission(
            World world,
            Entity entity,
            in Order order,
            in OrderBuffer buffer)
        {
            int orderTypeId = order.OrderTypeId;
            if ((uint)orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                return OrderSubmitResult.InvalidOrderType;
            }

            int count = _admissionValidatorCounts[orderTypeId];
            int offset = orderTypeId * MaxAdmissionValidatorsPerOrderType;
            for (int i = 0; i < count; i++)
            {
                IOrderAdmissionValidator validator = _admissionValidators[offset + i]
                    ?? throw new InvalidOperationException(
                        $"Order type {orderTypeId} admission validator slot {i} is empty.");
                OrderSubmitResult result = validator.Validate(world, entity, in order, in buffer);
                if (result != OrderSubmitResult.Activated)
                {
                    return result;
                }
            }

            return OrderSubmitResult.Activated;
        }
    }
}
