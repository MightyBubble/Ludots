export type LauncherBuildState =
  | "NoProject"
  | "Idle"
  | "Outdated"
  | "Building"
  | "Succeeded"
  | "Failed";

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
  mainAssemblyPath: string;
  projectPath: string;
  hasProject: boolean;
  buildState: LauncherBuildState;
  lastBuildMessage: string;
  kind: "ResourceOnly" | "BinaryOnly" | "BuildableSource";
  bindingNames: string[];
}

export interface LauncherPreset {
  id: string;
  name: string;
  selectors: string[];
  adapterId: string;
  buildMode: string;
  activeModIds: string[];
  includeDependencies: boolean;
}

export interface LauncherBindingInfo {
  name: string;
  targetType: string;
  targetValue: string;
  projectPath: string | null;
}

export interface PlatformProfile {
  id: string;
  name: string;
  appProjectPath: string;
  outputDirectory: string;
  clientProjectDirectory: string;
  clientDistributionDirectory: string;
  launchUrl: string;
  runtimeBootstrapFileName: string;
}

export interface LauncherStateSnapshot {
  platforms: PlatformProfile[];
  selectedPlatformId: string;
  presets: LauncherPreset[];
  selectedPresetId: string | null;
  workspaceSources: string[];
  bindings: LauncherBindingInfo[];
}

export interface LauncherSnapshotResponse {
  ok: boolean;
  state: LauncherStateSnapshot;
  mods: ModInfo[];
}

export interface BuildResult {
  id: string;
  ok: boolean;
  exitCode: number;
  output: string;
}

export interface LaunchResult {
  ok: boolean;
  pid?: number;
  url?: string;
  error?: string;
}

export interface AppBuildResult {
  id: string;
  ok: boolean;
  exitCode: number;
  output: string;
}

interface OkResponse {
  ok: boolean;
  error?: string;
}

async function readJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  const data = (await response.json()) as T;
  if (!response.ok) {
    throw new Error(extractErrorMessage(data) ?? `Request failed: ${response.status}`);
  }

  return data;
}

function extractErrorMessage(data: unknown): string | null {
  if (!data || typeof data !== "object") {
    return null;
  }

  const record = data as Record<string, unknown>;
  return typeof record.error === "string" ? record.error : null;
}

export async function fetchLauncherSnapshot(): Promise<LauncherSnapshotResponse> {
  return readJson<LauncherSnapshotResponse>("/api/launcher/state");
}

export async function fetchMods(): Promise<ModInfo[]> {
  const response = await readJson<{ ok: boolean; mods: ModInfo[] }>("/api/mods");
  return response.mods;
}

export async function fetchReadme(modId: string): Promise<string | null> {
  try {
    const response = await readJson<{ ok: boolean; content: string }>(`/api/mods/${modId}/readme`);
    return response.content;
  } catch {
    return null;
  }
}

export async function fetchChangelog(modId: string): Promise<string | null> {
  try {
    const response = await readJson<{ ok: boolean; content: string }>(`/api/mods/${modId}/changelog`);
    return response.content;
  } catch {
    return null;
  }
}

export async function fetchWorkspaceSources(): Promise<string[]> {
  try {
    const response = await readJson<{ ok: boolean; sources: string[] }>("/api/workspace");
    return response.sources;
  } catch {
    return [];
  }
}

export async function addWorkspaceSource(path: string): Promise<{ state: LauncherStateSnapshot }> {
  return readJson<{ ok: boolean; state: LauncherStateSnapshot }>("/api/workspace/add-source", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ path }),
  });
}

export function thumbnailUrl(modId: string): string {
  return `/api/mods/${modId}/thumbnail`;
}

export async function checkHealth(): Promise<boolean> {
  try {
    const response = await readJson<{ ok: boolean }>("/health");
    return response.ok === true;
  } catch {
    return false;
  }
}

export async function createMod(
  id: string,
  template: string,
): Promise<{ ok: boolean; output?: string; error?: string; mods?: ModInfo[] }> {
  return readJson<{ ok: boolean; output?: string; error?: string; mods?: ModInfo[] }>("/api/mods/create", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id, template }),
  });
}

export async function buildMod(
  modId: string,
): Promise<{ ok: boolean; result?: BuildResult; error?: string; mods?: ModInfo[] }> {
  return readJson<{ ok: boolean; result?: BuildResult; error?: string; mods?: ModInfo[] }>(`/api/mods/${modId}/build`, {
    method: "POST",
  });
}

export async function buildAllMods(
  modIds: string[],
): Promise<{ ok: boolean; results?: BuildResult[]; error?: string; mods?: ModInfo[] }> {
  return readJson<{ ok: boolean; results?: BuildResult[]; error?: string; mods?: ModInfo[] }>("/api/mods/build-all", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ modIds }),
  });
}

export async function buildApp(
  platformId: string,
): Promise<{ ok: boolean; result?: AppBuildResult; error?: string }> {
  return readJson<{ ok: boolean; result?: AppBuildResult; error?: string }>("/api/app/build", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ platformId }),
  });
}

export async function fixProject(
  modId: string,
): Promise<{ ok: boolean; projectPath?: string; error?: string; mods?: ModInfo[] }> {
  return readJson<{ ok: boolean; projectPath?: string; error?: string; mods?: ModInfo[] }>(
    `/api/mods/${modId}/fix-project`,
    { method: "POST" },
  );
}

export async function launchGame(platformId: string, modIds: string[]): Promise<LaunchResult> {
  return readJson<LaunchResult>("/api/launch", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ platformId, modIds }),
  });
}

export async function generateSln(
  modId: string,
): Promise<{ ok: boolean; slnPath?: string; error?: string }> {
  return readJson<{ ok: boolean; slnPath?: string; error?: string }>("/api/mods/generate-sln", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ modId }),
  });
}

export async function savePreset(payload: {
  presetId?: string;
  name: string;
  activeModIds: string[];
  includeDependencies?: boolean;
  selectAfterSave?: boolean;
}): Promise<{ ok: boolean; preset: LauncherPreset; state: LauncherStateSnapshot }> {
  return readJson<{ ok: boolean; preset: LauncherPreset; state: LauncherStateSnapshot }>("/api/presets", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}

export async function selectPreset(
  presetId: string,
): Promise<{ ok: boolean; state: LauncherStateSnapshot }> {
  return readJson<{ ok: boolean; state: LauncherStateSnapshot }>("/api/presets/select", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ presetId }),
  });
}

export async function deletePreset(
  presetId: string,
): Promise<{ ok: boolean; state: LauncherStateSnapshot }> {
  return readJson<{ ok: boolean; state: LauncherStateSnapshot }>(`/api/presets/${presetId}`, {
    method: "DELETE",
  });
}

export async function selectPlatform(
  platformId: string,
): Promise<{ ok: boolean; state: LauncherStateSnapshot }> {
  return readJson<{ ok: boolean; state: LauncherStateSnapshot }>("/api/platforms/select", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ platformId }),
  });
}

export function isOkResponse(value: OkResponse): boolean {
  return value.ok === true;
}
