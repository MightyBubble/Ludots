namespace Ludots.UI.Runtime;

public readonly struct UiGridTrack
{
	public static UiGridTrack Auto { get; } = new UiGridTrack(UiGridTrackSizing.Auto, 0f);

	public UiGridTrackSizing Sizing { get; }

	public float Value { get; }

	public UiGridTrack(UiGridTrackSizing sizing, float value)
	{
		Sizing = sizing;
		Value = value;
	}

	public static UiGridTrack Px(float value) => new UiGridTrack(UiGridTrackSizing.Pixel, value);

	public static UiGridTrack Percent(float value) => new UiGridTrack(UiGridTrackSizing.Percent, value);

	public static UiGridTrack Fr(float value) => new UiGridTrack(UiGridTrackSizing.Fr, value);
}
