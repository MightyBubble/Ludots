using Ludots.UI.Runtime;

namespace Ludots.UI.Browser;

public interface IUiBrowserCanvasContent
{
	IBrowserSurface Surface { get; }

	BrowserHitMaskColor? VisualHitMask { get; }

	UiRect GetContentRect(UiNode node);

	void EnsureSurfaceViewport(float width, float height);

	bool TryReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame);
}
