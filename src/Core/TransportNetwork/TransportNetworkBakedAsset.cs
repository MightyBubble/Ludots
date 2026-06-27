using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Surfaces;

namespace Ludots.Core.TransportNetwork
{
    public sealed class TransportNetworkBakedAsset
    {
        private readonly Dictionary<long, GraphChunkData> _graphChunks;
        private readonly Dictionary<long, SurfaceSplineSegment[]> _ribbonChunks;

        public TransportNetworkBakedAsset(
            Dictionary<long, GraphChunkData> graphChunks,
            Dictionary<long, SurfaceSplineSegment[]> ribbonChunks,
            int sampledNodeCount,
            int directedEdgeCount)
        {
            _graphChunks = graphChunks ?? throw new ArgumentNullException(nameof(graphChunks));
            _ribbonChunks = ribbonChunks ?? throw new ArgumentNullException(nameof(ribbonChunks));
            SampledNodeCount = sampledNodeCount;
            DirectedEdgeCount = directedEdgeCount;
        }

        public IReadOnlyDictionary<long, GraphChunkData> GraphChunks => _graphChunks;
        public IReadOnlyDictionary<long, SurfaceSplineSegment[]> RibbonChunks => _ribbonChunks;
        public int SampledNodeCount { get; }
        public int DirectedEdgeCount { get; }

        public bool TryGetGraphChunk(long chunkKey, out GraphChunkData chunk)
        {
            return _graphChunks.TryGetValue(chunkKey, out chunk);
        }

        public bool TryGetRibbonChunk(long chunkKey, out SurfaceSplineSegment[] segments)
        {
            return _ribbonChunks.TryGetValue(chunkKey, out segments);
        }
    }
}
