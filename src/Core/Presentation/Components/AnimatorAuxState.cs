namespace Ludots.Core.Presentation.Components
{
    public struct AnimatorAuxState
    {
        public AnimatorBuiltinClipState BaseClip;
        public AnimatorBuiltinClipState LayerClip;
        public AnimatorBuiltinClipState OverlayClip;

        public readonly bool HasAnyClip => BaseClip.IsActive || LayerClip.IsActive || OverlayClip.IsActive;
    }
}
