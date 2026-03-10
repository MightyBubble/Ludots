namespace Ludots.UI.Runtime;

public sealed record UiAnimationEntry(string Name, float DurationSeconds, float DelaySeconds, UiTransitionEasing Easing, float IterationCount, UiAnimationDirection Direction, UiAnimationFillMode FillMode, UiAnimationPlayState PlayState, UiKeyframeDefinition? Keyframes = null);
