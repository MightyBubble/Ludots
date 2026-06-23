import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import {
  DATA_PLANE_DEFAULT_TOPIC,
  createLudotsDataPlaneClient,
  decodeEntityColumnarPacket,
  ensureLudotsDataPlaneTransport
} from './client.js';

test('explicit mock transport completes handshake, subscribe, snapshot, command ack', async () => {
  const root = createRoot();
  const { transport, installedFake } = ensureLudotsDataPlaneTransport({
    root,
    mode: 'mock',
    intervalMs: 10,
    latencyMs: 0
  });
  const client = createLudotsDataPlaneClient({
    root,
    transport,
    installedFake,
    requestTimeoutMs: 1000
  });
  const events = [];

  const handshake = await client.handshake({ test: true });
  assert.equal(handshake.kind, 'handshakeAck');
  assert.equal(client.getStatus().phase, 'connected');

  await client.subscribe(DATA_PLANE_DEFAULT_TOPIC, (event) => events.push(event));
  assert.equal(events[0].kind, 'snapshot');
  assert.equal(events[0].payload.entityCount, 3);

  const ack = await client.command('inspectEntity', { nodeId: 'unit.guard' });
  assert.equal(ack.kind, 'commandAck');
  assert.ok(events.some((event) => event.kind === 'delta'));

  client.close();
  transport.dispose();
});

