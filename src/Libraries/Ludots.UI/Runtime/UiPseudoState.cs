using System;

namespace Ludots.UI.Runtime;

[Flags]
public enum UiPseudoState : ushort
{
	None = 0,
	Hover = 1,
	Active = 2,
	Focus = 4,
	Disabled = 8,
	Checked = 0x10,
	Selected = 0x20,
	Root = 0x40,
	Required = 0x80,
	Invalid = 0x100
}
