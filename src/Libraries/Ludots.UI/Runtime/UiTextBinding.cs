using System;

namespace Ludots.UI.Runtime;

/// <summary>
/// 节点文字的可变来源。持有方改写后，挂着它的节点在下一次 <see cref="UiScene.Layout"/> 时只重量这一个叶子，
/// 不重组页面、不重算样式。内容不变的写入不改版本号；容量够用后改写不分配。
/// </summary>
public sealed class UiTextBinding
{
	private char[] _chars;

	private int _length;

	private string? _text;

	public UiTextBinding(int capacity = 32)
	{
		_chars = new char[Math.Max(1, capacity)];
	}

	public UiTextBinding(string initial)
		: this(Math.Max(1, initial?.Length ?? 0))
	{
		Set(initial.AsSpan());
	}

	/// <summary>内容每变一次加一。</summary>
	public int Version { get; private set; }

	public int Length => _length;

	public ReadOnlySpan<char> Span => _chars.AsSpan(0, _length);

	public bool Set(UiTextBuffer buffer)
	{
		ArgumentNullException.ThrowIfNull(buffer, nameof(buffer));
		return Set(buffer.Span);
	}

	/// <returns>内容是否变了。</returns>
	public bool Set(ReadOnlySpan<char> text)
	{
		if (text.SequenceEqual(Span)) return false;
		if (text.Length > _chars.Length)
		{
			_chars = new char[Math.Max(text.Length, _chars.Length * 2)];
		}

		text.CopyTo(_chars);
		_length = text.Length;
		_text = null;
		Version++;
		return true;
	}

	/// <summary>给不在热路径上的读者（诊断、测试、序列化）用；同一版本只建一次字符串。</summary>
	public override string ToString() => _text ??= new string(Span);
}
