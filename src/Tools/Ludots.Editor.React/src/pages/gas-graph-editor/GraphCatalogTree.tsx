import React from 'react';
import { ChevronDown, ChevronRight, FileWarning, Folder, Workflow } from 'lucide-react';

export type CatalogGraph = {
  id: string;
  kind: string;
};

export type CatalogMod = {
  id: string;
  name: string;
  path: string;
  error?: string | null;
  graphs: CatalogGraph[];
};

type TreeNode = {
  key: string;
  label: string;
  graph?: CatalogGraph;
  children: TreeNode[];
};

function buildVirtualTree(graphs: CatalogGraph[]): TreeNode[] {
  type Mutable = { label: string; graph?: CatalogGraph; children: Map<string, Mutable> };
  const root: Mutable = { label: '', children: new Map() };
  for (const graph of graphs) {
    const parts = graph.id.split('.').filter((part) => part.length > 0);
    if (parts.length === 0) {
      throw new Error(`Graph id '${graph.id}' cannot be empty.`);
    }
    let cursor = root;
    for (let i = 0; i < parts.length; i++) {
      const part = parts[i]!;
      let next = cursor.children.get(part);
      if (!next) {
        next = { label: part, children: new Map() };
        cursor.children.set(part, next);
      }
      cursor = next;
      if (i === parts.length - 1) cursor.graph = graph;
    }
  }

  const toNodes = (items: Map<string, Mutable>, prefix: string): TreeNode[] =>
    [...items.values()]
      .sort((a, b) => a.label.localeCompare(b.label))
      .map((item) => {
        const key = prefix.length === 0 ? item.label : `${prefix}.${item.label}`;
        return {
          key,
          label: item.label,
          graph: item.graph,
          children: toNodes(item.children, key),
        };
      });

  return toNodes(root.children, '');
}

function filterTree(nodes: TreeNode[], query: string): TreeNode[] {
  if (!query) return nodes;
  const keep: TreeNode[] = [];
  for (const node of nodes) {
    const children = filterTree(node.children, query);
    const selfMatch = node.key.toLocaleLowerCase().includes(query) || node.label.toLocaleLowerCase().includes(query);
    if (selfMatch || children.length > 0) {
      keep.push({ ...node, children: selfMatch ? node.children : children });
    }
  }
  return keep;
}

