using System;

namespace Ludots.UI.Runtime;

[Flags]
public enum UiPseudoState : byte
{
    None = 0,
    Hover = 1 << 0,
    Active = 1 << 1,
    Focus = 1 << 2,
    Disabled = 1 << 3,
    Checked = 1 << 4,
    Selected = 1 << 5,
    Root = 1 << 6
}
