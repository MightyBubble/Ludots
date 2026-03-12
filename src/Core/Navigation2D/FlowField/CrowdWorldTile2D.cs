using System;

namespace Ludots.Core.Navigation2D.FlowField
{
    public sealed class CrowdWorldTile2D
    {
        public readonly int Size;
        public readonly byte[] Obstacles;
        public readonly float[] Density;
        public readonly float[] AverageVelocityX;
        public readonly float[] AverageVelocityY;
        public readonly float[] Discomfort;

        public CrowdWorldTile2D(int size)
        {
            Size = size;
            Obstacles = new byte[size * size];
            Density = new float[size * size];
            AverageVelocityX = new float[size * size];
            AverageVelocityY = new float[size * size];
            Discomfort = new float[size * size];
        }

        public void ClearObstacles()
        {
            Array.Clear(Obstacles, 0, Obstacles.Length);
        }

        public void ClearCrowdFields()
        {
            Array.Clear(Density, 0, Density.Length);
            Array.Clear(AverageVelocityX, 0, AverageVelocityX.Length);
            Array.Clear(AverageVelocityY, 0, AverageVelocityY.Length);
            Array.Clear(Discomfort, 0, Discomfort.Length);
        }

        public void Reset()
        {
            ClearObstacles();
            ClearCrowdFields();
        }
    }
}
