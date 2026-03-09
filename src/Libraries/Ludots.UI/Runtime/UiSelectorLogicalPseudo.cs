using System.Linq;

namespace Ludots.UI.Runtime;

public sealed class UiSelectorLogicalPseudo
{
    public UiSelectorLogicalPseudo(UiSelectorLogicalPseudoKind kind, IReadOnlyList<UiSelector> selectors)
    {
        if (selectors == null || selectors.Count == 0)
        {
            throw new ArgumentException("Logical pseudo requires at least one selector.", nameof(selectors));
        }

        Kind = kind;
        Selectors = selectors;
    }

    public UiSelectorLogicalPseudoKind Kind { get; }

    public IReadOnlyList<UiSelector> Selectors { get; }

    public int Specificity => Kind == UiSelectorLogicalPseudoKind.Where ? 0 : Selectors.Max(static selector => selector.Specificity);
}

public enum UiSelectorLogicalPseudoKind : byte
{
    Not = 0,
    Is = 1,
    Where = 2
}
