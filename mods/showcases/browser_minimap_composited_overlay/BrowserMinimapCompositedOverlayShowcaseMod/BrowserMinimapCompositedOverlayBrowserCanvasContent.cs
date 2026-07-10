using Ludots.UI.Browser;
using Ludots.UI.Runtime;

namespace BrowserMinimapCompositedOverlayShowcaseMod;

internal sealed class BrowserMinimapCompositedOverlayBrowserCanvasContent : BrowserSurfaceCanvasContent
{
	private readonly BrowserMinimapCompositedOverlayLayoutState _layoutState;

	public BrowserMinimapCompositedOverlayBrowserCanvasContent(
		IBrowserSurface surface,
		BrowserMinimapCompositedOverlayLayoutState layoutState,
		BrowserSurfaceHitTestOptions? hitTestOptions = null)
		: base(surface, hitTestOptions)
	{
		_layoutState = layoutState ?? throw new ArgumentNullException(nameof(layoutState));
	}

	public override UiRect GetContentRect(UiNode node)
	{
		BrowserMinimapCompositedOverlayCanvasRect rect = _layoutState.GetCanvasRect();
		return new UiRect(rect.X, rect.Y, rect.Width, rect.Height);
	}
}
