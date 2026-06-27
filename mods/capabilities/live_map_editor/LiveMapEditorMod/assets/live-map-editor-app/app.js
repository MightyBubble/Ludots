const SCHEMA_VERSION = 1;
const STATE_TOPIC = 'ludots.liveMapEditor.state';
const COMMAND_TOPIC = 'ludots.liveMapEditor.commands';
const SESSION_ID = `live-map-editor-web-${Math.random().toString(16).slice(2)}`;

let requestId = 0;
let clientSeq = 0;
let latestState = null;
const pending = new Map();
const subscriptions = new Map();

const el = {
  transport: document.querySelector('#transport-status'),
  saveStatus: document.querySelector('#save-status'),
  save: document.querySelector('#save-map'),
  mapId: document.querySelector('#map-id'),
  mapSummary: document.querySelector('#map-summary'),
  navSummary: document.querySelector('#nav-summary'),
  transportSummary: document.querySelector('#transport-summary'),
  simSummary: document.querySelector('#sim-summary'),
  pickStatus: document.querySelector('#pick-status'),
  entityStatus: document.querySelector('#entity-status'),
  navStatus: document.querySelector('#nav-status'),
  toolTabs: document.querySelector('#tool-tabs'),
  brushRadius: document.querySelector('#brush-radius'),
  brushHeight: document.querySelector('#brush-height'),
  brushArea: document.querySelector('#brush-area'),
  brushCost: document.querySelector('#brush-cost'),
  brushBlocked: document.querySelector('#brush-blocked'),
  brushWater: document.querySelector('#brush-water'),
  brushRamp: document.querySelector('#brush-ramp'),
  paintPicked: document.querySelector('#paint-picked'),
  template: document.querySelector('#entity-template'),
  placePicked: document.querySelector('#place-picked'),
  selectPicked: document.querySelector('#select-picked'),
  removeSelected: document.querySelector('#remove-selected'),
  rebake: document.querySelector('#rebake'),
  queryPath: document.querySelector('#query-path'),
  transportModeTabs: document.querySelector('#transport-mode-tabs'),
  transportSampleStep: document.querySelector('#transport-sample-step'),
  transportDefaultWidth: document.querySelector('#transport-default-width'),
  transportApplyRoot: document.querySelector('#transport-apply-root'),
  transportNodeId: document.querySelector('#transport-node-id'),
  transportNodeKind: document.querySelector('#transport-node-kind'),
  transportNodeTags: document.querySelector('#transport-node-tags'),
  transportAddNode: document.querySelector('#transport-add-node'),
  transportSelectNode: document.querySelector('#transport-select-node'),
  transportUpdateNode: document.querySelector('#transport-update-node'),
  transportMoveNode: document.querySelector('#transport-move-node'),
  transportDeleteNode: document.querySelector('#transport-delete-node'),
  transportSegmentId: document.querySelector('#transport-segment-id'),
  transportSegmentArea: document.querySelector('#transport-segment-area'),
  transportSegmentTags: document.querySelector('#transport-segment-tags'),
  transportSegmentDirection: document.querySelector('#transport-segment-direction'),
  transportSegmentFlow: document.querySelector('#transport-segment-flow'),
  transportSegmentDepth: document.querySelector('#transport-segment-depth'),
  transportSegmentWidth: document.querySelector('#transport-segment-width'),
  transportSegmentLanes: document.querySelector('#transport-segment-lanes'),
  transportSegmentVisual: document.querySelector('#transport-segment-visual'),
  transportSegmentSample: document.querySelector('#transport-segment-sample'),
  transportBeginSegment: document.querySelector('#transport-begin-segment'),
  transportAddPoint: document.querySelector('#transport-add-point'),
  transportAddNodePoint: document.querySelector('#transport-add-node-point'),
  transportUndoPoint: document.querySelector('#transport-undo-point'),
  transportCommitSegment: document.querySelector('#transport-commit-segment'),
  transportSelectSegment: document.querySelector('#transport-select-segment'),
  transportUpdateSegment: document.querySelector('#transport-update-segment'),
  transportInsertPoint: document.querySelector('#transport-insert-point'),
  transportMovePoint: document.querySelector('#transport-move-point'),
  transportDeletePoint: document.querySelector('#transport-delete-point'),
  transportDeleteSegment: document.querySelector('#transport-delete-segment'),
  transportRebake: document.querySelector('#transport-rebake'),
  transportSave: document.querySelector('#transport-save'),
  transportAgent: document.querySelector('#transport-agent'),
  transportQueryRoute: document.querySelector('#transport-query-route'),
  transportNodes: document.querySelector('#transport-nodes'),
  transportSegments: document.querySelector('#transport-segments')
};

