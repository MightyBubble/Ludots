namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Maps Event Tags to Ability Slots.
    /// When Event[TagId] happens, trigger AbilityStateBuffer[SlotIndex].
    /// </summary>
    public unsafe struct ReactionBuffer
    {
        public const int CAPACITY = 8;
        public fixed int EventTagIds[CAPACITY];
        public fixed int AbilitySlots[CAPACITY];
        public int Count;

        public bool Add(int eventTagId, int abilitySlotIndex)
        {
            if (Count >= CAPACITY) return false;
            EventTagIds[Count] = eventTagId;
            AbilitySlots[Count] = abilitySlotIndex;
            Count++;
            return true;
        }
    }
}
