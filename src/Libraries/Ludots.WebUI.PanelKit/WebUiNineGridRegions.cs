namespace Ludots.WebUI.PanelKit;

/// <summary>
/// Canonical 3x3 HUD surface region ids. Center is reserved for the 3D world viewport
/// (no opaque resident panel); overlays/modals may temporarily cover it.
/// </summary>
public static class WebUiNineGridRegions
{
	public const string TopLeft = "region.top-left";
	public const string TopCenter = "region.top-center";
	public const string TopRight = "region.top-right";
	public const string MiddleLeft = "region.middle-left";
	public const string Center = "region.center";
	public const string MiddleRight = "region.middle-right";
	public const string BottomLeft = "region.bottom-left";
	public const string BottomCenter = "region.bottom-center";
	public const string BottomRight = "region.bottom-right";

	public static IReadOnlyList<string> All { get; } =
	[
		TopLeft,
		TopCenter,
		TopRight,
		MiddleLeft,
		Center,
		MiddleRight,
		BottomLeft,
		BottomCenter,
		BottomRight,
	];

	/// <summary>
	/// Default HUD geometry per region, in percent of the host viewport. The Center rect serves
	/// overlays/modals that temporarily cover the reserved 3D world viewport; resident opaque
	/// panels there are rejected by the HUD binder.
	/// </summary>
	public static WebUiNineGridRegionRect GetDefaultGeometry(string regionId) =>
		regionId switch
		{
			TopLeft => new WebUiNineGridRegionRect(1f, 1f, 26f, 16f),
			TopCenter => new WebUiNineGridRegionRect(28f, 1f, 42f, 12f),
			TopRight => new WebUiNineGridRegionRect(71f, 1f, 28f, 18f),
			MiddleLeft => new WebUiNineGridRegionRect(1f, 18f, 22f, 52f),
			Center => new WebUiNineGridRegionRect(24f, 20f, 52f, 48f),
			MiddleRight => new WebUiNineGridRegionRect(77f, 18f, 22f, 52f),
			BottomLeft => new WebUiNineGridRegionRect(1f, 72f, 22f, 26f),
			BottomCenter => new WebUiNineGridRegionRect(24f, 70f, 52f, 28f),
			BottomRight => new WebUiNineGridRegionRect(77f, 72f, 22f, 26f),
			_ => throw new InvalidOperationException($"Nine-grid region '{regionId}' has no default geometry."),
		};
}

public readonly record struct WebUiNineGridRegionRect(
	float XPercent,
	float YPercent,
	float WidthPercent,
	float HeightPercent);
