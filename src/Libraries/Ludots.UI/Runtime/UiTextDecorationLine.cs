using System;

namespace Ludots.UI.Runtime;

[Flags]
public enum UiTextDecorationLine : byte
{
	None = 0,
	Underline = 1,
	LineThrough = 2
}
