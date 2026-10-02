// Agent 工具直调层：不经过 LLM 也能跑的第一批编辑器工具。
// 全部走 Editor.Bridge（5299）与游戏进程内 agent-bridge（47921）两根既有管线。

export type ToolResult = {
  key: string;
  title: string;
  ok: boolean;
  summary: string;
  detail?: string[];
};

async function getJson(path: string): Promise<unknown> {
  const response = await fetch(path, { cache: 'no-store' });
  return response.json();
}

export async function listMods(): Promise<ToolResult> {
  const data = (await getJson('/api/mods')) as { mods?: Array<{ id: string }> };
  const mods = data.mods ?? [];
  return {
    key: `mods:${Date.now()}`,
    title: 'Mod 目录',
    ok: true,
    summary: `共 ${mods.length} 个 Mod。`,
    detail: mods.slice(0, 8).map((mod) => mod.id),
  };
}

export async function launcherState(): Promise<ToolResult> {
  const data = (await getJson('/api/launcher/state')) as {
    ok?: boolean;
    state?: { selectedPlatformId?: string; selectedPresetId?: string | null; presets?: Array<{ id: string }> };
    mods?: Array<{ id: string }>;
  };
  if (!data.ok || !data.state) {
    return { key: `state:${Date.now()}`, title: '启动器状态', ok: false, summary: '拿不到状态：桥没连上。' };
  }
  const preset = data.state.selectedPresetId
    ? (data.state.presets ?? []).find((row) => row.id === data.state.selectedPresetId)?.id ?? data.state.selectedPresetId
    : '（未选）';
  return {
    key: `state:${Date.now()}`,
    title: '启动器状态',
    ok: true,
    summary: `平台 ${data.state.selectedPlatformId ?? '?'} · preset ${preset} · 可选 Mod ${data.mods?.length ?? 0} 个。`,
  };
}

export async function pingAgentBridge(): Promise<ToolResult> {
  try {
    await fetch('/agent-bridge/rpc', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'ludots.graph.debug', params: { op: 'list' } }),
    });
    return {
      key: `bridge:${Date.now()}`,
      title: 'live debug 通道',
      ok: true,
      summary: '游戏进程在跑，agent-bridge（47921）已通。去蓝图房看实时图执行。',
    };
  } catch {
    return {
      key: `bridge:${Date.now()}`,
      title: 'live debug 通道',
      ok: false,
      summary: '没有运行中的游戏。去「运行」页开局后这里会通。',
    };
  }
}

export async function validateTextBank(modId: string): Promise<ToolResult> {
  const empty: ToolResult = {
    key: `validate:${modId}:${Date.now()}`,
    title: `引擎校验 · ${modId} 文本表`,
    ok: false,
    summary: '',
  };
  try {
    const tokensJson = (await getJson(`/api/mods/${encodeURIComponent(modId)}/story/catalogs/text_tokens`)) as {
      ok?: boolean;
      items?: unknown;
    };
    const localesJson = (await getJson(`/api/mods/${encodeURIComponent(modId)}/story/catalogs/text_locales`)) as {
      ok?: boolean;
      items?: unknown;
    };
    if (!tokensJson.ok) {
      return { ...empty, summary: `这个 Mod 没有文本表（text_tokens 缺失）。` };
    }
    const response = await fetch(`/api/mods/${encodeURIComponent(modId)}/story/text/validate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokens: tokensJson.items, locales: localesJson.items ?? {} }),
    });
    const data = (await response.json()) as { ok?: boolean; error?: string };
    if (data.ok) {
      return { ...empty, ok: true, summary: '引擎同源校验通过：这份文本表游戏能装载。' };
    }
    return { ...empty, summary: `校验未通过：${data.error ?? '未知错误'}` };
  } catch {
    return { ...empty, summary: '请求失败：桥没连上。' };
  }
}

export const AGENT_TOOLS: Array<{ id: string; label: string; run: () => Promise<ToolResult> }> = [
  { id: 'mods', label: '列 Mod 目录', run: listMods },
  { id: 'state', label: '启动器状态', run: launcherState },
  { id: 'bridge', label: '探 live debug', run: pingAgentBridge },
];
