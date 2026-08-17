using System;
using System.Collections.Generic;
using FlexLayoutSharp;

namespace Ludots.UI.Runtime;

internal sealed class UiLayoutScratch
{
	private readonly List<Node> _deferredCalc = new List<Node>(64);
	private readonly List<UiNode> _gridItems = new List<UiNode>(64);
	private readonly List<UiGridPlacementSlot> _gridPlacements = new List<UiGridPlacementSlot>(64);
	private readonly List<UiGridTrack> _columnTracks = new List<UiGridTrack>(32);
	private readonly List<UiGridTrack> _rowTracks = new List<UiGridTrack>(32);
	private readonly HashSet<long> _occupied = new HashSet<long>();
	private readonly List<UiInlineLineBox> _lines = new List<UiInlineLineBox>(16);
	private readonly List<UiInlineItem> _lineItems = new List<UiInlineItem>(64);
	private readonly List<UiNode> _tableCells = new List<UiNode>(64);
	private float[] _columnSizes = new float[32];
	private float[] _rowSizes = new float[32];
	private float[] _columnOffsets = new float[32];
	private float[] _rowOffsets = new float[32];
	private static readonly UiGridTrack[] DefaultSingleFr = { UiGridTrack.Fr(1f) };

	public static UiGridTrack[] SharedDefaultSingleFr => DefaultSingleFr;

	public List<Node> BeginDeferredCalc()
	{
		_deferredCalc.Clear();
		return _deferredCalc;
	}

	public List<UiNode> BeginGridItems()
	{
		_gridItems.Clear();
		return _gridItems;
	}

	public List<UiGridPlacementSlot> BeginGridPlacements()
	{
		_gridPlacements.Clear();
		return _gridPlacements;
	}

	public List<UiGridTrack> BeginColumnTracks()
	{
		_columnTracks.Clear();
		return _columnTracks;
	}

	public List<UiGridTrack> BeginRowTracks()
	{
		_rowTracks.Clear();
		return _rowTracks;
	}

	public HashSet<long> BeginOccupied()
	{
		_occupied.Clear();
		return _occupied;
	}

	public List<UiInlineLineBox> BeginLines()
	{
		_lines.Clear();
		return _lines;
	}

	public List<UiInlineItem> BeginLineItems()
	{
		_lineItems.Clear();
		return _lineItems;
	}

	public List<UiNode> BeginTableCells()
	{
		_tableCells.Clear();
		return _tableCells;
	}

	public Span<float> BeginColumnSizes(int count) => BeginFloatBuffer(ref _columnSizes, count);

	public Span<float> BeginRowSizes(int count) => BeginFloatBuffer(ref _rowSizes, count);

	public Span<float> BeginColumnOffsets(int count) => BeginFloatBuffer(ref _columnOffsets, count);

	public Span<float> BeginRowOffsets(int count) => BeginFloatBuffer(ref _rowOffsets, count);

	private static Span<float> BeginFloatBuffer(ref float[] buffer, int count)
	{
		if (count < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(count));
		}
		if (buffer.Length < count)
		{
			int size = Math.Max(32, buffer.Length);
			while (size < count)
			{
				size *= 2;
			}
			buffer = new float[size];
		}
		buffer.AsSpan(0, count).Clear();
		return buffer.AsSpan(0, count);
	}
}

internal struct UiGridPlacementSlot
{
	public UiNode Node;
	public int ColumnStart;
	public int ColumnSpan;
	public int RowStart;
	public int RowSpan;
}

internal struct UiInlineItem
{
	public UiNode Node;
	public float Width;
	public float Height;
	public float Ascent;
	public float Descent;
}

internal struct UiInlineLineBox
{
	public int ItemStart;
	public int ItemCount;
	public float Width;
	public float MaxAscent;
	public float MaxDescent;

	public readonly float Height => MaxAscent + MaxDescent;
}
