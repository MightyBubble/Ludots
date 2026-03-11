import { create } from "zustand";
import {
  addWorkspaceSource,
  buildAllMods,
  buildApp,
  buildMod,
  checkHealth,
  createMod,
  deletePreset,
  fetchChangelog,
  fetchLauncherSnapshot,
  fetchMods,
  fetchReadme,
  fixProject,
  generateSln,
  launchGame,
  savePreset,
  selectPlatform,
  selectPreset,
  type BuildResult,
  type LauncherPreset,
  type LauncherStateSnapshot,
  type ModInfo,
  type PlatformProfile,
} from "@/lib/api";

export type ViewMode = "card" | "list";
export type BuildState = "idle" | "building" | "done" | "error";
export type DetailTab = "info" | "readme" | "changelog" | "graph";

interface LauncherState {
  mods: ModInfo[];
  platforms: PlatformProfile[];
  presets: LauncherPreset[];
  selectedPlatformId: string;
  selectedPresetId: string | null;
  selectedModId: string | null;
  activeMods: Set<string>;
  presetDirty: boolean;
  bridgeOnline: boolean;
  loading: boolean;
  viewMode: ViewMode;
  search: string;
  readme: string | null;
  changelog: string | null;
  detailTab: DetailTab;
  buildLog: string;
  buildState: BuildState;
  buildProgress: number;
  buildTotal: number;
  launching: boolean;
  workspaceSources: string[];
  showWorkspace: boolean;
  showCreateDialog: boolean;

  init: () => Promise<void>;
  refreshMods: () => Promise<void>;
  selectPreset: (id: string) => Promise<void>;
  selectPlatform: (id: string) => Promise<void>;
  selectMod: (id: string | null) => Promise<void>;
  toggleMod: (id: string) => void;
  saveCurrentPreset: () => Promise<boolean>;
  savePresetAs: (name: string) => Promise<boolean>;
  deleteSelectedPreset: () => Promise<boolean>;
  setViewMode: (mode: ViewMode) => void;
  setSearch: (value: string) => void;
  setDetailTab: (tab: DetailTab) => void;
  buildActive: () => Promise<void>;
  buildPlatformApp: () => Promise<void>;
  buildSingleMod: (modId: string) => Promise<boolean>;
  fixProjectForMod: (modId: string) => Promise<string | null>;
  launch: () => Promise<void>;
  createNewMod: (id: string, template: string) => Promise<boolean>;
  generateSlnForMod: (modId: string) => Promise<string | null>;
  addSource: (path: string) => Promise<boolean>;
  toggleWorkspace: () => void;
  toggleCreateDialog: () => void;
}

function createModMap(mods: ModInfo[]): Map<string, ModInfo> {
  return new Map(mods.map((mod) => [mod.id, mod] as const));
}

function resolveDependencies(modId: string, byId: Map<string, ModInfo>, output: Set<string>): void {
  if (output.has(modId)) {
    return;
  }

  const mod = byId.get(modId);
  if (!mod) {
    return;
  }

  for (const dependencyId of Object.keys(mod.dependencies).sort((left, right) => left.localeCompare(right))) {
    resolveDependencies(dependencyId, byId, output);
  }

  output.add(modId);
}

function resolveSelection(modIds: Iterable<string>, mods: ModInfo[]): Set<string> {
  const output = new Set<string>();
  const byId = createModMap(mods);
  for (const modId of modIds) {
    resolveDependencies(modId, byId, output);
  }

  return output;
}

function collectDependents(
  modId: string,
  byId: Map<string, ModInfo>,
  activeMods: Set<string>,
  output: Set<string>,
): void {
  for (const activeModId of activeMods) {
    const activeMod = byId.get(activeModId);
    if (!activeMod || activeModId === modId || !Object.prototype.hasOwnProperty.call(activeMod.dependencies, modId)) {
      continue;
    }

    if (output.has(activeModId)) {
      continue;
    }

    output.add(activeModId);
    collectDependents(activeModId, byId, activeMods, output);
  }
}

