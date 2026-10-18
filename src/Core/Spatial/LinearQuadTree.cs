using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Optimized for static/semi-static spatial queries.
    /// </summary>
    public class LinearQuadTree<T>
    {
        private struct Node
        {
            public int FirstChildIndex; // -1 if leaf
            public int Count;
            public int FirstItemIndex;
            public Rect Bounds;
        }

        private struct ItemData
        {
            public T Item;
            public Rect Bounds;
            public int NextItemIndex; // Linked list for items in the same node
        }

        public struct Rect
        {
            public float X, Y, W, H;
            public bool Intersects(Rect other) => X < other.X + other.W && X + W > other.X && Y < other.Y + other.H && Y + H > other.Y;
            public bool Contains(Vector2 p) => p.X >= X && p.X <= X + W && p.Y >= Y && p.Y <= Y + H;
        }

        private Node[] _nodes;
        private ItemData[] _items;
        private int _nodeCount;
        private int _itemCount;
        private int _maxDepth;
        private Rect _rootBounds;
        
        // Reusable stack for traversal to avoid recursion
        private int[] _stack; 

        public LinearQuadTree(Rect bounds, int maxDepth = 6, int initialItemCapacity = 1024)
        {
            _rootBounds = bounds;
            _maxDepth = maxDepth;
            
            // Estimate node count: 4^0 + 4^1 + ... + 4^maxDepth
            int estimatedNodes = (int)((Math.Pow(4, maxDepth + 1) - 1) / 3);
            _nodes = new Node[estimatedNodes];
            _items = new ItemData[initialItemCapacity];
            _stack = new int[maxDepth * 4 + 32];
            
            Clear();
        }

        public void Clear()
        {
            _nodeCount = 1; // Root exists
            _itemCount = 0;
            _nodes[0] = new Node { FirstChildIndex = -1, FirstItemIndex = -1, Bounds = _rootBounds };
        }

        public void Insert(T item, Vector2 pos, Vector2 size)
        {
            Rect bounds = new Rect { X = pos.X, Y = pos.Y, W = size.X, H = size.Y };
            Insert(0, 0, item, bounds);
        }

        private void Insert(int nodeIndex, int depth, T item, Rect bounds)
        {
            if (depth >= _maxDepth)
            {
                AddItemToNode(nodeIndex, item, bounds);
                return;
            }

            ref var node = ref _nodes[nodeIndex];
            
            if (node.FirstChildIndex == -1)
            {
                Split(nodeIndex);
            }
            
            int childIndex = GetQuadrant(node.Bounds, bounds);
            if (childIndex != -1)
            {
                Insert(node.FirstChildIndex + childIndex, depth + 1, item, bounds);
            }
            else
            {
                AddItemToNode(nodeIndex, item, bounds);
            }
        }

        private void Split(int nodeIndex)
        {
            int firstChild = _nodeCount;
            if (_nodeCount + 4 > _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);

            Rect b = _nodes[nodeIndex].Bounds;
            float hw = b.W / 2f;
            float hh = b.H / 2f;

            _nodes[firstChild + 0] = new Node { Bounds = new Rect { X = b.X, Y = b.Y, W = hw, H = hh }, FirstChildIndex = -1, FirstItemIndex = -1 };
            _nodes[firstChild + 1] = new Node { Bounds = new Rect { X = b.X + hw, Y = b.Y, W = hw, H = hh }, FirstChildIndex = -1, FirstItemIndex = -1 };
            _nodes[firstChild + 2] = new Node { Bounds = new Rect { X = b.X, Y = b.Y + hh, W = hw, H = hh }, FirstChildIndex = -1, FirstItemIndex = -1 };
            _nodes[firstChild + 3] = new Node { Bounds = new Rect { X = b.X + hw, Y = b.Y + hh, W = hw, H = hh }, FirstChildIndex = -1, FirstItemIndex = -1 };

            _nodes[nodeIndex].FirstChildIndex = firstChild;
            _nodeCount += 4;
        }

        private int GetQuadrant(Rect nodeBounds, Rect itemBounds)
        {
            float cx = nodeBounds.X + nodeBounds.W / 2f;
            float cy = nodeBounds.Y + nodeBounds.H / 2f;

            bool top = itemBounds.Y < cy && itemBounds.Y + itemBounds.H < cy;
            bool bottom = itemBounds.Y > cy;
            bool left = itemBounds.X < cx && itemBounds.X + itemBounds.W < cx;
            bool right = itemBounds.X > cx;

            if (left)
            {
                if (top) return 0;
                if (bottom) return 2;
            }
            else if (right)
            {
                if (top) return 1;
                if (bottom) return 3;
            }

            return -1; // Overlaps
        }

        private void AddItemToNode(int nodeIndex, T item, Rect bounds)
        {
            if (_itemCount >= _items.Length) Array.Resize(ref _items, _items.Length * 2);

            int itemIndex = _itemCount++;
            _items[itemIndex] = new ItemData { Item = item, Bounds = bounds, NextItemIndex = _nodes[nodeIndex].FirstItemIndex };
            _nodes[nodeIndex].FirstItemIndex = itemIndex;
            _nodes[nodeIndex].Count++;
        }

        public void Query(Rect area, List<T> results)
        {
            int stackTop = 0;
            _stack[0] = 0;

            while (stackTop >= 0)
            {
                int nodeIndex = _stack[stackTop--];
                ref var node = ref _nodes[nodeIndex];

                if (!node.Bounds.Intersects(area)) continue;

                int currentItem = node.FirstItemIndex;
                while (currentItem != -1)
                {
                    ref var itemData = ref _items[currentItem];
                    if (itemData.Bounds.Intersects(area))
                    {
                        results.Add(itemData.Item);
                    }
                    currentItem = itemData.NextItemIndex;
                }

                if (node.FirstChildIndex != -1)
                {
                    // Push in reverse order
                    _stack[++stackTop] = node.FirstChildIndex + 3;
                    _stack[++stackTop] = node.FirstChildIndex + 2;
                    _stack[++stackTop] = node.FirstChildIndex + 1;
                    _stack[++stackTop] = node.FirstChildIndex + 0;
                }
            }
        }
    }
}
