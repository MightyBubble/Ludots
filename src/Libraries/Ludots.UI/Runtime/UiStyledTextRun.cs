using System;

namespace Ludots.UI.Runtime;

/// <summary>
/// One styled segment for rich text measurement/drawing. Platform-agnostic.
/// </summary>
public readonly record struct UiStyledTextRun(
	string Text,
	bool Bold = false,
	bool Italic = false,
	bool HasColor = false,
	UiColor Color = default)
{
	public static UiStyledTextRun Plain(string text) => new(text ?? string.Empty);
}
