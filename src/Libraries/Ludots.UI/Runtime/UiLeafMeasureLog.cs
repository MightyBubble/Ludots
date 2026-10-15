using FlexLayoutSharp;

namespace Ludots.UI.Runtime;

/// <summary>
/// 一个可量叶子在当前保留的 flex 树里被量过的约束和结果。文字变了以后按这些约束重量一遍，
/// 结果都没变就说明 flex 引擎缓存的尺寸仍然成立，不用标脏；记满溢出就只能当作变了。
/// </summary>
internal sealed class UiLeafMeasureLog
{
	private const int Capacity = 8;

	private readonly Entry[] _entries = new Entry[Capacity];

	public int Count { get; private set; }

	public bool Overflowed { get; private set; }

	public void Clear()
	{
		Count = 0;
		Overflowed = false;
	}

	public void Record(float width, MeasureMode widthMode, float height, MeasureMode heightMode, Size result)
	{
		for (int i = 0; i < Count; i++)
		{
			ref Entry entry = ref _entries[i];
			if (entry.Matches(width, widthMode, height, heightMode))
			{
				entry.Result = result;
				return;
			}
		}

		if (Count == Capacity)
		{
			Overflowed = true;
			return;
		}

		_entries[Count++] = new Entry(width, widthMode, height, heightMode, result);
	}

	public ref readonly Entry this[int index] => ref _entries[index];

	internal struct Entry
	{
		public readonly float Width;
		public readonly MeasureMode WidthMode;
		public readonly float Height;
		public readonly MeasureMode HeightMode;
		public Size Result;

		public Entry(float width, MeasureMode widthMode, float height, MeasureMode heightMode, Size result)
		{
			Width = width;
			WidthMode = widthMode;
			Height = height;
			HeightMode = heightMode;
			Result = result;
		}

		public readonly bool Matches(float width, MeasureMode widthMode, float height, MeasureMode heightMode) =>
			widthMode == WidthMode && heightMode == HeightMode && Same(width, Width) && Same(height, Height);

		private static bool Same(float a, float b) => a.Equals(b) || (float.IsNaN(a) && float.IsNaN(b));
	}
}
