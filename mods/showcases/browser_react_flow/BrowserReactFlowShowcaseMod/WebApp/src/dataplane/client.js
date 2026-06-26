const SCHEMA_VERSION = 1;
const CONTROL_CHANNEL = 'ludots.dataplane.control';
const BINARY_CHUNK_CHANNEL = 'ludots.dataplane.binaryChunk';
const SHARED_BUFFER_CHANNEL = 'ludots.dataplane.sharedBuffer';
const SDK_VERSION = '0.2.0-showcase';
const DEFAULT_REQUEST_TIMEOUT_MS = 4000;
const DEFAULT_TOPIC = 'ludots.showcase.browserReactFlow.world';

const CLIENT_CAPABILITIES = Object.freeze([
  'handshake',
  'subscribe',
  'unsubscribe',
  'command',
  'binary.base64',
  'shared-memory',
  'shared-buffer-descriptor',
  'entity-columnar.v1',
  'diagnostics'
]);

export function ensureLudotsDataPlaneTransport(options = {}) {
  const root = options.root ?? globalThis;
  const mode = options.mode ?? 'standard';

  if (root.ludotsDataplane && mode !== 'mock') {
    return { transport: root.ludotsDataplane, installedFake: false, mode: 'standard' };
  }

  if (mode !== 'mock') {
    throw new Error('window.ludotsDataplane is required outside explicit mock preview mode.');
  }

  const mockTransport = createMockLudotsDataPlaneTransport({
    intervalMs: options.intervalMs,
    latencyMs: options.latencyMs
  });
  root.ludotsDataplane = mockTransport;
  return { transport: mockTransport, installedFake: true, mode: 'mock' };
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
      sessionId: response.payload?.sessionId ?? response.sessionId ?? sessionId,
      transportName: response.payload?.transportName ??
        response.payload?.transportMode ??
        response.transportName ??
        status.transportName
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
    if (!transport || typeof transport.postMessage !== 'function') {
      throw new Error('No Ludots DataPlane transport is available.');
    }

    transport.postMessage(envelope);
  }

  function handleMessage(rawMessage) {
    if (closed) {
      return;
    }

    const incoming = normalizeIncomingMessage(rawMessage);
    if (!incoming) {
      return;
    }

    if (incoming.kind === 'sharedBuffer') {
      readSharedBufferEvent(incoming)
        .then((resolved) => {
          if (!closed) {
            resolvePendingRequest(resolved);
            dispatchDataEvent(resolved);
          }
        })
        .catch((error) => {
          emitDiagnostic('error', 'shared-buffer-read', error instanceof Error ? error.message : String(error), incoming);
        });
      updateStatus({ lastMessageAt: Date.now() });
      return;
    }

    updateStatus({ lastMessageAt: Date.now() });

    if (incoming.kind === 'binaryChunk') {
      dispatchDataEvent(incoming);
      return;
    }

    resolvePendingRequest(incoming);

    if (incoming.kind === 'snapshot' || incoming.kind === 'delta' || incoming.kind === 'diagnostics') {
      dispatchDataEvent(incoming);
    }
  }

  function resolvePendingRequest(incoming) {
    const pending = incoming.requestId ? pendingRequests.get(incoming.requestId) : null;
    if (!pending) {
      return;
    }

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

  async function readSharedBufferEvent(event) {
    if (!transport || typeof transport.readSharedBuffer !== 'function') {
      throw new Error('Shared-buffer descriptor received but transport cannot read shared buffers.');
    }

    const descriptor = normalizeSharedBufferDescriptor(event.sharedBuffer);
    const bytes = await transport.readSharedBuffer(descriptor);
    const byteView = normalizeByteView(bytes);
    return {
      ...event,
      kind: event.packetKind,
      payload: event.payload ?? {},
      sharedBuffer: descriptor,
      binaryBytes: byteView.byteLength,
      bytes: byteView,
      binaryChunks: [{
        byteLength: byteView.byteLength,
        descriptor,
        transport: 'shared-memory'
      }]
    };
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

  const version = view.getUint16(4, true);
  if (view.byteLength >= 32 && version === 1 && view.getUint8(6) === 1) {
    const frameKind = view.getUint8(7);
    if (frameKind === 1) {
      return decodeEntitySoaSnapshot(bytes, view);
    }

    if (frameKind === 2) {
      return decodeEntitySoaFullDelta(bytes, view);
    }
  }

  const kindByte = view.getUint8(6);
  const kind = kindByte === 1 ? 'snapshot' : kindByte === 3 ? 'indexedDelta' : 'delta';
  const schemaId = view.getInt32(8, true);
  const rowCount = view.getInt32(12, true);
  const removedCount = view.getInt32(16, true);
  if (kind === 'indexedDelta') {
    const indices = new Int32Array(rowCount);
    const generations = new Int32Array(rowCount);
    const x = new Float32Array(rowCount);
    const y = new Float32Array(rowCount);
    const hp = new Uint16Array(rowCount);
    const state = new Uint8Array(rowCount);
    let offset = 20;
    for (let index = 0; index < rowCount; index += 1) {
      indices[index] = view.getInt32(offset, true);
      generations[index] = view.getInt32(offset + 4, true);
      x[index] = view.getFloat32(offset + 8, true);
      y[index] = view.getFloat32(offset + 12, true);
      hp[index] = view.getUint16(offset + 16, true);
      state[index] = view.getUint8(offset + 18);
      offset += 19;
    }

    return { kind, schemaId, indices, generations, x, y, hp, state };
  }

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

function decodeEntitySoaSnapshot(bytes, view) {
  const schemaId = view.getInt32(8, true);
  const rowCount = view.getInt32(12, true);
  const sequence = readInt64AsNumber(view, 16);
  const tick = readInt64AsNumber(view, 24);
  let offset = 32;
  const stableIds = new Int32Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 4;
  const generations = new Int32Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 4;
  const x = new Float32Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 4;
  const y = new Float32Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 4;
  const hp = new Uint16Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 2;
  const team = new Uint8Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount;
  const state = new Uint8Array(bytes.buffer, bytes.byteOffset + offset, rowCount);

  return {
    kind: 'soaSnapshot',
    schemaId,
    sequence,
    tick,
    stableIds,
    generations,
    x,
    y,
    hp,
    team,
    state
  };
}

function decodeEntitySoaFullDelta(bytes, view) {
  const schemaId = view.getInt32(8, true);
  const rowCount = view.getInt32(12, true);
  const sequence = readInt64AsNumber(view, 16);
  const tick = readInt64AsNumber(view, 24);
  let offset = 32;
  const generations = new Int32Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 4;
  const x = new Float32Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 4;
  const y = new Float32Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 4;
  const hp = new Uint16Array(bytes.buffer, bytes.byteOffset + offset, rowCount);
  offset += rowCount * 2;
  const state = new Uint8Array(bytes.buffer, bytes.byteOffset + offset, rowCount);

  return {
    kind: 'soaFullDelta',
    schemaId,
    sequence,
    tick,
    generations,
    x,
    y,
    hp,
    state
  };
}

function readInt64AsNumber(view, offset) {
  if (typeof view.getBigInt64 === 'function') {
    return Number(view.getBigInt64(offset, true));
  }

  const low = view.getUint32(offset, true);
  const high = view.getInt32(offset + 4, true);
  return high * 4294967296 + low;
}

export function createEntitySoAStore(rowCount) {
  if (!Number.isInteger(rowCount) || rowCount < 0) {
    throw new TypeError('Entity row count must be a non-negative integer.');
  }

  return {
    kind: 'snapshot',
    schemaId: 0,
    stableIds: new Int32Array(rowCount),
    generations: new Int32Array(rowCount),
    x: new Float32Array(rowCount),
    y: new Float32Array(rowCount),
    hp: new Uint16Array(rowCount),
    team: new Uint8Array(rowCount),
    state: new Uint8Array(rowCount),
    version: 0,
    sequence: 0,
    tick: 0,
    lastDeltaRows: 0
  };
}

export function applyEntityColumnarPacket(store, packet) {
  if (packet.kind === 'soaSnapshot') {
    const target = store ?? createEntitySoAStore(0);
    target.kind = 'snapshot';
    target.schemaId = packet.schemaId;
    target.stableIds = packet.stableIds;
    target.generations = packet.generations;
    target.x = packet.x;
    target.y = packet.y;
    target.hp = packet.hp;
    target.team = packet.team;
    target.state = packet.state;
    target.version += 1;
    target.sequence = packet.sequence ?? target.version;
    target.tick = packet.tick ?? target.tick ?? 0;
    target.lastDeltaRows = packet.stableIds.length;
    return target;
  }

  if (packet.kind === 'snapshot') {
    const target = store && store.stableIds?.length === packet.stableIds.length
      ? store
      : createEntitySoAStore(packet.stableIds.length);
    target.kind = 'snapshot';
    target.schemaId = packet.schemaId;
    target.stableIds.set(packet.stableIds);
    target.generations.set(packet.generations);
    target.x.set(packet.x);
    target.y.set(packet.y);
    target.hp.set(packet.hp);
    target.team.set(packet.team);
    target.state.set(packet.state);
    target.version += 1;
    target.sequence = packet.sequence ?? target.version;
    target.tick = packet.tick ?? target.tick ?? 0;
    target.lastDeltaRows = packet.stableIds.length;
    return target;
  }

  if (!store) {
    throw new Error('Entity snapshot is required before applying a delta packet.');
  }

  if (packet.kind === 'soaFullDelta') {
    if (store.stableIds.length !== packet.generations.length) {
      throw new Error('SoA full delta row count does not match the current entity snapshot.');
    }

    store.kind = 'snapshot';
    store.schemaId = packet.schemaId;
    store.generations = packet.generations;
    store.x = packet.x;
    store.y = packet.y;
    store.hp = packet.hp;
    store.state = packet.state;
    store.version += 1;
    store.sequence = packet.sequence ?? store.version;
    store.tick = packet.tick ?? store.tick ?? 0;
    store.lastDeltaRows = packet.generations.length;
    return store;
  }

  if (packet.kind === 'indexedDelta') {
    for (let row = 0; row < packet.indices.length; row += 1) {
      const index = packet.indices[row];
      if (index < 0 || index >= store.stableIds.length) {
        continue;
      }

      store.generations[index] = packet.generations[row];
      store.x[index] = packet.x[row];
      store.y[index] = packet.y[row];
      store.hp[index] = packet.hp[row];
      store.state[index] = packet.state[row];
    }

    store.kind = 'snapshot';
    store.schemaId = packet.schemaId;
    store.version += 1;
    store.lastDeltaRows = packet.indices.length;
    return store;
  }

  if (packet.kind === 'delta') {
    const firstStableId = store.stableIds[0] ?? 0;
    for (let row = 0; row < packet.stableIds.length; row += 1) {
      const index = packet.stableIds[row] - firstStableId;
      if (index < 0 || index >= store.stableIds.length) {
        continue;
      }

      store.generations[index] = packet.generations[row];
      store.x[index] = packet.x[row];
      store.y[index] = packet.y[row];
      store.hp[index] = packet.hp[row];
      store.team[index] = packet.team[row];
      store.state[index] = packet.state[row];
    }

    store.version += 1;
    store.lastDeltaRows = packet.stableIds.length;
    return store;
  }

  return store;
}

export function createEntityAttributeView(decoded, options = {}) {
  const stableIds = decoded?.stableIds;
  if (!stableIds || typeof stableIds.length !== 'number') {
    throw new TypeError('Decoded entity columnar packet is required.');
  }

  const entityCount = stableIds.length;
  const visibleCount = clampInteger(options.visibleCount ?? 32, 0, 256);
  const maxVisibleStart = Math.max(0, entityCount - visibleCount);
  const visibleStart = clampInteger(options.visibleStart ?? 0, 0, maxVisibleStart);
  const bucketCount = clampInteger(options.bucketCount ?? 64, 1, 512);
  const reuse = options.reuse;
  const scratch = reuse?.scratch && reuse.buckets?.length === bucketCount
    ? reuse.scratch
    : createEntityAttributeViewScratch(bucketCount);
  const bucketHp = scratch.bucketHp;
  const bucketCounts = scratch.bucketCounts;
  const bucketActive = scratch.bucketActive;
  const teamCounts = scratch.teamCounts;
  bucketHp.fill(0);
  bucketCounts.fill(0);
  bucketActive.fill(0);
  teamCounts.fill(0);
  const visibleEnd = Math.min(entityCount, visibleStart + visibleCount);
  const visibleRows = reuse?.visibleRows ?? [];
  if (visibleRows.length > visibleCount) {
    visibleRows.length = visibleCount;
  }
  let totalHp = 0;
  let minHp = entityCount > 0 ? Number.POSITIVE_INFINITY : 0;
  let maxHp = 0;
  let activeRows = 0;
  let damagedRows = 0;

  for (let index = 0; index < entityCount; index += 1) {
    const hp = decoded.hp[index];
    const state = decoded.state[index];
    const bucketIndex = Math.min(bucketCount - 1, Math.floor((index * bucketCount) / entityCount));
    bucketHp[bucketIndex] += hp;
    bucketCounts[bucketIndex] += 1;
    totalHp += hp;

    if (hp < minHp) {
      minHp = hp;
    }

    if (hp > maxHp) {
      maxHp = hp;
    }

    if (hp < 100) {
      damagedRows += 1;
    }

    if (state !== 0) {
      activeRows += 1;
      bucketActive[bucketIndex] += 1;
    }

    teamCounts[decoded.team[index]] += 1;
  }

  for (let index = visibleStart; index < visibleEnd; index += 1) {
    const stableId = stableIds[index];
    const visibleIndex = index - visibleStart;
    const row = visibleRows[visibleIndex] ?? {};
    row.id = `entity.${stableId}`;
    row.stableId = stableId;
    row.generation = decoded.generations[index];
    row.hp = decoded.hp[index];
    row.team = decoded.team[index];
    row.state = decoded.state[index];
    row.x = decoded.x[index];
    row.y = decoded.y[index];
    visibleRows[visibleIndex] = row;
  }

  if (visibleRows.length !== visibleEnd - visibleStart) {
    visibleRows.length = visibleEnd - visibleStart;
  }

  const buckets = reuse?.buckets && reuse.buckets.length === bucketCount
    ? reuse.buckets
    : Array.from({ length: bucketCount }, (_, index) => ({
      index,
      count: 0,
      avgHp: 0,
      activeRows: 0
    }));
  for (let index = 0; index < bucketCount; index += 1) {
    const count = bucketCounts[index];
    const bucket = buckets[index];
    bucket.count = count;
    bucket.avgHp = count > 0 ? bucketHp[index] / count : 0;
    bucket.activeRows = bucketActive[index];
  }

  const summary = reuse?.summary ?? {};
  summary.avgHp = entityCount > 0 ? totalHp / entityCount : 0;
  summary.minHp = minHp;
  summary.maxHp = maxHp;
  summary.activeRows = activeRows;
  summary.damagedRows = damagedRows;
  summary.teamCounts = teamCounts;

  if (reuse && reuse.scratch === scratch) {
    reuse.entityCount = entityCount;
    reuse.visibleStart = visibleStart;
    reuse.visibleCount = visibleRows.length;
    reuse.visibleRows = visibleRows;
    reuse.buckets = buckets;
    reuse.summary = summary;
    return reuse;
  }

  return {
    entityCount,
    visibleStart,
    visibleCount: visibleRows.length,
    visibleRows,
    buckets,
    summary,
    scratch
  };
}

function createEntityAttributeViewScratch(bucketCount) {
  return {
    bucketHp: new Float64Array(bucketCount),
    bucketCounts: new Uint32Array(bucketCount),
    bucketActive: new Uint32Array(bucketCount),
    teamCounts: new Uint32Array(256)
  };
}

export function createMockLudotsDataPlaneTransport(options = {}) {
  const eventTarget = new EventTarget();
  const intervalMs = options.intervalMs ?? 1200;
  const latencyMs = options.latencyMs ?? 60;
  const sessionId = createId('mock-session');
  const subscriptions = new Map();
  const state = createInitialMockWorldState();
  let timer = 0;

  return {
    name: 'window.ludotsDataplane.mock',
    mode: 'mock',
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
        transportName: 'mock',
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
      applyMockCommand(state, envelope.payload);
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
    advanceMockWorld(state);
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

  if (data.channel === SHARED_BUFFER_CHANNEL) {
    const rawDescriptorBytes = typeof data.payload === 'string'
      ? data.payload.length
      : JSON.stringify(data.payload ?? {}).length;
    const packet = typeof data.payload === 'string' ? parseJsonOrNull(data.payload) : data.payload;
    const normalized = normalizeSharedBufferPacket(packet);
    return normalized ? { ...normalized, rawDescriptorBytes } : null;
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
    binaryChunks: [],
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
    bytes,
    binaryChunks: [{
      byteLength: bytes.byteLength,
      byteOffset: chunk.byteOffset ?? 0,
      totalChunks: chunk.totalChunks ?? 1,
      transport: 'base64'
    }]
  };
}

function normalizeSharedBufferPacket(packet) {
  if (!packet || typeof packet !== 'object') {
    return null;
  }

  const sharedBuffer = normalizeSharedBufferDescriptor(packet.payload?.sharedBuffer ?? packet.sharedBuffer);
  return {
    schemaVersion: SCHEMA_VERSION,
    sessionId: packet.sessionId,
    requestId: packet.requestId ?? 0,
    kind: 'sharedBuffer',
    packetKind: normalizeKind(packet.kind),
    topic: packet.topic,
    payload: packet.payload ?? {},
    delivery: packet.delivery,
    contentType: packet.contentType,
    clientSeq: packet.clientSeq ?? 0,
    sharedBuffer
  };
}

export function normalizeSharedBufferDescriptor(descriptor) {
  if (!descriptor || typeof descriptor !== 'object') {
    throw new TypeError('Shared-buffer descriptor is required.');
  }

  const normalized = {
    bufferId: String(descriptor.bufferId ?? descriptor.BufferId ?? ''),
    topic: descriptor.topic ?? descriptor.Topic ?? '',
    schemaId: Number(descriptor.schemaId ?? descriptor.SchemaId ?? 0),
    layout: descriptor.layout ?? descriptor.Layout ?? 'ring-buffer',
    capacityBytes: Number(descriptor.capacityBytes ?? descriptor.CapacityBytes ?? 0),
    headerBytes: Number(descriptor.headerBytes ?? descriptor.HeaderBytes ?? 0),
    byteOffset: Number(descriptor.byteOffset ?? descriptor.ByteOffset ?? 0),
    byteLength: Number(descriptor.byteLength ?? descriptor.ByteLength ?? 0),
    sequence: Number(descriptor.sequence ?? descriptor.Sequence ?? 0),
    tick: Number(descriptor.tick ?? descriptor.Tick ?? 0),
    droppedPackets: Number(descriptor.droppedPackets ?? descriptor.DroppedPackets ?? 0),
    coalescedPackets: Number(descriptor.coalescedPackets ?? descriptor.CoalescedPackets ?? 0)
  };

  if (!normalized.bufferId) {
    throw new Error('Shared-buffer descriptor is missing bufferId.');
  }

  if (normalized.byteOffset < 0 || normalized.byteLength < 0 || normalized.sequence <= 0) {
    throw new Error('Shared-buffer descriptor has an invalid byte range or sequence.');
  }

  return normalized;
}

function normalizeByteView(bytes) {
  if (bytes instanceof Uint8Array) {
    return bytes;
  }

  if (bytes instanceof ArrayBuffer) {
    return new Uint8Array(bytes);
  }

  if (Array.isArray(bytes)) {
    return Uint8Array.from(bytes);
  }

  if (bytes && typeof bytes.length === 'number') {
    return Uint8Array.from(bytes);
  }

  throw new TypeError('Shared-buffer reader did not return bytes.');
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

function createInitialMockWorldState() {
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
    diagnostics: mockDiagnostics(state, 'snapshot')
  };
}

function deltaPayload(state, extra) {
  return {
    tick: state.tick,
    selectedEntityId: state.selectedEntityId,
    entityCount: state.entities.length,
    entityPatches: state.entities.map(copyEntity),
    diagnostics: mockDiagnostics(state, extra.reason),
    reason: extra.reason
  };
}

function applyMockCommand(state, command) {
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

function advanceMockWorld(state) {
  for (const entity of state.entities) {
    entity.signal = clampNumber(entity.signal + (Math.random() - 0.5) * 0.08, 0.35, 0.98);
  }
}

function mockDiagnostics(state, reason) {
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

function clampInteger(value, min, max) {
  const number = Number.isFinite(value) ? Math.trunc(value) : min;
  return Math.max(min, Math.min(max, number));
}

export const DATA_PLANE_DEFAULT_TOPIC = DEFAULT_TOPIC;
