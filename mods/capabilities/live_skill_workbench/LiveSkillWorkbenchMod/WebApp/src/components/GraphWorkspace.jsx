import { memo, useMemo } from 'react';
import {
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  MarkerType
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';

function GraphWorkspaceComponent({ graph }) {
  const nodes = useMemo(() => {
    if (!graph?.nodes?.length) {
      return [];
    }
    return graph.nodes.map((node) => ({
      id: node.id,
      position: { x: node.x, y: node.y },
      data: { label: node.label },
      style: {
        border: '1px solid #9aa3ad',
        borderRadius: 4,
        background: '#f7f8fa',
        fontSize: 12,
        padding: '6px 10px',
        width: 140
      }
    }));
  }, [graph]);

  const edges = useMemo(() => {
    if (!graph?.edges?.length) {
      return [];
    }
    return graph.edges.map((edge) => ({
      id: edge.id,
      source: edge.source,
      target: edge.target,
      label: edge.label,
      markerEnd: { type: MarkerType.ArrowClosed, width: 14, height: 14 },
      style: { stroke: '#5b6b7a' },
      labelStyle: { fill: '#5b6b7a', fontSize: 10 }
    }));
  }, [graph]);

  if (!graph?.nodes?.length) {
    return <div className="lsw-empty">当前条目没有 Graph 描述。</div>;
  }

  return (
    <div className="lsw-graph">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        fitView
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        proOptions={{ hideAttribution: true }}
      >
        <Background gap={16} color="#d7dce3" />
        <MiniMap pannable zoomable />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
}

export const GraphWorkspace = memo(GraphWorkspaceComponent);
