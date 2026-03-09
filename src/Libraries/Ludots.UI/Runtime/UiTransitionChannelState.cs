using System;
using SkiaSharp;

namespace Ludots.UI.Runtime;

internal sealed class UiTransitionChannelState
{
	public string PropertyName { get; }

	public float DurationSeconds { get; }

	public float DelaySeconds { get; }

	public UiTransitionEasing Easing { get; }

	public UiTransitionValueKind ValueKind { get; }

	public float ElapsedSeconds { get; private set; }

	public float StartFloat { get; }

	public float EndFloat { get; }

	public SKColor StartColor { get; }

	public SKColor EndColor { get; }

	public bool IsCompleted => ElapsedSeconds >= DelaySeconds + DurationSeconds;

	public float CurrentFloat
	{
		get
		{
			if (ElapsedSeconds <= DelaySeconds)
			{
				return StartFloat;
			}
			float progress = Math.Clamp((ElapsedSeconds - DelaySeconds) / DurationSeconds, 0f, 1f);
			return UiTransitionMath.Lerp(StartFloat, EndFloat, UiTransitionMath.Evaluate(Easing, progress));
		}
	}

	public SKColor CurrentColor
	{
		get
		{
			if (ElapsedSeconds <= DelaySeconds)
			{
				return StartColor;
			}
			float progress = Math.Clamp((ElapsedSeconds - DelaySeconds) / DurationSeconds, 0f, 1f);
			return UiTransitionMath.Lerp(StartColor, EndColor, UiTransitionMath.Evaluate(Easing, progress));
		}
	}

	public UiTransitionChannelState(string propertyName, float durationSeconds, float delaySeconds, UiTransitionEasing easing, float startFloat, float endFloat)
	{
		PropertyName = propertyName;
		DurationSeconds = Math.Max(0.0001f, durationSeconds);
		DelaySeconds = Math.Max(0f, delaySeconds);
		Easing = easing;
		ValueKind = UiTransitionValueKind.Float;
		StartFloat = startFloat;
		EndFloat = endFloat;
	}

	public UiTransitionChannelState(string propertyName, float durationSeconds, float delaySeconds, UiTransitionEasing easing, SKColor startColor, SKColor endColor)
	{
		PropertyName = propertyName;
		DurationSeconds = Math.Max(0.0001f, durationSeconds);
		DelaySeconds = Math.Max(0f, delaySeconds);
		Easing = easing;
		ValueKind = UiTransitionValueKind.Color;
		StartColor = startColor;
		EndColor = endColor;
	}

	public void Advance(float deltaSeconds)
	{
		ElapsedSeconds = Math.Max(0f, ElapsedSeconds + Math.Max(0f, deltaSeconds));
	}
}
