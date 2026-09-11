/**
 * Graph canvas palette — studio HIG dark (silver chrome, blue data, yellow exec, red events).
 */
import { STUDIO_THEME } from '../authoring-studio/authoringTheme';

export const GAS_GRAPH_THEME = {
  canvasBg: STUDIO_THEME.bg,
  canvasDot: STUDIO_THEME.fill,
  minimapBg: STUDIO_THEME.bg,
  minimapMask: 'rgba(28, 28, 30, 0.55)',

  nodeBg: STUDIO_THEME.surface,
  nodeHeader: STUDIO_THEME.elevated,
  nodeBorder: STUDIO_THEME.fill,
  nodeBorderSelected: STUDIO_THEME.silver,
  nodeText: STUDIO_THEME.label,
  nodeMuted: STUDIO_THEME.muted,

  eventHeader: '#8b1a16',
  eventAccent: STUDIO_THEME.red,
  eventBorder: STUDIO_THEME.red,

  valueHeader: '#003a73',
  valueAccent: STUDIO_THEME.blue,
  valueBorder: STUDIO_THEME.blue,

  execAccent: STUDIO_THEME.yellow,
  execIdle: STUDIO_THEME.muted,
  execLive: STUDIO_THEME.yellow,
  execLiveHot: STUDIO_THEME.yellow,
  execBead: STUDIO_THEME.label,

  dataIdle: STUDIO_THEME.muted,
  dataLive: STUDIO_THEME.blue,
  dataLabel: STUDIO_THEME.label,
  listAccent: STUDIO_THEME.blue,

  liveCurrent: STUDIO_THEME.yellow,
  liveHot: STUDIO_THEME.yellow,
  liveTrail: STUDIO_THEME.muted,
} as const;

export type GasGraphTheme = typeof GAS_GRAPH_THEME;
