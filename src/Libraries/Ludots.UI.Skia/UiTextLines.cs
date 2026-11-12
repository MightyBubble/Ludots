using System;

namespace Ludots.UI.Skia;

/// <summary>
/// 一段文字折行后的结果：行内容存在自己的字符缓冲里（已按 white-space 规则归一），每行记起止和宽度。
/// 测量与绘制各持一份反复使用，容量够了以后不再分配。
/// </summary>
internal sealed class UiTextLines
{
	public const char Ellipsis = '…';

	private char[] _chars = new char[256];

	private Line[] _lines = new Line[8];

	public int CharCount { get; private set; }

	public int Count { get; private set; }

	public float MaxWidth { get; private set; }

	public void Reset()
	{
		CharCount = 0;
		Count = 0;
		MaxWidth = 0f;
	}

	public ReadOnlySpan<char> Chars(int start, int length) => _chars.AsSpan(start, length);

	public ref readonly Line this[int index] => ref _lines[index];

	public ReadOnlySpan<char> Text(int index)
	{
		ref readonly Line line = ref _lines[index];
		return _chars.AsSpan(line.Start, line.Length);
	}

	public void AppendChar(char value)
	{
		if (CharCount == _chars.Length)
		{
			Array.Resize(ref _chars, _chars.Length * 2);
		}
		_chars[CharCount++] = value;
	}

	public void AddLine(int start, int length, float contentWidth, float width, bool ellipsis)
	{
		if (Count == _lines.Length)
		{
			Array.Resize(ref _lines, _lines.Length * 2);
		}
		_lines[Count++] = new Line(start, length, contentWidth, width, ellipsis);
		if (width > MaxWidth)
		{
			MaxWidth = width;
		}
	}

	public string LineString(int index)
	{
		ref readonly Line line = ref _lines[index];
		ReadOnlySpan<char> text = _chars.AsSpan(line.Start, line.Length);
		return line.HasEllipsis ? string.Concat(text, "…") : new string(text);
	}

	internal readonly struct Line
	{
		public readonly int Start;
		public readonly int Length;

		/// <summary>行内文字的宽度，不含省略号。</summary>
		public readonly float ContentWidth;

		/// <summary>整行宽度，有省略号时含省略号。</summary>
		public readonly float Width;

		public readonly bool HasEllipsis;

		public Line(int start, int length, float contentWidth, float width, bool hasEllipsis)
		{
			Start = start;
			Length = length;
			ContentWidth = contentWidth;
			Width = width;
			HasEllipsis = hasEllipsis;
		}
	}
}
