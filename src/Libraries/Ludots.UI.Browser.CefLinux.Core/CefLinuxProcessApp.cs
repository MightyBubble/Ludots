using CefNet;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.CefLinux.Core;

public sealed class CefLinuxProcessApp : CefApp
{
	// cef_scheme_options_t bits from the CEF C API: standard|secure|cors|fetch
	// mirrors the CefCustomScheme flags the Windows CefSharp provider registers.
	private const int SchemeOptionsStandard = 1 << 0;
	private const int SchemeOptionsSecure = 1 << 3;
	private const int SchemeOptionsCorsEnabled = 1 << 4;
	private const int SchemeOptionsFetchEnabled = 1 << 6;

	private static readonly CefLinuxRenderProcessHandler RenderProcessHandler = new();
	private readonly CefLinuxBrowserProcessHandler _browserProcessHandler;

	public CefLinuxProcessApp(CefLinuxBrowserProcessScheduler? scheduler)
	{
		_browserProcessHandler = new CefLinuxBrowserProcessHandler(scheduler);
	}

	protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar)
	{
		registrar.AddCustomScheme(
			BrowserLocalAppUri.Scheme,
			SchemeOptionsStandard | SchemeOptionsSecure | SchemeOptionsCorsEnabled | SchemeOptionsFetchEnabled);
	}

	protected override CefBrowserProcessHandler? GetBrowserProcessHandler()
	{
		return _browserProcessHandler;
	}

	protected override CefRenderProcessHandler? GetRenderProcessHandler()
	{
		return RenderProcessHandler;
	}
}
