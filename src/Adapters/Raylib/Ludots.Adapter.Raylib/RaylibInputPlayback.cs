using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Input.Runtime;

namespace Ludots.Adapter.Raylib;

internal enum RaylibInputPlaybackEventKind : byte
{
    Button = 1,
    Axis = 2,
    Pointer = 3,
    Wheel = 4,
    UiClick = 5,
    Marker = 6
}

internal sealed class RaylibInputPlaybackConfig
{
    public int Version { get; set; }

    public RaylibInputPlaybackEventConfig[] Events { get; set; } = Array.Empty<RaylibInputPlaybackEventConfig>();
}

internal sealed class RaylibInputPlaybackEventConfig
{
    public int Frame { get; set; }

    public RaylibInputPlaybackEventKind Kind { get; set; }

    public string? Path { get; set; }

    public bool? Pressed { get; set; }

    public float? Value { get; set; }

    public float? X { get; set; }

    public float? Y { get; set; }

    public string? ElementId { get; set; }

    public string? Label { get; set; }
}

internal sealed class RaylibInputPlayback : IInputBackend
{
    internal const string ScriptPathEnvironmentVariable = "LUDOTS_RAYLIB_INPUT_PLAYBACK_PATH";
    private const int SupportedVersion = 1;

    private readonly RaylibInputPlaybackEventConfig[] _events;
    private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _axes = new(StringComparer.Ordinal);
    private int _nextEventIndex;
    private int _lastFrame = -1;
    private Vector2 _pointerPosition = new(-1f, -1f);
    private bool _pointerChanged;
    private float _wheel;

    private RaylibInputPlayback(RaylibInputPlaybackEventConfig[] events)
    {
        _events = events;
    }

    public static RaylibInputPlayback? LoadFromEnvironment()
    {
        string? path = Environment.GetEnvironmentVariable(ScriptPathEnvironmentVariable);
        return string.IsNullOrWhiteSpace(path) ? null : Load(path);
    }

