namespace Ludots.Platform.Abstractions
{
    public enum InputDeviceChangeKind : byte
    {
        Connected = 0,
        Disconnected = 1,
    }

    public readonly record struct InputDeviceChangeEvent(
        InputDeviceDescriptor Device,
        InputDeviceChangeKind Kind);
}
