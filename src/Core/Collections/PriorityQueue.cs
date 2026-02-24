using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Collections
{
    /// <summary>
    /// A generic priority queue optimized for pathfinding (e.g., A*).
    /// Uses a binary heap implementation with an internal array.
    /// Supports resizing and fast insertion/extraction.
    /// Zero allocations during Enqueue/Dequeue operations.
    /// </summary>
    /// <typeparam name="T">The type of elements in the queue.</typeparam>
    public class PriorityQueue<T>
    {
        private struct Node
        {
            public T Item;
            public float Priority;
        }

        private Node[] _nodes;
        private int _count;
        
        // Optional: Map item to index for O(1) UpdatePriority or Contains?
        // For simple A*, we often don't need DecreaseKey if we just add duplicates.
        // But for strict 0-GC on complex types, we might. 
        // For now, let's keep it simple: Binary Min-Heap.

        public int Count => _count;

        public PriorityQueue(int capacity = 64)
        {
            _nodes = new Node[capacity];
            _count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(T item, float priority)
        {
            if (_count >= _nodes.Length)
            {
                Resize(_nodes.Length * 2);
            }

            _nodes[_count] = new Node { Item = item, Priority = priority };
            HeapifyUp(_count);
            _count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Dequeue()
        {
            if (_count == 0) throw new InvalidOperationException("Queue is empty");

            T result = _nodes[0].Item;
            _count--;
            
            // Move last item to root
            _nodes[0] = _nodes[_count];
            // Clear the last slot to release references if T is a class
            _nodes[_count] = default; 

            if (_count > 0)
            {
                HeapifyDown(0);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out T result, out float priority)
        {
            if (_count == 0)
            {
                result = default;
                priority = 0;
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

                if (smallest == index)
                {
                    break;
                }

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

        private void Resize(int newSize)
        {
            Array.Resize(ref _nodes, newSize);
        }
    }
}
