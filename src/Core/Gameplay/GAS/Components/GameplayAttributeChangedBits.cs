using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Presentation dirty bits per attribute id. Absolute-max fixed layout; validate against plan on write.
    /// </summary>
    public unsafe struct GameplayAttributeChangedBits
    {
        public const int Capacity = GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots;

        public fixed byte Bits[Capacity];

        public void Mark(int attributeId)
        {
            ValidateAttributeId(attributeId);
            Bits[attributeId] = 1;
        }

        public bool IsSet(int attributeId)
        {
            ValidateAttributeId(attributeId);
            return Bits[attributeId] != 0;
        }

        public bool IsAnyBitSet()
        {
            int slots = ActiveAttributeSlotCount();
            for (int i = 0; i < slots; i++)
            {
                if (Bits[i] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            int slots = ActiveAttributeSlotCount();
            for (int i = 0; i < slots; i++)
            {
                Bits[i] = 0;
            }
        }

        private static int ActiveAttributeSlotCount()
        {
            return GasLoadTimeCapacitySession.IsFrozen
                ? GasLoadTimeCapacitySession.Plan.AttributeSlotCount
                : Capacity;
        }

        private static void ValidateAttributeId(int attributeId)
        {
            int slots = ActiveAttributeSlotCount();
            if ((uint)attributeId >= (uint)slots)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attributeId),
                    attributeId,
                    $"attributeId must be in [0, {slots - 1}].");
            }
        }
    }
}
