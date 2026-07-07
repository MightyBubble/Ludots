const SCHEMA_VERSION = 1;
const DEFAULT_REQUEST_TIMEOUT_MS = 12000;
const DEFAULT_TOPIC = 'ludots.showcase.control_plane.state';
const CONTROL_CHANNEL = 'ludots.dataplane.control';
const TOGGLE_PROXY_COMMAND = 'toggleProxy';

const CLIENT_CAPABILITIES = Object.freeze([
  'handshake',
  'subscribe',
  'unsubscribe',
  'command',
  'diagnostics'
]);

export function ensureLudotsDataPlaneTransport(options = {}) {
  const root = options.root ?? globalThis;
  if (root.ludotsDataplane) {
    return { transport: root.ludotsDataplane, hostBacked: true };
  }

  throw new Error('window.ludotsDataplane is required for the Ludots DataPlane transport.');
}

export function createLudotsDataPlaneClient(options = {}) {
  const root = options.root ?? globalThis;
  const transport = options.transport ?? root.ludotsDataplane;
  const requestTimeoutMs = options.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;
  const sessionId = options.sessionId ?? createId('control-plane-session');
  const pendingRequests = new Map();
  const subscriptions = new Map();
  const diagnostics = new Set();
  const detachCallbacks = [];

  let closed = false;
  let requestId = 0;
  let clientSeq = 0;
  let status = {
    phase: 'idle',
    sessionId,
    transportName: resolveTransportName(transport),
    hostBacked: Boolean(options.hostBacked)
  };

  if (typeof options.diagnostics === 'function') {
    diagnostics.add(options.diagnostics);
  }

  attachMessageSource(transport, handleMessage, detachCallbacks);
  if (transport?.windowBacked !== true) {
    attachMessageSource(root, handleMessage, detachCallbacks);
  }

  return {
    capabilities: CLIENT_CAPABILITIES,
    getStatus: () => status,
    handshake,
    subscribe,
    unsubscribe,
    command,
    close
  };

  async function handshake(metadata = {}) {
    updateStatus({ phase: 'connecting' });
    const response = await request('handshake', 'system', {
      metadata,
      capabilities: CLIENT_CAPABILITIES,
      sdkVersion: '0.2.0-control-plane'
    });
    updateStatus({
      phase: 'connected',
      sessionId: response.sessionId ?? response.payload?.sessionId ?? sessionId,
      transportName: response.payload?.transportName ?? response.transportName ?? status.transportName
    });
    emitDiagnostic('info', 'handshake', 'DataPlane handshake acknowledged.', response);
    return response;
  }

  async function subscribe(topic = DEFAULT_TOPIC, handler, subscribeOptions = {}) {
    assertNotClosed();
    if (typeof handler !== 'function') {
      throw new TypeError('subscribe requires a handler.');
    }

    const subscription = {
      topic,
      handler,
      id: subscribeOptions.subscriptionId ?? createId('sub')
    };
    subscriptions.set(subscription.id, subscription);

    try {
      const response = await request('subscribe', topic, {
        subscriptionId: subscription.id,
        snapshot: subscribeOptions.snapshot !== false,
        cursor: subscribeOptions.cursor ?? null
      });
      emitDiagnostic('info', 'subscribe', `Subscribed to ${topic}.`, response);
      return {
        topic,
        subscriptionId: subscription.id,
        response,
        unsubscribe: () => unsubscribe(subscription.id)
      };
    } catch (error) {
      subscriptions.delete(subscription.id);
      throw error;
    }
  }

  async function unsubscribe(subscriptionIdOrTopic) {
    assertNotClosed();
    const matches = [...subscriptions.values()].filter((subscription) =>
      subscription.id === subscriptionIdOrTopic || subscription.topic === subscriptionIdOrTopic
    );

    const responses = [];
    for (const subscription of matches) {
      responses.push(await request('unsubscribe', subscription.topic, { subscriptionId: subscription.id }));
      subscriptions.delete(subscription.id);
    }

    return { ok: true, removed: responses.length, responses };
  }

  async function command(name, payload = {}, commandOptions = {}) {
    assertNotClosed();
    const seq = commandOptions.clientSeq ?? ++clientSeq;
    const response = await request('command', commandOptions.topic ?? DEFAULT_TOPIC, {
      name,
      clientSeq: seq,
      entityRefs: commandOptions.entityRefs ?? [],
      payload
    });
    emitDiagnostic('info', 'command', `Command ${name} acknowledged.`, response);
    return response;
  }

  function request(kind, topic, payload, requestOptions = {}) {
    assertNotClosed();
    const nextRequestId = ++requestId;
    const envelope = {
      schemaVersion: SCHEMA_VERSION,
      sessionId,
      requestId: nextRequestId,
      kind,
      topic,
      payload
    };

    return new Promise((resolve, reject) => {
      const timeout = root.setTimeout?.(() => {
        pendingRequests.delete(nextRequestId);
        reject(new Error(`DataPlane request timed out: ${kind}`));
      }, requestOptions.timeoutMs ?? requestTimeoutMs);

      pendingRequests.set(nextRequestId, { kind, resolve, reject, timeout });
      try {
        sendEnvelope(envelope);
      } catch (error) {
        if (timeout) {
          root.clearTimeout?.(timeout);
        }

        pendingRequests.delete(nextRequestId);
        reject(error);
      }
    });
  }

  function sendEnvelope(envelope) {
    if (transport?.postMessage) {
      transport.postMessage(envelope);
      return;
    }

    throw new Error('No Ludots DataPlane transport is available.');
  }

  function handleMessage(rawMessage) {
    if (closed) {
      return;
    }

    const incoming = normalizeIncomingMessage(rawMessage);
    if (!incoming) {
      return;
    }

    const pending = incoming.requestId ? pendingRequests.get(incoming.requestId) : null;
    if (pending) {
      pendingRequests.delete(incoming.requestId);
      if (pending.timeout) {
        root.clearTimeout?.(pending.timeout);
      }

      if (incoming.kind === 'commandError' || incoming.payload?.code || incoming.payload?.error) {
        pending.reject(new Error(incoming.payload?.message ?? incoming.payload?.error ?? `${pending.kind} failed.`));
      } else {
        pending.resolve(incoming);
      }
    }

    if (incoming.kind === 'snapshot' || incoming.kind === 'delta' || incoming.kind === 'diagnostics') {
      dispatchDataEvent(incoming);
    }
  }

  function dispatchDataEvent(event) {
    let delivered = 0;
    for (const subscription of subscriptions.values()) {
      if (subscription.topic === event.topic || subscription.id === event.subscriptionId) {
        subscription.handler(event);
        delivered += 1;
      }
    }

    emitDiagnostic('debug', `stream:${event.kind}`, `Received ${event.kind}.`, {
      topic: event.topic,
      delivered
    });
  }

  function close() {
    if (closed) {
      return;
    }

    closed = true;
    for (const detach of detachCallbacks) {
      detach();
    }
    detachCallbacks.length = 0;

    for (const pending of pendingRequests.values()) {
      if (pending.timeout) {
        root.clearTimeout?.(pending.timeout);
      }
      pending.reject(new Error('DataPlane client closed.'));
    }
    pendingRequests.clear();
    subscriptions.clear();
    updateStatus({ phase: 'closed' });
  }

  function updateStatus(patch) {
    status = { ...status, ...patch };
  }

  function emitDiagnostic(level, type, message, detail = {}) {
    for (const listener of diagnostics) {
      listener({ level, type, message, detail, at: Date.now() });
    }
  }

  function assertNotClosed() {
    if (closed) {
      throw new Error('DataPlane client is closed.');
    }
  }
}

