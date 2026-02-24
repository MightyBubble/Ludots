namespace Ludots.Core.Gameplay.GAS.Components
{
    public enum OpCode : byte
    {
        None = 0,
        ApplyEffect = 1,  // Data1: EffectTemplateId (if using templates) or logic to create effect
        ModifyAttribute = 2, // Data1: AttrId, Data2: Value (Simple mod)
        SendEvent = 3, // Data1: EventTagId
        SpawnObject = 4, // Placeholder
        ApplyInstantEffect = 5
    }

    public enum SourceType : byte
    {
        Constant = 0,
        Attribute = 1 // Data2 is coefficient, Data1 is AttributeId
    }

    public struct Instruction
    {
        public OpCode Op;
        public SourceType SrcType;
        public int Data1;
        public float Data2;
    }

    public unsafe struct InstructionBuffer
    {
        public const int CAPACITY = 8;
        
        public fixed byte Ops[CAPACITY];
        public fixed byte SrcTypes[CAPACITY];
        public fixed int Data1s[CAPACITY];
        public fixed float Data2s[CAPACITY];
        
        public int Count;

        public bool Add(OpCode op, int d1, float d2, SourceType src = SourceType.Constant)
        {
            if (Count >= CAPACITY) return false;
            Ops[Count] = (byte)op;
            SrcTypes[Count] = (byte)src;
            Data1s[Count] = d1;
            Data2s[Count] = d2;
            Count++;
            return true;
        }

        public Instruction Get(int index)
        {
            if (index < 0 || index >= Count) return default;
            return new Instruction
            {
                Op = (OpCode)Ops[index],
                SrcType = (SourceType)SrcTypes[index],
                Data1 = Data1s[index],
                Data2 = Data2s[index]
            };
        }
    }
}
