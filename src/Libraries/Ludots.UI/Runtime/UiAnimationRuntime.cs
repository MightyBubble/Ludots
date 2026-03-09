using SkiaSharp;

namespace Ludots.UI.Runtime;

internal enum UiAnimationTrackKind : byte
{
    Float = 0,
    Color = 1
}

internal readonly record struct UiAnimationFloatStop(float Offset, float Value);

internal readonly record struct UiAnimationColorStop(float Offset, SKColor Value);

internal sealed class UiAnimationPropertyTrack
{
    private readonly UiAnimationTrackKind _kind;
    private readonly UiAnimationFloatStop[] _floatStops;
    private readonly UiAnimationColorStop[] _colorStops;

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

    public string PropertyName { get; }

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
        return _kind switch
        {
            UiAnimationTrackKind.Float => UiTransitionMath.ApplyFloat(style, PropertyName, Evaluate(_floatStops, progress)),
            _ => UiTransitionMath.ApplyColor(style, PropertyName, Evaluate(_colorStops, progress))
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
            UiAnimationFloatStop current = stops[i];
            if (progress > current.Offset)
            {
                continue;
            }

            UiAnimationFloatStop previous = stops[i - 1];
            float span = Math.Max(0.0001f, current.Offset - previous.Offset);
            float localProgress = Math.Clamp((progress - previous.Offset) / span, 0f, 1f);
            return UiTransitionMath.Lerp(previous.Value, current.Value, localProgress);
        }

        return stops[^1].Value;
    }

    private static SKColor Evaluate(IReadOnlyList<UiAnimationColorStop> stops, float progress)
    {
        if (stops.Count == 0)
        {
            return SKColors.Transparent;
        }

        if (progress <= stops[0].Offset)
        {
            return stops[0].Value;
        }

        for (int i = 1; i < stops.Count; i++)
        {
            UiAnimationColorStop current = stops[i];
            if (progress > current.Offset)
            {
                continue;
            }

            UiAnimationColorStop previous = stops[i - 1];
            float span = Math.Max(0.0001f, current.Offset - previous.Offset);
            float localProgress = Math.Clamp((progress - previous.Offset) / span, 0f, 1f);
            return UiTransitionMath.Lerp(previous.Value, current.Value, localProgress);
        }

        return stops[^1].Value;
    }
}

internal sealed class UiAnimationChannelState
{
    private readonly UiAnimationEntry _entry;
    private readonly List<UiAnimationPropertyTrack> _tracks;

    public UiAnimationChannelState(UiAnimationEntry entry, UiStyle baseStyle)
    {
        _entry = entry;
        _tracks = BuildTracks(entry, baseStyle);
    }

    public float ElapsedSeconds { get; private set; }

    public bool HasTracks => _tracks.Count > 0;

    public bool IsDiscardable => !HasTracks || (!HasForwardFill && IsFinite && ElapsedSeconds >= ActiveEndSeconds);

    private bool HasBackwardFill => _entry.FillMode is UiAnimationFillMode.Backwards or UiAnimationFillMode.Both;

    private bool HasForwardFill => _entry.FillMode is UiAnimationFillMode.Forwards or UiAnimationFillMode.Both;

    private bool IsFinite => !float.IsPositiveInfinity(_entry.IterationCount);

    private float ActiveDurationSeconds => _entry.DurationSeconds * Math.Max(0f, _entry.IterationCount);

    private float ActiveEndSeconds => _entry.DelaySeconds + ActiveDurationSeconds;

    public void Advance(float deltaSeconds)
    {
        if (!HasTracks || _entry.PlayState == UiAnimationPlayState.Paused || deltaSeconds <= 0f)
        {
            return;
        }

        ElapsedSeconds = Math.Max(0f, ElapsedSeconds + deltaSeconds);
    }

    public UiStyle Apply(UiStyle style)
    {
        if (!TryResolveDirectedProgress(out float progress))
        {
            return style;
        }

        UiStyle result = style;
        for (int i = 0; i < _tracks.Count; i++)
        {
            result = _tracks[i].Apply(result, progress);
        }

        return result;
    }

