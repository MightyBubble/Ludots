using System;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public ref struct GraphIntIdList
    {
        private Span<int> _buffer;
        public int Count;

        public GraphIntIdList(Span<int> buffer)
        {
            _buffer = buffer;
            Count = 0;
        }

        public Span<int> Span => _buffer.Slice(0, Count);

        public void SetCount(int count)
        {
            if (count < 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidIntIdListCount: count={count}.");
            }

            if (count > _buffer.Length)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.IntIdListCapacityExceeded: count={count}, capacity={_buffer.Length}.");
            }

            Count = count;
        }
    }
}
