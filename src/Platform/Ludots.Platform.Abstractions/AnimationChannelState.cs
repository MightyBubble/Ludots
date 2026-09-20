using System;

namespace Ludots.Platform.Abstractions
{
    public struct AnimationChannelState : IEquatable<AnimationChannelState>
    {
        public int ChannelId;
        public float NormalizedTime01;
        public float Weight01;
        public float Scalar0;
        public float Scalar1;

        public readonly bool IsActive => ChannelId > 0 && Weight01 > 0.001f;

        public static AnimationChannelState Create(
            int channelId,
            float normalizedTime01,
            float weight01,
            float scalar0 = 0f,
            float scalar1 = 0f)
        {
            return new AnimationChannelState
            {
                ChannelId = channelId,
                NormalizedTime01 = Clamp01(normalizedTime01),
                Weight01 = Clamp01(weight01),
                Scalar0 = scalar0,
                Scalar1 = scalar1,
            };
        }

        public readonly bool Equals(AnimationChannelState other)
        {
            return ChannelId == other.ChannelId &&
                   NormalizedTime01.Equals(other.NormalizedTime01) &&
                   Weight01.Equals(other.Weight01) &&
                   Scalar0.Equals(other.Scalar0) &&
                   Scalar1.Equals(other.Scalar1);
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is AnimationChannelState other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(ChannelId, NormalizedTime01, Weight01, Scalar0, Scalar1);
        }

        public static bool operator ==(AnimationChannelState left, AnimationChannelState right) => left.Equals(right);

        public static bool operator !=(AnimationChannelState left, AnimationChannelState right) => !left.Equals(right);

        private static float Clamp01(float value)
        {
            return Math.Clamp(value, 0f, 1f);
        }
    }
}
