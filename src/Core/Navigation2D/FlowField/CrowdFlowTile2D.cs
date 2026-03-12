using System;

namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdFlowTile2D
    {
        public readonly int Size;
        public readonly float[] Potential;
        public readonly float[] CostPosX;
        public readonly float[] CostNegX;
        public readonly float[] CostPosY;
        public readonly float[] CostNegY;

        public CrowdFlowTile2D(int size)
        {
            Size = size;
            Potential = new float[size * size];
            CostPosX = new float[size * size];
            CostNegX = new float[size * size];
            CostPosY = new float[size * size];
            CostNegY = new float[size * size];
            Reset();
        }

        public void Reset()
        {
            Array.Fill(Potential, float.PositiveInfinity);
            Array.Fill(CostPosX, 1f);
            Array.Fill(CostNegX, 1f);
            Array.Fill(CostPosY, 1f);
            Array.Fill(CostNegY, 1f);
        }
    }
}