boot().catch((error) => {
  el.transport.textContent = `transport error: ${error.message}`;
});

async function boot() {
  await waitForDataPlaneTransport();

  window.addEventListener('message', handleHostMessage);
  wireUi();
  await control('handshake', 'system', { capabilities: ['message', 'latest-wins', 'reliable-ordered'] });
  el.transport.textContent = 'connected';
  await control('subscribe', STATE_TOPIC, { snapshot: true });
}

async function waitForDataPlaneTransport(timeoutMs = 5000) {
  const startedAt = performance.now();
  while (performance.now() - startedAt <= timeoutMs) {
    if (window.ludotsDataplane && typeof window.ludotsDataplane.postMessage === 'function') {
      return window.ludotsDataplane;
    }

    el.transport.textContent = 'waiting for DataPlane';
    await delay(50);
  }

  throw new Error('window.ludotsDataplane did not initialize within 5000ms');
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function wireUi() {
  el.toolTabs.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-tool]');
    if (!button) return;
    sendCommand('setTool', { tool: button.dataset.tool });
  });

  for (const input of [
    el.brushRadius,
    el.brushHeight,
    el.brushArea,
    el.brushCost,
    el.brushBlocked,
    el.brushWater,
    el.brushRamp
  ]) {
    input.addEventListener('change', () => sendBrushCommand('setBrush'));
  }

  el.paintPicked.addEventListener('click', () => sendBrushCommand('paintTerrain'));
  el.placePicked.addEventListener('click', () => sendCommand('placeEntity', { template: el.template.value.trim() }));
  el.selectPicked.addEventListener('click', () => sendCommand('selectEntity', {}));
  el.removeSelected.addEventListener('click', () => sendCommand('removeEntity', {}, selectedEntityRefs()));
  el.rebake.addEventListener('click', () => sendCommand('rebakeDirty', { maxTiles: 16 }));
  el.queryPath.addEventListener('click', () => sendCommand('queryPath', {}));
  el.save.addEventListener('click', () => sendCommand('saveMap', {}));

  el.transportModeTabs.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-transport-mode]');
    if (!button) return;
    sendCommand('transportSetMode', { mode: button.dataset.transportMode });
  });

  el.transportApplyRoot.addEventListener('click', () => sendCommand('transportSetRoot', {
    sampleStepCm: readNumber(el.transportSampleStep, 'Transport sample step'),
    defaultVisualWidthMeters: readNumber(el.transportDefaultWidth, 'Transport visual width')
  }));
  el.transportAddNode.addEventListener('click', () => sendCommand('transportAddNode', readNodePayload()));
  el.transportSelectNode.addEventListener('click', () => sendCommand('transportSelectNode', {}));
  el.transportUpdateNode.addEventListener('click', () => sendCommand('transportUpdateNode', readNodePayload()));
  el.transportMoveNode.addEventListener('click', () => sendCommand('transportMoveNode', {}));
  el.transportDeleteNode.addEventListener('click', () => sendCommand('transportDeleteNode', {}));
  el.transportBeginSegment.addEventListener('click', () => sendCommand('transportBeginSegment', {}));
  el.transportAddPoint.addEventListener('click', () => sendCommand('transportAppendSegmentPoint', { snapToNode: false }));
  el.transportAddNodePoint.addEventListener('click', () => sendCommand('transportAppendSegmentPoint', { snapToNode: true }));
  el.transportUndoPoint.addEventListener('click', () => sendCommand('transportUndoSegmentPoint', {}));
  el.transportCommitSegment.addEventListener('click', () => sendCommand('transportCommitSegment', readSegmentPayload()));
  el.transportSelectSegment.addEventListener('click', () => sendCommand('transportSelectSegment', {}));
  el.transportUpdateSegment.addEventListener('click', () => sendCommand('transportUpdateSegment', readSegmentPayload()));
  el.transportInsertPoint.addEventListener('click', () => sendCommand('transportInsertSegmentPoint', {}));
  el.transportMovePoint.addEventListener('click', () => sendCommand('transportMoveSegmentPoint', {}));
  el.transportDeletePoint.addEventListener('click', () => sendCommand('transportDeleteSegmentPoint', {}));
  el.transportDeleteSegment.addEventListener('click', () => sendCommand('transportDeleteSegment', {}));
  el.transportRebake.addEventListener('click', () => sendCommand('transportRebake', {}));
  el.transportSave.addEventListener('click', () => sendCommand('transportSave', {}));
  el.transportAgent.addEventListener('change', () => sendCommand('transportSetRouteAgent', { agentTypeId: el.transportAgent.value }));
  el.transportQueryRoute.addEventListener('click', () => sendCommand('transportQueryRoute', { agentTypeId: el.transportAgent.value }));
}

