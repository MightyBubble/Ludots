namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct GameplayAttributeChangedBits
    {
        public fixed byte Bits[AttributeBuffer.MAX_ATTRS];

        public void Mark(int attributeId)
        {
            if ((uint)attributeId >= AttributeBuffer.MAX_ATTRS)
            {
                return;
            }

            Bits[attributeId] = 1;
        }

        public bool IsSet(int attributeId)
        {
            return (uint)attributeId < AttributeBuffer.MAX_ATTRS && Bits[attributeId] != 0;
        }

        public bool IsAnyBitSet()
        {
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
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
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                Bits[i] = 0;
            }
        }
    }
}
