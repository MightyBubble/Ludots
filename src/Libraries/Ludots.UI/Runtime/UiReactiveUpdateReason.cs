namespace Ludots.UI.Runtime;

public enum UiReactiveUpdateReason : byte
{
	None = 0,
	Mount = 1,
	StateChange = 2,
	RuntimeWindowChange = 3,
	ThemeChange = 4
}