    internal static RaylibInputPlayback Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Raylib input playback path must not be empty.");
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Raylib input playback file does not exist.", fullPath);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        RaylibInputPlaybackConfig config = JsonSerializer.Deserialize<RaylibInputPlaybackConfig>(
                File.ReadAllText(fullPath),
                options)
            ?? throw new JsonException("Raylib input playback root must be an object.");
        Validate(config, fullPath);
        return new RaylibInputPlayback(config.Events);
    }

    internal bool AdvanceFrame(
        int frame,
        Func<string, bool> clickUiElement,
        Action<string>? diagnostic = null)
    {
        if (frame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "Raylib input playback frame must be non-negative.");
        }

        ArgumentNullException.ThrowIfNull(clickUiElement);
        if (frame <= _lastFrame)
        {
            throw new InvalidOperationException(
                $"Raylib input playback frames must advance strictly: previous={_lastFrame}, current={frame}.");
        }

        _lastFrame = frame;
        _pointerChanged = false;
        _wheel = 0f;
        if (_nextEventIndex < _events.Length && _events[_nextEventIndex].Frame < frame)
        {
            RaylibInputPlaybackEventConfig missed = _events[_nextEventIndex];
            throw new InvalidOperationException(
                $"Raylib input playback missed event {_nextEventIndex} at frame {missed.Frame} ({missed.Kind}).");
        }

        bool uiHandled = false;
        while (_nextEventIndex < _events.Length && _events[_nextEventIndex].Frame == frame)
        {
            RaylibInputPlaybackEventConfig playbackEvent = _events[_nextEventIndex];
            switch (playbackEvent.Kind)
            {
                case RaylibInputPlaybackEventKind.Button:
                    _buttons[playbackEvent.Path!] = playbackEvent.Pressed!.Value;
                    break;
                case RaylibInputPlaybackEventKind.Axis:
                    _axes[playbackEvent.Path!] = playbackEvent.Value!.Value;
                    break;
                case RaylibInputPlaybackEventKind.Pointer:
                    _pointerPosition = new Vector2(playbackEvent.X!.Value, playbackEvent.Y!.Value);
                    _pointerChanged = true;
                    break;
                case RaylibInputPlaybackEventKind.Wheel:
                    _wheel = playbackEvent.Value!.Value;
                    break;
                case RaylibInputPlaybackEventKind.UiClick:
                    if (!clickUiElement(playbackEvent.ElementId!))
                    {
                        throw new InvalidOperationException(
                            $"Raylib input playback UI click was not handled: element='{playbackEvent.ElementId}', frame={frame}.");
                    }

                    uiHandled = true;
                    break;
                case RaylibInputPlaybackEventKind.Marker:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Raylib input playback event {_nextEventIndex} has unsupported kind '{playbackEvent.Kind}'.");
            }

            diagnostic?.Invoke(
                $"input-playback event={_nextEventIndex} frame={frame} kind={playbackEvent.Kind} " +
                $"target={playbackEvent.Path ?? playbackEvent.ElementId ?? playbackEvent.Label ?? "-"}");
            _nextEventIndex++;
        }

        return uiHandled;
    }

    internal void EnsureComplete(int finalFrame)
    {
        if (_nextEventIndex != _events.Length)
        {
            RaylibInputPlaybackEventConfig pending = _events[_nextEventIndex];
            throw new InvalidOperationException(
                $"Raylib input playback stopped at frame {finalFrame} with event {_nextEventIndex} still pending " +
                $"at frame {pending.Frame} ({pending.Kind}).");
        }
    }

    public float GetAxis(string devicePath)
    {
        return _axes.TryGetValue(devicePath, out float value) ? value : 0f;
    }

    public bool GetButton(string devicePath)
    {
        return _buttons.TryGetValue(devicePath, out bool pressed) && pressed;
    }

    public Vector2 GetMousePosition() => _pointerPosition;

    public float GetMouseWheel() => _wheel;

    internal bool PointerChangedThisFrame => _pointerChanged;

    public void EnableIME(bool enable)
    {
        if (enable)
        {
            throw new InvalidOperationException("Raylib input playback does not accept IME text input.");
        }
    }

    public void SetIMECandidatePosition(int x, int y)
    {
    }

    public string GetCharBuffer() => string.Empty;

    private static void Validate(RaylibInputPlaybackConfig config, string source)
    {
        if (config.Version != SupportedVersion)
        {
            throw new InvalidOperationException(
                $"Raylib input playback '{source}' requires version {SupportedVersion}, actual={config.Version}.");
        }

        if (config.Events == null || config.Events.Length == 0)
        {
            throw new InvalidOperationException($"Raylib input playback '{source}' requires at least one event.");
        }

        int previousFrame = -1;
        for (int i = 0; i < config.Events.Length; i++)
        {
            RaylibInputPlaybackEventConfig playbackEvent = config.Events[i]
                ?? throw new InvalidOperationException($"Raylib input playback '{source}' event {i} is null.");
            if (playbackEvent.Frame < 0 || playbackEvent.Frame < previousFrame)
            {
                throw new InvalidOperationException(
                    $"Raylib input playback '{source}' events must be ordered by non-negative frame; " +
                    $"event {i} has frame {playbackEvent.Frame} after {previousFrame}.");
            }

            previousFrame = playbackEvent.Frame;
            ValidateEvent(playbackEvent, source, i);
        }
    }

    private static void ValidateEvent(RaylibInputPlaybackEventConfig playbackEvent, string source, int index)
    {
        switch (playbackEvent.Kind)
        {
            case RaylibInputPlaybackEventKind.Button:
                RequirePath(playbackEvent, source, index);
                if (!playbackEvent.Pressed.HasValue)
                {
                    throw EventError(source, index, "Button requires 'pressed'.");
                }
                break;
            case RaylibInputPlaybackEventKind.Axis:
                RequirePath(playbackEvent, source, index);
                RequireFinite(playbackEvent.Value, source, index, "value");
                break;
            case RaylibInputPlaybackEventKind.Pointer:
                RequireFinite(playbackEvent.X, source, index, "x");
                RequireFinite(playbackEvent.Y, source, index, "y");
                break;
            case RaylibInputPlaybackEventKind.Wheel:
                RequireFinite(playbackEvent.Value, source, index, "value");
                break;
            case RaylibInputPlaybackEventKind.UiClick:
                if (string.IsNullOrWhiteSpace(playbackEvent.ElementId))
                {
                    throw EventError(source, index, "UiClick requires non-empty 'elementId'.");
                }
                break;
            case RaylibInputPlaybackEventKind.Marker:
                if (string.IsNullOrWhiteSpace(playbackEvent.Label))
                {
                    throw EventError(source, index, "Marker requires non-empty 'label'.");
                }
                break;
            default:
                throw EventError(source, index, $"Unsupported kind '{playbackEvent.Kind}'.");
        }
    }

    private static void RequirePath(RaylibInputPlaybackEventConfig playbackEvent, string source, int index)
    {
        if (string.IsNullOrWhiteSpace(playbackEvent.Path))
        {
            throw EventError(source, index, $"{playbackEvent.Kind} requires non-empty 'path'.");
        }
    }

    private static void RequireFinite(float? value, string source, int index, string property)
    {
        if (!value.HasValue || !float.IsFinite(value.Value))
        {
            throw EventError(source, index, $"{property} must be finite.");
        }
    }

    private static InvalidOperationException EventError(string source, int index, string detail)
    {
        return new InvalidOperationException($"Raylib input playback '{source}' event {index}: {detail}");
    }
}