function topoSort(modIds: Iterable<string>, mods: ModInfo[]): string[] {
  const byId = createModMap(mods);
  const selected = new Set(modIds);
  const result: string[] = [];
  const visited = new Set<string>();
  const visiting = new Set<string>();

  function visit(modId: string): void {
    if (visited.has(modId) || !selected.has(modId)) {
      return;
    }

    if (visiting.has(modId)) {
      return;
    }

    visiting.add(modId);
    const mod = byId.get(modId);
    if (mod) {
      for (const dependencyId of Object.keys(mod.dependencies).sort((left, right) => left.localeCompare(right))) {
        visit(dependencyId);
      }
    }

    visiting.delete(modId);
    visited.add(modId);
    result.push(modId);
  }

  [...selected].sort((left, right) => left.localeCompare(right)).forEach(visit);
  return result;
}

function pickSelectedModId(currentSelectedModId: string | null, mods: ModInfo[]): string | null {
  if (currentSelectedModId && mods.some((mod) => mod.id === currentSelectedModId)) {
    return currentSelectedModId;
  }

  return mods[0]?.id ?? null;
}

function findPreset(presets: LauncherPreset[], presetId: string | null): LauncherPreset | undefined {
  if (!presetId) {
    return undefined;
  }

  return presets.find((preset) => preset.id === presetId);
}

function setsEqual(left: Set<string>, right: Set<string>): boolean {
  if (left.size !== right.size) {
    return false;
  }

  for (const value of left) {
    if (!right.has(value)) {
      return false;
    }
  }

  return true;
}

function calculatePresetDirty(
  presets: LauncherPreset[],
  presetId: string | null,
  activeMods: Set<string>,
  mods: ModInfo[],
): boolean {
  const preset = findPreset(presets, presetId);
  if (!preset) {
    return activeMods.size > 0;
  }

  const presetSelection = resolveSelection(preset.activeModIds, mods);
  return !setsEqual(presetSelection, activeMods);
}

