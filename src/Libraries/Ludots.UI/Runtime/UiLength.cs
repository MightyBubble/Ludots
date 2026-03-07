namespace Ludots.UI.Runtime;

public readonly record struct UiLength(float Value, UiLengthUnit Unit)
{
    public static UiLength Auto => new(0f, UiLengthUnit.Auto);

    public static UiLength Px(float value)
    {
        return new UiLength(value, UiLengthUnit.Pixel);
    }

    public static UiLength Percent(float value)
    {
        return new UiLength(value, UiLengthUnit.Percent);
    }

    public bool IsAuto => Unit == UiLengthUnit.Auto;

    public float Resolve(float available)
    {
        return Unit switch
        {
            UiLengthUnit.Pixel => Value,
            UiLengthUnit.Percent => available * (Value / 100f),
            _ => float.NaN
        };
    }

    public override string ToString()
    {
        return Unit switch
        {
            UiLengthUnit.Pixel => $"{Value}px",
            UiLengthUnit.Percent => $"{Value}%",
            _ => "auto"
        };
    }
}
