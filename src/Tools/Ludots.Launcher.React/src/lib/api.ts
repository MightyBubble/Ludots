const BASE = "http://localhost:5299";

export interface ModInfo {
  id: string;
  name: string;
  version: string;
  priority: number;
  dependencies: Record<string, string>;
  rootPath: string;
  description: string;
  author: string;
  tags: string[];
  changelogFile: string;
  hasThumbnail: boolean;
  hasReadme: boolean;
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

export async function fetchReadme(modId: string): Promise<string | null> {
  try {
    const r = await fetch(`${BASE}/api/mods/${modId}/readme`);
    const j = await r.json();
    return j.ok ? j.content : null;
  } catch {
    return null;
  }
}

export async function fetchChangelog(modId: string): Promise<string | null> {
  try {
    const r = await fetch(`${BASE}/api/mods/${modId}/changelog`);
    const j = await r.json();
    return j.ok ? j.content : null;
  } catch {
    return null;
  }
}

export async function fetchWorkspaceSources(): Promise<string[]> {
  try {
    const r = await fetch(`${BASE}/api/workspace`);
    const j = await r.json();
    return j.ok ? j.sources : [];
  } catch {
    return [];
  }
}

export async function addWorkspaceSource(path: string): Promise<boolean> {
  try {
    const r = await fetch(`${BASE}/api/workspace/add-source`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path }),
    });
    const j = await r.json();
    return j.ok === true;
  } catch {
    return false;
  }
}

export function thumbnailUrl(modId: string): string {
  return `${BASE}/api/mods/${modId}/thumbnail`;
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
