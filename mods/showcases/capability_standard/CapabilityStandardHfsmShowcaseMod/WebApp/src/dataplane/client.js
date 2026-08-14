const SCHEMA_VERSION = 1;
const DEFAULT_REQUEST_TIMEOUT_MS = 12000;
const DEFAULT_TRANSPORT_WAIT_TIMEOUT_MS = 5000;
const DEFAULT_TRANSPORT_WAIT_POLL_MS = 50;
const CONTROL_CHANNEL = 'ludots.dataplane.control';
export const HFSM_GRAPH_DEBUG_TOPIC = 'ludots.showcase.capability_standard.hfsm.graph_debug';

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

  if (options.allowMock === true) {
    const transport = createMockTransport(root, options.previewGraphDebug);
    root.ludotsDataplane = transport;
    return { transport, hostBacked: false };
  }

  throw new Error('window.ludotsDataplane is required for the HFSM graph editor/debug view.');
}

export function waitForLudotsDataPlaneTransport(options = {}) {
  const root = options.root ?? globalThis;
  const timeoutMs = options.timeoutMs ?? DEFAULT_TRANSPORT_WAIT_TIMEOUT_MS;
  const pollMs = options.pollMs ?? DEFAULT_TRANSPORT_WAIT_POLL_MS;
  const signal = options.signal;
  const startedAt = Date.now();

  return new Promise((resolve, reject) => {
    let timer = 0;

    const clearTimer = () => {
      if (timer !== 0) {
        root.clearTimeout?.(timer);
        timer = 0;
      }
    };

    const fail = (error) => {
      clearTimer();
      signal?.removeEventListener?.('abort', abort);
      reject(error);
    };

    const succeed = (resolved) => {
      clearTimer();
      signal?.removeEventListener?.('abort', abort);
      resolve(resolved);
    };

    const abort = () => {
      fail(new Error('HFSM graph editor/debug transport wait was cancelled.'));
    };

    const poll = () => {
      if (signal?.aborted) {
        abort();
        return;
      }

      try {
        succeed(ensureLudotsDataPlaneTransport(options));
        return;
      } catch (error) {
        if (Date.now() - startedAt >= timeoutMs) {
          fail(error);
          return;
        }

        timer = root.setTimeout?.(poll, pollMs) ?? 0;
        if (timer === 0) {
          fail(error);
        }
      }
    };

    signal?.addEventListener?.('abort', abort, { once: true });
    poll();
  });
}

