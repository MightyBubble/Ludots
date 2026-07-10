namespace Ludots.UI.Browser.Cef;

internal static class CefDataPlaneFacadeScript
{
	public static string Create()
	{
		return """
			(function installLudotsDataplaneFacade() {
			  function postBrowserMessage(message) {
			    if (!window.CefSharp || typeof window.CefSharp.PostMessage !== 'function') {
			      throw new Error('Ludots browser host bridge is not available.');
			    }

			    window.CefSharp.PostMessage(message);
			  }

			  if (!window.ludotsBrowser) {
			    window.ludotsBrowser = {
			      name: 'ludots.browser',
			      postMessage: postBrowserMessage,
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

			  const nativeBridgeName = 'ludotsDataplaneNative';
			  const nativeV8BridgeName = '__ludotsCefV8';

			  function bindNativeBridge() {
			    if (window[nativeBridgeName]) {
			      return Promise.resolve(window[nativeBridgeName]);
			    }

			    if (window.CefSharp && typeof window.CefSharp.BindObjectAsync === 'function') {
			      return window.CefSharp.BindObjectAsync(nativeBridgeName).then(function () {
			        return window[nativeBridgeName];
			      });
			    }

			    if (window.cefSharp && typeof window.cefSharp.bindObjectAsync === 'function') {
			      return window.cefSharp.bindObjectAsync(nativeBridgeName).then(function () {
			        return window[nativeBridgeName];
			      });
			    }

			    return Promise.reject(new Error('Ludots DataPlane native bridge is not available.'));
			  }

			  function normalizeDescriptor(descriptor) {
			    if (!descriptor || typeof descriptor !== 'object') {
			      throw new TypeError('Shared-buffer descriptor is required.');
			    }

			    return {
			      bufferId: String(descriptor.bufferId || descriptor.BufferId || ''),
			      byteOffset: Number(descriptor.byteOffset ?? descriptor.ByteOffset ?? 0),
			      byteLength: Number(descriptor.byteLength ?? descriptor.ByteLength ?? 0),
			      sequence: Number(descriptor.sequence ?? descriptor.Sequence ?? 0)
			    };
			  }

			  function normalizeBytes(value) {
			    if (value instanceof Uint8Array) {
			      return value;
			    }

			    if (value instanceof ArrayBuffer) {
			      return new Uint8Array(value);
			    }

			    if (Array.isArray(value)) {
			      return Uint8Array.from(value);
			    }

			    if (value && typeof value.length === 'number') {
			      return Uint8Array.from(value);
			    }

			    throw new TypeError('Native shared-buffer read did not return bytes.');
			  }

			  window.ludotsDataplane = {
			    name: 'ludots.dataplane',
			    mode: 'browser-native-bridge',
			    postMessage(message) {
			      window.ludotsBrowser.postMessage(message);
			    },
			    readSharedBuffer(descriptor) {
			      const normalized = normalizeDescriptor(descriptor);
			      return bindNativeBridge()
			        .then(function (nativeBridge) {
			          if (!nativeBridge || typeof nativeBridge.readSharedBuffer !== 'function') {
			            throw new Error('Ludots DataPlane native bridge is missing readSharedBuffer.');
			          }

			          return nativeBridge.readSharedBuffer(
			            normalized.bufferId,
			            normalized.byteOffset,
			            normalized.byteLength,
			            normalized.sequence);
			        })
			        .then(normalizeBytes);
			    },
			    acquireV8Buffer(descriptor) {
			      const normalized = normalizeDescriptor(descriptor);
			      return Promise.resolve()
			        .then(function () {
			          const v8Bridge = window[nativeV8BridgeName];
			          if (!v8Bridge || typeof v8Bridge.acquireV8Buffer !== 'function') {
			            throw new Error('Ludots native V8 buffer bridge is missing acquireV8Buffer.');
			          }

			          return v8Bridge.acquireV8Buffer(normalized);
			        })
			        .then(function (value) {
			          if (!(value instanceof ArrayBuffer)) {
			            throw new TypeError('Native V8 buffer bridge did not return ArrayBuffer.');
			          }

			          return value;
			        });
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
