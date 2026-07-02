using System;

namespace Ludots.UI.Browser;

public readonly record struct BrowserSurfaceId(Guid Value)
{
	public static BrowserSurfaceId New() => new BrowserSurfaceId(Guid.NewGuid());

	public override string ToString() => Value.ToString("N");
}
