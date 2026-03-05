import { useLauncherStore } from "@/stores/launcherStore";
import { cn } from "@/lib/utils";
import { Package, Tag } from "lucide-react";

export function ModGrid() {
  const { mods, presets, selectedPresetId, selectedModId, selectMod } = useLauncherStore();
  const currentPreset = presets.find((p) => p.id === selectedPresetId);

  const activeModNames = new Set(
    (currentPreset?.modPaths ?? []).map((p) => {
      const parts = p.replace(/\\/g, "/").split("/");
      return parts[parts.length - 1];
    })
  );

  return (
    <div className="flex-1 overflow-y-auto p-4">
      <div className="grid grid-cols-[repeat(auto-fill,minmax(280px,1fr))] gap-3">
        {mods.map((mod) => {
          const active = activeModNames.has(mod.id);
          const selected = selectedModId === mod.id;
          return (
            <button
              key={mod.id}
              onClick={() => selectMod(selected ? null : mod.id)}
              className={cn(
                "flex flex-col p-4 rounded-xl border text-left transition-all",
                "hover:border-accent/40 hover:bg-surface-lighter/50",
                selected
                  ? "border-accent bg-accent/5 ring-1 ring-accent/30"
                  : "border-white/5 bg-surface-light",
                !active && "opacity-50"
              )}
            >
              <div className="flex items-start justify-between gap-2">
                <div className="flex items-center gap-2">
                  <Package size={16} className={active ? "text-accent" : "text-gray-500"} />
                  <span className="font-semibold text-sm">{mod.name}</span>
                </div>
                <span className="text-[10px] text-gray-500 font-mono shrink-0">v{mod.version}</span>
              </div>

              {active && (
                <span className="mt-1.5 text-[10px] px-1.5 py-0.5 bg-accent/10 text-accent rounded w-fit">
                  active
                </span>
              )}

              {Object.keys(mod.dependencies).length > 0 && (
                <div className="flex flex-wrap gap-1 mt-2">
                  {Object.keys(mod.dependencies).map((dep) => (
                    <span key={dep} className="flex items-center gap-0.5 text-[10px] text-gray-500">
                      <Tag size={8} />
                      {dep}
                    </span>
                  ))}
                </div>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
}
