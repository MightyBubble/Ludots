import { create } from "zustand";
import {
  fetchMods, fetchPresets, fetchReadme, fetchChangelog,
  fetchWorkspaceSources, addWorkspaceSource, checkHealth,
  createMod, buildAllMods, launchGame, generateSln,
  type ModInfo, type GamePreset,
} from "@/lib/api";

export type ViewMode = "card" | "list";
export type BuildState = "idle" | "building" | "done" | "error";

interface LauncherState {
  mods: ModInfo[];
  presets: GamePreset[];
  selectedPresetId: string | null;
  selectedModId: string | null;
  activeMods: Set<string>;
  bridgeOnline: boolean;
  loading: boolean;
  viewMode: ViewMode;
  search: string;

  readme: string | null;
  changelog: string | null;
  detailTab: "info" | "readme" | "changelog";

  buildLog: string;
  buildState: BuildState;
  buildProgress: number;
  buildTotal: number;
  launching: boolean;

  workspaceSources: string[];
  showWorkspace: boolean;
  showCreateDialog: boolean;

  init: () => Promise<void>;
  selectPreset: (id: string) => void;
  selectMod: (id: string | null) => void;
  toggleMod: (id: string) => void;
  setViewMode: (m: ViewMode) => void;
  setSearch: (s: string) => void;
  setDetailTab: (t: "info" | "readme" | "changelog") => void;

  buildActive: () => Promise<void>;
  launch: () => Promise<void>;
  createNewMod: (id: string, template: string) => Promise<boolean>;
  generateSlnForMod: (modId: string) => Promise<string | null>;

  addSource: (path: string) => Promise<boolean>;
  toggleWorkspace: () => void;
  toggleCreateDialog: () => void;
}

function resolveDeps(id: string, byId: Map<string, ModInfo>, out: Set<string>) {
  if (out.has(id)) return;
  const mod = byId.get(id);
  if (!mod) return;
  for (const dep of Object.keys(mod.dependencies)) resolveDeps(dep, byId, out);
  out.add(id);
}

function findDependents(id: string, byId: Map<string, ModInfo>, active: Set<string>): string[] {
  const r: string[] = [];
  for (const a of active) {
    const m = byId.get(a);
    if (m && m.id !== id && Object.keys(m.dependencies).includes(id)) r.push(a);
  }
  return r;
}

function topoSort(ids: string[], mods: ModInfo[]): string[] {
  const byId = new Map(mods.map(m => [m.id, m]));
  const set = new Set(ids);
  const result: string[] = [];
  const visited = new Set<string>();
  function visit(id: string) {
    if (visited.has(id) || !set.has(id)) return;
    visited.add(id);
    const m = byId.get(id);
    if (m) for (const d of Object.keys(m.dependencies)) visit(d);
    result.push(id);
  }
  for (const id of ids) visit(id);
  return result;
}

function modPathToId(p: string): string { return p.replace(/\\/g, "/").split("/").pop() ?? p; }
function presetToActive(p?: GamePreset): Set<string> {
  return p ? new Set(p.modPaths.map(modPathToId)) : new Set();
}

export const useLauncherStore = create<LauncherState>((set, get) => ({
  mods: [], presets: [], selectedPresetId: null, selectedModId: null,
  activeMods: new Set(), bridgeOnline: false, loading: true,
  viewMode: "card", search: "",
  readme: null, changelog: null, detailTab: "info",
  buildLog: "", buildState: "idle", buildProgress: 0, buildTotal: 0, launching: false,
  workspaceSources: [], showWorkspace: false, showCreateDialog: false,

  init: async () => {
    set({ loading: true });
    const online = await checkHealth();
    if (!online) { set({ bridgeOnline: false, loading: false }); return; }
    const [mods, presets, sources] = await Promise.all([fetchMods(), fetchPresets(), fetchWorkspaceSources()]);
    const first = presets.find(p => p.id === "default") ?? presets[0];
    set({
      mods, presets, workspaceSources: sources, bridgeOnline: true, loading: false,
      selectedPresetId: first?.id ?? null, activeMods: presetToActive(first),
      selectedModId: mods.length > 0 ? mods[0].id : null,
    });
    if (mods.length > 0) get().selectMod(mods[0].id);
  },

  selectPreset: (id) => {
    const p = get().presets.find(x => x.id === id);
    set({ selectedPresetId: id, activeMods: presetToActive(p) });
  },

  selectMod: async (id) => {
    set({ selectedModId: id, readme: null, changelog: null, detailTab: "info" });
    if (!id) return;
    const mod = get().mods.find(m => m.id === id);
    if (!mod) return;
    if (mod.hasReadme) fetchReadme(id).then(c => { if (get().selectedModId === id) set({ readme: c }); });
    if (mod.changelogFile) fetchChangelog(id).then(c => { if (get().selectedModId === id) set({ changelog: c }); });
  },

  toggleMod: (id) => {
    const { mods, activeMods } = get();
    const byId = new Map(mods.map(m => [m.id, m]));
    const next = new Set(activeMods);
    if (next.has(id)) {
      for (const d of findDependents(id, byId, next)) next.delete(d);
      next.delete(id);
    } else {
      resolveDeps(id, byId, next);
    }
    set({ activeMods: next, selectedPresetId: null });
  },

  setViewMode: (m) => set({ viewMode: m }),
  setSearch: (s) => set({ search: s }),
  setDetailTab: (t) => set({ detailTab: t }),
  toggleWorkspace: () => set(s => ({ showWorkspace: !s.showWorkspace })),
  toggleCreateDialog: () => set(s => ({ showCreateDialog: !s.showCreateDialog })),

  buildActive: async () => {
    const { mods, activeMods } = get();
    const sorted = topoSort([...activeMods], mods);
    set({ buildState: "building", buildLog: "", buildProgress: 0, buildTotal: sorted.length });

    const res = await buildAllMods(sorted);
    let log = "";
    let hasError = false;
    if (res.results) {
      for (let i = 0; i < res.results.length; i++) {
        const r = res.results[i];
        const icon = r.ok ? "✓" : "✗";
        log += `[${i + 1}/${sorted.length}] ${icon} ${r.id}\n`;
        if (r.output) log += r.output.split("\n").slice(-3).join("\n") + "\n";
        if (!r.ok) hasError = true;
        set({ buildProgress: i + 1, buildLog: log });
      }
    }
    set({ buildState: hasError ? "error" : "done" });
  },

  launch: async () => {
    const { selectedPresetId, activeMods, mods } = get();
    set({ launching: true });
    const modPaths = [...activeMods].map(id => {
      const m = mods.find(x => x.id === id);
      return m?.rootPath ?? id;
    });
    await launchGame(selectedPresetId ?? undefined, selectedPresetId ? undefined : modPaths);
    setTimeout(() => set({ launching: false }), 2000);
  },

  createNewMod: async (id, template) => {
    const res = await createMod(id, template);
    if (res.ok) { await get().init(); set({ showCreateDialog: false }); }
    return res.ok;
  },

  generateSlnForMod: async (modId) => {
    const res = await generateSln(modId);
    return res.ok ? (res.slnPath ?? null) : null;
  },

  addSource: async (path) => {
    const ok = await addWorkspaceSource(path);
    if (ok) {
      const [sources, mods] = await Promise.all([fetchWorkspaceSources(), fetchMods()]);
      set({ workspaceSources: sources, mods });
    }
    return ok;
  },
}));
