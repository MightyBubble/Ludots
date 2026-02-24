using System.Collections.Generic;
using Ludots.Core.Collections.Graphs;

namespace Ludots.Core.Navigation.HPA
{
    /// <summary>
    /// Represents the high-level graph where each node is a Chunk (Cluster).
    /// </summary>
    public class ClusterGraph
    {
        public StructGraph Graph { get; private set; }
        
        // Map ChunkKey -> Graph Node Index
        private Dictionary<long, int> _chunkToNodeIndex = new Dictionary<long, int>();
        private List<long> _nodeIndexToChunk = new List<long>();

        public ClusterGraph(int initialCapacity = 256)
        {
            Graph = new StructGraph(initialCapacity);
        }

        public int GetOrAddNode(long chunkKey)
        {
            if (_chunkToNodeIndex.TryGetValue(chunkKey, out int index))
            {
                return index;
            }

            index = _nodeIndexToChunk.Count;
            _nodeIndexToChunk.Add(chunkKey);
            _chunkToNodeIndex[chunkKey] = index;
            
            if (index >= Graph.NodeCapacity)
            {
                Graph.ResizeNodes(index * 2);
            }

            return index;
        }

        public long GetChunkKey(int nodeIndex)
        {
            return _nodeIndexToChunk[nodeIndex];
        }

        public void AddEdge(long fromChunk, long toChunk, float cost)
        {
            int from = GetOrAddNode(fromChunk);
            int to = GetOrAddNode(toChunk);
            Graph.AddEdge(from, to, cost);
        }
    }
}
