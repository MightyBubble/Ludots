using AngleSharp.Dom;
using ExCSS;
using FlexLayoutSharp;
using Ludots.UI.HtmlEngine.Core;
using System.Globalization;

namespace Ludots.UI.HtmlEngine.Core
{
    public class FlexBuilder
    {
        public static CssBox BuildTree(IElement rootElement)
        {
            var box = new CssBox(rootElement);
            
            foreach (var child in rootElement.Children)
            {
                var childBox = BuildTree(child);
                box.Children.Add(childBox);
                box.FlexNode.InsertChild(childBox.FlexNode, box.FlexNode.ChildrenCount);
            }
            
            return box;
        }

        public static void ApplyFlexStyles(CssBox box)
        {
            var style = box.ComputedStyle;
            var node = box.FlexNode;

            // Dimensions
            if (TryGetFloat(style, "width", out float width)) node.StyleSetWidth(width);
            if (TryGetFloat(style, "height", out float height)) node.StyleSetHeight(height);
            
            // Flex Props
            var display = style.Display;
            if (display == "flex")
            {
                // Default to flex
            }
            else if (display == "none")
            {
                node.StyleSetDisplay(FlexLayoutSharp.Display.None);
            }

            // Flex Direction
            var flexDirection = style.FlexDirection;
            if (flexDirection == "row") node.StyleSetFlexDirection(FlexLayoutSharp.FlexDirection.Row);
            else if (flexDirection == "column") node.StyleSetFlexDirection(FlexLayoutSharp.FlexDirection.Column);
            else if (flexDirection == "row-reverse") node.StyleSetFlexDirection(FlexLayoutSharp.FlexDirection.RowReverse);
            else if (flexDirection == "column-reverse") node.StyleSetFlexDirection(FlexLayoutSharp.FlexDirection.ColumnReverse);
            
            // Justify Content
            var justifyContent = style.JustifyContent;
            if (justifyContent == "center") node.StyleSetJustifyContent(Justify.Center);
            else if (justifyContent == "space-between") node.StyleSetJustifyContent(Justify.SpaceBetween);
            else if (justifyContent == "space-around") node.StyleSetJustifyContent(Justify.SpaceAround);
            // else if (justifyContent == "space-evenly") node.StyleSetJustifyContent(Justify.SpaceEvenly); // Not supported in this version of FlexLayoutSharp
            else if (justifyContent == "flex-start") node.StyleSetJustifyContent(Justify.FlexStart);
            else if (justifyContent == "flex-end") node.StyleSetJustifyContent(Justify.FlexEnd);

            // Align Items
            var alignItems = style.AlignItems;
            if (alignItems == "center") node.StyleSetAlignItems(Align.Center);
            else if (alignItems == "stretch") node.StyleSetAlignItems(Align.Stretch);
            else if (alignItems == "flex-start") node.StyleSetAlignItems(Align.FlexStart);
            else if (alignItems == "flex-end") node.StyleSetAlignItems(Align.FlexEnd);
            else if (alignItems == "baseline") node.StyleSetAlignItems(Align.Baseline);

            // Align Self
            var alignSelf = style.AlignSelf;
            if (alignSelf == "center") node.StyleSetAlignSelf(Align.Center);
            else if (alignSelf == "stretch") node.StyleSetAlignSelf(Align.Stretch);
            else if (alignSelf == "flex-start") node.StyleSetAlignSelf(Align.FlexStart);
            else if (alignSelf == "flex-end") node.StyleSetAlignSelf(Align.FlexEnd);
            else if (alignSelf == "baseline") node.StyleSetAlignSelf(Align.Baseline);
            else if (alignSelf == "auto") node.StyleSetAlignSelf(Align.Auto);

            // Flex Wrap
            var flexWrap = style.FlexWrap;
            if (flexWrap == "wrap") node.StyleSetFlexWrap(Wrap.Wrap);
            else if (flexWrap == "wrap-reverse") node.StyleSetFlexWrap(Wrap.WrapReverse);
            else if (flexWrap == "nowrap") node.StyleSetFlexWrap(Wrap.NoWrap);

            // Flex Grow/Shrink/Basis
            if (TryGetFloat(style, "flex-grow", out float flexGrow)) node.StyleSetFlexGrow(flexGrow);
            if (TryGetFloat(style, "flex-shrink", out float flexShrink)) node.StyleSetFlexShrink(flexShrink);
            if (TryGetFloat(style, "flex-basis", out float flexBasis)) node.StyleSetFlexBasis(flexBasis);

            // Position Type
            var position = style.Position;
            if (position == "absolute") node.StyleSetPositionType(PositionType.Absolute);
            else node.StyleSetPositionType(PositionType.Relative);

            // Position Offsets
            if (TryGetFloat(style, "top", out float top)) node.StyleSetPosition(Edge.Top, top);
            if (TryGetFloat(style, "bottom", out float bottom)) node.StyleSetPosition(Edge.Bottom, bottom);
            if (TryGetFloat(style, "left", out float left)) node.StyleSetPosition(Edge.Left, left);
            if (TryGetFloat(style, "right", out float right)) node.StyleSetPosition(Edge.Right, right);

            // Min/Max Dimensions
            if (TryGetFloat(style, "min-width", out float minWidth)) node.StyleSetMinWidth(minWidth);
            if (TryGetFloat(style, "max-width", out float maxWidth)) node.StyleSetMaxWidth(maxWidth);
            if (TryGetFloat(style, "min-height", out float minHeight)) node.StyleSetMinHeight(minHeight);
            if (TryGetFloat(style, "max-height", out float maxHeight)) node.StyleSetMaxHeight(maxHeight);

            // Margins
            if (TryGetFloat(style, "margin", out float margin)) node.StyleSetMargin(Edge.All, margin);
            if (TryGetFloat(style, "margin-left", out float ml)) node.StyleSetMargin(Edge.Left, ml);
            if (TryGetFloat(style, "margin-top", out float mt)) node.StyleSetMargin(Edge.Top, mt);
            if (TryGetFloat(style, "margin-right", out float mr)) node.StyleSetMargin(Edge.Right, mr);
            if (TryGetFloat(style, "margin-bottom", out float mb)) node.StyleSetMargin(Edge.Bottom, mb);

            // Padding
            if (TryGetFloat(style, "padding", out float padding)) node.StyleSetPadding(Edge.All, padding);
            
            // Recursively apply
            foreach (var child in box.Children)
            {
                ApplyFlexStyles(child);
            }
        }

        private static bool TryGetFloat(StyleDeclaration style, string propertyName, out float value)
        {
            value = 0;
            var valStr = style[propertyName];
            if (string.IsNullOrEmpty(valStr)) return false;
            
            if (valStr.EndsWith("px")) valStr = valStr.Substring(0, valStr.Length - 2);
            if (valStr.EndsWith("%")) return false; // Percentage not handled in this simple demo yet

            return float.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }
    }
}
