using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphWorld
{
    public readonly struct GraphCrossEdge
    {
        public readonly ushort FromLocalNodeId;
        public readonly long ToChunkKey;
        public readonly ushort ToLocalNodeId;
        public readonly float BaseCost;
        public readonly ushort TagSetId;
        public readonly int DepthCm;
        public readonly int WidthCm;
        public readonly TagBits256 TagBits;

        public GraphCrossEdge(
            ushort fromLocalNodeId,
            long toChunkKey,
            ushort toLocalNodeId,
            float baseCost,
            ushort tagSetId,
            int depthCm = 0,
            int widthCm = 0,
            TagBits256 tagBits = default)
        {
            if (depthCm < 0) throw new System.ArgumentOutOfRangeException(nameof(depthCm));
            if (widthCm < 0) throw new System.ArgumentOutOfRangeException(nameof(widthCm));
            FromLocalNodeId = fromLocalNodeId;
            ToChunkKey = toChunkKey;
            ToLocalNodeId = toLocalNodeId;
            BaseCost = baseCost;
            TagSetId = tagSetId;
            DepthCm = depthCm;
            WidthCm = widthCm;
            TagBits = tagBits;
        }
    }
}
