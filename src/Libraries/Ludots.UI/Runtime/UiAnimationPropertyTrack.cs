using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

internal sealed class UiAnimationPropertyTrack
{
	private readonly UiAnimationTrackKind _kind;

	private readonly UiAnimationFloatStop[] _floatStops;

	private readonly UiAnimationColorStop[] _colorStops;

	private readonly UiAnimationTransformStop[] _transformStops;

	public string PropertyName { get; }

	private UiAnimationPropertyTrack(string propertyName, UiAnimationFloatStop[] floatStops)
	{
		PropertyName = propertyName;
		_kind = UiAnimationTrackKind.Float;
		_floatStops = floatStops;
		_colorStops = Array.Empty<UiAnimationColorStop>();
		_transformStops = Array.Empty<UiAnimationTransformStop>();
	}

	private UiAnimationPropertyTrack(string propertyName, UiAnimationColorStop[] colorStops)
	{
		PropertyName = propertyName;
		_kind = UiAnimationTrackKind.Color;
		_colorStops = colorStops;
		_floatStops = Array.Empty<UiAnimationFloatStop>();
		_transformStops = Array.Empty<UiAnimationTransformStop>();
	}

	private UiAnimationPropertyTrack(string propertyName, UiAnimationTransformStop[] transformStops)
	{
		PropertyName = propertyName;
		_kind = UiAnimationTrackKind.Transform;
		_transformStops = transformStops;
		_floatStops = Array.Empty<UiAnimationFloatStop>();
		_colorStops = Array.Empty<UiAnimationColorStop>();
	}

	public static UiAnimationPropertyTrack CreateFloat(string propertyName, UiAnimationFloatStop[] stops)
	{
		return new UiAnimationPropertyTrack(propertyName, stops);
	}

	public static UiAnimationPropertyTrack CreateColor(string propertyName, UiAnimationColorStop[] stops)
	{
		return new UiAnimationPropertyTrack(propertyName, stops);
	}

	public static UiAnimationPropertyTrack CreateTransform(string propertyName, UiAnimationTransformStop[] stops)
	{
		return new UiAnimationPropertyTrack(propertyName, stops);
	}

	public UiStyle Apply(UiStyle style, float progress)
	{
		return _kind switch
		{
			UiAnimationTrackKind.Float => UiTransitionMath.ApplyFloat(style, PropertyName, Evaluate(_floatStops, progress)),
			UiAnimationTrackKind.Color => UiTransitionMath.ApplyColor(style, PropertyName, Evaluate(_colorStops, progress)),
			UiAnimationTrackKind.Transform => UiTransitionMath.ApplyTransform(style, Evaluate(_transformStops, progress)),
			_ => style,
		};
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
			UiAnimationFloatStop next = stops[i];
			if (progress > next.Offset)
			{
				continue;
			}
			UiAnimationFloatStop previous = stops[i - 1];
			float span = Math.Max(0.0001f, next.Offset - previous.Offset);
			float local = Math.Clamp((progress - previous.Offset) / span, 0f, 1f);
			return UiTransitionMath.Lerp(previous.Value, next.Value, local);
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
			UiAnimationColorStop next = stops[i];
			if (progress > next.Offset)
			{
				continue;
			}
			UiAnimationColorStop previous = stops[i - 1];
			float span = Math.Max(0.0001f, next.Offset - previous.Offset);
			float local = Math.Clamp((progress - previous.Offset) / span, 0f, 1f);
			return UiTransitionMath.Lerp(previous.Value, next.Value, local);
		}
		return stops[stops.Count - 1].Value;
	}

	private static UiTransform Evaluate(IReadOnlyList<UiAnimationTransformStop> stops, float progress)
	{
		if (stops.Count == 0)
		{
			return UiTransform.Identity;
		}
		if (progress <= stops[0].Offset)
		{
			return stops[0].Value;
		}
		for (int i = 1; i < stops.Count; i++)
		{
			UiAnimationTransformStop next = stops[i];
			if (progress > next.Offset)
			{
				continue;
			}
			UiAnimationTransformStop previous = stops[i - 1];
			float span = Math.Max(0.0001f, next.Offset - previous.Offset);
			float local = Math.Clamp((progress - previous.Offset) / span, 0f, 1f);
			if (!UiTransitionMath.TryLerp(previous.Value, next.Value, local, out UiTransform lerped))
			{
				return previous.Value;
			}
			return lerped;
		}
		return stops[stops.Count - 1].Value;
	}
}
