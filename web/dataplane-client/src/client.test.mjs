import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './client.js';

describe('@ludots/dataplane-client', () => {
  it('fails fast when window.ludotsDataplane is absent', () => {
    const root = {};
    assert.throws(
      () => ensureLudotsDataPlaneTransport({ root }),
      /window\.ludotsDataplane is required/
    );
  });

  it('uses host transport when present', () => {
    const transport = { name: 'host', postMessage() {} };
    const root = { ludotsDataplane: transport };
    const resolved = ensureLudotsDataPlaneTransport({ root });
    assert.equal(resolved.transport, transport);
    assert.equal(resolved.hostBacked, true);
  });

  it('handshakes and delivers command ack over a fake transport', async () => {
    const listeners = new Set();
    const transport = {
      name: 'test-transport',
      postMessage(envelope) {
        const response = {
          schemaVersion: 1,
          sessionId: envelope.sessionId,
          requestId: envelope.requestId,
          kind: envelope.kind === 'handshake' ? 'handshakeAck' : 'commandAck',
          topic: envelope.topic,
          payload: {
            sessionId: envelope.sessionId,
            transportName: 'test-transport',
            clientSeq: envelope.payload?.clientSeq ?? 0
          }
        };
        queueMicrotask(() => {
          for (const listener of listeners) {
            listener({ data: response });
          }
        });
      },
      addEventListener(_type, listener) {
        listeners.add(listener);
      },
      removeEventListener(_type, listener) {
        listeners.delete(listener);
      }
    };

    const client = createLudotsDataPlaneClient({
      transport,
      hostBacked: true,
      sessionId: 'test-session',
      defaultTopic: 'ludots.capability.liveSkillWorkbench.session'
    });

    const handshake = await client.handshake({ app: 'test' });
    assert.equal(handshake.kind, 'handshakeAck');
    assert.equal(client.getStatus().phase, 'connected');

    const ack = await client.command('stageEdit', { definitionId: 'Fireball', fieldPath: 'damage', numericValue: 80 });
    assert.equal(ack.kind, 'commandAck');
    client.close();
  });

  it('rejects commandError payloads', async () => {
    const listeners = new Set();
    const transport = {
      name: 'test-transport',
      postMessage(envelope) {
        const response = {
          schemaVersion: 1,
          sessionId: envelope.sessionId,
          requestId: envelope.requestId,
          kind: 'commandError',
          topic: envelope.topic,
          payload: { code: 'not_supported', message: 'Apply is not supported yet.' }
        };
        queueMicrotask(() => {
          for (const listener of listeners) {
            listener({ data: response });
          }
        });
      },
      addEventListener(_type, listener) {
        listeners.add(listener);
      },
      removeEventListener(_type, listener) {
        listeners.delete(listener);
      }
    };

    const client = createLudotsDataPlaneClient({ transport, sessionId: 'err-session' });
    await assert.rejects(
      () => client.command('applyStaged', {}),
      /Apply is not supported yet/
    );
    client.close();
  });
});