function readBrush() {
  return {
    radiusCells: readNumber(el.brushRadius, 'Brush radius'),
    heightLevel: readNumber(el.brushHeight, 'Height level'),
    areaId: readNumber(el.brushArea, 'Area id'),
    cost: readNumber(el.brushCost, 'Cost'),
    blocked: el.brushBlocked.checked,
    water: el.brushWater.checked,
    ramp: el.brushRamp.checked
  };
}

function sendBrushCommand(name) {
  try {
    return sendCommand(name, readBrush());
  } catch (error) {
    el.transport.textContent = error.message;
    return Promise.resolve();
  }
}

function readNodePayload() {
  return {
    id: readOptionalText(el.transportNodeId),
    kind: el.transportNodeKind.value,
    tags: readOptionalText(el.transportNodeTags)
  };
}

function readSegmentPayload() {
  return {
    id: readOptionalText(el.transportSegmentId),
    areaId: readOptionalText(el.transportSegmentArea),
    tags: readOptionalText(el.transportSegmentTags),
    direction: el.transportSegmentDirection.value,
    flowDirection: el.transportSegmentFlow.value,
    depthCm: readNumber(el.transportSegmentDepth, 'Transport depth'),
    widthCm: readNumber(el.transportSegmentWidth, 'Transport width'),
    laneCount: readNumber(el.transportSegmentLanes, 'Transport lanes'),
    visualWidthMeters: readNumber(el.transportSegmentVisual, 'Transport visual width'),
    sampleStepCm: readNumber(el.transportSegmentSample, 'Transport segment sample step')
  };
}

function readOptionalText(input) {
  const text = input.value.trim();
  return text === '' ? undefined : text;
}

function readNumber(input, label) {
  const text = input.value.trim();
  if (text === '') {
    throw new Error(`${label} is required`);
  }

  const value = Number(text);
  if (!Number.isFinite(value) || !input.checkValidity()) {
    throw new Error(`${label} is invalid`);
  }

  return value;
}

function control(kind, topic, payload) {
  return request(kind, topic, payload);
}

function sendCommand(name, payload, entityRefs = []) {
  return request('command', COMMAND_TOPIC, {
    name,
    clientSeq: ++clientSeq,
    entityRefs,
    payload
  }).catch((error) => {
    el.transport.textContent = error.message;
  });
}

function selectedEntityRefs() {
  const selected = latestState?.entities?.selected;
  if (!selected || !selected.stableId) return [];
  return [{ stableId: selected.stableId, generation: selected.generation ?? 0 }];
}

