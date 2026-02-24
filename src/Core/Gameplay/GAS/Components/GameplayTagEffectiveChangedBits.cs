namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct GameplayTagEffectiveChangedBits
    {
        public fixed ulong Bits[4];

        public void Mark(int tagId)
        {
            if ((uint)tagId >= 256u) return;
            int word = tagId >> 6;
            int bit = tagId & 63;
            Bits[word] |= 1UL << bit;
        }

        public void Clear()
        {
            Bits[0] = 0;
            Bits[1] = 0;
            Bits[2] = 0;
            Bits[3] = 0;
        }
    }
}