function normalizeIncomingMessage(rawMessage) {
  const data = rawMessage && typeof rawMessage === 'object' && 'data' in rawMessage
    ? rawMessage.data
    : rawMessage;

  if (!data) {
    return null;
  }

  if (typeof data === 'string') {
    return normalizeHostEnvelope(parseJsonOrNull(data));
  }

  if (data.channel === CONTROL_CHANNEL) {
    return normalizeHostEnvelope(parseJsonOrNull(data.payload) ?? data.payload);
  }

  return normalizeHostEnvelope(data);
}

function normalizeHostEnvelope(envelope) {
  if (!envelope || typeof envelope !== 'object' || envelope.schemaVersion !== SCHEMA_VERSION) {
    return null;
  }

  const packetKind = normalizeKind(envelope.kind);
  if (envelope.payload?.schemaVersion === SCHEMA_VERSION) {
    return {
      schemaVersion: SCHEMA_VERSION,
      sessionId: envelope.payload.sessionId ?? envelope.sessionId,
      requestId: envelope.payload.requestId ?? envelope.requestId,
      kind: normalizeKind(envelope.payload.kind),
      topic: envelope.payload.topic ?? envelope.topic,
      payload: envelope.payload.payload ?? {},
      packetKind
    };
  }

  return {
    schemaVersion: SCHEMA_VERSION,
    sessionId: envelope.sessionId,
    requestId: envelope.requestId,
    kind: packetKind,
    topic: envelope.topic,
    payload: envelope.payload ?? {},
    packetKind
  };
}

function attachMessageSource(source, handler, detachCallbacks) {
  if (!source?.addEventListener) {
    return;
  }

  source.addEventListener('message', handler);
  detachCallbacks.push(() => source.removeEventListener('message', handler));
}

function normalizeKind(kind) {
  if (!kind) {
    return 'unknown';
  }

  const text = String(kind);
  return text.charAt(0).toLowerCase() + text.slice(1);
}

function resolveTransportName(transport) {
  return transport?.name ?? transport?.transportName ?? 'none';
}

function parseJsonOrNull(text) {
  try {
    return typeof text === 'string' ? JSON.parse(text) : text;
  } catch {
    return null;
  }
}

function createId(prefix) {
  return `${prefix}-${Math.random().toString(16).slice(2, 10)}-${Date.now().toString(16)}`;
}

export const CONTROL_PLANE_TOPIC = DEFAULT_TOPIC;
export const CONTROL_PLANE_TOGGLE_PROXY_COMMAND = TOGGLE_PROXY_COMMAND;
