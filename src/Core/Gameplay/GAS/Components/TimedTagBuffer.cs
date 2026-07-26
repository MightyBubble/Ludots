namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct TimedTagBuffer
    {
        public const int Capacity = 16;

        public int Count;
        public fixed int TagIds[Capacity];
        public fixed int ExpireAt[Capacity];
        public fixed byte ClockIds[Capacity];

        public bool TryAdd(int tagId, int expireAt, GasClockId clockId)
        {
            if (Count >= Capacity) return false;
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

        public int GetTagId(int index)
        {
            fixed (int* tags = TagIds)
            {
                return tags[index];
            }
        }

        public int GetExpireAt(int index)
        {
            fixed (int* expires = ExpireAt)
            {
                return expires[index];
            }
        }

        public GasClockId GetClockId(int index)
        {
            fixed (byte* clocks = ClockIds)
            {
                return (GasClockId)clocks[index];
            }
        }
    }
}
