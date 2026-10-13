import { useCallback, useEffect, useMemo, useState } from 'react';
import { Button } from '@/components/ui/Button';
import { labelClass } from '@/components/ui/chrome';
import { fieldControlClass } from '@/components/ui/Field';
import { pageClass } from '@/components/ui/chrome';

type LauncherPreset = {
  id: string;
  name: string;
  activeModIds: string[];
};

type PlatformProfile = { id: string; name: string };

type LauncherStateSnapshot = {
  platforms: PlatformProfile[];
  selectedPlatformId: string;
  presets: LauncherPreset[];
  selectedPresetId: string | null;
};

type ModInfo = { id: string; name: string; kind: string };

type LaunchResult = {
  ok: boolean;
  pid?: number;
  url?: string;
  error?: string;
  plan?: { orderedModIds: string[]; diagnostics: { warnings: string[] } };
};

async function readJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  const data = (await response.json()) as T;
  return data;
}

async function pingAgentBridge(): Promise<boolean> {
  try {
    // 任何 HTTP 应答（含 JSON-RPC 错误应答）都证明游戏进程内的 agent bridge 在听。
    await fetch('/agent-bridge/rpc', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'ludots.graph.debug', params: { op: 'list' } }),
    });
    return true;
  } catch {
    return false;
  }
}

