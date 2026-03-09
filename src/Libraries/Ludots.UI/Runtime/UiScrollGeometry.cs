namespace Ludots.UI.Runtime;

public static class UiScrollGeometry
{
    public const float ScrollbarThickness = 10f;
    public const float ScrollbarPadding = 2f;
    public const float MinThumbSize = 18f;

    public static bool HasHorizontalScrollbar(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.CanScrollHorizontally;
    }

    public static bool HasVerticalScrollbar(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.CanScrollVertically;
    }

    public static UiRect GetHorizontalTrackRect(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        float width = Math.Max(0f, node.LayoutRect.Width - (ScrollbarPadding * 2f) - (HasVerticalScrollbar(node) ? ScrollbarThickness : 0f));
        float x = node.LayoutRect.X + ScrollbarPadding;
        float y = node.LayoutRect.Bottom - ScrollbarThickness - ScrollbarPadding;
        return new UiRect(x, y, width, ScrollbarThickness);
    }

    public static UiRect GetVerticalTrackRect(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        float height = Math.Max(0f, node.LayoutRect.Height - (ScrollbarPadding * 2f) - (HasHorizontalScrollbar(node) ? ScrollbarThickness : 0f));
        float x = node.LayoutRect.Right - ScrollbarThickness - ScrollbarPadding;
        float y = node.LayoutRect.Y + ScrollbarPadding;
        return new UiRect(x, y, ScrollbarThickness, height);
    }

    public static UiRect GetHorizontalThumbRect(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        UiRect track = GetHorizontalTrackRect(node);
        if (!HasHorizontalScrollbar(node) || track.Width <= 0.01f)
        {
            return new UiRect(track.X, track.Y, 0f, 0f);
        }

        float thumbWidth = Math.Clamp(track.Width * (node.LayoutRect.Width / Math.Max(node.LayoutRect.Width, node.ScrollContentWidth)), MinThumbSize, track.Width);
        float travel = Math.Max(0f, track.Width - thumbWidth);
        float progress = node.MaxScrollX <= 0.01f ? 0f : node.ScrollOffsetX / node.MaxScrollX;
        return new UiRect(track.X + (travel * progress), track.Y, thumbWidth, track.Height);
    }

    public static UiRect GetVerticalThumbRect(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        UiRect track = GetVerticalTrackRect(node);
        if (!HasVerticalScrollbar(node) || track.Height <= 0.01f)
        {
            return new UiRect(track.X, track.Y, 0f, 0f);
        }

        float thumbHeight = Math.Clamp(track.Height * (node.LayoutRect.Height / Math.Max(node.LayoutRect.Height, node.ScrollContentHeight)), MinThumbSize, track.Height);
        float travel = Math.Max(0f, track.Height - thumbHeight);
        float progress = node.MaxScrollY <= 0.01f ? 0f : node.ScrollOffsetY / node.MaxScrollY;
        return new UiRect(track.X, track.Y + (travel * progress), track.Width, thumbHeight);
    }
}
