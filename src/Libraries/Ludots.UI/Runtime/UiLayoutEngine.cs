using FlexLayoutSharp;
using FlexAlign = FlexLayoutSharp.Align;
using FlexDirection = FlexLayoutSharp.FlexDirection;
using FlexDisplay = FlexLayoutSharp.Display;
using FlexEdge = FlexLayoutSharp.Edge;
using FlexJustify = FlexLayoutSharp.Justify;
using FlexMeasureMode = FlexLayoutSharp.MeasureMode;
using FlexNode = FlexLayoutSharp.Node;
using FlexOverflow = FlexLayoutSharp.Overflow;
using FlexPositionType = FlexLayoutSharp.PositionType;
using FlexSize = FlexLayoutSharp.Size;

namespace Ludots.UI.Runtime;

public sealed class UiLayoutEngine
{
    public void Layout(UiNode root, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(root);

        FlexNode flexRoot = BuildFlexTree(root, isRoot: true, rootWidth: width, rootHeight: height);
        flexRoot.CalculateLayout(width, height, Direction.LTR);
        ApplyLayout(root, flexRoot, 0f, 0f);
    }

    private FlexNode BuildFlexTree(UiNode node, bool isRoot, float rootWidth, float rootHeight)
    {
        FlexNode flexNode = new() { Context = node };
        ConfigureNodeStyle(flexNode, node, isRoot, rootWidth, rootHeight);

        if (ShouldMeasureAsLeaf(node))
        {
            flexNode.SetMeasureFunc((_, width, widthMode, height, heightMode) => MeasureNode(node, width, widthMode, height, heightMode));
            return flexNode;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            FlexNode childFlexNode = BuildFlexTree(node.Children[i], isRoot: false, rootWidth, rootHeight);
            ApplyGapOffset(childFlexNode, node.Style, i);
            flexNode.AddChild(childFlexNode);
        }

        return flexNode;
    }

    private void ConfigureNodeStyle(FlexNode flexNode, UiNode node, bool isRoot, float rootWidth, float rootHeight)
    {
        UiStyle style = node.Style;
        bool isVisible = style.Visible && style.Display != UiDisplay.None;

        flexNode.StyleSetDisplay(isVisible ? FlexDisplay.Flex : FlexDisplay.None);
        flexNode.StyleSetFlexDirection(style.FlexDirection == UiFlexDirection.Row ? FlexDirection.Row : FlexDirection.Column);
        flexNode.StyleSetJustifyContent(MapJustify(style.JustifyContent));
        flexNode.StyleSetAlignItems(MapAlign(style.AlignItems));
        flexNode.StyleSetOverflow(MapOverflow(style));
        flexNode.StyleSetPositionType(style.PositionType == UiPositionType.Absolute ? FlexPositionType.Absolute : FlexPositionType.Relative);
        flexNode.StyleSetFlexGrow(style.FlexGrow);
        flexNode.StyleSetFlexShrink(style.FlexShrink);

        ApplyLength(style.Width, flexNode.StyleSetWidth, flexNode.StyleSetWidthPercent, flexNode.StyleSetWidthAuto);
        ApplyLength(style.Height, flexNode.StyleSetHeight, flexNode.StyleSetHeightPercent, flexNode.StyleSetHeightAuto);
        ApplyLength(style.MinWidth, flexNode.StyleSetMinWidth, flexNode.StyleSetMinWidthPercent, null);
        ApplyLength(style.MinHeight, flexNode.StyleSetMinHeight, flexNode.StyleSetMinHeightPercent, null);
        ApplyLength(style.MaxWidth, flexNode.StyleSetMaxWidth, flexNode.StyleSetMaxWidthPercent, null);
        ApplyLength(style.MaxHeight, flexNode.StyleSetMaxHeight, flexNode.StyleSetMaxHeightPercent, null);
        ApplyLength(style.FlexBasis, flexNode.StyleSetFlexBasis, flexNode.StyleSetFlexBasisPercent, flexNode.NodeStyleSetFlexBasisAuto);

        ApplyLength(style.Left, value => flexNode.StyleSetPosition(FlexEdge.Left, value), value => flexNode.StyleSetPositionPercent(FlexEdge.Left, value), null);
        ApplyLength(style.Top, value => flexNode.StyleSetPosition(FlexEdge.Top, value), value => flexNode.StyleSetPositionPercent(FlexEdge.Top, value), null);
        ApplyLength(style.Right, value => flexNode.StyleSetPosition(FlexEdge.Right, value), value => flexNode.StyleSetPositionPercent(FlexEdge.Right, value), null);
        ApplyLength(style.Bottom, value => flexNode.StyleSetPosition(FlexEdge.Bottom, value), value => flexNode.StyleSetPositionPercent(FlexEdge.Bottom, value), null);

        ApplyThickness(style.Margin, (edge, value) => flexNode.StyleSetMargin(edge, value), (edge, value) => flexNode.StyleSetMarginPercent(edge, value));
        ApplyThickness(style.Padding, (edge, value) => flexNode.StyleSetPadding(edge, value), (edge, value) => flexNode.StyleSetPaddingPercent(edge, value));
        ApplyBorder(style.BorderWidth, flexNode);

        if (isRoot)
        {
            if (style.Width.IsAuto)
            {
                flexNode.StyleSetWidth(rootWidth);
            }

            if (style.Height.IsAuto)
            {
                flexNode.StyleSetHeight(rootHeight);
            }
        }
    }