export function RunPage() {
  const [snapshot, setSnapshot] = useState<LauncherStateSnapshot | null>(null);
  const [mods, setMods] = useState<ModInfo[]>([]);
  const [selectedModIds, setSelectedModIds] = useState<Set<string>>(new Set());
  const [customSelection, setCustomSelection] = useState(false);
  const [filter, setFilter] = useState('');
  const [launching, setLaunching] = useState(false);
  const [lastLaunch, setLastLaunch] = useState<LaunchResult | null>(null);
  const [error, setError] = useState('');
  const [gameUp, setGameUp] = useState<boolean | null>(null);

  const applySnapshot = useCallback((state: LauncherStateSnapshot, resetSelection: boolean) => {
    setSnapshot(state);
    if (!resetSelection) return;
    const preset = state.presets.find((row) => row.id === state.selectedPresetId);
    setSelectedModIds(new Set(preset?.activeModIds ?? []));
    setCustomSelection(false);
  }, []);

  const loadSnapshot = useCallback(
    async (resetSelection: boolean) => {
      try {
        const data = await readJson<{ ok: boolean; state: LauncherStateSnapshot; mods?: ModInfo[] }>(
          '/api/launcher/state',
        );
        if (!data.ok) return;
        applySnapshot(data.state, resetSelection);
        if (Array.isArray(data.mods) && data.mods.length > 0) setMods(data.mods);
      } catch {
        setError('拿不到启动器状态：桥没连上。');
      }
    },
    [applySnapshot],
  );

  useEffect(() => {
    void loadSnapshot(true);
  }, [loadSnapshot]);

  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      const up = await pingAgentBridge();
      if (!cancelled) setGameUp(up);
    };
    void tick();
    const timer = window.setInterval(() => void tick(), 2500);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  const visibleMods = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (!needle) return mods;
    return mods.filter(
      (mod) => mod.id.toLowerCase().includes(needle) || (mod.name ?? '').toLowerCase().includes(needle),
    );
  }, [mods, filter]);

  const selectPreset = async (presetId: string) => {
    if (!presetId) {
      setCustomSelection(true);
      return;
    }
    try {
      const data = await readJson<{ ok: boolean; state: LauncherStateSnapshot }>('/api/presets/select', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ presetId }),
      });
      if (data.ok) applySnapshot(data.state, true);
    } catch {
      setError('选 preset 失败。');
    }
  };

  const selectPlatform = async (platformId: string) => {
    try {
      const data = await readJson<{ ok: boolean; state: LauncherStateSnapshot }>('/api/platforms/select', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ platformId }),
      });
      if (data.ok) applySnapshot(data.state, false);
    } catch {
      setError('选平台失败。');
    }
  };

  const toggleMod = (modId: string) => {
    setSelectedModIds((prev) => {
      const next = new Set(prev);
      if (next.has(modId)) next.delete(modId);
      else next.add(modId);
      return next;
    });
    setCustomSelection(true);
  };

  const launch = async () => {
    if (!snapshot || selectedModIds.size === 0) return;
    setLaunching(true);
    setError('');
    try {
      const usePreset = !customSelection && snapshot.selectedPresetId;
      const body = usePreset
        ? { platformId: snapshot.selectedPlatformId, presetId: snapshot.selectedPresetId }
        : { platformId: snapshot.selectedPlatformId, modIds: [...selectedModIds] };
      const data = await readJson<LaunchResult>('/api/launch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      setLastLaunch(data);
      if (!data.ok) setError(data.error ?? '开局失败。');
    } catch {
      setError('开局请求失败：桥没连上。');
    } finally {
      setLaunching(false);
    }
  };

  const platformId = snapshot?.selectedPlatformId ?? '';
  const presetId = customSelection ? '' : (snapshot?.selectedPresetId ?? '');

  return (
    <div className={`${pageClass} overflow-auto p-6 font-sans`}>
      <div className="mb-4 flex items-center gap-4 flex-wrap">
        <h1 className="text-xl text-studio-label">开局</h1>
        <span className="text-xs text-studio-muted">
          勾 Mod、选 preset 与平台，一键开局。再开一局会替换上一局；改完内容要重开才生效。
        </span>
      </div>

      <div className="grid grid-cols-12 gap-4">
        <aside className="col-span-4 space-y-3">
          <label className={labelClass}>
            平台
            <select
              className={fieldControlClass}
              value={platformId}
              onChange={(e) => void selectPlatform(e.target.value)}
            >
              {(snapshot?.platforms ?? []).map((platform) => (
                <option key={platform.id} value={platform.id}>
                  {platform.name}
                </option>
              ))}
            </select>
          </label>
          <label className={labelClass}>
            Preset
            <select className={fieldControlClass} value={presetId} onChange={(e) => void selectPreset(e.target.value)}>
              <option value="">（自选组合，不存 preset）</option>
              {(snapshot?.presets ?? []).map((preset) => (
                <option key={preset.id} value={preset.id}>
                  {preset.name}
                </option>
              ))}
            </select>
          </label>
          <Button
            variant="primary"
            className="w-full"
            disabled={launching || !platformId || selectedModIds.size === 0}
            onClick={() => void launch()}
          >
            {launching ? '正在拉起…' : `开局（${selectedModIds.size} 个 Mod）`}
          </Button>
          {error ? <p className="text-xs text-studio-red">{error}</p> : null}

          <div className="rounded-md border border-studio-elevated bg-studio-surface p-3 text-xs">
            <div className="text-studio-muted">上次开局</div>
            {lastLaunch ? (
              lastLaunch.ok ? (
                <div className="mt-1 space-y-1 text-studio-secondary">
                  <div>
                    进程 pid <span className="font-mono text-studio-label">{lastLaunch.pid}</span>
                    {lastLaunch.url ? <> · <span className="font-mono">{lastLaunch.url}</span></> : null}
                  </div>
                  <div>装载 {lastLaunch.plan?.orderedModIds.length ?? '?'} 个 Mod（按依赖序）</div>
                  {(lastLaunch.plan?.diagnostics.warnings ?? []).length > 0 ? (
                    <ul className="list-disc pl-4 text-studio-yellow">
                      {lastLaunch.plan!.diagnostics.warnings.slice(0, 5).map((warning) => (
                        <li key={warning}>{warning}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              ) : (
                <div className="mt-1 text-studio-red">{lastLaunch.error ?? '开局失败。'}</div>
              )
            ) : (
              <div className="mt-1 text-studio-muted">还没开过。</div>
            )}
          </div>

          <div
            data-run-agent-bridge={gameUp === null ? 'unknown' : gameUp ? 'up' : 'down'}
            className={`rounded-md px-3 py-2 text-xs ${
              gameUp
                ? 'border border-studio-blue/40 bg-studio-blue/10 text-studio-blue'
                : 'border border-studio-elevated bg-studio-surface text-studio-muted'
            }`}
          >
            {gameUp
              ? '游戏在跑 · live debug 通道已通（去蓝图房看实时图执行）'
              : '未检测到运行中的游戏。开局后这里会点亮 agent bridge。'}
          </div>
        </aside>

        <main className="col-span-8 space-y-2">
          <input
            className={fieldControlClass}
            placeholder="搜 Mod id 或名字…"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
          />
          <ul className="max-h-[70vh] space-y-1 overflow-auto rounded-md border border-studio-elevated p-2">
            {visibleMods.map((mod) => {
              const on = selectedModIds.has(mod.id);
              return (
                <li key={mod.id}>
                  <label className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1 text-xs hover:bg-studio-elevated">
                    <input type="checkbox" checked={on} onChange={() => toggleMod(mod.id)} />
                    <span className={on ? 'text-studio-label' : 'text-studio-secondary'}>{mod.id}</span>
                    <span className="text-[10px] text-studio-muted">{mod.name}</span>
                    <span className="ml-auto rounded bg-studio-elevated px-1.5 py-0.5 text-[10px] text-studio-muted">
                      {mod.kind}
                    </span>
                  </label>
                </li>
              );
            })}
            {visibleMods.length === 0 ? <li className="px-2 py-4 text-xs text-studio-muted">没有匹配的 Mod。</li> : null}
          </ul>
        </main>
      </div>
    </div>
  );
}

export default RunPage;