    private bool TryResolveDirectedProgress(out float progress)
    {
        progress = 0f;
        if (!HasTracks || _entry.DurationSeconds <= 0f)
        {
            return false;
        }

        float iterationCount = Math.Max(0f, _entry.IterationCount);
        if (iterationCount <= 0f)
        {
            return false;
        }

        float localTime = ElapsedSeconds - _entry.DelaySeconds;
        if (localTime < 0f)
        {
            if (!HasBackwardFill)
            {
                return false;
            }

            progress = ApplyDirection(iterationIndex: 0, progress: 0f);
            return true;
        }

        if (IsFinite && localTime >= ActiveDurationSeconds)
        {
            if (!HasForwardFill)
            {
                return false;
            }

            progress = ResolveEndProgress(iterationCount);
            return true;
        }

        float iterationPosition = localTime / _entry.DurationSeconds;
        int iterationIndex = Math.Max(0, (int)MathF.Floor(iterationPosition));
        float iterationProgress = iterationPosition - iterationIndex;
        progress = ApplyDirection(iterationIndex, iterationProgress);
        return true;
    }

    private float ResolveEndProgress(float iterationCount)
    {
        int iterationIndex = Math.Max(0, (int)MathF.Ceiling(iterationCount) - 1);
        float fractional = iterationCount % 1f;
        float iterationProgress = fractional <= 0.0001f ? 1f : fractional;
        return ApplyDirection(iterationIndex, iterationProgress);
    }

    private float ApplyDirection(int iterationIndex, float progress)
    {
        bool reversed = _entry.Direction switch
        {
            UiAnimationDirection.Reverse => true,
            UiAnimationDirection.Alternate => (iterationIndex & 1) == 1,
            UiAnimationDirection.AlternateReverse => (iterationIndex & 1) == 0,
            _ => false
        };

        return reversed ? 1f - progress : progress;
    }

    private static List<UiAnimationPropertyTrack> BuildTracks(UiAnimationEntry entry, UiStyle baseStyle)
    {
        List<UiAnimationPropertyTrack> tracks = new();
        UiKeyframeDefinition? keyframes = entry.Keyframes;
        if (keyframes == null || keyframes.Stops.Count == 0)
        {
            return tracks;
        }

        Dictionary<string, Dictionary<float, SKColor>> colorTracks = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<float, float>> floatTracks = new(StringComparer.OrdinalIgnoreCase);

        for (int stopIndex = 0; stopIndex < keyframes.Stops.Count; stopIndex++)
        {
            UiKeyframeStop stop = keyframes.Stops[stopIndex];
            UiStyle appliedStyle = baseStyle;
            HashSet<string> touchedProperties = new(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> property in stop.Declaration)
            {
                if (!TryNormalizeAnimatedPropertyName(property.Key, out string? propertyName))
                {
                    continue;
                }

                appliedStyle = UiStyleResolver.ApplyProperty(appliedStyle, property.Key, property.Value);
                touchedProperties.Add(propertyName);
            }

            float offset = ClampOffset(stop.Offset);
            foreach (string propertyName in touchedProperties)
            {
                switch (propertyName)
                {
                    case "background-color":
                        GetOrCreateColorTrack(colorTracks, propertyName)[offset] = appliedStyle.BackgroundColor;
                        break;
                    case "border-color":
                        GetOrCreateColorTrack(colorTracks, propertyName)[offset] = appliedStyle.BorderColor;
                        break;
                    case "outline-color":
                        GetOrCreateColorTrack(colorTracks, propertyName)[offset] = appliedStyle.OutlineColor;
                        break;
                    case "color":
                        GetOrCreateColorTrack(colorTracks, propertyName)[offset] = appliedStyle.Color;
                        break;
                    case "opacity":
                        GetOrCreateFloatTrack(floatTracks, propertyName)[offset] = appliedStyle.Opacity;
                        break;
                    case "filter":
                        GetOrCreateFloatTrack(floatTracks, propertyName)[offset] = appliedStyle.FilterBlurRadius;
                        break;
                    case "backdrop-filter":
                        GetOrCreateFloatTrack(floatTracks, propertyName)[offset] = appliedStyle.BackdropBlurRadius;
                        break;
                }
            }
        }

        AddColorTrack(tracks, "background-color", baseStyle.BackgroundColor, colorTracks);
        AddColorTrack(tracks, "border-color", baseStyle.BorderColor, colorTracks);
        AddColorTrack(tracks, "outline-color", baseStyle.OutlineColor, colorTracks);
        AddColorTrack(tracks, "color", baseStyle.Color, colorTracks);
        AddFloatTrack(tracks, "opacity", baseStyle.Opacity, floatTracks);
        AddFloatTrack(tracks, "filter", baseStyle.FilterBlurRadius, floatTracks);
        AddFloatTrack(tracks, "backdrop-filter", baseStyle.BackdropBlurRadius, floatTracks);
        return tracks;
    }

