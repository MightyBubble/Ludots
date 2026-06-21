using System;

namespace Ludots.UI.Browser;

public readonly record struct BrowserViewport
{
	public BrowserViewport(int width, int height, float deviceScaleFactor = 1f)
	{
		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width), "Viewport width must be greater than zero.");
		}
		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height), "Viewport height must be greater than zero.");
		}
		if (deviceScaleFactor <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(deviceScaleFactor), "Device scale factor must be greater than zero.");
		}

		Width = width;
		Height = height;
		DeviceScaleFactor = deviceScaleFactor;
	}

	public int Width { get; }

	public int Height { get; }

	public float DeviceScaleFactor { get; }
}
