using System;

namespace Ludots.UI.Runtime;

public enum UiVectorCommandKind : byte
{
	FillRect,
	StrokePolyline,
	FillPolygon
}

public readonly struct UiVectorCommand
{
	public UiVectorCommand(UiVectorCommandKind kind, UiColor color, float strokeWidth, float x, float y, float width, float height, int pointStart, int pointCount)
	{
		Kind = kind;
		Color = color;
		StrokeWidth = strokeWidth;
		X = x;
		Y = y;
		Width = width;
		Height = height;
		PointStart = pointStart;
		PointCount = pointCount;
	}

	public UiVectorCommandKind Kind { get; }

	public UiColor Color { get; }

	public float StrokeWidth { get; }

	public float X { get; }

	public float Y { get; }

	public float Width { get; }

	public float Height { get; }

	public int PointStart { get; }

	public int PointCount { get; }
}

/// <summary>
/// 与后端无关的矢量绘制内容：预分配的矩形 / 折线 / 填充多边形命令表，渲染器每次绘制时整表重放。
/// 坐标是内容框内的归一化坐标（0..1，y 向下），线宽按像素算，所以布局尺寸变了不用重写命令。
/// 容量在构造时定死，写满就抛异常；容量内重写不分配。每次改写都加 <see cref="Version"/>，
/// 挂着它的场景据此只重画、不重排。
/// </summary>
public sealed class UiVectorDrawList : IUiCanvasContent
{
	private readonly UiVectorCommand[] _commands;

	private readonly float[] _points;

	private int _commandCount;

	private int _pointCount;

	private int _openStart = -1;

	private UiVectorCommandKind _openKind;

	private UiColor _openColor;

	private float _openStrokeWidth;

	public UiVectorDrawList(int commandCapacity, int pointCapacity)
	{
		if (commandCapacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(commandCapacity), commandCapacity, "Command capacity must be positive.");
		}
		if (pointCapacity < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(pointCapacity), pointCapacity, "Point capacity must not be negative.");
		}
		_commands = new UiVectorCommand[commandCapacity];
		_points = new float[pointCapacity * 2];
	}

	public int Version { get; private set; }

	public int CommandCapacity => _commands.Length;

	public int PointCapacity => _points.Length / 2;

	public ReadOnlySpan<UiVectorCommand> Commands => _commands.AsSpan(0, _commandCount);

	/// <summary>交错的 x、y；命令用 <see cref="UiVectorCommand.PointStart"/> 和 <see cref="UiVectorCommand.PointCount"/> 按点索引。</summary>
	public ReadOnlySpan<float> Points => _points.AsSpan(0, _pointCount * 2);

	public void Clear()
	{
		ThrowIfPathOpen();
		_commandCount = 0;
		_pointCount = 0;
		Version++;
	}

	public void FillRect(float x, float y, float width, float height, UiColor color)
	{
		ThrowIfPathOpen();
		Append(new UiVectorCommand(UiVectorCommandKind.FillRect, color, 0f, x, y, width, height, 0, 0));
	}

	public void BeginPolyline(UiColor color, float strokeWidth)
	{
		if (strokeWidth <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(strokeWidth), strokeWidth, "Stroke width must be positive.");
		}
		BeginPath(UiVectorCommandKind.StrokePolyline, color, strokeWidth);
	}

	public void BeginPolygon(UiColor fill)
	{
		BeginPath(UiVectorCommandKind.FillPolygon, fill, 0f);
	}

	public void AddPoint(float x, float y)
	{
		if (_openStart < 0)
		{
			throw new InvalidOperationException("AddPoint requires an open polyline or polygon.");
		}
		if (_pointCount >= PointCapacity)
		{
			throw new InvalidOperationException($"UiVectorDrawList point capacity {PointCapacity} exceeded.");
		}
		_points[_pointCount * 2] = x;
		_points[_pointCount * 2 + 1] = y;
		_pointCount++;
	}

	public void EndPath()
	{
		if (_openStart < 0)
		{
			throw new InvalidOperationException("EndPath requires an open polyline or polygon.");
		}
		int start = _openStart;
		_openStart = -1;
		Append(new UiVectorCommand(_openKind, _openColor, _openStrokeWidth, 0f, 0f, 0f, 0f, start, _pointCount - start));
	}

	private void BeginPath(UiVectorCommandKind kind, UiColor color, float strokeWidth)
	{
		ThrowIfPathOpen();
		_openStart = _pointCount;
		_openKind = kind;
		_openColor = color;
		_openStrokeWidth = strokeWidth;
	}

	private void Append(in UiVectorCommand command)
	{
		if (_commandCount >= _commands.Length)
		{
			throw new InvalidOperationException($"UiVectorDrawList command capacity {_commands.Length} exceeded.");
		}
		_commands[_commandCount++] = command;
		Version++;
	}

	private void ThrowIfPathOpen()
	{
		if (_openStart >= 0)
		{
			throw new InvalidOperationException("Close the open path with EndPath first.");
		}
	}
}