    private static void AddColorTrack(
        ICollection<UiAnimationPropertyTrack> tracks,
        string propertyName,
        SKColor baseValue,
        IReadOnlyDictionary<string, Dictionary<float, SKColor>> values)
    {
        if (!values.TryGetValue(propertyName, out Dictionary<float, SKColor>? propertyValues) || propertyValues.Count == 0)
        {
            return;
        }

        propertyValues.TryAdd(0f, baseValue);
        propertyValues.TryAdd(1f, baseValue);
        UiAnimationColorStop[] stops = propertyValues
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new UiAnimationColorStop(pair.Key, pair.Value))
            .ToArray();
        if (stops.Length > 1)
        {
            tracks.Add(UiAnimationPropertyTrack.CreateColor(propertyName, stops));
        }
    }

    private static void AddFloatTrack(
        ICollection<UiAnimationPropertyTrack> tracks,
        string propertyName,
        float baseValue,
        IReadOnlyDictionary<string, Dictionary<float, float>> values)
    {
        if (!values.TryGetValue(propertyName, out Dictionary<float, float>? propertyValues) || propertyValues.Count == 0)
        {
            return;
        }

        propertyValues.TryAdd(0f, baseValue);
        propertyValues.TryAdd(1f, baseValue);
        UiAnimationFloatStop[] stops = propertyValues
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new UiAnimationFloatStop(pair.Key, pair.Value))
            .ToArray();
        if (stops.Length > 1)
        {
            tracks.Add(UiAnimationPropertyTrack.CreateFloat(propertyName, stops));
        }
    }

    private static Dictionary<float, SKColor> GetOrCreateColorTrack(
        IDictionary<string, Dictionary<float, SKColor>> tracks,
        string propertyName)
    {
        if (!tracks.TryGetValue(propertyName, out Dictionary<float, SKColor>? values))
        {
            values = new Dictionary<float, SKColor>();
            tracks[propertyName] = values;
        }

        return values;
    }

    private static Dictionary<float, float> GetOrCreateFloatTrack(
        IDictionary<string, Dictionary<float, float>> tracks,
        string propertyName)
    {
        if (!tracks.TryGetValue(propertyName, out Dictionary<float, float>? values))
        {
            values = new Dictionary<float, float>();
            tracks[propertyName] = values;
        }

        return values;
    }

    private static bool TryNormalizeAnimatedPropertyName(string propertyName, out string? normalized)
    {
        normalized = propertyName.Trim().ToLowerInvariant() switch
        {
            "background" or "background-color" => "background-color",
            "border-color" => "border-color",
            "outline" or "outline-color" => "outline-color",
            "color" => "color",
            "opacity" => "opacity",
            "filter" => "filter",
            "backdrop-filter" => "backdrop-filter",
            _ => null
        };

        return normalized != null;
    }

    private static float ClampOffset(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }
}