function request(kind, topic, payload) {
  const id = ++requestId;
  const envelope = {
    schemaVersion: SCHEMA_VERSION,
    sessionId: SESSION_ID,
    requestId: id,
    kind,
    topic,
    payload
  };

  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`${kind} timeout`));
    }, 4000);
    pending.set(id, { resolve, reject, timeout });
    window.ludotsDataplane.postMessage(envelope);
  });
}

function handleHostMessage(raw) {
  const message = normalizeMessage(raw);
  if (!message) return;

  const pendingRequest = message.requestId ? pending.get(message.requestId) : null;
  if (pendingRequest) {
    clearTimeout(pendingRequest.timeout);
    pending.delete(message.requestId);
    if (message.kind === 'commandError' || message.payload?.code || message.payload?.error) {
      pendingRequest.reject(new Error(message.payload?.message ?? message.payload?.error ?? 'command failed'));
    } else {
      pendingRequest.resolve(message);
    }
  }

  if (message.topic === STATE_TOPIC && (message.kind === 'snapshot' || message.kind === 'delta')) {
    latestState = message.payload;
    render(latestState);
  }

  const subscription = subscriptions.get(message.topic);
  if (subscription) subscription(message);
}

function normalizeMessage(raw) {
  const data = raw && typeof raw === 'object' && 'data' in raw ? raw.data : raw;
  if (!data) return null;
  if (typeof data === 'string') return normalizeEnvelope(parseJson(data));
  if (data.channel === 'ludots.dataplane.control') return normalizeEnvelope(parseJson(data.payload));
  return normalizeEnvelope(data);
}

