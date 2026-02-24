namespace Ludots.Core.Navigation.GraphWorld
{
    public readonly struct GraphCrossEdge
    {
        public readonly ushort FromLocalNodeId;
        public readonly long ToChunkKey;
        public readonly ushort ToLocalNodeId;
        public readonly float BaseCost;
        public readonly ushort TagSetId;

        public GraphCrossEdge(ushort fromLocalNodeId, long toChunkKey, ushort toLocalNodeId, float baseCost, ushort tagSetId)
        {
            FromLocalNodeId = fromLocalNodeId;
            ToChunkKey = toChunkKey;
            ToLocalNodeId = toLocalNodeId;
            BaseCost = baseCost;
            TagSetId = tagSetId;
        }
    }
}

