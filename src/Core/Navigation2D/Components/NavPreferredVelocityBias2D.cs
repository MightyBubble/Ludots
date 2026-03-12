using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Components
{
    public readonly struct NavPreferredVelocityBias2D
    {
        public Fix64Vec2 ValueCmPerSec { get; init; }
        public Fix64Vec2 CenterCm { get; init; }
        public Fix64 InnerRadiusCm { get; init; }
        public Fix64 OuterRadiusCm { get; init; }
        public Fix64Vec2 FadeDirectionCm { get; init; }
        public Fix64 FadeStartCm { get; init; }
        public Fix64 FadeEndCm { get; init; }
    }
}
