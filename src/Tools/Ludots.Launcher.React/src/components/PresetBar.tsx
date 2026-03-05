import { useLauncherStore } from "@/stores/launcherStore";
import { Play, ChevronDown } from "lucide-react";
import { cn } from "@/lib/utils";

export function PresetBar() {
  const { presets, selectedPresetId, activeMods, selectPreset } =
    useLauncherStore();

  const isCustom = selectedPresetId === null;

  return (
    <div className="flex items-center gap-4 px-6 py-3 bg-surface-lighter border-b border-white/5">
      <label className="text-sm text-gray-400 shrink-0">Preset</label>
      <div className="relative">
        <select
          value={selectedPresetId ?? "__custom__"}
          onChange={(e) => {
            const v = e.target.value;
            if (v !== "__custom__") selectPreset(v);
          }}
          className="appearance-none bg-surface border border-white/10 rounded-lg px-4 py-2 pr-8 text-sm min-w-[220px] cursor-pointer hover:border-accent/50 transition focus:outline-none focus:border-accent"
        >
          {isCustom && (
            <option value="__custom__" disabled>
              Custom Selection
            </option>
          )}
          {presets.map((p) => (
            <option key={p.id} value={p.id}>
              {p.windowTitle || p.id}
            </option>
          ))}
        </select>
        <ChevronDown
          size={14}
          className="absolute right-2 top-1/2 -translate-y-1/2 pointer-events-none text-gray-500"
        />
      </div>

      <span className="text-xs text-gray-500">
        {activeMods.size} mod{activeMods.size !== 1 ? "s" : ""} active
      </span>

      {isCustom && (
        <span className="text-[10px] px-2 py-0.5 bg-yellow-500/10 text-yellow-400 rounded">
          custom
        </span>
      )}

      <div className="flex-1" />

      <button
        className={cn(
          "flex items-center gap-2 px-8 py-2.5 rounded-lg font-semibold text-sm transition",
          "bg-accent hover:bg-accent-hover text-white shadow-lg shadow-accent/20"
        )}
      >
        <Play size={16} fill="currentColor" />
        LAUNCH
      </button>
    </div>
  );
}
