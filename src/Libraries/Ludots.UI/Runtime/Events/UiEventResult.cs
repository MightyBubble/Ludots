namespace Ludots.UI.Runtime.Events;

public readonly record struct UiEventResult(bool Handled, bool Captured, bool BubbleStopped)
{
	public static UiEventResult Unhandled => new UiEventResult(Handled: false, Captured: false, BubbleStopped: false);

	public static UiEventResult CreateHandled(bool captured = false, bool bubbleStopped = false)
	{
		return new UiEventResult(Handled: true, captured, bubbleStopped);
	}
}
