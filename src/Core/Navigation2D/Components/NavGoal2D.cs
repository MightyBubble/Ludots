using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Components
{
    public enum NavGoalKind2D : byte
    {
        None = 0,
        Point = 1,
    }

    public struct NavGoal2D
    {
        public NavGoalKind2D Kind;
        public Fix64Vec2 TargetCm;
        public Fix64 RadiusCm;
    }
}

