using System;

namespace Ludots.Core.Navigation.MultiLayerGraph
{
    public sealed class InterLayerMapping
    {
        public int FineNodeCount { get; }
        public int CoarseNodeCount { get; }

        private readonly int[] _fineToCoarse;
        private readonly int[] _gatewayStart;
        private readonly int[] _gatewayFineNodeIds;

        public InterLayerMapping(int fineNodeCount, int coarseNodeCount, int[] fineToCoarse, int[] gatewayStart, int[] gatewayFineNodeIds)
        {
            FineNodeCount = fineNodeCount;
            CoarseNodeCount = coarseNodeCount;
            _fineToCoarse = fineToCoarse ?? throw new ArgumentNullException(nameof(fineToCoarse));
            _gatewayStart = gatewayStart ?? throw new ArgumentNullException(nameof(gatewayStart));
            _gatewayFineNodeIds = gatewayFineNodeIds ?? throw new ArgumentNullException(nameof(gatewayFineNodeIds));

            if (_fineToCoarse.Length != fineNodeCount) throw new ArgumentException("fineToCoarse length mismatch.", nameof(fineToCoarse));
            if (_gatewayStart.Length != coarseNodeCount + 1) throw new ArgumentException("gatewayStart length mismatch.", nameof(gatewayStart));
        }

        public int GetCoarseOfFine(int fineNodeId)
        {
            if ((uint)fineNodeId >= (uint)FineNodeCount) return -1;
            return _fineToCoarse[fineNodeId];
        }

        public GatewayRange GetGatewaysOfCoarse(int coarseNodeId)
        {
            if ((uint)coarseNodeId >= (uint)CoarseNodeCount) return default;
            int start = _gatewayStart[coarseNodeId];
            int end = _gatewayStart[coarseNodeId + 1];
            return new GatewayRange(_gatewayFineNodeIds, start, end - start);
        }

        public readonly struct GatewayRange
        {
            private readonly int[] _ids;
            public readonly int Start;
            public readonly int Count;

            public GatewayRange(int[] ids, int start, int count)
            {
                _ids = ids;
                Start = start;
                Count = count;
            }

            public int this[int index] => _ids[Start + index];
        }
    }
}

