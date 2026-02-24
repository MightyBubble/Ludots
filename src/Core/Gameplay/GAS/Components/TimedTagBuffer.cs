namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct TimedTagBuffer
    {
        public int Count;
        public fixed int TagIds[16];
        public fixed int ExpireAt[16];
        public fixed byte ClockIds[16];

        public bool TryAdd(int tagId, int expireAt, GasClockId clockId)
        {
            if (Count >= 16) return false;
            fixed (int* tags = TagIds) tags[Count] = tagId;
            fixed (int* exp = ExpireAt) exp[Count] = expireAt;
            fixed (byte* clocks = ClockIds) clocks[Count] = (byte)clockId;
            Count++;
            return true;
        }

        public void RemoveAtSwapBack(int index)
        {
            Count--;
            if (index == Count) return;
            fixed (int* tags = TagIds) tags[index] = tags[Count];
            fixed (int* exp = ExpireAt) exp[index] = exp[Count];
            fixed (byte* clocks = ClockIds) clocks[index] = clocks[Count];
        }
    }
}
