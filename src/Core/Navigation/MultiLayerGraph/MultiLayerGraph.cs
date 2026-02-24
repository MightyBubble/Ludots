using System;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.MultiLayerGraph
{
    public sealed class MultiLayerGraph
    {
        private readonly NodeGraph[] _layers;
        private readonly InterLayerMapping[] _mappingFineToCoarse;

        public MultiLayerGraph(NodeGraph[] layers, InterLayerMapping[] mappingFineToCoarse)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _mappingFineToCoarse = mappingFineToCoarse ?? throw new ArgumentNullException(nameof(mappingFineToCoarse));
        }

        public int LayerCount => _layers.Length;

        public NodeGraph GetLayer(int layerIndex) => _layers[layerIndex];

        public InterLayerMapping GetMappingFineToCoarse(int fineLayerIndex) => _mappingFineToCoarse[fineLayerIndex];
    }
}

