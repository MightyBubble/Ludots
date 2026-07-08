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
  commandStatus: document.querySelector('#command-status'),
  saveStatus: document.querySelector('#save-status'),
  newMap: document.querySelector('#new-map'),
  save: document.querySelector('#save-map'),
  mapId: document.querySelector('#map-id'),
  mapSummary: document.querySelector('#map-summary'),
  mapLifecycleSummary: document.querySelector('#map-lifecycle-summary'),
  createMapId: document.querySelector('#create-map-id'),
  createMapBoard: document.querySelector('#create-map-board'),
  createMapTopology: document.querySelector('#create-map-topology'),
  createMapNav: document.querySelector('#create-map-nav'),
  createMapWidth: document.querySelector('#create-map-width'),
  createMapHeight: document.querySelector('#create-map-height'),
  createMapCell: document.querySelector('#create-map-cell'),
  createMapHex: document.querySelector('#create-map-hex'),
  createMapPreview: document.querySelector('#create-map-preview'),
  previewCreateMap: document.querySelector('#preview-create-map'),
  createMap: document.querySelector('#create-map'),
  createMapLoad: document.querySelector('#create-map-load'),
  boardStack: document.querySelector('#board-stack'),
  boardEditCell: document.querySelector('#board-edit-cell'),
  boardEditHex: document.querySelector('#board-edit-hex'),
  boardEditNav: document.querySelector('#board-edit-nav'),
  updateBoard: document.querySelector('#update-board'),
  deleteBoard: document.querySelector('#delete-board'),
  reloadMap: document.querySelector('#reload-map'),
  addBoardName: document.querySelector('#add-board-name'),
  addBoardTopology: document.querySelector('#add-board-topology'),
  addBoardNav: document.querySelector('#add-board-nav'),
  addBoardWidth: document.querySelector('#add-board-width'),
  addBoardHeight: document.querySelector('#add-board-height'),
  addBoardCell: document.querySelector('#add-board-cell'),
  addBoardHex: document.querySelector('#add-board-hex'),
  addBoardPreview: document.querySelector('#add-board-preview'),
  previewAddBoard: document.querySelector('#preview-add-board'),
  addBoard: document.querySelector('#add-board'),
  navSummary: document.querySelector('#nav-summary'),
  navConfigSummary: document.querySelector('#nav-config-summary'),
  transportSummary: document.querySelector('#transport-summary'),
  simSummary: document.querySelector('#sim-summary'),
  pickStatus: document.querySelector('#pick-status'),
  entityStatus: document.querySelector('#entity-status'),
  navStatus: document.querySelector('#nav-status'),
  toolTabs: document.querySelector('#tool-tabs'),
  brushRadius: document.querySelector('#brush-radius'),
  brushModeTabs: document.querySelector('#brush-mode-tabs'),
  brushTarget: document.querySelector('#brush-target'),
  brushHeight: document.querySelector('#brush-height'),
  brushWaterHeight: document.querySelector('#brush-water-height'),
  brushArea: document.querySelector('#brush-area'),
  brushCost: document.querySelector('#brush-cost'),
  brushBlocked: document.querySelector('#brush-blocked'),
  brushWater: document.querySelector('#brush-water'),
  brushRamp: document.querySelector('#brush-ramp'),
  paintPicked: document.querySelector('#paint-picked'),
  waterBucket: document.querySelector('#water-bucket'),
  brushStatus: document.querySelector('#brush-status'),
  viewToggles: document.querySelectorAll('[data-view-toggle]'),
  template: document.querySelector('#entity-template'),
  templateOptions: document.querySelector('#entity-template-options'),
  placePicked: document.querySelector('#place-picked'),
  selectPicked: document.querySelector('#select-picked'),
  removeSelected: document.querySelector('#remove-selected'),
  entityOverrideComponent: document.querySelector('#entity-override-component'),
  entityOverrideJson: document.querySelector('#entity-override-json'),
  setEntityOverride: document.querySelector('#set-entity-override'),
  deleteEntityOverride: document.querySelector('#delete-entity-override'),
  entityOverrides: document.querySelector('#entity-overrides'),
  obstacleTemplate: document.querySelector('#obstacle-template'),
  obstacleShape: document.querySelector('#obstacle-shape'),
  obstacleRadius: document.querySelector('#obstacle-radius'),
  obstacleNavRadius: document.querySelector('#obstacle-nav-radius'),
  obstacleHalfWidth: document.querySelector('#obstacle-half-width'),
  obstacleHalfHeight: document.querySelector('#obstacle-half-height'),
  obstacleSinkPhysics: document.querySelector('#obstacle-sink-physics'),
  obstacleSinkNav: document.querySelector('#obstacle-sink-nav'),
  obstaclePolygon: document.querySelector('#obstacle-polygon'),
  setObstacle: document.querySelector('#set-obstacle'),
  placeObstacle: document.querySelector('#place-obstacle'),
  eraseObstacle: document.querySelector('#erase-obstacle'),
  bakeScope: document.querySelector('#bake-scope'),
  bakeBudget: document.querySelector('#bake-budget'),
  bakeNeighbors: document.querySelector('#bake-neighbors'),
  bakeParallel: document.querySelector('#bake-parallel'),
  estimateBake: document.querySelector('#estimate-bake'),
  rebake: document.querySelector('#rebake'),
  clearNav: document.querySelector('#clear-nav'),
  navConfigMode: document.querySelector('#nav-config-mode'),
  navConfigAlgorithm: document.querySelector('#nav-config-algorithm'),
  navRuntimeBudget: document.querySelector('#nav-runtime-budget'),
  navRuntimeHeightScale: document.querySelector('#nav-runtime-height-scale'),
  navRuntimeUpDot: document.querySelector('#nav-runtime-up-dot'),
  navRuntimeCliff: document.querySelector('#nav-runtime-cliff'),
  navRuntimeNeighbors: document.querySelector('#nav-runtime-neighbors'),
  navConfigSave: document.querySelector('#nav-config-save'),
  navConfigReload: document.querySelector('#nav-config-reload'),
  navAgentId: document.querySelector('#nav-agent-id'),
  navAgentRadius: document.querySelector('#nav-agent-radius'),
  navAgentHeight: document.querySelector('#nav-agent-height'),
  navAgentClearance: document.querySelector('#nav-agent-clearance'),
  navAgentDraft: document.querySelector('#nav-agent-draft'),
  navAgentBeam: document.querySelector('#nav-agent-beam'),
  navAgentMass: document.querySelector('#nav-agent-mass'),
  navAgentLayer: document.querySelector('#nav-agent-layer'),
  navAgentUpsert: document.querySelector('#nav-agent-upsert'),
  navAgentDelete: document.querySelector('#nav-agent-delete'),
  navAgentProfiles: document.querySelector('#nav-agent-profiles'),
  navBakeProfileId: document.querySelector('#nav-bake-profile-id'),
  navBakeProfileClimb: document.querySelector('#nav-bake-profile-climb'),
  navBakeProfileSlope: document.querySelector('#nav-bake-profile-slope'),
  navBakeProfileUpsert: document.querySelector('#nav-bake-profile-upsert'),
  navBakeProfileDelete: document.querySelector('#nav-bake-profile-delete'),
  navBakeProfiles: document.querySelector('#nav-bake-profiles'),
  navLayerId: document.querySelector('#nav-layer-id'),
  navLayerValue: document.querySelector('#nav-layer-value'),
  navLayerUpsert: document.querySelector('#nav-layer-upsert'),
  navLayerDelete: document.querySelector('#nav-layer-delete'),
  navLayers: document.querySelector('#nav-layers'),
  navAreaId: document.querySelector('#nav-area-id'),
  navAreaValue: document.querySelector('#nav-area-value'),
  navAreaCost: document.querySelector('#nav-area-cost'),
  navAreaUpsert: document.querySelector('#nav-area-upsert'),
  navAreaDelete: document.querySelector('#nav-area-delete'),
  navAreas: document.querySelector('#nav-areas'),
  pathProfile: document.querySelector('#path-profile'),
  pathLayer: document.querySelector('#path-layer'),
  pathMaxPortals: document.querySelector('#path-max-portals'),
  queryPath: document.querySelector('#query-path'),
  minimap: document.querySelector('#minimap'),
  transportUnavailable: document.querySelector('#transport-unavailable'),
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
    if (!button || button.disabled) return;
    sendCommand('setTool', { tool: button.dataset.tool });
  });

  el.brushModeTabs.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-brush-mode]');
    if (!button) return;
    sendBrushCommand('setBrush', { mode: button.dataset.brushMode });
  });

  for (const input of [
    el.brushTarget,
    el.brushRadius,
    el.brushHeight,
    el.brushWaterHeight,
    el.brushArea,
    el.brushCost,
    el.brushBlocked,
    el.brushWater,
    el.brushRamp
  ]) {
    input.addEventListener('change', () => sendBrushCommand('setBrush'));
  }

  el.paintPicked.addEventListener('click', () => sendBrushCommand('paintTerrain'));
  el.waterBucket.addEventListener('click', () => sendBrushCommand('bucketFillWater'));
  for (const toggle of el.viewToggles) {
    toggle.addEventListener('change', () => sendCommand('setViewToggle', {
      name: toggle.dataset.viewToggle,
      enabled: toggle.checked
    }));
  }
  el.placePicked.addEventListener('click', () => sendCommand('placeEntity', { template: el.template.value.trim() }));
  el.selectPicked.addEventListener('click', () => sendCommand('selectEntity', {}));
  el.removeSelected.addEventListener('click', () => sendCommand('removeEntity', {}, selectedEntityRefs()));
  el.setEntityOverride.addEventListener('click', () => sendEntityOverride());
  el.deleteEntityOverride.addEventListener('click', () => sendCommand('deleteEntityOverride', {
    component: el.entityOverrideComponent.value.trim()
  }));
  for (const input of [
    el.obstacleTemplate,
    el.obstacleShape,
    el.obstacleRadius,
    el.obstacleNavRadius,
    el.obstacleHalfWidth,
    el.obstacleHalfHeight,
    el.obstacleSinkPhysics,
    el.obstacleSinkNav,
    el.obstaclePolygon
  ]) {
    input.addEventListener('change', () => sendObstacleCommand('setObstacle'));
  }
  el.setObstacle.addEventListener('click', () => sendObstacleCommand('setObstacle'));
  el.placeObstacle.addEventListener('click', () => sendObstacleCommand('placeObstacle'));
  el.eraseObstacle.addEventListener('click', () => sendCommand('eraseObstacle', {}));
  el.estimateBake.addEventListener('click', () => sendCommand('estimateNavBake', readBakeOptions()));
  el.rebake.addEventListener('click', () => sendCommand('rebakeNav', readBakeOptions()));
  el.clearNav.addEventListener('click', () => sendCommand('clearNavTiles', {}));
  el.navConfigSave.addEventListener('click', () => sendCommand('navConfigSave', {}));
  el.navConfigReload.addEventListener('click', () => sendCommand('navConfigReload', {}));
  el.navConfigMode.addEventListener('change', () => sendCommand('navSetMode', { mode: el.navConfigMode.value }));
  el.navConfigAlgorithm.addEventListener('change', () => sendCommand('navSetAlgorithm', { algorithm: el.navConfigAlgorithm.value }));
  el.navRuntimeBudget.addEventListener('change', () => sendRuntimeField('tileBudgetPerFixedTick', el.navRuntimeBudget));
  el.navRuntimeHeightScale.addEventListener('change', () => sendRuntimeField('heightScaleMeters', el.navRuntimeHeightScale));
  el.navRuntimeUpDot.addEventListener('change', () => sendRuntimeField('minWalkableUpDot', el.navRuntimeUpDot));
  el.navRuntimeCliff.addEventListener('change', () => sendRuntimeField('cliffHeightThreshold', el.navRuntimeCliff));
  el.navRuntimeNeighbors.addEventListener('change', () => sendCommand('navSetRuntimeField', {
    field: 'includeNeighborTiles',
    enabled: el.navRuntimeNeighbors.checked
  }));
  el.navAgentUpsert.addEventListener('click', () => sendCommand('navAddProfile', readAgentProfile()));
  el.navAgentDelete.addEventListener('click', () => sendCommand('navDeleteProfile', { id: el.navAgentId.value.trim() }));
  el.navBakeProfileUpsert.addEventListener('click', () => sendCommand('navAddBakeProfile', readBakeProfile()));
  el.navBakeProfileDelete.addEventListener('click', () => sendCommand('navDeleteBakeProfile', { id: el.navBakeProfileId.value.trim() }));
  el.navLayerUpsert.addEventListener('click', () => sendCommand('navAddLayer', readNavLayer()));
  el.navLayerDelete.addEventListener('click', () => sendCommand('navDeleteLayer', { id: el.navLayerId.value.trim() }));
  el.navAreaUpsert.addEventListener('click', () => sendCommand('navAddArea', readNavArea()));
  el.navAreaDelete.addEventListener('click', () => sendCommand('navDeleteArea', { id: el.navAreaId.value.trim() }));
  for (const input of [el.bakeScope, el.bakeBudget, el.bakeNeighbors, el.bakeParallel]) {
    input.addEventListener('change', () => sendCommand('setBakeOptions', readBakeOptions()));
  }
  for (const input of [el.pathProfile, el.pathLayer, el.pathMaxPortals]) {
    input.addEventListener('change', () => sendCommand('setPathOptions', readPathOptions()));
  }
  el.queryPath.addEventListener('click', () => sendCommand('setPathOptions', readPathOptions()).then(() => sendCommand('queryPath', {})));
  el.minimap.addEventListener('click', handleMinimapClick);
  el.newMap.addEventListener('click', () => focusMapCreation());
  el.save.addEventListener('click', () => sendCommand('saveMap', {}));
  el.previewCreateMap.addEventListener('click', () => sendCreateMapPreview());
  el.createMap.addEventListener('click', () => sendCreateMap());
  el.createMapLoad.addEventListener('click', () => sendCreateMap(true));
  el.previewAddBoard.addEventListener('click', () => sendAddBoardPreview());
  el.addBoard.addEventListener('click', () => sendAddBoard());
  el.updateBoard.addEventListener('click', () => sendUpdateBoard());
  el.deleteBoard.addEventListener('click', () => sendCommand('deleteBoard', { boardName: selectedBoardName() }));
  el.reloadMap.addEventListener('click', () => sendCommand('reloadMap', {}));
  el.boardStack.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-board-name]');
    if (!button) return;
    sendCommand('selectBoard', { boardName: button.dataset.boardName });
  });
  for (const input of [el.createMapWidth, el.createMapHeight, el.createMapCell]) {
    input.addEventListener('change', () => sendCreateMapPreview());
  }
  for (const input of [el.addBoardWidth, el.addBoardHeight, el.addBoardCell]) {
    input.addEventListener('change', () => sendAddBoardPreview());
  }

  el.transportModeTabs.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-transport-mode]');
    if (!button || button.disabled) return;
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
    mode: activeBrushMode(),
    target: el.brushTarget.value,
    radiusCells: readNumber(el.brushRadius, 'Brush radius'),
    heightLevel: readNumber(el.brushHeight, 'Height level'),
    waterHeightLevel: readNumber(el.brushWaterHeight, 'Water height'),
    areaId: readNumber(el.brushArea, 'Area id'),
    cost: readNumber(el.brushCost, 'Cost'),
    blocked: el.brushBlocked.checked,
    water: el.brushWater.checked,
    ramp: el.brushRamp.checked
  };
}

