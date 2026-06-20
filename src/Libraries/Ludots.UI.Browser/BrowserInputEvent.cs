using System;

namespace Ludots.UI.Browser;

public abstract record BrowserInputEvent;

public enum BrowserPointerEventType
{
	Move,
	Down,
	Up,
	Leave
}

public enum BrowserPointerButton
{
	None,
	Left,
	Middle,
	Right
}

public sealed record BrowserPointerEvent(
	BrowserPointerEventType EventType,
	int PointerId,
	float X,
	float Y,
	BrowserPointerButton Button = BrowserPointerButton.None,
	bool IsPrimaryButtonDown = false) : BrowserInputEvent;

public sealed record BrowserWheelEvent(float X, float Y, float DeltaX, float DeltaY) : BrowserInputEvent;

public enum BrowserKeyEventType
{
	Down,
	Up,
	Character
}

[Flags]
public enum BrowserInputModifiers
{
	None = 0,
	Shift = 1 << 0,
	Control = 1 << 1,
	Alt = 1 << 2,
	Meta = 1 << 3
}

public sealed record BrowserKeyEvent(
	BrowserKeyEventType EventType,
	string Key,
	string? Code = null,
	BrowserInputModifiers Modifiers = BrowserInputModifiers.None) : BrowserInputEvent;

public sealed record BrowserFocusEvent(bool IsFocused) : BrowserInputEvent;

public sealed record BrowserTextInputEvent(string Text) : BrowserInputEvent;

public enum BrowserImeCompositionEventType
{
	Start,
	Update,
	Commit,
	Cancel
}

public sealed record BrowserImeCompositionEvent(
	BrowserImeCompositionEventType EventType,
	string Text,
	int SelectionStart = 0,
	int SelectionLength = 0) : BrowserInputEvent;
