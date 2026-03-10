namespace Ludots.UI.Input;

public abstract class InputEvent
{
	public InputDeviceType DeviceType { get; set; }

	public bool Handled { get; set; }
}
