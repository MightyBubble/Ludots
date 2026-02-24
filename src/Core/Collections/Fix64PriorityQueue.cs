using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Collections
{
    /// <summary>
    /// Priority queue using Fix64 for deterministic priority comparison.
    /// Same binary min-heap implementation as PriorityQueue&lt;T&gt; but with Fix64 priority.
    /// </summary>
    public class Fix64PriorityQueue<T>
    {
        private struct Node
        {
            public T Item;
            public Fix64 Priority;
        }

        private Node[] _nodes;
        private int _count;

        public int Count => _count;

        public Fix64PriorityQueue(int capacity = 64)
        {
            _nodes = new Node[capacity];
            _count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(T item, Fix64 priority)
        {
            if (_count >= _nodes.Length)
            {
                Array.Resize(ref _nodes, _nodes.Length * 2);
            }

            _nodes[_count] = new Node { Item = item, Priority = priority };
            HeapifyUp(_count);
            _count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out T result, out Fix64 priority)
        {
            if (_count == 0)
            {
                result = default;
                priority = Fix64.Zero;
                return false;
            }

            result = _nodes[0].Item;
            priority = _nodes[0].Priority;
            _count--;

            _nodes[0] = _nodes[_count];
            _nodes[_count] = default;

            if (_count > 0)
            {
                HeapifyDown(0);
            }

            return true;
        }

        public void Clear()
        {
            Array.Clear(_nodes, 0, _count);
            _count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (_nodes[index].Priority >= _nodes[parentIndex].Priority)
                {
                    break;
                }

                Swap(index, parentIndex);
                index = parentIndex;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HeapifyDown(int index)
        {
            while (true)
            {
                int leftChild = 2 * index + 1;
                int rightChild = 2 * index + 2;
                int smallest = index;

                if (leftChild < _count && _nodes[leftChild].Priority < _nodes[smallest].Priority)
                {
                    smallest = leftChild;
                }

                if (rightChild < _count && _nodes[rightChild].Priority < _nodes[smallest].Priority)
                {
                    smallest = rightChild;
                }

                if (smallest == index) break;

                Swap(index, smallest);
                index = smallest;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap(int a, int b)
        {
            var temp = _nodes[a];
            _nodes[a] = _nodes[b];
            _nodes[b] = temp;
        }
    }
}
