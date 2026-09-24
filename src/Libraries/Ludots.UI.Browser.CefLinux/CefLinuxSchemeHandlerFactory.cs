using System;
using global::CefNet;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.CefLinux;

internal sealed class CefLinuxSchemeHandlerFactory : CefSchemeHandlerFactory
{
	private readonly CefLinuxBrowserSurfaceRegistry _registry;

	public CefLinuxSchemeHandlerFactory(CefLinuxBrowserSurfaceRegistry registry)
	{
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
	}

	protected override CefResourceHandler Create(
		CefBrowser browser,
		CefFrame frame,
		string schemeName,
		CefRequest request)
	{
		if (browser is null || request is null)
		{
			return null!;
		}

		if (!_registry.TryResolveResource(browser.Identifier, request.Url, out BrowserResource? resource) ||
			resource is null)
		{
			return null!;
		}

		return new CefLinuxResourceHandler(resource);
	}
}
