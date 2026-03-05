const BASE = "http://localhost:5299";

export interface ModInfo {
  id: string;
  name: string;
  version: string;
  priority: number;
  dependencies: Record<string, string>;
  rootPath: string;
}

export interface GamePreset {
  id: string;
  filePath: string;
  windowTitle: string;
  modPaths: string[];
}

export async function fetchMods(): Promise<ModInfo[]> {
  const r = await fetch(`${BASE}/api/mods`);
  const j = await r.json();
  return j.ok ? j.mods : [];
}

export async function fetchPresets(): Promise<GamePreset[]> {
  const r = await fetch(`${BASE}/api/presets`);
  const j = await r.json();
  return j.ok ? j.presets : [];
}

export async function checkHealth(): Promise<boolean> {
  try {
    const r = await fetch(`${BASE}/health`);
    const j = await r.json();
    return j.ok === true;
  } catch {
    return false;
  }
}
