using System.Globalization;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public sealed class UiStyleResolver
{
    public void ResolveTree(UiNode root, IReadOnlyList<UiStyleSheet>? styleSheets)
    {
        ArgumentNullException.ThrowIfNull(root);

        IReadOnlyList<UiStyleSheet> effectiveStyleSheets = styleSheets ?? Array.Empty<UiStyleSheet>();
        IReadOnlyDictionary<string, UiKeyframeDefinition> keyframes = BuildKeyframeIndex(effectiveStyleSheets);
        ResolveNode(root, effectiveStyleSheets, keyframes, parentStyle: null, inheritedVariables: null, isRoot: true);
    }

    private void ResolveNode(
        UiNode node,
        IReadOnlyList<UiStyleSheet> styleSheets,
        IReadOnlyDictionary<string, UiKeyframeDefinition> keyframes,
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

        UiAnimationSpec? resolvedAnimation = ResolveAnimationSpec(resolved.Animation, keyframes, variables);
        node.SetComputedStyle(resolved, resolvedAnimation);
        foreach (UiNode child in node.Children)
        {
            ResolveNode(child, styleSheets, keyframes, resolved, variables, isRoot: false);
        }
    }

    private static IReadOnlyDictionary<string, UiKeyframeDefinition> BuildKeyframeIndex(IReadOnlyList<UiStyleSheet> styleSheets)
    {
        Dictionary<string, UiKeyframeDefinition> keyframes = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < styleSheets.Count; i++)
        {
            foreach (UiKeyframeDefinition definition in styleSheets[i].Keyframes)
            {
                keyframes[definition.Name] = definition;
            }
        }

        return keyframes;
    }

    private static UiAnimationSpec? ResolveAnimationSpec(
        UiAnimationSpec? animation,
        IReadOnlyDictionary<string, UiKeyframeDefinition> keyframes,
        IReadOnlyDictionary<string, string> variables)
    {
        if (animation == null || animation.Entries.Count == 0)
        {
            return null;
        }

        List<UiAnimationEntry> entries = new(animation.Entries.Count);
        for (int i = 0; i < animation.Entries.Count; i++)
        {
            UiAnimationEntry entry = animation.Entries[i];
            if (!keyframes.TryGetValue(entry.Name, out UiKeyframeDefinition? definition) || definition == null)
            {
                continue;
            }

            entries.Add(entry with { Keyframes = ResolveKeyframes(definition, variables) });
        }

        return entries.Count == 0 ? null : new UiAnimationSpec(entries);
    }

    private static UiKeyframeDefinition ResolveKeyframes(UiKeyframeDefinition definition, IReadOnlyDictionary<string, string> variables)
    {
        List<UiKeyframeStop> stops = new(definition.Stops.Count);
        for (int i = 0; i < definition.Stops.Count; i++)
        {
            UiKeyframeStop stop = definition.Stops[i];
            UiStyleDeclaration resolvedDeclaration = new();
            foreach (KeyValuePair<string, string> property in stop.Declaration)
            {
                resolvedDeclaration.Set(property.Key, ResolveValue(property.Value, variables));
            }

            stops.Add(new UiKeyframeStop(stop.Offset, resolvedDeclaration));
        }

        return new UiKeyframeDefinition(definition.Name, stops);
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

        if (!HasExplicitValue(declaration, "font-family") && string.Equals(style.FontFamily, implicitStyle.FontFamily, StringComparison.Ordinal))
        {
            style = style with { FontFamily = parentStyle.FontFamily };
        }

        if (!HasExplicitValue(declaration, "white-space") && style.WhiteSpace == implicitStyle.WhiteSpace)
        {
            style = style with { WhiteSpace = parentStyle.WhiteSpace };
        }

        if (!HasExplicitValue(declaration, "direction") && style.Direction == implicitStyle.Direction)
        {
            style = style with { Direction = parentStyle.Direction };
        }

        if (!HasExplicitValue(declaration, "text-align") && style.TextAlign == implicitStyle.TextAlign)
        {
            style = style with { TextAlign = parentStyle.TextAlign };
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
            UiNodeKind.Checkbox or UiNodeKind.Radio or UiNodeKind.Toggle => UiStyle.Default with
            {
                Display = UiDisplay.Flex,
                FlexDirection = UiFlexDirection.Row,
                AlignItems = UiAlignItems.Center,
                Padding = UiThickness.Symmetric(10f, 8f),
                BorderRadius = 8f
            },
            UiNodeKind.Table => UiStyle.Default with { Display = UiDisplay.Flex, FlexDirection = UiFlexDirection.Column, AlignItems = UiAlignItems.Stretch },
            UiNodeKind.TableHeader or UiNodeKind.TableBody or UiNodeKind.TableFooter => UiStyle.Default with { Display = UiDisplay.Flex, FlexDirection = UiFlexDirection.Column, AlignItems = UiAlignItems.Stretch },
            UiNodeKind.TableRow => UiStyle.Default with { Display = UiDisplay.Flex, FlexDirection = UiFlexDirection.Row, AlignItems = UiAlignItems.Stretch },
            UiNodeKind.TableCell or UiNodeKind.TableHeaderCell => UiStyle.Default with
            {
                Display = UiDisplay.Flex,
                FlexDirection = UiFlexDirection.Column,
                AlignItems = UiAlignItems.Stretch,
                FlexGrow = 1f,
                FlexShrink = 1f,
                FlexBasis = UiLength.Px(0f)
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

    internal static UiStyle ApplyProperty(UiStyle style, string propertyName, string rawValue)
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
            case "align-content":
                return style with { AlignContent = ParseAlignContent(value) };
            case "flex-wrap":
                return style with { FlexWrap = ParseFlexWrap(value) };
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
                return TryParseGap(value, out float gap, out float rowGap, out float columnGap)
                    ? style with { Gap = gap, RowGap = rowGap, ColumnGap = columnGap }
                    : style;
            case "row-gap":
                return TryParseFloat(value, out float explicitRowGap) ? style with { RowGap = explicitRowGap } : style;
            case "column-gap":
                return TryParseFloat(value, out float explicitColumnGap) ? style with { ColumnGap = explicitColumnGap } : style;
            case "margin":
                return TryParseThickness(value, out UiThickness margin) ? style with { Margin = margin } : style;
            case "padding":
                return TryParseThickness(value, out UiThickness padding) ? style with { Padding = padding } : style;
            case "border-width":
                return TryParseFloat(value, out float borderWidth) ? style with { BorderWidth = borderWidth } : style;
            case "border-radius":
                return TryParseFloat(value, out float borderRadius) ? style with { BorderRadius = borderRadius } : style;
            case "border-style":
                return style with { BorderStyle = ParseBorderStyle(value) };
            case "z-index":
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int zIndex) ? style with { ZIndex = zIndex } : style;
            case "background":
                if (TryParseBackgroundLayers(value, out IReadOnlyList<UiBackgroundLayer>? backgroundLayers))
                {
                    return style with
                    {
                        BackgroundLayers = backgroundLayers,
                        BackgroundGradient = backgroundLayers.Select(static layer => layer.Gradient).FirstOrDefault(static gradient => gradient != null),
                        BackgroundColor = backgroundLayers.Count == 1 && backgroundLayers[0].Gradient == null
                            ? backgroundLayers[0].Color
                            : style.BackgroundColor
                    };
                }

                return TryParseColor(value, out SKColor background)
                    ? style with { BackgroundColor = background, BackgroundLayers = Array.Empty<UiBackgroundLayer>(), BackgroundGradient = null }
                    : style;
            case "background-color":
                return TryParseColor(value, out SKColor backgroundColor)
                    ? style with { BackgroundColor = backgroundColor }
                    : style;
            case "background-image":
                return TryParseBackgroundLayers(value, out IReadOnlyList<UiBackgroundLayer>? backgroundImageLayers)
                    ? style with
                    {
                        BackgroundLayers = backgroundImageLayers,
                        BackgroundGradient = backgroundImageLayers.Select(static layer => layer.Gradient).FirstOrDefault(static gradient => gradient != null)
                    }
                    : style;
            case "background-size":
                return TryParseBackgroundSizeList(value, out IReadOnlyList<UiBackgroundSize>? backgroundSizes)
                    ? style with { BackgroundSizes = backgroundSizes }
                    : style;
            case "background-position":
                return TryParseBackgroundPositionList(value, out IReadOnlyList<UiBackgroundPosition>? backgroundPositions)
                    ? style with { BackgroundPositions = backgroundPositions }
                    : style;
            case "background-repeat":
                return TryParseBackgroundRepeatList(value, out IReadOnlyList<UiBackgroundRepeat>? backgroundRepeats)
                    ? style with { BackgroundRepeats = backgroundRepeats }
                    : style;
            case "border-color":
                return TryParseColor(value, out SKColor borderColor) ? style with { BorderColor = borderColor } : style;
            case "outline":
                return TryParseOutline(value, style.Color, out float outlineWidth, out SKColor outlineColor) ? style with { OutlineWidth = outlineWidth, OutlineColor = outlineColor } : style;
            case "outline-width":
                return TryParseFloat(value, out float explicitOutlineWidth) ? style with { OutlineWidth = explicitOutlineWidth } : style;
            case "outline-color":
                return TryParseColor(value, out SKColor explicitOutlineColor) ? style with { OutlineColor = explicitOutlineColor } : style;
            case "box-shadow":
                return TryParseShadowList(value, out IReadOnlyList<UiShadow>? boxShadows)
                    ? style with { BoxShadow = boxShadows[0], BoxShadows = boxShadows }
                    : style;
            case "filter":
                return TryParseBlurFunction(value, out float filterBlurRadius) ? style with { FilterBlurRadius = filterBlurRadius } : style;
            case "backdrop-filter":
                return TryParseBlurFunction(value, out float backdropBlurRadius) ? style with { BackdropBlurRadius = backdropBlurRadius } : style;
            case "mask":
            case "mask-image":
                return TryParseMaskGradient(value, out UiLinearGradient? maskGradient) ? style with { MaskGradient = maskGradient } : style;
            case "clip-path":
                return TryParseClipPath(value, out UiClipPath? clipPath) ? style with { ClipPath = clipPath } : style;
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
            case "direction":
                return style with { Direction = ParseTextDirection(value) };
            case "text-align":
                return style with { TextAlign = ParseTextAlign(value) };
            case "text-decoration":
            case "text-decoration-line":
                return style with { TextDecorationLine = ParseTextDecorationLine(value) };
            case "text-overflow":
                return style with { TextOverflow = ParseTextOverflow(value) };
            case "object-fit":
                return style with { ObjectFit = ParseObjectFit(value) };
            case "image-slice":
            case "nine-slice":
            case "border-image-slice":
                return TryParseThickness(value, out UiThickness imageSlice) ? style with { ImageSlice = imageSlice } : style;
            case "transform":
                return TryParseTransform(value, out UiTransform? transform) ? style with { Transform = transform ?? UiTransform.Identity } : style;
            case "animation":
                return TryParseAnimationSpec(value, out UiAnimationSpec? animation) ? style with { Animation = animation } : style;
            case "transition":
                return TryParseTransitionSpec(value, out UiTransitionSpec? transition) ? style with { Transition = transition } : style;
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

    private static UiAlignContent ParseAlignContent(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "start" or "flex-start" => UiAlignContent.Start,
            "center" => UiAlignContent.Center,
            "end" or "flex-end" => UiAlignContent.End,
            "space-between" => UiAlignContent.SpaceBetween,
            "space-around" => UiAlignContent.SpaceAround,
            "space-evenly" => UiAlignContent.SpaceEvenly,
            _ => UiAlignContent.Stretch
        };
    }

    private static UiFlexWrap ParseFlexWrap(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "wrap" => UiFlexWrap.Wrap,
            "wrap-reverse" => UiFlexWrap.WrapReverse,
            _ => UiFlexWrap.NoWrap
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

    private static UiBorderStyle ParseBorderStyle(string value)
    {
        string token = SplitWhitespacePreservingFunctions(value).FirstOrDefault() ?? string.Empty;
        return token.ToLowerInvariant() switch
        {
            "dashed" => UiBorderStyle.Dashed,
            "dotted" => UiBorderStyle.Dotted,
            _ => UiBorderStyle.Solid
        };
    }

    private static UiTextDirection ParseTextDirection(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "rtl" => UiTextDirection.Rtl,
            "auto" => UiTextDirection.Auto,
            _ => UiTextDirection.Ltr
        };
    }

    private static UiTextAlign ParseTextAlign(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "left" => UiTextAlign.Left,
            "right" => UiTextAlign.Right,
            "center" => UiTextAlign.Center,
            "end" => UiTextAlign.End,
            _ => UiTextAlign.Start
        };
    }

    private static UiObjectFit ParseObjectFit(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "contain" => UiObjectFit.Contain,
            "cover" => UiObjectFit.Cover,
            "none" => UiObjectFit.None,
            "scale-down" => UiObjectFit.ScaleDown,
            _ => UiObjectFit.Fill
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

    private static bool TryParseGap(string value, out float gap, out float rowGap, out float columnGap)
    {
        gap = 0f;
        rowGap = 0f;
        columnGap = 0f;

        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 2)
        {
            return false;
        }

        if (!TryParseFloat(parts[0], out rowGap))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            gap = rowGap;
            columnGap = rowGap;
            return true;
        }

        if (!TryParseFloat(parts[1], out columnGap))
        {
            return false;
        }

        return true;
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

    private static UiTextDecorationLine ParseTextDecorationLine(string value)
    {
        UiTextDecorationLine decoration = UiTextDecorationLine.None;
        foreach (string token in SplitWhitespacePreservingFunctions(value))
        {
            switch (token.ToLowerInvariant())
            {
                case "none":
                    return UiTextDecorationLine.None;
                case "underline":
                    decoration |= UiTextDecorationLine.Underline;
                    break;
                case "line-through":
                    decoration |= UiTextDecorationLine.LineThrough;
                    break;
            }
        }
        return decoration;
    }

    private static UiTextOverflow ParseTextOverflow(string value)
    {
        return value.Trim().Equals("ellipsis", StringComparison.OrdinalIgnoreCase) ? UiTextOverflow.Ellipsis : UiTextOverflow.Clip;
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
        string segment = value.Trim();
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

    private static bool TryParseShadowList(string value, out IReadOnlyList<UiShadow> shadows)
    {
        List<UiShadow> parsed = new();
        foreach (string segment in SplitTopLevel(value, ','))
        {
            if (!TryParseShadow(segment, out UiShadow shadow))
            {
                shadows = Array.Empty<UiShadow>();
                return false;
            }

            parsed.Add(shadow);
        }

        shadows = parsed;
        return parsed.Count > 0;
    }

    private static bool TryParseBackgroundLayers(string value, out IReadOnlyList<UiBackgroundLayer> layers)
    {
        List<UiBackgroundLayer> parsed = new();
        foreach (string segment in SplitTopLevel(value, ','))
        {
            if (TryParseBackgroundImageSource(segment, out string? imageSource) && imageSource != null)
            {
                parsed.Add(UiBackgroundLayer.FromImage(imageSource));
                continue;
            }

            if (TryParseLinearGradient(segment, out UiLinearGradient? gradient) && gradient != null)
            {
                parsed.Add(UiBackgroundLayer.FromGradient(gradient));
                continue;
            }

            if (TryParseColor(segment, out SKColor color))
            {
                parsed.Add(UiBackgroundLayer.FromColor(color));
                continue;
            }

            layers = Array.Empty<UiBackgroundLayer>();
            return false;
        }

        layers = parsed;
        return parsed.Count > 0;
    }

    private static bool TryParseBackgroundImageSource(string value, out string? imageSource)
    {
        imageSource = null;
        string trimmed = value.Trim();
        if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
        {
            return false;
        }

        string inner = trimmed[4..^1].Trim();
        if ((inner.StartsWith('"') && inner.EndsWith('"')) || (inner.StartsWith('\'') && inner.EndsWith('\'')))
        {
            inner = inner[1..^1];
        }

        if (string.IsNullOrWhiteSpace(inner))
        {
            return false;
        }

        imageSource = inner;
        return true;
    }

    private static bool TryParseBackgroundSizeList(string value, out IReadOnlyList<UiBackgroundSize> sizes)
    {
        List<UiBackgroundSize> parsed = new();
        foreach (string segment in SplitTopLevel(value, ','))
        {
            if (!TryParseBackgroundSize(segment, out UiBackgroundSize size))
            {
                sizes = Array.Empty<UiBackgroundSize>();
                return false;
            }

            parsed.Add(size);
        }

        sizes = parsed;
        return parsed.Count > 0;
    }

    private static bool TryParseBackgroundPositionList(string value, out IReadOnlyList<UiBackgroundPosition> positions)
    {
        List<UiBackgroundPosition> parsed = new();
        foreach (string segment in SplitTopLevel(value, ','))
        {
            if (!TryParseBackgroundPosition(segment, out UiBackgroundPosition position))
            {
                positions = Array.Empty<UiBackgroundPosition>();
                return false;
            }

            parsed.Add(position);
        }

        positions = parsed;
        return parsed.Count > 0;
    }

    private static bool TryParseBackgroundRepeatList(string value, out IReadOnlyList<UiBackgroundRepeat> repeats)
    {
        List<UiBackgroundRepeat> parsed = new();
        foreach (string segment in SplitTopLevel(value, ','))
        {
            if (!TryParseBackgroundRepeat(segment, out UiBackgroundRepeat repeat))
            {
                repeats = Array.Empty<UiBackgroundRepeat>();
                return false;
            }

            parsed.Add(repeat);
        }

        repeats = parsed;
        return parsed.Count > 0;
    }

    private static bool TryParseBackgroundSize(string value, out UiBackgroundSize size)
    {
        size = UiBackgroundSize.Auto;
        List<string> tokens = SplitWhitespacePreservingFunctions(value);
        if (tokens.Count == 0)
        {
            return false;
        }

        if (tokens.Count == 1)
        {
            string token = tokens[0].Trim().ToLowerInvariant();
            switch (token)
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
            }

            if (TryParseLength(tokens[0], out UiLength singleLength))
            {
                size = UiBackgroundSize.Explicit(singleLength, UiLength.Auto);
                return true;
            }

            return false;
        }

        if (tokens.Count > 2)
        {
            return false;
        }

        if (!TryParseLength(tokens[0], out UiLength width) || !TryParseLength(tokens[1], out UiLength height))
        {
            return false;
        }

        size = width.IsAuto && height.IsAuto
            ? UiBackgroundSize.Auto
            : UiBackgroundSize.Explicit(width, height);
        return true;
    }

    private static bool TryParseBackgroundPosition(string value, out UiBackgroundPosition position)
    {
        position = UiBackgroundPosition.TopLeft;
        List<string> tokens = SplitWhitespacePreservingFunctions(value);
        if (tokens.Count == 0)
        {
            return false;
        }

        if (tokens.Count == 1)
        {
            string token = tokens[0].Trim().ToLowerInvariant();
            if (token is "center")
            {
                position = UiBackgroundPosition.Center;
                return true;
            }

            if (TryParseBackgroundPositionComponent(token, horizontal: true, out UiLength horizontal))
            {
                position = new UiBackgroundPosition(horizontal, UiLength.Percent(50f));
                return true;
            }

            if (TryParseBackgroundPositionComponent(token, horizontal: false, out UiLength vertical))
            {
                position = new UiBackgroundPosition(UiLength.Percent(50f), vertical);
                return true;
            }

            return false;
        }

        string first = tokens[0].Trim().ToLowerInvariant();
        string second = tokens[1].Trim().ToLowerInvariant();
        if (TryParseBackgroundPositionComponent(first, horizontal: true, out UiLength x)
            && TryParseBackgroundPositionComponent(second, horizontal: false, out UiLength y))
        {
            position = new UiBackgroundPosition(x, y);
            return true;
        }

        if (TryParseBackgroundPositionComponent(first, horizontal: false, out y)
            && TryParseBackgroundPositionComponent(second, horizontal: true, out x))
        {
            position = new UiBackgroundPosition(x, y);
            return true;
        }

        return false;
    }

    private static bool TryParseBackgroundRepeat(string value, out UiBackgroundRepeat repeat)
    {
        repeat = UiBackgroundRepeat.Repeat;
        string trimmed = value.Trim().ToLowerInvariant();
        switch (trimmed)
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

        string normalized = token.Trim().ToLowerInvariant();
        if (horizontal)
        {
            return normalized switch
            {
                "left" => Assign(UiLength.Percent(0f), out length),
                "center" => Assign(UiLength.Percent(50f), out length),
                "right" => Assign(UiLength.Percent(100f), out length),
                _ => false
            };
        }

        return normalized switch
        {
            "top" => Assign(UiLength.Percent(0f), out length),
            "center" => Assign(UiLength.Percent(50f), out length),
            "bottom" => Assign(UiLength.Percent(100f), out length),
            _ => false
        };
    }

    private static bool Assign(UiLength value, out UiLength target)
    {
        target = value;
        return true;
    }

    private static bool TryParseBlurFunction(string value, out float blurRadius)
    {
        blurRadius = 0f;
        string trimmed = value.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!trimmed.StartsWith("blur(", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
        {
            return false;
        }

        return TryParseFloat(trimmed[5..^1], out blurRadius);
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
        if (TryParseAngle(trimmed, out angle))
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

    private static bool TryParseTransform(string value, out UiTransform? transform)
    {
        transform = null;
        string trimmed = value.Trim();
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
        {
            transform = UiTransform.Identity;
            return true;
        }

        List<string> tokens = SplitWhitespacePreservingFunctions(trimmed);
        if (tokens.Count == 0)
        {
            return false;
        }

        List<UiTransformOperation> operations = new(tokens.Count);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!TryParseTransformOperation(tokens[i], out UiTransformOperation operation))
            {
                transform = null;
                return false;
            }

            operations.Add(operation);
        }

        transform = operations.Count == 0 ? UiTransform.Identity : new UiTransform(operations);
        return true;
    }

    private static bool TryParseTransformOperation(string token, out UiTransformOperation operation)
    {
        operation = default;
        int openIndex = token.IndexOf('(');
        if (openIndex <= 0 || !token.EndsWith(')'))
        {
            return false;
        }

        string name = token[..openIndex].Trim().ToLowerInvariant();
        string argumentText = token[(openIndex + 1)..^1].Trim();
        List<string> arguments = SplitTransformArguments(argumentText);

        switch (name)
        {
            case "translate":
                if (arguments.Count is < 1 or > 2 || !TryParseTransformLength(arguments[0], out UiLength translateX))
                {
                    return false;
                }

                UiLength translateY = UiLength.Px(0f);
                if (arguments.Count == 2 && !TryParseTransformLength(arguments[1], out translateY))
                {
                    return false;
                }

                operation = UiTransformOperation.Translate(translateX, translateY);
                return true;
            case "translatex":
                if (!TryParseTransformLength(argumentText, out UiLength tx))
                {
                    return false;
                }

                operation = UiTransformOperation.Translate(tx, UiLength.Px(0f));
                return true;
            case "translatey":
                if (!TryParseTransformLength(argumentText, out UiLength ty))
                {
                    return false;
                }

                operation = UiTransformOperation.Translate(UiLength.Px(0f), ty);
                return true;
            case "scale":
                if (arguments.Count is < 1 or > 2 || !TryParseFloat(arguments[0], out float scaleX))
                {
                    return false;
                }

                float scaleY = scaleX;
                if (arguments.Count == 2 && !TryParseFloat(arguments[1], out scaleY))
                {
                    return false;
                }

                operation = UiTransformOperation.Scale(scaleX, scaleY);
                return true;
            case "scalex":
                if (!TryParseFloat(argumentText, out float sx))
                {
                    return false;
                }

                operation = UiTransformOperation.Scale(sx, 1f);
                return true;
            case "scaley":
                if (!TryParseFloat(argumentText, out float sy))
                {
                    return false;
                }

                operation = UiTransformOperation.Scale(1f, sy);
                return true;
            case "rotate":
                if (!TryParseAngle(argumentText, out float angle))
                {
                    return false;
                }

                operation = UiTransformOperation.Rotate(angle);
                return true;
            default:
                return false;
        }
    }

    private static List<string> SplitTransformArguments(string value)
    {
        List<string> commaSeparated = SplitTopLevel(value, ',');
        if (commaSeparated.Count > 1)
        {
            return commaSeparated;
        }

        return SplitWhitespacePreservingFunctions(value);
    }

    private static bool TryParseTransformLength(string value, out UiLength length)
    {
        if (TryParseLength(value, out length))
        {
            return true;
        }

        if (TryParseFloat(value, out float pixels))
        {
            length = UiLength.Px(pixels);
            return true;
        }

        length = UiLength.Auto;
        return false;
    }

    private static bool TryParseMaskGradient(string value, out UiLinearGradient? gradient)
    {
        string trimmed = value.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            gradient = null;
            return true;
        }

        return TryParseLinearGradient(trimmed, out gradient);
    }

    private static bool TryParseClipPath(string value, out UiClipPath? clipPath)
    {
        clipPath = null;
        string trimmed = value.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.StartsWith("inset(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(')'))
        {
            string expression = trimmed["inset(".Length..^1];
            if (!TryParseThickness(expression, out UiThickness inset))
            {
                return false;
            }

            clipPath = UiClipPath.InsetShape(inset);
            return true;
        }

        if (trimmed.StartsWith("circle(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(')'))
        {
            string expression = trimmed["circle(".Length..^1].Trim();
            string[] parts = expression.Split(new[] { " at " }, StringSplitOptions.None);
            if (!TryParseLength(parts[0].Trim(), out UiLength radius))
            {
                return false;
            }

            UiLength centerX = UiLength.Percent(50f);
            UiLength centerY = UiLength.Percent(50f);
            if (parts.Length > 1)
            {
                List<string> centerTokens = SplitWhitespacePreservingFunctions(parts[1]);
                if (centerTokens.Count != 2
                    || !TryParseLength(centerTokens[0], out centerX)
                    || !TryParseLength(centerTokens[1], out centerY))
                {
                    return false;
                }
            }

            clipPath = UiClipPath.Circle(radius, centerX, centerY);
            return true;
        }

        return false;
    }

    private static bool TryParseAngle(string value, out float angle)
    {
        string trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.EndsWith("deg", StringComparison.Ordinal) && TryParseFloat(trimmed[..^3], out angle))
        {
            return true;
        }

        if (trimmed.EndsWith("rad", StringComparison.Ordinal) && TryParseFloat(trimmed[..^3], out float radians))
        {
            angle = radians * (180f / MathF.PI);
            return true;
        }

        if (trimmed.EndsWith("turn", StringComparison.Ordinal) && TryParseFloat(trimmed[..^4], out float turns))
        {
            angle = turns * 360f;
            return true;
        }

        return TryParseFloat(trimmed, out angle);
    }

    private static bool TryParseAnimationSpec(string value, out UiAnimationSpec? animation)
    {
        animation = null;
        if (string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        List<string> parts = SplitTopLevel(value, ',');
        if (parts.Count == 0)
        {
            return false;
        }

        List<UiAnimationEntry> entries = new(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            if (!TryParseAnimationEntry(parts[i], out UiAnimationEntry entry))
            {
                return false;
            }

            entries.Add(entry);
        }

        animation = entries.Count == 0 ? null : new UiAnimationSpec(entries);
        return true;
    }

    private static bool TryParseAnimationEntry(string value, out UiAnimationEntry entry)
    {
        entry = default!;
        List<string> tokens = SplitWhitespacePreservingFunctions(value);
        if (tokens.Count == 0)
        {
            return false;
        }

        string? name = null;
        float durationSeconds = 0f;
        float delaySeconds = 0f;
        UiTransitionEasing easing = UiTransitionEasing.Ease;
        float iterationCount = 1f;
        UiAnimationDirection direction = UiAnimationDirection.Normal;
        UiAnimationFillMode fillMode = UiAnimationFillMode.None;
        UiAnimationPlayState playState = UiAnimationPlayState.Running;
        bool durationAssigned = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i].Trim();
            if (TryParseTime(token, out float timeSeconds))
            {
                if (!durationAssigned)
                {
                    durationSeconds = timeSeconds;
                    durationAssigned = true;
                }
                else
                {
                    delaySeconds = timeSeconds;
                }

                continue;
            }

            if (TryParseTransitionEasing(token, out UiTransitionEasing parsedEasing))
            {
                easing = parsedEasing;
                continue;
            }

            if (TryParseAnimationIterationCount(token, out float parsedIterationCount))
            {
                iterationCount = parsedIterationCount;
                continue;
            }

            if (TryParseAnimationDirection(token, out UiAnimationDirection parsedDirection))
            {
                direction = parsedDirection;
                continue;
            }

            if (TryParseAnimationFillMode(token, out UiAnimationFillMode parsedFillMode))
            {
                fillMode = parsedFillMode;
                continue;
            }

            if (TryParseAnimationPlayState(token, out UiAnimationPlayState parsedPlayState))
            {
                playState = parsedPlayState;
                continue;
            }

            name = token;
        }

        if (string.IsNullOrWhiteSpace(name) || durationSeconds <= 0f)
        {
            return false;
        }

        entry = new UiAnimationEntry(name, durationSeconds, delaySeconds, easing, iterationCount, direction, fillMode, playState);
        return true;
    }

    private static bool TryParseAnimationIterationCount(string value, out float iterationCount)
    {
        if (string.Equals(value, "infinite", StringComparison.OrdinalIgnoreCase))
        {
            iterationCount = float.PositiveInfinity;
            return true;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedIterationCount) && parsedIterationCount >= 0f)
        {
            iterationCount = parsedIterationCount;
            return true;
        }

        iterationCount = 1f;
        return false;
    }

    private static bool TryParseAnimationDirection(string value, out UiAnimationDirection direction)
    {
        direction = value.ToLowerInvariant() switch
        {
            "normal" => UiAnimationDirection.Normal,
            "reverse" => UiAnimationDirection.Reverse,
            "alternate" => UiAnimationDirection.Alternate,
            "alternate-reverse" => UiAnimationDirection.AlternateReverse,
            _ => UiAnimationDirection.Normal
        };

        return value.Equals("normal", StringComparison.OrdinalIgnoreCase)
            || value.Equals("reverse", StringComparison.OrdinalIgnoreCase)
            || value.Equals("alternate", StringComparison.OrdinalIgnoreCase)
            || value.Equals("alternate-reverse", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseAnimationFillMode(string value, out UiAnimationFillMode fillMode)
    {
        fillMode = value.ToLowerInvariant() switch
        {
            "none" => UiAnimationFillMode.None,
            "forwards" => UiAnimationFillMode.Forwards,
            "backwards" => UiAnimationFillMode.Backwards,
            "both" => UiAnimationFillMode.Both,
            _ => UiAnimationFillMode.None
        };

        return value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("forwards", StringComparison.OrdinalIgnoreCase)
            || value.Equals("backwards", StringComparison.OrdinalIgnoreCase)
            || value.Equals("both", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseAnimationPlayState(string value, out UiAnimationPlayState playState)
    {
        playState = value.Equals("paused", StringComparison.OrdinalIgnoreCase)
            ? UiAnimationPlayState.Paused
            : UiAnimationPlayState.Running;
        return value.Equals("running", StringComparison.OrdinalIgnoreCase)
            || value.Equals("paused", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseTransitionSpec(string value, out UiTransitionSpec? transition)
    {
        transition = null;
        List<string> parts = SplitTopLevel(value, ',');
        if (parts.Count == 0)
        {
            return false;
        }

        List<UiTransitionEntry> entries = new(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            if (!TryParseTransitionEntry(parts[i], out UiTransitionEntry entry))
            {
                return false;
            }

            entries.Add(entry);
        }

        transition = new UiTransitionSpec(entries);
        return true;
    }

    private static bool TryParseTransitionEntry(string value, out UiTransitionEntry entry)
    {
        entry = default!;
        List<string> tokens = SplitWhitespacePreservingFunctions(value);
        if (tokens.Count == 0)
        {
            return false;
        }

        string propertyName = "all";
        float durationSeconds = 0f;
        float delaySeconds = 0f;
        UiTransitionEasing easing = UiTransitionEasing.Ease;
        bool durationAssigned = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i].Trim();
            if (TryParseTime(token, out float timeSeconds))
            {
                if (!durationAssigned)
                {
                    durationSeconds = timeSeconds;
                    durationAssigned = true;
                }
                else
                {
                    delaySeconds = timeSeconds;
                }

                continue;
            }

            if (TryParseTransitionEasing(token, out UiTransitionEasing parsedEasing))
            {
                easing = parsedEasing;
                continue;
            }

            propertyName = NormalizeTransitionPropertyName(token);
        }

        entry = new UiTransitionEntry(propertyName, durationSeconds, delaySeconds, easing);
        return durationSeconds > 0f;
    }

    private static bool TryParseTime(string value, out float seconds)
    {
        seconds = 0f;
        string trimmed = value.Trim();
        if (trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase) && float.TryParse(trimmed[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out float milliseconds))
        {
            seconds = milliseconds / 1000f;
            return true;
        }

        if (trimmed.EndsWith('s') && float.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSeconds))
        {
            seconds = parsedSeconds;
            return true;
        }

        return false;
    }

    private static bool TryParseTransitionEasing(string value, out UiTransitionEasing easing)
    {
        easing = value.ToLowerInvariant() switch
        {
            "linear" => UiTransitionEasing.Linear,
            "ease-in" => UiTransitionEasing.EaseIn,
            "ease-out" => UiTransitionEasing.EaseOut,
            "ease-in-out" => UiTransitionEasing.EaseInOut,
            "ease" => UiTransitionEasing.Ease,
            _ => UiTransitionEasing.Linear
        };

        return value.Equals("linear", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ease", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ease-in", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ease-out", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ease-in-out", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTransitionPropertyName(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "background" => "background-color",
            "filter-blur" => "filter",
            "backdrop-blur" => "backdrop-filter",
            _ => value.Trim().ToLowerInvariant()
        };
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
