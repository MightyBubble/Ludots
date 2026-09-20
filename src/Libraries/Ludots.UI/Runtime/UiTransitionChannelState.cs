using System;

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

	public UiColor StartColor { get; }

	public UiColor EndColor { get; }

	public UiTransform StartTransform { get; }

	public UiTransform EndTransform { get; }

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

	public UiColor CurrentColor
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

	public UiTransform CurrentTransform
	{
		get
		{
			UiTransform start = StartTransform ?? UiTransform.Identity;
			UiTransform end = EndTransform ?? UiTransform.Identity;
			if (ElapsedSeconds <= DelaySeconds)
			{
				return start;
			}
			float progress = Math.Clamp((ElapsedSeconds - DelaySeconds) / DurationSeconds, 0f, 1f);
			float eased = UiTransitionMath.Evaluate(Easing, progress);
			if (!UiTransitionMath.TryLerp(start, end, eased, out UiTransform lerped))
			{
				return start;
			}
			return lerped;
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
		StartTransform = UiTransform.Identity;
		EndTransform = UiTransform.Identity;
	}

	public UiTransitionChannelState(string propertyName, float durationSeconds, float delaySeconds, UiTransitionEasing easing, UiColor startColor, UiColor endColor)
	{
		PropertyName = propertyName;
		DurationSeconds = Math.Max(0.0001f, durationSeconds);
		DelaySeconds = Math.Max(0f, delaySeconds);
		Easing = easing;
		ValueKind = UiTransitionValueKind.Color;
		StartColor = startColor;
		EndColor = endColor;
		StartTransform = UiTransform.Identity;
		EndTransform = UiTransform.Identity;
	}

	public UiTransitionChannelState(string propertyName, float durationSeconds, float delaySeconds, UiTransitionEasing easing, UiTransform startTransform, UiTransform endTransform)
	{
		PropertyName = propertyName;
		DurationSeconds = Math.Max(0.0001f, durationSeconds);
		DelaySeconds = Math.Max(0f, delaySeconds);
		Easing = easing;
		ValueKind = UiTransitionValueKind.Transform;
		StartTransform = startTransform ?? UiTransform.Identity;
		EndTransform = endTransform ?? UiTransform.Identity;
	}

	public void Advance(float deltaSeconds)
	{
		ElapsedSeconds = Math.Max(0f, ElapsedSeconds + Math.Max(0f, deltaSeconds));
	}
}