export function createLudotsDataPlaneClient(options = {}) {
  const root = options.root ?? globalThis;
  const transport = options.transport ?? root.ludotsDataplane;
  const requestTimeoutMs = options.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;
  const sessionId = options.sessionId ?? createId('hfsm-graph-debug');
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
      sdkVersion: '0.2.0-hfsm-graph-debug'
    });
    updateStatus({
      phase: 'connected',
      sessionId: response.sessionId ?? response.payload?.sessionId ?? sessionId,
      transportName: response.payload?.transportName ?? response.payload?.transportMode ?? status.transportName
    });
    emitDiagnostic('info', 'handshake', 'DataPlane handshake acknowledged.', response);
    return response;
  }

  async function subscribe(topic = HFSM_GRAPH_DEBUG_TOPIC, handler, subscribeOptions = {}) {
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
    const response = await request('command', commandOptions.topic ?? HFSM_GRAPH_DEBUG_TOPIC, {
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
        if (!transport?.postMessage) {
          throw new Error('No Ludots DataPlane transport is available.');
        }

        transport.postMessage(envelope);
      } catch (error) {
        if (timeout) {
          root.clearTimeout?.(timeout);
        }

        pendingRequests.delete(nextRequestId);
        reject(error);
      }
    });
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
  if (envelope.payload?.schemaVersion === SCHEMA_VERSION && typeof envelope.payload.kind === 'string') {
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

function createMockTransport(root, graphDebug) {
  if (!graphDebug?.rootGraphId || !Array.isArray(graphDebug.nodes) || !Array.isArray(graphDebug.implementations)) {
    throw new Error('HFSM graph editor/debug mock mode requires graphDebug from showcase.json.');
  }

  const eventTarget = new EventTarget();
  const sessionId = createId('mock-hfsm');
  let subscriptions = new Set();
  let tick = 0;
  let timer = 0;
  let stateIndex = 0;
  let forcedDead = false;
  let selectedNodeId = 'GoDrink';
  let activeGraphId = graphDebug.rootGraphId;
  const states = ['GoDrink', 'Drinking', 'GoTrack', 'Running'];

  return {
    name: 'window.ludotsDataplane.mock',
    mode: 'mock',
    postMessage(message) {
      const envelope = typeof message === 'string' ? parseJsonOrNull(message) : message;
      root.setTimeout(() => handleEnvelope(envelope), 40);
    },
    addEventListener(type, listener, options) {
      eventTarget.addEventListener(type, listener, options);
    },
    removeEventListener(type, listener, options) {
      eventTarget.removeEventListener(type, listener, options);
    },
    dispose() {
      if (timer !== 0) {
        root.clearInterval(timer);
        timer = 0;
      }
      subscriptions.clear();
    }
  };

  function handleEnvelope(envelope) {
    if (!envelope || envelope.schemaVersion !== SCHEMA_VERSION) {
      return;
    }

    if (envelope.kind === 'handshake') {
      emitControl('handshakeAck', envelope, {
        sessionId,
        transportMode: 'mock',
        capabilities: {}
      });
      return;
    }

    if (envelope.kind === 'subscribe') {
      subscriptions.add(envelope.topic);
      emitData('Snapshot', envelope, createMockSnapshot());
      if (timer === 0) {
        timer = root.setInterval(() => {
          tick += 1;
          if (tick % 24 === 0) {
            stateIndex = (stateIndex + 1) % states.length;
          }
          for (const topic of subscriptions) {
            emitData('Delta', { ...envelope, topic, requestId: 0 }, createMockSnapshot());
          }
        }, 120);
      }
      return;
    }

    if (envelope.kind === 'command') {
      applyMockCommand(envelope.payload);
      emitControl('commandAck', envelope, { clientSeq: envelope.payload?.clientSeq ?? 0 });
      for (const topic of subscriptions) {
        emitData('Delta', { ...envelope, topic, requestId: 0 }, createMockSnapshot());
      }
    }
  }

  function applyMockCommand(payload) {
    const name = payload?.name;
    const commandPayload = payload?.payload ?? {};
    if (name === 'selectNode' && typeof commandPayload.nodeId === 'string') {
      selectedNodeId = commandPayload.nodeId;
      return;
    }

    if (name === 'openGraph' && typeof commandPayload.graphId === 'string') {
      activeGraphId = commandPayload.graphId;
      return;
    }

    if (name === 'killHero') {
      forcedDead = true;
      selectedNodeId = 'Dead';
      activeGraphId = graphDebug.rootGraphId;
      return;
    }

    if (name === 'makeThirsty') {
      forcedDead = false;
      stateIndex = 0;
      selectedNodeId = 'GoDrink';
      activeGraphId = graphDebug.rootGraphId;
      return;
    }

    if (name === 'resetStory') {
      tick = 0;
      stateIndex = 0;
      forcedDead = false;
      selectedNodeId = 'GoDrink';
      activeGraphId = graphDebug.rootGraphId;
    }
  }

  function emitControl(kind, request, payload) {
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

  function emitData(kind, request, payload) {
    emit({
      schemaVersion: SCHEMA_VERSION,
      sessionId: request.sessionId ?? sessionId,
      requestId: request.requestId ?? 0,
      kind,
      topic: request.topic ?? HFSM_GRAPH_DEBUG_TOPIC,
      delivery: 'LatestWins',
      contentType: 'application/json',
      payload
    });
  }

  function emit(data) {
    eventTarget.dispatchEvent(new MessageEvent('message', { data }));
  }

  function createMockSnapshot() {
    const stateId = forcedDead ? 'Dead' : states[stateIndex];
    const activeImplementation = graphDebug.implementations.find((graph) => graph.ownerStateId === stateId) ?? null;
    const activeOpNodeIds = activeImplementation?.nodes?.length
      ? [activeImplementation.nodes[Math.abs(tick) % activeImplementation.nodes.length].id]
      : [];
    const water = forcedDead ? Math.max(0, 100 - tick) : stateId === 'GoDrink' ? 24 : Math.max(0, 100 - tick);
    const statePath = resolveMockStatePath(stateId);

    return {
      schemaVersion: 1,
      revision: tick,
      mode: 'hfsm-editor-debug',
      selectedEntity: { instanceId: 'hfsm-hero', name: 'HFSM Runner' },
      runtime: {
        isActive: true,
        stateId,
        stateLabel: stateId,
        statePath,
        playerStory: 'Mock preview stream. Real values come from Ludots CEF.',
        lastEvent: forcedDead ? 'Mock Any State fired: health reached zero.' : 'Preview mode only.',
        health: forcedDead ? 0 : 100,
        water,
        lapCount: Math.floor(tick / 30),
        transitionCount: stateIndex,
        heroXCm: tick * 10,
        heroYCm: -260,
        dead: forcedDead
      },
      rootGraph: {
        id: graphDebug.rootGraphId,
        title: graphDebug.rootTitle,
        kind: 'hfsm',
        ownerStateId: '',
        summary: 'Previewing the same HFSM graph that the CEF runtime streams.',
        nodes: graphDebug.nodes,
        edges: graphDebug.edges ?? []
      },
      implementations: graphDebug.implementations.map((graph) => ({
        id: graph.id,
        title: graph.title,
        kind: 'implementation',
        ownerStateId: graph.ownerStateId,
        summary: graph.summary,
        nodes: graph.nodes ?? [],
        edges: graph.edges ?? []
      })),
      activeGraphId,
      selectedNodeId,
      activeStateId: stateId,
      activeStatePathIds: resolveMockStatePathIds(stateId),
      activeImplementationGraphId: activeImplementation?.id ?? '',
      activeOpNodeIds,
      command: { lastCommand: 'none', lastStatus: 'mock' }
    };
  }

  function resolveMockStatePath(stateId) {
    if (stateId === 'Dead') {
      return 'Dead';
    }

    return stateId === 'Running' || stateId === 'GoTrack'
      ? `Alive > Exercise > ${stateId}`
      : `Alive > Hydrate > ${stateId}`;
  }

  function resolveMockStatePathIds(stateId) {
    if (stateId === 'Dead') {
      return ['Dead'];
    }

    return stateId === 'Running' || stateId === 'GoTrack'
      ? ['Alive', 'Exercise', stateId]
      : ['Alive', 'Hydrate', stateId];
  }
}
