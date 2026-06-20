const SCHEMA_VERSION = 1;
const CONTROL_CHANNEL = 'ludots.dataplane.control';
const BINARY_CHUNK_CHANNEL = 'ludots.dataplane.binaryChunk';
const SDK_VERSION = '0.2.0-showcase';
const DEFAULT_REQUEST_TIMEOUT_MS = 4000;
const DEFAULT_TOPIC = 'ludots.showcase.browserReactFlow.world';

const CLIENT_CAPABILITIES = Object.freeze([
  'handshake',
  'subscribe',
  'unsubscribe',
  'command',
  'binary.base64',
  'entity-columnar.v1',
  'diagnostics'
]);

export function ensureLudotsDataPlaneTransport(options = {}) {
  const root = options.root ?? globalThis;

  if (root.ludotsDataplane && options.forceFake !== true) {
    return { transport: root.ludotsDataplane, installedFake: false };
  }

  if (root.CefSharp?.PostMessage && options.forceFake !== true) {
    const cefTransport = {
      name: 'cefsharp.ludots-dataplane',
      windowBacked: true,
      postMessage(message) {
        root.CefSharp.PostMessage(message);
      },
      addEventListener(type, listener, listenerOptions) {
        root.addEventListener(type, listener, listenerOptions);
      },
      removeEventListener(type, listener, listenerOptions) {
        root.removeEventListener(type, listener, listenerOptions);
      }
    };
    root.ludotsDataplane = cefTransport;
    return { transport: cefTransport, installedFake: false };
  }

  const fakeTransport = createFakeLudotsDataPlaneTransport({
    intervalMs: options.intervalMs,
    latencyMs: options.latencyMs
  });
  root.ludotsDataplane = fakeTransport;
  return { transport: fakeTransport, installedFake: true };
}

