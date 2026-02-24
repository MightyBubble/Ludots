using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Collections.Graphs
{
    /// <summary>
    /// Represents a high-performance, index-based directed graph.
    /// Optimized for Zero-GC traversal and Structure of Arrays (SoA) layout.
    /// Nodes are identified by integer IDs (0 to NodeCapacity - 1).
    /// </summary>
    public class StructGraph
    {
        // Edge storage
        internal struct Edge
        {
            public int TargetNodeId;
            public float Cost;
            public int NextEdgeIndex; // Linked list for edges of a node
        }

        // Node storage
        // Instead of a Node class, we store data in parallel arrays
        private int[] _nodeFirstEdge; // Index of the first edge for each node (-1 if none)
        private Edge[] _edges;        // Pool of all edges
        private int _edgeCount;
        private int _nodeCapacity;

        public int NodeCapacity => _nodeCapacity;

        public StructGraph(int nodeCapacity, int initialEdgeCapacity = 1024)
        {
            _nodeCapacity = nodeCapacity;
            _nodeFirstEdge = new int[nodeCapacity];
            Array.Fill(_nodeFirstEdge, -1);

            _edges = new Edge[initialEdgeCapacity];
            _edgeCount = 0;
        }

        /// <summary>
        /// Adds a directed edge from source to target.
        /// </summary>
        public void AddEdge(int sourceNode, int targetNode, float cost)
        {
            EnsureEdgeCapacity();

            int edgeIndex = _edgeCount++;
            _edges[edgeIndex] = new Edge
            {
                TargetNodeId = targetNode,
                Cost = cost,
                NextEdgeIndex = _nodeFirstEdge[sourceNode]
            };

            _nodeFirstEdge[sourceNode] = edgeIndex;
        }

        public void Clear()
        {
            Array.Fill(_nodeFirstEdge, -1);
            _edgeCount = 0;
        }

        /// <summary>
        /// Enumerator for zero-allocation iteration over edges.
        /// Usage: foreach (var edge in graph.GetEdges(nodeId)) { ... }
        /// </summary>
        public EdgeEnumerator GetEdges(int nodeId)
        {
            return new EdgeEnumerator(_edges, _nodeFirstEdge[nodeId]);
        }

        public ref struct EdgeEnumerator
        {
            private readonly Edge[] _edges;
            private int _currentIndex;

            internal EdgeEnumerator(Edge[] edges, int startIndex)
            {
                _edges = edges;
                _currentIndex = startIndex;
                Current = default;
                _first = true;
            }

            private bool _first;
            public EdgeData Current { get; private set; }

            public bool MoveNext()
            {
                if (_first)
                {
                    if (_currentIndex == -1) return false;
                    _first = false;
                }
                else
                {
                    if (_currentIndex == -1) return false;
                    _currentIndex = _edges[_currentIndex].NextEdgeIndex;
                    if (_currentIndex == -1) return false;
                }

                ref var rawEdge = ref _edges[_currentIndex];
                Current = new EdgeData(rawEdge.TargetNodeId, rawEdge.Cost);
                return true;
            }
            
            // Standard GetEnumerator for foreach support
            public EdgeEnumerator GetEnumerator() => this;
        }

        public readonly struct EdgeData
        {
            public readonly int Target;
            public readonly float Cost;

            public EdgeData(int target, float cost)
            {
                Target = target;
                Cost = cost;
            }
        }

        private void EnsureEdgeCapacity()
        {
            if (_edgeCount >= _edges.Length)
            {
                Array.Resize(ref _edges, _edges.Length * 2);
            }
        }
        
        /// <summary>
        /// Resizes the node capacity if needed.
        /// Existing edges are preserved, but node indices must remain valid.
        /// </summary>
        public void ResizeNodes(int newCapacity)
        {
            if (newCapacity <= _nodeCapacity) return;
            
            Array.Resize(ref _nodeFirstEdge, newCapacity);
            // Fill new slots with -1
            for (int i = _nodeCapacity; i < newCapacity; i++)
            {
                _nodeFirstEdge[i] = -1;
            }
            _nodeCapacity = newCapacity;
        }
    }
}