    private static void ApplyThickness(UiThickness thickness, Action<FlexEdge, float> pointSetter, Action<FlexEdge, float> percentSetter)
    {
        SetThicknessEdge(FlexEdge.Left, thickness.Left, pointSetter, percentSetter);
        SetThicknessEdge(FlexEdge.Top, thickness.Top, pointSetter, percentSetter);
        SetThicknessEdge(FlexEdge.Right, thickness.Right, pointSetter, percentSetter);
        SetThicknessEdge(FlexEdge.Bottom, thickness.Bottom, pointSetter, percentSetter);
    }

    private static void SetThicknessEdge(FlexEdge edge, float value, Action<FlexEdge, float> pointSetter, Action<FlexEdge, float> percentSetter)
    {
        pointSetter(edge, value);
    }

    private static void ApplyBorder(float borderWidth, FlexNode node)
    {
        node.StyleSetBorder(FlexEdge.Left, borderWidth);
        node.StyleSetBorder(FlexEdge.Top, borderWidth);
        node.StyleSetBorder(FlexEdge.Right, borderWidth);
        node.StyleSetBorder(FlexEdge.Bottom, borderWidth);
    }

    private static void ApplyLength(UiLength length, Action<float> pointSetter, Action<float> percentSetter, Action? autoSetter)
    {
        switch (length.Unit)
        {
            case UiLengthUnit.Pixel:
                pointSetter(length.Value);
                break;
            case UiLengthUnit.Percent:
                percentSetter(length.Value);
                break;
            default:
                autoSetter?.Invoke();
                break;
        }
    }

    private static FlexOverflow MapOverflow(UiStyle style)
    {
        if (style.ClipContent)
        {
            return FlexOverflow.Hidden;
        }

        return style.Overflow switch
        {
            UiOverflow.Hidden or UiOverflow.Clip => FlexOverflow.Hidden,
            UiOverflow.Scroll => FlexOverflow.Scroll,
            _ => FlexOverflow.Visible
        };
    }

    private static FlexJustify MapJustify(UiJustifyContent justifyContent)
    {
        return justifyContent switch
        {
            UiJustifyContent.Center => FlexJustify.Center,
            UiJustifyContent.End => FlexJustify.FlexEnd,
            UiJustifyContent.SpaceBetween => FlexJustify.SpaceBetween,
            UiJustifyContent.SpaceAround => FlexJustify.SpaceAround,
            UiJustifyContent.SpaceEvenly => FlexJustify.SpaceAround,
            _ => FlexJustify.FlexStart
        };
    }

