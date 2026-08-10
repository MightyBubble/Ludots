using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public static class UiGridLayoutEngine
{
	internal static void LayoutSubtree(UiNode root, UiLayoutEngine layoutEngine, UiLayoutScratch scratch)
	{
		ArgumentNullException.ThrowIfNull(root, nameof(root));
		ArgumentNullException.ThrowIfNull(layoutEngine, nameof(layoutEngine));
		ArgumentNullException.ThrowIfNull(scratch, nameof(scratch));
		LayoutNode(root, layoutEngine, scratch);
	}

	private static void LayoutNode(UiNode node, UiLayoutEngine layoutEngine, UiLayoutScratch scratch)
	{
		if (node.Style.Display == UiDisplay.Grid && node.Style.Visible)
		{
			LayoutGrid(node, layoutEngine, scratch);
		}
		for (int i = 0; i < node.Children.Count; i++)
		{
			LayoutNode(node.Children[i], layoutEngine, scratch);
		}
	}

	private static void LayoutGrid(UiNode grid, UiLayoutEngine layoutEngine, UiLayoutScratch scratch)
	{
		UiStyle style = grid.Style;
		float contentWidth = Math.Max(0f, grid.LayoutRect.Width - style.Padding.Horizontal - style.BorderWidth * 2f);
		float contentHeight = Math.Max(0f, grid.LayoutRect.Height - style.Padding.Vertical - style.BorderWidth * 2f);
		float columnGap = style.ColumnGap > 0f ? style.ColumnGap : style.Gap;
		float rowGap = style.RowGap > 0f ? style.RowGap : style.Gap;

		List<UiNode> items = scratch.BeginGridItems();
		for (int i = 0; i < grid.Children.Count; i++)
		{
			UiNode child = grid.Children[i];
			if (child.Style.Visible && child.Style.Display != UiDisplay.None)
			{
				items.Add(child);
			}
		}

		IReadOnlyList<UiGridTrack> columnTracks = style.GridTemplateColumns.Count > 0
			? style.GridTemplateColumns
			: UiLayoutScratch.SharedDefaultSingleFr;

		List<UiGridPlacementSlot> placements = PlaceItems(items, columnTracks.Count, style.GridAutoFlow, scratch);
		int columnCount = Math.Max(columnTracks.Count, MaxEnd(placements, column: true));
		int rowCount = Math.Max(style.GridTemplateRows.Count, MaxEnd(placements, column: false));
		if (rowCount < 1)
		{
			rowCount = 1;
		}

		List<UiGridTrack> expandedColumns = ExpandTracks(columnTracks, columnCount, scratch.BeginColumnTracks());
		Span<float> columnSizes = scratch.BeginColumnSizes(columnCount);
		ResolveTracks(expandedColumns, contentWidth, columnGap, columnSizes);

		List<UiGridTrack> expandedRows = ExpandTracks(style.GridTemplateRows, rowCount, scratch.BeginRowTracks());
		Span<float> rowSizes = scratch.BeginRowSizes(rowCount);
		ResolveTracks(expandedRows, contentHeight, rowGap, rowSizes);

		for (int r = 0; r < rowCount; r++)
		{
			if (expandedRows[r].Sizing == UiGridTrackSizing.Auto ||
				(expandedRows[r].Sizing == UiGridTrackSizing.Fr && contentHeight <= 0.01f))
			{
				float maxContent = 0f;
				for (int i = 0; i < placements.Count; i++)
				{
					UiGridPlacementSlot placement = placements[i];
					if (placement.RowStart <= r + 1 && placement.RowStart + placement.RowSpan - 1 >= r + 1)
					{
						maxContent = Math.Max(maxContent, EstimateContentHeight(placement.Node));
					}
				}
				rowSizes[r] = Math.Max(rowSizes[r], maxContent);
			}
		}

		Span<float> columnOffsets = scratch.BeginColumnOffsets(columnCount);
		BuildOffsets(columnSizes, columnGap, columnOffsets);
		Span<float> rowOffsets = scratch.BeginRowOffsets(rowCount);
		BuildOffsets(rowSizes, rowGap, rowOffsets);

		float originX = grid.LayoutRect.X + style.Padding.Left + style.BorderWidth;
		float originY = grid.LayoutRect.Y + style.Padding.Top + style.BorderWidth;
		for (int i = 0; i < placements.Count; i++)
		{
			UiGridPlacementSlot placement = placements[i];
			int colIndex = placement.ColumnStart - 1;
			int rowIndex = placement.RowStart - 1;
			float x = originX + columnOffsets[colIndex];
			float y = originY + rowOffsets[rowIndex];
			float width = SumRange(columnSizes, colIndex, placement.ColumnSpan, columnGap);
			float height = SumRange(rowSizes, rowIndex, placement.RowSpan, rowGap);
			placement.Node.SetLayout(new UiRect(x, y, width, height));
			placement.Node.SetScrollMetrics(width, height);
			layoutEngine.LayoutNestedContent(placement.Node);
		}

		float usedWidth = columnCount == 0 ? 0f : columnOffsets[columnCount - 1] + columnSizes[columnCount - 1];
		float usedHeight = rowCount == 0 ? 0f : rowOffsets[rowCount - 1] + rowSizes[rowCount - 1];
		grid.SetScrollMetrics(
			style.Padding.Horizontal + style.BorderWidth * 2f + usedWidth,
			style.Padding.Vertical + style.BorderWidth * 2f + usedHeight);
	}

	private static List<UiGridPlacementSlot> PlaceItems(List<UiNode> items, int explicitColumnCount, UiGridAutoFlow autoFlow, UiLayoutScratch scratch)
	{
		int columns = Math.Max(1, explicitColumnCount);
		List<UiGridPlacementSlot> result = scratch.BeginGridPlacements();
		HashSet<long> occupied = scratch.BeginOccupied();
		int cursorRow = 1;
		int cursorColumn = 1;

		for (int i = 0; i < items.Count; i++)
		{
			UiNode item = items[i];
			UiGridPlacement column = item.Style.GridColumn;
			UiGridPlacement row = item.Style.GridRow;
			int columnSpan = column.Span;
			int rowSpan = row.Span;
			int columnStart;
			int rowStart;

			if (!column.IsAuto && !row.IsAuto)
			{
				columnStart = column.Start;
				rowStart = row.Start;
			}
			else if (!column.IsAuto)
			{
				columnStart = column.Start;
				rowStart = FindNextFree(occupied, cursorRow, columnStart, columnSpan, rowSpan, columns, preferColumn: false);
			}
			else if (!row.IsAuto)
			{
				rowStart = row.Start;
				columnStart = FindNextFree(occupied, rowStart, cursorColumn, columnSpan, rowSpan, columns, preferColumn: true);
			}
			else
			{
				(columnStart, rowStart) = FindNextAutoCell(occupied, cursorRow, cursorColumn, columnSpan, rowSpan, columns, autoFlow);
			}

			MarkOccupied(occupied, columnStart, rowStart, columnSpan, rowSpan);
			result.Add(new UiGridPlacementSlot
			{
				Node = item,
				ColumnStart = columnStart,
				ColumnSpan = columnSpan,
				RowStart = rowStart,
				RowSpan = rowSpan
			});
			if (autoFlow == UiGridAutoFlow.Column)
			{
				cursorColumn = columnStart;
				cursorRow = rowStart + rowSpan;
			}
			else
			{
				cursorRow = rowStart;
				cursorColumn = columnStart + columnSpan;
				if (cursorColumn > columns)
				{
					cursorColumn = 1;
					cursorRow = rowStart + rowSpan;
				}
			}
		}

		return result;
	}

	private static (int ColumnStart, int RowStart) FindNextAutoCell(HashSet<long> occupied, int startRow, int startColumn, int columnSpan, int rowSpan, int columns, UiGridAutoFlow autoFlow)
	{
		int row = Math.Max(1, startRow);
		int column = Math.Max(1, startColumn);
		for (int guard = 0; guard < 10000; guard++)
		{
			if (column + columnSpan - 1 <= columns && IsFree(occupied, column, row, columnSpan, rowSpan))
			{
				return (column, row);
			}
			if (autoFlow == UiGridAutoFlow.Column)
			{
				row++;
				if (row > 256)
				{
					row = 1;
					column++;
				}
			}
			else
			{
				column++;
				if (column + columnSpan - 1 > columns)
				{
					column = 1;
					row++;
				}
			}
		}
		return (1, 1);
	}

	private static int FindNextFree(HashSet<long> occupied, int fixedAxis, int startOther, int columnSpan, int rowSpan, int columns, bool preferColumn)
	{
		if (preferColumn)
		{
			for (int column = Math.Max(1, startOther); column <= columns; column++)
			{
				if (column + columnSpan - 1 <= columns && IsFree(occupied, column, fixedAxis, columnSpan, rowSpan))
				{
					return column;
				}
			}
			return 1;
		}
		for (int row = Math.Max(1, fixedAxis); row < 10000; row++)
		{
			if (IsFree(occupied, startOther, row, columnSpan, rowSpan))
			{
				return row;
			}
		}
		return 1;
	}

	private static bool IsFree(HashSet<long> occupied, int columnStart, int rowStart, int columnSpan, int rowSpan)
	{
		for (int r = 0; r < rowSpan; r++)
		{
			for (int c = 0; c < columnSpan; c++)
			{
				if (occupied.Contains(Pack(columnStart + c, rowStart + r)))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static void MarkOccupied(HashSet<long> occupied, int columnStart, int rowStart, int columnSpan, int rowSpan)
	{
		for (int r = 0; r < rowSpan; r++)
		{
			for (int c = 0; c < columnSpan; c++)
			{
				occupied.Add(Pack(columnStart + c, rowStart + r));
			}
		}
	}

	private static long Pack(int column, int row) => ((long)column << 32) | (uint)row;

	private static int MaxEnd(List<UiGridPlacementSlot> placements, bool column)
	{
		int max = 0;
		for (int i = 0; i < placements.Count; i++)
		{
			UiGridPlacementSlot placement = placements[i];
			int end = column
				? placement.ColumnStart + placement.ColumnSpan - 1
				: placement.RowStart + placement.RowSpan - 1;
			if (end > max)
			{
				max = end;
			}
		}
		return max;
	}

	private static List<UiGridTrack> ExpandTracks(IReadOnlyList<UiGridTrack> template, int count, List<UiGridTrack> tracks)
	{
		for (int i = 0; i < count; i++)
		{
			tracks.Add(i < template.Count ? template[i] : UiGridTrack.Auto);
		}
		return tracks;
	}

	private static void ResolveTracks(IReadOnlyList<UiGridTrack> tracks, float available, float gap, Span<float> sizes)
	{
		if (tracks.Count == 0)
		{
			return;
		}
		float gapTotal = gap * Math.Max(0, tracks.Count - 1);
		float remaining = Math.Max(0f, available - gapTotal);
		float frTotal = 0f;
		for (int i = 0; i < tracks.Count; i++)
		{
			UiGridTrack track = tracks[i];
			switch (track.Sizing)
			{
			case UiGridTrackSizing.Pixel:
				sizes[i] = Math.Max(0f, track.Value);
				remaining -= sizes[i];
				break;
			case UiGridTrackSizing.Percent:
				sizes[i] = Math.Max(0f, available * (track.Value / 100f));
				remaining -= sizes[i];
				break;
			case UiGridTrackSizing.Fr:
				frTotal += Math.Max(0f, track.Value);
				break;
			default:
				sizes[i] = 0f;
				break;
			}
		}
		remaining = Math.Max(0f, remaining);
		if (frTotal > 0f)
		{
			for (int i = 0; i < tracks.Count; i++)
			{
				if (tracks[i].Sizing == UiGridTrackSizing.Fr)
				{
					sizes[i] = remaining * (tracks[i].Value / frTotal);
				}
			}
		}
	}

	private static void BuildOffsets(ReadOnlySpan<float> sizes, float gap, Span<float> offsets)
	{
		float cursor = 0f;
		for (int i = 0; i < sizes.Length; i++)
		{
			offsets[i] = cursor;
			cursor += sizes[i];
			if (i + 1 < sizes.Length)
			{
				cursor += gap;
			}
		}
	}

	private static float SumRange(ReadOnlySpan<float> sizes, int start, int span, float gap)
	{
		float total = 0f;
		int end = Math.Min(sizes.Length, start + span);
		for (int i = start; i < end; i++)
		{
			total += sizes[i];
			if (i + 1 < end)
			{
				total += gap;
			}
		}
		return total;
	}

	private static float EstimateContentHeight(UiNode node)
	{
		if (!string.IsNullOrWhiteSpace(node.TextContent))
		{
			return Math.Max(node.Style.FontSize * 1.4f, node.LayoutRect.Height);
		}
		if (node.Style.Height.Unit == UiLengthUnit.Pixel)
		{
			return node.Style.Height.Value;
		}
		return Math.Max(0f, node.LayoutRect.Height);
	}
}
