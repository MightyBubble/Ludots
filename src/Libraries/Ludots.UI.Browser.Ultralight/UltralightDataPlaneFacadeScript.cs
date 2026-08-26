namespace Ludots.UI.Browser.Ultralight;

internal static class UltralightDataPlaneFacadeScript
{
	public static string Create(string surfaceKey)
	{
		return $$"""
			(function installLudotsUltralightFacades() {
			  const surfaceKey = {{System.Text.Json.JsonSerializer.Serialize(surfaceKey)}};
			  const messagePrefix = '__LUDOTS_MSG__:';
			  window.__ludotsSharedReadPending = window.__ludotsSharedReadPending || Object.create(null);

			  function postBrowserMessage(message) {
			    console.log(messagePrefix + JSON.stringify(message));
			  }

			  if (!window.ludotsBrowser) {
			    window.ludotsBrowser = {
			      name: 'ludots.browser',
			      provider: 'ultralight',
			      surfaceKey: surfaceKey,
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

			  function decodeBase64(base64) {
			    const binary = atob(base64);
			    const bytes = new Uint8Array(binary.length);
			    for (let i = 0; i < binary.length; i++) {
			      bytes[i] = binary.charCodeAt(i);
			    }
			    return bytes;
			  }

			  window.__ludotsResolveSharedRead = function resolveSharedRead(requestId, base64, error) {
			    const pending = window.__ludotsSharedReadPending[requestId];
			    if (!pending) {
			      return;
			    }
			    delete window.__ludotsSharedReadPending[requestId];
			    if (error) {
			      pending.reject(new Error(String(error)));
			      return;
			    }
			    pending.resolve(decodeBase64(String(base64 || '')));
			  };

			  window.ludotsDataplane = {
			    name: 'ludots.dataplane',
			    mode: 'ultralight-message-bridge',
			    postMessage(message) {
			      window.ludotsBrowser.postMessage(message);
			    },
			    readSharedBuffer(descriptor) {
			      const normalized = normalizeDescriptor(descriptor);
			      const requestId = 'ul-shared-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
			      return new Promise(function (resolve, reject) {
			        window.__ludotsSharedReadPending[requestId] = { resolve: resolve, reject: reject };
			        postBrowserMessage({
			          channel: 'ludots.dataplane.shared-read',
			          payload: {
			            requestId: requestId,
			            bufferId: normalized.bufferId,
			            byteOffset: normalized.byteOffset,
			            byteLength: normalized.byteLength,
			            sequence: normalized.sequence
			          }
			        });
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
