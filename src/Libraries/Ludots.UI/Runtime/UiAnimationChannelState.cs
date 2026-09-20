using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

internal sealed class UiAnimationChannelState
{
	private readonly UiAnimationEntry _entry;

	private readonly List<UiAnimationPropertyTrack> _tracks;

	public float ElapsedSeconds { get; private set; }

	public bool HasTracks => _tracks.Count > 0;

	public bool IsDiscardable => !HasTracks || (!HasForwardFill && IsFinite && ElapsedSeconds >= ActiveEndSeconds);

	private bool HasBackwardFill
	{
		get
		{
			UiAnimationFillMode fillMode = _entry.FillMode;
			return (uint)(fillMode - 2) <= 1u;
		}
	}

	private bool HasForwardFill
	{
		get
		{
			UiAnimationFillMode fillMode = _entry.FillMode;
			return fillMode == UiAnimationFillMode.Forwards || fillMode == UiAnimationFillMode.Both;
		}
	}

	private bool IsFinite => !float.IsPositiveInfinity(_entry.IterationCount);

	private float ActiveDurationSeconds => _entry.DurationSeconds * Math.Max(0f, _entry.IterationCount);

	private float ActiveEndSeconds => _entry.DelaySeconds + ActiveDurationSeconds;

	public UiAnimationChannelState(UiAnimationEntry entry, UiStyle baseStyle)
	{
		_entry = entry;
		_tracks = BuildTracks(entry, baseStyle);
	}

	public void Advance(float deltaSeconds)
	{
		if (HasTracks && _entry.PlayState != UiAnimationPlayState.Paused && !(deltaSeconds <= 0f))
		{
			ElapsedSeconds = Math.Max(0f, ElapsedSeconds + deltaSeconds);
		}
	}

	public UiStyle Apply(UiStyle style)
	{
		if (!TryResolveDirectedProgress(out var progress))
		{
			return style;
		}
		UiStyle uiStyle = style;
		for (int i = 0; i < _tracks.Count; i++)
		{
			uiStyle = _tracks[i].Apply(uiStyle, progress);
		}
		return uiStyle;
	}

	private bool TryResolveDirectedProgress(out float progress)
	{
		progress = 0f;
		if (!HasTracks || _entry.DurationSeconds <= 0f)
		{
			return false;
		}
		float num = Math.Max(0f, _entry.IterationCount);
		if (num <= 0f)
		{
			return false;
		}
		float num2 = ElapsedSeconds - _entry.DelaySeconds;
		if (num2 < 0f)
		{
			if (!HasBackwardFill)
			{
				return false;
			}
			progress = ApplyDirection(0, 0f);
			return true;
		}
		if (IsFinite && num2 >= ActiveDurationSeconds)
		{
			if (!HasForwardFill)
			{
				return false;
			}
			progress = ResolveEndProgress(num);
			return true;
		}
		float num3 = num2 / _entry.DurationSeconds;
		int num4 = Math.Max(0, (int)MathF.Floor(num3));
		float progress2 = num3 - (float)num4;
		progress = ApplyDirection(num4, progress2);
		return true;
	}

	private float ResolveEndProgress(float iterationCount)
	{
		int iterationIndex = Math.Max(0, (int)MathF.Ceiling(iterationCount) - 1);
		float num = iterationCount % 1f;
		float progress = ((num <= 0.0001f) ? 1f : num);
		return ApplyDirection(iterationIndex, progress);
	}

	private float ApplyDirection(int iterationIndex, float progress)
	{
		UiAnimationDirection direction = _entry.Direction;
		bool reverse = direction switch
		{
			UiAnimationDirection.Reverse => true,
			UiAnimationDirection.Alternate => (iterationIndex & 1) == 1,
			UiAnimationDirection.AlternateReverse => (iterationIndex & 1) == 0,
			_ => false,
		};
		return reverse ? (1f - progress) : progress;
	}

