using Ludots.UI.Runtime;

namespace Ludots.UI.Runtime.Events;

public sealed record UiPointerEvent(
    UiPointerEventType PointerEventType,
    int PointerId,
    float X,
    float Y,
    UiNodeId? TargetNodeId = null,
    float DeltaX = 0f,
    float DeltaY = 0f)
    : UiEvent(UiEventKind.Pointer, TargetNodeId);
