namespace Ludots.UI.Runtime;

public sealed record UiClipPath(UiClipPathKind Kind, UiThickness Inset, UiLength Radius, UiLength CenterX, UiLength CenterY)
{
    public static UiClipPath InsetShape(UiThickness inset)
    {
        return new UiClipPath(UiClipPathKind.Inset, inset, UiLength.Auto, UiLength.Percent(50f), UiLength.Percent(50f));
    }

    public static UiClipPath Circle(UiLength radius, UiLength centerX, UiLength centerY)
    {
        return new UiClipPath(UiClipPathKind.Circle, UiThickness.Zero, radius, centerX, centerY);
    }
}