    private static FlexAlign MapAlign(UiAlignItems alignItems)
    {
        return alignItems switch
        {
            UiAlignItems.Start => FlexAlign.FlexStart,
            UiAlignItems.Center => FlexAlign.Center,
            UiAlignItems.End => FlexAlign.FlexEnd,
            _ => FlexAlign.Stretch
        };
    }

    private static bool ShouldMeasureAsLeaf(UiNode node)
    {
        if (node.Kind == UiNodeKind.Text)
        {
            return true;
        }

        if (node.Children.Count > 0)
        {
            return false;
        }

        return node.Kind is UiNodeKind.Button
            or UiNodeKind.Input
            or UiNodeKind.Checkbox
            or UiNodeKind.Toggle
            or UiNodeKind.Slider
            or UiNodeKind.Select
            or UiNodeKind.TextArea
            or UiNodeKind.Image;
    }

    private static void ApplyGapOffset(FlexNode childNode, UiStyle parentStyle, int childIndex)
    {
        if (childIndex == 0 || parentStyle.Gap <= 0f)
        {
            return;
        }

        if (parentStyle.FlexDirection == UiFlexDirection.Row)
        {
            childNode.StyleSetMargin(FlexEdge.Left, childNode.StyleGetMargin(FlexEdge.Left).value + parentStyle.Gap);
            return;
        }

        childNode.StyleSetMargin(FlexEdge.Top, childNode.StyleGetMargin(FlexEdge.Top).value + parentStyle.Gap);
    }

    private void ApplyLayout(UiNode uiNode, FlexNode flexNode, float parentX, float parentY)
    {
        float x = parentX + flexNode.LayoutGetLeft();
        float y = parentY + flexNode.LayoutGetTop();
        float width = Math.Max(0f, flexNode.LayoutGetWidth());
        float height = Math.Max(0f, flexNode.LayoutGetHeight());
        uiNode.SetLayout(new UiRect(x, y, width, height));

        int childCount = Math.Min(uiNode.Children.Count, flexNode.ChildrenCount);
        for (int i = 0; i < childCount; i++)
        {
            ApplyLayout(uiNode.Children[i], flexNode.GetChild(i), x, y);
        }
    }

    private FlexSize MeasureNode(UiNode node, float width, FlexMeasureMode widthMode, float height, FlexMeasureMode heightMode)
    {
        UiStyle style = node.Style;
        string? text = node.TextContent;

        if (!string.IsNullOrWhiteSpace(text))
        {
            float availableTextWidth = widthMode == FlexMeasureMode.Undefined
                ? float.PositiveInfinity
                : Math.Max(0f, width - style.Padding.Horizontal);

            UiTextLayoutResult textLayout = UiTextLayout.Measure(text, style, availableTextWidth, widthMode != FlexMeasureMode.Undefined);
            float measuredWidth = textLayout.Width + style.Padding.Horizontal;
            float measuredHeight = textLayout.Height + style.Padding.Vertical;
            return new FlexSize(ResolveMeasuredAxis(measuredWidth, width, widthMode), ResolveMeasuredAxis(measuredHeight, height, heightMode));
        }

        (float intrinsicWidth, float intrinsicHeight) = node.Kind switch
        {
            UiNodeKind.Button => (140f, 40f),
            UiNodeKind.Image => (160f, 96f),
            UiNodeKind.Input or UiNodeKind.Select or UiNodeKind.TextArea => (220f, 40f),
            UiNodeKind.Checkbox or UiNodeKind.Toggle => (120f, 28f),
            UiNodeKind.Slider => (220f, 24f),
            _ => (0f, 0f)
        };

        return new FlexSize(
            ResolveMeasuredAxis(intrinsicWidth, width, widthMode),
            ResolveMeasuredAxis(intrinsicHeight, height, heightMode));
    }

    private static float ResolveMeasuredAxis(float measured, float available, FlexMeasureMode mode)
    {
        return mode switch
        {
            FlexMeasureMode.Exactly => available,
            FlexMeasureMode.AtMost => Math.Min(measured, available),
            _ => measured
        };
    }
}