	private static List<UiAnimationPropertyTrack> BuildTracks(UiAnimationEntry entry, UiStyle baseStyle)
	{
		List<UiAnimationPropertyTrack> list = new List<UiAnimationPropertyTrack>();
		UiKeyframeDefinition keyframes = entry.Keyframes;
		if (keyframes == null || keyframes.Stops.Count == 0)
		{
			return list;
		}
		Dictionary<string, Dictionary<float, UiColor>> colorTracks = new Dictionary<string, Dictionary<float, UiColor>>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Dictionary<float, float>> floatTracks = new Dictionary<string, Dictionary<float, float>>(StringComparer.OrdinalIgnoreCase);
		Dictionary<float, UiTransform> transformStops = new Dictionary<float, UiTransform>();
		bool transformDeclared = false;
		bool transformPoisoned = false;
		for (int i = 0; i < keyframes.Stops.Count; i++)
		{
			UiKeyframeStop uiKeyframeStop = keyframes.Stops[i];
			UiStyle uiStyle = baseStyle;
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string> item in uiKeyframeStop.Declaration)
			{
				if (!TryNormalizeAnimatedPropertyName(item.Key, out string? normalized) || normalized == null)
				{
					continue;
				}
				if (normalized == "transform")
				{
					transformDeclared = true;
					if (!UiStyleResolver.TryParseTransform(item.Value, out UiTransform? parsed) || parsed == null)
					{
						transformPoisoned = true;
						continue;
					}
					uiStyle = uiStyle with { Transform = parsed };
					hashSet.Add(normalized);
					continue;
				}
				uiStyle = UiStyleResolver.ApplyProperty(uiStyle, item.Key, item.Value);
				hashSet.Add(normalized);
			}
			float key = ClampOffset(uiKeyframeStop.Offset);
			foreach (string item2 in hashSet)
			{
				switch (item2)
				{
				case "background-color":
					GetOrCreateColorTrack(colorTracks, item2)[key] = uiStyle.BackgroundColor;
					break;
				case "border-color":
					GetOrCreateColorTrack(colorTracks, item2)[key] = uiStyle.BorderColor;
					break;
				case "outline-color":
					GetOrCreateColorTrack(colorTracks, item2)[key] = uiStyle.OutlineColor;
					break;
				case "color":
					GetOrCreateColorTrack(colorTracks, item2)[key] = uiStyle.Color;
					break;
				case "opacity":
					GetOrCreateFloatTrack(floatTracks, item2)[key] = uiStyle.Opacity;
					break;
				case "filter":
					GetOrCreateFloatTrack(floatTracks, item2)[key] = uiStyle.FilterBlurRadius;
					break;
				case "backdrop-filter":
					GetOrCreateFloatTrack(floatTracks, item2)[key] = uiStyle.BackdropBlurRadius;
					break;
				case "transform":
					transformStops[key] = uiStyle.Transform ?? UiTransform.Identity;
					break;
				}
			}
		}
		AddColorTrack(list, "background-color", baseStyle.BackgroundColor, colorTracks);
		AddColorTrack(list, "border-color", baseStyle.BorderColor, colorTracks);
		AddColorTrack(list, "outline-color", baseStyle.OutlineColor, colorTracks);
		AddColorTrack(list, "color", baseStyle.Color, colorTracks);
		AddFloatTrack(list, "opacity", baseStyle.Opacity, floatTracks);
		AddFloatTrack(list, "filter", baseStyle.FilterBlurRadius, floatTracks);
		AddFloatTrack(list, "backdrop-filter", baseStyle.BackdropBlurRadius, floatTracks);
		if (transformDeclared && !transformPoisoned)
		{
			AddTransformTrack(list, baseStyle.Transform ?? UiTransform.Identity, transformStops);
		}
		return list;
	}

	private static void AddColorTrack(ICollection<UiAnimationPropertyTrack> tracks, string propertyName, UiColor baseValue, IReadOnlyDictionary<string, Dictionary<float, UiColor>> values)
	{
		if (values.TryGetValue(propertyName, out Dictionary<float, UiColor>? value) && value.Count != 0)
		{
			value.TryAdd(0f, baseValue);
			value.TryAdd(1f, baseValue);
			UiAnimationColorStop[] array = (from pair in value
				orderby pair.Key
				select new UiAnimationColorStop(pair.Key, pair.Value)).ToArray();
			if (array.Length > 1)
			{
				tracks.Add(UiAnimationPropertyTrack.CreateColor(propertyName, array));
			}
		}
	}

	private static void AddFloatTrack(ICollection<UiAnimationPropertyTrack> tracks, string propertyName, float baseValue, IReadOnlyDictionary<string, Dictionary<float, float>> values)
	{
		if (values.TryGetValue(propertyName, out Dictionary<float, float>? value) && value.Count != 0)
		{
			value.TryAdd(0f, baseValue);
			value.TryAdd(1f, baseValue);
			UiAnimationFloatStop[] array = (from pair in value
				orderby pair.Key
				select new UiAnimationFloatStop(pair.Key, pair.Value)).ToArray();
			if (array.Length > 1)
			{
				tracks.Add(UiAnimationPropertyTrack.CreateFloat(propertyName, array));
			}
		}
	}

	private static void AddTransformTrack(ICollection<UiAnimationPropertyTrack> tracks, UiTransform baseValue, Dictionary<float, UiTransform> values)
	{
		if (values.Count == 0)
		{
			return;
		}
		if (!values.ContainsKey(0f))
		{
			UiTransform first = values.OrderBy(pair => pair.Key).First().Value;
			values[0f] = UiTransitionMath.AreCompatible(baseValue, first) ? baseValue : first;
		}
		if (!values.ContainsKey(1f))
		{
			UiTransform last = values.OrderBy(pair => pair.Key).Last().Value;
			values[1f] = UiTransitionMath.AreCompatible(baseValue, last) ? baseValue : last;
		}
		UiAnimationTransformStop[] array = (from pair in values
			orderby pair.Key
			select new UiAnimationTransformStop(pair.Key, pair.Value)).ToArray();
		if (array.Length <= 1)
		{
			return;
		}
		for (int i = 1; i < array.Length; i++)
		{
			if (!UiTransitionMath.AreCompatible(array[i - 1].Value, array[i].Value))
			{
				return;
			}
		}
		tracks.Add(UiAnimationPropertyTrack.CreateTransform("transform", array));
	}

	private static Dictionary<float, UiColor> GetOrCreateColorTrack(IDictionary<string, Dictionary<float, UiColor>> tracks, string propertyName)
	{
		if (!tracks.TryGetValue(propertyName, out Dictionary<float, UiColor>? value))
		{
			value = (tracks[propertyName] = new Dictionary<float, UiColor>());
		}
		return value;
	}

	private static Dictionary<float, float> GetOrCreateFloatTrack(IDictionary<string, Dictionary<float, float>> tracks, string propertyName)
	{
		if (!tracks.TryGetValue(propertyName, out Dictionary<float, float>? value))
		{
			value = (tracks[propertyName] = new Dictionary<float, float>());
		}
		return value;
	}

	private static bool TryNormalizeAnimatedPropertyName(string propertyName, out string? normalized)
	{
		string text = propertyName.Trim().ToLowerInvariant();
		normalized = text switch
		{
			"background" or "background-color" => "background-color",
			"border-color" => "border-color",
			"outline" or "outline-color" => "outline-color",
			"color" => "color",
			"opacity" => "opacity",
			"filter" => "filter",
			"backdrop-filter" => "backdrop-filter",
			"transform" => "transform",
			_ => null,
		};
		return normalized != null;
	}

	private static float ClampOffset(float value)
	{
		return Math.Clamp(value, 0f, 1f);
	}
}
