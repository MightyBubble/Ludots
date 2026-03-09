using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ludots.UI.Runtime;

public sealed class UiSelector
{
	private const int IdWeight = 10000;

	private const int ClassWeight = 100;

	public IReadOnlyList<UiSelectorPart> Parts { get; }

	public int Specificity { get; }

	public UiSelector(IReadOnlyList<UiSelectorPart> parts)
	{
		if (parts == null || parts.Count == 0)
		{
			throw new ArgumentException("Selector must contain at least one part.", "parts");
		}
		Parts = parts;
		Specificity = CalculateSpecificity(parts);
	}

	private static int CalculateSpecificity(IReadOnlyList<UiSelectorPart> parts)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (UiSelectorPart part in parts)
		{
			if (!string.IsNullOrWhiteSpace(part.Id))
			{
				num++;
			}
			num2 += part.Classes.Count;
			num2 += part.Attributes.Count;
			num2 += part.StructuralPseudos.Count;
			num2 += CountPseudoStateFlags(part.PseudoState);
			for (int i = 0; i < part.LogicalPseudos.Count; i++)
			{
				int specificity = part.LogicalPseudos[i].Specificity;
				num += specificity / 10000;
				specificity %= 10000;
				num2 += specificity / 100;
				num3 += specificity % 100;
			}
			if (!string.IsNullOrWhiteSpace(part.TagName) && part.TagName != "*")
			{
				num3++;
			}
		}
		return num * 10000 + num2 * 100 + num3;
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < Parts.Count; i++)
		{
			UiSelectorPart uiSelectorPart = Parts[i];
			if (i > 0)
			{
				stringBuilder.Append(FormatCombinator(uiSelectorPart.Combinator));
			}
			stringBuilder.Append(FormatPart(uiSelectorPart));
		}
		return stringBuilder.ToString();
	}

	private static string FormatPart(UiSelectorPart part)
	{
		string value = ((part.Classes.Count == 0) ? string.Empty : string.Concat(part.Classes.Select((string text) => "." + text)));
		string value2 = ((part.Attributes.Count == 0) ? string.Empty : string.Concat(part.Attributes.Select(FormatAttribute)));
		string value3 = (string.IsNullOrWhiteSpace(part.Id) ? string.Empty : ("#" + part.Id));
		string value4 = ((part.StructuralPseudos.Count == 0) ? string.Empty : string.Concat(part.StructuralPseudos.Select(FormatStructuralPseudo)));
		string value5 = ((part.LogicalPseudos.Count == 0) ? string.Empty : string.Concat(part.LogicalPseudos.Select(FormatLogicalPseudo)));
		string value6 = FormatPseudoState(part.PseudoState);
		return $"{part.TagName ?? "*"}{value3}{value}{value2}{value4}{value5}{value6}";
	}

	private static string FormatCombinator(UiSelectorCombinator combinator)
	{
		if (1 == 0)
		{
		}
		string result = combinator switch
		{
			UiSelectorCombinator.Child => " > ", 
			UiSelectorCombinator.AdjacentSibling => " + ", 
			UiSelectorCombinator.GeneralSibling => " ~ ", 
			_ => " ", 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string FormatAttribute(UiSelectorAttribute attribute)
	{
		if (attribute.Operator == UiSelectorAttributeOperator.Exists || attribute.Value == null)
		{
			return "[" + attribute.Name + "]";
		}
		UiSelectorAttributeOperator uiSelectorAttributeOperator = attribute.Operator;
		if (1 == 0)
		{
		}
		string text = uiSelectorAttributeOperator switch
		{
			UiSelectorAttributeOperator.Equals => "=", 
			UiSelectorAttributeOperator.Includes => "~=", 
			UiSelectorAttributeOperator.DashMatch => "|=", 
			UiSelectorAttributeOperator.Prefix => "^=", 
			UiSelectorAttributeOperator.Suffix => "$=", 
			UiSelectorAttributeOperator.Substring => "*=", 
			_ => string.Empty, 
		};
		if (1 == 0)
		{
		}
		string value = text;
		return $"[{attribute.Name}{value}{attribute.Value}]";
	}

	private static string FormatStructuralPseudo(UiStructuralPseudo pseudo)
	{
		UiStructuralPseudoKind kind = pseudo.Kind;
		if (1 == 0)
		{
		}
		string result = kind switch
		{
			UiStructuralPseudoKind.FirstChild => ":first-child", 
			UiStructuralPseudoKind.LastChild => ":last-child", 
			UiStructuralPseudoKind.NthChild => string.IsNullOrWhiteSpace(pseudo.Expression) ? ":nth-child(1)" : (":nth-child(" + pseudo.Expression + ")"), 
			UiStructuralPseudoKind.NthLastChild => string.IsNullOrWhiteSpace(pseudo.Expression) ? ":nth-last-child(1)" : (":nth-last-child(" + pseudo.Expression + ")"), 
			_ => string.Empty, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string FormatLogicalPseudo(UiSelectorLogicalPseudo pseudo)
	{
		UiSelectorLogicalPseudoKind kind = pseudo.Kind;
		if (1 == 0)
		{
		}
		string text = kind switch
		{
			UiSelectorLogicalPseudoKind.Not => "not", 
			UiSelectorLogicalPseudoKind.Is => "is", 
			UiSelectorLogicalPseudoKind.Where => "where", 
			_ => string.Empty, 
		};
		if (1 == 0)
		{
		}
		string value = text;
		return string.IsNullOrEmpty(value) ? string.Empty : $":{value}({string.Join(", ", pseudo.Selectors.Select((UiSelector selector) => selector.ToString()))})";
	}

	private static string FormatPseudoState(UiPseudoState pseudoState)
	{
		if (pseudoState == UiPseudoState.None)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Hover, "hover");
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Active, "active");
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Focus, "focus");
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Disabled, "disabled");
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Checked, "checked");
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Selected, "selected");
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Root, "root");
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Required, "required");
		AppendPseudoState(stringBuilder, pseudoState, UiPseudoState.Invalid, "invalid");
		return stringBuilder.ToString();
	}

	private static int CountPseudoStateFlags(UiPseudoState pseudoState)
	{
		int num = 0;
		num += (pseudoState.HasFlag(UiPseudoState.Hover) ? 1 : 0);
		num += (pseudoState.HasFlag(UiPseudoState.Active) ? 1 : 0);
		num += (pseudoState.HasFlag(UiPseudoState.Focus) ? 1 : 0);
		num += (pseudoState.HasFlag(UiPseudoState.Disabled) ? 1 : 0);
		num += (pseudoState.HasFlag(UiPseudoState.Checked) ? 1 : 0);
		num += (pseudoState.HasFlag(UiPseudoState.Selected) ? 1 : 0);
		num += (pseudoState.HasFlag(UiPseudoState.Root) ? 1 : 0);
		num += (pseudoState.HasFlag(UiPseudoState.Required) ? 1 : 0);
		return num + (pseudoState.HasFlag(UiPseudoState.Invalid) ? 1 : 0);
	}

	private static void AppendPseudoState(StringBuilder builder, UiPseudoState pseudoState, UiPseudoState flag, string text)
	{
		if (pseudoState.HasFlag(flag))
		{
			builder.Append(':').Append(text);
		}
	}
}
