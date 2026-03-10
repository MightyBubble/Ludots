using System;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public readonly record struct UiShadow(float OffsetX, float OffsetY, float BlurRadius, float SpreadRadius, SKColor Color)
{
	public bool IsVisible => Color.Alpha > 0 && (Math.Abs(OffsetX) > 0.01f || Math.Abs(OffsetY) > 0.01f || Math.Abs(BlurRadius) > 0.01f || Math.Abs(SpreadRadius) > 0.01f);
}
