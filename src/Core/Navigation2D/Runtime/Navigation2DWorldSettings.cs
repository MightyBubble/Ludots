using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Runtime
{
    public readonly struct Navigation2DWorldSettings
    {
        public readonly int MaxAgents;
        public readonly Fix64 CellSizeCm;

        public Navigation2DWorldSettings(int maxAgents, Fix64 cellSizeCm)
        {
            MaxAgents = maxAgents;
            CellSizeCm = cellSizeCm;
        }
    }
}

