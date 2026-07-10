import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import {
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './client.js';

test('standard Ludots facade is the only host transport entrypoint', async () => {
  const posted = [];
  const root = createRoot({
    ludotsDataplane: {
      name: 'ludots.dataplane',
      postMessage(message) {
        posted.push(message);
      },
      addEventListener() {},
      removeEventListener() {}
    },
    CefSharp: {
      PostMessage() {
        throw new Error('client must not call CefSharp directly');
      }
    }
  });

  const { transport, hostBacked } = ensureLudotsDataPlaneTransport({ root });
  assert.equal(transport, root.ludotsDataplane);
  assert.equal(hostBacked, true);

  const client = createLudotsDataPlaneClient({
    root,
    transport,
    requestTimeoutMs: 10
  });

  await assert.rejects(() => client.handshake({ test: true }), /timed out/);
  assert.equal(posted.length, 1);
  assert.equal(posted[0].kind, 'handshake');
  client.close();
});

test('missing Ludots facade fails instead of adapting provider globals', () => {
  const root = createRoot({
    CefSharp: {
      PostMessage() {
        throw new Error('provider global must not be used');
      }
    }
  });

  assert.throws(
    () => ensureLudotsDataPlaneTransport({ root }),
    /window\.ludotsDataplane/
  );
  assert.equal(root.ludotsDataplane, undefined);
});

test('production client source does not depend on CEF provider globals', () => {
  const source = fs.readFileSync(new URL('./client.js', import.meta.url), 'utf8');

  assert.equal(source.includes('CefSharp'), false);
  assert.equal(source.includes('cefsharp'), false);
  assert.equal(source.includes('window.cefSharp'), false);
});

function createRoot(overrides = {}) {
  const eventTarget = new EventTarget();
  return {
    setTimeout,
    clearTimeout,
    addEventListener: eventTarget.addEventListener.bind(eventTarget),
    removeEventListener: eventTarget.removeEventListener.bind(eventTarget),
    dispatchEvent: eventTarget.dispatchEvent.bind(eventTarget),
    ...overrides
  };
}
