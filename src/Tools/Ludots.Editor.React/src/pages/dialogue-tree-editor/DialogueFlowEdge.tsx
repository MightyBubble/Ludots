import { BaseEdge, getBezierPath, type Edge, type EdgeProps } from '@xyflow/react';
import type { DialogueEdgeKind } from './dialogueTreeModel';

export type DialogueFlowEdgeData = {
  kind: DialogueEdgeKind;
};

export function DialogueFlowEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style,
  markerEnd,
}: EdgeProps<Edge<DialogueFlowEdgeData, 'dialogueFlow'>>) {
  const upward = targetY + 8 < sourceY;
  const [path] = getBezierPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    curvature: upward ? 0.42 : 0.25,
  });
  return <BaseEdge id={id} path={path} markerEnd={markerEnd} style={style} />;
}
