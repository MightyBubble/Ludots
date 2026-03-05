import { create } from "zustand";
import { fetchMods, fetchPresets, checkHealth, type ModInfo, type GamePreset } from "@/lib/api";

interface LauncherState {
  mods: ModInfo[];
  presets: GamePreset[];
  selectedPresetId: string | null;
  selectedModId: string | null;
  bridgeOnline: boolean;
  loading: boolean;

  init: () => Promise<void>;
  selectPreset: (id: string) => void;
  selectMod: (id: string | null) => void;
}

export const useLauncherStore = create<LauncherState>((set) => ({
  mods: [],
  presets: [],
  selectedPresetId: null,
  selectedModId: null,
  bridgeOnline: false,
  loading: true,

  init: async () => {
    set({ loading: true });
    const online = await checkHealth();
    if (!online) {
      set({ bridgeOnline: false, loading: false });
      return;
    }
    const [mods, presets] = await Promise.all([fetchMods(), fetchPresets()]);
    set({
      mods,
      presets,
      bridgeOnline: true,
      loading: false,
      selectedPresetId: presets.length > 0 ? presets[0].id : null,
    });
  },

  selectPreset: (id) => set({ selectedPresetId: id }),
  selectMod: (id) => set({ selectedModId: id }),
}));
