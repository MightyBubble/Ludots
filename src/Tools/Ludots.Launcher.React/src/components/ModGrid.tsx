import { useLauncherStore } from "@/stores/launcherStore";
import { cn } from "@/lib/utils";
import { thumbnailUrl } from "@/lib/api";
import { Check, User, Tag } from "lucide-react";

export function ModGrid() {
  const { mods, activeMods, selectedModId, selectMod, toggleMod } = useLauncherStore();

  return (
    <div className="flex-1 overflow-y-auto p-4">
      <div className="grid grid-cols-[repeat(auto-fill,minmax(300px,1fr))] gap-3">
        {mods.map((mod) => {
          const active = activeMods.has(mod.id);
          const selected = selectedModId === mod.id;
          return (
            <div
              key={mod.id}
              onClick={() => selectMod(selected ? null : mod.id)}
              className={cn(
                "flex gap-3 p-3 rounded-xl border cursor-pointer transition-all",
                "hover:border-accent/40 hover:bg-surface-lighter/50",
                selected ? "border-accent bg-accent/5 ring-1 ring-accent/30" : "border-white/5 bg-surface-light",
                !active && "opacity-40"
              )}
            >
              {/* Thumbnail */}
              <div className="w-16 h-16 shrink-0 rounded-lg bg-surface-lighter overflow-hidden flex items-center justify-center">
                {mod.hasThumbnail ? (
                  <img
                    src={thumbnailUrl(mod.id)}
                    alt={mod.name}
                    className="w-full h-full object-cover"
                    onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }}
                  />
                ) : (
                  <span className="text-2xl text-gray-600">{mod.name[0]}</span>
                )}
              </div>

              {/* Info */}
              <div className="flex-1 min-w-0">
                <div className="flex items-center justify-between gap-1">
                  <span className="font-semibold text-sm truncate">{mod.name}</span>
                  <button
                    onClick={(e) => { e.stopPropagation(); toggleMod(mod.id); }}
                    className={cn(
                      "w-5 h-5 rounded border flex items-center justify-center transition shrink-0",
                      active ? "bg-accent border-accent text-white" : "border-gray-600 hover:border-gray-400"
                    )}
                  >
                    {active && <Check size={12} strokeWidth={3} />}
                  </button>
                </div>

                <div className="flex items-center gap-2 mt-0.5">
                  <span className="text-[10px] text-gray-500 font-mono">v{mod.version}</span>
                  {mod.author && (
                    <span className="flex items-center gap-0.5 text-[10px] text-gray-500">
                      <User size={8} /> {mod.author}
                    </span>
                  )}
                </div>

                {mod.description && (
                  <p className="text-[11px] text-gray-400 mt-1 line-clamp-1">{mod.description}</p>
                )}

                {mod.tags && mod.tags.length > 0 && (
                  <div className="flex flex-wrap gap-1 mt-1.5">
                    {mod.tags.map((t) => (
                      <span key={t} className="flex items-center gap-0.5 text-[9px] px-1.5 py-0.5 bg-white/5 text-gray-400 rounded">
                        <Tag size={7} />{t}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
