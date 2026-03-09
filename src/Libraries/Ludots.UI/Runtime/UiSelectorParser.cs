using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ludots.UI.Runtime;

public static class UiSelectorParser
{
	public static UiSelector Parse(string selectorText)
	{
		UiSelector[] array = ParseMany(selectorText).ToArray();
		if (array.Length != 1)
		{
			throw new InvalidOperationException($"Expected a single selector, got {array.Length} from '{selectorText}'.");
		}
		return array[0];
	}

	public static IReadOnlyList<UiSelector> ParseMany(string selectorText)
	{
		if (string.IsNullOrWhiteSpace(selectorText))
		{
			throw new ArgumentException("Selector text is required.", "selectorText");
		}
		List<string> list = SplitTopLevel(selectorText, ',');
		List<UiSelector> list2 = new List<UiSelector>(list.Count);
		foreach (string item in list)
		{
			list2.Add(ParseSingle(item));
		}
		return list2;
	}

	private static UiSelector ParseSingle(string selectorText)
	{
		List<UiSelectorPart> list = new List<UiSelectorPart>();
		UiSelectorCombinator uiSelectorCombinator = UiSelectorCombinator.None;
		int i = 0;
		while (i < selectorText.Length)
		{
			bool flag = false;
			for (; i < selectorText.Length && char.IsWhiteSpace(selectorText[i]); i++)
			{
				flag = true;
			}
			if (flag && list.Count > 0 && uiSelectorCombinator == UiSelectorCombinator.None)
			{
				uiSelectorCombinator = UiSelectorCombinator.Descendant;
			}
			if (i >= selectorText.Length)
			{
				break;
			}
			char c = selectorText[i];
			if (1 == 0)
			{
			}
			UiSelectorCombinator uiSelectorCombinator2 = c switch
			{
				'>' => UiSelectorCombinator.Child, 
				'+' => UiSelectorCombinator.AdjacentSibling, 
				'~' => UiSelectorCombinator.GeneralSibling, 
				_ => uiSelectorCombinator, 
			};
			if (1 == 0)
			{
			}
			uiSelectorCombinator = uiSelectorCombinator2;
			char c2 = selectorText[i];
			if ((c2 == '+' || c2 == '>' || c2 == '~') ? true : false)
			{
				i++;
				continue;
			}
			int num = i;
			int num2 = 0;
			int num3 = 0;
			for (; i < selectorText.Length; i++)
			{
				char c3 = selectorText[i];
				switch (c3)
				{
				case '[':
					num2++;
					continue;
				case ']':
					num2 = Math.Max(0, num2 - 1);
					continue;
				case '(':
					num3++;
					continue;
				case ')':
					num3 = Math.Max(0, num3 - 1);
					continue;
				default:
				{
					bool flag2 = num2 == 0 && num3 == 0;
					bool flag3 = flag2;
					if (flag3)
					{
						bool flag4 = char.IsWhiteSpace(c3);
						bool flag5 = flag4;
						if (!flag5)
						{
							bool flag6 = ((c3 == '+' || c3 == '>' || c3 == '~') ? true : false);
							flag5 = flag6;
						}
						flag3 = flag5;
					}
					if (!flag3)
					{
						continue;
					}
					break;
				}
				}
				break;
			}
			int num4 = num;
			string text = selectorText.Substring(num4, i - num4).Trim();
			if (text.Length != 0)
			{
				UiSelectorCombinator combinator = ((list.Count != 0) ? ((uiSelectorCombinator == UiSelectorCombinator.None) ? UiSelectorCombinator.Descendant : uiSelectorCombinator) : UiSelectorCombinator.None);
				list.Add(ParseToken(text, combinator));
				uiSelectorCombinator = UiSelectorCombinator.None;
			}
		}
		return new UiSelector(list);
	}

