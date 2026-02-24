using System;

namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdFlowTile2D
    {
        public readonly int Size;
        public readonly float[] Potential;

        public CrowdFlowTile2D(int size)
        {
            Size = size;
            Potential = new float[size * size];
            Array.Fill(Potential, float.PositiveInfinity);
        }

        public void Reset()
        {
            Array.Fill(Potential, float.PositiveInfinity);
        }
    }
}
