namespace Ludots.UI.Runtime;

public interface IUiCanvasHitTestSink
{
	bool HitTest(UiNode node, float x, float y);
}
