using System;

namespace Ludots.UI.Runtime;

public readonly record struct UiLength(float Value, UiLengthUnit Unit, UiCalcExpression? Calc = null)
{
	public static UiLength Auto => new UiLength(0f, UiLengthUnit.Auto);

	public bool IsAuto => Unit == UiLengthUnit.Auto;

	public bool IsCalc => Unit == UiLengthUnit.Calc;

	public bool IsViewportUnit => Unit == UiLengthUnit.Vw || Unit == UiLengthUnit.Vh || Unit == UiLengthUnit.Vmin || Unit == UiLengthUnit.Vmax;

	public static UiLength Px(float value)
	{
		return new UiLength(value, UiLengthUnit.Pixel);
	}

	public static UiLength Percent(float value)
	{
		return new UiLength(value, UiLengthUnit.Percent);
	}

	public static UiLength Viewport(float value, UiLengthUnit unit)
	{
		if (unit != UiLengthUnit.Vw && unit != UiLengthUnit.Vh && unit != UiLengthUnit.Vmin && unit != UiLengthUnit.Vmax)
		{
			throw new ArgumentOutOfRangeException(nameof(unit), "Viewport factory accepts Vw, Vh, Vmin or Vmax only.");
		}
		return new UiLength(value, unit);
	}

	public static UiLength CalcExpression(UiCalcExpression expression)
	{
		ArgumentNullException.ThrowIfNull(expression, "expression");
		return new UiLength(0f, UiLengthUnit.Calc, expression);
	}

	/// <summary>
	/// Resolves to pixels. Percent resolves against context.Available; viewport units resolve
	/// against context viewport dimensions; calc evaluates the expression tree.
	/// </summary>
	public float Resolve(UiLengthContext context)
	{
		UiLengthUnit unit = Unit;
		if (1 == 0)
		{
		}
		float result = unit switch
		{
			UiLengthUnit.Pixel => Value,
			UiLengthUnit.Percent => context.Available * (Value / 100f),
			UiLengthUnit.Vw => ResolveViewportUnit(context.ViewportWidth, Value),
			UiLengthUnit.Vh => ResolveViewportUnit(context.ViewportHeight, Value),
			UiLengthUnit.Vmin => ResolveViewportUnit(context.ViewportMin, Value),
			UiLengthUnit.Vmax => ResolveViewportUnit(context.ViewportMax, Value),
			UiLengthUnit.Calc => Calc?.Evaluate(context) ?? float.NaN,
			_ => float.NaN,
		};
		if (1 == 0)
		{
		}
		return result;
	}

	/// <summary>
	/// Legacy overload: resolves with the given containing-block size and no viewport context.
	/// Viewport units throw (fail fast) because they cannot resolve without viewport dimensions.
	/// </summary>
	public float Resolve(float available)
	{
		return Resolve(new UiLengthContext(available));
	}

	private static float ResolveViewportUnit(float viewportDimension, float value)
	{
		if (viewportDimension <= 0f)
		{
			throw new InvalidOperationException("Viewport length cannot be resolved without viewport dimensions; pass a UiLengthContext with viewport width/height.");
		}
		return viewportDimension * (value / 100f);
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
			UiLengthUnit.Vw => $"{Value}vw",
			UiLengthUnit.Vh => $"{Value}vh",
			UiLengthUnit.Vmin => $"{Value}vmin",
			UiLengthUnit.Vmax => $"{Value}vmax",
			UiLengthUnit.Calc => $"calc({Calc})",
			_ => "auto",
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