	private static UiSelectorPart ParseToken(string token, UiSelectorCombinator combinator)
	{
		string text = null;
		string id = null;
		List<string> list = new List<string>();
		List<UiSelectorAttribute> list2 = new List<UiSelectorAttribute>();
		List<UiStructuralPseudo> structuralPseudos = new List<UiStructuralPseudo>();
		List<UiSelectorLogicalPseudo> logicalPseudos = new List<UiSelectorLogicalPseudo>();
		UiPseudoState pseudoState = UiPseudoState.None;
		int i = 0;
		while (i < token.Length)
		{
			switch (token[i])
			{
			case '#':
				i++;
				id = ReadIdentifier(token, ref i);
				break;
			case '.':
			{
				i++;
				string text4 = ReadIdentifier(token, ref i);
				if (!string.IsNullOrWhiteSpace(text4))
				{
					list.Add(text4);
				}
				break;
			}
			case ':':
				i++;
				if (i < token.Length && token[i] == ':')
				{
					i++;
				}
				ApplyPseudo(ReadPseudoToken(token, ref i), ref pseudoState, structuralPseudos, logicalPseudos);
				break;
			case '[':
			{
				i++;
				int num = i;
				int num2 = 1;
				for (; i < token.Length; i++)
				{
					if (num2 <= 0)
					{
						break;
					}
					if (token[i] == '[')
					{
						num2++;
					}
					else if (token[i] == ']')
					{
						num2--;
						if (num2 == 0)
						{
							break;
						}
					}
				}
				int num3 = num;
				string text3 = token.Substring(num3, Math.Min(i, token.Length) - num3).Trim();
				if (i < token.Length && token[i] == ']')
				{
					i++;
				}
				if (!string.IsNullOrWhiteSpace(text3))
				{
					list2.Add(ParseAttribute(text3));
				}
				break;
			}
			default:
			{
				string text2 = ReadIdentifier(token, ref i);
				if (!string.IsNullOrWhiteSpace(text2))
				{
					text = text2;
				}
				else
				{
					i++;
				}
				break;
			}
			}
		}
		if (text == null)
		{
			text = "*";
		}
		return new UiSelectorPart(text, id, list, list2, structuralPseudos, logicalPseudos, pseudoState, combinator);
	}

	private static string ReadIdentifier(string token, ref int index)
	{
		StringBuilder stringBuilder = new StringBuilder();
		while (index < token.Length)
		{
			char c = token[index];
			bool flag;
			switch (c)
			{
			case '#':
			case '(':
			case ')':
			case '.':
			case ':':
			case '[':
			case ']':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				break;
			}
			stringBuilder.Append(c);
			index++;
		}
		return stringBuilder.ToString().Trim();
	}

	private static string ReadPseudoToken(string token, ref int index)
	{
		int num = index;
		while (index < token.Length)
		{
			char c = token[index];
			bool flag;
			switch (c)
			{
			case '#':
			case '.':
			case ':':
			case '[':
			case ']':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				break;
			}
			if (c == '(')
			{
				int num2 = 1;
				index++;
				while (index < token.Length && num2 > 0)
				{
					if (token[index] == '(')
					{
						num2++;
					}
					else if (token[index] == ')')
					{
						num2--;
					}
					index++;
				}
				break;
			}
			index++;
		}
		int num3 = num;
		return token.Substring(num3, Math.Min(index, token.Length) - num3).Trim();
	}

	private static UiSelectorAttribute ParseAttribute(string expression)
	{
		string[] array = new string[6] { "~=", "|=", "^=", "$=", "*=", "=" };
		string[] array2 = array;
		foreach (string text in array2)
		{
			int num = expression.IndexOf(text, StringComparison.Ordinal);
			if (num >= 0)
			{
				string name = expression.Substring(0, num).Trim();
				int num2 = num + text.Length;
				string value = expression.Substring(num2, expression.Length - num2).Trim().Trim('"', '\'');
				return new UiSelectorAttribute(name, value, ParseAttributeOperator(text));
			}
		}
		return new UiSelectorAttribute(expression.Trim(), null);
	}

