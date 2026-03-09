using System.Runtime.CompilerServices;

namespace Ludots.UI.Runtime.Events;

public sealed record UiPointerEvent : UiEvent
{
	public UiPointerEventType PointerEventType { get; init; }

	public int PointerId { get; init; }

	public float X { get; init; }

	public float Y { get; init; }

	public float DeltaX { get; init; }

	public float DeltaY { get; init; }

	public UiPointerEvent(UiPointerEventType PointerEventType, int PointerId, float X, float Y, UiNodeId? TargetNodeId = null, float DeltaX = 0f, float DeltaY = 0f)
		: base(UiEventKind.Pointer, TargetNodeId)
	{
		this.PointerEventType = PointerEventType;
		this.PointerId = PointerId;
		this.X = X;
		this.Y = Y;
		this.DeltaX = DeltaX;
		this.DeltaY = DeltaY;
	}

	[CompilerGenerated]
	public void Deconstruct(out UiPointerEventType PointerEventType, out int PointerId, out float X, out float Y, out UiNodeId? TargetNodeId, out float DeltaX, out float DeltaY)
	{
		PointerEventType = this.PointerEventType;
		PointerId = this.PointerId;
		X = this.X;
		Y = this.Y;
		TargetNodeId = base.TargetNodeId;
		DeltaX = this.DeltaX;
		DeltaY = this.DeltaY;
	}
}
