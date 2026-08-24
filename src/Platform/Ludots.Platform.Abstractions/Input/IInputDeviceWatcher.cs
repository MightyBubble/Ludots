namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Host-adapter side of input device discovery: only the host can observe which devices
    /// are plugged in. Engine code enumerates via GetConnectedDevices or subscribes to
    /// DeviceChanged; per-frame device input reading stays on IInputBackend.
    /// </summary>
    public interface IInputDeviceWatcher
    {
        IReadOnlyList<InputDeviceDescriptor> GetConnectedDevices();

        event Action<InputDeviceChangeEvent>? DeviceChanged;
    }
}
