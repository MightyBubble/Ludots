namespace Ludots.UI.Runtime.Events;

public abstract record UiEvent(UiEventKind Kind, UiNodeId? TargetNodeId);
