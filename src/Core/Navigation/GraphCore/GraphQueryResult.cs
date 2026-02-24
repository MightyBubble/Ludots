namespace Ludots.Core.Navigation.GraphCore
{
    public readonly struct GraphQueryResult
    {
        public readonly int Count;
        public readonly int Dropped;

        public bool Overflowed => Dropped > 0;

        public GraphQueryResult(int count, int dropped)
        {
            Count = count;
            Dropped = dropped;
        }
    }
}

