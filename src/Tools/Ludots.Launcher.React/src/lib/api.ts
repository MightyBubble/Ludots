const BASE = "http://localhost:5299";

export interface ModInfo {
  id: string;
  name: string;
  version: string;
  priority: number;
  dependencies: Record<string, string>;
  rootPath: string;
  relativePath: string;
  layerPath: string;
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

export async function createMod(id: string, template: string): Promise<{ ok: boolean; output?: string; error?: string }> {
  const r = await fetch(`${BASE}/api/mods/create`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id, template }),
  });
  return r.json();
}

export async function buildMod(modId: string): Promise<{ ok: boolean; exitCode?: number; output?: string }> {
  const r = await fetch(`${BASE}/api/mods/${modId}/build`, { method: "POST" });
  return r.json();
}

export async function buildAllMods(modIds: string[]): Promise<{ ok: boolean; results?: Array<{ id: string; ok: boolean; output?: string }> }> {
  const r = await fetch(`${BASE}/api/mods/build-all`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ modIds }),
  });
  return r.json();
}

export async function launchGame(presetId?: string, modPaths?: string[]): Promise<{ ok: boolean; pid?: number; error?: string }> {
  const body: Record<string, unknown> = {};
  if (presetId) body.presetId = presetId;
  if (modPaths) body.modPaths = modPaths;
  const r = await fetch(`${BASE}/api/launch`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return r.json();
}

export async function generateSln(modId: string): Promise<{ ok: boolean; slnPath?: string; error?: string }> {
  const r = await fetch(`${BASE}/api/mods/generate-sln`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ modId }),
  });
  return r.json();
}
