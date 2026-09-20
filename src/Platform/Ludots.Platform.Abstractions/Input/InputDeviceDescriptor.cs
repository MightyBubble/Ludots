namespace Ludots.Platform.Abstractions
{
    public enum InputDeviceKind : byte
    {
        Keyboard = 0,
        Mouse = 1,
        Gamepad = 2,
        Touch = 3,
    }

    /// <summary>
    /// Stable description of one connected input device. DeviceId is stable for the host
    /// session; SeatSlot carries the owning client-local seat slot and is -1 while unassigned.
    /// </summary>
    public readonly record struct InputDeviceDescriptor(
        string DeviceId,
        InputDeviceKind Kind,
        string DisplayName,
        int SeatSlot);
}
