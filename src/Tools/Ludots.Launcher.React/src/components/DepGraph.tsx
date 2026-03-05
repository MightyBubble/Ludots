import { useMemo } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { cn } from "@/lib/utils";
import type { ModInfo } from "@/lib/api";

interface LayoutNode {
  mod: ModInfo;
  x: number;
  y: number;
  level: number;
}

function topoLayout(mods: ModInfo[], active: Set<string>): LayoutNode[] {
  const relevant = mods.filter((m) => active.has(m.id));
  const byId = new Map(relevant.map((m) => [m.id, m]));

  const indeg = new Map<string, number>();
  for (const m of relevant) indeg.set(m.id, 0);
  for (const m of relevant) {
    for (const dep of Object.keys(m.dependencies)) {
      if (indeg.has(dep)) indeg.set(dep, (indeg.get(dep) ?? 0) + 1);
    }
  }

  const levels: string[][] = [];
  const placed = new Set<string>();

  while (placed.size < relevant.length) {
    const layer: string[] = [];
    for (const m of relevant) {
      if (placed.has(m.id)) continue;
      const deps = Object.keys(m.dependencies).filter((d) => byId.has(d));
      if (deps.every((d) => placed.has(d))) layer.push(m.id);
    }
    if (layer.length === 0) break;
    layer.sort((a, b) => (byId.get(a)?.priority ?? 0) - (byId.get(b)?.priority ?? 0));
    levels.push(layer);
    for (const id of layer) placed.add(id);
  }

  const nodeW = 140;
  const nodeH = 50;
  const gapX = 20;
  const gapY = 24;

  const nodes: LayoutNode[] = [];
  for (let li = 0; li < levels.length; li++) {
    const layer = levels[li];
    const totalW = layer.length * nodeW + (layer.length - 1) * gapX;
    const startX = -totalW / 2;
    for (let ni = 0; ni < layer.length; ni++) {
      const mod = byId.get(layer[ni])!;
      nodes.push({
        mod,
        x: startX + ni * (nodeW + gapX) + nodeW / 2,
        y: li * (nodeH + gapY),
        level: li,
      });
    }
  }

  return nodes;
}

export function DepGraph() {
  const { mods, activeMods, selectedModId, selectMod } = useLauncherStore();

  const nodes = useMemo(() => topoLayout(mods, activeMods), [mods, activeMods]);
  const posMap = useMemo(
    () => new Map(nodes.map((n) => [n.mod.id, n])),
    [nodes]
  );

  if (nodes.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-gray-500 text-sm">
        No active mods
      </div>
    );
  }

  const minX = Math.min(...nodes.map((n) => n.x)) - 80;
  const maxX = Math.max(...nodes.map((n) => n.x)) + 80;
  const maxY = Math.max(...nodes.map((n) => n.y)) + 50;
  const w = maxX - minX;
  const h = maxY + 16;

  const edges: { from: LayoutNode; to: LayoutNode }[] = [];
  for (const node of nodes) {
    for (const dep of Object.keys(node.mod.dependencies)) {
      const target = posMap.get(dep);
      if (target) edges.push({ from: node, to: target });
    }
  }

  return (
    <div className="overflow-auto h-full p-2">
      <svg
        width={w}
        height={h}
        viewBox={`${minX} 0 ${w} ${h}`}
        className="mx-auto"
      >
        <defs>
          <marker
            id="arrow"
            viewBox="0 0 10 10"
            refX="10"
            refY="5"
            markerWidth="6"
            markerHeight="6"
            orient="auto-start-reverse"
          >
            <path d="M 0 0 L 10 5 L 0 10 z" fill="#555" />
          </marker>
        </defs>

        {edges.map((e, i) => (
          <line
            key={i}
            x1={e.from.x}
            y1={e.from.y + 18}
            x2={e.to.x}
            y2={e.to.y - 2}
            stroke="#444"
            strokeWidth={1.5}
            markerEnd="url(#arrow)"
          />
        ))}

        {nodes.map((n) => {
          const sel = selectedModId === n.mod.id;
          return (
            <g key={n.mod.id} onClick={() => selectMod(sel ? null : n.mod.id)} className="cursor-pointer">
              <rect
                x={n.x - 64}
                y={n.y - 14}
                width={128}
                height={32}
                rx={6}
                className={cn(
                  "transition-all",
                  sel ? "fill-[#0f7dff33] stroke-[#0f7dff]" : "fill-[#22223a] stroke-[#333]"
                )}
                strokeWidth={sel ? 2 : 1}
              />
              <text
                x={n.x}
                y={n.y + 4}
                textAnchor="middle"
                className="fill-gray-200 text-[11px] font-medium select-none"
              >
                {n.mod.name.length > 16
                  ? n.mod.name.slice(0, 15) + "…"
                  : n.mod.name}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
