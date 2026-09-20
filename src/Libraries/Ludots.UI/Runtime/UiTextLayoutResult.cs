using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public sealed record UiTextLayoutResult(
	IReadOnlyList<string> Lines,
	float Width,
	float Height,
	float LineHeight,
	float Ascent = 0f,
	float Descent = 0f);
