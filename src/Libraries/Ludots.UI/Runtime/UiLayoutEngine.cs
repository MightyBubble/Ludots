using System;
using System.Collections.Generic;
using FlexLayoutSharp;

namespace Ludots.UI.Runtime;

public sealed class UiLayoutEngine
{
	private enum LengthTarget : byte
	{
		Width,
		Height,
		MinWidth,
		MinHeight,
		MaxWidth,
		MaxHeight,
		FlexBasis,
		Left,
		Top,
		Right,
		Bottom
	}

	private readonly IUiTextMeasurer _textMeasurer;
	private readonly IUiImageSizeProvider _imageSizeProvider;
	private readonly UiLayoutScratch _scratch = new UiLayoutScratch();
	private readonly UiFlexNodePool _flexPool = new UiFlexNodePool();
	private readonly MeasureFunc _leafMeasureFunc;
	private readonly MeasureFunc _inlineMeasureFunc;

	public UiLayoutEngine(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		_textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
		_imageSizeProvider = imageSizeProvider ?? throw new ArgumentNullException(nameof(imageSizeProvider));
		_leafMeasureFunc = MeasureLeafNode;
		_inlineMeasureFunc = MeasureInlineNode;
	}

	private sealed class TableRowInfo
	{
		public UiNode? Section { get; }
		public UiNode Row { get; }
		public int RowIndex { get; }
		public TableRowInfo(UiNode? section, UiNode row, int rowIndex)
		{
			Section = section;
			Row = row;
			RowIndex = rowIndex;
		}
	}

	private sealed class TableCellPlacement
	{
		public UiNode Cell { get; }
		public UiNode Row { get; }
		public int RowIndex { get; }
		public int ColumnIndex { get; }
		public int ColumnSpan { get; }
		public int RowSpan { get; }
		public TableCellPlacement(UiNode cell, UiNode row, int rowIndex, int columnIndex, int columnSpan, int rowSpan)
		{
			Cell = cell;
			Row = row;
			RowIndex = rowIndex;
			ColumnIndex = columnIndex;
			ColumnSpan = columnSpan;
			RowSpan = rowSpan;
		}
	}

	public void Layout(UiNode root, float width, float height)
	{
		ArgumentNullException.ThrowIfNull(root, "root");
		List<Node> deferredCalcNodes = _scratch.BeginDeferredCalc();
		Node node = BuildFlexTree(root, isRoot: true, width, height, deferredCalcNodes);
		node.CalculateLayout(width, height, Direction.LTR);
		if (deferredCalcNodes.Count > 0)
		{
			for (int i = 0; i < deferredCalcNodes.Count; i++)
			{
				ResolveDeferredCalcLengths(deferredCalcNodes[i], width, height);
			}
			node.CalculateLayout(width, height, Direction.LTR);
		}
		ApplyLayout(root, node, 0f, 0f);
		NormalizeTableLayouts(root);
		UiGridLayoutEngine.LayoutSubtree(root, this, _scratch);
		UiInlineFlowEngine.LayoutSubtree(root, _textMeasurer, _imageSizeProvider, _scratch);
		_flexPool.ReleaseAll();
	}

	internal void LayoutNestedContent(UiNode node)
	{
		if (node.Children.Count == 0 || node.Style.Display == UiDisplay.Grid || UiInlineFlowEngine.IsInlineFormattingContext(node))
		{
			return;
		}
		List<Node> deferredCalcNodes = _scratch.BeginDeferredCalc();
		float width = node.LayoutRect.Width;
		float height = node.LayoutRect.Height;
		Node flexRoot = _flexPool.Rent();
		flexRoot.Context = node;
		ConfigureNodeStyle(flexRoot, node, isRoot: true, width, height, deferredCalcNodes);
		flexRoot.StyleSetWidth(width);
		flexRoot.StyleSetHeight(height);
		for (int i = 0; i < node.Children.Count; i++)
		{
			Node child = BuildFlexTree(node.Children[i], isRoot: false, width, height, deferredCalcNodes);
			ApplyGapOffset(child, node.Style, i);
			flexRoot.AddChild(child);
		}
		flexRoot.CalculateLayout(width, height, Direction.LTR);
		if (deferredCalcNodes.Count > 0)
		{
			for (int i = 0; i < deferredCalcNodes.Count; i++)
			{
				ResolveDeferredCalcLengths(deferredCalcNodes[i], width, height);
			}
			flexRoot.CalculateLayout(width, height, Direction.LTR);
		}
		int count = Math.Min(node.Children.Count, flexRoot.ChildrenCount);
		for (int i = 0; i < count; i++)
		{
			ApplyLayout(node.Children[i], flexRoot.GetChild(i), node.LayoutRect.X, node.LayoutRect.Y);
		}
	}

