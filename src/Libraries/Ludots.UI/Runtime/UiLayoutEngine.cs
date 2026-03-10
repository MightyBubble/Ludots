using System;
using System.Collections.Generic;
using System.Linq;
using FlexLayoutSharp;

namespace Ludots.UI.Runtime;

public sealed class UiLayoutEngine
{
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
		Node node = BuildFlexTree(root, isRoot: true, width, height);
		node.CalculateLayout(width, height, Direction.LTR);
		ApplyLayout(root, node, 0f, 0f);
		NormalizeTableLayouts(root);
	}

	private Node BuildFlexTree(UiNode node, bool isRoot, float rootWidth, float rootHeight)
	{
		Node node2 = new Node
		{
			Context = node
		};
		ConfigureNodeStyle(node2, node, isRoot, rootWidth, rootHeight);
		if (ShouldMeasureAsLeaf(node))
		{
			node2.SetMeasureFunc((Node _, float width, MeasureMode widthMode, float height, MeasureMode heightMode) => MeasureNode(node, width, widthMode, height, heightMode));
			return node2;
		}
		for (int num = 0; num < node.Children.Count; num++)
		{
			Node node3 = BuildFlexTree(node.Children[num], isRoot: false, rootWidth, rootHeight);
			ApplyGapOffset(node3, node.Style, num);
			node2.AddChild(node3);
		}
		return node2;
	}

	private void ConfigureNodeStyle(Node flexNode, UiNode node, bool isRoot, float rootWidth, float rootHeight)
	{
		UiStyle style = node.Style;
		bool flag = style.Visible && style.Display != UiDisplay.None;
		flexNode.StyleSetDisplay((!flag) ? Display.None : Display.Flex);
		flexNode.StyleSetFlexDirection((style.FlexDirection == UiFlexDirection.Row) ? FlexDirection.Row : FlexDirection.Column);
		flexNode.StyleSetJustifyContent(MapJustify(style.JustifyContent));
		flexNode.StyleSetAlignItems(MapAlign(style.AlignItems));
		flexNode.StyleSetAlignContent(MapAlignContent(style.AlignContent));
		flexNode.StyleSetFlexWrap(MapWrap(style.FlexWrap));
		flexNode.StyleSetOverflow(MapOverflow(style));
		flexNode.StyleSetPositionType((style.PositionType == UiPositionType.Absolute) ? PositionType.Absolute : PositionType.Relative);
		flexNode.StyleSetFlexGrow(style.FlexGrow);
		flexNode.StyleSetFlexShrink(style.FlexShrink);
		ApplyLength(style.Width, flexNode.StyleSetWidth, flexNode.StyleSetWidthPercent, flexNode.StyleSetWidthAuto);
		ApplyLength(style.Height, flexNode.StyleSetHeight, flexNode.StyleSetHeightPercent, flexNode.StyleSetHeightAuto);
		ApplyLength(style.MinWidth, flexNode.StyleSetMinWidth, flexNode.StyleSetMinWidthPercent, null);
		ApplyLength(style.MinHeight, flexNode.StyleSetMinHeight, flexNode.StyleSetMinHeightPercent, null);
		ApplyLength(style.MaxWidth, flexNode.StyleSetMaxWidth, flexNode.StyleSetMaxWidthPercent, null);
		ApplyLength(style.MaxHeight, flexNode.StyleSetMaxHeight, flexNode.StyleSetMaxHeightPercent, null);
		ApplyLength(style.FlexBasis, flexNode.StyleSetFlexBasis, flexNode.StyleSetFlexBasisPercent, flexNode.NodeStyleSetFlexBasisAuto);
		ApplyLength(style.Left, delegate(float value)
		{
			flexNode.StyleSetPosition(Edge.Left, value);
		}, delegate(float value)
		{
			flexNode.StyleSetPositionPercent(Edge.Left, value);
		}, null);
		ApplyLength(style.Top, delegate(float value)
		{
			flexNode.StyleSetPosition(Edge.Top, value);
		}, delegate(float value)
		{
			flexNode.StyleSetPositionPercent(Edge.Top, value);
		}, null);
		ApplyLength(style.Right, delegate(float value)
		{
			flexNode.StyleSetPosition(Edge.Right, value);
		}, delegate(float value)
		{
			flexNode.StyleSetPositionPercent(Edge.Right, value);
		}, null);
		ApplyLength(style.Bottom, delegate(float value)
		{
			flexNode.StyleSetPosition(Edge.Bottom, value);
		}, delegate(float value)
		{
			flexNode.StyleSetPositionPercent(Edge.Bottom, value);
		}, null);
		ApplyThickness(style.Margin, delegate(Edge edge, float value)
		{
			flexNode.StyleSetMargin(edge, value);
		}, delegate(Edge edge, float value)
		{
			flexNode.StyleSetMarginPercent(edge, value);
		});
		ApplyThickness(style.Padding, delegate(Edge edge, float value)
		{
			flexNode.StyleSetPadding(edge, value);
		}, delegate(Edge edge, float value)
		{
			flexNode.StyleSetPaddingPercent(edge, value);
		});
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

	private static void ApplyThickness(UiThickness thickness, Action<Edge, float> pointSetter, Action<Edge, float> percentSetter)
	{
		SetThicknessEdge(Edge.Left, thickness.Left, pointSetter, percentSetter);
		SetThicknessEdge(Edge.Top, thickness.Top, pointSetter, percentSetter);
		SetThicknessEdge(Edge.Right, thickness.Right, pointSetter, percentSetter);
		SetThicknessEdge(Edge.Bottom, thickness.Bottom, pointSetter, percentSetter);
	}

	private static void SetThicknessEdge(Edge edge, float value, Action<Edge, float> pointSetter, Action<Edge, float> percentSetter)
	{
		pointSetter(edge, value);
	}

	private static void ApplyBorder(float borderWidth, Node node)
	{
		node.StyleSetBorder(Edge.Left, borderWidth);
		node.StyleSetBorder(Edge.Top, borderWidth);
		node.StyleSetBorder(Edge.Right, borderWidth);
		node.StyleSetBorder(Edge.Bottom, borderWidth);
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

	private static Overflow MapOverflow(UiStyle style)
	{
		if (style.ClipContent)
		{
			return Overflow.Hidden;
		}
		UiOverflow overflow = style.Overflow;
		if (1 == 0)
		{
		}
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
		if (1 == 0)
		{
		}
		return result;
	}

	private static Justify MapJustify(UiJustifyContent justifyContent)
	{
		if (1 == 0)
		{
		}
		Justify result = justifyContent switch
		{
			UiJustifyContent.Center => Justify.Center, 
			UiJustifyContent.End => Justify.FlexEnd, 
			UiJustifyContent.SpaceBetween => Justify.SpaceBetween, 
			UiJustifyContent.SpaceAround => Justify.SpaceAround, 
			UiJustifyContent.SpaceEvenly => Justify.SpaceAround, 
			_ => Justify.FlexStart, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static Align MapAlign(UiAlignItems alignItems)
	{
		if (1 == 0)
		{
		}
		Align result = alignItems switch
		{
			UiAlignItems.Start => Align.FlexStart, 
			UiAlignItems.Center => Align.Center, 
			UiAlignItems.End => Align.FlexEnd, 
			_ => Align.Stretch, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static Align MapAlignContent(UiAlignContent alignContent)
	{
		if (1 == 0)
		{
		}
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
		if (1 == 0)
		{
		}
		return result;
	}

	private static Wrap MapWrap(UiFlexWrap wrap)
	{
		if (1 == 0)
		{
		}
		Wrap result = wrap switch
		{
			UiFlexWrap.Wrap => Wrap.Wrap, 
			UiFlexWrap.WrapReverse => Wrap.WrapReverse, 
			_ => Wrap.NoWrap, 
		};
		if (1 == 0)
		{
		}
		return result;
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
		UiNodeKind kind = node.Kind;
		return (kind - 2 <= UiNodeKind.Text || kind - 7 <= UiNodeKind.Column) ? true : false;
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
		foreach (TableCellPlacement item in list3.OrderBy((TableCellPlacement placement) => placement.ColumnSpan))
		{
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
		foreach (TableCellPlacement item3 in list3.OrderBy((TableCellPlacement placement) => placement.RowSpan))
		{
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
		Dictionary<UiNode, TableRowInfo> dictionary = list2.ToDictionary((TableRowInfo info) => info.Row);
		foreach (var (uiNode, list4) in list)
		{
			if (uiNode != null && list4.Count != 0)
			{
				TableRowInfo tableRowInfo = dictionary[list4[0]];
				TableRowInfo tableRowInfo2 = dictionary[list4[list4.Count - 1]];
				float y2 = array3[tableRowInfo.RowIndex];
				float num16 = SumTableRange(array2, tableRowInfo.RowIndex, tableRowInfo2.RowIndex - tableRowInfo.RowIndex + 1);
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
		bool flag = true;
		foreach (TableRowInfo item3 in list)
		{
			if (!flag)
			{
				AdvanceTableRowOccupancy(list4);
			}
			flag = false;
			int startColumn = 0;
			foreach (UiNode tableCell in GetTableCells(item3.Row))
			{
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
		int item = ((list3.Count != 0) ? list3.Max((TableCellPlacement placement) => placement.ColumnIndex + placement.ColumnSpan) : 0);
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
			UiNodeKind kind = child.Kind;
			if (kind - 18 <= UiNodeKind.Button)
			{
				List<UiNode> list3 = child.Children.Where((UiNode node) => node.Kind == UiNodeKind.TableRow).ToList();
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

	private static IReadOnlyList<UiNode> GetTableCells(UiNode row)
	{
		return row.Children.Where(delegate(UiNode child)
		{
			UiNodeKind kind = child.Kind;
			return kind - 22 <= UiNodeKind.Text;
		}).ToArray();
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
		float num = columnWidths.Sum();
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
		float num5 = columnWidths.Sum();
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
			UiTextLayoutResult uiTextLayoutResult = UiTextLayout.Measure(textContent, style, availableWidth, widthMode != MeasureMode.Undefined);
			float measured = uiTextLayoutResult.Width + style.Padding.Horizontal;
			float measured2 = uiTextLayoutResult.Height + style.Padding.Vertical;
			return new Size(ResolveMeasuredAxis(measured, width, widthMode), ResolveMeasuredAxis(measured2, height, heightMode));
		}
		UiNodeKind kind = node.Kind;
		if (1 == 0)
		{
		}
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
		if (1 == 0)
		{
		}
		var (measured3, measured4) = tuple;
		return new Size(ResolveMeasuredAxis(measured3, width, widthMode), ResolveMeasuredAxis(measured4, height, heightMode));
	}

	private static float ResolveMeasuredAxis(float measured, float available, MeasureMode mode)
	{
		if (1 == 0)
		{
		}
		float result = mode switch
		{
			MeasureMode.Exactly => available, 
			MeasureMode.AtMost => Math.Min(measured, available), 
			_ => measured, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static (float Width, float Height) ResolveImageIntrinsicSize(UiNode node)
	{
		if (UiImageSourceCache.TryGetSize(node.Attributes["src"], out var width, out var height))
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
