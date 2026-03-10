using System;
using System.Globalization;

namespace Ludots.UI.Runtime;

internal static class UiStructuralPseudoMatcher
{
	public static bool Matches(int siblingCount, int index, UiStructuralPseudo pseudo)
	{
		UiStructuralPseudoKind kind = pseudo.Kind;
		if (1 == 0)
		{
		}
		bool result = kind switch
		{
			UiStructuralPseudoKind.FirstChild => index == 1, 
			UiStructuralPseudoKind.LastChild => index == siblingCount, 
			UiStructuralPseudoKind.NthChild => MatchesNthChild(pseudo.Expression, index), 
			UiStructuralPseudoKind.NthLastChild => MatchesNthChild(pseudo.Expression, siblingCount - index + 1), 
			_ => false, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool MatchesNthChild(string? expression, int index)
	{
		if (string.IsNullOrWhiteSpace(expression))
		{
			return false;
		}
		string text = expression.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
		if (text == "odd")
		{
			return index % 2 == 1;
		}
		if (text == "even")
		{
			return index % 2 == 0;
		}
		if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return index == result;
		}
		int num = text.IndexOf('n');
		if (num < 0)
		{
			return false;
		}
		string text2 = text.Substring(0, num);
		string text3 = text;
		int num2 = num + 1;
		string text4 = text3.Substring(num2, text3.Length - num2);
		string text5 = text2;
		if (1 == 0)
		{
		}
		num2 = (((text5 != null && text5.Length == 0) || text5 == "+") ? 1 : ((!(text5 == "-")) ? ((!int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2)) ? int.MinValue : result2) : (-1)));
		if (1 == 0)
		{
		}
		int num3 = num2;
		if (num3 == int.MinValue)
		{
			return false;
		}
		int result3 = 0;
		if (!string.IsNullOrEmpty(text4) && !int.TryParse(text4, NumberStyles.Integer, CultureInfo.InvariantCulture, out result3))
		{
			return false;
		}
		if (num3 == 0)
		{
			return index == result3;
		}
		int num4 = index - result3;
		if (num3 > 0)
		{
			return num4 >= 0 && num4 % num3 == 0;
		}
		return num4 <= 0 && num4 % num3 == 0;
	}
}