	private static UiSelectorAttributeOperator ParseAttributeOperator(string operatorToken)
	{
		if (1 == 0)
		{
		}
		UiSelectorAttributeOperator result = operatorToken switch
		{
			"=" => UiSelectorAttributeOperator.Equals, 
			"~=" => UiSelectorAttributeOperator.Includes, 
			"|=" => UiSelectorAttributeOperator.DashMatch, 
			"^=" => UiSelectorAttributeOperator.Prefix, 
			"$=" => UiSelectorAttributeOperator.Suffix, 
			"*=" => UiSelectorAttributeOperator.Substring, 
			_ => UiSelectorAttributeOperator.Exists, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static void ApplyPseudo(string value, ref UiPseudoState pseudoState, ICollection<UiStructuralPseudo> structuralPseudos, ICollection<UiSelectorLogicalPseudo> logicalPseudos)
	{
		string text = value.Trim().ToLowerInvariant();
		switch (text)
		{
		case "hover":
			pseudoState |= UiPseudoState.Hover;
			return;
		case "active":
			pseudoState |= UiPseudoState.Active;
			return;
		case "focus":
			pseudoState |= UiPseudoState.Focus;
			return;
		case "disabled":
			pseudoState |= UiPseudoState.Disabled;
			return;
		case "checked":
			pseudoState |= UiPseudoState.Checked;
			return;
		case "selected":
			pseudoState |= UiPseudoState.Selected;
			return;
		case "required":
			pseudoState |= UiPseudoState.Required;
			return;
		case "invalid":
			pseudoState |= UiPseudoState.Invalid;
			return;
		case "root":
			pseudoState |= UiPseudoState.Root;
			return;
		case "first-child":
			structuralPseudos.Add(new UiStructuralPseudo(UiStructuralPseudoKind.FirstChild));
			return;
		case "last-child":
			structuralPseudos.Add(new UiStructuralPseudo(UiStructuralPseudoKind.LastChild));
			return;
		}
		UiSelectorLogicalPseudo pseudo;
		if (text.StartsWith("nth-child(", StringComparison.Ordinal) && text.EndsWith(')'))
		{
			string text2 = text;
			int length = "nth-child(".Length;
			string expression = text2.Substring(length, text2.Length - 1 - length).Trim();
			structuralPseudos.Add(new UiStructuralPseudo(UiStructuralPseudoKind.NthChild, expression));
		}
		else if (text.StartsWith("nth-last-child(", StringComparison.Ordinal) && text.EndsWith(')'))
		{
			string text2 = text;
			int length = "nth-last-child(".Length;
			string expression2 = text2.Substring(length, text2.Length - 1 - length).Trim();
			structuralPseudos.Add(new UiStructuralPseudo(UiStructuralPseudoKind.NthLastChild, expression2));
		}
		else if (TryParseLogicalPseudo(text, "not", UiSelectorLogicalPseudoKind.Not, out pseudo) || TryParseLogicalPseudo(text, "is", UiSelectorLogicalPseudoKind.Is, out pseudo) || TryParseLogicalPseudo(text, "where", UiSelectorLogicalPseudoKind.Where, out pseudo))
		{
			logicalPseudos.Add(pseudo);
		}
	}

	private static bool TryParseLogicalPseudo(string value, string name, UiSelectorLogicalPseudoKind kind, out UiSelectorLogicalPseudo? pseudo)
	{
		string text = name + "(";
		if (!value.StartsWith(text, StringComparison.Ordinal) || !value.EndsWith(')'))
		{
			pseudo = null;
			return false;
		}
		int length = text.Length;
		string selectorText = value.Substring(length, value.Length - 1 - length).Trim();
		IReadOnlyList<UiSelector> selectors = ParseMany(selectorText);
		pseudo = new UiSelectorLogicalPseudo(kind, selectors);
		return true;
	}

	private static List<string> SplitTopLevel(string text, char separator)
	{
		List<string> list = new List<string>();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			switch (c)
			{
			case '[':
				num++;
				continue;
			case ']':
				num--;
				continue;
			case '(':
				num2++;
				continue;
			case ')':
				num2--;
				continue;
			}
			if (c == separator && num == 0 && num2 == 0)
			{
				num4 = num3;
				string text2 = text.Substring(num4, i - num4).Trim();
				if (text2.Length > 0)
				{
					list.Add(text2);
				}
				num3 = i + 1;
			}
		}
		num4 = num3;
		string text3 = text.Substring(num4, text.Length - num4).Trim();
		if (text3.Length > 0)
		{
			list.Add(text3);
		}
		return list;
	}
}
