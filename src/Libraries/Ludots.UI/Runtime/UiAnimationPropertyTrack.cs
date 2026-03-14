using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

internal sealed class UiAnimationPropertyTrack
{
	private readonly UiAnimationTrackKind _kind;

	private readonly UiAnimationFloatStop[] _floatStops;

	private readonly UiAnimationColorStop[] _colorStops;

	public string PropertyName { get; }

	private UiAnimationPropertyTrack(string propertyName, UiAnimationFloatStop[] floatStops)
	{
		PropertyName = propertyName;
		_kind = UiAnimationTrackKind.Float;
		_floatStops = floatStops;
		_colorStops = Array.Empty<UiAnimationColorStop>();
	}

	private UiAnimationPropertyTrack(string propertyName, UiAnimationColorStop[] colorStops)
	{
		PropertyName = propertyName;
		_kind = UiAnimationTrackKind.Color;
		_colorStops = colorStops;
		_floatStops = Array.Empty<UiAnimationFloatStop>();
	}

	public static UiAnimationPropertyTrack CreateFloat(string propertyName, UiAnimationFloatStop[] stops)
	{
		return new UiAnimationPropertyTrack(propertyName, stops);
	}

	public static UiAnimationPropertyTrack CreateColor(string propertyName, UiAnimationColorStop[] stops)
	{
		return new UiAnimationPropertyTrack(propertyName, stops);
	}

	public UiStyle Apply(UiStyle style, float progress)
	{
		UiAnimationTrackKind kind = _kind;
		if (1 == 0)
		{
		}
		UiStyle result = ((kind != UiAnimationTrackKind.Float) ? UiTransitionMath.ApplyColor(style, PropertyName, Evaluate(_colorStops, progress)) : UiTransitionMath.ApplyFloat(style, PropertyName, Evaluate(_floatStops, progress)));
		if (1 == 0)
		{
		}
		return result;
	}

	private static float Evaluate(IReadOnlyList<UiAnimationFloatStop> stops, float progress)
	{
		if (stops.Count == 0)
		{
			return 0f;
		}
		if (progress <= stops[0].Offset)
		{
			return stops[0].Value;
		}
		for (int i = 1; i < stops.Count; i++)
		{
			UiAnimationFloatStop uiAnimationFloatStop = stops[i];
			if (!(progress > uiAnimationFloatStop.Offset))
			{
				UiAnimationFloatStop uiAnimationFloatStop2 = stops[i - 1];
				float num = Math.Max(0.0001f, uiAnimationFloatStop.Offset - uiAnimationFloatStop2.Offset);
				float progress2 = Math.Clamp((progress - uiAnimationFloatStop2.Offset) / num, 0f, 1f);
				return UiTransitionMath.Lerp(uiAnimationFloatStop2.Value, uiAnimationFloatStop.Value, progress2);
			}
		}
		return stops[stops.Count - 1].Value;
	}

	private static UiColor Evaluate(IReadOnlyList<UiAnimationColorStop> stops, float progress)
	{
		if (stops.Count == 0)
		{
			return UiColor.Transparent;
		}
		if (progress <= stops[0].Offset)
		{
			return stops[0].Value;
		}
		for (int i = 1; i < stops.Count; i++)
		{
			UiAnimationColorStop uiAnimationColorStop = stops[i];
			if (!(progress > uiAnimationColorStop.Offset))
			{
				UiAnimationColorStop uiAnimationColorStop2 = stops[i - 1];
				float num = Math.Max(0.0001f, uiAnimationColorStop.Offset - uiAnimationColorStop2.Offset);
				float progress2 = Math.Clamp((progress - uiAnimationColorStop2.Offset) / num, 0f, 1f);
				return UiTransitionMath.Lerp(uiAnimationColorStop2.Value, uiAnimationColorStop.Value, progress2);
			}
		}
		return stops[stops.Count - 1].Value;
	}
}
