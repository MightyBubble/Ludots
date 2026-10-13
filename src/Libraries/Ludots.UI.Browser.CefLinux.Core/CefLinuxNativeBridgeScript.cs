namespace Ludots.UI.Browser.CefLinux.Core;

internal static class CefLinuxNativeBridgeScript
{
	public static string Source => """
		var __ludotsNativeBridge;
		if (!__ludotsNativeBridge) {
		  __ludotsNativeBridge = {
		    postHostMessage: function (payloadJson) {
		      native function __ludotsPostHostMessage();
		      return __ludotsPostHostMessage(payloadJson);
		    }
		  };
		}
		""";
}