function TreeBranch({
  node,
  selectedGraphId,
  expanded,
  onToggle,
  onSelect,
}: {
  node: TreeNode;
  selectedGraphId: string;
  expanded: Set<string>;
  onToggle: (key: string) => void;
  onSelect: (graphId: string) => void;
}) {
  const hasChildren = node.children.length > 0;
  const isOpen = expanded.has(node.key);
  const isSelected = node.graph?.id === selectedGraphId;
  return (
    <div>
      <div className="flex items-center gap-1">
        {hasChildren ? (
          <button
            type="button"
            aria-label={isOpen ? `Collapse ${node.label}` : `Expand ${node.label}`}
            onClick={() => onToggle(node.key)}
            className="rounded p-0.5 text-slate-500 hover:bg-slate-800 hover:text-slate-200"
          >
            {isOpen ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
          </button>
        ) : (
          <span className="w-4" />
        )}
        {node.graph ? (
          <button
            type="button"
            onClick={() => onSelect(node.graph!.id)}
            className={`flex min-w-0 flex-1 items-center gap-1 rounded px-1 py-0.5 text-left text-[11px] ${
              isSelected ? 'bg-sky-900/70 text-sky-100' : 'text-slate-300 hover:bg-slate-800'
            }`}
          >
            <Workflow size={11} className="shrink-0 text-amber-300" aria-hidden="true" />
            <span className="truncate font-mono">{node.label}</span>
            <span className="ml-auto shrink-0 text-[9px] uppercase text-slate-500">{node.graph.kind}</span>
          </button>
        ) : (
          <button
            type="button"
            onClick={() => onToggle(node.key)}
            className="flex min-w-0 flex-1 items-center gap-1 rounded px-1 py-0.5 text-left text-[11px] text-slate-400 hover:bg-slate-800"
          >
            <Folder size={11} className="shrink-0 text-slate-500" aria-hidden="true" />
            <span className="truncate">{node.label}</span>
          </button>
        )}
      </div>
      {hasChildren && isOpen ? (
        <div className="ml-3 border-l border-slate-800 pl-1">
          {node.children.map((child) => (
            <TreeBranch
              key={child.key}
              node={child}
              selectedGraphId={selectedGraphId}
              expanded={expanded}
              onToggle={onToggle}
              onSelect={onSelect}
            />
          ))}
        </div>
      ) : null}
    </div>
  );
}

export function GraphCatalogTree({
  mods,
  selectedModId,
  selectedGraphId,
  status,
  onSelect,
}: {
  mods: CatalogMod[];
  selectedModId: string;
  selectedGraphId: string;
  status: string;
  onSelect: (modId: string, graphId: string) => void;
}) {
  const [query, setQuery] = React.useState('');
  const [expandedMods, setExpandedMods] = React.useState<Set<string>>(() => new Set([selectedModId]));
  const [expandedFolders, setExpandedFolders] = React.useState<Set<string>>(() => {
    const next = new Set<string>();
    const parts = selectedGraphId.split('.').filter((part) => part.length > 0);
    let path = '';
    for (const part of parts) {
      path = path.length === 0 ? part : `${path}.${part}`;
      next.add(path);
    }
    return next;
  });

  React.useEffect(() => {
    setExpandedMods((previous) => {
      if (previous.has(selectedModId)) return previous;
      const next = new Set(previous);
      next.add(selectedModId);
      return next;
    });
  }, [selectedModId]);

  const normalizedQuery = query.trim().toLocaleLowerCase();

  return (
    <aside className="flex min-h-0 flex-col border-r border-slate-800 bg-slate-950/80">
      <div className="border-b border-slate-800 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
        Graphs
      </div>
      <div className="border-b border-slate-800 px-2 py-2">
        <input
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Filter mods / graphs"
          aria-label="Filter graph catalog"
          className="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px] text-slate-100 outline-none placeholder:text-slate-600"
        />
      </div>
      <div className="min-h-0 flex-1 overflow-auto px-2 py-2">
        {mods.length === 0 ? (
          <div className="px-1 text-[11px] text-slate-500">{status || 'No mods with graphs.json.'}</div>
        ) : (
          mods
            .filter((mod) => {
              if (!normalizedQuery) return true;
              if (mod.id.toLocaleLowerCase().includes(normalizedQuery) || mod.name.toLocaleLowerCase().includes(normalizedQuery)) {
                return true;
              }
              return filterTree(buildVirtualTree(mod.graphs), normalizedQuery).length > 0;
            })
            .map((mod) => {
              const open = expandedMods.has(mod.id) || normalizedQuery.length > 0;
              const tree = filterTree(buildVirtualTree(mod.graphs), normalizedQuery);
              return (
                <div key={mod.id} className="mb-2">
                  <button
                    type="button"
                    onClick={() => {
                      setExpandedMods((previous) => {
                        const next = new Set(previous);
                        if (next.has(mod.id)) next.delete(mod.id);
                        else next.add(mod.id);
                        return next;
                      });
                    }}
                    className={`flex w-full items-center gap-1 rounded px-1 py-1 text-left text-[11px] ${
                      selectedModId === mod.id ? 'bg-slate-800 text-white' : 'text-slate-300 hover:bg-slate-800'
                    }`}
                  >
                    {open ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
                    <span className="truncate font-semibold">{mod.name}</span>
                    <span className="ml-auto text-[9px] text-slate-500">{mod.graphs.length}</span>
                  </button>
                  {mod.error ? (
                    <div className="mt-1 flex items-start gap-1 px-2 text-[10px] text-amber-300">
                      <FileWarning size={11} className="mt-0.5 shrink-0" aria-hidden="true" />
                      <span>{mod.error}</span>
                    </div>
                  ) : null}
                  {open ? (
                    <div className="mt-1 ml-1">
                      {tree.map((node) => (
                        <TreeBranch
                          key={node.key}
                          node={node}
                          selectedGraphId={selectedGraphId}
                          expanded={expandedFolders}
                          onToggle={(key) => {
                            setExpandedFolders((previous) => {
                              const next = new Set(previous);
                              if (next.has(key)) next.delete(key);
                              else next.add(key);
                              return next;
                            });
                          }}
                          onSelect={(graphId) => onSelect(mod.id, graphId)}
                        />
                      ))}
                    </div>
                  ) : null}
                </div>
              );
            })
        )}
      </div>
    </aside>
  );
}
