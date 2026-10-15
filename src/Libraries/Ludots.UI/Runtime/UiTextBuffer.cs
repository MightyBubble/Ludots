using System;
using System.Runtime.CompilerServices;

namespace Ludots.UI.Runtime;

/// <summary>
/// 可复用的字符缓冲。格式化只往缓冲里写，容量够用后不再分配。
/// </summary>
public sealed class UiTextBuffer
{
	private char[] _chars;

	private int _length;

	public UiTextBuffer(int capacity = 64)
	{
		_chars = new char[Math.Max(1, capacity)];
	}

	public int Length => _length;

	public ReadOnlySpan<char> Span => _chars.AsSpan(0, _length);

	public void Clear() => _length = 0;

	public void Append(char value)
	{
		if (_length == _chars.Length) Grow(1);
		_chars[_length++] = value;
	}

	public void Append(string? value) => Append(value.AsSpan());

	public void Append(ReadOnlySpan<char> value)
	{
		if (value.IsEmpty) return;
		if (_length + value.Length > _chars.Length) Grow(value.Length);
		value.CopyTo(_chars.AsSpan(_length));
		_length += value.Length;
	}

	public void Append<T>(T value, ReadOnlySpan<char> format, IFormatProvider? provider) where T : ISpanFormattable
	{
		while (true)
		{
			if (value.TryFormat(_chars.AsSpan(_length), out int written, format, provider))
			{
				_length += written;
				return;
			}

			Grow(Math.Max(16, _chars.Length));
		}
	}

	public void Append(IFormatProvider? provider, [InterpolatedStringHandlerArgument("", "provider")] ref UiTextBufferHandler handler)
	{
	}

	/// <summary>把 <paramref name="start"/> 之后的内容截掉，配合 <see cref="Length"/> 回退半截写入。</summary>
	public void Truncate(int start)
	{
		if ((uint)start > (uint)_length) throw new ArgumentOutOfRangeException(nameof(start));
		_length = start;
	}

	public override string ToString() => new string(Span);

	private void Grow(int extra)
	{
		var next = new char[Math.Max(_chars.Length * 2, _length + extra)];
		_chars.AsSpan(0, _length).CopyTo(next);
		_chars = next;
	}
}

/// <summary>写进 <see cref="UiTextBuffer"/> 的插值处理器；数值走 <see cref="ISpanFormattable"/>，不产生中间字符串。</summary>
[InterpolatedStringHandler]
public readonly ref struct UiTextBufferHandler
{
	private readonly UiTextBuffer _buffer;

	private readonly IFormatProvider? _provider;

	public UiTextBufferHandler(int literalLength, int formattedCount, UiTextBuffer buffer, IFormatProvider? provider)
	{
		_buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
		_provider = provider;
	}

	public void AppendLiteral(string value) => _buffer.Append(value.AsSpan());

	public void AppendFormatted(string? value) => _buffer.Append(value.AsSpan());

	public void AppendFormatted(ReadOnlySpan<char> value) => _buffer.Append(value);

	public void AppendFormatted(char value) => _buffer.Append(value);

	public void AppendFormatted<T>(T value) where T : ISpanFormattable => _buffer.Append(value, default, _provider);

	public void AppendFormatted<T>(T value, string? format) where T : ISpanFormattable => _buffer.Append(value, format.AsSpan(), _provider);
}
