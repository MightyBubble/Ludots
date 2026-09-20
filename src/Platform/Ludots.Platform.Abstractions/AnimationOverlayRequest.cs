using System;

namespace Ludots.Platform.Abstractions
{
    public struct AnimationOverlayRequest : IEquatable<AnimationOverlayRequest>
    {
        public AnimationChannelState BaseClip;
        public AnimationChannelState LayerClip;
        public AnimationChannelState OverlayClip;

        public readonly bool HasAnyClip => BaseClip.IsActive || LayerClip.IsActive || OverlayClip.IsActive;

        public readonly bool Equals(AnimationOverlayRequest other)
        {
            return BaseClip.Equals(other.BaseClip) &&
                   LayerClip.Equals(other.LayerClip) &&
                   OverlayClip.Equals(other.OverlayClip);
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is AnimationOverlayRequest other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(BaseClip, LayerClip, OverlayClip);
        }

        public static bool operator ==(AnimationOverlayRequest left, AnimationOverlayRequest right) => left.Equals(right);

        public static bool operator !=(AnimationOverlayRequest left, AnimationOverlayRequest right) => !left.Equals(right);
    }
}
