using System;
using Arch.Core;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public ref struct GraphTargetList
    {
        private Span<Entity> _buffer;
        public int Count;

        public GraphTargetList(Span<Entity> buffer)
        {
            _buffer = buffer;
            Count = 0;
        }

        public Span<Entity> Span => _buffer.Slice(0, Count);

        public void SetCount(int count)
        {
            if (count < 0) count = 0;
            if (count > _buffer.Length) count = _buffer.Length;
            Count = count;
        }
    }

    public enum GraphRelationshipFilterMode : int
    {
        Hostile = 1,
        Friendly = 2,
        Neutral = 3,
        NotFriendly = 4,
        NotHostile = 5
    }
}