function normalizeEnvelope(envelope) {
  if (!envelope || envelope.schemaVersion !== SCHEMA_VERSION) return null;
  const kind = lowerFirst(envelope.kind);
  if (envelope.payload?.schemaVersion === SCHEMA_VERSION) {
    return {
      requestId: envelope.payload.requestId ?? envelope.requestId,
      kind: envelope.payload.kind,
      topic: envelope.payload.topic ?? envelope.topic,
      payload: envelope.payload.payload ?? {}
    };
  }

  return {
    requestId: envelope.requestId,
    kind,
    topic: envelope.topic,
    payload: envelope.payload ?? {}
  };
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function lowerFirst(value) {
  const text = String(value ?? '');
  return text ? text.charAt(0).toLowerCase() + text.slice(1) : '';
}

function render(state) {
  el.mapId.textContent = state.map?.id ?? 'No map';
  el.saveStatus.textContent = `${state.save?.status ?? 'idle'} ${state.save?.message ?? ''}`.trim();
  renderTools(state.tool);
  renderTransportModes(state.transport?.mode);
  renderTransportAgentOptions(state.transport?.agentTypes ?? [], state.transport?.route?.agentTypeId ?? '');
  renderSummary(el.mapSummary, [
    ['Map', state.map?.id ?? '-'],
    ['Boards', String(state.map?.boards?.length ?? 0)],
    ['Terrain', state.terrain ? `${state.terrain.topology} ${state.terrain.widthCells}x${state.terrain.heightCells}` : '-'],
    ['Cell cm', String(state.terrain?.cellSizeCm ?? '-')],
    ['Entities', String(state.entities?.count ?? 0)],
    ['Dirty', state.terrain?.dirty ? `${state.terrain.dirty.width}x${state.terrain.dirty.height} cm` : 'clean']
  ]);
  renderSummary(el.navSummary, [
    ['Runtime', state.nav?.runtime ?? '-'],
    ['Supported', state.nav?.supportedRuntime ? 'yes' : 'no'],
    ['Loaded tiles', String(state.nav?.loadedTiles ?? 0)],
    ['Pending', String(state.nav?.pendingTiles ?? 0)],
    ['Last bake', state.nav?.message ?? '-']
  ]);
  renderSummary(el.transportSummary, [
    ['Status', state.transport?.available ? state.transport.status : `unavailable ${state.transport?.lastError ?? ''}`],
    ['Asset', state.transport?.assetId || '-'],
    ['Nodes', String(state.transport?.nodeCount ?? 0)],
    ['Segments', String(state.transport?.segmentCount ?? 0)],
    ['Draft pts', String(state.transport?.draftPointCount ?? 0)],
    ['Graph chunks', String(state.transport?.bakedGraphChunks ?? 0)],
    ['Ribbon chunks', String(state.transport?.bakedRibbonChunks ?? 0)],
    ['Route', `${state.transport?.route?.status ?? '-'} ${state.transport?.route?.pointCount ?? 0} pts ${state.transport?.route?.elapsedUs ?? 0} us`],
    ['Selected', state.transport?.selectedNodeId || state.transport?.selectedSegmentId || '-']
  ]);
  renderTransportLists(state.transport);
  el.pickStatus.textContent = state.pick?.hasWorld
    ? `Pick ${state.pick.xCm}, ${state.pick.yCm} cm | cell ${state.pick.hasCell ? `${state.pick.col}, ${state.pick.row}` : '-'}`
    : 'No pick';
  el.entityStatus.textContent = state.entities?.selected
    ? `Selected ${state.entities.selected.name} #${state.entities.selected.entityId}`
    : 'No selection';
  el.navStatus.textContent = `Path ${state.sim?.status ?? '-'} | ${state.sim?.pointCount ?? 0} pts | ${state.sim?.elapsedUs ?? 0} us`;
  el.simSummary.textContent = `Nav ${state.sim?.hasStart ? `${state.sim.startXcm},${state.sim.startYcm}` : '-'} -> ${state.sim?.hasGoal ? `${state.sim.goalXcm},${state.sim.goalYcm}` : '-'} | Transport ${state.transport?.route?.hasStart ? `${state.transport.route.startXcm},${state.transport.route.startYcm}` : '-'} -> ${state.transport?.route?.hasGoal ? `${state.transport.route.goalXcm},${state.transport.route.goalYcm}` : '-'} | ${state.transport?.route?.agentTypeId || '-'}`;
}

function renderTools(activeTool) {
  for (const button of el.toolTabs.querySelectorAll('button[data-tool]')) {
    button.classList.toggle('active', button.dataset.tool === activeTool);
  }
}

function renderTransportModes(activeMode) {
  for (const button of el.transportModeTabs.querySelectorAll('button[data-transport-mode]')) {
    button.classList.toggle('active', button.dataset.transportMode === activeMode);
  }
}

function renderTransportAgentOptions(agentTypes, activeAgent) {
  const current = el.transportAgent.value || activeAgent;
  const nextIds = agentTypes.map((agent) => agent.id).join('|');
  if (el.transportAgent.dataset.ids !== nextIds) {
    el.transportAgent.replaceChildren();
    for (const agent of agentTypes) {
      const option = document.createElement('option');
      option.value = agent.id;
      option.textContent = `${agent.id} draft ${agent.draftCm} beam ${agent.beamCm}`;
      el.transportAgent.append(option);
    }
    el.transportAgent.dataset.ids = nextIds;
  }
  if (current && [...el.transportAgent.options].some((option) => option.value === current)) {
    el.transportAgent.value = current;
  }
}

function renderTransportLists(transport) {
  const nodes = transport?.nodes ?? [];
  el.transportNodes.textContent = nodes.length
    ? nodes.map((node) => `${node.id} ${node.kind} (${node.xcm}, ${node.ycm})`).join('\n')
    : 'No nodes';
  const segments = transport?.segments ?? [];
  el.transportSegments.textContent = segments.length
    ? segments.map((segment) => `${segment.id} ${segment.areaId || '-'} ${segment.direction}/${segment.flowDirection} pts=${segment.pointCount} depth=${segment.depthCm} width=${segment.widthCm}`).join('\n')
    : 'No segments';
}

function renderSummary(target, rows) {
  target.replaceChildren();
  for (const [label, value] of rows) {
    const dt = document.createElement('dt');
    dt.textContent = label;
    const dd = document.createElement('dd');
    dd.textContent = value;
    target.append(dt, dd);
  }
}
