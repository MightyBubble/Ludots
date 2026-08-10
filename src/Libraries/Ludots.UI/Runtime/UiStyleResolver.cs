using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Ludots.UI.Runtime;

public sealed class UiStyleResolver
{
	private static readonly Comparison<(int Specificity, int Order, UiStyleDeclaration Declaration)> MatchComparison =
		static (left, right) =>
		{
			int bySpecificity = left.Specificity.CompareTo(right.Specificity);
			return bySpecificity != 0 ? bySpecificity : left.Order.CompareTo(right.Order);
		};

	private readonly List<(int Specificity, int Order, UiStyleDeclaration Declaration)> _matchBuffer = new List<(int, int, UiStyleDeclaration)>(64);
	private readonly UiStyleDeclaration _cascadeBuffer = new UiStyleDeclaration();
	private readonly UiStyleBuilder _styleBuilder = new UiStyleBuilder();
	private readonly Dictionary<string, string> _rootVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	private readonly List<Dictionary<string, string>> _variableFramePool = new List<Dictionary<string, string>>();
	private readonly List<Dictionary<string, string>> _variableFramesRented = new List<Dictionary<string, string>>();

	public void ResolveTree(UiNode root, IReadOnlyList<UiStyleSheet>? styleSheets)
	{
		ArgumentNullException.ThrowIfNull(root, "root");
		IReadOnlyList<UiStyleSheet> styleSheets2 = styleSheets ?? Array.Empty<UiStyleSheet>();
		IReadOnlyDictionary<string, UiKeyframeDefinition> keyframes = BuildKeyframeIndex(styleSheets2);
		_rootVariables.Clear();
		try
		{
			ResolveNode(root, styleSheets2, keyframes, null, _rootVariables, isRoot: true);
		}
		finally
		{
			ReleaseVariableFrames();
		}
	}

	internal static bool TryResolveGeneratedContent(UiElement host, UiPseudoElement pseudoElement, IReadOnlyList<UiStyleSheet> styleSheets, out string text)
	{
		ArgumentNullException.ThrowIfNull(host, "host");
		text = string.Empty;
		if (pseudoElement != UiPseudoElement.Before && pseudoElement != UiPseudoElement.After)
		{
			return false;
		}
		IReadOnlyList<UiStyleSheet> sheets = styleSheets ?? Array.Empty<UiStyleSheet>();
		List<(int Specificity, int Order, string Value)> matches = new List<(int, int, string)>();
		int sequence = 0;
		for (int i = 0; i < sheets.Count; i++)
		{
			foreach (UiStyleRule rule in sheets[i].Rules)
			{
				if (rule.Selector.PseudoElement != pseudoElement)
				{
					continue;
				}
				if (!UiElementSelectorMatcher.MatchesOriginatingElement(host, rule.Selector))
				{
					continue;
				}
				string? contentValue = rule.Declaration["content"];
				if (contentValue == null)
				{
					continue;
				}
				matches.Add((rule.Selector.Specificity, i * 10000 + rule.Order + sequence++, contentValue));
			}
		}
		if (matches.Count == 0)
		{
			return false;
		}
		matches.Sort(static (left, right) =>
		{
			int bySpecificity = left.Specificity.CompareTo(right.Specificity);
			return bySpecificity != 0 ? bySpecificity : left.Order.CompareTo(right.Order);
		});
		string winning = matches[^1].Value;
		return TryEvaluateContent(winning, host, out text);
	}

