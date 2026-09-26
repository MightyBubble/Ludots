using System;

namespace Ludots.UI.Browser;

public sealed class BrowserSurfaceHitTestOptions
{
	public static BrowserSurfaceHitTestOptions Bounds { get; } = new();

	public static BrowserSurfaceHitTestOptions Alpha(
		byte alphaThreshold = 8,
		BrowserHitMaskColor? hitMaskColor = null)
	{
		return new BrowserSurfaceHitTestOptions
		{
			Mode = BrowserSurfaceHitTestMode.Alpha,
			AlphaThreshold = alphaThreshold,
			HitMaskColor = hitMaskColor ?? BrowserHitMaskColor.Default
		};
	}

	public BrowserSurfaceHitTestMode Mode { get; init; } = BrowserSurfaceHitTestMode.Bounds;

	public byte AlphaThreshold { get; init; } = 8;

	public BrowserHitMaskColor? HitMaskColor { get; init; }

	public void Validate()
	{
		if (!Enum.IsDefined(typeof(BrowserSurfaceHitTestMode), Mode))
		{
			throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "Unsupported browser surface hit-test mode.");
		}
	}
}
