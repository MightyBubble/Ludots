namespace Ludots.UI.Runtime;

public readonly record struct UiLength(float Value, UiLengthUnit Unit)
{
	public static UiLength Auto => new UiLength(0f, UiLengthUnit.Auto);

	public bool IsAuto => Unit == UiLengthUnit.Auto;

	public static UiLength Px(float value)
	{
		return new UiLength(value, UiLengthUnit.Pixel);
	}

	public static UiLength Percent(float value)
	{
		return new UiLength(value, UiLengthUnit.Percent);
	}

	public float Resolve(float available)
	{
		UiLengthUnit unit = Unit;
		if (1 == 0)
		{
		}
		float result = unit switch
		{
			UiLengthUnit.Pixel => Value, 
			UiLengthUnit.Percent => available * (Value / 100f), 
			_ => float.NaN, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public override string ToString()
	{
		UiLengthUnit unit = Unit;
		if (1 == 0)
		{
		}
		string result = unit switch
		{
			UiLengthUnit.Pixel => $"{Value}px", 
			UiLengthUnit.Percent => $"{Value}%", 
			_ => "auto", 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
