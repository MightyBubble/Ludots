using System;

namespace Ludots.UI.Browser;

public readonly record struct BrowserDirtyRect
{
	public BrowserDirtyRect(int x, int y, int width, int height)
	{
		if (x < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(x), "Dirty rect X must be non-negative.");
		}
		if (y < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(y), "Dirty rect Y must be non-negative.");
		}
		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width), "Dirty rect width must be greater than zero.");
		}
		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height), "Dirty rect height must be greater than zero.");
		}

		X = x;
		Y = y;
		Width = width;
		Height = height;
	}

	public int X { get; }

	public int Y { get; }

	public int Width { get; }

	public int Height { get; }

	public int Right => X + Width;

	public int Bottom => Y + Height;
}
