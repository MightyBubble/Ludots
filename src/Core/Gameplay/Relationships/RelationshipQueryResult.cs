namespace Ludots.Core.Gameplay.Relationships
{
    public readonly struct RelationshipQueryResult
    {
        public readonly int Count;
        public readonly int Dropped;

        public bool Overflowed => Dropped > 0;

        public RelationshipQueryResult(int count, int dropped)
        {
            Count = count;
            Dropped = dropped;
        }
    }
}
