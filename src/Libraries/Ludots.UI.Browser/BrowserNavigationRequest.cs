using System;

namespace Ludots.UI.Browser;

public sealed class BrowserNavigationRequest
{
	public BrowserNavigationRequest(Uri uri)
	{
		Uri = uri ?? throw new ArgumentNullException(nameof(uri));
	}

	public Uri Uri { get; }
}
