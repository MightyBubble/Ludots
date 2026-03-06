using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Presentation.Tween
{
    public enum ScreenOverlayItemTweenProperty : byte
    {
        X = 0,
        Y = 1,
        Width = 2,
        Height = 3,
        TextAlpha = 4,
        FillAlpha = 5,
        BorderAlpha = 6
    }

    public sealed class ScreenOverlayTweenSink : ITweenSink
    {
        private readonly TweenedScreenOverlayRegistry _items;

        public ScreenOverlayTweenSink(TweenedScreenOverlayRegistry items)
        {
            _items = items;
        }

        public bool TryApply(in TweenTarget target, float value)
        {
            if (!_items.IsActive(target.TargetId))
                return false;

            ref var item = ref _items.Get(target.TargetId);
            switch ((ScreenOverlayItemTweenProperty)target.PropertyKey)
            {
                case ScreenOverlayItemTweenProperty.X:
                    item.X = value;
                    return true;
                case ScreenOverlayItemTweenProperty.Y:
                    item.Y = value;
                    return true;
                case ScreenOverlayItemTweenProperty.Width:
                    item.Width = value;
                    return true;
                case ScreenOverlayItemTweenProperty.Height:
                    item.Height = value;
                    return true;
                case ScreenOverlayItemTweenProperty.TextAlpha:
                    item.TextColor.W = value;
                    return true;
                case ScreenOverlayItemTweenProperty.FillAlpha:
                    item.FillColor.W = value;
                    return true;
                case ScreenOverlayItemTweenProperty.BorderAlpha:
                    item.BorderColor.W = value;
                    return true;
                default:
                    return false;
            }
        }
    }
}
