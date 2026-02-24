namespace Ludots.UI.Input;

public enum InputDeviceType
{
    Mouse,
    Touch,
    Pen,
    Keyboard,
    Gamepad
}

public abstract class InputEvent
{
    public InputDeviceType DeviceType { get; set; }
    public bool Handled { get; set; }
}

public enum PointerAction
{
    Down,
    Move,
    Up,
    Cancel,
    Scroll
}

public class PointerEvent : InputEvent
{
    public int PointerId { get; set; }
    public PointerAction Action { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float DeltaX { get; set; }
    public float DeltaY { get; set; }
}

public enum NavigationDirection
{
    Up,
    Down,
    Left,
    Right,
    Submit,
    Cancel
}

public class NavigationEvent : InputEvent
{
    public NavigationDirection Direction { get; set; }
}
