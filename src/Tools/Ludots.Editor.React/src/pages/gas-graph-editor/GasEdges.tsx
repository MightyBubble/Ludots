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
    ? 3.8 + intensity * 2.2
    : Number(style?.strokeWidth ?? 2.25);
  const beadDur = `${0.42 + (1 - intensity) * 0.28}s`;

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
          opacity: live ? 0.7 + intensity * 0.3 : style?.opacity ?? 0.9,
          strokeDasharray: undefined,
          filter: live ? `drop-shadow(0 0 ${6 + intensity * 8}px rgba(251, 191, 36, ${0.45 + intensity * 0.4}))` : undefined,
        }}
      />
      {live ? (
        <>
          <circle r={4.8} fill={GAS_GRAPH_THEME.execBead} className="gas-control-bead">
            <animateMotion dur={beadDur} repeatCount="indefinite" path={path} rotate="auto" />
          </circle>
          <circle r={2.4} fill="#f59e0b" className="gas-control-bead" opacity={0.85}>
            <animateMotion dur={beadDur} begin="0.18s" repeatCount="indefinite" path={path} rotate="auto" />
          </circle>
        </>
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
