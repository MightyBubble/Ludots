using Ludots.UI.Runtime;
using SkiaSharp;

namespace Ludots.UI.Skia;

internal sealed record UiTextRun(
	string Text,
	SKTypeface Typeface,
	bool Bold = false,
	bool Italic = false,
	bool HasColor = false,
	UiColor Color = default);
