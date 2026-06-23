using System;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal static class CefBrowserInputTranslator
{
	public static CefWheelDelta ToCefWheelDelta(BrowserWheelEvent wheel)
	{
		return new CefWheelDelta(
			-(int)MathF.Round(wheel.DeltaX),
			-(int)MathF.Round(wheel.DeltaY));
	}
}

internal readonly record struct CefWheelDelta(int DeltaX, int DeltaY);
