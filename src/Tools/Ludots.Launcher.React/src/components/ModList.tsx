import { AlertTriangle, Check, LayoutGrid, List, Search, Tag, User, Wrench } from "lucide-react";
import { thumbnailUrl, type ModInfo } from "@/lib/api";
import { cn } from "@/lib/utils";
import { useLauncherStore, type ViewMode } from "@/stores/launcherStore";

export function ModList() {
  const {
    mods,
    activeMods,
    selectedModId,
    selectMod,
    toggleMod,
    viewMode,
    setViewMode,
    search,
    setSearch,
  } = useLauncherStore();

  const byId = new Map(mods.map((mod) => [mod.id, mod] as const));
  const filtered = mods.filter((mod) => {
    const term = search.trim().toLowerCase();
    if (!term) {
      return true;
    }

    return (
      mod.name.toLowerCase().includes(term) ||
      mod.description.toLowerCase().includes(term) ||
      mod.author.toLowerCase().includes(term) ||
      mod.tags.some((tag) => tag.toLowerCase().includes(term))
    );
  });

  return (
    <div className="flex min-w-0 flex-1 flex-col border-r border-bg-border">
      <div className="flex items-center gap-2 border-b border-bg-border bg-bg-panel/60 px-3 py-2">
        <div className="relative flex-1">
          <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-500" />
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Search mods, authors, tags..."
            className="w-full rounded-lg border border-bg-border bg-bg py-1.5 pl-8 pr-3 text-xs transition focus:border-accent/60 focus:outline-none"
          />
        </div>
        <ViewToggle mode={viewMode} onChange={setViewMode} />
      </div>

      <div className="flex-1 overflow-y-auto p-2">
        {viewMode === "card" ? (
          <div className="grid grid-cols-[repeat(auto-fill,minmax(280px,1fr))] gap-2">
            {filtered.map((mod) => (
              <CardItem
                key={mod.id}
                mod={mod}
                active={activeMods.has(mod.id)}
                selected={selectedModId === mod.id}
                missingDependencies={getMissingDependencies(mod, activeMods, byId)}
                onSelect={() => void selectMod(mod.id)}
                onToggle={() => toggleMod(mod.id)}
              />
            ))}
          </div>
        ) : (
          <div className="space-y-px">
            {filtered.map((mod) => (
              <ListItem
                key={mod.id}
                mod={mod}
                active={activeMods.has(mod.id)}
                selected={selectedModId === mod.id}
                missingDependencies={getMissingDependencies(mod, activeMods, byId)}
                onSelect={() => void selectMod(mod.id)}
                onToggle={() => toggleMod(mod.id)}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function ViewToggle({ mode, onChange }: { mode: ViewMode; onChange: (mode: ViewMode) => void }) {
  return (
    <div className="flex overflow-hidden rounded-lg border border-bg-border bg-bg">
      <button
        onClick={() => onChange("card")}
        className={cn(
          "p-1.5 transition",
          mode === "card" ? "bg-accent/10 text-accent" : "text-gray-500 hover:text-gray-200",
        )}
      >
        <LayoutGrid size={14} />
      </button>
      <button
        onClick={() => onChange("list")}
        className={cn(
          "p-1.5 transition",
          mode === "list" ? "bg-accent/10 text-accent" : "text-gray-500 hover:text-gray-200",
        )}
      >
        <List size={14} />
      </button>
    </div>
  );
}

function getMissingDependencies(
  mod: ModInfo,
  activeMods: Set<string>,
  byId: Map<string, ModInfo>,
): string[] {
  return Object.keys(mod.dependencies).filter((dependencyId) => activeMods.has(mod.id) && !byId.has(dependencyId));
}

function getBuildTone(buildState: ModInfo["buildState"]): string {
  switch (buildState) {
    case "Succeeded":
      return "bg-ok/10 text-ok";
    case "Outdated":
      return "bg-warn/10 text-warn";
    case "NoProject":
      return "bg-err/10 text-err";
    case "Failed":
      return "bg-err/10 text-err";
    default:
      return "bg-bg text-gray-400";
  }
}

function getBuildLabel(mod: ModInfo): string {
  switch (mod.buildState) {
    case "Succeeded":
      return "Built";
    case "Outdated":
      return "Outdated";
    case "NoProject":
      return "No Project";
    case "Failed":
      return "Failed";
    case "Idle":
      return "Not Built";
    default:
      return mod.buildState;
  }
}

interface ItemProps {
  mod: ModInfo;
  active: boolean;
  selected: boolean;
  missingDependencies: string[];
  onSelect: () => void;
  onToggle: () => void;
}

function CardItem({ mod, active, selected, missingDependencies, onSelect, onToggle }: ItemProps) {
  return (
    <div
      onClick={onSelect}
      className={cn(
        "group flex cursor-pointer gap-3 rounded-2xl border p-3 transition-all",
        selected
          ? "border-accent/50 bg-accent/5 ring-1 ring-accent/20"
          : "border-bg-border bg-bg-card hover:border-bg-border hover:bg-bg-hover",
        !active && "opacity-60",
      )}
    >
      <div className="flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-xl bg-bg">
        {mod.hasThumbnail ? (
          <img
            src={thumbnailUrl(mod.id)}
            alt=""
            className="h-full w-full object-cover"
            onError={(event) => {
              event.currentTarget.style.display = "none";
            }}
          />
        ) : (
          <span className="text-xl font-bold text-gray-600">{mod.name[0]}</span>
        )}
      </div>

      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <div className="flex items-center gap-1.5">
              <span className="truncate text-sm font-semibold">{mod.name}</span>
              <span className="shrink-0 text-[10px] font-mono text-gray-500">v{mod.version}</span>
            </div>
            {mod.author ? (
              <div className="mt-0.5 flex items-center gap-1 text-[11px] text-gray-500">
                <User size={10} />
                {mod.author}
              </div>
            ) : null}
          </div>

          <button
            onClick={(event) => {
              event.stopPropagation();
              onToggle();
            }}
            className={cn(
              "flex h-6 w-6 shrink-0 items-center justify-center rounded-md border transition",
              active ? "border-accent bg-accent text-white" : "border-gray-600 group-hover:border-gray-400",
            )}
          >
            {active ? <Check size={12} strokeWidth={3} /> : null}
          </button>
        </div>

        {mod.description ? <p className="mt-1 text-xs leading-relaxed text-gray-400 line-clamp-2">{mod.description}</p> : null}

        <div className="mt-2 flex flex-wrap items-center gap-1.5">
          <span className={cn("rounded-full px-2 py-0.5 text-[10px] uppercase tracking-[0.2em]", getBuildTone(mod.buildState))}>
            {getBuildLabel(mod)}
          </span>
          {mod.tags.slice(0, 3).map((tag) => (
            <span key={tag} className="rounded-full bg-bg px-2 py-0.5 text-[10px] text-gray-500">
              <Tag size={9} className="mr-1 inline" />
              {tag}
            </span>
          ))}
          {!mod.hasProject ? (
            <span className="rounded-full bg-err/10 px-2 py-0.5 text-[10px] uppercase tracking-[0.2em] text-err">
              <Wrench size={9} className="mr-1 inline" />
              Fix Needed
            </span>
          ) : null}
        </div>

        {missingDependencies.length > 0 ? (
          <div className="mt-2 flex items-center gap-1 text-[11px] text-warn">
            <AlertTriangle size={11} />
            Missing mods: {missingDependencies.join(", ")}
          </div>
        ) : null}
      </div>
    </div>
  );
}

function ListItem({ mod, active, selected, missingDependencies, onSelect, onToggle }: ItemProps) {
  return (
    <div
      onClick={onSelect}
      className={cn(
        "group flex cursor-pointer items-center gap-3 rounded-lg border border-transparent px-3 py-2 transition",
        selected ? "bg-accent/5 border-l-accent" : "hover:bg-bg-hover",
        !active && "opacity-60",
      )}
    >
      <button
        onClick={(event) => {
          event.stopPropagation();
          onToggle();
        }}
        className={cn(
          "flex h-4 w-4 shrink-0 items-center justify-center rounded border transition",
          active ? "border-accent bg-accent text-white" : "border-gray-600 group-hover:border-gray-400",
        )}
      >
        {active ? <Check size={10} strokeWidth={3} /> : null}
      </button>

      <div className="flex h-9 w-9 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-bg">
        {mod.hasThumbnail ? (
          <img
            src={thumbnailUrl(mod.id)}
            alt=""
            className="h-full w-full object-cover"
            onError={(event) => {
              event.currentTarget.style.display = "none";
            }}
          />
        ) : (
          <span className="text-sm font-bold text-gray-600">{mod.name[0]}</span>
        )}
      </div>

      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate text-sm font-medium">{mod.name}</span>
          <span className="text-[10px] font-mono text-gray-500">v{mod.version}</span>
        </div>
        <div className="flex items-center gap-2 text-[11px] text-gray-500">
          {mod.author ? <span>{mod.author}</span> : null}
          <span className={cn("rounded-full px-1.5 py-0.5 uppercase tracking-[0.15em]", getBuildTone(mod.buildState))}>
            {getBuildLabel(mod)}
          </span>
          {missingDependencies.length > 0 ? <AlertTriangle size={11} className="text-warn" /> : null}
        </div>
      </div>
    </div>
  );
}