function joinBuildLog(results: BuildResult[]): string {
  return results
    .map((result) => {
      const header = `[${result.ok ? "OK" : "FAIL"}] ${result.id}`;
      return result.output ? `${header}\n${result.output.trim()}` : header;
    })
    .join("\n\n");
}

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export const useLauncherStore = create<LauncherState>((set, get) => {
  const applyServerState = (
    state: LauncherStateSnapshot,
    options?: {
      mods?: ModInfo[];
      preserveSelection?: boolean;
      forcePresetClean?: boolean;
    },
  ) => {
    set((current) => {
      const mods = options?.mods ?? current.mods;
      const nextActiveMods = options?.preserveSelection
        ? resolveSelection(current.activeMods, mods)
        : resolveSelection(findPreset(state.presets, state.selectedPresetId)?.activeModIds ?? [], mods);
      const presetDirty = options?.forcePresetClean
        ? false
        : calculatePresetDirty(state.presets, state.selectedPresetId, nextActiveMods, mods);

      return {
        mods,
        platforms: state.platforms,
        selectedPlatformId: state.selectedPlatformId,
        presets: state.presets,
        selectedPresetId: state.selectedPresetId,
        workspaceSources: state.workspaceSources,
        activeMods: nextActiveMods,
        presetDirty,
        selectedModId: pickSelectedModId(current.selectedModId, mods),
      };
    });
  };

  const applyMods = (mods: ModInfo[]) => {
    set((current) => {
      const nextActiveMods = resolveSelection(current.activeMods, mods);
      return {
        mods,
        activeMods: nextActiveMods,
        presetDirty: calculatePresetDirty(current.presets, current.selectedPresetId, nextActiveMods, mods),
        selectedModId: pickSelectedModId(current.selectedModId, mods),
      };
    });
  };

  const setBuildResults = (
    results: BuildResult[],
    mods?: ModInfo[],
  ) => {
    set({
      buildLog: joinBuildLog(results),
      buildState: results.every((result) => result.ok) ? "done" : "error",
      buildProgress: results.length,
      buildTotal: results.length,
    });

    if (mods) {
      applyMods(mods);
    }
  };

  return {
    mods: [],
    platforms: [],
    presets: [],
    selectedPlatformId: "raylib",
    selectedPresetId: null,
    selectedModId: null,
    activeMods: new Set<string>(),
    presetDirty: false,
    bridgeOnline: false,
    loading: true,
    viewMode: "card",
    search: "",
    readme: null,
    changelog: null,
    detailTab: "info",
    buildLog: "",
    buildState: "idle",
    buildProgress: 0,
    buildTotal: 0,
    launching: false,
    workspaceSources: [],
    showWorkspace: false,
    showCreateDialog: false,

    init: async () => {
      set({ loading: true });
      try {
        const online = await checkHealth();
        if (!online) {
          set({ bridgeOnline: false, loading: false });
          return;
        }

        const snapshot = await fetchLauncherSnapshot();
        set({
          bridgeOnline: true,
          loading: false,
        });
        applyServerState(snapshot.state, {
          mods: snapshot.mods,
          preserveSelection: false,
          forcePresetClean: true,
        });
        await get().selectMod(pickSelectedModId(get().selectedModId, snapshot.mods));
      } catch {
        set({ bridgeOnline: false, loading: false });
      }
    },

    refreshMods: async () => {
      try {
        const mods = await fetchMods();
        applyMods(mods);
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
      }
    },

    selectPreset: async (id) => {
      try {
        const response = await selectPreset(id);
        applyServerState(response.state, {
          preserveSelection: false,
          forcePresetClean: true,
        });
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
      }
    },

    selectPlatform: async (id) => {
      try {
        const response = await selectPlatform(id);
        applyServerState(response.state, {
          preserveSelection: true,
        });
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
      }
    },

    selectMod: async (id) => {
      set({ selectedModId: id, readme: null, changelog: null, detailTab: "info" });
      if (!id) {
        return;
      }

      const mod = get().mods.find((candidate) => candidate.id === id);
      if (!mod) {
        return;
      }

      if (mod.hasReadme) {
        fetchReadme(id).then((content) => {
          if (get().selectedModId === id) {
            set({ readme: content });
          }
        });
      }

      if (mod.changelogFile) {
        fetchChangelog(id).then((content) => {
          if (get().selectedModId === id) {
            set({ changelog: content });
          }
        });
      }
    },

    toggleMod: (id) => {
      const { mods, activeMods, presets, selectedPresetId } = get();
      const byId = createModMap(mods);
      const next = new Set(activeMods);

      if (next.has(id)) {
        const dependents = new Set<string>();
        collectDependents(id, byId, next, dependents);
        dependents.forEach((dependentId) => next.delete(dependentId));
        next.delete(id);
      } else {
        resolveDependencies(id, byId, next);
      }

      set({
        activeMods: next,
        presetDirty: calculatePresetDirty(presets, selectedPresetId, next, mods),
      });
    },

    saveCurrentPreset: async () => {
      const state = get();
      const preset = findPreset(state.presets, state.selectedPresetId);
      if (!preset) {
        return false;
      }

      try {
        const response = await savePreset({
          presetId: preset.id,
          name: preset.name,
          activeModIds: topoSort(state.activeMods, state.mods),
          includeDependencies: true,
          selectAfterSave: true,
        });

        applyServerState(response.state, {
          preserveSelection: true,
          forcePresetClean: true,
        });
        return true;
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
        return false;
      }
    },

    savePresetAs: async (name) => {
      if (!name.trim()) {
        return false;
      }

      const state = get();
      try {
        const response = await savePreset({
          name: name.trim(),
          activeModIds: topoSort(state.activeMods, state.mods),
          includeDependencies: true,
          selectAfterSave: true,
        });

        applyServerState(response.state, {
          preserveSelection: true,
          forcePresetClean: true,
        });
        return true;
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
        return false;
      }
    },

    deleteSelectedPreset: async () => {
      const { selectedPresetId } = get();
      if (!selectedPresetId) {
        return false;
      }

      try {
        const response = await deletePreset(selectedPresetId);
        applyServerState(response.state, {
          preserveSelection: false,
          forcePresetClean: true,
        });
        return true;
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
        return false;
      }
    },

    setViewMode: (mode) => set({ viewMode: mode }),
    setSearch: (value) => set({ search: value }),
    setDetailTab: (tab) => set({ detailTab: tab }),
    toggleWorkspace: () => set((state) => ({ showWorkspace: !state.showWorkspace })),
    toggleCreateDialog: () => set((state) => ({ showCreateDialog: !state.showCreateDialog })),

    buildActive: async () => {
      const state = get();
      const selected = topoSort(state.activeMods, state.mods);
      if (selected.length === 0) {
        return;
      }

      set({
        buildState: "building",
        buildLog: "",
        buildProgress: 0,
        buildTotal: selected.length,
      });

      try {
        const response = await buildAllMods(selected);
        const results = response.results ?? [];
        setBuildResults(results, response.mods);
      } catch (error) {
        set({
          buildState: "error",
          buildLog: getErrorMessage(error),
          buildProgress: 1,
          buildTotal: 1,
        });
      }
    },

    buildPlatformApp: async () => {
      const { selectedPlatformId } = get();
      set({
        buildState: "building",
        buildLog: "",
        buildProgress: 0,
        buildTotal: 1,
      });

      try {
        const response = await buildApp(selectedPlatformId);
        const result = response.result
          ? {
              id: selectedPlatformId,
              ok: response.result.ok,
              exitCode: response.result.exitCode,
              output: response.result.output,
            }
          : {
              id: selectedPlatformId,
              ok: false,
              exitCode: 1,
              output: response.error ?? "App build failed.",
            };

        setBuildResults([result]);
      } catch (error) {
        set({
          buildState: "error",
          buildLog: getErrorMessage(error),
          buildProgress: 1,
          buildTotal: 1,
        });
      }
    },

    buildSingleMod: async (modId) => {
      set({
        buildState: "building",
        buildLog: "",
        buildProgress: 0,
        buildTotal: 1,
      });

      try {
        const response = await buildMod(modId);
        const result = response.result ?? {
          id: modId,
          ok: false,
          exitCode: 1,
          output: response.error ?? "Build failed.",
        };

        setBuildResults([result], response.mods);
        return result.ok;
      } catch (error) {
        set({
          buildState: "error",
          buildLog: getErrorMessage(error),
          buildProgress: 1,
          buildTotal: 1,
        });
        return false;
      }
    },

    fixProjectForMod: async (modId) => {
      try {
        const response = await fixProject(modId);
        if (response.mods) {
          applyMods(response.mods);
        }

        const message = response.projectPath ?? null;
        set({
          buildLog: message ? `Created project: ${message}` : get().buildLog,
          buildState: message ? "done" : "error",
          buildProgress: 1,
          buildTotal: 1,
        });
        return message;
      } catch (error) {
        set({
          buildLog: getErrorMessage(error),
          buildState: "error",
          buildProgress: 1,
          buildTotal: 1,
        });
        return null;
      }
    },

    launch: async () => {
      const { selectedPlatformId, activeMods } = get();
      set({ launching: true });
      try {
        const result = await launchGame(selectedPlatformId, topoSort(activeMods, get().mods));
        if (!result.ok) {
          set({
            buildState: "error",
            buildLog: result.error ?? "Launch failed.",
          });
          return;
        }

        if (result.url) {
          window.open(result.url, "_blank", "noopener,noreferrer");
        }
      } catch (error) {
        set({
          buildState: "error",
          buildLog: getErrorMessage(error),
        });
      } finally {
        set({ launching: false });
      }
    },

    createNewMod: async (id, template) => {
      try {
        const response = await createMod(id, template);
        if (!response.ok) {
          return false;
        }

        if (response.mods) {
          applyMods(response.mods);
        } else {
          await get().refreshMods();
        }

        set({
          showCreateDialog: false,
          selectedModId: id,
        });
        return true;
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
        return false;
      }
    },

    generateSlnForMod: async (modId) => {
      try {
        const response = await generateSln(modId);
        return response.ok ? (response.slnPath ?? null) : null;
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
        return null;
      }
    },

    addSource: async (path) => {
      try {
        const response = await addWorkspaceSource(path);
        const mods = await fetchMods();
        applyServerState(response.state, {
          mods,
          preserveSelection: true,
        });
        return true;
      } catch (error) {
        set({ buildState: "error", buildLog: getErrorMessage(error) });
        return false;
      }
    },
  };
});
