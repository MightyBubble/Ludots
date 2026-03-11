import type { ModInfo } from "@/lib/api";

interface DependencyGraphPanelProps {
  mods: ModInfo[];
  activeMods: Set<string>;
  selectedModId: string | null;
}

interface GraphNode {
  id: string;
  name: string;
  version: string;
  buildState: ModInfo["buildState"];
  isMissing: boolean;
  isSelected: boolean;
  x: number;
  y: number;
}

interface GraphEdge {
  fromId: string;
  toId: string;
  label: string;
  isError: boolean;
}

const NODE_WIDTH = 168;
const NODE_HEIGHT = 52;
const COLUMN_GAP = 208;
const ROW_GAP = 76;

export function DependencyGraphPanel({ mods, activeMods, selectedModId }: DependencyGraphPanelProps) {
  const graph = buildGraph(mods, activeMods, selectedModId);
  if (!graph) {
    return (
      <div className="rounded-2xl border border-dashed border-bg-border bg-bg/50 p-4 text-xs text-gray-500">
        Enable mods to see dependency load order and graph structure.
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="rounded-2xl border border-bg-border bg-bg/40 p-3">
        <div className="mb-2 flex items-center justify-between text-[11px] uppercase tracking-[0.25em] text-gray-500">
          <span>Active Graph</span>
          <span>{graph.nodes.length} nodes</span>
        </div>

        <div className="overflow-x-auto">
          <svg
            viewBox={`0 0 ${graph.width} ${graph.height}`}
            className="min-w-full"
            role="img"
            aria-label="Active mod dependency graph"
          >
            <defs>
              <marker
                id="dependency-arrow"
                markerWidth="8"
                markerHeight="8"
                refX="7"
                refY="4"
                orient="auto"
                markerUnits="strokeWidth"
              >
                <path d="M0,0 L8,4 L0,8 z" fill="#6b7280" />
              </marker>
            </defs>

            {graph.edges.map((edge) => {
              const from = graph.nodesById.get(edge.fromId);
              const to = graph.nodesById.get(edge.toId);
              if (!from || !to) {
                return null;
              }

              const x1 = from.x + NODE_WIDTH;
              const y1 = from.y + NODE_HEIGHT / 2;
              const x2 = to.x;
              const y2 = to.y + NODE_HEIGHT / 2;
              const midX = (x1 + x2) / 2;
              const midY = (y1 + y2) / 2 - 6;

              return (
                <g key={`${edge.fromId}:${edge.toId}`}>
                  <line
                    x1={x1}
                    y1={y1}
                    x2={x2}
                    y2={y2}
                    stroke={edge.isError ? "#f87171" : "#6b7280"}
                    strokeWidth={edge.isError ? 2.2 : 1.6}
                    markerEnd="url(#dependency-arrow)"
                  />
                  {edge.label ? (
                    <text
                      x={midX}
                      y={midY}
                      textAnchor="middle"
                      fontSize="10"
                      fill={edge.isError ? "#fca5a5" : "#9ca3af"}
                    >
                      {edge.label}
                    </text>
                  ) : null}
                </g>
              );
            })}

            {graph.nodes.map((node) => (
              <g key={node.id} transform={`translate(${node.x}, ${node.y})`}>
                <rect
                  width={NODE_WIDTH}
                  height={NODE_HEIGHT}
                  rx="14"
                  fill={node.isMissing ? "#3f1d1d" : node.isSelected ? "#1f3342" : "#161c24"}
                  stroke={node.isMissing ? "#f87171" : node.isSelected ? "#38bdf8" : "#334155"}
                  strokeWidth={node.isSelected ? 2 : 1}
                />
                <text x="14" y="21" fontSize="12" fill="#f8fafc" fontWeight="700">
                  {node.name}
                </text>
                <text x="14" y="38" fontSize="10" fill={node.isMissing ? "#fca5a5" : "#94a3b8"}>
                  {node.isMissing ? "missing" : `v${node.version} • ${formatBuildState(node.buildState)}`}
                </text>
              </g>
            ))}
          </svg>
        </div>
      </div>

      <div className="rounded-2xl border border-bg-border bg-bg/40 p-3">
        <div className="mb-2 text-[11px] uppercase tracking-[0.25em] text-gray-500">Load Order</div>
        <div className="flex flex-wrap gap-2">
          {graph.loadOrder.map((modId, index) => (
            <span
              key={modId}
              className="rounded-full border border-bg-border bg-bg px-2.5 py-1 text-[11px] text-gray-300"
            >
              {index + 1}. {modId}
            </span>
          ))}
        </div>
      </div>
    </div>
  );
}

function buildGraph(mods: ModInfo[], activeMods: Set<string>, selectedModId: string | null) {
  if (activeMods.size === 0) {
    return null;
  }

  const byId = new Map(mods.map((mod) => [mod.id, mod] as const));
  const nodeIds = new Set(activeMods);
  const edges: GraphEdge[] = [];

  for (const activeModId of activeMods) {
    const activeMod = byId.get(activeModId);
    if (!activeMod) {
      continue;
    }

    for (const [dependencyId, range] of Object.entries(activeMod.dependencies)) {
      nodeIds.add(dependencyId);
      edges.push({
        fromId: dependencyId,
        toId: activeModId,
        label: range,
        isError: !byId.has(dependencyId),
      });
    }
  }

  const depthCache = new Map<string, number>();
  const visiting = new Set<string>();

  const computeDepth = (modId: string): number => {
    if (depthCache.has(modId)) {
      return depthCache.get(modId) ?? 0;
    }

    if (visiting.has(modId)) {
      return 0;
    }

    visiting.add(modId);
    const mod = byId.get(modId);
    const depth = !mod || Object.keys(mod.dependencies).length === 0
      ? 0
      : Math.max(...Object.keys(mod.dependencies).map((dependencyId) => computeDepth(dependencyId) + 1));
    visiting.delete(modId);
    depthCache.set(modId, depth);
    return depth;
  };

  const columns = new Map<number, string[]>();
  for (const modId of nodeIds) {
    const depth = computeDepth(modId);
    const bucket = columns.get(depth) ?? [];
    bucket.push(modId);
    columns.set(depth, bucket);
  }

  for (const bucket of columns.values()) {
    bucket.sort((left, right) => {
      const leftMod = byId.get(left);
      const rightMod = byId.get(right);
      const leftPriority = leftMod?.priority ?? Number.MAX_SAFE_INTEGER;
      const rightPriority = rightMod?.priority ?? Number.MAX_SAFE_INTEGER;
      if (leftPriority !== rightPriority) {
        return leftPriority - rightPriority;
      }

      return left.localeCompare(right);
    });
  }

  const sortedDepths = [...columns.keys()].sort((left, right) => left - right);
  const nodes: GraphNode[] = [];
  for (const depth of sortedDepths) {
    const bucket = columns.get(depth) ?? [];
    bucket.forEach((modId, index) => {
      const mod = byId.get(modId);
      nodes.push({
        id: modId,
        name: mod?.name ?? modId,
        version: mod?.version ?? "missing",
        buildState: mod?.buildState ?? "Failed",
        isMissing: !mod,
        isSelected: modId === selectedModId,
        x: 20 + depth * COLUMN_GAP,
        y: 18 + index * ROW_GAP,
      });
    });
  }

  const nodesById = new Map(nodes.map((node) => [node.id, node] as const));
  const maxColumnLength = Math.max(...[...columns.values()].map((bucket) => bucket.length), 1);
  const width = Math.max(720, 40 + sortedDepths.length * COLUMN_GAP);
  const height = Math.max(220, 36 + maxColumnLength * ROW_GAP);
  const loadOrder = topoSort(activeMods, mods);

  return { nodes, nodesById, edges, width, height, loadOrder };
}

function topoSort(modIds: Set<string>, mods: ModInfo[]): string[] {
  const byId = new Map(mods.map((mod) => [mod.id, mod] as const));
  const selected = new Set(modIds);
  const ordered: string[] = [];
  const visited = new Set<string>();

  const visit = (modId: string) => {
    if (visited.has(modId) || !selected.has(modId)) {
      return;
    }

    visited.add(modId);
    const mod = byId.get(modId);
    if (mod) {
      Object.keys(mod.dependencies)
        .sort((left, right) => left.localeCompare(right))
        .forEach(visit);
    }

    ordered.push(modId);
  };

  [...selected].sort((left, right) => left.localeCompare(right)).forEach(visit);
  return ordered;
}

function formatBuildState(buildState: ModInfo["buildState"]): string {
  switch (buildState) {
    case "Succeeded":
      return "built";
    case "Outdated":
      return "outdated";
    case "NoProject":
      return "no project";
    case "Idle":
      return "not built";
    default:
      return buildState.toLowerCase();
  }
}
