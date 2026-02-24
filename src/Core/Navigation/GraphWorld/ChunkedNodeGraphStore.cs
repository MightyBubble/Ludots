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
        }

        public bool Remove(long chunkKey)
        {
            return _chunks.Remove(chunkKey);
        }

        public bool TryGetChunk(long chunkKey, out GraphChunkData chunk)
        {
            return _chunks.TryGetValue(chunkKey, out chunk);
        }

        public LoadedGraphView BuildLoadedView()
        {
            int totalNodes = 0;
            int totalEdges = 0;
            int totalCrossEdges = 0;

            foreach (var kv in _chunks)
            {
                var g = kv.Value.Graph;
                totalNodes += g.NodeCount;
                totalEdges += g.EdgeCount;
                totalCrossEdges += kv.Value.CrossEdges.Length;
            }

            var builder = new NodeGraphBuilder(totalNodes, totalEdges + totalCrossEdges);
            var nodeKeys = totalNodes == 0 ? Array.Empty<GraphNodeKey>() : new GraphNodeKey[totalNodes];
            var nodeIdByKey = new Dictionary<GraphNodeKey, int>(totalNodes);

            var chunkOffsets = new Dictionary<long, int>(_chunks.Count);
            int offset = 0;
            foreach (var kv in _chunks)
            {
                chunkOffsets[kv.Key] = offset;
                var g = kv.Value.Graph;

                var xs = g.PosXcmArray;
                var ys = g.PosYcmArray;
                var tags = g.NodeTagSetIdArray;
                int n = g.NodeCount;
                for (int i = 0; i < n; i++)
                {
                    int nodeId = builder.AddNode(xs[i], ys[i], tags[i]);
                    var key = new GraphNodeKey(kv.Key, (ushort)i);
                    nodeKeys[nodeId] = key;
                    nodeIdByKey[key] = nodeId;
                }

                offset += n;
            }

            foreach (var kv in _chunks)
            {
                var g = kv.Value.Graph;
                int chunkOffset = chunkOffsets[kv.Key];

                var edgeStart = g.EdgeStartArray;
                var edgeTo = g.EdgeToArray;
                var edgeCost = g.EdgeBaseCostArray;
                var edgeTags = g.EdgeTagSetIdArray;

                int nodeCount = g.NodeCount;
                for (int n = 0; n < nodeCount; n++)
                {
                    int fromGlobal = chunkOffset + n;
                    for (int e = edgeStart[n]; e < edgeStart[n + 1]; e++)
                    {
                        int toGlobal = chunkOffset + edgeTo[e];
                        builder.AddEdge(fromGlobal, toGlobal, edgeCost[e], edgeTags[e]);
                    }
                }

                var cross = kv.Value.CrossEdges;
                for (int i = 0; i < cross.Length; i++)
                {
                    var ce = cross[i];
                    int fromGlobal = chunkOffset + ce.FromLocalNodeId;
                    var toKey = new GraphNodeKey(ce.ToChunkKey, ce.ToLocalNodeId);
                    if (!nodeIdByKey.TryGetValue(toKey, out int toGlobal)) continue;
                    builder.AddEdge(fromGlobal, toGlobal, ce.BaseCost, ce.TagSetId);
                }
            }

            var graph = builder.Build();
            return new LoadedGraphView(graph, nodeKeys, nodeIdByKey);
        }

        public LoadedGraphView BuildLoadedView(IReadOnlyList<long> chunkKeys)
        {
            if (chunkKeys == null) throw new ArgumentNullException(nameof(chunkKeys));
            if (chunkKeys.Count == 0) return new LoadedGraphView(new NodeGraphBuilder(0, 0).Build(), Array.Empty<GraphNodeKey>(), new Dictionary<GraphNodeKey, int>());

            var included = new HashSet<long>();
            for (int i = 0; i < chunkKeys.Count; i++) included.Add(chunkKeys[i]);

            int totalNodes = 0;
            int totalEdges = 0;
            int totalCrossEdges = 0;

            foreach (var key in included)
            {
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                var g = chunk.Graph;
                totalNodes += g.NodeCount;
                totalEdges += g.EdgeCount;
                var cross = chunk.CrossEdges;
                for (int i = 0; i < cross.Length; i++)
                {
                    if (included.Contains(cross[i].ToChunkKey)) totalCrossEdges++;
                }
            }

            var builder = new NodeGraphBuilder(totalNodes, totalEdges + totalCrossEdges);
            var nodeKeys = totalNodes == 0 ? Array.Empty<GraphNodeKey>() : new GraphNodeKey[totalNodes];
            var nodeIdByKey = new Dictionary<GraphNodeKey, int>(totalNodes);
            var chunkOffsets = new Dictionary<long, int>(included.Count);

            int offset = 0;
            foreach (var key in included)
            {
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                chunkOffsets[key] = offset;
                var g = chunk.Graph;

                var xs = g.PosXcmArray;
                var ys = g.PosYcmArray;
                var tags = g.NodeTagSetIdArray;
                int n = g.NodeCount;
                for (int i = 0; i < n; i++)
                {
                    int nodeId = builder.AddNode(xs[i], ys[i], tags[i]);
                    var nk = new GraphNodeKey(key, (ushort)i);
                    nodeKeys[nodeId] = nk;
                    nodeIdByKey[nk] = nodeId;
                }

                offset += n;
            }

            foreach (var key in included)
            {
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                var g = chunk.Graph;
                int chunkOffset = chunkOffsets[key];

                var edgeStart = g.EdgeStartArray;
                var edgeTo = g.EdgeToArray;
                var edgeCost = g.EdgeBaseCostArray;
                var edgeTags = g.EdgeTagSetIdArray;

                int nodeCount = g.NodeCount;
                for (int n = 0; n < nodeCount; n++)
                {
                    int fromGlobal = chunkOffset + n;
                    for (int e = edgeStart[n]; e < edgeStart[n + 1]; e++)
                    {
                        int toGlobal = chunkOffset + edgeTo[e];
                        builder.AddEdge(fromGlobal, toGlobal, edgeCost[e], edgeTags[e]);
                    }
                }

                var cross = chunk.CrossEdges;
                for (int i = 0; i < cross.Length; i++)
                {
                    var ce = cross[i];
                    if (!included.Contains(ce.ToChunkKey)) continue;
                    int fromGlobal = chunkOffset + ce.FromLocalNodeId;
                    var toKey = new GraphNodeKey(ce.ToChunkKey, ce.ToLocalNodeId);
                    if (!nodeIdByKey.TryGetValue(toKey, out int toGlobal)) continue;
                    builder.AddEdge(fromGlobal, toGlobal, ce.BaseCost, ce.TagSetId);
                }
            }

            var graph = builder.Build();
            return new LoadedGraphView(graph, nodeKeys, nodeIdByKey);
        }
    }
}
