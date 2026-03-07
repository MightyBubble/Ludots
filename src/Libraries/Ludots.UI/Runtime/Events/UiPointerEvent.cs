using Ludots.UI.Runtime;

namespace Ludots.UI.Runtime.Events;

public sealed record UiPointerEvent(
    UiPointerEventType PointerEventType,
    int PointerId,
    float X,
    float Y,
    UiNodeId? TargetNodeId = null)
    : UiEvent(UiEventKind.Pointer, TargetNodeId);
