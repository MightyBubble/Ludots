namespace Ludots.UI.Input;

public enum KeyboardAction
{
	Down,
	Up,
	Character
}

public sealed class KeyboardEvent : InputEvent
{
	public KeyboardAction Action { get; set; }

	public string Key { get; set; } = string.Empty;

	public string? Code { get; set; }

	public string? Text { get; set; }

	public int Modifiers { get; set; }
}
