import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { STUDIO_CHROME } from './authoring-studio/authoringTheme';

type ModInfo = { id: string; name?: string };
type Issue = { path?: string; message?: string; sourceMod?: string; sourceFile?: string };
type Owner = { file: string; path: string; sourceMod?: string; sourceFile?: string };
type HelpItem = { topic: string; text: string };
type Area = { id: number; name: string };
type CellRow = Record<string, number | null>;
type CostMatrix = { areas?: Area[]; agentTypes?: string[]; cells?: Record<string, CellRow> };
type Snapshot = {
  ok?: boolean;
  modId?: string;
  mapId?: string;
  mapOwner?: string | null;
  mapOwnedBySelection?: boolean;
  fixedHz?: number;
  bundleVersion?: number;
  scenario?: unknown;
  globals?: unknown;
  templates?: unknown;
  debug?: unknown;
  debugView?: unknown;
  defaults?: Record<string, unknown>;
  owners?: Owner[];
  costMatrix?: CostMatrix;
  help?: HelpItem[];
  issues?: Issue[];
};

const TABS = [
  { id: 'scenario', label: '场景实例' },
  { id: 'globals', label: '全局参数' },
  { id: 'templates', label: '实体模板' },
  { id: 'costMatrix', label: '代价矩阵' },
  { id: 'bundle', label: '完整包' },
  { id: 'debug', label: '调试参数' },
  { id: 'debugView', label: '调试视图' },
] as const;

type TabId = (typeof TABS)[number]['id'];

const LIVE_METHOD: Partial<Record<TabId, string>> = {
  scenario: 'ludots.crowd.config.rebuildWorld',
  globals: 'ludots.crowd.config.rebuildWorld',
  templates: 'ludots.crowd.config.rebuildWorld',
  costMatrix: 'ludots.crowd.config.rebuildWorld',
  debug: 'ludots.crowd.config.applyDebug',
  debugView: 'ludots.crowd.config.applyDebugView',
};

function pretty(value: unknown): string {
  return JSON.stringify(value ?? {}, null, 2);
}

function readMods(payload: { mods?: unknown }): ModInfo[] {
  const raw = Array.isArray(payload.mods) ? payload.mods : [];
  return raw
    .map((item) => {
      const row = item as { id?: string; Id?: string; name?: string; Name?: string };
      return { id: row.id ?? row.Id ?? '', name: row.name ?? row.Name };
    })
    .filter((mod) => mod.id.length > 0);
}

function applyLabel(tab: TabId): string {
  if (tab === 'debug') return '应用调试参数';
  if (tab === 'debugView') return '应用调试视图';
  if (tab === 'bundle') return '导入';
  return '应用并重建世界';
}

type AgentComponent = {
  profileId?: string;
  speedCmPerSecond?: number;
  personalRadiusCm?: number;
  pushPriority?: number;
};

type UnitDraft = {
  id?: string;
  components?: { CrowdSimulationAgent?: AgentComponent };
};