test('standard facade is the only production transport entrypoint', async () => {
  const posted = [];
  const root = createRoot({
    ludotsDataplane: {
      name: 'ludots.standard.facade',
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

  const { transport, installedFake, mode } = ensureLudotsDataPlaneTransport({ root });
  assert.equal(transport, root.ludotsDataplane);
  assert.equal(installedFake, false);
  assert.equal(mode, 'standard');

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

test('BLUI-like facade can adapt its private bridge behind window.ludotsDataplane', async () => {
  const root = createRoot();
  const outbound = [];
  const bluiFacade = {
    name: 'blui.ludots-dataplane',
    postMessage(message) {
      outbound.push(message);
      this.dispatch({
        schemaVersion: 1,
        sessionId: message.sessionId,
        requestId: message.requestId,
        kind: 'Control',
        topic: message.topic,
        delivery: 'ReliableOrdered',
        contentType: 'application/json+ludots-dataplane-control',
        payload: {
          schemaVersion: 1,
          sessionId: message.sessionId,
          requestId: message.requestId,
          kind: 'handshakeAck',
          topic: message.topic,
          payload: {
            sessionId: 'blui-session',
            transportMode: 'message',
            capabilities: { modeName: 'message' }
          }
        }
      });
    },
    addEventListener(type, listener) {
      root.addEventListener(type, listener);
    },
    removeEventListener(type, listener) {
      root.removeEventListener(type, listener);
    },
    dispatch(message) {
      root.dispatchEvent(new MessageEvent('message', { data: message }));
    }
  };
  root.ludotsDataplane = bluiFacade;

  const { transport, mode } = ensureLudotsDataPlaneTransport({ root });
  const client = createLudotsDataPlaneClient({
    root,
    transport,
    requestTimeoutMs: 100
  });

  const ack = await client.handshake({ host: 'ue5-blui' });

  assert.equal(mode, 'standard');
  assert.equal(transport.name, 'blui.ludots-dataplane');
  assert.equal(outbound[0].kind, 'handshake');
  assert.equal(ack.kind, 'handshakeAck');
  assert.equal(client.getStatus().sessionId, 'blui-session');
  client.close();
});

test('plain browser preview must opt into mock transport explicitly', () => {
  const root = createRoot();

  assert.throws(
    () => ensureLudotsDataPlaneTransport({ root }),
    /window\.ludotsDataplane/
  );
  assert.equal(root.ludotsDataplane, undefined);

  const { transport, installedFake, mode } = ensureLudotsDataPlaneTransport({ root, mode: 'mock' });
  assert.equal(transport.name, 'window.ludotsDataplane.mock');
  assert.equal(installedFake, true);
  assert.equal(mode, 'mock');
  transport.dispose();
});

test('client source does not depend on CefSharp or BLUI private globals', () => {
  const sources = [
    fs.readFileSync(new URL('./client.js', import.meta.url), 'utf8'),
    fs.readFileSync(new URL('./DataPlanePanel.jsx', import.meta.url), 'utf8'),
    fs.readFileSync(new URL('../main.jsx', import.meta.url), 'utf8')
  ];

  for (const source of sources) {
    assert.equal(source.includes('CefSharp'), false);
    assert.equal(source.includes('BLUI'), false);
  }
});

test('production web sources do not expose legacy fake or fallback transport entrypoints', () => {
  const sources = [
    fs.readFileSync(new URL('./client.js', import.meta.url), 'utf8'),
    fs.readFileSync(new URL('./DataPlanePanel.jsx', import.meta.url), 'utf8'),
    fs.readFileSync(new URL('../main.jsx', import.meta.url), 'utf8')
  ];

  for (const source of sources) {
    assert.equal(source.includes('forceFake'), false);
    assert.equal(source.includes('createFake'), false);
    assert.equal(source.includes('fake transport'), false);
    assert.equal(source.includes('fallback'), false);
  }
});

test('columnar decoder returns typed-array views', () => {
  const bytes = new Uint8Array(20 + 20);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, 0x5044574c, true);
  view.setUint16(4, 1, true);
  view.setUint8(6, 1);
  view.setInt32(8, 17, true);
  view.setInt32(12, 1, true);
  view.setInt32(16, 0, true);
  view.setInt32(20, 1001, true);
  view.setInt32(24, 4, true);
  view.setFloat32(28, 12.5, true);
  view.setFloat32(32, 24.5, true);
  view.setUint16(36, 94, true);
  view.setUint8(38, 2);
  view.setUint8(39, 7);

  const decoded = decodeEntityColumnarPacket(bytes);

  assert.equal(decoded.kind, 'snapshot');
  assert.equal(decoded.schemaId, 17);
  assert.ok(decoded.stableIds instanceof Int32Array);
  assert.ok(decoded.x instanceof Float32Array);
  assert.ok(decoded.hp instanceof Uint16Array);
  assert.equal(decoded.stableIds[0], 1001);
  assert.equal(decoded.generations[0], 4);
  assert.equal(decoded.x[0], 12.5);
  assert.equal(decoded.y[0], 24.5);
  assert.equal(decoded.hp[0], 94);
  assert.equal(decoded.team[0], 2);
  assert.equal(decoded.state[0], 7);
});

test('shared-buffer descriptor events read bytes through the standard facade', async () => {
  const root = createRoot();
  const bytes = createEntityColumnarPacket();
  const reads = [];
  const facade = {
    name: 'cef.ludots-dataplane',
    mode: 'browser-native-bridge',
    postMessage(message) {
      if (message.kind === 'handshake') {
        this.dispatch({
          schemaVersion: 1,
          sessionId: message.sessionId,
          requestId: message.requestId,
          kind: 'Control',
          topic: message.topic,
          delivery: 'ReliableOrdered',
          contentType: 'application/json+ludots-dataplane-control',
          payload: {
            schemaVersion: 1,
            sessionId: message.sessionId,
            requestId: message.requestId,
            kind: 'handshakeAck',
            topic: message.topic,
            payload: {
              sessionId: 'shared-session',
              transportMode: 'shared-memory',
              capabilities: { modeName: 'shared-memory' }
            }
          }
        });
        return;
      }

      if (message.kind === 'subscribe') {
        this.dispatch({
          schemaVersion: 1,
          sessionId: message.sessionId,
          requestId: message.requestId,
          kind: 'Control',
          topic: message.topic,
          delivery: 'ReliableOrdered',
          contentType: 'application/json+ludots-dataplane-control',
          payload: {
            schemaVersion: 1,
            sessionId: message.sessionId,
            requestId: message.requestId,
            kind: 'subscribed',
            topic: message.topic,
            payload: { subscriptionId: message.payload.subscriptionId }
          }
        });
      }
    },
    readSharedBuffer(descriptor) {
      reads.push(descriptor);
      return Promise.resolve(bytes);
    },
    addEventListener(type, listener) {
      root.addEventListener(type, listener);
    },
    removeEventListener(type, listener) {
      root.removeEventListener(type, listener);
    },
    dispatch(message) {
      root.dispatchEvent(new MessageEvent('message', { data: message }));
    }
  };
  root.ludotsDataplane = facade;
  const client = createLudotsDataPlaneClient({
    root,
    transport: facade,
    requestTimeoutMs: 100
  });
  const events = [];

  await client.handshake({ test: 'shared-memory' });
  await client.subscribe(DATA_PLANE_DEFAULT_TOPIC, (event) => events.push(event));
  facade.dispatch({
    channel: 'ludots.dataplane.sharedBuffer',
    payload: JSON.stringify({
      schemaVersion: 1,
      sessionId: 'shared-session',
      requestId: 0,
      kind: 'Snapshot',
      topic: DATA_PLANE_DEFAULT_TOPIC,
      delivery: 'LatestWins',
      contentType: 'application/octet-stream',
      payload: {
        sharedBuffer: {
          bufferId: 'browser-react-flow.world.0',
          topic: DATA_PLANE_DEFAULT_TOPIC,
          schemaId: 17,
          layout: 'ring-buffer',
          capacityBytes: 4096,
          headerBytes: 64,
          byteOffset: 64,
          byteLength: bytes.byteLength,
          sequence: 1,
          tick: 42,
          droppedPackets: 0,
          coalescedPackets: 0
        }
      }
    })
  });

  const event = await waitFor(() => events.find((item) => item.kind === 'snapshot'));
  const decoded = decodeEntityColumnarPacket(event.bytes);

  assert.equal(reads.length, 1);
  assert.equal(reads[0].bufferId, 'browser-react-flow.world.0');
  assert.ok(event.bytes instanceof Uint8Array);
  assert.equal(event.binaryBytes, bytes.byteLength);
  assert.equal(event.binaryChunks[0].transport, 'shared-memory');
  assert.equal(decoded.stableIds[0], 1001);
  assert.equal(decoded.x[0], 12.5);
  client.close();
});

function createRoot(overrides = {}) {
  const eventTarget = new EventTarget();
  return {
    setTimeout,
    clearTimeout,
    setInterval,
    clearInterval,
    addEventListener: eventTarget.addEventListener.bind(eventTarget),
    removeEventListener: eventTarget.removeEventListener.bind(eventTarget),
    dispatchEvent: eventTarget.dispatchEvent.bind(eventTarget),
    ...overrides
  };
}

function createEntityColumnarPacket() {
  const bytes = new Uint8Array(20 + 20);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, 0x5044574c, true);
  view.setUint16(4, 1, true);
  view.setUint8(6, 1);
  view.setInt32(8, 17, true);
  view.setInt32(12, 1, true);
  view.setInt32(16, 0, true);
  view.setInt32(20, 1001, true);
  view.setInt32(24, 4, true);
  view.setFloat32(28, 12.5, true);
  view.setFloat32(32, 24.5, true);
  view.setUint16(36, 94, true);
  view.setUint8(38, 2);
  view.setUint8(39, 7);
  return bytes;
}

async function waitFor(predicate) {
  const started = Date.now();
  while (Date.now() - started < 250) {
    const value = predicate();
    if (value) {
      return value;
    }

    await new Promise((resolve) => setTimeout(resolve, 0));
  }

  throw new Error('Timed out waiting for predicate.');
}
