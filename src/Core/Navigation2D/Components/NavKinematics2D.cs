using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Components
{
    public struct NavKinematics2D
    {
        public Fix64 MaxSpeedCmPerSec;
        public Fix64 MaxAccelCmPerSec2;
        public Fix64 RadiusCm;
        public Fix64 NeighborDistCm;
        public Fix64 TimeHorizonSec;
        public int MaxNeighbors;
    }
}