export function CrowdConfigEditorPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedMod = searchParams.get('mod') ?? 'CrowdSimulationS4DeployMod';
  const [mods, setMods] = useState<ModInfo[]>([]);
  const [modId, setModId] = useState(requestedMod);
  const [tab, setTab] = useState<TabId>('scenario');
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [texts, setTexts] = useState<Record<string, string>>({});
  const [cells, setCells] = useState<Record<string, CellRow>>({});
  const [bundleText, setBundleText] = useState('');
  const [issues, setIssues] = useState<Issue[]>([]);
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [templateId, setTemplateId] = useState('');
  const [bridgePort, setBridgePort] = useState('47921');

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetch('/api/mods');
        const json = (await res.json()) as { mods?: unknown };
        if (cancelled) return;
        const list = readMods(json);
        setMods(list);
        setModId((current) => {
          if (current && list.some((mod) => mod.id === current)) return current;
          if (requestedMod && list.some((mod) => mod.id === requestedMod)) return requestedMod;
          return list.find((mod) => mod.id === 'CrowdSimulationS4DeployMod')?.id ?? list[0]?.id ?? '';
        });
      } catch (err) {
        if (!cancelled) setStatus(err instanceof Error ? err.message : '桥没有返回 mod 列表');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [requestedMod]);

  useEffect(() => {
    if (!modId) return;
    let cancelled = false;
    setLoading(true);
    void (async () => {
      try {
        const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/crowd-config`);
        const json = (await res.json()) as Snapshot;
        if (cancelled) return;
        setSnapshot(json);
        setTexts({
          scenario: pretty(json.scenario),
          globals: pretty(json.globals),
          templates: pretty(json.templates),
          debug: pretty(json.debug),
          debugView: pretty(json.debugView),
        });
        setCells(json.costMatrix?.cells ?? {});
        setIssues(json.issues ?? []);
        const units = ((json.templates as { unitTypes?: UnitDraft[] } | undefined)?.unitTypes ?? []).filter(
          (unit) => unit.components?.CrowdSimulationAgent,
        );
        setTemplateId(units[0]?.id ?? '');
        if (json.ok === false) setStatus('这份配置没有读出来');
      } catch (err) {
        if (!cancelled) setStatus(err instanceof Error ? err.message : '群体配置没有读出来');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [modId]);

  const units = useMemo(() => {
    try {
      const parsed = JSON.parse(texts.templates || '{}') as { unitTypes?: UnitDraft[] };
      return (parsed.unitTypes ?? []).filter((unit) => unit.id && unit.components?.CrowdSimulationAgent);
    } catch {
      return [];
    }
  }, [texts.templates]);

  const selectedUnit = units.find((unit) => unit.id === templateId) ?? units[0];
  const agent = selectedUnit?.components?.CrowdSimulationAgent;

  function setText(layer: string, value: string) {
    setTexts((current) => ({ ...current, [layer]: value }));
  }

  function parseLayer(layer: TabId): unknown {
    if (layer === 'costMatrix') {
      return {
        areas: snapshot?.costMatrix?.areas ?? [],
        agentTypes: snapshot?.costMatrix?.agentTypes ?? [],
        cells,
      };
    }
    return JSON.parse(texts[layer] || '{}');
  }

  async function post(path: string, body: unknown): Promise<Snapshot & { written?: string[]; lost?: string[]; rebuilt?: boolean }> {
    const res = await fetch(path, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
    });
    return (await res.json()) as Snapshot & { written?: string[]; lost?: string[]; rebuilt?: boolean };
  }

  async function reload() {
    const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/crowd-config`);
    const json = (await res.json()) as Snapshot;
    setSnapshot(json);
    setCells(json.costMatrix?.cells ?? {});
  }

  async function validate() {
    setBusy(true);
    setStatus('');
    try {
      const draft = parseLayer(tab);
      const json = await post(`/api/mods/${encodeURIComponent(modId)}/crowd-config/validate`, { layer: tab, draft });
      setIssues(json.issues ?? []);
      setStatus(json.ok ? '校验通过' : '校验没有通过，没有写文件，也没有重建。');
    } catch (err) {
      setIssues([{ path: '', message: err instanceof Error ? err.message : '草稿不是 JSON' }]);
    } finally {
      setBusy(false);
    }
  }

  async function apply() {
    setBusy(true);
    setStatus('');
    try {
      const draft = parseLayer(tab);
      const json = await post(`/api/mods/${encodeURIComponent(modId)}/crowd-config/apply`, { layer: tab, draft });
      setIssues(json.issues ?? []);
      if (!json.ok) {
        setStatus('校验没有通过，没有写文件，也没有重建。');
        return;
      }
      const written = (json.written ?? []).join('、') || '没有需要写入的差异';
      const method = LIVE_METHOD[tab];
      if (!method) {
        setStatus(`已写入 ${written}`);
        await reload();
        return;
      }
      try {
        const rpc = await fetch(`http://127.0.0.1:${bridgePort}/rpc`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ jsonrpc: '2.0', id: 1, method, params: {} }),
        });
        const payload = (await rpc.json()) as { result?: { ok?: boolean; issues?: Issue[]; lost?: string[]; rebuilt?: boolean }; error?: { message?: string } };
        if (payload.error) {
          setStatus(`已写入 ${written}。运行中的桥拒绝了：${payload.error.message ?? ''}`);
        } else if (payload.result?.ok === false) {
          setIssues(payload.result.issues ?? []);
          setStatus(`已写入 ${written}。运行中的局没有改，因为校验没有通过。`);
        } else if (payload.result?.rebuilt) {
          setStatus(`已写入 ${written}。世界已重建。丢掉的是：${(payload.result.lost ?? []).join('、')}`);
        } else {
          setStatus(`已写入 ${written}。运行中的局已按这份生效。`);
        }
      } catch {
        setStatus(`已写入 ${written}。没有运行中的桥，所以没有当场生效。`);
      }
      await reload();
    } catch (err) {
      setIssues([{ path: '', message: err instanceof Error ? err.message : '草稿不是 JSON' }]);
    } finally {
      setBusy(false);
    }
  }

  function resetDraft() {
    if (tab === 'costMatrix') {
      const defaults = snapshot?.defaults?.costMatrix as CostMatrix | undefined;
      setCells(defaults?.cells ?? snapshot?.costMatrix?.cells ?? {});
      setStatus('草稿已回到默认。文件还没改。');
      return;
    }
    if (tab === 'bundle') return;
    const defaults = snapshot?.defaults?.[tab];
    setText(tab, pretty(defaults));
    setStatus('草稿已回到默认。文件还没改。');
  }

  function updateAgent(field: keyof AgentComponent, raw: string) {
    if (!selectedUnit?.id) return;
    let parsed: { unitTypes?: UnitDraft[] };
    try {
      parsed = JSON.parse(texts.templates || '{}') as { unitTypes?: UnitDraft[] };
    } catch {
      setIssues([{ path: 'unitTypes', message: '实体模板草稿不是 JSON，检查器写不回去。' }]);
      return;
    }
    const list = parsed.unitTypes ?? [];
    const unit = list.find((item) => item.id === selectedUnit.id);
    const component = unit?.components?.CrowdSimulationAgent;
    if (!component) return;
    if (field === 'profileId') component.profileId = raw;
    else component[field] = Number(raw);
    setText('templates', pretty(parsed));
  }

  async function exportBundle() {
    setBusy(true);
    try {
      const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/crowd-config/bundle`);
      const json = await res.json();
      setBundleText(pretty(json));
      setStatus(`导出的 version 是 ${snapshot?.bundleVersion ?? ''}`);
    } finally {
      setBusy(false);
    }
  }

  async function importBundle() {
    setBusy(true);
    setStatus('');
    try {
      const bundle = JSON.parse(bundleText || '{}') as unknown;
      const json = await post(`/api/mods/${encodeURIComponent(modId)}/crowd-config/bundle`, { bundle, write: true });
      setIssues(json.issues ?? []);
      setStatus(json.ok ? `已写入 ${(json.written ?? []).join('、') || '完整包'}` : '完整包没有导入。');
      if (json.ok) await reload();
    } catch (err) {
      setIssues([{ path: 'bundle', message: err instanceof Error ? err.message : '完整包不是 JSON' }]);
    } finally {
      setBusy(false);
    }
  }

  const defaultsText = tab === 'bundle' || tab === 'costMatrix' ? '' : pretty(snapshot?.defaults?.[tab]);
  const areas = snapshot?.costMatrix?.areas ?? [];
  const agentTypes = snapshot?.costMatrix?.agentTypes ?? Object.keys(cells);

  return (
    <div className="h-full overflow-auto bg-studio-bg px-6 py-6 text-studio-label">
      <div className="mx-auto max-w-6xl">
        <h1 className="text-2xl font-semibold">群体配置</h1>
        <p className="mt-2 max-w-3xl text-sm leading-relaxed text-studio-secondary">
          场景、全局、模板来自加载顺序里的合并结果。写入只落在当前选中的 Mod。空的代价格子是不可通行。完整包的 version 必须正好等于 {snapshot?.bundleVersion ?? '…'}，没有迁移。
        </p>

        <div className="mt-4 flex flex-wrap items-end gap-3">
          <label className={STUDIO_CHROME.label}>
            Mod
            <select
              className={STUDIO_CHROME.field}
              value={modId}
              onChange={(event) => {
                const next = event.target.value;
                setModId(next);
                setSearchParams(next ? { mod: next } : {});
              }}
            >
              {mods.map((mod) => (
                <option key={mod.id} value={mod.id}>
                  {mod.id}
                </option>
              ))}
            </select>
          </label>
          <p className="text-xs text-studio-muted">
            地图 {snapshot?.mapId ?? '…'} · 地图文件属于 {snapshot?.mapOwner ?? '（没有文件）'}
            {snapshot && snapshot.mapOwnedBySelection === false ? ' · 当前 Mod 不能改这份地图' : ''}
            · FixedHz {snapshot?.fixedHz ?? '…'}
          </p>
          <label className={STUDIO_CHROME.label}>
            运行桥端口
            <input
              className={STUDIO_CHROME.field}
              value={bridgePort}
              onChange={(event) => setBridgePort(event.target.value)}
              data-live-bridge-port=""
            />
          </label>
        </div>

        <ul className="mt-4 flex flex-wrap gap-2">
          {TABS.map((item) => (
            <li key={item.id}>
              <button
                type="button"
                data-crowd-tab={item.id}
                className={tab === item.id ? STUDIO_CHROME.navOn : STUDIO_CHROME.navOff}
                onClick={() => setTab(item.id)}
              >
                {item.label}
              </button>
            </li>
          ))}
        </ul>

        <ul className="mt-4 grid gap-2 sm:grid-cols-2">
          {(snapshot?.help ?? []).map((item) => (
            <li key={item.topic} className="rounded-md border border-studio-elevated bg-studio-surface px-3 py-2 text-xs leading-relaxed">
              <span className="text-studio-blue">{item.topic}</span>
              <span className="text-studio-secondary"> {item.text}</span>
            </li>
          ))}
        </ul>

        {loading ? <p className="mt-4 text-sm text-studio-muted">正在合并…</p> : null}
        {status ? <p className="mt-4 text-sm text-studio-secondary" data-crowd-status="">{status}</p> : null}
        {issues.length > 0 ? (
          <ul className="mt-3 space-y-2">
            {issues.map((issue, index) => (
              <li key={`${issue.path}-${index}`} data-crowd-error="" className="rounded-md border border-studio-red/40 bg-studio-red/10 px-3 py-2 text-sm">
                <span className="font-mono text-xs text-studio-red">{issue.path || '（无字段）'}</span>
                <span className="mt-1 block">{issue.message}</span>
                {issue.sourceMod || issue.sourceFile ? (
                  <span className="mt-1 block font-mono text-xs text-studio-secondary">
                    {issue.sourceMod ?? ''} {issue.sourceFile ?? ''}
                  </span>
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}

        {tab === 'templates' ? (
          <div data-template-inspector="" className="mt-4 rounded-xl border border-studio-elevated bg-studio-surface p-4">
            <h2 className="text-sm font-semibold">单位模板</h2>
            <p className="mt-1 text-xs text-studio-muted">速度、个人半径、体型写在 CrowdSimulationAgent。数值规则由装载器校验，这里只改草稿。</p>
            <label className={STUDIO_CHROME.label}>
              模板
              <select className={STUDIO_CHROME.field} value={selectedUnit?.id ?? ''} onChange={(event) => setTemplateId(event.target.value)}>
                {units.map((unit) => (
                  <option key={unit.id} value={unit.id}>
                    {unit.id}
                  </option>
                ))}
              </select>
            </label>
            {agent ? (
              <div className="mt-3 grid gap-3 sm:grid-cols-3">
                <label className={STUDIO_CHROME.label}>
                  speedCmPerSecond
                  <input className={STUDIO_CHROME.field} value={agent.speedCmPerSecond ?? ''} onChange={(event) => updateAgent('speedCmPerSecond', event.target.value)} />
                </label>
                <label className={STUDIO_CHROME.label}>
                  personalRadiusCm
                  <input className={STUDIO_CHROME.field} value={agent.personalRadiusCm ?? ''} onChange={(event) => updateAgent('personalRadiusCm', event.target.value)} />
                </label>
                <label className={STUDIO_CHROME.label}>
                  体型 profileId
                  <input className={STUDIO_CHROME.field} value={agent.profileId ?? ''} onChange={(event) => updateAgent('profileId', event.target.value)} />
                </label>
              </div>
            ) : (
              <p className="mt-2 text-xs text-studio-muted">这份草稿里没有带 CrowdSimulationAgent 的模板。</p>
            )}
          </div>
        ) : null}

        {tab === 'costMatrix' ? (
          <div className="mt-4 overflow-auto rounded-xl border border-studio-elevated bg-studio-surface p-4">
            <h2 className="text-sm font-semibold">移动类型 × 区域</h2>
            <p className="mt-1 text-xs text-studio-muted">格子留空就是不可通行。写入时仍会带上 cost，因为装载器不接受缺行。</p>
            <table data-cost-matrix="" className="mt-3 w-full text-left text-xs">
              <thead>
                <tr className="text-studio-muted">
                  <th className="py-1 pr-3 font-normal">agentType</th>
                  {areas.map((area) => (
                    <th key={area.id} className="py-1 pr-3 font-normal">
                      {area.name}
                      <span className="block font-mono">{area.id}</span>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {agentTypes.map((agentType) => (
                  <tr key={agentType} className="border-t border-studio-elevated">
                    <td className="py-1 pr-3 font-mono">{agentType}</td>
                    {areas.map((area) => {
                      const raw = cells[agentType]?.[String(area.id)];
                      return (
                        <td key={area.id} className="py-1 pr-3">
                          <input
                            className="w-16 rounded border border-studio-elevated bg-studio-bg px-1 py-0.5 font-mono"
                            value={raw == null ? '' : String(raw)}
                            placeholder="不可通行"
                            onChange={(event) => {
                              const next = event.target.value.trim();
                              setCells((current) => ({
                                ...current,
                                [agentType]: {
                                  ...(current[agentType] ?? {}),
                                  [String(area.id)]: next === '' ? null : Number(next),
                                },
                              }));
                            }}
                          />
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {tab === 'bundle' ? (
          <div className="mt-4">
            <p className="text-sm text-studio-secondary" data-bundle-version="">
              结构版本 {snapshot?.bundleVersion ?? '…'}。缺 version、或 version 不是这个数，整包拒绝，不改草稿。
            </p>
            <div className="mt-3 flex gap-2">
              <button type="button" className={STUDIO_CHROME.btnGhost} disabled={busy} onClick={() => void exportBundle()}>
                导出
              </button>
              <button type="button" className={STUDIO_CHROME.btnPrimary} disabled={busy} onClick={() => void importBundle()}>
                导入
              </button>
            </div>
            <textarea
              className="mt-3 h-80 w-full rounded-md border border-studio-elevated bg-studio-bg p-3 font-mono text-xs"
              value={bundleText}
              onChange={(event) => setBundleText(event.target.value)}
            />
          </div>
        ) : null}

        {tab !== 'bundle' && tab !== 'costMatrix' ? (
          <div className="mt-4 grid gap-4 lg:grid-cols-2">
            <label className="block text-xs text-studio-muted">
              当前草稿
              <textarea
                className="mt-1 h-96 w-full rounded-md border border-studio-elevated bg-studio-bg p-3 font-mono text-xs text-studio-label"
                value={texts[tab] ?? ''}
                onChange={(event) => setText(tab, event.target.value)}
              />
            </label>
            <label className="block text-xs text-studio-muted">
              默认
              <textarea className="mt-1 h-96 w-full rounded-md border border-studio-elevated bg-studio-surface p-3 font-mono text-xs text-studio-secondary" readOnly value={defaultsText} />
            </label>
          </div>
        ) : null}

        {tab !== 'bundle' ? (
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="button" className={STUDIO_CHROME.btnGhost} disabled={busy} onClick={() => void validate()}>
              校验
            </button>
            <button type="button" className={STUDIO_CHROME.btnPrimary} disabled={busy} onClick={() => void apply()}>
              {applyLabel(tab)}
            </button>
            <button type="button" className={STUDIO_CHROME.btnGhost} disabled={busy} onClick={resetDraft}>
              恢复默认
            </button>
          </div>
        ) : null}

        <details className="mt-6">
          <summary className="cursor-pointer text-sm text-studio-secondary">字段归属</summary>
          <ul className="mt-2 max-h-64 space-y-1 overflow-auto text-xs">
            {(snapshot?.owners ?? []).slice(0, 80).map((owner) => (
              <li key={`${owner.file}:${owner.path}`} className="font-mono text-studio-muted">
                {owner.file} {owner.path} · {owner.sourceFile}
              </li>
            ))}
          </ul>
        </details>
      </div>
    </div>
  );
}
