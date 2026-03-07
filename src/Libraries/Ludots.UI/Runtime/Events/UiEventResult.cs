namespace Ludots.UI.Runtime.Events;

public readonly record struct UiEventResult(bool Handled, bool Captured, bool BubbleStopped)
{
    public static UiEventResult Unhandled => new(false, false, false);

    public static UiEventResult CreateHandled(bool captured = false, bool bubbleStopped = false) =>
        new(true, captured, bubbleStopped);
}
