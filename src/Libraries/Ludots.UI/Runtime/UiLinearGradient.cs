using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public sealed record UiLinearGradient(float AngleDegrees, IReadOnlyList<UiGradientStop> Stops);
