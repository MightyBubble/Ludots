import assert from 'node:assert/strict';
import test from 'node:test';
import {
  DATA_PLANE_DEFAULT_TOPIC,
  createLudotsDataPlaneClient,
  decodeEntityColumnarPacket,
  ensureLudotsDataPlaneTransport
} from './client.js';

test('fake transport completes handshake, subscribe, snapshot, command ack', async () => {
  const root = globalThis;
  const { transport, installedFake } = ensureLudotsDataPlaneTransport({
    root,
    forceFake: true,
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
