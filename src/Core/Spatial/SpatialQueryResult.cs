namespace Ludots.Core.Spatial
{
    public readonly struct SpatialQueryResult
    {
        public readonly int Count;
        public readonly int Dropped;

        public bool Overflowed => Dropped > 0;

        public SpatialQueryResult(int count, int dropped)
        {
            Count = count;
            Dropped = dropped;
        }
    }
}
