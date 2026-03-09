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
using FlexWrap = FlexLayoutSharp.Wrap;

namespace Ludots.UI.Runtime;

public sealed class UiLayoutEngine
{
    public void Layout(UiNode root, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(root);

        FlexNode flexRoot = BuildFlexTree(root, isRoot: true, rootWidth: width, rootHeight: height);
        flexRoot.CalculateLayout(width, height, Direction.LTR);
        ApplyLayout(root, flexRoot, 0f, 0f);
        NormalizeTableLayouts(root);
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
        flexNode.StyleSetAlignContent(MapAlignContent(style.AlignContent));
        flexNode.StyleSetFlexWrap(MapWrap(style.FlexWrap));
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

    private static FlexAlign MapAlignContent(UiAlignContent alignContent)
    {
        return alignContent switch
        {
            UiAlignContent.Start => FlexAlign.FlexStart,
            UiAlignContent.Center => FlexAlign.Center,
            UiAlignContent.End => FlexAlign.FlexEnd,
            UiAlignContent.SpaceBetween => FlexAlign.SpaceBetween,
            UiAlignContent.SpaceAround or UiAlignContent.SpaceEvenly => FlexAlign.SpaceAround,
            _ => FlexAlign.Stretch
        };
    }

    private static FlexWrap MapWrap(UiFlexWrap wrap)
    {
        return wrap switch
        {
            UiFlexWrap.Wrap => FlexWrap.Wrap,
            UiFlexWrap.WrapReverse => FlexWrap.WrapReverse,
            _ => FlexWrap.NoWrap
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

        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            return true;
        }

        return node.Kind is UiNodeKind.Button
            or UiNodeKind.Input
            or UiNodeKind.Checkbox
            or UiNodeKind.Radio
            or UiNodeKind.Toggle
            or UiNodeKind.Slider
            or UiNodeKind.Select
            or UiNodeKind.TextArea
            or UiNodeKind.Image;
    }

    private static void ApplyGapOffset(FlexNode childNode, UiStyle parentStyle, int childIndex)
    {
        float gap = GetMainAxisGap(parentStyle);
        if (childIndex == 0 || gap <= 0f)
        {
            return;
        }

        if (parentStyle.FlexDirection == UiFlexDirection.Row)
        {
            childNode.StyleSetMargin(FlexEdge.Left, childNode.StyleGetMargin(FlexEdge.Left).value + gap);
            return;
        }

        childNode.StyleSetMargin(FlexEdge.Top, childNode.StyleGetMargin(FlexEdge.Top).value + gap);
    }

    private static float GetMainAxisGap(UiStyle parentStyle)
    {
        return parentStyle.FlexDirection == UiFlexDirection.Row
            ? parentStyle.ColumnGap > 0f ? parentStyle.ColumnGap : parentStyle.Gap
            : parentStyle.RowGap > 0f ? parentStyle.RowGap : parentStyle.Gap;
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

        float contentWidth = width;
        float contentHeight = height;
        for (int i = 0; i < childCount; i++)
        {
            UiRect childRect = uiNode.Children[i].LayoutRect;
            contentWidth = Math.Max(contentWidth, Math.Max(0f, childRect.Right - x));
            contentHeight = Math.Max(contentHeight, Math.Max(0f, childRect.Bottom - y));
        }

        uiNode.SetScrollMetrics(contentWidth, contentHeight);
    }

    private void NormalizeTableLayouts(UiNode node)
    {
        if (node.Kind == UiNodeKind.Table)
        {
            NormalizeTableLayout(node);
        }

        foreach (UiNode child in node.Children)
        {
            NormalizeTableLayouts(child);
        }
    }

    private void NormalizeTableLayout(UiNode table)
    {
        List<(UiNode? Section, List<UiNode> Rows)> rowGroups = CollectTableRowGroups(table);
        if (rowGroups.Count == 0)
        {
            return;
        }

        (List<TableRowInfo> rowInfos, List<TableCellPlacement> placements, int columnCount) = BuildTableLayoutModel(rowGroups);
        if (rowInfos.Count == 0 || placements.Count == 0 || columnCount == 0)
        {
            return;
        }

        float availableWidth = Math.Max(0f, table.LayoutRect.Width - table.Style.Padding.Horizontal);
        if (availableWidth <= 0.01f)
        {
            return;
        }

        float[] columnWidths = new float[columnCount];
        foreach (TableCellPlacement placement in placements.OrderBy(static placement => placement.ColumnSpan))
        {
            float preferredWidth = MeasureTableCellPreferredWidth(placement.Cell);
            float currentWidth = SumTableRange(columnWidths, placement.ColumnIndex, placement.ColumnSpan);
            if (preferredWidth > currentWidth + 0.01f)
            {
                float additionalWidth = (preferredWidth - currentWidth) / placement.ColumnSpan;
                for (int i = 0; i < placement.ColumnSpan; i++)
                {
                    columnWidths[placement.ColumnIndex + i] += additionalWidth;
                }
            }
        }

        FitTableColumns(columnWidths, availableWidth);

        float[] rowHeights = new float[rowInfos.Count];
        foreach (TableRowInfo rowInfo in rowInfos)
        {
            rowHeights[rowInfo.RowIndex] = Math.Max(24f, rowInfo.Row.LayoutRect.Height);
        }

        foreach (TableCellPlacement placement in placements.OrderBy(static placement => placement.RowSpan))
        {
            float cellWidth = SumTableRange(columnWidths, placement.ColumnIndex, placement.ColumnSpan);
            FlexSize measured = MeasureNode(placement.Cell, cellWidth, FlexMeasureMode.AtMost, 0f, FlexMeasureMode.Undefined);
            float preferredHeight = Math.Max(24f, Math.Max(placement.Cell.LayoutRect.Height, measured.Height));
            float currentHeight = SumTableRange(rowHeights, placement.RowIndex, placement.RowSpan);
            if (preferredHeight > currentHeight + 0.01f)
            {
                float additionalHeight = (preferredHeight - currentHeight) / placement.RowSpan;
                for (int i = 0; i < placement.RowSpan; i++)
                {
                    rowHeights[placement.RowIndex + i] += additionalHeight;
                }
            }
        }

        float innerX = table.LayoutRect.X + table.Style.Padding.Left;
        float currentY = table.LayoutRect.Y + table.Style.Padding.Top;
        float[] rowTops = new float[rowInfos.Count];
        foreach (TableRowInfo rowInfo in rowInfos)
        {
            rowTops[rowInfo.RowIndex] = currentY;
            float rowHeight = rowHeights[rowInfo.RowIndex];
            rowInfo.Row.SetLayout(new UiRect(innerX, currentY, availableWidth, rowHeight));
            rowInfo.Row.SetScrollMetrics(availableWidth, rowHeight);
            currentY += rowHeight;
        }

        foreach (TableCellPlacement placement in placements)
        {
            float cellX = innerX + SumTableRange(columnWidths, 0, placement.ColumnIndex);
            float cellY = rowTops[placement.RowIndex];
            float cellWidth = SumTableRange(columnWidths, placement.ColumnIndex, placement.ColumnSpan);
            float cellHeight = SumTableRange(rowHeights, placement.RowIndex, placement.RowSpan);
            placement.Cell.SetLayout(new UiRect(cellX, cellY, cellWidth, cellHeight));
            placement.Cell.SetScrollMetrics(cellWidth, cellHeight);
        }

        Dictionary<UiNode, TableRowInfo> rowLookup = rowInfos.ToDictionary(static info => info.Row);
        foreach ((UiNode? section, List<UiNode> rows) in rowGroups)
        {
            if (section == null || rows.Count == 0)
            {
                continue;
            }

            TableRowInfo firstRow = rowLookup[rows[0]];
            TableRowInfo lastRow = rowLookup[rows[^1]];
            float sectionTop = rowTops[firstRow.RowIndex];
            float sectionHeight = SumTableRange(rowHeights, firstRow.RowIndex, (lastRow.RowIndex - firstRow.RowIndex) + 1);
            section.SetLayout(new UiRect(innerX, sectionTop, availableWidth, sectionHeight));
            section.SetScrollMetrics(availableWidth, sectionHeight);
        }

        float contentHeight = Math.Max(table.LayoutRect.Height, (currentY - table.LayoutRect.Y) + table.Style.Padding.Bottom);
        table.SetScrollMetrics(table.LayoutRect.Width, contentHeight);
    }

    private static (List<TableRowInfo> RowInfos, List<TableCellPlacement> Placements, int ColumnCount) BuildTableLayoutModel(List<(UiNode? Section, List<UiNode> Rows)> rowGroups)
    {
        List<TableRowInfo> rowInfos = new();
        foreach ((UiNode? section, List<UiNode> rows) in rowGroups)
        {
            foreach (UiNode row in rows)
            {
                rowInfos.Add(new TableRowInfo(section, row, rowInfos.Count));
            }
        }

        List<TableCellPlacement> placements = new();
        List<int> occupiedColumns = new();
        bool firstRow = true;
        foreach (TableRowInfo rowInfo in rowInfos)
        {
            if (!firstRow)
            {
                AdvanceTableRowOccupancy(occupiedColumns);
            }

            firstRow = false;
            int searchColumn = 0;
            foreach (UiNode cell in GetTableCells(rowInfo.Row))
            {
                int columnSpan = GetTableSpan(cell.Attributes["colspan"]);
                int requestedRowSpan = GetTableSpan(cell.Attributes["rowspan"]);
                int rowSpan = Math.Max(1, Math.Min(requestedRowSpan, rowInfos.Count - rowInfo.RowIndex));
                int columnIndex = FindAvailableTableColumn(occupiedColumns, searchColumn, columnSpan);
                EnsureTableCapacity(occupiedColumns, columnIndex + columnSpan);
                for (int i = 0; i < columnSpan; i++)
                {
                    occupiedColumns[columnIndex + i] = Math.Max(occupiedColumns[columnIndex + i], rowSpan);
                }

                placements.Add(new TableCellPlacement(cell, rowInfo.Row, rowInfo.RowIndex, columnIndex, columnSpan, rowSpan));
                searchColumn = columnIndex + columnSpan;
            }
        }

        int columnCount = placements.Count == 0 ? 0 : placements.Max(static placement => placement.ColumnIndex + placement.ColumnSpan);
        return (rowInfos, placements, columnCount);
    }

    private static List<(UiNode? Section, List<UiNode> Rows)> CollectTableRowGroups(UiNode table)
    {
        List<(UiNode? Section, List<UiNode> Rows)> groups = new();
        List<UiNode> directRows = new();

        foreach (UiNode child in table.Children)
        {
            if (child.Kind == UiNodeKind.TableRow)
            {
                directRows.Add(child);
                continue;
            }

            if (child.Kind is UiNodeKind.TableHeader or UiNodeKind.TableBody or UiNodeKind.TableFooter)
            {
                List<UiNode> sectionRows = child.Children.Where(static node => node.Kind == UiNodeKind.TableRow).ToList();
                if (sectionRows.Count > 0)
                {
                    groups.Add((child, sectionRows));
                }
            }
        }

        if (directRows.Count > 0)
        {
            groups.Insert(0, (null, directRows));
        }

        return groups;
    }

    private static IReadOnlyList<UiNode> GetTableCells(UiNode row)
    {
        return row.Children.Where(static child => child.Kind is UiNodeKind.TableCell or UiNodeKind.TableHeaderCell).ToArray();
    }

    private static void AdvanceTableRowOccupancy(List<int> occupiedColumns)
    {
        for (int i = 0; i < occupiedColumns.Count; i++)
        {
            if (occupiedColumns[i] > 0)
            {
                occupiedColumns[i]--;
            }
        }
    }

    private static int FindAvailableTableColumn(List<int> occupiedColumns, int startColumn, int columnSpan)
    {
        int columnIndex = Math.Max(0, startColumn);
        while (true)
        {
            EnsureTableCapacity(occupiedColumns, columnIndex + columnSpan);
            bool isAvailable = true;
            for (int i = 0; i < columnSpan; i++)
            {
                if (occupiedColumns[columnIndex + i] > 0)
                {
                    columnIndex += i + 1;
                    isAvailable = false;
                    break;
                }
            }

            if (isAvailable)
            {
                return columnIndex;
            }
        }
    }

    private static void EnsureTableCapacity(List<int> occupiedColumns, int count)
    {
        while (occupiedColumns.Count < count)
        {
            occupiedColumns.Add(0);
        }
    }

    private static int GetTableSpan(string? value)
    {
        return int.TryParse(value, out int parsed) && parsed > 1 ? parsed : 1;
    }

    private static float SumTableRange(float[] values, int start, int length)
    {
        float total = 0f;
        for (int i = 0; i < length && start + i < values.Length; i++)
        {
            total += values[start + i];
        }

        return total;
    }

    private float MeasureTableCellPreferredWidth(UiNode cell)
    {
        FlexSize measured = MeasureNode(cell, 0f, FlexMeasureMode.Undefined, 0f, FlexMeasureMode.Undefined);
        float preferredWidth = measured.Width;
        if (preferredWidth <= 0.01f)
        {
            preferredWidth = Math.Max(cell.LayoutRect.Width, 48f);
        }

        return Math.Max(48f, preferredWidth);
    }

    private float MeasureTableRowHeight(UiNode row, IReadOnlyList<UiNode> cells, float[] columnWidths)
    {
        float rowHeight = Math.Max(24f, row.LayoutRect.Height);
        for (int i = 0; i < cells.Count; i++)
        {
            float availableCellWidth = i < columnWidths.Length ? columnWidths[i] : 0f;
            FlexSize measured = MeasureNode(cells[i], availableCellWidth, FlexMeasureMode.AtMost, 0f, FlexMeasureMode.Undefined);
            rowHeight = Math.Max(rowHeight, Math.Max(cells[i].LayoutRect.Height, measured.Height));
        }

        return rowHeight;
    }

    private static void FitTableColumns(float[] columnWidths, float availableWidth)
    {
        if (columnWidths.Length == 0)
        {
            return;
        }

        float totalWidth = columnWidths.Sum();
        if (totalWidth <= 0.01f)
        {
            float equalWidth = availableWidth / columnWidths.Length;
            for (int i = 0; i < columnWidths.Length; i++)
            {
                columnWidths[i] = equalWidth;
            }

            return;
        }

        if (totalWidth < availableWidth)
        {
            float extraWidth = (availableWidth - totalWidth) / columnWidths.Length;
            for (int i = 0; i < columnWidths.Length; i++)
            {
                columnWidths[i] += extraWidth;
            }

            return;
        }

        float scale = availableWidth / totalWidth;
        for (int i = 0; i < columnWidths.Length; i++)
        {
            columnWidths[i] = Math.Max(36f, columnWidths[i] * scale);
        }

        float adjustedTotal = columnWidths.Sum();
        if (columnWidths.Length > 0)
        {
            columnWidths[^1] += availableWidth - adjustedTotal;
        }
    }

    private sealed class TableRowInfo
    {
        public TableRowInfo(UiNode? section, UiNode row, int rowIndex)
        {
            Section = section;
            Row = row;
            RowIndex = rowIndex;
        }

        public UiNode? Section { get; }

        public UiNode Row { get; }

        public int RowIndex { get; }
    }

    private sealed class TableCellPlacement
    {
        public TableCellPlacement(UiNode cell, UiNode row, int rowIndex, int columnIndex, int columnSpan, int rowSpan)
        {
            Cell = cell;
            Row = row;
            RowIndex = rowIndex;
            ColumnIndex = columnIndex;
            ColumnSpan = columnSpan;
            RowSpan = rowSpan;
        }

        public UiNode Cell { get; }

        public UiNode Row { get; }

        public int RowIndex { get; }

        public int ColumnIndex { get; }

        public int ColumnSpan { get; }

        public int RowSpan { get; }
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
            UiNodeKind.Image => ResolveImageIntrinsicSize(node),
            UiNodeKind.Input or UiNodeKind.Select or UiNodeKind.TextArea => (220f, 40f),
            UiNodeKind.Checkbox or UiNodeKind.Radio or UiNodeKind.Toggle => (120f, 28f),
            UiNodeKind.Slider => (220f, 24f),
            _ when string.Equals(node.TagName, "canvas", StringComparison.OrdinalIgnoreCase) => ResolveCanvasIntrinsicSize(node),
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

    private static (float Width, float Height) ResolveImageIntrinsicSize(UiNode node)
    {
        if (UiImageSourceCache.TryGetSize(node.Attributes["src"], out float width, out float height))
        {
            return (width, height);
        }

        return (160f, 96f);
    }

    private static (float Width, float Height) ResolveCanvasIntrinsicSize(UiNode node)
    {
        float width = TryParseDimension(node.Attributes["width"], 300f);
        float height = TryParseDimension(node.Attributes["height"], 150f);
        return (width, height);
    }

    private static float TryParseDimension(string? value, float fallback)
    {
        return float.TryParse(value, out float parsed) && parsed > 0.01f
            ? parsed
            : fallback;
    }
}
