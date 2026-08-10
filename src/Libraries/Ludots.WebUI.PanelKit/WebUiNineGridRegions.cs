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
}