	private Node BuildFlexTree(UiNode node, bool isRoot, float rootWidth, float rootHeight, List<Node> deferredCalcNodes)
	{
		Node flexNode = _flexPool.Rent();
		flexNode.Context = node;
		ConfigureNodeStyle(flexNode, node, isRoot, rootWidth, rootHeight, deferredCalcNodes);
		if (node.Style.Display == UiDisplay.Grid)
		{
			return flexNode;
		}
		if (UiInlineFlowEngine.IsInlineFormattingContext(node))
		{
			flexNode.SetMeasureFunc(_inlineMeasureFunc);
			return flexNode;
		}
		if (ShouldMeasureAsLeaf(node))
		{
			flexNode.SetMeasureFunc(_leafMeasureFunc);
			return flexNode;
		}
		for (int i = 0; i < node.Children.Count; i++)
		{
			Node child = BuildFlexTree(node.Children[i], isRoot: false, rootWidth, rootHeight, deferredCalcNodes);
			ApplyGapOffset(child, node.Style, i);
			flexNode.AddChild(child);
		}
		return flexNode;
	}

	private Size MeasureLeafNode(Node flexNode, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
	{
		return flexNode.Context is UiNode node
			? MeasureNode(node, width, widthMode, height, heightMode)
			: default;
	}

	private Size MeasureInlineNode(Node flexNode, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
	{
		return flexNode.Context is UiNode node
			? UiInlineFlowEngine.Measure(node, _textMeasurer, _imageSizeProvider, width, widthMode, height, heightMode, _scratch)
			: default;
	}

	private void ConfigureNodeStyle(Node flexNode, UiNode node, bool isRoot, float rootWidth, float rootHeight, List<Node> deferredCalcNodes)
	{
		UiStyle style = node.Style;
		bool visible = style.Visible && style.Display != UiDisplay.None;
		flexNode.StyleSetDisplay(visible ? Display.Flex : Display.None);
		UiFlexDirection flexDirection = style.Display == UiDisplay.Block ? UiFlexDirection.Column : style.FlexDirection;
		flexNode.StyleSetFlexDirection(flexDirection == UiFlexDirection.Row ? FlexDirection.Row : FlexDirection.Column);
		flexNode.StyleSetJustifyContent(MapJustify(style.JustifyContent));
		flexNode.StyleSetAlignItems(MapAlign(style.AlignItems));
		flexNode.StyleSetAlignContent(MapAlignContent(style.AlignContent));
		flexNode.StyleSetFlexWrap(MapWrap(style.FlexWrap));
		flexNode.StyleSetOverflow(MapOverflow(style));
		flexNode.StyleSetPositionType(style.PositionType == UiPositionType.Absolute ? PositionType.Absolute : PositionType.Relative);
		flexNode.StyleSetFlexGrow(style.FlexGrow);
		flexNode.StyleSetFlexShrink(style.FlexShrink);
		UiLengthContext horizontalContext = new UiLengthContext(isRoot ? rootWidth : 0f, rootWidth, rootHeight);
		UiLengthContext verticalContext = new UiLengthContext(isRoot ? rootHeight : 0f, rootWidth, rootHeight);
		if (ApplyNodeLength(flexNode, style.Width, horizontalContext, isRoot, LengthTarget.Width)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.Height, verticalContext, isRoot, LengthTarget.Height)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.MinWidth, horizontalContext, isRoot, LengthTarget.MinWidth)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.MinHeight, verticalContext, isRoot, LengthTarget.MinHeight)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.MaxWidth, horizontalContext, isRoot, LengthTarget.MaxWidth)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.MaxHeight, verticalContext, isRoot, LengthTarget.MaxHeight)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.FlexBasis, horizontalContext, isRoot, LengthTarget.FlexBasis)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.Left, horizontalContext, isRoot, LengthTarget.Left)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.Top, verticalContext, isRoot, LengthTarget.Top)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.Right, horizontalContext, isRoot, LengthTarget.Right)) deferredCalcNodes.Add(flexNode);
		if (ApplyNodeLength(flexNode, style.Bottom, verticalContext, isRoot, LengthTarget.Bottom)) deferredCalcNodes.Add(flexNode);
		ApplyThicknessPoints(flexNode, style.Margin, isPadding: false);
		ApplyThicknessPoints(flexNode, style.Padding, isPadding: true);
		ApplyBorder(style.BorderWidth, flexNode);
		if (isRoot)
		{
			if (style.Width.IsAuto) flexNode.StyleSetWidth(rootWidth);
			if (style.Height.IsAuto) flexNode.StyleSetHeight(rootHeight);
		}
	}

	private static void ApplyThicknessPoints(Node node, UiThickness thickness, bool isPadding)
	{
		if (isPadding)
		{
			node.StyleSetPadding(Edge.Left, thickness.Left);
			node.StyleSetPadding(Edge.Top, thickness.Top);
			node.StyleSetPadding(Edge.Right, thickness.Right);
			node.StyleSetPadding(Edge.Bottom, thickness.Bottom);
		}
		else
		{
			node.StyleSetMargin(Edge.Left, thickness.Left);
			node.StyleSetMargin(Edge.Top, thickness.Top);
			node.StyleSetMargin(Edge.Right, thickness.Right);
			node.StyleSetMargin(Edge.Bottom, thickness.Bottom);
		}
	}

	private static void ApplyBorder(float borderWidth, Node node)
	{
		node.StyleSetBorder(Edge.Left, borderWidth);
		node.StyleSetBorder(Edge.Top, borderWidth);
		node.StyleSetBorder(Edge.Right, borderWidth);
		node.StyleSetBorder(Edge.Bottom, borderWidth);
	}

	private static bool ApplyNodeLength(Node flexNode, UiLength length, UiLengthContext context, bool isRoot, LengthTarget target)
	{
		switch (length.Unit)
		{
		case UiLengthUnit.Pixel:
			SetPoint(flexNode, target, length.Value);
			return false;
		case UiLengthUnit.Percent:
			SetPercent(flexNode, target, length.Value);
			return false;
		case UiLengthUnit.Vw:
		case UiLengthUnit.Vh:
		case UiLengthUnit.Vmin:
		case UiLengthUnit.Vmax:
			SetPoint(flexNode, target, length.Resolve(context));
			return false;
		case UiLengthUnit.Calc:
			if (length.Calc == null)
			{
				SetAuto(flexNode, target);
				return false;
			}
			if (length.Calc.HasPercentTerm && !isRoot)
			{
				return true;
			}
			float resolved = length.Calc.Evaluate(context);
			if (float.IsNaN(resolved))
			{
				SetAuto(flexNode, target);
				return false;
			}
			SetPoint(flexNode, target, resolved);
			return false;
		default:
			SetAuto(flexNode, target);
			return false;
		}
	}

	private static void SetPoint(Node node, LengthTarget target, float value)
	{
		switch (target)
		{
		case LengthTarget.Width: node.StyleSetWidth(value); break;
		case LengthTarget.Height: node.StyleSetHeight(value); break;
		case LengthTarget.MinWidth: node.StyleSetMinWidth(value); break;
		case LengthTarget.MinHeight: node.StyleSetMinHeight(value); break;
		case LengthTarget.MaxWidth: node.StyleSetMaxWidth(value); break;
		case LengthTarget.MaxHeight: node.StyleSetMaxHeight(value); break;
		case LengthTarget.FlexBasis: node.StyleSetFlexBasis(value); break;
		case LengthTarget.Left: node.StyleSetPosition(Edge.Left, value); break;
		case LengthTarget.Top: node.StyleSetPosition(Edge.Top, value); break;
		case LengthTarget.Right: node.StyleSetPosition(Edge.Right, value); break;
		case LengthTarget.Bottom: node.StyleSetPosition(Edge.Bottom, value); break;
		}
	}

	private static void SetPercent(Node node, LengthTarget target, float value)
	{
		switch (target)
		{
		case LengthTarget.Width: node.StyleSetWidthPercent(value); break;
		case LengthTarget.Height: node.StyleSetHeightPercent(value); break;
		case LengthTarget.MinWidth: node.StyleSetMinWidthPercent(value); break;
		case LengthTarget.MinHeight: node.StyleSetMinHeightPercent(value); break;
		case LengthTarget.MaxWidth: node.StyleSetMaxWidthPercent(value); break;
		case LengthTarget.MaxHeight: node.StyleSetMaxHeightPercent(value); break;
		case LengthTarget.FlexBasis: node.StyleSetFlexBasisPercent(value); break;
		case LengthTarget.Left: node.StyleSetPositionPercent(Edge.Left, value); break;
		case LengthTarget.Top: node.StyleSetPositionPercent(Edge.Top, value); break;
		case LengthTarget.Right: node.StyleSetPositionPercent(Edge.Right, value); break;
		case LengthTarget.Bottom: node.StyleSetPositionPercent(Edge.Bottom, value); break;
		}
	}

	private static void SetAuto(Node node, LengthTarget target)
	{
		switch (target)
		{
		case LengthTarget.Width: node.StyleSetWidthAuto(); break;
		case LengthTarget.Height: node.StyleSetHeightAuto(); break;
		case LengthTarget.FlexBasis: node.NodeStyleSetFlexBasisAuto(); break;
		}
	}

	private static void ResolveDeferredCalcLengths(Node flexNode, float rootWidth, float rootHeight)
	{
		if (flexNode.Context is not UiNode node)
		{
			return;
		}
		UiStyle style = node.Style;
		Node? parent = flexNode.GetParent();
		float parentContentWidth = parent == null ? rootWidth : Math.Max(0f, parent.LayoutGetWidth() - parent.LayoutGetPadding(Edge.Left) - parent.LayoutGetPadding(Edge.Right));
		float parentContentHeight = parent == null ? rootHeight : Math.Max(0f, parent.LayoutGetHeight() - parent.LayoutGetPadding(Edge.Top) - parent.LayoutGetPadding(Edge.Bottom));
		UiLengthContext horizontalContext = new UiLengthContext(parentContentWidth, rootWidth, rootHeight);
		UiLengthContext verticalContext = new UiLengthContext(parentContentHeight, rootWidth, rootHeight);
		ApplyDeferredCalc(flexNode, style.Width, horizontalContext, LengthTarget.Width);
		ApplyDeferredCalc(flexNode, style.Height, verticalContext, LengthTarget.Height);
		ApplyDeferredCalc(flexNode, style.MinWidth, horizontalContext, LengthTarget.MinWidth);
		ApplyDeferredCalc(flexNode, style.MinHeight, verticalContext, LengthTarget.MinHeight);
		ApplyDeferredCalc(flexNode, style.MaxWidth, horizontalContext, LengthTarget.MaxWidth);
		ApplyDeferredCalc(flexNode, style.MaxHeight, verticalContext, LengthTarget.MaxHeight);
		ApplyDeferredCalc(flexNode, style.FlexBasis, horizontalContext, LengthTarget.FlexBasis);
		ApplyDeferredCalc(flexNode, style.Left, horizontalContext, LengthTarget.Left);
		ApplyDeferredCalc(flexNode, style.Top, verticalContext, LengthTarget.Top);
		ApplyDeferredCalc(flexNode, style.Right, horizontalContext, LengthTarget.Right);
		ApplyDeferredCalc(flexNode, style.Bottom, verticalContext, LengthTarget.Bottom);
	}

	private static void ApplyDeferredCalc(Node flexNode, UiLength length, UiLengthContext context, LengthTarget target)
	{
		if (length.Unit != UiLengthUnit.Calc || length.Calc == null || !length.Calc.HasPercentTerm)
		{
			return;
		}
		float resolved = length.Calc.Evaluate(context);
		if (float.IsNaN(resolved))
		{
			SetAuto(flexNode, target);
			return;
		}
		SetPoint(flexNode, target, resolved);
	}

	private static Overflow MapOverflow(UiStyle style)
	{
		if (style.ClipContent)
		{
			return Overflow.Hidden;
		}
		UiOverflow overflow = style.Overflow;
		Overflow result;
		switch (overflow)
		{
		case UiOverflow.Hidden:
		case UiOverflow.Clip:
			result = Overflow.Hidden;
			break;
		case UiOverflow.Scroll:
			result = Overflow.Scroll;
			break;
		default:
			result = Overflow.Visible;
			break;
		}
		return result;
	}

	private static Justify MapJustify(UiJustifyContent justifyContent)
	{
		Justify result = justifyContent switch
		{
			UiJustifyContent.Center => Justify.Center, 
			UiJustifyContent.End => Justify.FlexEnd, 
			UiJustifyContent.SpaceBetween => Justify.SpaceBetween, 
			UiJustifyContent.SpaceAround => Justify.SpaceAround, 
			UiJustifyContent.SpaceEvenly => Justify.SpaceAround, 
			_ => Justify.FlexStart, 
		};
		return result;
	}

	private static Align MapAlign(UiAlignItems alignItems)
	{
		Align result = alignItems switch
		{
			UiAlignItems.Start => Align.FlexStart, 
			UiAlignItems.Center => Align.Center, 
			UiAlignItems.End => Align.FlexEnd, 
			_ => Align.Stretch, 
		};
		return result;
	}

	private static Align MapAlignContent(UiAlignContent alignContent)
	{
		Align result;
		switch (alignContent)
		{
		case UiAlignContent.Start:
			result = Align.FlexStart;
			break;
		case UiAlignContent.Center:
			result = Align.Center;
			break;
		case UiAlignContent.End:
			result = Align.FlexEnd;
			break;
		case UiAlignContent.SpaceBetween:
			result = Align.SpaceBetween;
			break;
		case UiAlignContent.SpaceAround:
		case UiAlignContent.SpaceEvenly:
			result = Align.SpaceAround;
			break;
		default:
			result = Align.Stretch;
			break;
		}
		return result;
	}

	private static Wrap MapWrap(UiFlexWrap wrap)
	{
		Wrap result = wrap switch
		{
			UiFlexWrap.Wrap => Wrap.Wrap, 
			UiFlexWrap.WrapReverse => Wrap.WrapReverse, 
			_ => Wrap.NoWrap, 
		};
		return result;
	}

	private static bool ShouldMeasureAsLeaf(UiNode node)
	{
		if (node.Children.Count > 0)
		{
			return false;
		}
		if (node.Kind == UiNodeKind.Text || !string.IsNullOrWhiteSpace(node.TextContent))
		{
			return true;
		}
		return node.Kind is UiNodeKind.Button or UiNodeKind.Image or UiNodeKind.Input or UiNodeKind.Select
			or UiNodeKind.TextArea or UiNodeKind.Checkbox or UiNodeKind.Radio or UiNodeKind.Toggle or UiNodeKind.Slider;
	}

	private static void ApplyGapOffset(Node childNode, UiStyle parentStyle, int childIndex)
	{
		float mainAxisGap = GetMainAxisGap(parentStyle);
		if (childIndex != 0 && !(mainAxisGap <= 0f))
		{
			if (parentStyle.FlexDirection == UiFlexDirection.Row)
			{
				childNode.StyleSetMargin(Edge.Left, childNode.StyleGetMargin(Edge.Left).value + mainAxisGap);
			}
			else
			{
				childNode.StyleSetMargin(Edge.Top, childNode.StyleGetMargin(Edge.Top).value + mainAxisGap);
			}
		}
	}

	private static float GetMainAxisGap(UiStyle parentStyle)
	{
		return (parentStyle.FlexDirection != UiFlexDirection.Row) ? ((parentStyle.RowGap > 0f) ? parentStyle.RowGap : parentStyle.Gap) : ((parentStyle.ColumnGap > 0f) ? parentStyle.ColumnGap : parentStyle.Gap);
	}


	private void ApplyLayout(UiNode uiNode, Node flexNode, float parentX, float parentY)
	{
		float num = parentX + flexNode.LayoutGetLeft();
		float num2 = parentY + flexNode.LayoutGetTop();
		float num3 = Math.Max(0f, flexNode.LayoutGetWidth());
		float num4 = Math.Max(0f, flexNode.LayoutGetHeight());
		uiNode.SetLayout(new UiRect(num, num2, num3, num4));
		int num5 = Math.Min(uiNode.Children.Count, flexNode.ChildrenCount);
		for (int i = 0; i < num5; i++)
		{
			ApplyLayout(uiNode.Children[i], flexNode.GetChild(i), num, num2);
		}
		float num6 = num3;
		float num7 = num4;
		for (int j = 0; j < num5; j++)
		{
			UiRect layoutRect = uiNode.Children[j].LayoutRect;
			num6 = Math.Max(num6, Math.Max(0f, layoutRect.Right - num));
			num7 = Math.Max(num7, Math.Max(0f, layoutRect.Bottom - num2));
		}
		uiNode.SetScrollMetrics(num6, num7);
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
		List<(UiNode, List<UiNode>)> list = CollectTableRowGroups(table);
		if (list.Count == 0)
		{
			return;
		}
		var (list2, list3, num) = BuildTableLayoutModel(list);
		if (list2.Count == 0 || list3.Count == 0 || num == 0)
		{
			return;
		}
		float num2 = Math.Max(0f, table.LayoutRect.Width - table.Style.Padding.Horizontal);
		if (num2 <= 0.01f)
		{
			return;
		}
		float[] array = new float[num];
		list3.Sort(static (left, right) => left.ColumnSpan.CompareTo(right.ColumnSpan));
		for (int i = 0; i < list3.Count; i++)
		{
			TableCellPlacement item = list3[i];
			float num3 = MeasureTableCellPreferredWidth(item.Cell);
			float num4 = SumTableRange(array, item.ColumnIndex, item.ColumnSpan);
			if (num3 > num4 + 0.01f)
			{
				float num5 = (num3 - num4) / (float)item.ColumnSpan;
				for (int num6 = 0; num6 < item.ColumnSpan; num6++)
				{
					array[item.ColumnIndex + num6] += num5;
				}
			}
		}
		FitTableColumns(array, num2);
		float[] array2 = new float[list2.Count];
		foreach (TableRowInfo item2 in list2)
		{
			array2[item2.RowIndex] = Math.Max(24f, item2.Row.LayoutRect.Height);
		}
		list3.Sort(static (left, right) => left.RowSpan.CompareTo(right.RowSpan));
		for (int i = 0; i < list3.Count; i++)
		{
			TableCellPlacement item3 = list3[i];
			float width = SumTableRange(array, item3.ColumnIndex, item3.ColumnSpan);
			Size size = MeasureNode(item3.Cell, width, MeasureMode.AtMost, 0f, MeasureMode.Undefined);
			float num7 = Math.Max(24f, Math.Max(item3.Cell.LayoutRect.Height, size.Height));
			float num8 = SumTableRange(array2, item3.RowIndex, item3.RowSpan);
			if (num7 > num8 + 0.01f)
			{
				float num9 = (num7 - num8) / (float)item3.RowSpan;
				for (int num10 = 0; num10 < item3.RowSpan; num10++)
				{
					array2[item3.RowIndex + num10] += num9;
				}
			}
		}
		float num11 = table.LayoutRect.X + table.Style.Padding.Left;
		float num12 = table.LayoutRect.Y + table.Style.Padding.Top;
		float[] array3 = new float[list2.Count];
		foreach (TableRowInfo item4 in list2)
		{
			array3[item4.RowIndex] = num12;
			float num13 = array2[item4.RowIndex];
			item4.Row.SetLayout(new UiRect(num11, num12, num2, num13));
			item4.Row.SetScrollMetrics(num2, num13);
			num12 += num13;
		}
		foreach (TableCellPlacement item5 in list3)
		{
			float x = num11 + SumTableRange(array, 0, item5.ColumnIndex);
			float y = array3[item5.RowIndex];
			float num14 = SumTableRange(array, item5.ColumnIndex, item5.ColumnSpan);
			float num15 = SumTableRange(array2, item5.RowIndex, item5.RowSpan);
			item5.Cell.SetLayout(new UiRect(x, y, num14, num15));
			item5.Cell.SetScrollMetrics(num14, num15);
		}
		foreach (var (uiNode, list4) in list)
		{
			if (uiNode != null && list4.Count != 0)
			{
				int firstRowIndex = -1;
				int lastRowIndex = -1;
				for (int i = 0; i < list2.Count; i++)
				{
					if (list2[i].Row == list4[0])
					{
						firstRowIndex = list2[i].RowIndex;
					}
					if (list2[i].Row == list4[list4.Count - 1])
					{
						lastRowIndex = list2[i].RowIndex;
					}
				}
				if (firstRowIndex < 0 || lastRowIndex < 0)
				{
					throw new InvalidOperationException("Table section rows are missing from the layout model.");
				}
				float y2 = array3[firstRowIndex];
				float num16 = SumTableRange(array2, firstRowIndex, lastRowIndex - firstRowIndex + 1);
				uiNode.SetLayout(new UiRect(num11, y2, num2, num16));
				uiNode.SetScrollMetrics(num2, num16);
			}
		}
		float contentHeight = Math.Max(table.LayoutRect.Height, num12 - table.LayoutRect.Y + table.Style.Padding.Bottom);
		table.SetScrollMetrics(table.LayoutRect.Width, contentHeight);
	}

	private static (List<TableRowInfo> RowInfos, List<TableCellPlacement> Placements, int ColumnCount) BuildTableLayoutModel(List<(UiNode? Section, List<UiNode> Rows)> rowGroups)
	{
		List<TableRowInfo> list = new List<TableRowInfo>();
		foreach (var (section, list2) in rowGroups)
		{
			foreach (UiNode item2 in list2)
			{
				list.Add(new TableRowInfo(section, item2, list.Count));
			}
		}
		List<TableCellPlacement> list3 = new List<TableCellPlacement>();
		List<int> list4 = new List<int>();
		List<UiNode> cells = new List<UiNode>();
		bool flag = true;
		foreach (TableRowInfo item3 in list)
		{
			if (!flag)
			{
				AdvanceTableRowOccupancy(list4);
			}
			flag = false;
			int startColumn = 0;
			CollectTableCells(item3.Row, cells);
			for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
			{
				UiNode tableCell = cells[cellIndex];
				int tableSpan = GetTableSpan(tableCell.Attributes["colspan"]);
				int tableSpan2 = GetTableSpan(tableCell.Attributes["rowspan"]);
				int num = Math.Max(1, Math.Min(tableSpan2, list.Count - item3.RowIndex));
				int num2 = FindAvailableTableColumn(list4, startColumn, tableSpan);
				EnsureTableCapacity(list4, num2 + tableSpan);
				for (int i = 0; i < tableSpan; i++)
				{
					list4[num2 + i] = Math.Max(list4[num2 + i], num);
				}
				list3.Add(new TableCellPlacement(tableCell, item3.Row, item3.RowIndex, num2, tableSpan, num));
				startColumn = num2 + tableSpan;
			}
		}
		int item = 0;
		for (int i = 0; i < list3.Count; i++)
		{
			int end = list3[i].ColumnIndex + list3[i].ColumnSpan;
			if (end > item)
			{
				item = end;
			}
		}
		return (RowInfos: list, Placements: list3, ColumnCount: item);
	}

	private static List<(UiNode? Section, List<UiNode> Rows)> CollectTableRowGroups(UiNode table)
	{
		List<(UiNode, List<UiNode>)> list = new List<(UiNode, List<UiNode>)>();
		List<UiNode> list2 = new List<UiNode>();
		foreach (UiNode child in table.Children)
		{
			if (child.Kind == UiNodeKind.TableRow)
			{
				list2.Add(child);
				continue;
			}
			if (child.Kind is UiNodeKind.TableHeader or UiNodeKind.TableBody or UiNodeKind.TableFooter)
			{
				List<UiNode> list3 = new List<UiNode>();
				for (int i = 0; i < child.Children.Count; i++)
				{
					if (child.Children[i].Kind == UiNodeKind.TableRow)
					{
						list3.Add(child.Children[i]);
					}
				}
				if (list3.Count > 0)
				{
					list.Add((child, list3));
				}
			}
		}
		if (list2.Count > 0)
		{
			list.Insert(0, (null, list2));
		}
		return list;
	}

	private static void CollectTableCells(UiNode row, List<UiNode> cells)
	{
		cells.Clear();
		for (int i = 0; i < row.Children.Count; i++)
		{
			UiNode child = row.Children[i];
			if (child.Kind is UiNodeKind.TableCell or UiNodeKind.TableHeaderCell)
			{
				cells.Add(child);
			}
		}
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
		int num = Math.Max(0, startColumn);
		bool flag;
		do
		{
			EnsureTableCapacity(occupiedColumns, num + columnSpan);
			flag = true;
			for (int i = 0; i < columnSpan; i++)
			{
				if (occupiedColumns[num + i] > 0)
				{
					num += i + 1;
					flag = false;
					break;
				}
			}
		}
		while (!flag);
		return num;
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
		int result;
		return (!int.TryParse(value, out result) || result <= 1) ? 1 : result;
	}

	private static float SumTableRange(float[] values, int start, int length)
	{
		float num = 0f;
		for (int i = 0; i < length && start + i < values.Length; i++)
		{
			num += values[start + i];
		}
		return num;
	}

	private float MeasureTableCellPreferredWidth(UiNode cell)
	{
		Size size = MeasureNode(cell, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
		float num = size.Width;
		if (num <= 0.01f)
		{
			num = Math.Max(cell.LayoutRect.Width, 48f);
		}
		return Math.Max(48f, num);
	}

	private float MeasureTableRowHeight(UiNode row, IReadOnlyList<UiNode> cells, float[] columnWidths)
	{
		float num = Math.Max(24f, row.LayoutRect.Height);
		for (int i = 0; i < cells.Count; i++)
		{
			float width = ((i < columnWidths.Length) ? columnWidths[i] : 0f);
			Size size = MeasureNode(cells[i], width, MeasureMode.AtMost, 0f, MeasureMode.Undefined);
			num = Math.Max(num, Math.Max(cells[i].LayoutRect.Height, size.Height));
		}
		return num;
	}

	private static void FitTableColumns(float[] columnWidths, float availableWidth)
	{
		if (columnWidths.Length == 0)
		{
			return;
		}
		float num = SumTableRange(columnWidths, 0, columnWidths.Length);
		if (num <= 0.01f)
		{
			float num2 = availableWidth / (float)columnWidths.Length;
			for (int i = 0; i < columnWidths.Length; i++)
			{
				columnWidths[i] = num2;
			}
			return;
		}
		if (num < availableWidth)
		{
			float num3 = (availableWidth - num) / (float)columnWidths.Length;
			for (int j = 0; j < columnWidths.Length; j++)
			{
				columnWidths[j] += num3;
			}
			return;
		}
		float num4 = availableWidth / num;
		for (int k = 0; k < columnWidths.Length; k++)
		{
			columnWidths[k] = Math.Max(36f, columnWidths[k] * num4);
		}
		float num5 = SumTableRange(columnWidths, 0, columnWidths.Length);
		if (columnWidths.Length != 0)
		{
			columnWidths[^1] += availableWidth - num5;
		}
	}

	private Size MeasureNode(UiNode node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
	{
		UiStyle style = node.Style;
		string textContent = node.TextContent;
		if (!string.IsNullOrWhiteSpace(textContent))
		{
			float availableWidth = ((widthMode == MeasureMode.Undefined) ? float.PositiveInfinity : Math.Max(0f, width - style.Padding.Horizontal));
			UiTextLayoutResult uiTextLayoutResult = _textMeasurer.Measure(textContent, style, availableWidth, widthMode != MeasureMode.Undefined);
			float measured = uiTextLayoutResult.Width + style.Padding.Horizontal;
			float measured2 = uiTextLayoutResult.Height + style.Padding.Vertical;
			return new Size(ResolveMeasuredAxis(measured, width, widthMode), ResolveMeasuredAxis(measured2, height, heightMode));
		}
		UiNodeKind kind = node.Kind;
		(float, float) tuple;
		switch (kind)
		{
		case UiNodeKind.Button:
			tuple = (140f, 40f);
			break;
		case UiNodeKind.Image:
			tuple = ResolveImageIntrinsicSize(node);
			break;
		case UiNodeKind.Input:
		case UiNodeKind.Select:
		case UiNodeKind.TextArea:
			tuple = (220f, 40f);
			break;
		case UiNodeKind.Checkbox:
		case UiNodeKind.Radio:
		case UiNodeKind.Toggle:
			tuple = (120f, 28f);
			break;
		case UiNodeKind.Slider:
			tuple = (220f, 24f);
			break;
		default:
			tuple = ((!string.Equals(node.TagName, "canvas", StringComparison.OrdinalIgnoreCase)) ? (0f, 0f) : ResolveCanvasIntrinsicSize(node));
			break;
		}
		var (measured3, measured4) = tuple;
		return new Size(ResolveMeasuredAxis(measured3, width, widthMode), ResolveMeasuredAxis(measured4, height, heightMode));
	}

	private static float ResolveMeasuredAxis(float measured, float available, MeasureMode mode)
	{
		float result = mode switch
		{
			MeasureMode.Exactly => available, 
			MeasureMode.AtMost => Math.Min(measured, available), 
			_ => measured, 
		};
		return result;
	}

	private (float Width, float Height) ResolveImageIntrinsicSize(UiNode node)
	{
		if (_imageSizeProvider.TryGetSize(node.Attributes["src"], out var width, out var height))
		{
			return (Width: width, Height: height);
		}
		return (Width: 160f, Height: 96f);
	}

	private static (float Width, float Height) ResolveCanvasIntrinsicSize(UiNode node)
	{
		float item = TryParseDimension(node.Attributes["width"], 300f);
		float item2 = TryParseDimension(node.Attributes["height"], 150f);
		return (Width: item, Height: item2);
	}

	private static float TryParseDimension(string? value, float fallback)
	{
		float result;
		return (float.TryParse(value, out result) && result > 0.01f) ? result : fallback;
	}
}
