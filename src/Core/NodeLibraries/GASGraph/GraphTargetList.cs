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
            if (count < 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidTargetListCount: count={count}.");
            }

            if (count > _buffer.Length)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.TargetListCapacityExceeded: count={count}, capacity={_buffer.Length}.");
            }

            Count = count;
        }
    }

}
