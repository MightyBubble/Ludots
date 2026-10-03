using System;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.CefLinux;

internal static class CefLinuxBrowserInputTranslator
{
	public static CefLinuxWheelDelta ToCefWheelDelta(BrowserWheelEvent wheel)
	{
		return new CefLinuxWheelDelta(
			-(int)MathF.Round(wheel.DeltaX),
			-(int)MathF.Round(wheel.DeltaY));
	}
}

internal readonly record struct CefLinuxWheelDelta(int DeltaX, int DeltaY);
