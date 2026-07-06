import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import {
  DATA_PLANE_DEFAULT_TOPIC,
  applyEntityColumnarPacket,
  createEntityAttributeView,
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

test('entity attribute view maps 50k decoded rows into bounded web controls', () => {
  const bytes = createEntityColumnarPacket(50_000);
  const decoded = decodeEntityColumnarPacket(bytes);

  const view = createEntityAttributeView(decoded, {
    visibleStart: 1024,
    visibleCount: 32,
    bucketCount: 64
  });

  assert.equal(view.entityCount, 50_000);
  assert.equal(view.visibleRows.length, 32);
  assert.equal(view.visibleRows[0].stableId, 1001 + 1024);
  assert.equal(view.buckets.length, 64);
  assert.equal(view.summary.teamCounts.reduce((sum, count) => sum + count, 0), 50_000);
  assert.ok(view.summary.avgHp > 0);
  assert.ok(view.summary.activeRows > 0);
});

test('entity attribute view can reuse bounded arrays while remapping a 50k store', () => {
  const first = applyEntityColumnarPacket(null, decodeEntityColumnarPacket(createEntitySoaPacket(50_000, 1, 1)));
  const second = applyEntityColumnarPacket(first, decodeEntityColumnarPacket(createEntitySoaFullDeltaPacket(50_000, 2, 2)));
  const view = createEntityAttributeView(first, {
    visibleStart: 1024,
    visibleCount: 32,
    bucketCount: 64
  });
  const rows = view.visibleRows;
  const firstRow = rows[0];
  const buckets = view.buckets;
  const teamCounts = view.summary.teamCounts;

  const reused = createEntityAttributeView(second, {
    visibleStart: 1024,
    visibleCount: 32,
    bucketCount: 64,
    reuse: view
  });

  assert.equal(reused, view);
  assert.equal(reused.visibleRows, rows);
  assert.equal(reused.visibleRows[0], firstRow);
  assert.equal(reused.buckets, buckets);
  assert.equal(reused.summary.teamCounts, teamCounts);
  assert.equal(reused.visibleRows[0].hp, second.hp[1024]);
  assert.equal(reused.summary.teamCounts.reduce((sum, count) => sum + count, 0), 50_000);
});

test('SoA full snapshot maps 50k changed attributes without row object expansion', () => {
  const bytes = createEntitySoaPacket(50_000, 19, 77);
  const decoded = decodeEntityColumnarPacket(bytes);

  assert.equal(decoded.kind, 'soaSnapshot');
  assert.equal(decoded.sequence, 19);
  assert.equal(decoded.tick, 77);
  assert.equal(decoded.stableIds.length, 50_000);
  assert.equal(decoded.stableIds.buffer, bytes.buffer);
  assert.equal(decoded.hp.buffer, bytes.buffer);

  const store = applyEntityColumnarPacket(null, decoded);
  assert.equal(store.stableIds, decoded.stableIds);
  assert.equal(store.hp, decoded.hp);
  assert.equal(store.lastDeltaRows, 50_000);
  assert.equal(store.tick, 77);

  const view = createEntityAttributeView(store, {
    visibleStart: 49_968,
    visibleCount: 32,
    bucketCount: 64
  });
  assert.equal(view.entityCount, 50_000);
  assert.equal(view.visibleRows.length, 32);
  assert.equal(view.visibleRows[0].stableId, 1001 + 49_968);
  assert.equal(view.summary.teamCounts.reduce((sum, count) => sum + count, 0), 50_000);
});

test('indexed delta patches a persistent SoA store in place', () => {
  const snapshot = decodeEntityColumnarPacket(createEntitySoaPacket(50_000, 1, 1));
  const store = applyEntityColumnarPacket(null, snapshot);
  const previousStableIds = store.stableIds;
  const delta = decodeEntityColumnarPacket(createIndexedDeltaPacket([
    { index: 1024, generation: 8, x: 88.5, y: 99.5, hp: 51, state: 6 },
    { index: 49_999, generation: 9, x: 188.5, y: 199.5, hp: 52, state: 7 }
  ]));

  const updated = applyEntityColumnarPacket(store, delta);

  assert.equal(updated, store);
  assert.equal(updated.stableIds, previousStableIds);
  assert.equal(updated.generations[1024], 8);
  assert.equal(updated.x[1024], 88.5);
  assert.equal(updated.y[49_999], 199.5);
  assert.equal(updated.hp[49_999], 52);
  assert.equal(updated.state[49_999], 7);
  assert.equal(updated.lastDeltaRows, 2);
});

test('SoA full delta maps 50k dynamic attribute changes onto a snapshot store', () => {
  const snapshot = decodeEntityColumnarPacket(createEntitySoaPacket(50_000, 1, 1));
  const store = applyEntityColumnarPacket(null, snapshot);
  const stableIds = store.stableIds;
  const team = store.team;
  const delta = decodeEntityColumnarPacket(createEntitySoaFullDeltaPacket(50_000, 2, 2));

  const updated = applyEntityColumnarPacket(store, delta);

  assert.equal(updated, store);
  assert.equal(updated.stableIds, stableIds);
  assert.equal(updated.team, team);
  assert.equal(updated.generations, delta.generations);
  assert.equal(updated.hp, delta.hp);
  assert.equal(updated.lastDeltaRows, 50_000);
  assert.equal(updated.tick, 2);
  assert.equal(updated.x[49_999], delta.x[49_999]);
  assert.equal(updated.state[49_999], delta.state[49_999]);
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

test('shared-buffer snapshot can acknowledge subscribe request directly', async () => {
  const root = createRoot();
  const bytes = createEntityColumnarPacket(50_000);
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
          channel: 'ludots.dataplane.sharedBuffer',
          payload: JSON.stringify({
            schemaVersion: 1,
            sessionId: message.sessionId,
            requestId: message.requestId,
            kind: 'Snapshot',
            topic: message.topic,
            delivery: 'LatestWins',
            contentType: 'application/octet-stream',
            payload: {
              sharedBuffer: {
                bufferId: 'browser-react-flow.world.0',
                topic: message.topic,
                schemaId: 17,
                layout: 'ring-buffer',
                capacityBytes: bytes.byteLength + 64,
                headerBytes: 64,
                byteOffset: 64,
                byteLength: bytes.byteLength,
                sequence: 1,
                tick: 1,
                droppedPackets: 0,
                coalescedPackets: 0
              }
            }
          })
        });
      }
    },
    readSharedBuffer() {
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

  await client.handshake({ test: 'shared-memory-subscribe' });
  const subscription = await client.subscribe(DATA_PLANE_DEFAULT_TOPIC, (event) => events.push(event));

  assert.equal(subscription.topic, DATA_PLANE_DEFAULT_TOPIC);
  assert.equal(events[0].kind, 'snapshot');
  assert.equal(events[0].binaryBytes, bytes.byteLength);
  assert.equal(decodeEntityColumnarPacket(events[0].bytes).stableIds.length, 50_000);
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

function createEntityColumnarPacket(rowCount = 1) {
  const bytes = new Uint8Array(20 + rowCount * 20);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, 0x5044574c, true);
  view.setUint16(4, 1, true);
  view.setUint8(6, 1);
  view.setInt32(8, 17, true);
  view.setInt32(12, rowCount, true);
  view.setInt32(16, 0, true);
  let offset = 20;
  for (let index = 0; index < rowCount; index += 1) {
    view.setInt32(offset, 1001 + index, true);
    view.setInt32(offset + 4, 4 + (index % 3), true);
    view.setFloat32(offset + 8, 12.5 + (index % 512), true);
    view.setFloat32(offset + 12, 24.5 + Math.floor(index / 512), true);
    view.setUint16(offset + 16, 25 + (index % 75), true);
    view.setUint8(offset + 18, index % 8);
    view.setUint8(offset + 19, index % 7);
    offset += 20;
  }
  return bytes;
}

function createEntitySoaPacket(rowCount = 1, sequence = 1, tick = 1) {
  const bytes = new Uint8Array(32 + rowCount * (4 + 4 + 4 + 4 + 2 + 1 + 1));
  const view = new DataView(bytes.buffer);
  view.setUint32(0, 0x5044574c, true);
  view.setUint16(4, 1, true);
  view.setUint8(6, 1);
  view.setUint8(7, 1);
  view.setInt32(8, 1, true);
  view.setInt32(12, rowCount, true);
  view.setBigInt64(16, BigInt(sequence), true);
  view.setBigInt64(24, BigInt(tick), true);
  let offset = 32;
  const stableIds = new Int32Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 4;
  const generations = new Int32Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 4;
  const x = new Float32Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 4;
  const y = new Float32Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 4;
  const hp = new Uint16Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 2;
  const team = new Uint8Array(bytes.buffer, offset, rowCount);
  offset += rowCount;
  const state = new Uint8Array(bytes.buffer, offset, rowCount);
  for (let index = 0; index < rowCount; index += 1) {
    stableIds[index] = 1001 + index;
    generations[index] = tick;
    x[index] = 12.5 + (index % 512);
    y[index] = 24.5 + Math.floor(index / 512);
    hp[index] = 25 + (index % 75);
    team[index] = index % 8;
    state[index] = index % 7;
  }
  return bytes;
}

function createEntitySoaFullDeltaPacket(rowCount = 1, sequence = 1, tick = 1) {
  const bytes = new Uint8Array(32 + rowCount * (4 + 4 + 4 + 2 + 1));
  const view = new DataView(bytes.buffer);
  view.setUint32(0, 0x5044574c, true);
  view.setUint16(4, 1, true);
  view.setUint8(6, 1);
  view.setUint8(7, 2);
  view.setInt32(8, 1, true);
  view.setInt32(12, rowCount, true);
  view.setBigInt64(16, BigInt(sequence), true);
  view.setBigInt64(24, BigInt(tick), true);
  let offset = 32;
  const generations = new Int32Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 4;
  const x = new Float32Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 4;
  const y = new Float32Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 4;
  const hp = new Uint16Array(bytes.buffer, offset, rowCount);
  offset += rowCount * 2;
  const state = new Uint8Array(bytes.buffer, offset, rowCount);
  for (let index = 0; index < rowCount; index += 1) {
    generations[index] = tick;
    x[index] = 112.5 + (index % 512);
    y[index] = 224.5 + Math.floor(index / 512);
    hp[index] = 35 + (index % 65);
    state[index] = index % 8;
  }
  return bytes;
}

function createIndexedDeltaPacket(rows) {
  const bytes = new Uint8Array(20 + rows.length * 19);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, 0x5044574c, true);
  view.setUint16(4, 1, true);
  view.setUint8(6, 3);
  view.setInt32(8, 1, true);
  view.setInt32(12, rows.length, true);
  view.setInt32(16, 0, true);
  let offset = 20;
  for (const row of rows) {
    view.setInt32(offset, row.index, true);
    view.setInt32(offset + 4, row.generation, true);
    view.setFloat32(offset + 8, row.x, true);
    view.setFloat32(offset + 12, row.y, true);
    view.setUint16(offset + 16, row.hp, true);
    view.setUint8(offset + 18, row.state);
    offset += 19;
  }
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
