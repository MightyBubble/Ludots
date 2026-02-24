namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdWorldTile2D
    {
        public readonly int Size;
        public readonly byte[] Obstacles;

        public CrowdWorldTile2D(int size)
        {
            Size = size;
            Obstacles = new byte[size * size];
        }
    }
}
