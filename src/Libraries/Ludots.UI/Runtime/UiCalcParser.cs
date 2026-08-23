using System;
using System.Globalization;

namespace Ludots.UI.Runtime;

/// <summary>
/// Recursive-descent parser for CSS calc() length expressions. Grammar (simplified CSS):
/// additive := multiplicative (('+' | '-') multiplicative)*
/// multiplicative := factor (('*' | '/') factor)*
/// factor := ['+' | '-'] number-unit | '(' additive ')' | 'calc(' additive ')'
/// Unitless numbers are accepted and stored as px-valued terms.
/// Malformed input fails deterministically (TryParse returns false, nothing is consumed globally).
/// </summary>
internal static class UiCalcParser
{
	public static bool TryParse(string text, out UiCalcExpression? expression)
	{
		expression = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		int pos = 0;
		if (!TryParseAdditive(text, ref pos, out expression) || !SkipWhitespace(text, ref pos) || pos != text.Length || expression == null)
		{
			expression = null;
			return false;
		}
		return true;
	}

	private static bool TryParseAdditive(string text, ref int pos, out UiCalcExpression? expr)
	{
		if (!TryParseMultiplicative(text, ref pos, out expr))
		{
			return false;
		}
		while (true)
		{
			SkipWhitespace(text, ref pos);
			if (pos >= text.Length)
			{
				return true;
			}
			char op = text[pos];
			if (op != '+' && op != '-')
			{
				return true;
			}
			pos++;
			SkipWhitespace(text, ref pos);
			if (!TryParseMultiplicative(text, ref pos, out UiCalcExpression? right) || right == null)
			{
				return false;
			}
			expr = new UiCalcExpression(expr, (op == '+') ? UiCalcOperator.Add : UiCalcOperator.Subtract, right);
		}
	}

	private static bool TryParseMultiplicative(string text, ref int pos, out UiCalcExpression? expr)
	{
		if (!TryParseFactor(text, ref pos, out expr))
		{
			return false;
		}
		while (true)
		{
			SkipWhitespace(text, ref pos);
			if (pos >= text.Length)
			{
				return true;
			}
			char op = text[pos];
			if (op != '*' && op != '/')
			{
				return true;
			}
			pos++;
			SkipWhitespace(text, ref pos);
			if (!TryParseFactor(text, ref pos, out UiCalcExpression? right) || right == null)
			{
				return false;
			}
			expr = new UiCalcExpression(expr, (op == '*') ? UiCalcOperator.Multiply : UiCalcOperator.Divide, right);
		}
	}

	private static bool TryParseFactor(string text, ref int pos, out UiCalcExpression? expr)
	{
		SkipWhitespace(text, ref pos);
		if (pos >= text.Length)
		{
			expr = null;
			return false;
		}
		if (text[pos] == '(')
		{
			pos++;
			if (!TryParseAdditive(text, ref pos, out expr) || !ExpectChar(text, ref pos, ')'))
			{
				expr = null;
				return false;
			}
			return expr != null;
		}
		if (pos + 5 <= text.Length && text.Substring(pos, 5).Equals("calc(", StringComparison.OrdinalIgnoreCase))
		{
			pos += 5;
			if (!TryParseAdditive(text, ref pos, out expr) || !ExpectChar(text, ref pos, ')'))
			{
				expr = null;
				return false;
			}
			return expr != null;
		}
		return TryParseLengthTerm(text, ref pos, out expr);
	}

	private static bool TryParseLengthTerm(string text, ref int pos, out UiCalcExpression? expr)
	{
		expr = null;
		SkipWhitespace(text, ref pos);
		int start = pos;
		if (pos < text.Length && (text[pos] == '+' || text[pos] == '-'))
		{
			pos++;
		}
		while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.'))
		{
			pos++;
		}
		if (pos == start || (pos == start + 1 && (text[start] == '+' || text[start] == '-')))
		{
			return false;
		}
		string numberPart = text.Substring(start, pos - start);
		string unit;
		if (pos < text.Length && text[pos] == '%')
		{
			pos++;
			unit = "%";
		}
		else
		{
			unit = ReadUnitSuffix(text, ref pos).ToLowerInvariant();
		}
		if (!float.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
		{
			return false;
		}
		UiLength length;
		switch (unit)
		{
		case "":
			length = UiLength.Px(value);
			break;
		case "px":
			length = UiLength.Px(value);
			break;
		case "%":
			length = UiLength.Percent(value);
			break;
		case "vw":
			length = new UiLength(value, UiLengthUnit.Vw);
			break;
		case "vh":
			length = new UiLength(value, UiLengthUnit.Vh);
			break;
		case "vmin":
			length = new UiLength(value, UiLengthUnit.Vmin);
			break;
		case "vmax":
			length = new UiLength(value, UiLengthUnit.Vmax);
			break;
		default:
			return false;
		}
		expr = new UiCalcExpression(length);
		return true;
	}

	private static string ReadUnitSuffix(string text, ref int pos)
	{
		int start = pos;
		while (pos < text.Length && char.IsLetter(text[pos]))
		{
			pos++;
		}
		return text.Substring(start, pos - start);
	}

	private static bool ExpectChar(string text, ref int pos, char expected)
	{
		SkipWhitespace(text, ref pos);
		if (pos < text.Length && text[pos] == expected)
		{
			pos++;
			return true;
		}
		return false;
	}

	private static bool SkipWhitespace(string text, ref int pos)
	{
		while (pos < text.Length && char.IsWhiteSpace(text[pos]))
		{
			pos++;
		}
		return true;
	}
}
