namespace Ludots.UI.Runtime;

public sealed record UiStructuralPseudo(UiStructuralPseudoKind Kind, string? Expression = null);

public enum UiStructuralPseudoKind : byte
{
    FirstChild = 0,
    LastChild = 1,
    NthChild = 2,
    NthLastChild = 3
}
