namespace Ludots.UI.Input;

public class PointerEvent : InputEvent
{
	public int PointerId { get; set; }

	public PointerAction Action { get; set; }

	public float X { get; set; }

	public float Y { get; set; }

	public float DeltaX { get; set; }

	public float DeltaY { get; set; }
}
