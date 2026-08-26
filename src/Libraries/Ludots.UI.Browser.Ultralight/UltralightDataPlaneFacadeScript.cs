namespace Ludots.UI.Browser.Ultralight;

internal static class UltralightDataPlaneFacadeScript
{
	public static string Create(string surfaceKey)
	{
		return $$"""
			(function installLudotsUltralightFacades() {
			  var surfaceKey = {{System.Text.Json.JsonSerializer.Serialize(surfaceKey)}};
			  var messagePrefix = '__LUDOTS_MSG__:';
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
			      addEventListener: function (type, listener, options) {
			        window.addEventListener(type, listener, options);
			      },
			      removeEventListener: function (type, listener, options) {
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

			    var byteOffset = descriptor.byteOffset;
			    if (byteOffset === undefined || byteOffset === null) {
			      byteOffset = descriptor.ByteOffset;
			    }
			    if (byteOffset === undefined || byteOffset === null) {
			      byteOffset = 0;
			    }

			    var byteLength = descriptor.byteLength;
			    if (byteLength === undefined || byteLength === null) {
			      byteLength = descriptor.ByteLength;
			    }
			    if (byteLength === undefined || byteLength === null) {
			      byteLength = 0;
			    }

			    var sequence = descriptor.sequence;
			    if (sequence === undefined || sequence === null) {
			      sequence = descriptor.Sequence;
			    }
			    if (sequence === undefined || sequence === null) {
			      sequence = 0;
			    }

			    return {
			      bufferId: String(descriptor.bufferId || descriptor.BufferId || ''),
			      byteOffset: Number(byteOffset),
			      byteLength: Number(byteLength),
			      sequence: Number(sequence)
			    };
			  }

			  function decodeBase64(base64) {
			    var binary = atob(base64);
			    var bytes = new Uint8Array(binary.length);
			    for (var i = 0; i < binary.length; i++) {
			      bytes[i] = binary.charCodeAt(i);
			    }
			    return bytes;
			  }

			  window.__ludotsResolveSharedRead = function resolveSharedRead(requestId, base64, error) {
			    var pending = window.__ludotsSharedReadPending[requestId];
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
			    postMessage: function (message) {
			      window.ludotsBrowser.postMessage(message);
			    },
			    readSharedBuffer: function (descriptor) {
			      var normalized = normalizeDescriptor(descriptor);
			      var requestId = 'ul-shared-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
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
			    addEventListener: function (type, listener, options) {
			      window.addEventListener(type, listener, options);
			    },
			    removeEventListener: function (type, listener, options) {
			      window.removeEventListener(type, listener, options);
			    }
			  };
			})();
			""";
	}
}