function activeBrushMode() {
  return el.brushModeTabs.querySelector('button.active')?.dataset.brushMode || latestState?.brush?.mode || 'set';
}

function sendBrushCommand(name, patch = {}) {
  try {
    return sendCommand(name, { ...readBrush(), ...patch });
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function readEntityOverride() {
  const component = el.entityOverrideComponent.value.trim();
  const json = el.entityOverrideJson.value.trim();
  if (!component) throw new Error('Override component is required');
  JSON.parse(json);
  return { component, json };
}

function readObstacle() {
  return {
    template: el.obstacleTemplate.value.trim(),
    shape: el.obstacleShape.value,
    radiusCm: readNumber(el.obstacleRadius, 'Obstacle radius'),
    halfWidthCm: readNumber(el.obstacleHalfWidth, 'Obstacle half width'),
    halfHeightCm: readNumber(el.obstacleHalfHeight, 'Obstacle half height'),
    navRadiusCm: readNumber(el.obstacleNavRadius, 'Obstacle nav radius'),
    sinkPhysicsCollider: el.obstacleSinkPhysics.checked,
    sinkNavigationObstacle: el.obstacleSinkNav.checked,
    polygon: el.obstaclePolygon.value.trim()
  };
}

function sendObstacleCommand(name, patch = {}) {
  try {
    return sendCommand(name, { ...readObstacle(), ...patch });
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function sendEntityOverride() {
  try {
    return sendCommand('setEntityOverride', readEntityOverride());
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function readBakeOptions() {
  return {
    scope: el.bakeScope.value,
    maxTiles: readNumber(el.bakeBudget, 'Bake budget'),
    includeNeighbors: el.bakeNeighbors.checked,
    parallel: el.bakeParallel.checked
  };
}

function readPathOptions() {
  return {
    profileId: el.pathProfile.value || undefined,
    layer: readNumber(el.pathLayer, 'Path layer'),
    maxPortals: readNumber(el.pathMaxPortals, 'Max portals')
  };
}

function readCreateMapPayload() {
  return {
    mapId: el.createMapId.value.trim(),
    boardName: el.createMapBoard.value.trim(),
    topology: el.createMapTopology.value,
    navigationEnabled: el.createMapNav.checked,
    widthMeters: readNumber(el.createMapWidth, 'Create map width'),
    heightMeters: readNumber(el.createMapHeight, 'Create map height'),
    cellSizeCm: readNumber(el.createMapCell, 'Create map cell size'),
    hexEdgeLengthCm: readNumber(el.createMapHex, 'Create map hex edge')
  };
}

function readAddBoardPayload() {
  return {
    boardName: el.addBoardName.value.trim(),
    topology: el.addBoardTopology.value,
    navigationEnabled: el.addBoardNav.checked,
    widthMeters: readNumber(el.addBoardWidth, 'Add board width'),
    heightMeters: readNumber(el.addBoardHeight, 'Add board height'),
    cellSizeCm: readNumber(el.addBoardCell, 'Add board cell size'),
    hexEdgeLengthCm: readNumber(el.addBoardHex, 'Add board hex edge')
  };
}

function sendCreateMapPreview() {
  try {
    const payload = readCreateMapPayload();
    return sendCommand('previewBoardAllocation', {
      slot: 'createMap',
      widthMeters: payload.widthMeters,
      heightMeters: payload.heightMeters,
      cellSizeCm: payload.cellSizeCm
    });
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function sendAddBoardPreview() {
  try {
    const payload = readAddBoardPayload();
    return sendCommand('previewBoardAllocation', {
      slot: 'addBoard',
      widthMeters: payload.widthMeters,
      heightMeters: payload.heightMeters,
      cellSizeCm: payload.cellSizeCm
    });
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function focusMapCreation() {
  sendCommand('setTool', { tool: 'map' });
  document.querySelector('.panel.right')?.scrollTo({ top: 0, behavior: 'smooth' });
  window.setTimeout(() => el.createMapId.focus(), 120);
}

function sendCreateMap(loadAfterCreate = false) {
  try {
    return sendCommand('createMap', { ...readCreateMapPayload(), loadAfterCreate });
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function sendAddBoard() {
  try {
    return sendCommand('addBoard', readAddBoardPayload());
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function sendUpdateBoard() {
  try {
    return sendCommand('updateBoard', {
      boardName: selectedBoardName(),
      cellSizeCm: readNumber(el.boardEditCell, 'Board cell size'),
      hexEdgeLengthCm: readNumber(el.boardEditHex, 'Board hex edge'),
      navigationEnabled: el.boardEditNav.checked
    });
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function selectedBoardName() {
  return latestState?.mapLifecycle?.selectedBoardName || latestState?.map?.selectedBoardName || 'default';
}

function sendRuntimeField(field, input) {
  try {
    return sendCommand('navSetRuntimeField', { field, value: readNumber(input, field) });
  } catch (error) {
    setCommandStatus(error.message, 'error');
    return Promise.resolve();
  }
}

function readAgentProfile() {
  return {
    id: el.navAgentId.value.trim(),
    radiusCm: readNumber(el.navAgentRadius, 'Agent radius'),
    heightCm: readNumber(el.navAgentHeight, 'Agent height'),
    clearanceCm: readNumber(el.navAgentClearance, 'Agent clearance'),
    draftCm: readNumber(el.navAgentDraft, 'Agent draft'),
    beamCm: readNumber(el.navAgentBeam, 'Agent beam'),
    mass: readNumber(el.navAgentMass, 'Agent mass'),
    layer: readNumber(el.navAgentLayer, 'Agent layer')
  };
}

function readBakeProfile() {
  return {
    id: el.navBakeProfileId.value.trim(),
    maxClimbCm: readNumber(el.navBakeProfileClimb, 'Bake max climb'),
    maxSlopeDeg: readNumber(el.navBakeProfileSlope, 'Bake max slope')
  };
}

function readNavLayer() {
  return {
    id: el.navLayerId.value.trim(),
    layer: readNumber(el.navLayerValue, 'Navigation layer')
  };
}

function readNavArea() {
  return {
    id: el.navAreaId.value.trim(),
    areaId: readNumber(el.navAreaValue, 'Navigation area id'),
    cost: readNumber(el.navAreaCost, 'Navigation area cost')
  };
}

function handleMinimapClick(event) {
  const minimap = latestState?.minimap;
  if (!minimap || !minimap.widthCells || !minimap.heightCells || !minimap.cellSizeCm) {
    return;
  }

  const rect = el.minimap.getBoundingClientRect();
  const xRatio = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
  const yRatio = Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height));
  const xCm = Math.round(xRatio * minimap.widthCells * minimap.cellSizeCm);
  const yCm = Math.round(yRatio * minimap.heightCells * minimap.cellSizeCm);
  sendCommand('cameraPanTo', { xCm, yCm });
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
  setCommandStatus(`${name} sending`, 'pending');
  return request('command', COMMAND_TOPIC, {
    name,
    clientSeq: ++clientSeq,
    entityRefs,
    payload
  }).then((message) => {
    setCommandStatus(`${name} ok`, 'ok');
    return message;
  }).catch((error) => {
    setCommandStatus(`${name} ${formatCommandError(error)}`, 'error');
  });
}

function selectedEntityRefs() {
  const selected = latestState?.entities?.selected;
  if (!selected || !selected.stableId) return [];
  return [{ stableId: selected.stableId, generation: selected.generation ?? 0 }];
}

function setCommandStatus(message, tone = 'idle') {
  el.commandStatus.textContent = message;
  el.commandStatus.dataset.tone = tone;
}

function formatCommandError(error) {
  const code = error?.code ? `${error.code}: ` : '';
  return `${code}${error?.message ?? 'command failed'}`;
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
      const error = new Error(message.payload?.message ?? message.payload?.error ?? 'command failed');
      error.code = message.payload?.code ?? message.payload?.error ?? '';
      pendingRequest.reject(error);
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
  if (isDataPlaneEnvelope(envelope.payload)) {
    const nested = envelope.payload;
    return {
      requestId: nested.requestId ?? envelope.requestId,
      kind: lowerFirst(nested.kind),
      topic: nested.topic ?? envelope.topic,
      payload: nested.payload ?? {}
    };
  }

  return {
    requestId: envelope.requestId,
    kind,
    topic: envelope.topic,
    payload: envelope.payload ?? {}
  };
}

function isDataPlaneEnvelope(value) {
  return Boolean(
    value &&
    value.schemaVersion === SCHEMA_VERSION &&
    typeof value.kind === 'string' &&
    typeof value.topic === 'string'
  );
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
  renderBrushModes(state.brush?.mode);
  renderBrushInputs(state.brush);
  renderBrushPickState(state.pick, state.terrain);
  renderObstacleInputs(state.obstacle);
  renderViewToggles(state.view);
  renderTransportAvailability(state.transport);
  renderTransportModes(state.transport?.mode);
  renderTransportAgentOptions(state.transport?.agentTypes ?? [], state.transport?.route?.agentTypeId ?? '');
  renderEntityTemplateOptions(state.entities?.templates ?? []);
  renderPathProfileOptions(state.nav?.profiles ?? [], state.nav?.queryProfileId ?? '');
  renderBakeInputs(state.nav);
  renderNavConfig(state.navConfig);
  renderMapLifecycle(state);
  renderSummary(el.mapSummary, [
    ['Map', state.map?.id ?? '-'],
    ['Boards', String(state.map?.boards?.length ?? 0)],
    ['Authored', String(state.map?.authoredBoards?.length ?? 0)],
    ['Selected', state.mapLifecycle?.selectedBoardName ?? '-'],
    ['Reload', state.mapLifecycle?.reloadRequired ? 'required' : 'clean'],
    ['Terrain', state.terrain ? `${state.terrain.topology} ${state.terrain.widthCells}x${state.terrain.heightCells}` : '-'],
    ['Cell cm', String(state.terrain?.cellSizeCm ?? '-')],
    ['Entities', String(state.entities?.count ?? 0)],
    ['Dirty', state.terrain?.dirty ? `${state.terrain.dirty.width}x${state.terrain.dirty.height} cm` : 'clean']
  ]);
  renderSummary(el.navSummary, [
    ['Runtime', state.nav?.runtime ?? '-'],
    ['Supported', state.nav?.supportedRuntime ? 'yes' : 'no'],
    ['Config', state.navConfig?.dirty ? 'reload required' : (state.navConfig?.status ?? 'idle')],
    ['Loaded tiles', String(state.nav?.loadedTiles ?? 0)],
    ['Pending', String(state.nav?.pendingTiles ?? 0)],
    ['Estimate', String(state.nav?.estimatedTiles ?? 0)],
    ['Scope', state.nav?.bakeScope ?? '-'],
    ['Profile', `${state.nav?.queryProfileId || state.nav?.queryProfileIndex || 0} L${state.nav?.queryLayer ?? 0}`],
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
  renderEntityOverrides(state.entities?.selected);
  el.navStatus.textContent = `Path ${state.sim?.status ?? '-'} | ${state.sim?.pointCount ?? 0} pts | ${state.sim?.elapsedUs ?? 0} us`;
  el.simSummary.textContent = `Nav ${state.sim?.hasStart ? `${state.sim.startXcm},${state.sim.startYcm}` : '-'} -> ${state.sim?.hasGoal ? `${state.sim.goalXcm},${state.sim.goalYcm}` : '-'} | Transport ${state.transport?.route?.hasStart ? `${state.transport.route.startXcm},${state.transport.route.startYcm}` : '-'} -> ${state.transport?.route?.hasGoal ? `${state.transport.route.goalXcm},${state.transport.route.goalYcm}` : '-'} | ${state.transport?.route?.agentTypeId || '-'}`;
  renderMinimap(state.minimap);
}

function renderMapLifecycle(state) {
  const lifecycle = state.mapLifecycle ?? {};
  renderSummary(el.mapLifecycleSummary, [
    ['Board status', lifecycle.status || 'idle'],
    ['Target mod', lifecycle.targetModId || '-'],
    ['Config path', lifecycle.mapConfigPath || '-'],
    ['Message', lifecycle.message || '-']
  ]);
  renderBoardStack(state.map?.authoredBoards ?? [], lifecycle.selectedBoardName || state.map?.selectedBoardName || '');
  renderAllocationPreview(el.createMapPreview, lifecycle.createMapPreview);
  renderAllocationPreview(el.addBoardPreview, lifecycle.addBoardPreview);
  renderBoardEditor(state.map?.authoredBoards ?? [], lifecycle.selectedBoardName || state.map?.selectedBoardName || '');
}

function renderBoardStack(boards, selectedName) {
  el.boardStack.replaceChildren();
  if (!boards.length) {
    el.boardStack.textContent = 'No authored boards';
    return;
  }

  for (const board of boards) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.boardName = board.name;
    button.className = board.name === selectedName ? 'active' : '';
    const allocation = board.allocation;
    const extent = allocation
      ? `${formatMeters(allocation.allocatedWidthMeters)}m x ${formatMeters(allocation.allocatedHeightMeters)}m`
      : `${board.widthInMacroTiles}x${board.heightInMacroTiles} MT`;
    button.textContent = `${board.name} | ${board.spatialType} | ${extent} | ${board.navigationEnabled ? 'nav' : 'no nav'}`;
    el.boardStack.append(button);
  }
}

function renderBoardEditor(boards, selectedName) {
  const selected = boards.find((board) => board.name === selectedName) ?? boards[0];
  const disabled = !selected;
  el.updateBoard.disabled = disabled;
  el.deleteBoard.disabled = disabled || boards.length <= 1;
  el.reloadMap.disabled = !latestState?.mapLifecycle?.reloadRequired;
  if (!selected) return;

  const key = `${selected.name}:${selected.gridCellSizeCm}:${selected.hexEdgeLengthCm}:${selected.navigationEnabled}`;
  if (el.boardEditCell.dataset.boardKey === key) {
    return;
  }

  el.boardEditCell.dataset.boardKey = key;
  syncInput(el.boardEditCell, selected.gridCellSizeCm);
  syncInput(el.boardEditHex, selected.hexEdgeLengthCm);
  if (document.activeElement !== el.boardEditNav) {
    el.boardEditNav.checked = Boolean(selected.navigationEnabled);
  }
}

function renderAllocationPreview(target, preview) {
  if (!preview) {
    renderSummary(target, [['Allocation', '-']]);
    return;
  }

  renderSummary(target, [
    ['Requested', `${formatMeters(preview.requestedWidthMeters)}m x ${formatMeters(preview.requestedHeightMeters)}m`],
    ['Allocated', `${formatMeters(preview.allocatedWidthMeters)}m x ${formatMeters(preview.allocatedHeightMeters)}m`],
    ['Cells', `${formatInteger(preview.allocatedWidthCells)} x ${formatInteger(preview.allocatedHeightCells)}`],
    ['MacroTiles', `${preview.widthMacroTiles} x ${preview.heightMacroTiles}`],
    ['Terrain/NavTiles', `${preview.widthTerrainChunks} x ${preview.heightTerrainChunks}`],
    ['Unit', `${formatMeters(preview.macroTileMeters)}m MT / ${formatMeters(preview.terrainChunkMeters)}m chunk`],
    ['Sparse resident', `0 / ${formatInteger(preview.totalTerrainChunks)}`],
    ['Full file', formatBytes(preview.fullTerrainBytes)],
    ['Snap', preview.snappedToMacroTile ? 'macro tile' : 'exact']
  ]);
}

function renderTools(activeTool) {
  for (const button of el.toolTabs.querySelectorAll('button[data-tool]')) {
    button.classList.toggle('active', button.dataset.tool === activeTool);
  }
}

function renderBrushModes(activeMode) {
  for (const button of el.brushModeTabs.querySelectorAll('button[data-brush-mode]')) {
    button.classList.toggle('active', button.dataset.brushMode === (activeMode || 'set'));
  }
}

function renderBrushInputs(brush) {
  if (!brush) return;
  syncInput(el.brushTarget, brush.target);
  syncInput(el.brushRadius, brush.radiusCells);
  syncInput(el.brushHeight, brush.heightLevel);
  syncInput(el.brushWaterHeight, brush.waterHeightLevel);
  syncInput(el.brushArea, brush.areaId);
  syncInput(el.brushCost, brush.cost);
  el.brushBlocked.checked = Boolean(brush.blocked);
  el.brushWater.checked = Boolean(brush.water);
  el.brushRamp.checked = Boolean(brush.ramp);
}

function renderBrushPickState(pick, terrain) {
  const hasTerrain = Boolean(terrain?.widthCells && terrain?.heightCells);
  const hasCell = Boolean(pick?.hasCell);
  const disabled = !hasTerrain || !hasCell;
  el.paintPicked.disabled = disabled;
  el.waterBucket.disabled = disabled;
  const text = !hasTerrain
    ? 'No terrain'
    : hasCell
      ? `Picked cell ${pick.col}, ${pick.row}`
      : 'No picked cell';
  el.brushStatus.textContent = text;
  el.brushStatus.dataset.tone = hasCell ? 'ok' : 'warn';
  el.paintPicked.title = hasCell ? `Paint cell ${pick.col}, ${pick.row}` : text;
  el.waterBucket.title = hasCell ? `Fill water from cell ${pick.col}, ${pick.row}` : text;
}

function renderObstacleInputs(obstacle) {
  if (!obstacle) return;
  syncInput(el.obstacleTemplate, obstacle.templateId);
  syncInput(el.obstacleShape, obstacle.shape);
  syncInput(el.obstacleRadius, obstacle.radiusCm);
  syncInput(el.obstacleHalfWidth, obstacle.halfWidthCm);
  syncInput(el.obstacleHalfHeight, obstacle.halfHeightCm);
  syncInput(el.obstacleNavRadius, obstacle.navRadiusCm);
  el.obstacleSinkPhysics.checked = Boolean(obstacle.sinkPhysicsCollider);
  el.obstacleSinkNav.checked = Boolean(obstacle.sinkNavigationObstacle);
  if (Array.isArray(obstacle.polygon) && document.activeElement !== el.obstaclePolygon) {
    el.obstaclePolygon.value = obstacle.polygon.map((point) => `${point.x},${point.y}`).join(' ');
  }
}

function renderEntityOverrides(selected) {
  const overrides = selected?.overrides ?? [];
  el.entityOverrides.textContent = overrides.length
    ? overrides.map((item) => `${item.component}\n${item.json}`).join('\n\n')
    : 'No overrides';

  const selectionKey = selected ? `${selected.entityId}:${selected.generation}:${selected.instanceId ?? ''}` : '';
  if (el.entityOverrides.dataset.selectionKey === selectionKey) {
    return;
  }

  el.entityOverrides.dataset.selectionKey = selectionKey;
  const first = overrides[0];
  if (first) {
    el.entityOverrideComponent.value = first.component;
    el.entityOverrideJson.value = first.json;
  } else {
    el.entityOverrideComponent.value = '';
    el.entityOverrideJson.value = '{}';
  }
}

function renderViewToggles(view) {
  if (!view) return;
  for (const toggle of el.viewToggles) {
    const name = toggle.dataset.viewToggle;
    toggle.checked = Boolean(view[name]);
  }
}

function renderBakeInputs(nav) {
  if (!nav) return;
  syncInput(el.bakeScope, nav.bakeScope);
  syncInput(el.bakeBudget, nav.bakeMaxTiles);
  el.bakeNeighbors.checked = Boolean(nav.bakeIncludeNeighbors);
  el.bakeParallel.checked = Boolean(nav.bakeParallel);
  syncInput(el.pathLayer, nav.queryLayer);
  syncInput(el.pathMaxPortals, nav.maxPortals);
}

function renderNavConfig(navConfig) {
  if (!navConfig) {
    renderSummary(el.navConfigSummary, [['Status', 'missing']]);
    return;
  }

  renderSummary(el.navConfigSummary, [
    ['Status', navConfig.dirty ? 'reload required' : (navConfig.status || 'ready')],
    ['Target mod', navConfig.targetModId || '-'],
    ['Agents', String(navConfig.agentProfiles?.length ?? 0)],
    ['Bake', String(navConfig.bakeProfiles?.length ?? 0)],
    ['Layers', String(navConfig.layers?.length ?? 0)],
    ['Areas', String(navConfig.areas?.length ?? 0)],
    ['Message', navConfig.message || '-']
  ]);
  syncInput(el.navConfigMode, navConfig.mode);
  syncInput(el.navConfigAlgorithm, navConfig.algorithm);
  const runtime = navConfig.runtimeIncremental ?? {};
  syncInput(el.navRuntimeBudget, runtime.tileBudgetPerFixedTick);
  syncInput(el.navRuntimeHeightScale, runtime.heightScaleMeters);
  syncInput(el.navRuntimeUpDot, runtime.minWalkableUpDot);
  syncInput(el.navRuntimeCliff, runtime.cliffHeightThreshold);
  el.navRuntimeNeighbors.checked = Boolean(runtime.includeNeighborTiles);

  const agentProfiles = navConfig.agentProfiles ?? [];
  el.navAgentProfiles.textContent = agentProfiles.length
    ? agentProfiles.map((profile) => `${profile.id} r=${profile.radiusCm} h=${profile.heightCm} clear=${profile.clearanceCm} mass=${profile.mass} L${profile.layer}`).join('\n')
    : 'No agent profiles';

  const bakeProfiles = navConfig.bakeProfiles ?? [];
  el.navBakeProfiles.textContent = bakeProfiles.length
    ? bakeProfiles.map((profile) => `${profile.id} climb=${profile.maxClimbCm} slope=${profile.maxSlopeDeg}`).join('\n')
    : 'No bake profiles';

  const layers = navConfig.layers ?? [];
  el.navLayers.textContent = layers.length
    ? layers.map((layer) => `${layer.id} L${layer.layer}`).join('\n')
    : 'No layers';

  const areas = navConfig.areas ?? [];
  el.navAreas.textContent = areas.length
    ? areas.map((area) => `${area.id} #${area.areaId} cost=${area.cost}`).join('\n')
    : 'No areas';

  seedNavConfigForms(agentProfiles, bakeProfiles, layers, areas);
}

function seedNavConfigForms(agentProfiles, bakeProfiles, layers, areas) {
  if (!el.navAgentId.dataset.seeded && agentProfiles.length) {
    const profile = agentProfiles[0];
    syncInput(el.navAgentId, profile.id);
    syncInput(el.navAgentRadius, profile.radiusCm);
    syncInput(el.navAgentHeight, profile.heightCm);
    syncInput(el.navAgentClearance, profile.clearanceCm);
    syncInput(el.navAgentDraft, profile.draftCm);
    syncInput(el.navAgentBeam, profile.beamCm);
    syncInput(el.navAgentMass, profile.mass);
    syncInput(el.navAgentLayer, profile.layer);
    el.navAgentId.dataset.seeded = '1';
  }

  if (!el.navBakeProfileId.dataset.seeded && bakeProfiles.length) {
    const profile = bakeProfiles[0];
    syncInput(el.navBakeProfileId, profile.id);
    syncInput(el.navBakeProfileClimb, profile.maxClimbCm);
    syncInput(el.navBakeProfileSlope, profile.maxSlopeDeg);
    el.navBakeProfileId.dataset.seeded = '1';
  }

  if (!el.navLayerId.dataset.seeded && layers.length) {
    const layer = layers[0];
    syncInput(el.navLayerId, layer.id);
    syncInput(el.navLayerValue, layer.layer);
    el.navLayerId.dataset.seeded = '1';
  }

  if (!el.navAreaId.dataset.seeded && areas.length) {
    const area = areas[0];
    syncInput(el.navAreaId, area.id);
    syncInput(el.navAreaValue, area.areaId);
    syncInput(el.navAreaCost, area.cost);
    el.navAreaId.dataset.seeded = '1';
  }
}

function renderTransportModes(activeMode) {
  for (const button of el.transportModeTabs.querySelectorAll('button[data-transport-mode]')) {
    button.classList.toggle('active', button.dataset.transportMode === activeMode);
  }
}

function renderTransportAvailability(transport) {
  const available = Boolean(transport?.available);
  const message = available ? '' : (transport?.lastError || 'No NodeGraph board');
  el.transportUnavailable.textContent = message;
  el.transportUnavailable.classList.toggle('visible', !available);
  for (const button of el.toolTabs.querySelectorAll('button[data-tool="transport"]')) {
    button.disabled = !available;
  }

  for (const target of document.querySelectorAll('[id^="transport-"], #transport-agent, [data-transport-mode]')) {
    if (target === el.transport || target === el.transportUnavailable || target === el.transportSummary) {
      continue;
    }
    target.disabled = !available;
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

function renderPathProfileOptions(profiles, activeProfile) {
  const current = el.pathProfile.value || activeProfile;
  const ids = profiles.map((profile) => profile.id).join('|');
  if (el.pathProfile.dataset.ids !== ids) {
    el.pathProfile.replaceChildren();
    for (const profile of profiles) {
      const option = document.createElement('option');
      option.value = profile.id;
      option.textContent = profile.id;
      el.pathProfile.append(option);
    }
    el.pathProfile.dataset.ids = ids;
  }
  if (current && [...el.pathProfile.options].some((option) => option.value === current)) {
    el.pathProfile.value = current;
  }
}

function renderEntityTemplateOptions(templates) {
  const ids = templates.map((template) => template.id).join('|');
  if (el.templateOptions.dataset.ids === ids) {
    return;
  }

  el.templateOptions.replaceChildren();
  for (const template of templates) {
    const option = document.createElement('option');
    option.value = template.id;
    option.label = `#${template.key}`;
    el.templateOptions.append(option);
  }
  el.templateOptions.dataset.ids = ids;
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

function renderMinimap(minimap) {
  const canvas = el.minimap;
  const context = canvas.getContext('2d');
  context.clearRect(0, 0, canvas.width, canvas.height);
  if (!minimap || !Array.isArray(minimap.chunks) || minimap.widthChunks <= 0 || minimap.heightChunks <= 0) {
    context.fillStyle = 'rgba(151, 170, 196, 0.18)';
    context.fillRect(0, 0, canvas.width, canvas.height);
    return;
  }

  const cellWidth = canvas.width / minimap.widthChunks;
  const cellHeight = canvas.height / minimap.heightChunks;
  for (const chunk of minimap.chunks) {
    const light = Math.min(78, 18 + Number(chunk.h || 0) * 4);
    context.fillStyle = chunk.water
      ? `hsl(196 64% ${Math.max(30, light)}%)`
      : chunk.blocked
        ? 'hsl(2 58% 48%)'
        : `hsl(${95 + Number(chunk.area || 0) * 18} 38% ${light}%)`;
    const x = chunk.x * cellWidth;
    const y = chunk.y * cellHeight;
    context.fillRect(x, y, Math.ceil(cellWidth), Math.ceil(cellHeight));
    if (chunk.dirty) {
      context.fillStyle = 'rgba(244, 189, 92, 0.26)';
      context.fillRect(x, y, Math.ceil(cellWidth), Math.ceil(cellHeight));
      context.strokeStyle = '#f4bd5c';
      context.lineWidth = 1;
      context.strokeRect(x + 0.5, y + 0.5, Math.max(1, cellWidth - 1), Math.max(1, cellHeight - 1));
    }
  }

  if (minimap.dirty) {
    context.strokeStyle = '#f4bd5c';
    context.lineWidth = 2;
    context.strokeRect(1, 1, canvas.width - 2, canvas.height - 2);
  }

  drawMinimapCamera(context, canvas, minimap);
}

function drawMinimapCamera(context, canvas, minimap) {
  const camera = minimap.camera;
  const worldWidthCm = Number(minimap.widthCells || 0) * Number(minimap.cellSizeCm || 0);
  const worldHeightCm = Number(minimap.heightCells || 0) * Number(minimap.cellSizeCm || 0);
  if (!camera || worldWidthCm <= 0 || worldHeightCm <= 0) {
    return;
  }

  const x = Math.max(0, Math.min(canvas.width, Number(camera.targetXcm || 0) / worldWidthCm * canvas.width));
  const y = Math.max(0, Math.min(canvas.height, Number(camera.targetYcm || 0) / worldHeightCm * canvas.height));
  const yaw = Number(camera.yawDeg || 0) * Math.PI / 180;
  const fov = Math.max(0.2, Math.min(1.4, Number(camera.fovYDeg || 55) * Math.PI / 180));
  const length = 34;
  const left = yaw - fov * 0.5;
  const right = yaw + fov * 0.5;
  const ax = x + Math.sin(left) * length;
  const ay = y - Math.cos(left) * length;
  const bx = x + Math.sin(right) * length;
  const by = y - Math.cos(right) * length;

  context.save();
  context.strokeStyle = '#eef4ff';
  context.fillStyle = 'rgba(238, 244, 255, 0.16)';
  context.lineWidth = 1.5;
  context.beginPath();
  context.moveTo(x, y);
  context.lineTo(ax, ay);
  context.lineTo(bx, by);
  context.closePath();
  context.fill();
  context.stroke();
  context.beginPath();
  context.arc(x, y, 3.5, 0, Math.PI * 2);
  context.fillStyle = '#55d6be';
  context.fill();
  context.restore();
}

function formatMeters(value) {
  const number = Number(value);
  return Number.isFinite(number)
    ? number.toLocaleString(undefined, { maximumFractionDigits: 2 })
    : '0';
}

function formatInteger(value) {
  const number = Number(value);
  return Number.isFinite(number)
    ? Math.round(number).toLocaleString()
    : '0';
}

function formatBytes(value) {
  let size = Number(value);
  if (!Number.isFinite(size) || size <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit += 1;
  }

  return `${size.toLocaleString(undefined, { maximumFractionDigits: unit === 0 ? 0 : 1 })} ${units[unit]}`;
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

function syncInput(input, value) {
  if (value === undefined || value === null) return;
  const text = String(value);
  if (input.value !== text && document.activeElement !== input) {
    input.value = text;
  }
}
