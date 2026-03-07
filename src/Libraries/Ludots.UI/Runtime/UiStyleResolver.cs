using System.Globalization;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public sealed class UiStyleResolver
{
    public void ResolveTree(UiNode root, IReadOnlyList<UiStyleSheet>? styleSheets)
    {
        ArgumentNullException.ThrowIfNull(root);
        ResolveNode(root, styleSheets ?? Array.Empty<UiStyleSheet>(), parentStyle: null, inheritedVariables: null, isRoot: true);
    }

    private void ResolveNode(
        UiNode node,
        IReadOnlyList<UiStyleSheet> styleSheets,
        UiStyle? parentStyle,
        IReadOnlyDictionary<string, string>? inheritedVariables,
        bool isRoot)
    {
        if (isRoot)
        {
            node.AddPseudoState(UiPseudoState.Root);
        }
        else
        {
            node.RemovePseudoState(UiPseudoState.Root);
        }

        List<(int Specificity, int Order, UiStyleDeclaration Declaration)> declarations = new();
        int sequence = 0;

        for (int sheetIndex = 0; sheetIndex < styleSheets.Count; sheetIndex++)
        {
            foreach (UiStyleRule rule in styleSheets[sheetIndex].Rules)
            {
                if (UiSelectorMatcher.Matches(node, rule.Selector))
                {
                    declarations.Add((rule.Selector.Specificity, (sheetIndex * 10_000) + rule.Order + sequence++, rule.Declaration));
                }
            }
        }

        declarations.Sort(static (left, right) =>
        {
            int specificity = left.Specificity.CompareTo(right.Specificity);
            return specificity != 0 ? specificity : left.Order.CompareTo(right.Order);
        });

        UiStyleDeclaration mergedDeclaration = new();
        for (int i = 0; i < declarations.Count; i++)
        {
            mergedDeclaration.Merge(declarations[i].Declaration);
        }

        mergedDeclaration.Merge(node.InlineStyle);

        Dictionary<string, string> variables = inheritedVariables != null
            ? new Dictionary<string, string>(inheritedVariables, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ApplyCustomProperties(mergedDeclaration, variables);

        UiStyle resolved = node.LocalStyle;
        resolved = ApplyDeclaration(resolved, mergedDeclaration, variables);
        resolved = ApplyInheritance(node, resolved, parentStyle, mergedDeclaration);

        if (node.Kind == UiNodeKind.Text && resolved.Display == UiDisplay.Flex)
        {
            resolved = resolved with { Display = UiDisplay.Text };
        }

        node.SetComputedStyle(resolved);
        foreach (UiNode child in node.Children)
        {
            ResolveNode(child, styleSheets, resolved, variables, isRoot: false);
        }
    }

    private static void ApplyCustomProperties(UiStyleDeclaration declaration, IDictionary<string, string> variables)
    {
        foreach (KeyValuePair<string, string> entry in declaration)
        {
            if (!entry.Key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            variables[entry.Key] = ResolveValue(entry.Value, (IReadOnlyDictionary<string, string>)variables);
        }
    }

    private static UiStyle ApplyInheritance(UiNode node, UiStyle style, UiStyle? parentStyle, UiStyleDeclaration declaration)
    {
        if (parentStyle == null)
        {
            return style;
        }

        UiStyle implicitStyle = GetImplicitStyle(node.Kind);

        if (!HasExplicitValue(declaration, "color") && style.Color == implicitStyle.Color)
        {
            style = style with { Color = parentStyle.Color };
        }

        if (!HasExplicitValue(declaration, "font-size") && Math.Abs(style.FontSize - implicitStyle.FontSize) < 0.01f)
        {
            style = style with { FontSize = parentStyle.FontSize };
        }

        if (!HasExplicitValue(declaration, "font-weight") && style.Bold == implicitStyle.Bold)
        {
            style = style with { Bold = parentStyle.Bold };
        }

        return style;
    }

    private static bool HasExplicitValue(UiStyleDeclaration declaration, string propertyName)
    {
        return declaration[propertyName] != null;
    }

    private static UiStyle GetImplicitStyle(UiNodeKind kind)
    {
        return kind switch
        {
            UiNodeKind.Row => UiStyle.Default with { Display = UiDisplay.Flex, FlexDirection = UiFlexDirection.Row, AlignItems = UiAlignItems.Center },
            UiNodeKind.Column => UiStyle.Default with { Display = UiDisplay.Flex, FlexDirection = UiFlexDirection.Column },
            UiNodeKind.Text => UiStyle.Default with { Display = UiDisplay.Text, Color = SKColors.White },
            UiNodeKind.Button => UiStyle.Default with
            {
                Display = UiDisplay.Flex,
                FlexDirection = UiFlexDirection.Row,
                AlignItems = UiAlignItems.Center,
                JustifyContent = UiJustifyContent.Center,
                Padding = UiThickness.Symmetric(16f, 10f),
                BackgroundColor = new SKColor(58, 121, 220),
                BorderRadius = 10f,
                Color = SKColors.White
            },
            UiNodeKind.Card => UiStyle.Default with
            {
                Display = UiDisplay.Flex,
                FlexDirection = UiFlexDirection.Column,
                Padding = UiThickness.All(16f),
                BackgroundColor = new SKColor(25, 31, 48),
                BorderRadius = 12f
            },
            UiNodeKind.Panel => UiStyle.Default with { Display = UiDisplay.Flex, FlexDirection = UiFlexDirection.Column },
            _ => UiStyle.Default
        };
    }

    private static UiStyle ApplyDeclaration(UiStyle style, UiStyleDeclaration declaration, IReadOnlyDictionary<string, string> variables)
    {
        foreach (KeyValuePair<string, string> entry in declaration)
        {
            if (entry.Key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string resolvedValue = ResolveValue(entry.Value, variables);
            style = ApplyProperty(style, entry.Key, resolvedValue);
        }

        return style;
    }

    private static string ResolveValue(string rawValue, IReadOnlyDictionary<string, string> variables, int depth = 0)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || depth > 8 || !rawValue.Contains("var(", StringComparison.OrdinalIgnoreCase))
        {
            return rawValue.Trim();
        }

        string result = rawValue;
        int startIndex = result.IndexOf("var(", StringComparison.OrdinalIgnoreCase);
        while (startIndex >= 0)
        {
            int endIndex = FindVarEnd(result, startIndex + 4);
            if (endIndex < 0)
            {
                break;
            }

            string expression = result.Substring(startIndex + 4, endIndex - (startIndex + 4));
            string replacement = ResolveVariableExpression(expression, variables, depth + 1);
            result = result.Substring(0, startIndex) + replacement + result[(endIndex + 1)..];
            startIndex = result.IndexOf("var(", StringComparison.OrdinalIgnoreCase);
        }

        return result.Trim();
    }

    private static int FindVarEnd(string value, int searchStart)
    {
        int depth = 1;
        for (int i = searchStart; i < value.Length; i++)
        {
            if (value[i] == '(')
            {
                depth++;
            }
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string ResolveVariableExpression(string expression, IReadOnlyDictionary<string, string> variables, int depth)
    {
        int commaIndex = FindTopLevelComma(expression);
        string variableName = commaIndex >= 0 ? expression[..commaIndex].Trim() : expression.Trim();
        string? fallback = commaIndex >= 0 ? expression[(commaIndex + 1)..].Trim() : null;

        if (variables.TryGetValue(variableName, out string? variableValue))
        {
            return ResolveValue(variableValue, variables, depth);
        }

        return fallback != null ? ResolveValue(fallback, variables, depth) : string.Empty;
    }

    private static int FindTopLevelComma(string value)
    {
        int depth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(')
            {
                depth++;
            }
            else if (value[i] == ')')
            {
                depth--;
            }
            else if (value[i] == ',' && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static UiStyle ApplyProperty(UiStyle style, string propertyName, string rawValue)
    {
        string value = rawValue.Trim();
        switch (propertyName.Trim().ToLowerInvariant())
        {
            case "display":
                return style with { Display = ParseDisplay(value) };
            case "flex-direction":
                return style with { FlexDirection = ParseFlexDirection(value) };
            case "justify-content":
                return style with { JustifyContent = ParseJustifyContent(value) };
            case "align-items":
                return style with { AlignItems = ParseAlignItems(value) };
            case "position":
                return style with { PositionType = ParsePositionType(value) };
            case "left":
                return TryParseLength(value, out UiLength left) ? style with { Left = left } : style;
            case "top":
                return TryParseLength(value, out UiLength top) ? style with { Top = top } : style;
            case "right":
                return TryParseLength(value, out UiLength right) ? style with { Right = right } : style;
            case "bottom":
                return TryParseLength(value, out UiLength bottom) ? style with { Bottom = bottom } : style;
            case "width":
                return TryParseLength(value, out UiLength width) ? style with { Width = width } : style;
            case "height":
                return TryParseLength(value, out UiLength height) ? style with { Height = height } : style;
            case "min-width":
                return TryParseLength(value, out UiLength minWidth) ? style with { MinWidth = minWidth } : style;
            case "min-height":
                return TryParseLength(value, out UiLength minHeight) ? style with { MinHeight = minHeight } : style;
            case "max-width":
                return TryParseLength(value, out UiLength maxWidth) ? style with { MaxWidth = maxWidth } : style;
            case "max-height":
                return TryParseLength(value, out UiLength maxHeight) ? style with { MaxHeight = maxHeight } : style;
            case "flex-basis":
                return TryParseLength(value, out UiLength flexBasis) ? style with { FlexBasis = flexBasis } : style;
            case "flex-grow":
                return TryParseFloat(value, out float flexGrow) ? style with { FlexGrow = flexGrow } : style;
            case "flex-shrink":
                return TryParseFloat(value, out float flexShrink) ? style with { FlexShrink = flexShrink } : style;
            case "gap":
                return TryParseFloat(value, out float gap) ? style with { Gap = gap } : style;
            case "margin":
                return TryParseThickness(value, out UiThickness margin) ? style with { Margin = margin } : style;
            case "padding":
                return TryParseThickness(value, out UiThickness padding) ? style with { Padding = padding } : style;
            case "border-width":
                return TryParseFloat(value, out float borderWidth) ? style with { BorderWidth = borderWidth } : style;
            case "border-radius":
                return TryParseFloat(value, out float borderRadius) ? style with { BorderRadius = borderRadius } : style;
            case "background":
                if (TryParseLinearGradient(value, out UiLinearGradient? backgroundGradient))
                {
                    return style with { BackgroundGradient = backgroundGradient };
                }

                return TryParseColor(value, out SKColor background) ? style with { BackgroundColor = background } : style;
            case "background-color":
                return TryParseColor(value, out SKColor backgroundColor) ? style with { BackgroundColor = backgroundColor } : style;
            case "background-image":
                return TryParseLinearGradient(value, out UiLinearGradient? backgroundImageGradient) ? style with { BackgroundGradient = backgroundImageGradient } : style;
            case "border-color":
                return TryParseColor(value, out SKColor borderColor) ? style with { BorderColor = borderColor } : style;
            case "outline":
                return TryParseOutline(value, style.Color, out float outlineWidth, out SKColor outlineColor) ? style with { OutlineWidth = outlineWidth, OutlineColor = outlineColor } : style;
            case "outline-width":
                return TryParseFloat(value, out float explicitOutlineWidth) ? style with { OutlineWidth = explicitOutlineWidth } : style;
            case "outline-color":
                return TryParseColor(value, out SKColor explicitOutlineColor) ? style with { OutlineColor = explicitOutlineColor } : style;
            case "box-shadow":
                return TryParseShadow(value, out UiShadow boxShadow) ? style with { BoxShadow = boxShadow } : style;
            case "text-shadow":
                return TryParseShadow(value, out UiShadow textShadow) ? style with { TextShadow = textShadow } : style;
            case "color":
                return TryParseColor(value, out SKColor color) ? style with { Color = color } : style;
            case "font-size":
                return TryParseFloat(value, out float fontSize) ? style with { FontSize = fontSize } : style;
            case "font-family":
                return style with { FontFamily = ParseFontFamily(value) };
            case "font-weight":
                return style with { Bold = IsBold(value) };
            case "white-space":
                return style with { WhiteSpace = ParseWhiteSpace(value) };
            case "opacity":
                return TryParseFloat(value, out float opacity) ? style with { Opacity = Math.Clamp(opacity, 0f, 1f) } : style;
            case "visibility":
                return style with { Visible = !string.Equals(value, "hidden", StringComparison.OrdinalIgnoreCase) };
            case "overflow":
                UiOverflow overflow = ParseOverflow(value);
                return style with { Overflow = overflow, ClipContent = overflow is UiOverflow.Hidden or UiOverflow.Clip };
            case "clip-content":
            case "overflow-clip":
                bool clipContent = ParseBoolean(value);
                return style with { ClipContent = clipContent, Overflow = clipContent ? UiOverflow.Clip : style.Overflow };
            default:
                return style;
        }
    }

    private static UiDisplay ParseDisplay(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "none" => UiDisplay.None,
            "block" => UiDisplay.Block,
            "inline" => UiDisplay.Inline,
            "text" => UiDisplay.Text,
            _ => UiDisplay.Flex
        };
    }

    private static UiFlexDirection ParseFlexDirection(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "row" => UiFlexDirection.Row,
            _ => UiFlexDirection.Column
        };
    }

    private static UiJustifyContent ParseJustifyContent(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "center" => UiJustifyContent.Center,
            "end" or "flex-end" => UiJustifyContent.End,
            "space-between" => UiJustifyContent.SpaceBetween,
            "space-around" => UiJustifyContent.SpaceAround,
            "space-evenly" => UiJustifyContent.SpaceEvenly,
            _ => UiJustifyContent.Start
        };
    }

    private static UiAlignItems ParseAlignItems(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "start" or "flex-start" => UiAlignItems.Start,
            "center" => UiAlignItems.Center,
            "end" or "flex-end" => UiAlignItems.End,
            _ => UiAlignItems.Stretch
        };
    }

    private static UiPositionType ParsePositionType(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "absolute" => UiPositionType.Absolute,
            _ => UiPositionType.Relative
        };
    }

    private static UiOverflow ParseOverflow(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "hidden" => UiOverflow.Hidden,
            "scroll" => UiOverflow.Scroll,
            "clip" => UiOverflow.Clip,
            _ => UiOverflow.Visible
        };
    }

    private static bool TryParseLength(string value, out UiLength length)
    {
        string trimmed = value.Trim();
        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            length = UiLength.Auto;
            return true;
        }

        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            if (float.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percentValue))
            {
                length = UiLength.Percent(percentValue);
                return true;
            }
        }

        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^2];
        }

        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float pixelValue))
        {
            length = UiLength.Px(pixelValue);
            return true;
        }

        length = UiLength.Auto;
        return false;
    }

    private static bool TryParseFloat(string value, out float parsed)
    {
        string trimmed = value.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^2];
        }

        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseThickness(string value, out UiThickness thickness)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        float[] values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!TryParseFloat(parts[i], out values[i]))
            {
                thickness = UiThickness.Zero;
                return false;
            }
        }

        thickness = values.Length switch
        {
            1 => UiThickness.All(values[0]),
            2 => new UiThickness(values[1], values[0], values[1], values[0]),
            3 => new UiThickness(values[1], values[0], values[1], values[2]),
            4 => new UiThickness(values[3], values[0], values[1], values[2]),
            _ => UiThickness.Zero
        };
        return values.Length > 0;
    }

    private static bool TryParseColor(string value, out SKColor color)
    {
        color = SKColors.Transparent;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (SKColor.TryParse(value, out color))
        {
            return true;
        }

        if (value.StartsWith("rgba", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = value.Replace("rgba(", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(")", string.Empty, StringComparison.Ordinal).Split(',');
            if (parts.Length == 4 &&
                byte.TryParse(parts[0].Trim(), out byte r) &&
                byte.TryParse(parts[1].Trim(), out byte g) &&
                byte.TryParse(parts[2].Trim(), out byte b) &&
                float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float alpha))
            {
                byte a = alpha > 1f ? (byte)Math.Clamp(alpha, 0f, 255f) : (byte)Math.Clamp(alpha * 255f, 0f, 255f);
                color = new SKColor(r, g, b, a);
                return true;
            }
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = value.Replace("rgb(", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(")", string.Empty, StringComparison.Ordinal).Split(',');
            if (parts.Length == 3 &&
                byte.TryParse(parts[0].Trim(), out byte r) &&
                byte.TryParse(parts[1].Trim(), out byte g) &&
                byte.TryParse(parts[2].Trim(), out byte b))
            {
                color = new SKColor(r, g, b, 255);
                return true;
            }
        }

        return false;
    }

    private static UiWhiteSpace ParseWhiteSpace(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "nowrap" => UiWhiteSpace.NoWrap,
            "pre-wrap" => UiWhiteSpace.PreWrap,
            _ => UiWhiteSpace.Normal
        };
    }

    private static string ParseFontFamily(string value)
    {
        return value.Trim();
    }

    private static bool TryParseOutline(string value, SKColor defaultColor, out float outlineWidth, out SKColor outlineColor)
    {
        outlineWidth = 0f;
        outlineColor = defaultColor;

        foreach (string token in SplitWhitespacePreservingFunctions(value))
        {
            if (TryParseFloat(token, out float parsedWidth))
            {
                outlineWidth = parsedWidth;
                continue;
            }

            if (TryParseColor(token, out SKColor parsedColor))
            {
                outlineColor = parsedColor;
            }
        }

        return outlineWidth > 0f;
    }

    private static bool TryParseShadow(string value, out UiShadow shadow)
    {
        shadow = default;
        string segment = SplitTopLevel(value, ',').FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        float[] lengths = new float[4];
        int lengthCount = 0;
        SKColor color = new(0, 0, 0, 160);

        foreach (string token in SplitWhitespacePreservingFunctions(segment))
        {
            if (token.Equals("inset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseColor(token, out SKColor parsedColor))
            {
                color = parsedColor;
                continue;
            }

            if (lengthCount < lengths.Length && TryParseFloat(token, out float parsedLength))
            {
                lengths[lengthCount++] = parsedLength;
            }
        }

        if (lengthCount < 2)
        {
            return false;
        }

        shadow = new UiShadow(
            lengths[0],
            lengths[1],
            lengthCount >= 3 ? Math.Max(0f, lengths[2]) : 0f,
            lengthCount >= 4 ? lengths[3] : 0f,
            color);
        return true;
    }

    private static bool TryParseLinearGradient(string value, out UiLinearGradient? gradient)
    {
        gradient = null;
        string trimmed = value.Trim();
        if (!trimmed.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
        {
            return false;
        }

        string expression = trimmed["linear-gradient(".Length..^1];
        List<string> parts = SplitTopLevel(expression, ',');
        if (parts.Count < 2)
        {
            return false;
        }

        float angle = 90f;
        int stopStartIndex = 0;
        if (TryParseGradientAngle(parts[0], out float parsedAngle))
        {
            angle = parsedAngle;
            stopStartIndex = 1;
        }

        List<UiGradientStop> stops = new();
        List<int> implicitIndexes = new();
        for (int i = stopStartIndex; i < parts.Count; i++)
        {
            if (!TryParseGradientStop(parts[i], out UiGradientStop stop, out bool hasExplicitPosition))
            {
                return false;
            }

            if (!hasExplicitPosition)
            {
                implicitIndexes.Add(stops.Count);
            }

            stops.Add(stop);
        }

        if (stops.Count < 2)
        {
            return false;
        }

        if (implicitIndexes.Count > 0)
        {
            for (int i = 0; i < stops.Count; i++)
            {
                if (!implicitIndexes.Contains(i))
                {
                    continue;
                }

                float position = stops.Count == 1 ? 0f : (float)i / (stops.Count - 1);
                stops[i] = stops[i] with { Position = position };
            }
        }

        gradient = new UiLinearGradient(angle, stops);
        return true;
    }

    private static bool TryParseGradientAngle(string value, out float angle)
    {
        string trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.EndsWith("deg", StringComparison.Ordinal) && TryParseFloat(trimmed[..^3], out angle))
        {
            return true;
        }

        angle = trimmed switch
        {
            "to right" => 0f,
            "to bottom right" => 45f,
            "to bottom" => 90f,
            "to bottom left" => 135f,
            "to left" => 180f,
            "to top left" => 225f,
            "to top" => 270f,
            "to top right" => 315f,
            _ => 0f
        };

        return trimmed.StartsWith("to ", StringComparison.Ordinal);
    }

    private static bool TryParseGradientStop(string value, out UiGradientStop stop, out bool hasExplicitPosition)
    {
        stop = default;
        hasExplicitPosition = false;
        List<string> tokens = SplitWhitespacePreservingFunctions(value);
        if (tokens.Count == 0)
        {
            return false;
        }

        float position = 0f;
        string colorToken = value.Trim();
        string lastToken = tokens[^1];
        if (lastToken.EndsWith("%", StringComparison.Ordinal) && float.TryParse(lastToken[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
        {
            hasExplicitPosition = true;
            position = Math.Clamp(percent / 100f, 0f, 1f);
            colorToken = string.Join(' ', tokens.Take(tokens.Count - 1));
        }

        if (!TryParseColor(colorToken, out SKColor color))
        {
            return false;
        }

        stop = new UiGradientStop(position, color);
        return true;
    }

    private static List<string> SplitTopLevel(string value, char separator)
    {
        List<string> parts = new();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
            }
            else if (current == separator && depth == 0)
            {
                parts.Add(value[start..i].Trim());
                start = i + 1;
            }
        }

        if (start <= value.Length)
        {
            parts.Add(value[start..].Trim());
        }

        return parts.Where(static part => !string.IsNullOrWhiteSpace(part)).ToList();
    }

    private static List<string> SplitWhitespacePreservingFunctions(string value)
    {
        List<string> tokens = new();
        int depth = 0;
        int start = -1;

        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (char.IsWhiteSpace(current) && depth == 0)
            {
                if (start >= 0)
                {
                    tokens.Add(value[start..i].Trim());
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
            }

            if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
            }
        }

        if (start >= 0)
        {
            tokens.Add(value[start..].Trim());
        }

        return tokens.Where(static token => !string.IsNullOrWhiteSpace(token)).ToList();
    }

    private static bool IsBold(string value)
    {
        if (string.Equals(value, "bold", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fontWeight) && fontWeight >= 600;
    }

    private static bool ParseBoolean(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
}
