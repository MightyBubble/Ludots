using Ludots.UI.Input;

namespace Ludots.UI.Runtime;

public interface IUiCanvasKeyboardInputSink
{
	bool HandleKeyboardInput(UiNode node, KeyboardEvent keyboardEvent);
}
