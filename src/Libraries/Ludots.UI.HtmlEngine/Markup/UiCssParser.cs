using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ExCSS;
using Ludots.UI.Runtime;

namespace Ludots.UI.HtmlEngine.Markup;

public static class UiCssParser
{
	public static UiStyleSheet ParseStyleSheet(string css)
	{
		if (string.IsNullOrWhiteSpace(css))
		{
			return new UiStyleSheet();
		}
		StylesheetParser stylesheetParser = new StylesheetParser();
		Stylesheet stylesheet = stylesheetParser.Parse(css);
		UiStyleSheet uiStyleSheet = new UiStyleSheet();
		List<(string, string)> list = ParseRawRules(css).ToList();
		foreach (UiKeyframeDefinition item3 in ParseRawKeyframes(css))
		{
			uiStyleSheet.AddKeyframes(item3);
		}
		Dictionary<string, Queue<IStyleRule>> dictionary = stylesheet.StyleRules.GroupBy<IStyleRule, string>((IStyleRule rule) => NormalizeSelector(rule.SelectorText), StringComparer.OrdinalIgnoreCase).ToDictionary<IGrouping<string, IStyleRule>, string, Queue<IStyleRule>>((IGrouping<string, IStyleRule> group) => group.Key, (IGrouping<string, IStyleRule> group) => new Queue<IStyleRule>(group), StringComparer.OrdinalIgnoreCase);
		for (int num = 0; num < list.Count; num++)
		{
			(string, string) tuple = list[num];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			UiStyleDeclaration uiStyleDeclaration = new UiStyleDeclaration();
			if (dictionary.TryGetValue(NormalizeSelector(item), out var value) && value.Count > 0)
			{
				IStyleRule styleRule = value.Dequeue();
				foreach (Property item4 in styleRule.Style)
				{
					uiStyleDeclaration.Set(item4.Name, item4.Value);
				}
			}
			uiStyleDeclaration.Merge(ParseInline(item2));
			foreach (UiSelector item5 in UiSelectorParser.ParseMany(item))
			{
				uiStyleSheet.AddRule(item5, uiStyleDeclaration);
			}
		}
		foreach (var (text2, queue2) in dictionary)
		{
			while (queue2.Count > 0)
			{
				IStyleRule styleRule2 = queue2.Dequeue();
				UiStyleDeclaration uiStyleDeclaration2 = new UiStyleDeclaration();
				foreach (Property item6 in styleRule2.Style)
				{
					uiStyleDeclaration2.Set(item6.Name, item6.Value);
				}
				foreach (UiSelector item7 in UiSelectorParser.ParseMany(styleRule2.SelectorText))
				{
					uiStyleSheet.AddRule(item7, uiStyleDeclaration2);
				}
			}
		}
		return uiStyleSheet;
	}

	public static UiStyleDeclaration ParseInline(string inlineCss)
	{
		UiStyleDeclaration uiStyleDeclaration = new UiStyleDeclaration();
		if (string.IsNullOrWhiteSpace(inlineCss))
		{
			return uiStyleDeclaration;
		}
		foreach (string item in SplitInlineDeclarations(inlineCss))
		{
			if (TrySplitDeclaration(item, out string name, out string value))
			{
				uiStyleDeclaration.Set(name, value);
			}
		}
		return uiStyleDeclaration;
	}

	private static IEnumerable<string> SplitInlineDeclarations(string inlineCss)
	{
		int depth = 0;
		bool inSingleQuote = false;
		bool inDoubleQuote = false;
		int segmentStart = 0;
		int num2;
		for (int i = 0; i < inlineCss.Length; i++)
		{
			char current = inlineCss[i];
			char previous = ((i > 0) ? inlineCss[i - 1] : '\0');
			if (current == '\'' && !inDoubleQuote && previous != '\\')
			{
				inSingleQuote = !inSingleQuote;
			}
			else if (current == '"' && !inSingleQuote && previous != '\\')
			{
				inDoubleQuote = !inDoubleQuote;
			}
			else
			{
				if (inSingleQuote || inDoubleQuote)
				{
					continue;
				}
				int num;
				switch (current)
				{
				case '(':
					depth++;
					continue;
				case ')':
					num = ((depth > 0) ? 1 : 0);
					break;
				default:
					num = 0;
					break;
				}
				if (num != 0)
				{
					depth--;
				}
				else if (current == ';' && depth == 0)
				{
					num2 = segmentStart;
					string declaration = inlineCss.Substring(num2, i - num2).Trim();
					if (!string.IsNullOrWhiteSpace(declaration))
					{
						yield return declaration;
					}
					segmentStart = i + 1;
				}
			}
		}
		num2 = segmentStart;
		string tail = inlineCss.Substring(num2, inlineCss.Length - num2).Trim();
		if (!string.IsNullOrWhiteSpace(tail))
		{
			yield return tail;
		}
	}

