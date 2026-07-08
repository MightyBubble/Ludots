using System.Numerics;

namespace Ludots.Core.Input.Automation
{
    public enum InputAutomationCommandKind
    {
        PointerMove,
        PointerDown,
        PointerUp,
        PointerClick,
        PointerDrag,
        PointerScroll,
        KeyDown,
        KeyUp,
        KeyStroke,
        Text
    }

    public enum InputAutomationPointerButton
    {
        Left,
        Middle,
        Right
    }

    public enum InputAutomationFrameEventKind
    {
        PointerMove,
        PointerDown,
        PointerUp,
        PointerCancel,
        PointerScroll,
        KeyDown,
        KeyUp,
        Character
    }

    public sealed class InputAutomationCommand
    {
        public InputAutomationCommandKind Kind { get; init; }

        public int Frame { get; init; }

        public int DurationFrames { get; init; }

        public float X { get; init; }

        public float Y { get; init; }

        public float EndX { get; init; }

        public float EndY { get; init; }

        public float DeltaX { get; init; }

        public float DeltaY { get; init; }

        public InputAutomationPointerButton Button { get; init; } = InputAutomationPointerButton.Left;

        public string Key { get; init; } = string.Empty;

        public string Text { get; init; } = string.Empty;

        public string DevicePath { get; init; } = string.Empty;

        public int Modifiers { get; init; }
    }

    public readonly record struct InputAutomationFrameEvent(
        InputAutomationFrameEventKind Kind,
        Vector2 Position,
        InputAutomationPointerButton? Button,
        Vector2 Delta,
        string Key,
        string Text,
        int Modifiers);
}
