using CefNet;

namespace Ludots.UI.Browser.CefLinux.Core;

internal sealed class CefLinuxRenderProcessHandler : CefRenderProcessHandler
{
	private static bool _nativeBridgeExtensionRegistered;

	protected override void OnContextCreated(CefBrowser browser, CefFrame frame, CefV8Context context)
	{
		if (_nativeBridgeExtensionRegistered)
		{
			return;
		}

		CefApi.RegisterExtension(
			CefLinuxProcessMessages.NativeBridgeExtensionName,
			CefLinuxNativeBridgeScript.Source,
			new CefLinuxV8Bridge());
		_nativeBridgeExtensionRegistered = true;
	}
}
