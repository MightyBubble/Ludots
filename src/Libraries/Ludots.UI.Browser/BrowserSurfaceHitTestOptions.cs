using System;

namespace Ludots.UI.Browser;

public sealed class BrowserSurfaceHitTestOptions
{
	public static BrowserSurfaceHitTestOptions Bounds { get; } = new();

	public static BrowserSurfaceHitTestOptions Alpha(byte alphaThreshold = 8)
	{
		return new BrowserSurfaceHitTestOptions
		{
			Mode = BrowserSurfaceHitTestMode.Alpha,
			AlphaThreshold = alphaThreshold
		};
	}

	public BrowserSurfaceHitTestMode Mode { get; init; } = BrowserSurfaceHitTestMode.Bounds;

	public byte AlphaThreshold { get; init; } = 8;

	public void Validate()
	{
		if (!Enum.IsDefined(typeof(BrowserSurfaceHitTestMode), Mode))
		{
			throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "Unsupported browser surface hit-test mode.");
		}
	}
}
