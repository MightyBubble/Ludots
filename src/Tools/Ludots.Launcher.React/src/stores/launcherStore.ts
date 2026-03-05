import { create } from "zustand";
import {
  fetchMods, fetchPresets, fetchReadme, fetchChangelog,
  fetchWorkspaceSources, addWorkspaceSource, checkHealth,
  buildAllMods, launchGame, createMod, generateSln,
  type ModInfo, type GamePreset,
} from "@/lib/api";

interface LauncherState {
  mods: ModInfo[];
  presets: GamePreset[];
  selectedPresetId: string | null;
  selectedModId: string | null;
  activeMods: Set<string>;
  bridgeOnline: boolean;
  loading: boolean;

  readme: string | null;
  changelog: string | null;
  detailTab: "info" | "readme" | "changelog";
  workspaceSources: string[];
  showWorkspace: boolean;
  buildLog: string;
  building: boolean;
  launching: boolean;

  init: () => Promise<void>;
  selectPreset: (id: string) => void;
  selectMod: (id: string | null) => void;
  toggleMod: (id: string) => void;
  setDetailTab: (tab: "info" | "readme" | "changelog") => void;
  addSource: (path: string) => Promise<boolean>;
  toggleWorkspace: () => void;
  buildActive: () => Promise<void>;
  launch: () => Promise<void>;
  createNewMod: (id: string, template: string) => Promise<boolean>;
  generateSlnForMod: (modId: string) => Promise<string | null>;
  appendLog: (msg: string) => void;
}

function resolveDeps(modId: string, byId: Map<string, ModInfo>, out: Set<string>) {
  if (out.has(modId)) return;
  const mod = byId.get(modId);
  if (!mod) return;
  for (const dep of Object.keys(mod.dependencies)) resolveDeps(dep, byId, out);
  out.add(modId);
}

function findDependents(modId: string, byId: Map<string, ModInfo>, active: Set<string>): string[] {
  const result: string[] = [];
  for (const id of active) {
    const mod = byId.get(id);
    if (mod && mod.id !== modId && Object.keys(mod.dependencies).includes(modId))
      result.push(id);
  }
  return result;
}

function modPathToId(p: string): string {
  return p.replace(/\\/g, "/").split("/").pop() ?? p;
}

function presetToActive(preset?: GamePreset): Set<string> {
  if (!preset) return new Set();
  return new Set(preset.modPaths.map(modPathToId));
}

export const useLauncherStore = create<LauncherState>((set, get) => ({
  mods: [], presets: [], selectedPresetId: null, selectedModId: null,
  activeMods: new Set(), bridgeOnline: false, loading: true,
  readme: null, changelog: null, detailTab: "info",
  workspaceSources: [], showWorkspace: false,
  buildLog: "", building: false, launching: false,

  init: async () => {
    set({ loading: true });
    const online = await checkHealth();
    if (!online) { set({ bridgeOnline: false, loading: false }); return; }
    const [mods, presets, sources] = await Promise.all([
      fetchMods(), fetchPresets(), fetchWorkspaceSources(),
    ]);
    const first = presets.length > 0 ? presets[0] : undefined;
    set({
      mods, presets, workspaceSources: sources,
      bridgeOnline: true, loading: false,
      selectedPresetId: first?.id ?? null,
      activeMods: presetToActive(first),
    });
  },

  selectPreset: (id) => {
    const preset = get().presets.find((p) => p.id === id);
    set({ selectedPresetId: id, activeMods: presetToActive(preset) });
  },

  selectMod: async (id) => {
    set({ selectedModId: id, readme: null, changelog: null, detailTab: "info" });
    if (!id) return;
    const mod = get().mods.find((m) => m.id === id);
    if (!mod) return;
    if (mod.hasReadme) fetchReadme(id).then((c) => { if (get().selectedModId === id) set({ readme: c }); });
    if (mod.changelogFile) fetchChangelog(id).then((c) => { if (get().selectedModId === id) set({ changelog: c }); });
  },

  toggleMod: (id) => {
    const { mods, activeMods } = get();
    const byId = new Map(mods.map((m) => [m.id, m]));
    const next = new Set(activeMods);
    if (next.has(id)) {
      const deps = findDependents(id, byId, next);
      for (const d of deps) next.delete(d);
      next.delete(id);
    } else {
      resolveDeps(id, byId, next);
    }
    set({ activeMods: next, selectedPresetId: null });
  },

  setDetailTab: (tab) => set({ detailTab: tab }),
  toggleWorkspace: () => set((s) => ({ showWorkspace: !s.showWorkspace })),

  addSource: async (path) => {
    const ok = await addWorkspaceSource(path);
    if (ok) {
      const sources = await fetchWorkspaceSources();
      const mods = await fetchMods();
      set({ workspaceSources: sources, mods });
    }
    return ok;
  },

  appendLog: (msg) => set((s) => ({ buildLog: s.buildLog + msg + "\n" })),

  buildActive: async () => {
    const { mods, activeMods } = get();
    const byId = new Map(mods.map((m) => [m.id, m]));
    const ordered: string[] = [];
    const visited = new Set<string>();
    function topoSort(id: string) {
      if (visited.has(id)) return;
      visited.add(id);
      const mod = byId.get(id);
      if (mod) for (const dep of Object.keys(mod.dependencies)) topoSort(dep);
      ordered.push(id);
    }
    for (const id of activeMods) topoSort(id);

    set({ building: true, buildLog: `Building ${ordered.length} mod(s)...\n` });
    try {
      const res = await buildAllMods(ordered);
      if (res.ok && res.results) {
        for (const r of res.results) {
          get().appendLog(`[${r.id}] ${r.ok ? "OK" : "FAIL"}`);
          if (r.output) get().appendLog(r.output);
        }
      } else {
        get().appendLog("Build request failed.");
      }
    } catch (e) {
      get().appendLog(`Error: ${e}`);
    }
    set({ building: false });
  },

  launch: async () => {
    const { selectedPresetId, activeMods, presets, mods } = get();
    set({ launching: true });
    get().appendLog("Launching game...");
    try {
      let modPaths: string[] | undefined;
      if (!selectedPresetId) {
        modPaths = [...activeMods].map((id) => {
          const mod = mods.find((m) => m.id === id);
          return mod?.rootPath ?? id;
        });
      }
      const res = await launchGame(selectedPresetId ?? undefined, modPaths);
      if (res.ok) {
        get().appendLog(`Game launched (PID: ${res.pid})`);
      } else {
        get().appendLog(`Launch failed: ${res.error ?? "unknown error"}`);
      }
    } catch (e) {
      get().appendLog(`Launch error: ${e}`);
    }
    set({ launching: false });
  },

  createNewMod: async (id, template) => {
    try {
      const res = await createMod(id, template);
      if (res.ok) {
        get().appendLog(`Mod "${id}" created.`);
        await get().init();
        return true;
      }
      get().appendLog(`Create mod failed: ${res.error ?? "unknown"}`);
      return false;
    } catch (e) {
      get().appendLog(`Create mod error: ${e}`);
      return false;
    }
  },

  generateSlnForMod: async (modId) => {
    try {
      const res = await generateSln(modId);
      if (res.ok && res.slnPath) {
        get().appendLog(`.sln generated: ${res.slnPath}`);
        return res.slnPath;
      }
      get().appendLog(`Generate .sln failed: ${res.error ?? "unknown"}`);
      return null;
    } catch (e) {
      get().appendLog(`Generate .sln error: ${e}`);
      return null;
    }
  },
}));
