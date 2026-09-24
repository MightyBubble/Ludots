namespace Ludots.Core.Input.Runtime
{
    public sealed class MouseCaptureRequest
    {
        public static MouseCaptureRequest None { get; } = new();

        public bool Capture { get; init; }
        public bool HideCursor { get; init; }
        public bool UseRelativeDelta { get; init; }
    }
}
