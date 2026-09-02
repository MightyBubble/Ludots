import React from 'react';
import {
  BaseEdge,
  EdgeLabelRenderer,
  getBezierPath,
  type Edge,
  type EdgeProps,
} from '@xyflow/react';
import { GAS_GRAPH_THEME } from './gasGraphTheme';

export type GasEdgeLiveData = {
  kind: 'control' | 'value';
  synthetic?: boolean;
  live?: boolean;
  intensity?: number;
  liveValue?: string | null;
};

export type GasControlEdgeType = Edge<GasEdgeLiveData, 'gasControl'>;
export type GasValueEdgeType = Edge<GasEdgeLiveData, 'gasValue'>;

export function GasControlEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style,
  markerEnd,
  data,
}: EdgeProps<GasControlEdgeType>) {
  const [path] = getBezierPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
  });
  const live = Boolean(data?.live);
  const intensity = Math.max(0, Math.min(1, data?.intensity ?? 0));
  const stroke = live ? GAS_GRAPH_THEME.execLive : (style?.stroke as string) ?? GAS_GRAPH_THEME.execIdle;
  const strokeWidth = live
    ? 3.2 + intensity * 1.6
    : Number(style?.strokeWidth ?? 2);
  const beadDur = `${0.48 + (1 - intensity) * 0.35}s`;

  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        markerEnd={markerEnd}
        style={{
          ...style,
          stroke,
          strokeWidth,
          opacity: live ? 0.55 + intensity * 0.45 : style?.opacity ?? 0.85,
          strokeDasharray: undefined,
          filter: live ? `drop-shadow(0 0 ${4 + intensity * 6}px rgba(251, 191, 36, ${0.35 + intensity * 0.35}))` : undefined,
        }}
      />
      {live ? (
        <circle r={3.6} fill={GAS_GRAPH_THEME.execBead} className="gas-control-bead">
          <animateMotion dur={beadDur} repeatCount="indefinite" path={path} rotate="auto" />
        </circle>
      ) : null}
    </>
  );
}

export function GasValueEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style,
  markerEnd,
  label,
  data,
}: EdgeProps<GasValueEdgeType>) {
  const [path, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
  });
  const live = Boolean(data?.live);
  const liveValue = data?.liveValue?.trim() || null;
  const display = liveValue ?? (typeof label === 'string' ? label : null);
  const stroke = live
    ? GAS_GRAPH_THEME.dataLive
    : (style?.stroke as string) ?? GAS_GRAPH_THEME.dataIdle;
  const strokeWidth = live ? 1.65 : Number(style?.strokeWidth ?? 1.5);

  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        markerEnd={markerEnd}
        style={{
          ...style,
          stroke,
          strokeWidth,
          opacity: live ? 0.75 : style?.opacity ?? 0.7,
          strokeDasharray: undefined,
          filter: undefined,
        }}
      />
      {display ? (
        <EdgeLabelRenderer>
          <div
            className={`gas-value-edge-label ${live || liveValue ? 'is-live' : ''}`}
            style={{
              transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`,
            }}
          >
            {display}
          </div>
        </EdgeLabelRenderer>
      ) : null}
    </>
  );
}

export const gasEdgeTypes = {
  gasControl: GasControlEdge,
  gasValue: GasValueEdge,
};
