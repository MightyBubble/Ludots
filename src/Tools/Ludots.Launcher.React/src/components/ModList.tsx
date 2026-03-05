import { useLauncherStore, type ViewMode } from "@/stores/launcherStore";
import { cn } from "@/lib/utils";
import { thumbnailUrl } from "@/lib/api";
import {
  Search, LayoutGrid, List, Check, AlertTriangle, User, Tag,
} from "lucide-react";

export function ModList() {
  const {
    mods, activeMods, selectedModId, selectMod, toggleMod,
    viewMode, setViewMode, search, setSearch,
  } = useLauncherStore();

  const byId = new Map(mods.map(m => [m.id, m]));
  const filtered = mods.filter(m =>
    m.name.toLowerCase().includes(search.toLowerCase()) ||
    m.description?.toLowerCase().includes(search.toLowerCase()) ||
    m.tags?.some(t => t.toLowerCase().includes(search.toLowerCase()))
  );

  return (
    <div className="flex-1 flex flex-col min-w-0 border-r border-bg-border">
      {/* Toolbar */}
      <div className="flex items-center gap-2 px-3 py-2 border-b border-bg-border bg-bg-panel/50">
        <div className="relative flex-1">
          <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-500" />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search mods..."
            className="w-full bg-bg border border-bg-border rounded-lg pl-8 pr-3 py-1.5 text-xs focus:outline-none focus:border-accent/50 transition" />
        </div>
        <ViewToggle mode={viewMode} onChange={setViewMode} />
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-2">
        {viewMode === "card" ? (
          <div className="grid grid-cols-[repeat(auto-fill,minmax(260px,1fr))] gap-2">
            {filtered.map(mod => <CardItem key={mod.id} mod={mod} active={activeMods.has(mod.id)}
              selected={selectedModId === mod.id} missingDeps={getMissingDeps(mod, activeMods, byId)}
              onSelect={() => selectMod(mod.id)} onToggle={() => toggleMod(mod.id)} />)}
          </div>
        ) : (
          <div className="space-y-px">
            {filtered.map(mod => <ListItem key={mod.id} mod={mod} active={activeMods.has(mod.id)}
              selected={selectedModId === mod.id} missingDeps={getMissingDeps(mod, activeMods, byId)}
              onSelect={() => selectMod(mod.id)} onToggle={() => toggleMod(mod.id)} />)}
          </div>
        )}
      </div>
    </div>
  );
}

function ViewToggle({ mode, onChange }: { mode: ViewMode; onChange: (m: ViewMode) => void }) {
  return (
    <div className="flex bg-bg rounded-lg border border-bg-border overflow-hidden">
      <button onClick={() => onChange("card")}
        className={cn("p-1.5 transition", mode === "card" ? "bg-accent/10 text-accent" : "text-gray-500 hover:text-gray-300")}>
        <LayoutGrid size={14} />
      </button>
      <button onClick={() => onChange("list")}
        className={cn("p-1.5 transition", mode === "list" ? "bg-accent/10 text-accent" : "text-gray-500 hover:text-gray-300")}>
        <List size={14} />
      </button>
    </div>
  );
}

function getMissingDeps(mod: { dependencies: Record<string, string> }, active: Set<string>, byId: Map<string, unknown>): string[] {
  if (!active.has(mod.dependencies ? Object.keys(mod.dependencies)[0] ?? "" : "")) { /* check all */ }
  return Object.keys(mod.dependencies).filter(d => active.has((mod as any).id) && !active.has(d));
}

interface ItemProps {
  mod: ReturnType<typeof useLauncherStore.getState>["mods"][0];
  active: boolean; selected: boolean; missingDeps: string[];
  onSelect: () => void; onToggle: () => void;
}

function CardItem({ mod, active, selected, missingDeps, onSelect, onToggle }: ItemProps) {
  return (
    <div onClick={onSelect} className={cn(
      "flex gap-2.5 p-2.5 rounded-xl border cursor-pointer transition-all group",
      selected ? "border-accent/50 bg-accent/5 ring-1 ring-accent/20" : "border-bg-border bg-bg-card hover:bg-bg-hover hover:border-bg-border",
      !active && "opacity-35"
    )}>
      <div className="w-14 h-14 shrink-0 rounded-lg bg-bg overflow-hidden flex items-center justify-center">
        {mod.hasThumbnail
          ? <img src={thumbnailUrl(mod.id)} alt="" className="w-full h-full object-cover" onError={e => { (e.target as HTMLImageElement).style.display = "none"; }} />
          : <span className="text-xl font-bold text-gray-600">{mod.name[0]}</span>}
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-1.5">
          <span className="text-xs font-semibold truncate">{mod.name}</span>
          <span className="text-2xs text-gray-500 font-mono shrink-0">v{mod.version}</span>
        </div>
        {mod.author && <div className="flex items-center gap-0.5 text-2xs text-gray-500 mt-0.5"><User size={8} />{mod.author}</div>}
        {mod.description && <p className="text-2xs text-gray-400 mt-0.5 line-clamp-2">{mod.description}</p>}
        {mod.tags?.length > 0 && (
          <div className="flex flex-wrap gap-1 mt-1">
            {mod.tags.slice(0, 3).map(t => <span key={t} className="text-2xs px-1 py-px rounded bg-bg text-gray-500"><Tag size={7} className="inline mr-0.5" />{t}</span>)}
          </div>
        )}
        {missingDeps.length > 0 && active && (
          <div className="flex items-center gap-1 mt-1 text-2xs text-warn">
            <AlertTriangle size={10} />Missing: {missingDeps.join(", ")}
          </div>
        )}
      </div>
      <button onClick={e => { e.stopPropagation(); onToggle(); }}
        className={cn("w-5 h-5 rounded border flex items-center justify-center shrink-0 mt-0.5 transition",
          active ? "bg-accent border-accent text-white" : "border-gray-600 group-hover:border-gray-400")}>
        {active && <Check size={11} strokeWidth={3} />}
      </button>
    </div>
  );
}

function ListItem({ mod, active, selected, missingDeps, onSelect, onToggle }: ItemProps) {
  return (
    <div onClick={onSelect} className={cn(
      "flex items-center gap-3 px-3 py-2 rounded-lg cursor-pointer transition-all group",
      selected ? "bg-accent/5 border-l-2 border-accent" : "hover:bg-bg-hover border-l-2 border-transparent",
      !active && "opacity-35"
    )}>
      <button onClick={e => { e.stopPropagation(); onToggle(); }}
        className={cn("w-4 h-4 rounded border flex items-center justify-center shrink-0 transition",
          active ? "bg-accent border-accent text-white" : "border-gray-600 group-hover:border-gray-400")}>
        {active && <Check size={10} strokeWidth={3} />}
      </button>
      <div className="w-8 h-8 shrink-0 rounded bg-bg overflow-hidden flex items-center justify-center">
        {mod.hasThumbnail
          ? <img src={thumbnailUrl(mod.id)} alt="" className="w-full h-full object-cover" onError={e => { (e.target as HTMLImageElement).style.display = "none"; }} />
          : <span className="text-sm font-bold text-gray-600">{mod.name[0]}</span>}
      </div>
      <span className="text-xs font-medium truncate flex-1">{mod.name}</span>
      {mod.author && <span className="text-2xs text-gray-500 shrink-0">{mod.author}</span>}
      <span className="text-2xs text-gray-500 font-mono shrink-0">v{mod.version}</span>
      {missingDeps.length > 0 && active && <AlertTriangle size={12} className="text-warn shrink-0" />}
    </div>
  );
}
