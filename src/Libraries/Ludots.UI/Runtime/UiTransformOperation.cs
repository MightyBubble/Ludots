using System;

namespace Ludots.UI.Runtime;

public readonly record struct UiTransformOperation(UiTransformOperationKind Kind, UiLength XLength, UiLength YLength, float ScaleX, float ScaleY, float AngleDegrees)
{
	public static UiTransformOperation Translate(UiLength x, UiLength y)
	{
		return new UiTransformOperation(UiTransformOperationKind.Translate, x, y, 1f, 1f, 0f);
	}

	public static UiTransformOperation Scale(float x, float y)
	{
		return new UiTransformOperation(UiTransformOperationKind.Scale, UiLength.Auto, UiLength.Auto, x, y, 0f);
	}

	public static UiTransformOperation Rotate(float angleDegrees)
	{
		return new UiTransformOperation(UiTransformOperationKind.Rotate, UiLength.Auto, UiLength.Auto, 1f, 1f, angleDegrees);
	}

	public override string ToString()
	{
		UiTransformOperationKind kind = Kind;
		if (1 == 0)
		{
		}
		string result = kind switch
		{
			UiTransformOperationKind.Translate => $"translate({XLength}, {YLength})", 
			UiTransformOperationKind.Scale => (!(Math.Abs(ScaleX - ScaleY) < 0.001f)) ? $"scale({ScaleX:0.###}, {ScaleY:0.###})" : $"scale({ScaleX:0.###})", 
			UiTransformOperationKind.Rotate => $"rotate({AngleDegrees:0.###}deg)", 
			_ => string.Empty, 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