	internal static bool TryEvaluateContent(string rawValue, UiElement host, out string text)
	{
		text = string.Empty;
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return false;
		}
		string value = rawValue.Trim();
		if (value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
			value.Equals("normal", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (value.StartsWith("attr(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
		{
			string attributeName = value.Substring(5, value.Length - 6).Trim().Trim('"', '\'');
			if (string.IsNullOrWhiteSpace(attributeName))
			{
				return false;
			}
			text = host.Attributes[attributeName] ?? string.Empty;
			return true;
		}
		if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
		{
			text = UnescapeContentString(value.Substring(1, value.Length - 2));
			return true;
		}
		return false;
	}

	private static string UnescapeContentString(string value)
	{
		return value
			.Replace("\\\"", "\"", StringComparison.Ordinal)
			.Replace("\\'", "'", StringComparison.Ordinal)
			.Replace("\\\\", "\\", StringComparison.Ordinal);
	}

	private void ResolveNode(UiNode node, IReadOnlyList<UiStyleSheet> styleSheets, IReadOnlyDictionary<string, UiKeyframeDefinition> keyframes, UiStyle? parentStyle, Dictionary<string, string> inheritedVariables, bool isRoot)
	{
		if (isRoot)
		{
			node.AddPseudoState(UiPseudoState.Root);
		}
		else
		{
			node.RemovePseudoState(UiPseudoState.Root);
		}
		_matchBuffer.Clear();
		int sequence = 0;
		for (int i = 0; i < styleSheets.Count; i++)
		{
			IReadOnlyList<UiStyleRule> rules = styleSheets[i].Rules;
			for (int r = 0; r < rules.Count; r++)
			{
				UiStyleRule rule = rules[r];
				if (UiSelectorMatcher.Matches(node, rule.Selector))
				{
					_matchBuffer.Add((rule.Selector.Specificity, i * 10000 + rule.Order + sequence++, rule.Declaration));
				}
			}
		}
		_matchBuffer.Sort(MatchComparison);
		_cascadeBuffer.Clear();
		for (int i = 0; i < _matchBuffer.Count; i++)
		{
			_cascadeBuffer.Merge(_matchBuffer[i].Declaration);
		}
		_cascadeBuffer.Merge(node.InlineStyle);
		Dictionary<string, string> variables = inheritedVariables;
		if (HasCustomProperties(_cascadeBuffer))
		{
			variables = RentVariableFrame(inheritedVariables);
			ApplyCustomProperties(_cascadeBuffer, variables);
		}
		_styleBuilder.CopyFrom(node.LocalStyle);
		ApplyDeclaration(_styleBuilder, _cascadeBuffer, variables);
		ApplyUserAgentTagDefaults(node, _styleBuilder, _cascadeBuffer);
		ApplyInheritance(node, _styleBuilder, parentStyle, _cascadeBuffer);
		if (node.Kind == UiNodeKind.Text && _styleBuilder.Display == UiDisplay.Flex)
		{
			_styleBuilder.Display = UiDisplay.Text;
		}
		UiStyle localStyle = _styleBuilder.ToStyle();
		UiAnimationSpec animation = ResolveAnimationSpec(localStyle.Animation, keyframes, variables);
		node.SetComputedStyle(localStyle, animation);
		for (int i = 0; i < node.Children.Count; i++)
		{
			ResolveNode(node.Children[i], styleSheets, keyframes, localStyle, variables, isRoot: false);
		}
	}

	private Dictionary<string, string> RentVariableFrame(Dictionary<string, string> parent)
	{
		Dictionary<string, string> frame = _variableFramePool.Count > 0
			? _variableFramePool[_variableFramePool.Count - 1]
			: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (_variableFramePool.Count > 0)
		{
			_variableFramePool.RemoveAt(_variableFramePool.Count - 1);
		}
		frame.Clear();
		foreach (KeyValuePair<string, string> item in parent)
		{
			frame[item.Key] = item.Value;
		}
		_variableFramesRented.Add(frame);
		return frame;
	}

	private void ReleaseVariableFrames()
	{
		for (int i = 0; i < _variableFramesRented.Count; i++)
		{
			Dictionary<string, string> frame = _variableFramesRented[i];
			frame.Clear();
			_variableFramePool.Add(frame);
		}
		_variableFramesRented.Clear();
	}

	private static bool HasCustomProperties(UiStyleDeclaration declaration)
	{
		Dictionary<string, string>.Enumerator enumerator = declaration.EnumerateValues();
		while (enumerator.MoveNext())
		{
			if (enumerator.Current.Key.StartsWith("--", StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static IReadOnlyDictionary<string, UiKeyframeDefinition> BuildKeyframeIndex(IReadOnlyList<UiStyleSheet> styleSheets)
	{
		Dictionary<string, UiKeyframeDefinition> dictionary = new Dictionary<string, UiKeyframeDefinition>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < styleSheets.Count; i++)
		{
			foreach (UiKeyframeDefinition keyframe in styleSheets[i].Keyframes)
			{
				dictionary[keyframe.Name] = keyframe;
			}
		}
		return dictionary;
	}

	private static UiAnimationSpec? ResolveAnimationSpec(UiAnimationSpec? animation, IReadOnlyDictionary<string, UiKeyframeDefinition> keyframes, IReadOnlyDictionary<string, string> variables)
	{
		if (animation == null || animation.Entries.Count == 0)
		{
			return null;
		}
		List<UiAnimationEntry> list = new List<UiAnimationEntry>(animation.Entries.Count);
		for (int i = 0; i < animation.Entries.Count; i++)
		{
			UiAnimationEntry uiAnimationEntry = animation.Entries[i];
			if (keyframes.TryGetValue(uiAnimationEntry.Name, out UiKeyframeDefinition value) && value != null)
			{
				list.Add(uiAnimationEntry with
				{
					Keyframes = ResolveKeyframes(value, variables)
				});
			}
		}
		return (list.Count == 0) ? null : new UiAnimationSpec(list);
	}

	private static UiKeyframeDefinition ResolveKeyframes(UiKeyframeDefinition definition, IReadOnlyDictionary<string, string> variables)
	{
		List<UiKeyframeStop> list = new List<UiKeyframeStop>(definition.Stops.Count);
		for (int i = 0; i < definition.Stops.Count; i++)
		{
			UiKeyframeStop uiKeyframeStop = definition.Stops[i];
			UiStyleDeclaration uiStyleDeclaration = new UiStyleDeclaration();
			foreach (KeyValuePair<string, string> item in uiKeyframeStop.Declaration)
			{
				uiStyleDeclaration.Set(item.Key, ResolveValue(item.Value, variables));
			}
			list.Add(new UiKeyframeStop(uiKeyframeStop.Offset, uiStyleDeclaration));
		}
		return new UiKeyframeDefinition(definition.Name, list);
	}

	private static void ApplyCustomProperties(UiStyleDeclaration declaration, IDictionary<string, string> variables)
	{
		Dictionary<string, string>.Enumerator enumerator = declaration.EnumerateValues();
		while (enumerator.MoveNext())
		{
			KeyValuePair<string, string> item = enumerator.Current;
			if (item.Key.StartsWith("--", StringComparison.Ordinal))
			{
				variables[item.Key] = ResolveValue(item.Value, (IReadOnlyDictionary<string, string>)variables);
			}
		}
	}

	private static void ApplyUserAgentTagDefaults(UiNode node, UiStyleBuilder style, UiStyleDeclaration declaration)
	{
		string tag = node.TagName ?? string.Empty;
		if (tag.Equals("strong", StringComparison.OrdinalIgnoreCase) || tag.Equals("b", StringComparison.OrdinalIgnoreCase))
		{
			if (!HasExplicitValue(declaration, "display"))
			{
				style.Display = UiDisplay.Inline;
			}
			if (!HasExplicitValue(declaration, "font-weight"))
			{
				style.Bold = true;
			}
		}
		else if (tag.Equals("em", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("i", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("span", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("a", StringComparison.OrdinalIgnoreCase))
		{
			if (!HasExplicitValue(declaration, "display"))
			{
				style.Display = UiDisplay.Inline;
			}
		}
		else if (tag.Equals("p", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("h1", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("h2", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("h3", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("h4", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("h5", StringComparison.OrdinalIgnoreCase) ||
			tag.Equals("h6", StringComparison.OrdinalIgnoreCase))
		{
			if (!HasExplicitValue(declaration, "display"))
			{
				style.Display = UiDisplay.Block;
			}
		}
		else if (tag.Equals("img", StringComparison.OrdinalIgnoreCase))
		{
			if (!HasExplicitValue(declaration, "display"))
			{
				style.Display = UiDisplay.Inline;
			}
		}
	}

	private static void ApplyInheritance(UiNode node, UiStyleBuilder style, UiStyle? parentStyle, UiStyleDeclaration declaration)
	{
		if (parentStyle == null)
		{
			return;
		}
		UiStyle implicitStyle = GetImplicitStyle(node.Kind);
		if (!HasExplicitValue(declaration, "color") && style.Color == implicitStyle.Color)
		{
			style.Color = parentStyle.Color;
		}
		if (!HasExplicitValue(declaration, "font-size") && Math.Abs(style.FontSize - implicitStyle.FontSize) < 0.01f)
		{
			style.FontSize = parentStyle.FontSize;
		}
		if (!HasExplicitValue(declaration, "font-weight") && style.Bold == implicitStyle.Bold)
		{
			style.Bold = parentStyle.Bold;
		}
		if (!HasExplicitValue(declaration, "font-family") && string.Equals(style.FontFamily, implicitStyle.FontFamily, StringComparison.Ordinal))
		{
			style.FontFamily = parentStyle.FontFamily;
		}
		if (!HasExplicitValue(declaration, "white-space") && style.WhiteSpace == implicitStyle.WhiteSpace)
		{
			style.WhiteSpace = parentStyle.WhiteSpace;
		}
		if (!HasExplicitValue(declaration, "direction") && style.Direction == implicitStyle.Direction)
		{
			style.Direction = parentStyle.Direction;
		}
		if (!HasExplicitValue(declaration, "text-align") && style.TextAlign == implicitStyle.TextAlign)
		{
			style.TextAlign = parentStyle.TextAlign;
		}
	}

	private static bool HasExplicitValue(UiStyleDeclaration declaration, string propertyName)
	{
		return declaration[propertyName] != null;
	}

	private static readonly UiStyle[] ImplicitStylesByKind = CreateImplicitStylesByKind();

	private static UiStyle[] CreateImplicitStylesByKind()
	{
		int count = Enum.GetValues<UiNodeKind>().Length;
		UiStyle[] styles = new UiStyle[count];
		for (int i = 0; i < count; i++)
		{
			styles[i] = CreateImplicitStyle((UiNodeKind)i);
		}
		return styles;
	}

	private static UiStyle GetImplicitStyle(UiNodeKind kind)
	{
		int index = (int)kind;
		if ((uint)index < (uint)ImplicitStylesByKind.Length)
		{
			return ImplicitStylesByKind[index];
		}
		return UiStyle.Default;
	}

	private static UiStyle CreateImplicitStyle(UiNodeKind kind)
	{
		UiStyle result;
		switch (kind)
		{
		case UiNodeKind.Row:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				AlignItems = UiAlignItems.Center
			};
			break;
		case UiNodeKind.Column:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column
			};
			break;
		case UiNodeKind.Text:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Text,
				Color = UiColor.White
			};
			break;
		case UiNodeKind.Button:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				AlignItems = UiAlignItems.Center,
				JustifyContent = UiJustifyContent.Center,
				Padding = UiThickness.Symmetric(16f, 10f),
				BackgroundColor = new UiColor(58, 121, 220),
				BorderRadius = 10f,
				Color = UiColor.White
			};
			break;
		case UiNodeKind.Checkbox:
		case UiNodeKind.Radio:
		case UiNodeKind.Toggle:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				AlignItems = UiAlignItems.Center,
				Padding = UiThickness.Symmetric(10f, 8f),
				BorderRadius = 8f
			};
			break;
		case UiNodeKind.Table:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				AlignItems = UiAlignItems.Stretch
			};
			break;
		case UiNodeKind.TableHeader:
		case UiNodeKind.TableBody:
		case UiNodeKind.TableFooter:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				AlignItems = UiAlignItems.Stretch
			};
			break;
		case UiNodeKind.TableRow:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				AlignItems = UiAlignItems.Stretch
			};
			break;
		case UiNodeKind.TableCell:
		case UiNodeKind.TableHeaderCell:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				AlignItems = UiAlignItems.Stretch,
				FlexGrow = 1f,
				FlexShrink = 1f,
				FlexBasis = UiLength.Px(0f)
			};
			break;
		case UiNodeKind.Card:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				Padding = UiThickness.All(16f),
				BackgroundColor = new UiColor(25, 31, 48),
				BorderRadius = 12f
			};
			break;
		case UiNodeKind.Panel:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column
			};
			break;
		default:
			result = UiStyle.Default;
			break;
		}
		return result;
	}

	private static void ApplyDeclaration(UiStyleBuilder style, UiStyleDeclaration declaration, IReadOnlyDictionary<string, string> variables)
	{
		Dictionary<string, string>.Enumerator enumerator = declaration.EnumerateValues();
		while (enumerator.MoveNext())
		{
			KeyValuePair<string, string> item = enumerator.Current;
			if (!item.Key.StartsWith("--", StringComparison.Ordinal))
			{
				string rawValue = ResolveValue(item.Value, variables);
				ApplyProperty(style, item.Key, rawValue);
			}
		}
	}

	private static string ResolveValue(string rawValue, IReadOnlyDictionary<string, string> variables, int depth = 0)
	{
		if (string.IsNullOrWhiteSpace(rawValue) || depth > 8 || !rawValue.Contains("var(", StringComparison.OrdinalIgnoreCase))
		{
			return rawValue.Trim();
		}
		string text = rawValue;
		for (int num = text.IndexOf("var(", StringComparison.OrdinalIgnoreCase); num >= 0; num = text.IndexOf("var(", StringComparison.OrdinalIgnoreCase))
		{
			int num2 = FindVarEnd(text, num + 4);
			if (num2 < 0)
			{
				break;
			}
			string expression = text.Substring(num + 4, num2 - (num + 4));
			string text2 = ResolveVariableExpression(expression, variables, depth + 1);
			string text3 = text.Substring(0, num);
			string text4 = text;
			int num3 = num2 + 1;
			text = text3 + text2 + text4.Substring(num3, text4.Length - num3);
		}
		return text.Trim();
	}

	private static int FindVarEnd(string value, int searchStart)
	{
		int num = 1;
		for (int i = searchStart; i < value.Length; i++)
		{
			if (value[i] == '(')
			{
				num++;
			}
			else if (value[i] == ')')
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

	private static string ResolveVariableExpression(string expression, IReadOnlyDictionary<string, string> variables, int depth)
	{
		int num = FindTopLevelComma(expression);
		string key = ((num >= 0) ? expression.Substring(0, num).Trim() : expression.Trim());
		object obj;
		if (num < 0)
		{
			obj = null;
		}
		else
		{
			int num2 = num + 1;
			obj = expression.Substring(num2, expression.Length - num2).Trim();
		}
		string text = (string)obj;
		if (variables.TryGetValue(key, out string value))
		{
			return ResolveValue(value, variables, depth);
		}
		return (text != null) ? ResolveValue(text, variables, depth) : string.Empty;
	}

	private static int FindTopLevelComma(string value)
	{
		int num = 0;
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] == '(')
			{
				num++;
			}
			else if (value[i] == ')')
			{
				num--;
			}
			else if (value[i] == ',' && num == 0)
			{
				return i;
			}
		}
		return -1;
	}

	internal static UiStyle ApplyProperty(UiStyle style, string propertyName, string rawValue)
	{
		UiStyleBuilder builder = new UiStyleBuilder();
		builder.CopyFrom(style);
		ApplyProperty(builder, propertyName, rawValue);
		return builder.ToStyle();
	}

	internal static void ApplyProperty(UiStyleBuilder builder, string propertyName, string rawValue)
	{
		string text = rawValue.Trim();
		switch (propertyName.Trim().ToLowerInvariant())
		{
		case "display":
			builder.Display = ParseDisplay(text);
			return;
		case "flex-direction":
			builder.FlexDirection = ParseFlexDirection(text);
			return;
		case "justify-content":
			builder.JustifyContent = ParseJustifyContent(text);
			return;
		case "align-items":
			builder.AlignItems = ParseAlignItems(text);
			return;
		case "align-content":
			builder.AlignContent = ParseAlignContent(text);
			return;
		case "flex-wrap":
			builder.FlexWrap = ParseFlexWrap(text);
			return;
		case "position":
			builder.PositionType = ParsePositionType(text);
			return;
		case "left":
		{
			UiLength length;
			if (TryParseLength(text, out length))
			{
				builder.Left = length;
			}
			return;
		}
		case "top":
		{
			UiLength length2;
			if (TryParseLength(text, out length2))
			{
				builder.Top = length2;
			}
			return;
		}
		case "right":
		{
			UiLength length4;
			if (TryParseLength(text, out length4))
			{
				builder.Right = length4;
			}
			return;
		}
		case "bottom":
		{
			UiLength length10;
			if (TryParseLength(text, out length10))
			{
				builder.Bottom = length10;
			}
			return;
		}
		case "width":
		{
			UiLength length9;
			if (TryParseLength(text, out length9))
			{
				builder.Width = length9;
			}
			return;
		}
		case "height":
		{
			UiLength length6;
			if (TryParseLength(text, out length6))
			{
				builder.Height = length6;
			}
			return;
		}
		case "min-width":
		{
			UiLength length3;
			if (TryParseLength(text, out length3))
			{
				builder.MinWidth = length3;
			}
			return;
		}
		case "min-height":
		{
			UiLength length11;
			if (TryParseLength(text, out length11))
			{
				builder.MinHeight = length11;
			}
			return;
		}
		case "max-width":
		{
			UiLength length8;
			if (TryParseLength(text, out length8))
			{
				builder.MaxWidth = length8;
			}
			return;
		}
		case "max-height":
		{
			UiLength length7;
			if (TryParseLength(text, out length7))
			{
				builder.MaxHeight = length7;
			}
			return;
		}
		case "flex-basis":
		{
			UiLength length5;
			if (TryParseLength(text, out length5))
			{
				builder.FlexBasis = length5;
			}
			return;
		}
		case "flex-grow":
		{
			float parsed;
			if (TryParseFloat(text, out parsed))
			{
				builder.FlexGrow = parsed;
			}
			return;
		}
		case "flex-shrink":
		{
			float parsed2;
			if (TryParseFloat(text, out parsed2))
			{
				builder.FlexShrink = parsed2;
			}
			return;
		}
		case "gap":
		{
			float gap;
			float rowGap;
			float columnGap;
			if (TryParseGap(text, out gap, out rowGap, out columnGap))
			{
				builder.Gap = gap;
				builder.RowGap = rowGap;
				builder.ColumnGap = columnGap;
			}
			return;
		}
		case "row-gap":
		{
			float parsed6;
			if (TryParseFloat(text, out parsed6))
			{
				builder.RowGap = parsed6;
			}
			return;
		}
		case "column-gap":
		{
			float parsed4;
			if (TryParseFloat(text, out parsed4))
			{
				builder.ColumnGap = parsed4;
			}
			return;
		}
		case "margin":
		{
			UiThickness thickness3;
			if (TryParseThickness(text, out thickness3))
			{
				builder.Margin = thickness3;
			}
			return;
		}
		case "padding":
		{
			UiThickness thickness;
			if (TryParseThickness(text, out thickness))
			{
				builder.Padding = thickness;
			}
			return;
		}
		case "border-width":
		{
			float parsed9;
			if (TryParseFloat(text, out parsed9))
			{
				builder.BorderWidth = parsed9;
			}
			return;
		}
		case "border-radius":
		{
			float parsed8;
			if (TryParseFloat(text, out parsed8))
			{
				builder.BorderRadius = parsed8;
			}
			return;
		}
		case "border-style":
			builder.BorderStyle = ParseBorderStyle(text);
			return;
		case "z-index":
		{
			int result;
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
			{
				builder.ZIndex = result;
			}
			return;
		}
		case "background":
		{
			if (TryParseBackgroundLayers(text, out IReadOnlyList<UiBackgroundLayer> layers))
			{
				builder.BackgroundLayers = layers;
			builder.BackgroundGradient = layers.Select((UiBackgroundLayer layer) => layer.Gradient).FirstOrDefault((UiLinearGradient uiLinearGradient) => uiLinearGradient != null);
			builder.BackgroundColor = ((layers.Count == 1 && layers[0].Gradient == null) ? layers[0].Color : builder.BackgroundColor);
			return;
			}
			UiColor color3;
			if (TryParseColor(text, out color3))
			{
				builder.BackgroundColor = color3;
				builder.BackgroundLayers = Array.Empty<UiBackgroundLayer>();
				builder.BackgroundGradient = null;
			}
			return;
		}
		case "background-color":
		{
			UiColor color;
			if (TryParseColor(text, out color))
			{
				builder.BackgroundColor = color;
			}
			return;
		}
		case "background-image":
		{
			IReadOnlyList<UiBackgroundLayer> layers2;
			if (TryParseBackgroundLayers(text, out layers2))
			{
				builder.BackgroundLayers = layers2;
				builder.BackgroundGradient = layers2.Select((UiBackgroundLayer layer) => layer.Gradient).FirstOrDefault((UiLinearGradient uiLinearGradient) => uiLinearGradient != null);
			}
			return;
		}
		case "background-size":
		{
			IReadOnlyList<UiBackgroundSize> sizes;
			if (TryParseBackgroundSizeList(text, out sizes))
			{
				builder.BackgroundSizes = sizes;
			}
			return;
		}
		case "background-position":
		{
			IReadOnlyList<UiBackgroundPosition> positions;
			if (TryParseBackgroundPositionList(text, out positions))
			{
				builder.BackgroundPositions = positions;
			}
			return;
		}
		case "background-repeat":
		{
			IReadOnlyList<UiBackgroundRepeat> repeats;
			if (TryParseBackgroundRepeatList(text, out repeats))
			{
				builder.BackgroundRepeats = repeats;
			}
			return;
		}
		case "border-color":
		{
			UiColor color4;
			if (TryParseColor(text, out color4))
			{
				builder.BorderColor = color4;
			}
			return;
		}
		case "outline":
		{
			float outlineWidth;
			UiColor outlineColor;
			if (TryParseOutline(text, builder.Color, out outlineWidth, out outlineColor))
			{
				builder.OutlineWidth = outlineWidth;
				builder.OutlineColor = outlineColor;
			}
			return;
		}
		case "outline-width":
		{
			float parsed5;
			if (TryParseFloat(text, out parsed5))
			{
				builder.OutlineWidth = parsed5;
			}
			return;
		}
		case "outline-color":
		{
			UiColor color2;
			if (TryParseColor(text, out color2))
			{
				builder.OutlineColor = color2;
			}
			return;
		}
		case "box-shadow":
		{
			IReadOnlyList<UiShadow> shadows;
			if (TryParseShadowList(text, out shadows))
			{
				builder.BoxShadow = shadows[0];
				builder.BoxShadows = shadows;
			}
			return;
		}
		case "filter":
		{
			float blurRadius;
			if (TryParseBlurFunction(text, out blurRadius))
			{
				builder.FilterBlurRadius = blurRadius;
			}
			return;
		}
		case "backdrop-filter":
		{
			float blurRadius2;
			if (TryParseBlurFunction(text, out blurRadius2))
			{
				builder.BackdropBlurRadius = blurRadius2;
			}
			return;
		}
		case "mask":
		case "mask-image":
		{
			UiLinearGradient gradient;
			if (TryParseMaskGradient(text, out gradient))
			{
				builder.MaskGradient = gradient;
			}
			return;
		}
		case "clip-path":
		{
			UiClipPath clipPath;
			if (TryParseClipPath(text, out clipPath))
			{
				builder.ClipPath = clipPath;
			}
			return;
		}
		case "text-shadow":
		{
			UiShadow shadow;
			if (TryParseShadow(text, out shadow))
			{
				builder.TextShadow = shadow;
			}
			return;
		}
		case "color":
		{
			UiColor color5;
			if (TryParseColor(text, out color5))
			{
				builder.Color = color5;
			}
			return;
		}
		case "font-size":
		{
			float parsed7;
			if (TryParseFloat(text, out parsed7))
			{
				builder.FontSize = parsed7;
			}
			return;
		}
		case "font-family":
			builder.FontFamily = ParseFontFamily(text);
			return;
		case "font-weight":
			builder.Bold = IsBold(text);
			return;
		case "white-space":
			builder.WhiteSpace = ParseWhiteSpace(text);
			return;
		case "direction":
			builder.Direction = ParseTextDirection(text);
			return;
		case "text-align":
			builder.TextAlign = ParseTextAlign(text);
			return;
		case "text-decoration":
		case "text-decoration-line":
			builder.TextDecorationLine = ParseTextDecorationLine(text);
			return;
		case "text-overflow":
			builder.TextOverflow = ParseTextOverflow(text);
			return;
		case "object-fit":
			builder.ObjectFit = ParseObjectFit(text);
			return;
		case "image-slice":
		case "nine-slice":
		case "border-image-slice":
		{
			UiThickness thickness2;
			if (TryParseThickness(text, out thickness2))
			{
				builder.ImageSlice = thickness2;
			}
			return;
		}
		case "transform":
		{
			UiTransform transform;
			if (TryParseTransform(text, out transform))
			{
				builder.Transform = (transform ?? UiTransform.Identity);
			}
			return;
		}
		case "animation":
		{
			UiAnimationSpec animation;
			if (TryParseAnimationSpec(text, out animation))
			{
				builder.Animation = animation;
			}
			return;
		}
		case "transition":
		{
			UiTransitionSpec transition;
			if (TryParseTransitionSpec(text, out transition))
			{
				builder.Transition = transition;
			}
			return;
		}
		case "opacity":
		{
			float parsed3;
			if (TryParseFloat(text, out parsed3))
			{
				builder.Opacity = Math.Clamp(parsed3, 0f, 1f);
			}
			return;
		}
		case "visibility":
			builder.Visible = !string.Equals(text, "hidden", StringComparison.OrdinalIgnoreCase);
			return;
		case "pointer-events":
			builder.PointerEvents = ParsePointerEvents(text);
			return;
		case "overflow":
		{
			UiOverflow uiOverflow = ParseOverflow(text);
			builder.Overflow = uiOverflow;
			builder.ClipContent = uiOverflow == UiOverflow.Hidden || uiOverflow == UiOverflow.Clip;
			return;
		}
		case "clip-content":
		case "overflow-clip":
		{
			bool flag = ParseBoolean(text);
			builder.ClipContent = flag;
			builder.Overflow = (flag ? UiOverflow.Clip : builder.Overflow);
			return;
		}
		case "grid-template-columns":
		{
			if (TryParseGridTemplate(text, out IReadOnlyList<UiGridTrack> columns))
			{
				builder.GridTemplateColumns = columns;
			}
			return;
		}
		case "grid-template-rows":
		{
			if (TryParseGridTemplate(text, out IReadOnlyList<UiGridTrack> rows))
			{
				builder.GridTemplateRows = rows;
			}
			return;
		}
		case "grid-auto-flow":
			builder.GridAutoFlow = ParseGridAutoFlow(text);
			return;
		case "grid-column":
		{
			if (TryParseGridPlacement(text, out UiGridPlacement placement))
			{
				builder.GridColumn = placement;
			}
			return;
		}
		case "grid-row":
		{
			if (TryParseGridPlacement(text, out UiGridPlacement placement))
			{
				builder.GridRow = placement;
			}
			return;
		}
		case "box-sizing":
			if (text.Equals("border-box", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (text.Equals("content-box", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("box-sizing: content-box is unsupported; Ludots layout is border-box only.");
			}
			throw new InvalidOperationException("Unsupported box-sizing value '" + text + "'.");
		case "float":
			return;
		default:
			return;
		}
	}

	private static UiDisplay ParseDisplay(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiDisplay result = text switch
		{
			"none" => UiDisplay.None, 
			"block" => UiDisplay.Block, 
			"flex" => UiDisplay.Flex,
			"inline" => UiDisplay.Inline, 
			"text" => UiDisplay.Text,
			"grid" => UiDisplay.Grid,
			_ => UiDisplay.Flex, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiGridAutoFlow ParseGridAutoFlow(string value)
	{
		string text = value.Trim().ToLowerInvariant();
		if (text.Contains("column", StringComparison.Ordinal))
		{
			return UiGridAutoFlow.Column;
		}
		return UiGridAutoFlow.Row;
	}

	public static bool TryParseGridTemplate(string value, out IReadOnlyList<UiGridTrack> tracks)
	{
		tracks = Array.Empty<UiGridTrack>();
		if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (value.Contains("minmax(", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		List<UiGridTrack> list = new List<UiGridTrack>();
		List<string> tokens = ExpandGridTemplateTokens(value);
		for (int i = 0; i < tokens.Count; i++)
		{
			if (!TryParseGridTrack(tokens[i], out UiGridTrack track))
			{
				return false;
			}
			list.Add(track);
		}
		tracks = list;
		return true;
	}

	private static List<string> ExpandGridTemplateTokens(string value)
	{
		List<string> raw = SplitWhitespacePreservingFunctions(value);
		List<string> expanded = new List<string>();
		for (int i = 0; i < raw.Count; i++)
		{
			string token = raw[i];
			if (token.StartsWith("repeat(", StringComparison.OrdinalIgnoreCase) && token.EndsWith(')'))
			{
				if (TryExpandRepeat(token, out List<string> repeated))
				{
					expanded.AddRange(repeated);
				}
				continue;
			}
			expanded.Add(token);
		}
		return expanded;
	}

	private static bool TryExpandRepeat(string token, out List<string> tracks)
	{
		tracks = new List<string>();
		int open = token.IndexOf('(');
		if (open < 0 || !token.EndsWith(')'))
		{
			return false;
		}
		string inner = token.Substring(open + 1, token.Length - open - 2).Trim();
		int comma = inner.IndexOf(',');
		if (comma < 0)
		{
			return false;
		}
		if (!int.TryParse(inner.Substring(0, comma).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 1)
		{
			return false;
		}
		string trackList = inner.Substring(comma + 1).Trim();
		if (trackList.Contains("minmax(", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		List<string> parts = SplitWhitespacePreservingFunctions(trackList);
		for (int i = 0; i < count; i++)
		{
			tracks.AddRange(parts);
		}
		return tracks.Count > 0;
	}

	private static bool TryParseGridTrack(string token, out UiGridTrack track)
	{
		track = UiGridTrack.Auto;
		string text = token.Trim();
		if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			track = UiGridTrack.Auto;
			return true;
		}
		if (text.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
		{
			string number = text.Substring(0, text.Length - 2).Trim();
			if (float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float fr))
			{
				track = UiGridTrack.Fr(fr);
				return true;
			}
			return false;
		}
		if (TryParseLength(text, out UiLength length))
		{
			if (length.Unit == UiLengthUnit.Pixel)
			{
				track = UiGridTrack.Px(length.Value);
				return true;
			}
			if (length.Unit == UiLengthUnit.Percent)
			{
				track = UiGridTrack.Percent(length.Value);
				return true;
			}
		}
		return false;
	}

	public static bool TryParseGridPlacement(string value, out UiGridPlacement placement)
	{
		placement = UiGridPlacement.Auto;
		if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string[] parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 1)
		{
			if (parts[0].StartsWith("span", StringComparison.OrdinalIgnoreCase))
			{
				string spanText = parts[0].Substring(4).Trim();
				if (int.TryParse(spanText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int spanOnly))
				{
					placement = new UiGridPlacement(0, spanOnly);
					return true;
				}
				return false;
			}
			if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int startOnly))
			{
				placement = new UiGridPlacement(startOnly, 1);
				return true;
			}
			return false;
		}
		if (parts.Length != 2)
		{
			return false;
		}
		if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int start))
		{
			return false;
		}
		string endPart = parts[1];
		if (endPart.StartsWith("span", StringComparison.OrdinalIgnoreCase))
		{
			string spanText = endPart.Substring(4).Trim();
			if (!int.TryParse(spanText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int span))
			{
				return false;
			}
			placement = new UiGridPlacement(start, span);
			return true;
		}
		if (!int.TryParse(endPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int end))
		{
			return false;
		}
		placement = new UiGridPlacement(start, Math.Max(1, end - start));
		return true;
	}

	private static UiPointerEvents ParsePointerEvents(string value)
	{
		return value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
			? UiPointerEvents.None
			: UiPointerEvents.Auto;
	}

	private static UiFlexDirection ParseFlexDirection(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiFlexDirection result = ((text == "row") ? UiFlexDirection.Row : UiFlexDirection.Column);
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiJustifyContent ParseJustifyContent(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiJustifyContent result;
		switch (text)
		{
		case "center":
			result = UiJustifyContent.Center;
			break;
		case "end":
		case "flex-end":
			result = UiJustifyContent.End;
			break;
		case "space-between":
			result = UiJustifyContent.SpaceBetween;
			break;
		case "space-around":
			result = UiJustifyContent.SpaceAround;
			break;
		case "space-evenly":
			result = UiJustifyContent.SpaceEvenly;
			break;
		default:
			result = UiJustifyContent.Start;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiAlignItems ParseAlignItems(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiAlignItems result;
		switch (text)
		{
		case "start":
		case "flex-start":
			result = UiAlignItems.Start;
			break;
		case "center":
			result = UiAlignItems.Center;
			break;
		case "end":
		case "flex-end":
			result = UiAlignItems.End;
			break;
		default:
			result = UiAlignItems.Stretch;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiAlignContent ParseAlignContent(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiAlignContent result;
		switch (text)
		{
		case "start":
		case "flex-start":
			result = UiAlignContent.Start;
			break;
		case "center":
			result = UiAlignContent.Center;
			break;
		case "end":
		case "flex-end":
			result = UiAlignContent.End;
			break;
		case "space-between":
			result = UiAlignContent.SpaceBetween;
			break;
		case "space-around":
			result = UiAlignContent.SpaceAround;
			break;
		case "space-evenly":
			result = UiAlignContent.SpaceEvenly;
			break;
		default:
			result = UiAlignContent.Stretch;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiFlexWrap ParseFlexWrap(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiFlexWrap result = ((text == "wrap") ? UiFlexWrap.Wrap : ((text == "wrap-reverse") ? UiFlexWrap.WrapReverse : UiFlexWrap.NoWrap));
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiPositionType ParsePositionType(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiPositionType result = ((text == "absolute") ? UiPositionType.Absolute : UiPositionType.Relative);
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiOverflow ParseOverflow(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiOverflow result = text switch
		{
			"hidden" => UiOverflow.Hidden, 
			"scroll" => UiOverflow.Scroll, 
			"clip" => UiOverflow.Clip, 
			_ => UiOverflow.Visible, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiBorderStyle ParseBorderStyle(string value)
	{
		string text = SplitWhitespacePreservingFunctions(value).FirstOrDefault() ?? string.Empty;
		string text2 = text.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiBorderStyle result = ((text2 == "dashed") ? UiBorderStyle.Dashed : ((text2 == "dotted") ? UiBorderStyle.Dotted : UiBorderStyle.Solid));
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiTextDirection ParseTextDirection(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiTextDirection result = ((text == "rtl") ? UiTextDirection.Rtl : ((text == "auto") ? UiTextDirection.Auto : UiTextDirection.Ltr));
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiTextAlign ParseTextAlign(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiTextAlign result = text switch
		{
			"left" => UiTextAlign.Left, 
			"right" => UiTextAlign.Right, 
			"center" => UiTextAlign.Center, 
			"end" => UiTextAlign.End, 
			_ => UiTextAlign.Start, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static UiObjectFit ParseObjectFit(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiObjectFit result = text switch
		{
			"contain" => UiObjectFit.Contain, 
			"cover" => UiObjectFit.Cover, 
			"none" => UiObjectFit.None, 
			"scale-down" => UiObjectFit.ScaleDown, 
			_ => UiObjectFit.Fill, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool TryParseLength(string value, out UiLength length)
	{
		string text = value.Trim();
		if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			length = UiLength.Auto;
			return true;
		}
		if (text.StartsWith("calc(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(")", StringComparison.Ordinal))
		{
			string inner = text.Substring("calc(".Length, text.Length - "calc(".Length - 1);
			if (UiCalcParser.TryParse(inner, out UiCalcExpression? expression))
			{
				length = UiLength.CalcExpression(expression);
				return true;
			}
			length = UiLength.Auto;
			return false;
		}
		if (text.EndsWith("%", StringComparison.Ordinal))
		{
			string text2 = text;
			if (float.TryParse(text2.Substring(0, text2.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				length = UiLength.Percent(result);
				return true;
			}
		}
		if (TryParseViewportUnit(text, out length))
		{
			return true;
		}
		if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			text = text2.Substring(0, text2.Length - 2);
		}
		if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
		{
			length = UiLength.Px(result2);
			return true;
		}
		length = UiLength.Auto;
		return false;
	}

	private static bool TryParseViewportUnit(string text, out UiLength length)
	{
		length = UiLength.Auto;
		if (text.EndsWith("vmin", StringComparison.OrdinalIgnoreCase) && TryParseUnitValue(text, "vmin", out float vmin))
		{
			length = new UiLength(vmin, UiLengthUnit.Vmin);
			return true;
		}
		if (text.EndsWith("vmax", StringComparison.OrdinalIgnoreCase) && TryParseUnitValue(text, "vmax", out float vmax))
		{
			length = new UiLength(vmax, UiLengthUnit.Vmax);
			return true;
		}
		if (text.EndsWith("vw", StringComparison.OrdinalIgnoreCase) && TryParseUnitValue(text, "vw", out float vw))
		{
			length = new UiLength(vw, UiLengthUnit.Vw);
			return true;
		}
		if (text.EndsWith("vh", StringComparison.OrdinalIgnoreCase) && TryParseUnitValue(text, "vh", out float vh))
		{
			length = new UiLength(vh, UiLengthUnit.Vh);
			return true;
		}
		return false;
	}

	private static bool TryParseUnitValue(string text, string suffix, out float value)
	{
		value = 0f;
		return float.TryParse(text.Substring(0, text.Length - suffix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}

	private static bool TryParseFloat(string value, out float parsed)
	{
		string text = value.Trim();
		if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			text = text2.Substring(0, text2.Length - 2);
		}
		return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
	}

	private static bool TryParseThickness(string value, out UiThickness thickness)
	{
		string[] array = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		float[] array2 = new float[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			if (!TryParseFloat(array[i], out array2[i]))
			{
				thickness = UiThickness.Zero;
				return false;
			}
		}
		int num = array2.Length;
		if (1 == 0)
		{
		}
		UiThickness uiThickness = num switch
		{
			1 => UiThickness.All(array2[0]), 
			2 => new UiThickness(array2[1], array2[0], array2[1], array2[0]), 
			3 => new UiThickness(array2[1], array2[0], array2[1], array2[2]), 
			4 => new UiThickness(array2[3], array2[0], array2[1], array2[2]), 
			_ => UiThickness.Zero, 
		};
		if (1 == 0)
		{
		}
		thickness = uiThickness;
		return array2.Length != 0;
	}

	private static bool TryParseGap(string value, out float gap, out float rowGap, out float columnGap)
	{
		gap = 0f;
		rowGap = 0f;
		columnGap = 0f;
		string[] array = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length == 0 || array.Length > 2)
		{
			return false;
		}
		if (!TryParseFloat(array[0], out rowGap))
		{
			return false;
		}
		if (array.Length == 1)
		{
			gap = rowGap;
			columnGap = rowGap;
			return true;
		}
		if (!TryParseFloat(array[1], out columnGap))
		{
			return false;
		}
		return true;
	}

	private static bool TryParseColor(string value, out UiColor color)
	{
		color = UiColor.Transparent;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if (string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (UiColor.TryParse(value, out color))
		{
			return true;
		}
		if (value.StartsWith("rgba", StringComparison.OrdinalIgnoreCase))
		{
			string[] array = value.Replace("rgba(", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(")", string.Empty, StringComparison.Ordinal).Split(',');
			if (array.Length == 4 && byte.TryParse(array[0].Trim(), out var result) && byte.TryParse(array[1].Trim(), out var result2) && byte.TryParse(array[2].Trim(), out var result3) && float.TryParse(array[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result4))
			{
				byte alpha = ((result4 > 1f) ? ((byte)Math.Clamp(result4, 0f, 255f)) : ((byte)Math.Clamp(result4 * 255f, 0f, 255f)));
				color = new UiColor(result, result2, result3, alpha);
				return true;
			}
		}
		if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
		{
			string[] array2 = value.Replace("rgb(", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(")", string.Empty, StringComparison.Ordinal).Split(',');
			if (array2.Length == 3 && byte.TryParse(array2[0].Trim(), out var result5) && byte.TryParse(array2[1].Trim(), out var result6) && byte.TryParse(array2[2].Trim(), out var result7))
			{
				color = new UiColor(result5, result6, result7, byte.MaxValue);
				return true;
			}
		}
		return false;
	}

	private static UiTextDecorationLine ParseTextDecorationLine(string value)
	{
		UiTextDecorationLine uiTextDecorationLine = UiTextDecorationLine.None;
		foreach (string item in SplitWhitespacePreservingFunctions(value))
		{
			switch (item.ToLowerInvariant())
			{
			case "none":
				return UiTextDecorationLine.None;
			case "underline":
				uiTextDecorationLine |= UiTextDecorationLine.Underline;
				break;
			case "line-through":
				uiTextDecorationLine |= UiTextDecorationLine.LineThrough;
				break;
			}
		}
		return uiTextDecorationLine;
	}

	private static UiTextOverflow ParseTextOverflow(string value)
	{
		return value.Trim().Equals("ellipsis", StringComparison.OrdinalIgnoreCase) ? UiTextOverflow.Ellipsis : UiTextOverflow.Clip;
	}

	private static UiWhiteSpace ParseWhiteSpace(string value)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiWhiteSpace result = ((text == "nowrap") ? UiWhiteSpace.NoWrap : ((text == "pre-wrap") ? UiWhiteSpace.PreWrap : UiWhiteSpace.Normal));
		if (1 == 0)
		{
		}
		return result;
	}

	private static string ParseFontFamily(string value)
	{
		return value.Trim();
	}

	private static bool TryParseOutline(string value, UiColor defaultColor, out float outlineWidth, out UiColor outlineColor)
	{
		outlineWidth = 0f;
		outlineColor = defaultColor;
		foreach (string item in SplitWhitespacePreservingFunctions(value))
		{
			UiColor color;
			if (TryParseFloat(item, out var parsed))
			{
				outlineWidth = parsed;
			}
			else if (TryParseColor(item, out color))
			{
				outlineColor = color;
			}
		}
		return outlineWidth > 0f;
	}

	private static bool TryParseShadow(string value, out UiShadow shadow)
	{
		shadow = default(UiShadow);
		string value2 = value.Trim();
		if (string.IsNullOrWhiteSpace(value2))
		{
			return false;
		}
		float[] array = new float[4];
		int num = 0;
		UiColor color = new UiColor(0, 0, 0, 160);
		foreach (string item in SplitWhitespacePreservingFunctions(value2))
		{
			if (!item.Equals("inset", StringComparison.OrdinalIgnoreCase))
			{
				float parsed;
				if (TryParseColor(item, out var color2))
				{
					color = color2;
				}
				else if (num < array.Length && TryParseFloat(item, out parsed))
				{
					array[num++] = parsed;
				}
			}
		}
		if (num < 2)
		{
			return false;
		}
		shadow = new UiShadow(array[0], array[1], (num >= 3) ? Math.Max(0f, array[2]) : 0f, (num >= 4) ? array[3] : 0f, color);
		return true;
	}

	private static bool TryParseShadowList(string value, out IReadOnlyList<UiShadow> shadows)
	{
		List<UiShadow> list = new List<UiShadow>();
		foreach (string item in SplitTopLevel(value, ','))
		{
			if (!TryParseShadow(item, out var shadow))
			{
				shadows = Array.Empty<UiShadow>();
				return false;
			}
			list.Add(shadow);
		}
		shadows = list;
		return list.Count > 0;
	}

	private static bool TryParseBackgroundLayers(string value, out IReadOnlyList<UiBackgroundLayer> layers)
	{
		List<UiBackgroundLayer> list = new List<UiBackgroundLayer>();
		foreach (string item in SplitTopLevel(value, ','))
		{
			if (TryParseBackgroundImageSource(item, out string imageSource) && imageSource != null)
			{
				list.Add(UiBackgroundLayer.FromImage(imageSource));
				continue;
			}
			if (TryParseLinearGradient(item, out UiLinearGradient gradient) && gradient != null)
			{
				list.Add(UiBackgroundLayer.FromGradient(gradient));
				continue;
			}
			if (TryParseColor(item, out var color))
			{
				list.Add(UiBackgroundLayer.FromColor(color));
				continue;
			}
			layers = Array.Empty<UiBackgroundLayer>();
			return false;
		}
		layers = list;
		return list.Count > 0;
	}

	private static bool TryParseBackgroundImageSource(string value, out string? imageSource)
	{
		imageSource = null;
		string text = value.Trim();
		if (!text.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
		{
			return false;
		}
		string text2 = text;
		string text3 = text2.Substring(4, text2.Length - 1 - 4).Trim();
		if ((text3.StartsWith('"') && text3.EndsWith('"')) || (text3.StartsWith('\'') && text3.EndsWith('\'')))
		{
			text2 = text3;
			text3 = text2.Substring(1, text2.Length - 1 - 1);
		}
		if (string.IsNullOrWhiteSpace(text3))
		{
			return false;
		}
		imageSource = text3;
		return true;
	}

	private static bool TryParseBackgroundSizeList(string value, out IReadOnlyList<UiBackgroundSize> sizes)
	{
		List<UiBackgroundSize> list = new List<UiBackgroundSize>();
		foreach (string item in SplitTopLevel(value, ','))
		{
			if (!TryParseBackgroundSize(item, out var size))
			{
				sizes = Array.Empty<UiBackgroundSize>();
				return false;
			}
			list.Add(size);
		}
		sizes = list;
		return list.Count > 0;
	}

	private static bool TryParseBackgroundPositionList(string value, out IReadOnlyList<UiBackgroundPosition> positions)
	{
		List<UiBackgroundPosition> list = new List<UiBackgroundPosition>();
		foreach (string item in SplitTopLevel(value, ','))
		{
			if (!TryParseBackgroundPosition(item, out var position))
			{
				positions = Array.Empty<UiBackgroundPosition>();
				return false;
			}
			list.Add(position);
		}
		positions = list;
		return list.Count > 0;
	}

	private static bool TryParseBackgroundRepeatList(string value, out IReadOnlyList<UiBackgroundRepeat> repeats)
	{
		List<UiBackgroundRepeat> list = new List<UiBackgroundRepeat>();
		foreach (string item in SplitTopLevel(value, ','))
		{
			if (!TryParseBackgroundRepeat(item, out var repeat))
			{
				repeats = Array.Empty<UiBackgroundRepeat>();
				return false;
			}
			list.Add(repeat);
		}
		repeats = list;
		return list.Count > 0;
	}

	private static bool TryParseBackgroundSize(string value, out UiBackgroundSize size)
	{
		size = UiBackgroundSize.Auto;
		List<string> list = SplitWhitespacePreservingFunctions(value);
		if (list.Count == 0)
		{
			return false;
		}
		if (list.Count == 1)
		{
			switch (list[0].Trim().ToLowerInvariant())
			{
			case "auto":
				size = UiBackgroundSize.Auto;
				return true;
			case "cover":
				size = UiBackgroundSize.Cover;
				return true;
			case "contain":
				size = UiBackgroundSize.Contain;
				return true;
			default:
			{
				if (TryParseLength(list[0], out var length))
				{
					size = UiBackgroundSize.Explicit(length, UiLength.Auto);
					return true;
				}
				return false;
			}
			}
		}
		if (list.Count > 2)
		{
			return false;
		}
		if (!TryParseLength(list[0], out var length2) || !TryParseLength(list[1], out var length3))
		{
			return false;
		}
		size = ((length2.IsAuto && length3.IsAuto) ? UiBackgroundSize.Auto : UiBackgroundSize.Explicit(length2, length3));
		return true;
	}

	private static bool TryParseBackgroundPosition(string value, out UiBackgroundPosition position)
	{
		position = UiBackgroundPosition.TopLeft;
		List<string> list = SplitWhitespacePreservingFunctions(value);
		if (list.Count == 0)
		{
			return false;
		}
		if (list.Count == 1)
		{
			string text = list[0].Trim().ToLowerInvariant();
			if (text == "center")
			{
				position = UiBackgroundPosition.Center;
				return true;
			}
			if (TryParseBackgroundPositionComponent(text, horizontal: true, out var length))
			{
				position = new UiBackgroundPosition(length, UiLength.Percent(50f));
				return true;
			}
			if (TryParseBackgroundPositionComponent(text, horizontal: false, out var length2))
			{
				position = new UiBackgroundPosition(UiLength.Percent(50f), length2);
				return true;
			}
			return false;
		}
		string token = list[0].Trim().ToLowerInvariant();
		string token2 = list[1].Trim().ToLowerInvariant();
		if (TryParseBackgroundPositionComponent(token, horizontal: true, out var length3) && TryParseBackgroundPositionComponent(token2, horizontal: false, out var length4))
		{
			position = new UiBackgroundPosition(length3, length4);
			return true;
		}
		if (TryParseBackgroundPositionComponent(token, horizontal: false, out length4) && TryParseBackgroundPositionComponent(token2, horizontal: true, out length3))
		{
			position = new UiBackgroundPosition(length3, length4);
			return true;
		}
		return false;
	}

	private static bool TryParseBackgroundRepeat(string value, out UiBackgroundRepeat repeat)
	{
		repeat = UiBackgroundRepeat.Repeat;
		switch (value.Trim().ToLowerInvariant())
		{
		case "repeat":
			repeat = UiBackgroundRepeat.Repeat;
			return true;
		case "no-repeat":
			repeat = UiBackgroundRepeat.NoRepeat;
			return true;
		case "repeat-x":
		case "repeat no-repeat":
			repeat = UiBackgroundRepeat.RepeatX;
			return true;
		case "repeat-y":
		case "no-repeat repeat":
			repeat = UiBackgroundRepeat.RepeatY;
			return true;
		default:
			return false;
		}
	}

	private static bool TryParseBackgroundPositionComponent(string token, bool horizontal, out UiLength length)
	{
		if (TryParseLength(token, out length))
		{
			return true;
		}
		string text = token.Trim().ToLowerInvariant();
		bool result;
		if (horizontal)
		{
			if (1 == 0)
			{
			}
			result = text switch
			{
				"left" => Assign(UiLength.Percent(0f), out length), 
				"center" => Assign(UiLength.Percent(50f), out length), 
				"right" => Assign(UiLength.Percent(100f), out length), 
				_ => false, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
		if (1 == 0)
		{
		}
		result = text switch
		{
			"top" => Assign(UiLength.Percent(0f), out length), 
			"center" => Assign(UiLength.Percent(50f), out length), 
			"bottom" => Assign(UiLength.Percent(100f), out length), 
			_ => false, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool Assign(UiLength value, out UiLength target)
	{
		target = value;
		return true;
	}

	private static bool TryParseBlurFunction(string value, out float blurRadius)
	{
		blurRadius = 0f;
		string text = value.Trim();
		if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (!text.StartsWith("blur(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
		{
			return false;
		}
		string text2 = text;
		return TryParseFloat(text2.Substring(5, text2.Length - 1 - 5), out blurRadius);
	}

	private static bool TryParseLinearGradient(string value, out UiLinearGradient? gradient)
	{
		gradient = null;
		string text = value.Trim();
		if (!text.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
		{
			return false;
		}
		string text2 = text;
		int length = "linear-gradient(".Length;
		string value2 = text2.Substring(length, text2.Length - 1 - length);
		List<string> list = SplitTopLevel(value2, ',');
		if (list.Count < 2)
		{
			return false;
		}
		float angleDegrees = 90f;
		int num = 0;
		if (TryParseGradientAngle(list[0], out var angle))
		{
			angleDegrees = angle;
			num = 1;
		}
		List<UiGradientStop> list2 = new List<UiGradientStop>();
		List<int> list3 = new List<int>();
		for (int i = num; i < list.Count; i++)
		{
			if (!TryParseGradientStop(list[i], out var stop, out var hasExplicitPosition))
			{
				return false;
			}
			if (!hasExplicitPosition)
			{
				list3.Add(list2.Count);
			}
			list2.Add(stop);
		}
		if (list2.Count < 2)
		{
			return false;
		}
		if (list3.Count > 0)
		{
			for (int j = 0; j < list2.Count; j++)
			{
				if (list3.Contains(j))
				{
					float position = ((list2.Count == 1) ? 0f : ((float)j / (float)(list2.Count - 1)));
					list2[j] = list2[j]with
					{
						Position = position
					};
				}
			}
		}
		gradient = new UiLinearGradient(angleDegrees, list2);
		return true;
	}

	private static bool TryParseGradientAngle(string value, out float angle)
	{
		string text = value.Trim().ToLowerInvariant();
		if (TryParseAngle(text, out angle))
		{
			return true;
		}
		if (1 == 0)
		{
		}
		float num = text switch
		{
			"to right" => 0f, 
			"to bottom right" => 45f, 
			"to bottom" => 90f, 
			"to bottom left" => 135f, 
			"to left" => 180f, 
			"to top left" => 225f, 
			"to top" => 270f, 
			"to top right" => 315f, 
			_ => 0f, 
		};
		if (1 == 0)
		{
		}
		angle = num;
		return text.StartsWith("to ", StringComparison.Ordinal);
	}

	private static bool TryParseGradientStop(string value, out UiGradientStop stop, out bool hasExplicitPosition)
	{
		stop = default(UiGradientStop);
		hasExplicitPosition = false;
		List<string> list = SplitWhitespacePreservingFunctions(value);
		if (list.Count == 0)
		{
			return false;
		}
		float position = 0f;
		string value2 = value.Trim();
		string text = list[list.Count - 1];
		if (text.EndsWith("%", StringComparison.Ordinal))
		{
			string text2 = text;
			if (float.TryParse(text2.Substring(0, text2.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				hasExplicitPosition = true;
				position = Math.Clamp(result / 100f, 0f, 1f);
				value2 = string.Join(' ', list.Take(list.Count - 1));
			}
		}
		if (!TryParseColor(value2, out var color))
		{
			return false;
		}
		stop = new UiGradientStop(position, color);
		return true;
	}

	internal static bool TryParseTransform(string value, out UiTransform? transform)
	{
		transform = null;
		string text = value.Trim();
		if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
		{
			transform = UiTransform.Identity;
			return true;
		}
		List<string> list = SplitWhitespacePreservingFunctions(text);
		if (list.Count == 0)
		{
			return false;
		}
		List<UiTransformOperation> list2 = new List<UiTransformOperation>(list.Count);
		for (int i = 0; i < list.Count; i++)
		{
			if (!TryParseTransformOperation(list[i], out var operation))
			{
				transform = null;
				return false;
			}
			list2.Add(operation);
		}
		transform = ((list2.Count == 0) ? UiTransform.Identity : new UiTransform(list2));
		return true;
	}

	private static bool TryParseTransformOperation(string token, out UiTransformOperation operation)
	{
		operation = default(UiTransformOperation);
		int num = token.IndexOf('(');
		if (num <= 0 || !token.EndsWith(')'))
		{
			return false;
		}
		string text = token.Substring(0, num).Trim().ToLowerInvariant();
		int num2 = num + 1;
		string value = token.Substring(num2, token.Length - 1 - num2).Trim();
		List<string> list = SplitTransformArguments(value);
		switch (text)
		{
		case "translate":
		{
			num2 = list.Count;
			bool flag = ((num2 < 1 || num2 > 2) ? true : false);
			if (flag || !TryParseTransformLength(list[0], out var length3))
			{
				return false;
			}
			UiLength length4 = UiLength.Px(0f);
			if (list.Count == 2 && !TryParseTransformLength(list[1], out length4))
			{
				return false;
			}
			operation = UiTransformOperation.Translate(length3, length4);
			return true;
		}
		case "translatex":
		{
			if (!TryParseTransformLength(value, out var length))
			{
				return false;
			}
			operation = UiTransformOperation.Translate(length, UiLength.Px(0f));
			return true;
		}
		case "translatey":
		{
			if (!TryParseTransformLength(value, out var length2))
			{
				return false;
			}
			operation = UiTransformOperation.Translate(UiLength.Px(0f), length2);
			return true;
		}
		case "scale":
		{
			num2 = list.Count;
			bool flag = ((num2 < 1 || num2 > 2) ? true : false);
			if (flag || !TryParseFloat(list[0], out var parsed2))
			{
				return false;
			}
			float parsed3 = parsed2;
			if (list.Count == 2 && !TryParseFloat(list[1], out parsed3))
			{
				return false;
			}
			operation = UiTransformOperation.Scale(parsed2, parsed3);
			return true;
		}
		case "scalex":
		{
			if (!TryParseFloat(value, out var parsed4))
			{
				return false;
			}
			operation = UiTransformOperation.Scale(parsed4, 1f);
			return true;
		}
		case "scaley":
		{
			if (!TryParseFloat(value, out var parsed))
			{
				return false;
			}
			operation = UiTransformOperation.Scale(1f, parsed);
			return true;
		}
		case "rotate":
		{
			if (!TryParseAngle(value, out var angle))
			{
				return false;
			}
			operation = UiTransformOperation.Rotate(angle);
			return true;
		}
		default:
			return false;
		}
	}

	private static List<string> SplitTransformArguments(string value)
	{
		List<string> list = SplitTopLevel(value, ',');
		if (list.Count > 1)
		{
			return list;
		}
		return SplitWhitespacePreservingFunctions(value);
	}

	private static bool TryParseTransformLength(string value, out UiLength length)
	{
		if (TryParseLength(value, out length))
		{
			return true;
		}
		if (TryParseFloat(value, out var parsed))
		{
			length = UiLength.Px(parsed);
			return true;
		}
		length = UiLength.Auto;
		return false;
	}

	private static bool TryParseMaskGradient(string value, out UiLinearGradient? gradient)
	{
		string text = value.Trim();
		if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			gradient = null;
			return true;
		}
		return TryParseLinearGradient(text, out gradient);
	}

	private static bool TryParseClipPath(string value, out UiClipPath? clipPath)
	{
		clipPath = null;
		string text = value.Trim();
		if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (text.StartsWith("inset(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
		{
			string text2 = text;
			int length = "inset(".Length;
			string value2 = text2.Substring(length, text2.Length - 1 - length);
			if (!TryParseThickness(value2, out var thickness))
			{
				return false;
			}
			clipPath = UiClipPath.InsetShape(thickness);
			return true;
		}
		if (text.StartsWith("circle(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
		{
			string text2 = text;
			int length = "circle(".Length;
			string text3 = text2.Substring(length, text2.Length - 1 - length).Trim();
			string[] array = text3.Split(new string[1] { " at " }, StringSplitOptions.None);
			if (!TryParseLength(array[0].Trim(), out var length2))
			{
				return false;
			}
			UiLength length3 = UiLength.Percent(50f);
			UiLength length4 = UiLength.Percent(50f);
			if (array.Length > 1)
			{
				List<string> list = SplitWhitespacePreservingFunctions(array[1]);
				if (list.Count != 2 || !TryParseLength(list[0], out length3) || !TryParseLength(list[1], out length4))
				{
					return false;
				}
			}
			clipPath = UiClipPath.Circle(length2, length3, length4);
			return true;
		}
		return false;
	}

	private static bool TryParseAngle(string value, out float angle)
	{
		string text = value.Trim().ToLowerInvariant();
		if (text.EndsWith("deg", StringComparison.Ordinal))
		{
			string text2 = text;
			if (TryParseFloat(text2.Substring(0, text2.Length - 3), out angle))
			{
				return true;
			}
		}
		if (text.EndsWith("rad", StringComparison.Ordinal))
		{
			string text2 = text;
			if (TryParseFloat(text2.Substring(0, text2.Length - 3), out var parsed))
			{
				angle = parsed * (180f / (float)Math.PI);
				return true;
			}
		}
		if (text.EndsWith("turn", StringComparison.Ordinal))
		{
			string text2 = text;
			if (TryParseFloat(text2.Substring(0, text2.Length - 4), out var parsed2))
			{
				angle = parsed2 * 360f;
				return true;
			}
		}
		return TryParseFloat(text, out angle);
	}

	private static bool TryParseAnimationSpec(string value, out UiAnimationSpec? animation)
	{
		animation = null;
		if (string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		List<string> list = SplitTopLevel(value, ',');
		if (list.Count == 0)
		{
			return false;
		}
		List<UiAnimationEntry> list2 = new List<UiAnimationEntry>(list.Count);
		for (int i = 0; i < list.Count; i++)
		{
			if (!TryParseAnimationEntry(list[i], out UiAnimationEntry entry))
			{
				return false;
			}
			list2.Add(entry);
		}
		animation = ((list2.Count == 0) ? null : new UiAnimationSpec(list2));
		return true;
	}

	private static bool TryParseAnimationEntry(string value, out UiAnimationEntry entry)
	{
		entry = null;
		List<string> list = SplitWhitespacePreservingFunctions(value);
		if (list.Count == 0)
		{
			return false;
		}
		string text = null;
		float num = 0f;
		float delaySeconds = 0f;
		UiTransitionEasing easing = UiTransitionEasing.Ease;
		float iterationCount = 1f;
		UiAnimationDirection direction = UiAnimationDirection.Normal;
		UiAnimationFillMode fillMode = UiAnimationFillMode.None;
		UiAnimationPlayState playState = UiAnimationPlayState.Running;
		bool flag = false;
		for (int i = 0; i < list.Count; i++)
		{
			string text2 = list[i].Trim();
			UiTransitionEasing easing2;
			float iterationCount2;
			UiAnimationDirection direction2;
			UiAnimationFillMode fillMode2;
			UiAnimationPlayState playState2;
			if (TryParseTime(text2, out var seconds))
			{
				if (!flag)
				{
					num = seconds;
					flag = true;
				}
				else
				{
					delaySeconds = seconds;
				}
			}
			else if (TryParseTransitionEasing(text2, out easing2))
			{
				easing = easing2;
			}
			else if (TryParseAnimationIterationCount(text2, out iterationCount2))
			{
				iterationCount = iterationCount2;
			}
			else if (TryParseAnimationDirection(text2, out direction2))
			{
				direction = direction2;
			}
			else if (TryParseAnimationFillMode(text2, out fillMode2))
			{
				fillMode = fillMode2;
			}
			else if (TryParseAnimationPlayState(text2, out playState2))
			{
				playState = playState2;
			}
			else
			{
				text = text2;
			}
		}
		if (string.IsNullOrWhiteSpace(text) || num <= 0f)
		{
			return false;
		}
		entry = new UiAnimationEntry(text, num, delaySeconds, easing, iterationCount, direction, fillMode, playState);
		return true;
	}

	private static bool TryParseAnimationIterationCount(string value, out float iterationCount)
	{
		if (string.Equals(value, "infinite", StringComparison.OrdinalIgnoreCase))
		{
			iterationCount = float.PositiveInfinity;
			return true;
		}
		if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && result >= 0f)
		{
			iterationCount = result;
			return true;
		}
		iterationCount = 1f;
		return false;
	}

	private static bool TryParseAnimationDirection(string value, out UiAnimationDirection direction)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiAnimationDirection uiAnimationDirection = text switch
		{
			"normal" => UiAnimationDirection.Normal, 
			"reverse" => UiAnimationDirection.Reverse, 
			"alternate" => UiAnimationDirection.Alternate, 
			"alternate-reverse" => UiAnimationDirection.AlternateReverse, 
			_ => UiAnimationDirection.Normal, 
		};
		if (1 == 0)
		{
		}
		direction = uiAnimationDirection;
		return value.Equals("normal", StringComparison.OrdinalIgnoreCase) || value.Equals("reverse", StringComparison.OrdinalIgnoreCase) || value.Equals("alternate", StringComparison.OrdinalIgnoreCase) || value.Equals("alternate-reverse", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryParseAnimationFillMode(string value, out UiAnimationFillMode fillMode)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiAnimationFillMode uiAnimationFillMode = text switch
		{
			"none" => UiAnimationFillMode.None, 
			"forwards" => UiAnimationFillMode.Forwards, 
			"backwards" => UiAnimationFillMode.Backwards, 
			"both" => UiAnimationFillMode.Both, 
			_ => UiAnimationFillMode.None, 
		};
		if (1 == 0)
		{
		}
		fillMode = uiAnimationFillMode;
		return value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("forwards", StringComparison.OrdinalIgnoreCase) || value.Equals("backwards", StringComparison.OrdinalIgnoreCase) || value.Equals("both", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryParseAnimationPlayState(string value, out UiAnimationPlayState playState)
	{
		playState = (value.Equals("paused", StringComparison.OrdinalIgnoreCase) ? UiAnimationPlayState.Paused : UiAnimationPlayState.Running);
		return value.Equals("running", StringComparison.OrdinalIgnoreCase) || value.Equals("paused", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryParseTransitionSpec(string value, out UiTransitionSpec? transition)
	{
		transition = null;
		List<string> list = SplitTopLevel(value, ',');
		if (list.Count == 0)
		{
			return false;
		}
		List<UiTransitionEntry> list2 = new List<UiTransitionEntry>(list.Count);
		for (int i = 0; i < list.Count; i++)
		{
			if (!TryParseTransitionEntry(list[i], out UiTransitionEntry entry))
			{
				return false;
			}
			list2.Add(entry);
		}
		transition = new UiTransitionSpec(list2);
		return true;
	}

	private static bool TryParseTransitionEntry(string value, out UiTransitionEntry entry)
	{
		entry = null;
		List<string> list = SplitWhitespacePreservingFunctions(value);
		if (list.Count == 0)
		{
			return false;
		}
		string propertyName = "all";
		float num = 0f;
		float delaySeconds = 0f;
		UiTransitionEasing easing = UiTransitionEasing.Ease;
		bool flag = false;
		for (int i = 0; i < list.Count; i++)
		{
			string value2 = list[i].Trim();
			UiTransitionEasing easing2;
			if (TryParseTime(value2, out var seconds))
			{
				if (!flag)
				{
					num = seconds;
					flag = true;
				}
				else
				{
					delaySeconds = seconds;
				}
			}
			else if (TryParseTransitionEasing(value2, out easing2))
			{
				easing = easing2;
			}
			else
			{
				propertyName = NormalizeTransitionPropertyName(value2);
			}
		}
		entry = new UiTransitionEntry(propertyName, num, delaySeconds, easing);
		return num > 0f;
	}

	private static bool TryParseTime(string value, out float seconds)
	{
		seconds = 0f;
		string text = value.Trim();
		if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			if (float.TryParse(text2.Substring(0, text2.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				seconds = result / 1000f;
				return true;
			}
		}
		if (text.EndsWith('s'))
		{
			string text2 = text;
			if (float.TryParse(text2.Substring(0, text2.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
			{
				seconds = result2;
				return true;
			}
		}
		return false;
	}

	private static bool TryParseTransitionEasing(string value, out UiTransitionEasing easing)
	{
		string text = value.ToLowerInvariant();
		if (1 == 0)
		{
		}
		UiTransitionEasing uiTransitionEasing = text switch
		{
			"linear" => UiTransitionEasing.Linear, 
			"ease-in" => UiTransitionEasing.EaseIn, 
			"ease-out" => UiTransitionEasing.EaseOut, 
			"ease-in-out" => UiTransitionEasing.EaseInOut, 
			"ease" => UiTransitionEasing.Ease, 
			_ => UiTransitionEasing.Linear, 
		};
		if (1 == 0)
		{
		}
		easing = uiTransitionEasing;
		return value.Equals("linear", StringComparison.OrdinalIgnoreCase) || value.Equals("ease", StringComparison.OrdinalIgnoreCase) || value.Equals("ease-in", StringComparison.OrdinalIgnoreCase) || value.Equals("ease-out", StringComparison.OrdinalIgnoreCase) || value.Equals("ease-in-out", StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeTransitionPropertyName(string value)
	{
		string text = value.Trim().ToLowerInvariant();
		if (1 == 0)
		{
		}
		string result = text switch
		{
			"background" => "background-color", 
			"filter-blur" => "filter", 
			"backdrop-blur" => "backdrop-filter", 
			_ => value.Trim().ToLowerInvariant(), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static List<string> SplitTopLevel(string value, char separator)
	{
		List<string> list = new List<string>();
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			switch (c)
			{
			case '(':
				num++;
				continue;
			case ')':
				num--;
				continue;
			}
			if (c == separator && num == 0)
			{
				int num3 = num2;
				list.Add(value.Substring(num3, i - num3).Trim());
				num2 = i + 1;
			}
		}
		if (num2 <= value.Length)
		{
			int num3 = num2;
			list.Add(value.Substring(num3, value.Length - num3).Trim());
		}
		return list.Where((string part) => !string.IsNullOrWhiteSpace(part)).ToList();
	}

	private static List<string> SplitWhitespacePreservingFunctions(string value)
	{
		List<string> list = new List<string>();
		int num = 0;
		int num2 = -1;
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (char.IsWhiteSpace(c) && num == 0)
			{
				if (num2 >= 0)
				{
					int num3 = num2;
					list.Add(value.Substring(num3, i - num3).Trim());
					num2 = -1;
				}
				continue;
			}
			if (num2 < 0)
			{
				num2 = i;
			}
			switch (c)
			{
			case '(':
				num++;
				break;
			case ')':
				num--;
				break;
			}
		}
		if (num2 >= 0)
		{
			int num3 = num2;
			list.Add(value.Substring(num3, value.Length - num3).Trim());
		}
		return list.Where((string token) => !string.IsNullOrWhiteSpace(token)).ToList();
	}

	private static bool IsBold(string value)
	{
		if (string.Equals(value, "bold", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		int result;
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= 600;
	}

	private static bool ParseBoolean(string value)
	{
		return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
	}
}
