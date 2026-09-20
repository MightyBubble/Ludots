namespace Ludots.UI.Runtime;

public readonly struct UiGridTrack
{
	public static UiGridTrack Auto { get; } = new UiGridTrack(UiGridTrackSizing.Auto, 0f, UiGridTrackSizing.Auto, 0f);

	public UiGridTrackSizing MinSizing { get; }

	public float MinValue { get; }

	public UiGridTrackSizing MaxSizing { get; }

	public float MaxValue { get; }

	public UiGridTrackSizing Sizing => MaxSizing;

	public float Value => MaxValue;

	public bool IsMinMax => MinSizing != MaxSizing || System.Math.Abs(MinValue - MaxValue) > 0.0001f;

	public UiGridTrack(UiGridTrackSizing sizing, float value)
		: this(sizing, value, sizing, value)
	{
	}

	public UiGridTrack(UiGridTrackSizing minSizing, float minValue, UiGridTrackSizing maxSizing, float maxValue)
	{
		MinSizing = minSizing;
		MinValue = minValue;
		MaxSizing = maxSizing;
		MaxValue = maxValue;
	}

	public static UiGridTrack Px(float value) => new UiGridTrack(UiGridTrackSizing.Pixel, value);

	public static UiGridTrack Percent(float value) => new UiGridTrack(UiGridTrackSizing.Percent, value);

	public static UiGridTrack Fr(float value) => new UiGridTrack(UiGridTrackSizing.Fr, value);

	public static UiGridTrack MinMax(UiGridTrack min, UiGridTrack max)
	{
		return new UiGridTrack(min.MinSizing, min.MinValue, max.MaxSizing, max.MaxValue);
	}
}