	private static bool TrySplitDeclaration(string declaration, out string name, out string value)
	{
		int num = 0;
		bool flag = false;
		bool flag2 = false;
		for (int i = 0; i < declaration.Length; i++)
		{
			char c = declaration[i];
			char c2 = ((i > 0) ? declaration[i - 1] : '\0');
			if (c == '\'' && !flag2 && c2 != '\\')
			{
				flag = !flag;
			}
			else if (c == '"' && !flag && c2 != '\\')
			{
				flag2 = !flag2;
			}
			else
			{
				if (flag || flag2)
				{
					continue;
				}
				switch (c)
				{
				case '(':
					num++;
					continue;
				case ')':
					if (num > 0)
					{
						num--;
						continue;
					}
					break;
				}
				if (c == ':' && num == 0)
				{
					name = declaration.Substring(0, i).Trim();
					int num2 = i + 1;
					value = declaration.Substring(num2, declaration.Length - num2).Trim();
					return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value);
				}
			}
		}
		name = string.Empty;
		value = string.Empty;
		return false;
	}

	private static IEnumerable<(string SelectorText, string DeclarationText)> ParseRawRules(string css)
	{
		string content = StripComments(css);
		int index = 0;
		while (index < content.Length)
		{
			int openBrace = content.IndexOf('{', index);
			if (openBrace < 0)
			{
				break;
			}
			int num = index;
			string selectorText = content.Substring(num, openBrace - num).Trim();
			int closeBrace = FindMatchingBrace(content, openBrace + 1);
			if (closeBrace < 0)
			{
				break;
			}
			num = openBrace + 1;
			string declarationText = content.Substring(num, closeBrace - num).Trim();
			if (!string.IsNullOrWhiteSpace(selectorText) && !selectorText.StartsWith("@", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(declarationText))
			{
				yield return (SelectorText: selectorText, DeclarationText: declarationText);
			}
			index = closeBrace + 1;
		}
	}

	private static IEnumerable<UiKeyframeDefinition> ParseRawKeyframes(string css)
	{
		string content = StripComments(css);
		int index = 0;
		while (index < content.Length)
		{
			int atRuleIndex = content.IndexOf("@keyframes", index, StringComparison.OrdinalIgnoreCase);
			if (atRuleIndex < 0)
			{
				break;
			}
			int nameStart = atRuleIndex + "@keyframes".Length;
			int openBrace = content.IndexOf('{', nameStart);
			if (openBrace < 0)
			{
				break;
			}
			int num = nameStart;
			string name = content.Substring(num, openBrace - num).Trim();
			int closeBrace = FindMatchingBrace(content, openBrace + 1);
			if (closeBrace < 0)
			{
				break;
			}
			num = openBrace + 1;
			UiKeyframeDefinition definition = ParseRawKeyframeDefinition(name, content.Substring(num, closeBrace - num));
			if (definition != null)
			{
				yield return definition;
			}
			index = closeBrace + 1;
		}
	}

	private static UiKeyframeDefinition? ParseRawKeyframeDefinition(string name, string body)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		List<UiKeyframeStop> list = new List<UiKeyframeStop>();
		int num = 0;
		while (num < body.Length)
		{
			int num2 = body.IndexOf('{', num);
			if (num2 < 0)
			{
				break;
			}
			int num3 = num;
			string text = body.Substring(num3, num2 - num3).Trim();
			int num4 = FindMatchingBrace(body, num2 + 1);
			if (num4 < 0)
			{
				break;
			}
			num3 = num2 + 1;
			string inlineCss = body.Substring(num3, num4 - num3).Trim();
			UiStyleDeclaration declaration = ParseInline(inlineCss);
			string[] array = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			foreach (string selectorText in array)
			{
				if (TryParseKeyframeOffset(selectorText, out var offset))
				{
					list.Add(new UiKeyframeStop(offset, declaration));
				}
			}
			num = num4 + 1;
		}
		return (list.Count == 0) ? null : new UiKeyframeDefinition(name, list);
	}

	private static bool TryParseKeyframeOffset(string selectorText, out float offset)
	{
		string text = selectorText.Trim();
		if (text.Equals("from", StringComparison.OrdinalIgnoreCase))
		{
			offset = 0f;
			return true;
		}
		if (text.Equals("to", StringComparison.OrdinalIgnoreCase))
		{
			offset = 1f;
			return true;
		}
		if (text.EndsWith('%'))
		{
			string text2 = text;
			if (float.TryParse(text2.Substring(0, text2.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				offset = Math.Clamp(result / 100f, 0f, 1f);
				return true;
			}
		}
		offset = 0f;
		return false;
	}

	private static int FindMatchingBrace(string content, int startIndex)
	{
		int num = 1;
		for (int i = startIndex; i < content.Length; i++)
		{
			if (content[i] == '{')
			{
				num++;
			}
			else if (content[i] == '}')
			{
				num--;
				if (num == 0)
				{
					return i;
				}
			}
		}
		return -1;
	}

	private static string StripComments(string css)
	{
		return string.IsNullOrWhiteSpace(css) ? string.Empty : Regex.Replace(css, "/\\*.*?\\*/", string.Empty, RegexOptions.Singleline);
	}

	private static string NormalizeSelector(string selectorText)
	{
		return selectorText.Trim();
	}
}
