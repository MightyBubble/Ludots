using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public enum ModifierOp : byte
    {
        Add = 0,
        Multiply = 1,
        Override = 2
    }

    public struct ModifierData
    {
        public int AttributeId;
        public ModifierOp Operation;
        public float Value;
    }

    /// <summary>
    /// Stores a fixed number of modifiers for an effect entity.
    /// If an effect needs more than 8 modifiers, split it into multiple effect entities.
    /// </summary>
    public unsafe struct EffectModifiers
    {
        public const int CAPACITY = GasConstants.EFFECT_MODIFIERS_CAPACITY;
        public fixed int AttributeIds[CAPACITY];
        public fixed byte Operations[CAPACITY];
        public fixed float Values[CAPACITY];
        public int Count;

        public bool Add(int attrId, ModifierOp op, float val)
        {
            if (Count >= CAPACITY) return false;
            AttributeIds[Count] = attrId;
            Operations[Count] = (byte)op;
            Values[Count] = val;
            Count++;
            return true;
        }

        public ModifierData Get(int index)
        {
            if (index < 0 || index >= Count) return default;
            return new ModifierData 
            { 
                AttributeId = AttributeIds[index], 
                Operation = (ModifierOp)Operations[index], 
                Value = Values[index] 
            };
        }

    }
}
