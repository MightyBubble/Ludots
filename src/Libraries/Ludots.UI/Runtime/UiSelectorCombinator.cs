namespace Ludots.UI.Runtime;

public enum UiSelectorCombinator : byte
{
    None = 0,
    Descendant = 1,
    Child = 2,
    AdjacentSibling = 3,
    GeneralSibling = 4
}
