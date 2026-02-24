using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.MultiLayerGraph
{
    public sealed class InterLayerMappingBuilder
    {
        private int[] _fineToCoarse = Array.Empty<int>();
        private int _fineNodeCount;
        private int _coarseNodeCount;
        private List<int>[] _gatewaysByCoarse = Array.Empty<List<int>>();

        public void Reset(int fineNodeCount, int coarseNodeCount)
        {
            if (fineNodeCount < 0) throw new ArgumentOutOfRangeException(nameof(fineNodeCount));
            if (coarseNodeCount < 0) throw new ArgumentOutOfRangeException(nameof(coarseNodeCount));

            _fineNodeCount = fineNodeCount;
            _coarseNodeCount = coarseNodeCount;
            _fineToCoarse = fineNodeCount == 0 ? Array.Empty<int>() : new int[fineNodeCount];
            for (int i = 0; i < _fineToCoarse.Length; i++) _fineToCoarse[i] = -1;

            _gatewaysByCoarse = coarseNodeCount == 0 ? Array.Empty<List<int>>() : new List<int>[coarseNodeCount];
        }

        public void SetParent(int fineNodeId, int coarseNodeId)
        {
            if ((uint)fineNodeId >= (uint)_fineNodeCount) throw new ArgumentOutOfRangeException(nameof(fineNodeId));
            if ((uint)coarseNodeId >= (uint)_coarseNodeCount) throw new ArgumentOutOfRangeException(nameof(coarseNodeId));
            _fineToCoarse[fineNodeId] = coarseNodeId;
        }

        public void AddGateway(int coarseNodeId, int fineGatewayNodeId)
        {
            if ((uint)coarseNodeId >= (uint)_coarseNodeCount) throw new ArgumentOutOfRangeException(nameof(coarseNodeId));
            if ((uint)fineGatewayNodeId >= (uint)_fineNodeCount) throw new ArgumentOutOfRangeException(nameof(fineGatewayNodeId));

            var list = _gatewaysByCoarse[coarseNodeId];
            if (list == null)
            {
                list = new List<int>(4);
                _gatewaysByCoarse[coarseNodeId] = list;
            }
            list.Add(fineGatewayNodeId);
        }

        public InterLayerMapping Build()
        {
            int totalGateway = 0;
            for (int i = 0; i < _gatewaysByCoarse.Length; i++)
            {
                totalGateway += _gatewaysByCoarse[i]?.Count ?? 0;
            }

            var gatewayStart = new int[_coarseNodeCount + 1];
            var gatewayFineNodeIds = totalGateway == 0 ? Array.Empty<int>() : new int[totalGateway];

            int sum = 0;
            for (int c = 0; c < _coarseNodeCount; c++)
            {
                gatewayStart[c] = sum;
                var list = _gatewaysByCoarse[c];
                if (list != null)
                {
                    list.CopyTo(gatewayFineNodeIds, sum);
                    sum += list.Count;
                }
            }
            gatewayStart[_coarseNodeCount] = sum;

            return new InterLayerMapping(_fineNodeCount, _coarseNodeCount, _fineToCoarse, gatewayStart, gatewayFineNodeIds);
        }
    }
}

