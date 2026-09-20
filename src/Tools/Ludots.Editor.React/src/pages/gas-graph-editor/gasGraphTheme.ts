/**
 * Graph canvas palette aliases the studio zinc + chart tokens. No local hex.
 */
import { STUDIO_THEME } from '../authoring-studio/authoringTheme';

export const GAS_GRAPH_THEME = {
  canvasBg: STUDIO_THEME.bg,
  canvasDot: STUDIO_THEME.fill,
  minimapBg: STUDIO_THEME.bg,
  minimapMask: 'color-mix(in srgb, var(--studio-bg) 55%, transparent)',

  nodeBg: STUDIO_THEME.surface,
  nodeHeader: STUDIO_THEME.elevated,
  nodeBorder: STUDIO_THEME.fill,
  nodeBorderSelected: STUDIO_THEME.silver,
  nodeText: STUDIO_THEME.label,
  nodeMuted: STUDIO_THEME.muted,

  eventHeader: 'color-mix(in srgb, var(--studio-red) 42%, var(--studio-bg))',
  eventAccent: STUDIO_THEME.red,
  eventBorder: STUDIO_THEME.red,

  valueHeader: 'color-mix(in srgb, var(--studio-blue) 42%, var(--studio-bg))',
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
