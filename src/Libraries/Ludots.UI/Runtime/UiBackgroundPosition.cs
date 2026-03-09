namespace Ludots.UI.Runtime;

public readonly record struct UiBackgroundPosition(UiLength X, UiLength Y)
{
    public static UiBackgroundPosition TopLeft => new(UiLength.Percent(0f), UiLength.Percent(0f));

    public static UiBackgroundPosition Center => new(UiLength.Percent(50f), UiLength.Percent(50f));

    public static UiBackgroundPosition BottomRight => new(UiLength.Percent(100f), UiLength.Percent(100f));
}
