namespace Ludots.Core.Navigation.GraphCore
{
    public sealed class GraphEdgeCostOverlay
    {
        public float[] CostAdd { get; private set; } = System.Array.Empty<float>();
        public float[] CostMul { get; private set; } = System.Array.Empty<float>();
        public byte[] Blocked { get; private set; } = System.Array.Empty<byte>();

        public void EnsureCapacity(int edgeCount)
        {
            if (edgeCount < 0) throw new System.ArgumentOutOfRangeException(nameof(edgeCount));
            if (CostAdd.Length == edgeCount && CostMul.Length == edgeCount && Blocked.Length == edgeCount) return;
            CostAdd = edgeCount == 0 ? System.Array.Empty<float>() : new float[edgeCount];
            CostMul = edgeCount == 0 ? System.Array.Empty<float>() : new float[edgeCount];
            Blocked = edgeCount == 0 ? System.Array.Empty<byte>() : new byte[edgeCount];
        }
    }
}

