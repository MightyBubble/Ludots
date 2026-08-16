using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.MultiLayerGraph;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.GraphWorld
{
    /// <summary>
    /// Map-scoped loaded graph runtime that owns the cached flattened loaded view,
    /// projection index, and rebuild policy for chunked node-graph boards.
    /// </summary>
    public sealed class LoadedGraphRuntime : IDisposable
    {
        private static readonly long[] EmptyChunkKeys = Array.Empty<long>();

        private readonly ChunkedNodeGraphStore _store;
        private readonly ILoadedChunks _loadedChunks;
        private readonly int _preferredProjectionCellSizeCm;
        private LoadedGraphView _currentView;
        private INodeGraphSpatialIndex _currentIndex;
        private bool _dirty = true;
        private int _revision;

        public LoadedGraphRuntime(
            ChunkedNodeGraphStore store,
            ILoadedChunks loadedChunks,
            int preferredProjectionCellSizeCm)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _loadedChunks = loadedChunks ?? throw new ArgumentNullException(nameof(loadedChunks));
            _preferredProjectionCellSizeCm = preferredProjectionCellSizeCm;
            _currentView = _store.BuildLoadedView(EmptyChunkKeys);
            _currentIndex = CreateSpatialIndex(_currentView.Graph, preferredProjectionCellSizeCm);

            _loadedChunks.ChunkLoaded += OnChunkChanged;
            _loadedChunks.ChunkUnloaded += OnChunkChanged;
        }

        public int Revision
        {
            get
            {
                EnsureCurrent();
                return _revision;
            }
        }

        public int LoadedChunkCount => _loadedChunks.ActiveChunkKeys.Count;

        public LoadedGraphView CurrentView
        {
            get
            {
                EnsureCurrent();
                return _currentView;
            }
        }

        public NodeGraph CurrentGraph => CurrentView.Graph;

        public INodeGraphSpatialIndex CurrentSpatialIndex
        {
            get
            {
                EnsureCurrent();
                return _currentIndex;
            }
        }

        public bool HasLoadedGraph => CurrentGraph.NodeCount > 0;

        public bool TryFindNearestNode(WorldCmInt2 position, int maxRadiusCm, out int nodeId, out int distSqCm)
        {
            return CurrentSpatialIndex.TryFindNearest(position, maxRadiusCm, out nodeId, out distSqCm);
        }

        public Ludots.Core.Navigation.MultiLayerGraph.MultiLayerGraph BuildFineToCoarseRuntime(NodeGraph coarseGraph, InterLayerMapping fineToCoarseMapping)
        {
            if (coarseGraph == null) throw new ArgumentNullException(nameof(coarseGraph));
            if (fineToCoarseMapping == null) throw new ArgumentNullException(nameof(fineToCoarseMapping));

            return new Ludots.Core.Navigation.MultiLayerGraph.MultiLayerGraph(
                new[] { coarseGraph, CurrentGraph },
                new InterLayerMapping[] { null, fineToCoarseMapping });
        }

        public void Dispose()
        {
            _loadedChunks.ChunkLoaded -= OnChunkChanged;
            _loadedChunks.ChunkUnloaded -= OnChunkChanged;
        }

        public static INodeGraphSpatialIndex CreateSpatialIndex(NodeGraph graph, int preferredCellSizeCm)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            int cellSizeCm = preferredCellSizeCm > 0
                ? preferredCellSizeCm
                : EstimateProjectionCellSizeCm(graph);
            return new UniformGridNodeGraphSpatialIndex(graph, cellSizeCm);
        }

        private void EnsureCurrent()
        {
            if (!_dirty && !_store.IsViewDirty)
            {
                return;
            }

            if (_loadedChunks.ActiveChunkKeys.Count == 0)
            {
                _currentView = _store.BuildLoadedView(EmptyChunkKeys);
            }
            else if (_loadedChunks.ActiveChunkKeys is IReadOnlyList<long> chunkKeys)
            {
                _currentView = _store.BuildLoadedView(chunkKeys);
            }
            else
            {
                var chunkKeyArray = new long[_loadedChunks.ActiveChunkKeys.Count];
                int index = 0;
                foreach (long chunkKey in _loadedChunks.ActiveChunkKeys)
                {
                    chunkKeyArray[index++] = chunkKey;
                }

                _currentView = _store.BuildLoadedView(chunkKeyArray);
            }

            _currentIndex = CreateSpatialIndex(_currentView.Graph, _preferredProjectionCellSizeCm);
            _store.ClearDirtyFlag();
            _dirty = false;
            _revision++;
        }

        private static int EstimateProjectionCellSizeCm(NodeGraph graph)
        {
            if (graph.NodeCount <= 1)
            {
                return SpatialScaleDefaults.CellCm;
            }

            var xs = graph.PosXcm;
            var ys = graph.PosYcm;
            int minX = xs[0];
            int maxX = xs[0];
            int minY = ys[0];
            int maxY = ys[0];
            for (int i = 1; i < graph.NodeCount; i++)
            {
                int x = xs[i];
                int y = ys[i];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            long width = Math.Max(1, (long)maxX - minX);
            long height = Math.Max(1, (long)maxY - minY);
            double avgAreaPerNode = (width * height) / (double)Math.Max(1, graph.NodeCount);
            int estimated = (int)Math.Round(Math.Sqrt(Math.Max(1d, avgAreaPerNode)));
            return Math.Max(SpatialScaleDefaults.CellCm, estimated);
        }

        private void OnChunkChanged(long _)
        {
            _dirty = true;
        }
    }
}
