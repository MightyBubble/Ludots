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

			  const nativeBridgeName = 'ludotsDataplaneNative';

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
			    name: 'cef.ludots-dataplane',
			    mode: 'browser-native-bridge',
			    postMessage(message) {
			      window.CefSharp.PostMessage(message);
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
