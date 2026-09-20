using System;
using Ludots.UI.Browser;
using UltralightNet;

namespace Ludots.UI.Browser.Ultralight;

internal static class UltralightBrowserInputTranslator
{
	public static ULMouseEvent ToMouseEvent(BrowserPointerEvent pointer)
	{
		return new ULMouseEvent
		{
			Type = pointer.EventType switch
			{
				BrowserPointerEventType.Move or BrowserPointerEventType.Leave => ULMouseEventType.MouseMoved,
				BrowserPointerEventType.Down => ULMouseEventType.MouseDown,
				BrowserPointerEventType.Up => ULMouseEventType.MouseUp,
				_ => throw new ArgumentOutOfRangeException(nameof(pointer), pointer.EventType, "Unsupported pointer event.")
			},
			X = (int)MathF.Round(pointer.X),
			Y = (int)MathF.Round(pointer.Y),
			Button = pointer.Button switch
			{
				BrowserPointerButton.None => ULMouseEventButton.None,
				BrowserPointerButton.Left => ULMouseEventButton.Left,
				BrowserPointerButton.Middle => ULMouseEventButton.Middle,
				BrowserPointerButton.Right => ULMouseEventButton.Right,
				_ => ULMouseEventButton.None
			}
		};
	}

	public static ULScrollEvent ToScrollEvent(BrowserWheelEvent wheel)
	{
		return new ULScrollEvent
		{
			Type = ULScrollEventType.ByPixel,
			DeltaX = -(int)MathF.Round(wheel.DeltaX),
			DeltaY = -(int)MathF.Round(wheel.DeltaY)
		};
	}

	public static ULKeyEvent ToKeyEvent(BrowserKeyEvent key)
	{
		ULKeyEventType type = key.EventType switch
		{
			BrowserKeyEventType.Down => ULKeyEventType.RawKeyDown,
			BrowserKeyEventType.Up => ULKeyEventType.KeyUp,
			BrowserKeyEventType.Character => ULKeyEventType.Char,
			_ => throw new ArgumentOutOfRangeException(nameof(key), key.EventType, "Unsupported key event.")
		};

		ULKeyEventModifiers modifiers = default;
		if (key.Modifiers.HasFlag(BrowserInputModifiers.Shift))
		{
			modifiers |= ULKeyEventModifiers.ShiftKey;
		}
		if (key.Modifiers.HasFlag(BrowserInputModifiers.Control))
		{
			modifiers |= ULKeyEventModifiers.CtrlKey;
		}
		if (key.Modifiers.HasFlag(BrowserInputModifiers.Alt))
		{
			modifiers |= ULKeyEventModifiers.AltKey;
		}
		if (key.Modifiers.HasFlag(BrowserInputModifiers.Meta))
		{
			modifiers |= ULKeyEventModifiers.MetaKey;
		}

		string text = key.EventType == BrowserKeyEventType.Character ? key.Key : string.Empty;
		string unmodified = text;
		int virtualKeyCode = MapVirtualKey(key);
		int nativeKeyCode = virtualKeyCode;
		return ULKeyEvent.Create(
			type,
			modifiers,
			virtualKeyCode,
			nativeKeyCode,
			text,
			unmodified,
			isKeypad: false,
			isAutoRepeat: false,
			isSystemKey: false);
	}

	public static ULKeyEvent ToTextInputEvent(string text)
	{
		return ULKeyEvent.Create(
			ULKeyEventType.Char,
			default,
			0,
			0,
			text,
			text,
			isKeypad: false,
			isAutoRepeat: false,
			isSystemKey: false);
	}

	private static int MapVirtualKey(BrowserKeyEvent key)
	{
		if (!string.IsNullOrEmpty(key.Code))
		{
			return key.Code switch
			{
				"Enter" or "NumpadEnter" => ULKeyCodes.GK_RETURN,
				"Escape" => ULKeyCodes.GK_ESCAPE,
				"Backspace" => ULKeyCodes.GK_BACK,
				"Tab" => ULKeyCodes.GK_TAB,
				"Space" => ULKeyCodes.GK_SPACE,
				"ArrowLeft" => ULKeyCodes.GK_LEFT,
				"ArrowUp" => ULKeyCodes.GK_UP,
				"ArrowRight" => ULKeyCodes.GK_RIGHT,
				"ArrowDown" => ULKeyCodes.GK_DOWN,
				"Delete" => ULKeyCodes.GK_DELETE,
				_ => 0
			};
		}

		return key.Key switch
		{
			"Enter" => ULKeyCodes.GK_RETURN,
			"Escape" => ULKeyCodes.GK_ESCAPE,
			"Backspace" => ULKeyCodes.GK_BACK,
			"Tab" => ULKeyCodes.GK_TAB,
			" " => ULKeyCodes.GK_SPACE,
			_ => 0
		};
	}
}
