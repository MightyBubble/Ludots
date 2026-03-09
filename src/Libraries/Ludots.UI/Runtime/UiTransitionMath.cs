using System;
using SkiaSharp;

namespace Ludots.UI.Runtime;

internal static class UiTransitionMath
{
	public static float Evaluate(UiTransitionEasing easing, float progress)
	{
		progress = Math.Clamp(progress, 0f, 1f);
		if (1 == 0)
		{
		}
		float result = easing switch
		{
			UiTransitionEasing.Linear => progress, 
			UiTransitionEasing.EaseIn => progress * progress, 
			UiTransitionEasing.EaseOut => 1f - (1f - progress) * (1f - progress), 
			UiTransitionEasing.EaseInOut => (progress < 0.5f) ? (2f * progress * progress) : (1f - MathF.Pow(-2f * progress + 2f, 2f) / 2f), 
			_ => CubicBezierApproximate(progress, 0.25f, 0.1f, 0.25f, 1f), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public static float Lerp(float start, float end, float progress)
	{
		return start + (end - start) * progress;
	}

	public static SKColor Lerp(SKColor start, SKColor end, float progress)
	{
		byte red = (byte)Math.Clamp(MathF.Round(Lerp((int)start.Red, (int)end.Red, progress)), 0f, 255f);
		byte green = (byte)Math.Clamp(MathF.Round(Lerp((int)start.Green, (int)end.Green, progress)), 0f, 255f);
		byte blue = (byte)Math.Clamp(MathF.Round(Lerp((int)start.Blue, (int)end.Blue, progress)), 0f, 255f);
		byte alpha = (byte)Math.Clamp(MathF.Round(Lerp((int)start.Alpha, (int)end.Alpha, progress)), 0f, 255f);
		return new SKColor(red, green, blue, alpha);
	}

	public static UiStyle Apply(UiStyle style, UiTransitionChannelState channel)
	{
		UiTransitionValueKind valueKind = channel.ValueKind;
		if (1 == 0)
		{
		}
		UiStyle result = ((valueKind != UiTransitionValueKind.Float) ? ApplyColor(style, channel.PropertyName, channel.CurrentColor) : ApplyFloat(style, channel.PropertyName, channel.CurrentFloat));
		if (1 == 0)
		{
		}
		return result;
	}

	public static UiStyle ApplyFloat(UiStyle style, string propertyName, float value)
	{
		if (1 == 0)
		{
		}
		UiStyle result = propertyName switch
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
		if (1 == 0)
		{
		}
		return result;
	}

	public static UiStyle ApplyColor(UiStyle style, string propertyName, SKColor value)
	{
		if (1 == 0)
		{
		}
		UiStyle result = propertyName switch
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
		if (1 == 0)
		{
		}
		return result;
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
