using SkiaSharp;

namespace Ludots.UI.Runtime;

internal enum UiTransitionValueKind : byte
{
    Float = 0,
    Color = 1
}

internal sealed class UiTransitionChannelState
{
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

    public void Advance(float deltaSeconds)
    {
        ElapsedSeconds = Math.Max(0f, ElapsedSeconds + Math.Max(0f, deltaSeconds));
    }

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
}

internal static class UiTransitionMath
{
    public static float Evaluate(UiTransitionEasing easing, float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        return easing switch
        {
            UiTransitionEasing.Linear => progress,
            UiTransitionEasing.EaseIn => progress * progress,
            UiTransitionEasing.EaseOut => 1f - ((1f - progress) * (1f - progress)),
            UiTransitionEasing.EaseInOut => progress < 0.5f
                ? 2f * progress * progress
                : 1f - (MathF.Pow(-2f * progress + 2f, 2f) / 2f),
            _ => CubicBezierApproximate(progress, 0.25f, 0.1f, 0.25f, 1f)
        };
    }

    public static float Lerp(float start, float end, float progress)
    {
        return start + ((end - start) * progress);
    }

    public static SKColor Lerp(SKColor start, SKColor end, float progress)
    {
        byte red = (byte)Math.Clamp(MathF.Round(Lerp((float)start.Red, end.Red, progress)), 0f, 255f);
        byte green = (byte)Math.Clamp(MathF.Round(Lerp((float)start.Green, end.Green, progress)), 0f, 255f);
        byte blue = (byte)Math.Clamp(MathF.Round(Lerp((float)start.Blue, end.Blue, progress)), 0f, 255f);
        byte alpha = (byte)Math.Clamp(MathF.Round(Lerp((float)start.Alpha, end.Alpha, progress)), 0f, 255f);
        return new SKColor(red, green, blue, alpha);
    }

    public static UiStyle Apply(UiStyle style, UiTransitionChannelState channel)
    {
        return channel.ValueKind switch
        {
            UiTransitionValueKind.Float => ApplyFloat(style, channel.PropertyName, channel.CurrentFloat),
            _ => ApplyColor(style, channel.PropertyName, channel.CurrentColor)
        };
    }

    public static UiStyle ApplyFloat(UiStyle style, string propertyName, float value)
    {
        return propertyName switch
        {
            "opacity" => style with { Opacity = Math.Clamp(value, 0f, 1f) },
            "filter" => style with { FilterBlurRadius = Math.Max(0f, value) },
            "backdrop-filter" => style with { BackdropBlurRadius = Math.Max(0f, value) },
            _ => style
        };
    }

    public static UiStyle ApplyColor(UiStyle style, string propertyName, SKColor value)
    {
        return propertyName switch
        {
            "background-color" => style with { BackgroundColor = value },
            "border-color" => style with { BorderColor = value },
            "outline-color" => style with { OutlineColor = value },
            "color" => style with { Color = value },
            _ => style
        };
    }

    private static float CubicBezierApproximate(float progress, float x1, float y1, float x2, float y2)
    {
        float t = progress;
        for (int i = 0; i < 5; i++)
        {
            float x = SampleCubic(t, 0f, x1, x2, 1f) - progress;
            float derivative = SampleCubicDerivative(t, 0f, x1, x2, 1f);
            if (Math.Abs(derivative) < 0.0001f)
            {
                break;
            }

            t = Math.Clamp(t - (x / derivative), 0f, 1f);
        }

        return SampleCubic(t, 0f, y1, y2, 1f);
    }

    private static float SampleCubic(float t, float a, float b, float c, float d)
    {
        float inverse = 1f - t;
        return (inverse * inverse * inverse * a)
            + (3f * inverse * inverse * t * b)
            + (3f * inverse * t * t * c)
            + (t * t * t * d);
    }

    private static float SampleCubicDerivative(float t, float a, float b, float c, float d)
    {
        float inverse = 1f - t;
        return (3f * inverse * inverse * (b - a))
            + (6f * inverse * t * (c - b))
            + (3f * t * t * (d - c));
    }
}
