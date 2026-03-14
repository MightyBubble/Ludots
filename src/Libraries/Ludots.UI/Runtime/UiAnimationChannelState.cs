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
			if ((uint)(fillMode - 2) <= 1u)
			{
				return true;
			}
			return false;
		}
	}

	private bool HasForwardFill
	{
		get
		{
			UiAnimationFillMode fillMode = _entry.FillMode;
			if (fillMode == UiAnimationFillMode.Forwards || fillMode == UiAnimationFillMode.Both)
			{
				return true;
			}
			return false;
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
		if (1 == 0)
		{
		}
		bool flag = direction switch
		{
			UiAnimationDirection.Reverse => true, 
			UiAnimationDirection.Alternate => (iterationIndex & 1) == 1, 
			UiAnimationDirection.AlternateReverse => (iterationIndex & 1) == 0, 
			_ => false, 
		};
		if (1 == 0)
		{
		}
		return flag ? (1f - progress) : progress;
	}

	private static List<UiAnimationPropertyTrack> BuildTracks(UiAnimationEntry entry, UiStyle baseStyle)
	{
		List<UiAnimationPropertyTrack> list = new List<UiAnimationPropertyTrack>();
		UiKeyframeDefinition keyframes = entry.Keyframes;
		if (keyframes == null || keyframes.Stops.Count == 0)
		{
			return list;
		}
		Dictionary<string, Dictionary<float, UiColor>> dictionary = new Dictionary<string, Dictionary<float, UiColor>>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Dictionary<float, float>> dictionary2 = new Dictionary<string, Dictionary<float, float>>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < keyframes.Stops.Count; i++)
		{
			UiKeyframeStop uiKeyframeStop = keyframes.Stops[i];
			UiStyle uiStyle = baseStyle;
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string> item in uiKeyframeStop.Declaration)
			{
				if (TryNormalizeAnimatedPropertyName(item.Key, out string normalized))
				{
					uiStyle = UiStyleResolver.ApplyProperty(uiStyle, item.Key, item.Value);
					hashSet.Add(normalized);
				}
			}
			float key = ClampOffset(uiKeyframeStop.Offset);
			foreach (string item2 in hashSet)
			{
				switch (item2)
				{
				case "background-color":
					GetOrCreateColorTrack(dictionary, item2)[key] = uiStyle.BackgroundColor;
					break;
				case "border-color":
					GetOrCreateColorTrack(dictionary, item2)[key] = uiStyle.BorderColor;
					break;
				case "outline-color":
					GetOrCreateColorTrack(dictionary, item2)[key] = uiStyle.OutlineColor;
					break;
				case "color":
					GetOrCreateColorTrack(dictionary, item2)[key] = uiStyle.Color;
					break;
				case "opacity":
					GetOrCreateFloatTrack(dictionary2, item2)[key] = uiStyle.Opacity;
					break;
				case "filter":
					GetOrCreateFloatTrack(dictionary2, item2)[key] = uiStyle.FilterBlurRadius;
					break;
				case "backdrop-filter":
					GetOrCreateFloatTrack(dictionary2, item2)[key] = uiStyle.BackdropBlurRadius;
					break;
				}
			}
		}
		AddColorTrack(list, "background-color", baseStyle.BackgroundColor, dictionary);
		AddColorTrack(list, "border-color", baseStyle.BorderColor, dictionary);
		AddColorTrack(list, "outline-color", baseStyle.OutlineColor, dictionary);
		AddColorTrack(list, "color", baseStyle.Color, dictionary);
		AddFloatTrack(list, "opacity", baseStyle.Opacity, dictionary2);
		AddFloatTrack(list, "filter", baseStyle.FilterBlurRadius, dictionary2);
		AddFloatTrack(list, "backdrop-filter", baseStyle.BackdropBlurRadius, dictionary2);
		return list;
	}

	private static void AddColorTrack(ICollection<UiAnimationPropertyTrack> tracks, string propertyName, UiColor baseValue, IReadOnlyDictionary<string, Dictionary<float, UiColor>> values)
	{
		if (values.TryGetValue(propertyName, out Dictionary<float, UiColor> value) && value.Count != 0)
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
		if (values.TryGetValue(propertyName, out Dictionary<float, float> value) && value.Count != 0)
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

	private static Dictionary<float, UiColor> GetOrCreateColorTrack(IDictionary<string, Dictionary<float, UiColor>> tracks, string propertyName)
	{
		if (!tracks.TryGetValue(propertyName, out Dictionary<float, UiColor> value))
		{
			value = (tracks[propertyName] = new Dictionary<float, UiColor>());
		}
		return value;
	}

	private static Dictionary<float, float> GetOrCreateFloatTrack(IDictionary<string, Dictionary<float, float>> tracks, string propertyName)
	{
		if (!tracks.TryGetValue(propertyName, out Dictionary<float, float> value))
		{
			value = (tracks[propertyName] = new Dictionary<float, float>());
		}
		return value;
	}

	private static bool TryNormalizeAnimatedPropertyName(string propertyName, out string? normalized)
	{
		string text = propertyName.Trim().ToLowerInvariant();
		if (1 == 0)
		{
		}
		string text2;
		switch (text)
		{
		case "background":
		case "background-color":
			text2 = "background-color";
			break;
		case "border-color":
			text2 = "border-color";
			break;
		case "outline":
		case "outline-color":
			text2 = "outline-color";
			break;
		case "color":
			text2 = "color";
			break;
		case "opacity":
			text2 = "opacity";
			break;
		case "filter":
			text2 = "filter";
			break;
		case "backdrop-filter":
			text2 = "backdrop-filter";
			break;
		default:
			text2 = null;
			break;
		}
		if (1 == 0)
		{
		}
		normalized = text2;
		return normalized != null;
	}

	private static float ClampOffset(float value)
	{
		return Math.Clamp(value, 0f, 1f);
	}
}
