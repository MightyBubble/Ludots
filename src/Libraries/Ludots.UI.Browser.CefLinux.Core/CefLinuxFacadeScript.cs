namespace Ludots.UI.Browser.CefLinux.Core;

public static class CefLinuxFacadeScript
{
	public static string Create()
	{
		return """
			(function installLudotsBrowserFacade() {
			  function postNativeHostMessage(message) {
			    if (!window.__ludotsNativeBridge || typeof window.__ludotsNativeBridge.postHostMessage !== 'function') {
			      throw new Error('Ludots browser host bridge is not available.');
			    }

			    window.__ludotsNativeBridge.postHostMessage(typeof message === 'string' ? message : JSON.stringify(message));
			  }

			  if (!window.ludotsBrowser) {
			    window.ludotsBrowser = {
			      name: 'ludots.browser',
			      postMessage: postNativeHostMessage,
			      addEventListener(type, listener, options) {
			        window.addEventListener(type, listener, options);
			      },
			      removeEventListener(type, listener, options) {
			        window.removeEventListener(type, listener, options);
			      }
			    };
			  }

			  if (window.ludotsDataplane) {
			    return;
			  }

			  window.ludotsDataplane = {
			    name: 'ludots.dataplane',
			    mode: 'message-only',
			    postMessage(message) {
			      window.ludotsBrowser.postMessage(message);
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
