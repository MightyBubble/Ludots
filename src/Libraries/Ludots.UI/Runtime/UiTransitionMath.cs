using System;

namespace Ludots.UI.Runtime;

internal static class UiTransitionMath
{
	public static float Evaluate(UiTransitionEasing easing, float progress)
	{
		progress = Math.Clamp(progress, 0f, 1f);
		return easing switch
		{
			UiTransitionEasing.Linear => progress,
			UiTransitionEasing.EaseIn => progress * progress,
			UiTransitionEasing.EaseOut => 1f - (1f - progress) * (1f - progress),
			UiTransitionEasing.EaseInOut => (progress < 0.5f) ? (2f * progress * progress) : (1f - MathF.Pow(-2f * progress + 2f, 2f) / 2f),
			_ => CubicBezierApproximate(progress, 0.25f, 0.1f, 0.25f, 1f),
		};
	}

	public static float Lerp(float start, float end, float progress)
	{
		return start + (end - start) * progress;
	}

	public static UiColor Lerp(UiColor start, UiColor end, float progress)
	{
		byte red = (byte)Math.Clamp(MathF.Round(Lerp((int)start.Red, (int)end.Red, progress)), 0f, 255f);
		byte green = (byte)Math.Clamp(MathF.Round(Lerp((int)start.Green, (int)end.Green, progress)), 0f, 255f);
		byte blue = (byte)Math.Clamp(MathF.Round(Lerp((int)start.Blue, (int)end.Blue, progress)), 0f, 255f);
		byte alpha = (byte)Math.Clamp(MathF.Round(Lerp((int)start.Alpha, (int)end.Alpha, progress)), 0f, 255f);
		return new UiColor(red, green, blue, alpha);
	}

	public static bool AreCompatible(UiTransform start, UiTransform end)
	{
		if (start.Operations.Count != end.Operations.Count)
		{
			return false;
		}
		for (int i = 0; i < start.Operations.Count; i++)
		{
			UiTransformOperation a = start.Operations[i];
			UiTransformOperation b = end.Operations[i];
			if (a.Kind != b.Kind)
			{
				return false;
			}
			if (a.Kind == UiTransformOperationKind.Translate
				&& (a.XLength.Unit != b.XLength.Unit || a.YLength.Unit != b.YLength.Unit))
			{
				return false;
			}
		}
		return true;
	}

	public static bool TryLerp(UiTransform start, UiTransform end, float progress, out UiTransform result)
	{
		if (!AreCompatible(start, end))
		{
			result = UiTransform.Identity;
			return false;
		}
		if (start.Operations.Count == 0)
		{
			result = UiTransform.Identity;
			return true;
		}
		UiTransformOperation[] operations = new UiTransformOperation[start.Operations.Count];
		for (int i = 0; i < operations.Length; i++)
		{
			UiTransformOperation a = start.Operations[i];
			UiTransformOperation b = end.Operations[i];
			operations[i] = a.Kind switch
			{
				UiTransformOperationKind.Translate => UiTransformOperation.Translate(
					new UiLength(Lerp(a.XLength.Value, b.XLength.Value, progress), a.XLength.Unit),
					new UiLength(Lerp(a.YLength.Value, b.YLength.Value, progress), a.YLength.Unit)),
				UiTransformOperationKind.Scale => UiTransformOperation.Scale(
					Lerp(a.ScaleX, b.ScaleX, progress),
					Lerp(a.ScaleY, b.ScaleY, progress)),
				UiTransformOperationKind.Rotate => UiTransformOperation.Rotate(
					Lerp(a.AngleDegrees, b.AngleDegrees, progress)),
				_ => a,
			};
		}
		result = new UiTransform(operations);
		return true;
	}

	public static UiStyle Apply(UiStyle style, UiTransitionChannelState channel)
	{
		return channel.ValueKind switch
		{
			UiTransitionValueKind.Float => ApplyFloat(style, channel.PropertyName, channel.CurrentFloat),
			UiTransitionValueKind.Color => ApplyColor(style, channel.PropertyName, channel.CurrentColor),
			UiTransitionValueKind.Transform => ApplyTransform(style, channel.CurrentTransform),
			_ => style,
		};
	}

	public static UiStyle ApplyFloat(UiStyle style, string propertyName, float value)
	{
		return propertyName switch
		{
			"opacity" => style with
			{
				Opacity = Math.Clamp(value, 0f, 1f)
			},
			"filter" => style with
			{
				FilterBlurRadius = Math.Max(0f, value)
			},
			"backdrop-filter" => style with
			{
				BackdropBlurRadius = Math.Max(0f, value)
			},
			_ => style,
		};
	}

	public static UiStyle ApplyColor(UiStyle style, string propertyName, UiColor value)
	{
		return propertyName switch
		{
			"background-color" => style with
			{
				BackgroundColor = value
			},
			"border-color" => style with
			{
				BorderColor = value
			},
			"outline-color" => style with
			{
				OutlineColor = value
			},
			"color" => style with
			{
				Color = value
			},
			_ => style,
		};
	}

	public static UiStyle ApplyTransform(UiStyle style, UiTransform value)
	{
		return style with
		{
			Transform = value ?? UiTransform.Identity
		};
	}

	private static float CubicBezierApproximate(float progress, float x1, float y1, float x2, float y2)
	{
		float num = progress;
		for (int i = 0; i < 5; i++)
		{
			float num2 = SampleCubic(num, 0f, x1, x2, 1f) - progress;
			float num3 = SampleCubicDerivative(num, 0f, x1, x2, 1f);
			if (Math.Abs(num3) < 0.0001f)
			{
				break;
			}
			num = Math.Clamp(num - num2 / num3, 0f, 1f);
		}
		return SampleCubic(num, 0f, y1, y2, 1f);
	}

	private static float SampleCubic(float t, float a, float b, float c, float d)
	{
		float num = 1f - t;
		return num * num * num * a + 3f * num * num * t * b + 3f * num * t * t * c + t * t * t * d;
	}

	private static float SampleCubicDerivative(float t, float a, float b, float c, float d)
	{
		float num = 1f - t;
		return 3f * num * num * (b - a) + 6f * num * t * (c - b) + 3f * t * t * (d - c);
	}
}
