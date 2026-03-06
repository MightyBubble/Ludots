using Arch.Core;

namespace Ludots.Core.Presentation.Tween
{
    public enum TweenCommandKind : byte
    {
        Start = 0,
        StopScope = 1,
        CompleteScope = 2
    }

    public enum TweenEase : byte
    {
        Linear = 0,
        QuadOut = 1,
        QuadInOut = 2,
        CubicOut = 3,
        SmoothStep = 4
    }

    public struct TweenTarget
    {
        public int SinkId;
        public int TargetId;
        public int PropertyKey;
        public Entity Owner;
    }

    public struct TweenCommand
    {
        public TweenCommandKind Kind;
        public int ScopeId;
        public TweenTarget Target;
        public float From;
        public float To;
        public float Duration;
        public float Delay;
        public TweenEase Ease;
        public bool ReplaceExisting;
    }

    internal static class TweenEasing
    {
        public static float Evaluate(TweenEase ease, float t)
        {
            t = System.Math.Clamp(t, 0f, 1f);
            return ease switch
            {
                TweenEase.Linear => t,
                TweenEase.QuadOut => 1f - ((1f - t) * (1f - t)),
                TweenEase.QuadInOut => t < 0.5f
                    ? 2f * t * t
                    : 1f - ((-2f * t + 2f) * (-2f * t + 2f) * 0.5f),
                TweenEase.CubicOut => 1f - ((1f - t) * (1f - t) * (1f - t)),
                TweenEase.SmoothStep => t * t * (3f - (2f * t)),
                _ => t
            };
        }
    }
}