export function createLudotsDataPlaneClient(options = {}) {
  const root = options.root ?? globalThis;
  const transport = options.transport ?? root.ludotsDataplane;
  const requestTimeoutMs = options.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;
  const sessionId = options.sessionId ?? createId('web-session');
  const diagnosticsListeners = new Set();
  const pendingRequests = new Map();
  const subscriptions = new Map();
  const detachCallbacks = [];

  let closed = false;
  let requestId = 0;
  let clientSeq = 0;
  let status = {
    phase: 'idle',
    sessionId,
    transportName: resolveTransportName(transport),
    installedFake: Boolean(options.installedFake),
    lastMessageAt: null
  };

  if (typeof options.diagnostics === 'function') {
    diagnosticsListeners.add(options.diagnostics);
  }

  attachMessageSource(transport, handleMessage, detachCallbacks);
  if (transport?.windowBacked !== true) {
    attachMessageSource(root, handleMessage, detachCallbacks);
  }

  return {
    capabilities: CLIENT_CAPABILITIES,
    getStatus: () => status,
    onDiagnostics(listener) {
      diagnosticsListeners.add(listener);
      return () => diagnosticsListeners.delete(listener);
    },
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
      sdkVersion: SDK_VERSION
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
    const response = await request('command', commandOptions.topic ?? 'orders', {
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

      pendingRequests.set(nextRequestId, { kind, topic, resolve, reject, timeout });

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

    if (root.CefSharp?.PostMessage) {
      root.CefSharp.PostMessage(envelope);
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

    updateStatus({ lastMessageAt: Date.now() });

    if (incoming.kind === 'binaryChunk') {
      dispatchDataEvent(incoming);
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
      delivered,
      binaryBytes: event.binaryBytes ?? 0
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
    for (const listener of diagnosticsListeners) {
      listener({ level, type, message, detail, at: Date.now() });
    }
  }

  function assertNotClosed() {
    if (closed) {
      throw new Error('DataPlane client is closed.');
    }
  }
}

export function decodeBinaryChunk(chunk) {
  const base64 = chunk?.data ?? chunk?.base64 ?? chunk?.payload ?? '';
  if (!base64) {
    return new Uint8Array();
  }

  const binary = globalThis.atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}

export function decodeEntityColumnarPacket(bytes) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (view.byteLength < 20 || view.getUint32(0, true) !== 0x5044574c) {
    throw new Error('Invalid Ludots entity columnar packet.');
  }

  const kind = view.getUint8(6) === 1 ? 'snapshot' : 'delta';
  const schemaId = view.getInt32(8, true);
  const rowCount = view.getInt32(12, true);
  const removedCount = view.getInt32(16, true);
  const removedStableIds = new Int32Array(removedCount);
  const stableIds = new Int32Array(rowCount);
  const generations = new Int32Array(rowCount);
  const x = new Float32Array(rowCount);
  const y = new Float32Array(rowCount);
  const hp = new Uint16Array(rowCount);
  const team = new Uint8Array(rowCount);
  const state = new Uint8Array(rowCount);
  let offset = 20;

  for (let index = 0; index < removedCount; index += 1) {
    removedStableIds[index] = view.getInt32(offset, true);
    offset += 4;
  }

  for (let index = 0; index < rowCount; index += 1) {
    stableIds[index] = view.getInt32(offset, true);
    generations[index] = view.getInt32(offset + 4, true);
    x[index] = view.getFloat32(offset + 8, true);
    y[index] = view.getFloat32(offset + 12, true);
    hp[index] = view.getUint16(offset + 16, true);
    team[index] = view.getUint8(offset + 18);
    state[index] = view.getUint8(offset + 19);
    offset += 20;
  }

  return { kind, schemaId, removedStableIds, stableIds, generations, x, y, hp, team, state };
}

export function createFakeLudotsDataPlaneTransport(options = {}) {
  const eventTarget = new EventTarget();
  const intervalMs = options.intervalMs ?? 1200;
  const latencyMs = options.latencyMs ?? 60;
  const sessionId = createId('fake-session');
  const subscriptions = new Map();
  const state = createInitialFakeWorldState();
  let timer = 0;

  return {
    name: 'window.ludotsDataplane.fake',
    postMessage(message) {
      const envelope = normalizeClientEnvelope(message);
      globalThis.setTimeout(() => handleClientEnvelope(envelope), latencyMs);
    },
    addEventListener(type, listener, listenerOptions) {
      eventTarget.addEventListener(type, listener, listenerOptions);
    },
    removeEventListener(type, listener, listenerOptions) {
      eventTarget.removeEventListener(type, listener, listenerOptions);
    },
    dispose() {
      stopStream();
      subscriptions.clear();
    }
  };

  function handleClientEnvelope(envelope) {
    if (!envelope || envelope.schemaVersion !== SCHEMA_VERSION) {
      return;
    }

    if (envelope.kind === 'handshake') {
      emitHostControl('handshakeAck', envelope, {
        sessionId,
        transportName: 'fake',
        capabilities: { supportsBinary: false, supportsReliableOrdered: true, supportsLatestWins: true }
      });
      return;
    }

    if (envelope.kind === 'subscribe') {
      const subscriptionId = envelope.payload?.subscriptionId ?? createId('sub');
      subscriptions.set(subscriptionId, { topic: envelope.topic, id: subscriptionId });
      emitHostData('Snapshot', envelope, snapshotPayload(state));
      startStream();
      return;
    }

    if (envelope.kind === 'unsubscribe') {
      subscriptions.delete(envelope.payload?.subscriptionId);
      emitHostControl('unsubscribed', envelope, { topic: envelope.topic });
      if (subscriptions.size === 0) {
        stopStream();
      }
      return;
    }

    if (envelope.kind === 'command') {
      applyFakeCommand(state, envelope.payload);
      emitHostControl('commandAck', envelope, { clientSeq: envelope.payload?.clientSeq ?? 0 });
      emitDelta({ reason: envelope.payload?.name ?? 'command' });
    }
  }

  function startStream() {
    if (timer !== 0) {
      return;
    }
    timer = globalThis.setInterval(() => emitDelta({ reason: 'simulation' }), intervalMs);
  }

  function stopStream() {
    if (timer === 0) {
      return;
    }
    globalThis.clearInterval(timer);
    timer = 0;
  }

  function emitDelta(extra) {
    state.tick += 1;
    advanceFakeWorld(state);
    for (const subscription of subscriptions.values()) {
      emitHostData('Delta', {
        sessionId,
        requestId: 0,
        topic: subscription.topic
      }, deltaPayload(state, extra));
    }
  }

  function emitHostControl(kind, request, payload) {
    emit({
      schemaVersion: SCHEMA_VERSION,
      sessionId: request.sessionId ?? sessionId,
      requestId: request.requestId ?? 0,
      kind: 'Control',
      topic: request.topic ?? 'system',
      delivery: 'ReliableOrdered',
      contentType: 'application/json+ludots-dataplane-control',
      payload: {
        schemaVersion: SCHEMA_VERSION,
        sessionId: request.sessionId ?? sessionId,
        requestId: request.requestId ?? 0,
        kind,
        topic: request.topic ?? 'system',
        payload
      }
    });
  }

  function emitHostData(packetKind, request, payload) {
    emit({
      schemaVersion: SCHEMA_VERSION,
      sessionId: request.sessionId ?? sessionId,
      requestId: request.requestId ?? 0,
      kind: packetKind,
      topic: request.topic ?? DEFAULT_TOPIC,
      delivery: 'LatestWins',
      contentType: 'application/json',
      payload
    });
  }

  function emit(data) {
    eventTarget.dispatchEvent(new MessageEvent('message', { data }));
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

  if (data.channel === BINARY_CHUNK_CHANNEL) {
    const chunk = typeof data.payload === 'string' ? parseJsonOrNull(data.payload) : data.payload;
    return normalizeBinaryChunk(chunk);
  }

  return normalizeHostEnvelope(data);
}

function normalizeHostEnvelope(envelope) {
  if (!envelope || typeof envelope !== 'object') {
    return null;
  }

  if (envelope.schemaVersion !== SCHEMA_VERSION) {
    return null;
  }

  const packetKind = normalizeKind(envelope.kind);
  if (envelope.payload?.schemaVersion === SCHEMA_VERSION) {
    return {
      schemaVersion: SCHEMA_VERSION,
      sessionId: envelope.payload.sessionId ?? envelope.sessionId,
      requestId: envelope.payload.requestId ?? envelope.requestId,
      kind: envelope.payload.kind,
      topic: envelope.payload.topic ?? envelope.topic,
      payload: envelope.payload.payload ?? {},
      packetKind,
      delivery: envelope.delivery,
      contentType: envelope.contentType
    };
  }

  return {
    schemaVersion: SCHEMA_VERSION,
    sessionId: envelope.sessionId,
    requestId: envelope.requestId,
    kind: packetKind,
    topic: envelope.topic,
    payload: envelope.payload ?? {},
    packetKind,
    delivery: envelope.delivery,
    contentType: envelope.contentType
  };
}

function normalizeBinaryChunk(chunk) {
  if (!chunk || typeof chunk !== 'object') {
    return null;
  }

  const bytes = decodeBinaryChunk(chunk);
  return {
    schemaVersion: SCHEMA_VERSION,
    sessionId: chunk.sessionId,
    requestId: chunk.requestId ?? 0,
    kind: 'binaryChunk',
    packetKind: normalizeKind(chunk.kind),
    topic: chunk.topic,
    payload: chunk,
    binaryBytes: bytes.byteLength,
    bytes
  };
}

function normalizeClientEnvelope(message) {
  if (typeof message === 'string') {
    return parseJsonOrNull(message);
  }
  return message;
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

function createInitialFakeWorldState() {
  return {
    tick: 1,
    selectedEntityId: 'unit.scout',
    entities: [
      entity('unit.scout', 'Scout', 94, 4, 2, 'Hold'),
      entity('unit.guard', 'Guard', 88, 7, 6, 'Patrol'),
      entity('unit.engineer', 'Engineer', 76, 2, 8, 'Survey')
    ]
  };
}

function entity(id, label, hp, x, y, order) {
  return {
    id,
    label,
    hp,
    order,
    position: { x, y },
    destination: { x, y },
    signal: 0.75
  };
}

function snapshotPayload(state) {
  return {
    tick: state.tick,
    selectedEntityId: state.selectedEntityId,
    entityCount: state.entities.length,
    entities: state.entities.map(copyEntity),
    diagnostics: fakeDiagnostics(state, 'snapshot')
  };
}

function deltaPayload(state, extra) {
  return {
    tick: state.tick,
    selectedEntityId: state.selectedEntityId,
    entityCount: state.entities.length,
    entityPatches: state.entities.map(copyEntity),
    diagnostics: fakeDiagnostics(state, extra.reason),
    reason: extra.reason
  };
}

function applyFakeCommand(state, command) {
  const payload = command?.payload ?? {};
  if (command?.name === 'inspectEntity') {
    state.selectedEntityId = payload.nodeId ?? state.selectedEntityId;
  }

  if (command?.name === 'issueMoveOrder') {
    state.selectedEntityId = payload.nodeId ?? state.selectedEntityId;
    const entity = state.entities[0];
    entity.order = 'Move';
    entity.destination = payload.target ?? entity.destination;
  }
}

function advanceFakeWorld(state) {
  for (const entity of state.entities) {
    entity.signal = clampNumber(entity.signal + (Math.random() - 0.5) * 0.08, 0.35, 0.98);
  }
}

function fakeDiagnostics(state, reason) {
  return {
    reason,
    hostFps: 60,
    entityCount: state.entities.length,
    coalescedPackets: Math.max(0, state.tick - 1),
    droppedPackets: 0
  };
}

function copyEntity(entity) {
  return {
    ...entity,
    position: { ...entity.position },
    destination: { ...entity.destination }
  };
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

function clampNumber(value, min, max) {
  return Math.max(min, Math.min(max, Number(value.toFixed(2))));
}

export const DATA_PLANE_DEFAULT_TOPIC = DEFAULT_TOPIC;
