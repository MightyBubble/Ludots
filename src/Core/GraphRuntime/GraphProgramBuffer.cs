namespace Ludots.Core.GraphRuntime
{
    public unsafe struct GraphProgramBuffer
    {
        public const int CAPACITY = 128;

        public fixed ushort Ops[CAPACITY];
        public fixed byte Dsts[CAPACITY];
        public fixed byte As[CAPACITY];
        public fixed byte Bs[CAPACITY];
        public fixed byte Cs[CAPACITY];
        public fixed byte Flags[CAPACITY];
        public fixed int Imms[CAPACITY];
        public fixed float ImmFs[CAPACITY];
        public int Count;

        public void Clear() => Count = 0;

        public void Add(ushort op, byte dst = 0, byte a = 0, byte b = 0, byte c = 0, int imm = 0, float immF = 0f, byte flags = 0)
        {
            if ((uint)Count >= CAPACITY) return;
            Ops[Count] = op;
            Dsts[Count] = dst;
            As[Count] = a;
            Bs[Count] = b;
            Cs[Count] = c;
            Flags[Count] = flags;
            Imms[Count] = imm;
            ImmFs[Count] = immF;
            Count++;
        }

        public GraphInstruction Get(int index)
        {
            if ((uint)index >= (uint)Count) return default;
            return new GraphInstruction
            {
                Op = Ops[index],
                Dst = Dsts[index],
                A = As[index],
                B = Bs[index],
                C = Cs[index],
                Flags = Flags[index],
                Imm = Imms[index],
                ImmF = ImmFs[index]
            };
        }
    }
}

