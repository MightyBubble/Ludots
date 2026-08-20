using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Runtime;

namespace PanelSkinWebMod;

/// <summary>
/// Pins the CEF surface to the top-right corner of the live UI root, following resizes.
/// </summary>
internal sealed class FireballWebSkinCanvasContent : BrowserSurfaceCanvasContent
{
    private readonly UIRoot _root;
    private readonly int _width;
    private readonly int _height;
    private readonly int _margin;

    public FireballWebSkinCanvasContent(
        IBrowserSurface surface,
        UIRoot root,
        int width,
        int height,
        int margin)
        : base(surface, BrowserSurfaceHitTestOptions.Bounds)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _width = width;
        _height = height;
        _margin = margin;
    }

    public override UiRect GetContentRect(UiNode node)
    {
        float x = System.MathF.Max(_margin, _root.Width - _width - _margin);
        return new UiRect(x, _margin, _width, _height);
    }
}
