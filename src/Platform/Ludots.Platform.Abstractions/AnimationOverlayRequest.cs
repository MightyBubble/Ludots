namespace Ludots.Platform.Abstractions
{
    public struct AnimationOverlayRequest
    {
        public AnimationChannelState BaseClip;
        public AnimationChannelState LayerClip;
        public AnimationChannelState OverlayClip;

        public readonly bool HasAnyClip => BaseClip.IsActive || LayerClip.IsActive || OverlayClip.IsActive;
    }
}
