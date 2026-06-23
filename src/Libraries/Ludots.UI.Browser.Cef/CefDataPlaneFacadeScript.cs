namespace Ludots.UI.Browser.Cef;

internal static class CefDataPlaneFacadeScript
{
	public static string Create()
	{
		return """
			(function installLudotsDataplaneFacade() {
			  if (window.ludotsDataplane) {
			    return;
			  }

			  window.ludotsDataplane = {
			    name: 'cef.ludots-dataplane',
			    mode: 'message',
			    postMessage(message) {
			      window.CefSharp.PostMessage(message);
			    },
			    addEventListener(type, listener, options) {
			      window.addEventListener(type, listener, options);
			    },
			    removeEventListener(type, listener, options) {
			      window.removeEventListener(type, listener, options);
			    }
			  };
			})();
			""";
	}
}
