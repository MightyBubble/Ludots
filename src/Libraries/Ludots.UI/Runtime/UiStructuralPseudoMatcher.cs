using System.Globalization;

namespace Ludots.UI.Runtime;

internal static class UiStructuralPseudoMatcher
{
    public static bool Matches(int siblingCount, int index, UiStructuralPseudo pseudo)
    {
        return pseudo.Kind switch
        {
            UiStructuralPseudoKind.FirstChild => index == 1,
            UiStructuralPseudoKind.LastChild => index == siblingCount,
            UiStructuralPseudoKind.NthChild => MatchesNthChild(pseudo.Expression, index),
            UiStructuralPseudoKind.NthLastChild => MatchesNthChild(pseudo.Expression, (siblingCount - index) + 1),
            _ => false
        };
    }

    private static bool MatchesNthChild(string? expression, int index)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        string normalized = expression.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (normalized == "odd")
        {
            return index % 2 == 1;
        }

        if (normalized == "even")
        {
            return index % 2 == 0;
        }

        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int exact))
        {
            return index == exact;
        }

        int nIndex = normalized.IndexOf('n');
        if (nIndex < 0)
        {
            return false;
        }

        string aPart = normalized[..nIndex];
        string bPart = normalized[(nIndex + 1)..];
        int a = aPart switch
        {
            "" or "+" => 1,
            "-" => -1,
            _ when int.TryParse(aPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedA) => parsedA,
            _ => int.MinValue
        };

        if (a == int.MinValue)
        {
            return false;
        }

        int b = 0;
        if (!string.IsNullOrEmpty(bPart) && !int.TryParse(bPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
        {
            return false;
        }

        if (a == 0)
        {
            return index == b;
        }

        int delta = index - b;
        if (a > 0)
        {
            return delta >= 0 && delta % a == 0;
        }

        return delta <= 0 && delta % a == 0;
    }
}
