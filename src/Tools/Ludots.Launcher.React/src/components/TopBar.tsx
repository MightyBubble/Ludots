import {
  FolderOpen,
  Gamepad2,
  Globe,
  Hammer,
  Loader2,
  Monitor,
  Play,
  Plus,
  Save,
  Trash2,
  Wifi,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useLauncherStore } from "@/stores/launcherStore";

export function TopBar() {
  const {
    platforms,
    selectedPlatformId,
    presets,
    selectedPresetId,
    presetDirty,
    activeMods,
    buildState,
    launching,
    selectPlatform,
    selectPreset,
    saveCurrentPreset,
    savePresetAs,
    deleteSelectedPreset,
    buildActive,
    buildPlatformApp,
    launch,
    toggleCreateDialog,
    toggleWorkspace,
  } = useLauncherStore();

  const selectedPreset = presets.find((preset) => preset.id === selectedPresetId) ?? null;
  const isBusy = buildState === "building" || launching;
  const canDeletePreset = selectedPreset !== null && selectedPreset.id !== "default";

  const handleSave = async () => {
    if (selectedPreset) {
      await saveCurrentPreset();
      return;
    }

    const name = window.prompt("Preset name", "My Preset");
    if (name?.trim()) {
      await savePresetAs(name);
    }
  };

  const handleSaveAs = async () => {
    const suggestedName = selectedPreset?.name ?? "My Preset";
    const name = window.prompt("Save current mod selection as preset", suggestedName);
    if (name?.trim()) {
      await savePresetAs(name);
    }
  };

  const handleDelete = async () => {
    if (!selectedPreset || !canDeletePreset) {
      return;
    }

    if (!window.confirm(`Delete preset "${selectedPreset.name}"?`)) {
      return;
    }

    await deleteSelectedPreset();
  };

  return (
    <header className="flex items-center gap-3 border-b border-bg-border bg-bg-panel px-4 py-3">
      <div className="flex items-center gap-2">
        <Gamepad2 className="text-accent" size={22} />
        <div className="flex flex-col">
          <span className="text-sm font-bold tracking-[0.25em]">LUDOTS</span>
          <span className="text-[10px] uppercase tracking-[0.35em] text-gray-500">Launcher</span>
        </div>
      </div>

      <div className="h-6 w-px bg-bg-border" />

      <div className="flex items-center gap-2">
        <span className="text-[10px] uppercase tracking-[0.3em] text-gray-500">Platform</span>
        <div className="flex rounded-xl border border-bg-border bg-bg p-1">
          {platforms.map((platform) => {
            const isSelected = platform.id === selectedPlatformId;
            return (
              <button
                key={platform.id}
                onClick={() => void selectPlatform(platform.id)}
                className={cn(
                  "flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs transition",
                  isSelected
                    ? "bg-accent text-white shadow-lg shadow-accent/20"
                    : "text-gray-400 hover:bg-bg-hover hover:text-gray-200",
                )}
              >
                {platform.id === "web" ? <Globe size={13} /> : <Monitor size={13} />}
                {platform.name}
              </button>
            );
          })}
        </div>
      </div>

      <div className="h-6 w-px bg-bg-border" />

      <div className="flex min-w-[240px] items-center gap-2">
        <span className="text-[10px] uppercase tracking-[0.3em] text-gray-500">Preset</span>
        <select
          value={selectedPresetId ?? ""}
          onChange={(event) => void selectPreset(event.target.value)}
          className="min-w-[180px] rounded-lg border border-bg-border bg-bg px-3 py-2 text-xs transition hover:border-accent/40 focus:border-accent focus:outline-none"
        >
          {presets.map((preset) => (
            <option key={preset.id} value={preset.id}>
              {preset.name}
            </option>
          ))}
        </select>
        {presetDirty ? (
          <span className="rounded-full border border-warn/30 bg-warn/10 px-2 py-0.5 text-[10px] uppercase tracking-[0.2em] text-warn">
            Unsaved
          </span>
        ) : null}
      </div>

      <span className="text-xs text-gray-500">{activeMods.size} mods selected</span>

      <div className="flex-1" />

      <div className="flex items-center gap-1">
        <button
          onClick={() => void handleSave()}
          disabled={isBusy || activeMods.size === 0}
          className="flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs text-gray-300 transition hover:bg-bg-hover hover:text-white disabled:cursor-not-allowed disabled:opacity-40"
          title={selectedPreset ? "Save to selected preset" : "Save current selection as preset"}
        >
          <Save size={13} />
          Save
        </button>
        <button
          onClick={() => void handleSaveAs()}
          disabled={isBusy || activeMods.size === 0}
          className="rounded-lg px-3 py-1.5 text-xs text-gray-300 transition hover:bg-bg-hover hover:text-white disabled:cursor-not-allowed disabled:opacity-40"
          title="Save as new preset"
        >
          Save As
        </button>
        <button
          onClick={() => void handleDelete()}
          disabled={isBusy || !canDeletePreset}
          className="flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs text-gray-300 transition hover:bg-bg-hover hover:text-white disabled:cursor-not-allowed disabled:opacity-40"
          title="Delete preset"
        >
          <Trash2 size={13} />
          Delete
        </button>
      </div>

      <div className="h-6 w-px bg-bg-border" />

      <button
        onClick={toggleWorkspace}
        title="Mod source directories"
        className="rounded-lg p-2 text-gray-400 transition hover:bg-bg-hover hover:text-gray-100"
      >
        <FolderOpen size={16} />
      </button>
      <button
        onClick={toggleCreateDialog}
        title="Create mod"
        className="rounded-lg p-2 text-gray-400 transition hover:bg-bg-hover hover:text-gray-100"
      >
        <Plus size={16} />
      </button>

      <div className="h-6 w-px bg-bg-border" />

      <button
        onClick={() => void buildActive()}
        disabled={isBusy || activeMods.size === 0}
        className={cn(
          "flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-medium transition",
          buildState === "error"
            ? "bg-err/10 text-err hover:bg-err/20"
            : buildState === "done"
              ? "bg-ok/10 text-ok hover:bg-ok/20"
              : "bg-bg-hover text-gray-200 hover:bg-bg-border",
          isBusy && "cursor-not-allowed opacity-50",
        )}
      >
        {buildState === "building" ? <Loader2 size={14} className="animate-spin" /> : <Hammer size={14} />}
        Build Mods
      </button>

      <button
        onClick={() => void buildPlatformApp()}
        disabled={isBusy}
        className="rounded-lg bg-bg-hover px-3 py-1.5 text-xs font-medium text-gray-200 transition hover:bg-bg-border disabled:cursor-not-allowed disabled:opacity-50"
      >
        Build App
      </button>

      <button
        onClick={() => void launch()}
        disabled={isBusy || activeMods.size === 0}
        className={cn(
          "flex items-center gap-1.5 rounded-lg px-5 py-1.5 text-xs font-semibold text-white shadow-lg shadow-accent/25 transition",
          "bg-accent hover:bg-accent-hover",
          isBusy && "cursor-not-allowed opacity-50",
        )}
      >
        {launching ? <Loader2 size={14} className="animate-spin" /> : <Play size={14} fill="currentColor" />}
        Launch
      </button>

      <div className="h-6 w-px bg-bg-border" />
      <div className="flex items-center gap-1 text-[10px] uppercase tracking-[0.2em] text-ok">
        <Wifi size={11} />
        Bridge
      </div>
    </header>
  );
}
