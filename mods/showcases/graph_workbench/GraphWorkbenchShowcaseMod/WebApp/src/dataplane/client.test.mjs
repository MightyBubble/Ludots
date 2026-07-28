import assert from 'node:assert/strict';
import test from 'node:test';
import { normalizeIncomingMessage } from './client.js';

test('normalizes nested Ludots DataPlane control envelope', () => {
  const message = normalizeIncomingMessage({
    channel: 'ludots.dataplane.control',
    payload: JSON.stringify({
      schemaVersion: 1,
      sessionId: 's1',
      requestId: 7,
      kind: 'Control',
      topic: 'system',
      payload: {
        schemaVersion: 1,
        sessionId: 's1',
        requestId: 7,
        kind: 'handshakeAck',
        topic: 'system',
        payload: { sessionId: 'host' }
      }
    })
  });

  assert.equal(message.kind, 'handshakeAck');
  assert.equal(message.requestId, 7);
  assert.equal(message.payload.sessionId, 'host');
});
