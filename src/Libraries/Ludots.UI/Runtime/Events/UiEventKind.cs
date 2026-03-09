namespace Ludots.UI.Runtime.Events;

public enum UiEventKind : byte
{
	Pointer = 0,
	FocusChanged = 1,
	Custom = byte.MaxValue
}
