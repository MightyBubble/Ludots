import React, { useMemo } from 'react';
import {
  pinX,
  pinY,
  type CanvasEdge,
  type CanvasNode,
  type PanelTemplate,
  type PanelVariable,
  type ValueKind,
} from './model';

const NODE_W = 168;
const PANEL_W = 220;

function kindColor(kind: ValueKind | undefined): string {
  switch (kind) {
    case 'Float':
      return '#6fcf97';
    case 'Int':
      return '#6aa8b8';
    case 'Text':
      return '#d4b483';
    case 'Bool':
      return '#d9785c';
    default:
      return '#8b9a90';
  }
}

function nodeWidth(n: CanvasNode): number {
  return n.kind === 'panel' ? PANEL_W : NODE_W;
}

function nodeHeight(n: CanvasNode): number {
  const ports = Math.max(n.ins?.length ?? 0, n.outs?.length ?? 1, 1);
  return n.kind === 'panel' ? 56 + ports * 28 + 16 : 44 + ports * 22 + 12;
}

export function ShaderGraphCanvas({
  tpl,
  activeVar,
  onSelectVar,
}: {
  tpl: PanelTemplate;
  activeVar: string | null;
  onSelectVar: (id: string) => void;
}) {
  const varById = useMemo(() => {
    const m = new Map<string, PanelVariable>();
    for (const v of tpl.variables) m.set(v.id, v);
    return m;
  }, [tpl.variables]);

  const nodeById = useMemo(() => {
    const m = new Map<string, CanvasNode>();
    for (const n of tpl.nodes) m.set(n.id, n);
    return m;
  }, [tpl.nodes]);

  const bounds = useMemo(() => {
    let maxX = 0;
    let maxY = 0;
    for (const n of tpl.nodes) {
      maxX = Math.max(maxX, n.x + nodeWidth(n) + 40);
      maxY = Math.max(maxY, n.y + nodeHeight(n) + 40);
    }
    return { w: Math.max(1100, maxX), h: Math.max(420, maxY) };
  }, [tpl.nodes]);

  const edgePaths = tpl.edges.map((edge: CanvasEdge) => {
    const from = nodeById.get(edge.from);
    const to = nodeById.get(edge.to);
    if (!from || !to) return null;
    const x1 = pinX(from, 'out');
    const y1 = pinY(from, edge.fromPort, 'out');
    const x2 = pinX(to, 'in');
    const y2 = pinY(to, edge.toPort, 'in');
    const mid = (x1 + x2) / 2;
    const d = `M ${x1} ${y1} C ${mid} ${y1}, ${mid} ${y2}, ${x2} ${y2}`;
    const lit = activeVar != null && edge.toPort === activeVar && to.kind === 'panel';
    const stroke = kindColor(edge.valueKind ?? varById.get(edge.toPort)?.valueKind);
    return (
      <path
        key={edge.id}
        d={d}
        fill="none"
        stroke={stroke}
        strokeWidth={lit ? 3.2 : 2}
        opacity={lit ? 1 : 0.75}
        className={lit ? 'upa-edge is-lit' : 'upa-edge'}
      />
    );
  });

  return (
    <div className="upa-canvas-wrap">
      <div className="upa-canvas-legend">
        <span>
          右侧 <strong>Panel</strong> = Shader Graph 的「多引脚汇入」
        </span>
        <span className="upa-canvas-legend-mute">
          看得见像 PanelNode；落盘是 outputs[] / bindings，不是 GraphNodeOp
        </span>
      </div>
      <div className="upa-canvas" style={{ width: bounds.w, height: bounds.h }}>
        <svg className="upa-canvas-edges" width={bounds.w} height={bounds.h}>
          {edgePaths}
        </svg>
        {tpl.nodes.map((n) => {
          const w = nodeWidth(n);
          const h = nodeHeight(n);
          const isPanel = n.kind === 'panel';
          return (
            <div
              key={n.id}
              className={`upa-gnode kind-${n.kind}`}
              style={{ left: n.x, top: n.y, width: w, minHeight: h }}
            >
              <div className="upa-gnode-h">
                <span className="upa-gnode-kind">
                  {isPanel ? 'PANEL SINK' : n.kind === 'intent' ? 'INTENT (UNPAID)' : n.kind}
                </span>
                <span className="upa-gnode-title">{n.title}</span>
                <span className="upa-gnode-detail">{n.detail}</span>
              </div>
              {isPanel ? (
                <ul className="upa-pins">
                  {(n.ins ?? []).map((port) => {
                    const v = varById.get(port);
                    const lit = activeVar === port;
                    return (
                      <li key={port}>
                        <button
                          type="button"
                          className={`upa-pin in ${lit ? 'is-lit' : ''}`}
                          onClick={() => onSelectVar(port)}
                        >
                          <i style={{ background: kindColor(v?.valueKind) }} />
                          <span className="upa-pin-name">{port}</span>
                          <span className="upa-pin-type">{v?.valueKind ?? '?'}</span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              ) : (
                <>
                  {(n.ins ?? []).length > 0 ? (
                    <ul className="upa-pins outs-left">
                      {(n.ins ?? []).map((port) => (
                        <li key={port} className="upa-pin-label in">
                          <i />
                          {port}
                        </li>
                      ))}
                    </ul>
                  ) : null}
                  <ul className="upa-pins outs-right">
                    {(n.outs ?? ['out']).map((port) => (
                      <li key={port} className="upa-pin-label out">
                        {port}
                        <i />
                      </li>
                    ))}
                  </ul>
                </>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
