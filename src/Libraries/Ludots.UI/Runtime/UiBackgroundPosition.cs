namespace Ludots.UI.Runtime;

public readonly record struct UiBackgroundPosition(UiLength X, UiLength Y)
{
	public static UiBackgroundPosition TopLeft => new UiBackgroundPosition(UiLength.Percent(0f), UiLength.Percent(0f));

	public static UiBackgroundPosition Center => new UiBackgroundPosition(UiLength.Percent(50f), UiLength.Percent(50f));

	public static UiBackgroundPosition BottomRight => new UiBackgroundPosition(UiLength.Percent(100f), UiLength.Percent(100f));
}
