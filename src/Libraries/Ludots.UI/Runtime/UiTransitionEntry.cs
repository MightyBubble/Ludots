namespace Ludots.UI.Runtime;

public sealed record UiTransitionEntry(string PropertyName, float DurationSeconds, float DelaySeconds, UiTransitionEasing Easing);
