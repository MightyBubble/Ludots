namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct GameplayTagEffectiveCache
    {
        public fixed ulong Bits[4];

        public bool Has(int tagId)
        {
            if ((uint)tagId >= 256u) return false;
            int word = tagId >> 6;
            int bit = tagId & 63;
            return (Bits[word] & (1UL << bit)) != 0;
        }

        public void Set(int tagId, bool value)
        {
            if ((uint)tagId >= 256u) return;
            int word = tagId >> 6;
            int bit = tagId & 63;
            ulong mask = 1UL << bit;
            Bits[word] = value ? (Bits[word] | mask) : (Bits[word] & ~mask);
        }
    }
}

