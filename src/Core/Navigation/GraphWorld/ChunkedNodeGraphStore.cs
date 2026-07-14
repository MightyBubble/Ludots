using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.GraphWorld
{
    public sealed class ChunkedNodeGraphStore
    {
        private readonly Dictionary<long, GraphChunkData> _chunks = new Dictionary<long, GraphChunkData>();
        private ILoadedChunks _loadedChunks;
        private bool _viewDirty;

        /// <summary>
        /// Subscribe to an ILoadedChunks source. When chunks are unloaded,
        /// the corresponding graph data is removed automatically.
        /// </summary>
        public void SubscribeToLoadedChunks(ILoadedChunks source)
        {
            UnsubscribeFromLoadedChunks();

            _loadedChunks = source;
            if (_loadedChunks != null)
            {
                _loadedChunks.ChunkUnloaded += OnChunkUnloaded;
            }
        }

        /// <summary>
        /// Detach from the current ILoadedChunks source to prevent event subscription leaks.
        /// Call this before the store is abandoned or replaced.
        /// </summary>
        public void UnsubscribeFromLoadedChunks()
        {
            if (_loadedChunks != null)
            {
                _loadedChunks.ChunkUnloaded -= OnChunkUnloaded;
                _loadedChunks = null;
            }
        }

        private void OnChunkUnloaded(long chunkKey)
        {
            if (_chunks.Remove(chunkKey))
            {
                _viewDirty = true;
            }
        }

        public bool IsViewDirty => _viewDirty;
        public void ClearDirtyFlag() => _viewDirty = false;

        public void Clear()
        {
            _chunks.Clear();
            _viewDirty = true;
        }

        public void AddOrReplace(long chunkKey, GraphChunkData chunk)
        {
            _chunks[chunkKey] = chunk ?? throw new ArgumentNullException(nameof(chunk));
            _viewDirty = true;
        }

        public bool Remove(long chunkKey)
        {
            if (_chunks.Remove(chunkKey))
            {
                _viewDirty = true;
                return true;
            }

            return false;
        }

        public bool TryGetChunk(long chunkKey, out GraphChunkData chunk)
        {
            return _chunks.TryGetValue(chunkKey, out chunk);
        }

        public LoadedGraphView BuildLoadedView()
        {
            if (_chunks.Count == 0)
            {
                return BuildLoadedViewCore(Array.Empty<long>(), 0);
            }

            var sortedKeys = new long[_chunks.Count];
            _chunks.Keys.CopyTo(sortedKeys, 0);
            Array.Sort(sortedKeys);
            return BuildLoadedViewCore(sortedKeys, sortedKeys.Length);
        }

        public LoadedGraphView BuildLoadedView(IReadOnlyList<long> chunkKeys)
        {
            if (chunkKeys == null) throw new ArgumentNullException(nameof(chunkKeys));
            if (chunkKeys.Count == 0)
            {
                return BuildLoadedViewCore(Array.Empty<long>(), 0);
            }

            var sortedKeys = new long[chunkKeys.Count];
            for (int i = 0; i < chunkKeys.Count; i++)
            {
                sortedKeys[i] = chunkKeys[i];
            }
            Array.Sort(sortedKeys);

            int uniqueCount = 1;
            for (int i = 1; i < sortedKeys.Length; i++)
            {
                if (sortedKeys[i] != sortedKeys[uniqueCount - 1])
                {
                    sortedKeys[uniqueCount++] = sortedKeys[i];
                }
            }

            return BuildLoadedViewCore(sortedKeys, uniqueCount);
        }

        private LoadedGraphView BuildLoadedViewCore(long[] sortedChunkKeys, int chunkKeyCount)
        {
            if (chunkKeyCount == 0)
            {
                return new LoadedGraphView(
                    new NodeGraphBuilder(0, 0).Build(),
                    Array.Empty<GraphNodeKey>(),
                    new Dictionary<GraphNodeKey, int>());
            }

            int totalNodes = 0;
            int totalEdges = 0;
            int totalCrossEdges = 0;

            for (int keyIndex = 0; keyIndex < chunkKeyCount; keyIndex++)
            {
                long key = sortedChunkKeys[keyIndex];
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                var g = chunk.Graph;
                totalNodes += g.NodeCount;
                totalEdges += g.EdgeCount;
                totalCrossEdges += chunk.CrossEdges.Length;
            }

            var builder = new NodeGraphBuilder(totalNodes, totalEdges + totalCrossEdges);
            var nodeKeys = totalNodes == 0 ? Array.Empty<GraphNodeKey>() : new GraphNodeKey[totalNodes];
            var nodeIdByKey = new Dictionary<GraphNodeKey, int>(totalNodes);
            var chunkOffsets = new Dictionary<long, int>(chunkKeyCount);

            int offset = 0;
            for (int keyIndex = 0; keyIndex < chunkKeyCount; keyIndex++)
            {
                long key = sortedChunkKeys[keyIndex];
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                chunkOffsets[key] = offset;
                var g = chunk.Graph;

                var xs = g.PosXcmArray;
                var ys = g.PosYcmArray;
                var tags = g.NodeTagSetIdArray;
                var tagSets = g.TagSetsArray;
                int n = g.NodeCount;
                for (int i = 0; i < n; i++)
                {
                    ushort tagSetId = builder.AddTagSet(in tagSets[tags[i]]);
                    int nodeId = builder.AddNode(xs[i], ys[i], tagSetId);
                    var nk = new GraphNodeKey(key, (ushort)i);
                    nodeKeys[nodeId] = nk;
                    nodeIdByKey[nk] = nodeId;
                }

                offset += n;
            }

            for (int keyIndex = 0; keyIndex < chunkKeyCount; keyIndex++)
            {
                long key = sortedChunkKeys[keyIndex];
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                var g = chunk.Graph;
                int chunkOffset = chunkOffsets[key];

                var edgeStart = g.EdgeStartArray;
                var edgeTo = g.EdgeToArray;
                var edgeCost = g.EdgeBaseCostArray;
                var edgeTags = g.EdgeTagSetIdArray;
                var edgeDepth = g.EdgeDepthCmArray;
                var edgeWidth = g.EdgeWidthCmArray;
                var tagSets = g.TagSetsArray;

                int nodeCount = g.NodeCount;
                for (int n = 0; n < nodeCount; n++)
                {
                    int fromGlobal = chunkOffset + n;
                    for (int e = edgeStart[n]; e < edgeStart[n + 1]; e++)
                    {
                        int toGlobal = chunkOffset + edgeTo[e];
                        ushort tagSetId = builder.AddTagSet(in tagSets[edgeTags[e]]);
                        builder.AddEdge(fromGlobal, toGlobal, edgeCost[e], tagSetId, edgeDepth[e], edgeWidth[e]);
                    }
                }

                var cross = chunk.CrossEdges;
                for (int i = 0; i < cross.Length; i++)
                {
                    var ce = cross[i];
                    if (!chunkOffsets.ContainsKey(ce.ToChunkKey)) continue;
                    int fromGlobal = chunkOffset + ce.FromLocalNodeId;
                    var toKey = new GraphNodeKey(ce.ToChunkKey, ce.ToLocalNodeId);
                    if (!nodeIdByKey.TryGetValue(toKey, out int toGlobal))
                    {
                        throw new InvalidOperationException(
                            $"NAV.GRAPH.ERR.CrossEdgeTargetMissing: sourceChunk={key}, targetChunk={ce.ToChunkKey}, targetLocalNode={ce.ToLocalNodeId}.");
                    }
                    TagBits256 crossTags = ResolveCrossEdgeTags(g, in ce);
                    ushort tagSetId = builder.AddTagSet(in crossTags);
                    builder.AddEdge(fromGlobal, toGlobal, ce.BaseCost, tagSetId, ce.DepthCm, ce.WidthCm);
                }
            }

            var graph = builder.Build();
            return new LoadedGraphView(graph, nodeKeys, nodeIdByKey);
        }

        private static TagBits256 ResolveCrossEdgeTags(NodeGraph sourceGraph, in GraphCrossEdge edge)
        {
            if (!edge.TagBits.Equals(default(TagBits256)))
            {
                return edge.TagBits;
            }

            if (edge.TagSetId >= sourceGraph.TagSetsArray.Length)
            {
                throw new InvalidOperationException(
                    $"NAV.GRAPH.ERR.CrossEdgeTagSetOutOfRange: tagSetId={edge.TagSetId}, tagSetCount={sourceGraph.TagSetsArray.Length}.");
            }

            return sourceGraph.TagSetsArray[edge.TagSetId];
        }
    }
}
