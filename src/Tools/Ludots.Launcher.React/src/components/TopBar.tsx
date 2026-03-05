import { useLauncherStore } from "@/stores/launcherStore";
import { cn } from "@/lib/utils";
import {
  Gamepad2, Play, Hammer, Plus, FolderOpen, ChevronDown, Wifi, Loader2,
} from "lucide-react";

export function TopBar() {
  const {
    presets, selectedPresetId, activeMods, selectPreset,
    buildActive, launch, buildState, launching,
    toggleCreateDialog, toggleWorkspace,
  } = useLauncherStore();
  const isBusy = buildState === "building" || launching;

  return (
    <header className="flex items-center gap-3 px-4 py-2.5 bg-bg-panel border-b border-bg-border shrink-0">
      {/* Logo */}
      <Gamepad2 className="text-accent" size={24} />
      <span className="font-bold text-sm tracking-wider mr-2">LUDOTS</span>

      {/* Divider */}
      <div className="w-px h-5 bg-bg-border" />

      {/* Preset */}
      <div className="relative ml-1">
        <select
          value={selectedPresetId ?? "__custom__"}
          onChange={e => { if (e.target.value !== "__custom__") selectPreset(e.target.value); }}
          className="appearance-none bg-bg border border-bg-border rounded-lg pl-3 pr-7 py-1.5 text-xs cursor-pointer hover:border-accent/40 focus:outline-none focus:border-accent transition min-w-[180px]"
        >
          {selectedPresetId === null && <option value="__custom__">Custom Selection</option>}
          {presets.map(p => (
            <option key={p.id} value={p.id}>{p.windowTitle || p.id}</option>
          ))}
        </select>
        <ChevronDown size={12} className="absolute right-2 top-1/2 -translate-y-1/2 pointer-events-none text-gray-500" />
      </div>

      <span className="text-2xs text-gray-500">{activeMods.size} mods</span>

      {/* Spacer */}
      <div className="flex-1" />

      {/* Actions */}
      <button onClick={toggleWorkspace} title="Mod Sources"
        className="p-1.5 rounded-lg hover:bg-bg-hover text-gray-400 hover:text-gray-200 transition">
        <FolderOpen size={16} />
      </button>
      <button onClick={toggleCreateDialog} title="New Mod"
        className="p-1.5 rounded-lg hover:bg-bg-hover text-gray-400 hover:text-gray-200 transition">
        <Plus size={16} />
      </button>

      <div className="w-px h-5 bg-bg-border" />

      {/* Build */}
      <button onClick={buildActive} disabled={isBusy}
        className={cn(
          "flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition",
          buildState === "building" ? "bg-warn/10 text-warn" :
          buildState === "error" ? "bg-err/10 text-err hover:bg-err/20" :
          buildState === "done" ? "bg-ok/10 text-ok hover:bg-ok/20" :
          "bg-bg-hover text-gray-300 hover:bg-bg-border",
          isBusy && "opacity-60 cursor-not-allowed"
        )}>
        {buildState === "building" ? <Loader2 size={14} className="animate-spin" /> : <Hammer size={14} />}
        Build
      </button>

      {/* Launch */}
      <button onClick={launch} disabled={isBusy}
        className={cn(
          "flex items-center gap-1.5 px-5 py-1.5 rounded-lg text-xs font-semibold transition",
          "bg-accent hover:bg-accent-hover text-white shadow-lg shadow-accent/20",
          isBusy && "opacity-60 cursor-not-allowed"
        )}>
        {launching ? <Loader2 size={14} className="animate-spin" /> : <Play size={14} fill="currentColor" />}
        LAUNCH
      </button>

      <div className="w-px h-5 bg-bg-border" />
      <div className="flex items-center gap-1 text-2xs text-ok"><Wifi size={10} />Bridge</div>
    </header>
  );
}
