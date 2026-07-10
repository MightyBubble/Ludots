import assert from 'node:assert/strict';
import test from 'node:test';
import {
  ENTITY_COMMAND_PANEL_SET_PROFILE_COMMAND,
  ENTITY_COMMAND_PANEL_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './client.js';

test('requires the standard Ludots DataPlane facade', () => {
  assert.throws(
    () => ensureLudotsDataPlaneTransport({ root: {} }),
    /window\.ludotsDataplane is required/);
});

test('setProfile command targets the entity command panel topic', async () => {
  const sent = [];
  const listeners = [];
  const transport = {
    name: 'test-transport',
    postMessage(message) {
      sent.push(message);
      queueMicrotask(() => {
        for (const listener of listeners) {
          listener({
            data: {
              schemaVersion: 1,
              sessionId: message.sessionId,
              requestId: message.requestId,
              kind: 'commandAck',
              topic: message.topic,
              payload: { ok: true }
            }
          });
        }
      });
    },
    addEventListener(type, listener) {
      if (type === 'message') {
        listeners.push(listener);
      }
    },
    removeEventListener() {}
  };
  const root = {
    setTimeout,
    clearTimeout,
    addEventListener() {},
    removeEventListener() {}
  };
  const client = createLudotsDataPlaneClient({
    root,
    transport,
    sessionId: 'test-session',
    requestTimeoutMs: 100
  });

  await client.command(ENTITY_COMMAND_PANEL_SET_PROFILE_COMMAND, { profile: 'Ability' });

  assert.equal(sent.length, 1);
  assert.equal(sent[0].kind, 'command');
  assert.equal(sent[0].topic, ENTITY_COMMAND_PANEL_TOPIC);
  assert.equal(sent[0].payload.name, ENTITY_COMMAND_PANEL_SET_PROFILE_COMMAND);
  assert.deepEqual(sent[0].payload.payload, { profile: 'Ability' });
  client.close();
});

test('snapshot events are delivered to matching subscriptions', async () => {
  const listeners = [];
  const sent = [];
  const transport = {
    name: 'test-transport',
    postMessage(message) {
      sent.push(message);
      queueMicrotask(() => {
        for (const listener of listeners) {
          listener({
            data: {
              schemaVersion: 1,
              sessionId: message.sessionId,
              requestId: message.requestId,
              kind: message.kind === 'handshake' ? 'handshakeAck' : 'subscribeAck',
              topic: message.topic,
              payload: {}
            }
          });
        }
      });
    },
    addEventListener(type, listener) {
      if (type === 'message') {
        listeners.push(listener);
      }
    },
    removeEventListener() {}
  };
  const root = {
    setTimeout,
    clearTimeout,
    addEventListener() {},
    removeEventListener() {}
  };
  const client = createLudotsDataPlaneClient({
    root,
    transport,
    sessionId: 'test-session',
    requestTimeoutMs: 100
  });
  const snapshots = [];

  await client.handshake({ app: 'test' });
  await client.subscribe(ENTITY_COMMAND_PANEL_TOPIC, event => snapshots.push(event.payload));
  for (const listener of listeners) {
    listener({
      data: {
        schemaVersion: 1,
        sessionId: 'test-session',
        requestId: 0,
        kind: 'snapshot',
        topic: ENTITY_COMMAND_PANEL_TOPIC,
        payload: { activeProfile: 'Family', tileCount: 8 }
      }
    });
  }

  assert.equal(snapshots.length, 1);
  assert.deepEqual(snapshots[0], { activeProfile: 'Family', tileCount: 8 });
  client.close();
});
