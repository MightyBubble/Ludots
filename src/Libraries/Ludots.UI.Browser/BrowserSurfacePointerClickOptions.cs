using System;

namespace Ludots.UI.Browser;

public sealed class BrowserSurfacePointerClickOptions
{
	public int DoubleClickMaxDelayMilliseconds { get; init; } = 500;

	public float DoubleClickMaxDistancePixels { get; init; } = 8f;

	public void Validate()
	{
		if (DoubleClickMaxDelayMilliseconds <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(DoubleClickMaxDelayMilliseconds),
				DoubleClickMaxDelayMilliseconds,
				"Browser double-click delay must be positive.");
		}

		if (DoubleClickMaxDistancePixels <= 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(DoubleClickMaxDistancePixels),
				DoubleClickMaxDistancePixels,
				"Browser double-click distance must be positive.");
		}
	}
}
