using System;

namespace Ludots.Core.GraphRuntime
{
    public struct GraphInstruction
    {
        public ushort Op;
        public byte Dst;
        public byte A;
        public byte B;
        public byte C;
        public byte Flags;
        public int Imm;
        public float ImmF;
    }
}

