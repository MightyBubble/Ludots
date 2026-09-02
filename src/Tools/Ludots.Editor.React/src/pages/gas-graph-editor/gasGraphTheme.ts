/**
 * Graph canvas palette — zinc agent-kit dark (n8n / assistant-ui / shadcn AI Elements),
 * Flow Canvas exec semantics (warm amber control, quiet data wires).
 */
export const GAS_GRAPH_THEME = {
  canvasBg: '#09090b',
  canvasDot: '#3f3f46',
  minimapBg: '#09090b',
  minimapMask: 'rgba(9, 9, 11, 0.55)',

  nodeBg: '#18181b',
  nodeHeader: '#27272a',
  nodeBorder: '#3f3f46',
  nodeBorderSelected: '#a1a1aa',
  nodeText: '#fafafa',
  nodeMuted: '#a1a1aa',

  eventHeader: '#881337',
  eventAccent: '#fb7185',
  eventBorder: '#9f1239',

  valueHeader: '#0c4a6e',
  valueAccent: '#7dd3fc',
  valueBorder: '#0369a1',

  execAccent: '#fbbf24',
  execIdle: '#a8a29e',
  execLive: '#fbbf24',
  execLiveHot: '#f59e0b',
  execBead: '#fff7ed',

  dataIdle: '#64748b',
  dataLive: '#bae6fd',
  dataLabel: '#e2e8f0',
  listAccent: '#2dd4bf',

  liveCurrent: '#fbbf24',
  liveHot: '#fcd34d',
  liveTrail: '#a8a29e',
} as const;

export type GasGraphTheme = typeof GAS_GRAPH_THEME;
