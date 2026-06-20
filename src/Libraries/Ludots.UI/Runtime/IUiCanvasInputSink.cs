using Ludots.UI.Input;

namespace Ludots.UI.Runtime;

public interface IUiCanvasInputSink
{
    bool HandleInput(UiNode node, PointerEvent pointerEvent);
}
