using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.AI.WorldState
{
    public readonly struct WorldStateCondition256
    {
        public readonly WorldStateBits256 Mask;
        public readonly WorldStateBits256 Values;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WorldStateCondition256(in WorldStateBits256 mask, in WorldStateBits256 values)
        {
            Mask = mask;
            Values = values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Match(in WorldStateBits256 state)
        {
            return state.Match(in Mask, in Values);
        }
    }
}

