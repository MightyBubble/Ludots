namespace Ludots.UI.Runtime;

public readonly record struct UiBackgroundSize(UiBackgroundSizeMode Mode, UiLength Width, UiLength Height)
{
    public static UiBackgroundSize Auto => new(UiBackgroundSizeMode.Auto, UiLength.Auto, UiLength.Auto);

    public static UiBackgroundSize Cover => new(UiBackgroundSizeMode.Cover, UiLength.Auto, UiLength.Auto);

    public static UiBackgroundSize Contain => new(UiBackgroundSizeMode.Contain, UiLength.Auto, UiLength.Auto);

    public static UiBackgroundSize Explicit(UiLength width, UiLength height)
    {
        return new UiBackgroundSize(UiBackgroundSizeMode.Explicit, width, height);
    }
}
