import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { adapterFor, TIMELINE_CONTEXT_ORDER } from './contexts/index.ts';
import { asArray, asRecord, readString, type TimelineContextId, type TimelineMutation } from './model.ts';
import { TimelineEditor } from './TimelineEditor';
import { TimelineInspector, clipValues } from './TimelineInspector';
import { TimelinePalette } from './TimelinePalette';

type ModInfo = { id: string; name?: string };
type CatalogFile = {
  id: string;
  context: TimelineContextId;
  relativePath: string;
  exists: boolean;
};
type CatalogResponse = {
  ok: boolean;
  error?: string;
  catalogs?: CatalogFile[];
};

const DEFAULT_MOD: Record<TimelineContextId, string> = {
  sequencer: 'NarrativeShowcaseMod',
  'ability-exec': 'RtsRedAlertLikeShowcaseMod',
  'presenter-timer': 'CapabilityStandardPresenterCommandShowcaseMod',
};

const CONTEXT_HINT: Record<TimelineContextId, string> = {
  sequencer: '改镜头何时切、字幕何时出、信号何时打动作图。',
  'ability-exec': '改技能里持续、瞬发、等待和收束的到达 tick。',
  'presenter-timer': '改命名倒计时多长、到期后做什么、谁能打断。',
};

const fieldClass = 'mt-1 w-full bg-zinc-900 border border-zinc-700 rounded px-2 py-1.5 text-sm text-zinc-100';
const labelClass = 'block text-xs text-zinc-400';

function applyMutation(
  mutation: TimelineMutation,
  setSource: (value: unknown) => void,
  setError: (value: string) => void,
): boolean {
  if (mutation.ok === false) {
    setError(mutation.error);
    return false;
  }
  setSource(mutation.source);
  setError('');
  return true;
}

function documentId(item: unknown): string {
  const record = asRecord(item);
  return record ? readString(record.id) : '';
}

export const TimelineAuthoringPage: React.FC = () => {
  const [contextId, setContextId] = useState<TimelineContextId>('sequencer');
  const [mods, setMods] = useState<ModInfo[]>([]);
  const [modId, setModId] = useState(DEFAULT_MOD.sequencer);
  const [catalogs, setCatalogs] = useState<CatalogFile[]>([]);
  const [relativePath, setRelativePath] = useState('');
  const [items, setItems] = useState<unknown[]>([]);
  const [selectedId, setSelectedId] = useState('');
  const [selectedClipId, setSelectedClipId] = useState<string | null>(null);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [playhead, setPlayhead] = useState<number | null>(null);
  const [playing, setPlaying] = useState(false);

  const adapter = adapterFor(contextId);
  const selected = useMemo(() => items.find((item) => documentId(item) === selectedId) ?? items[0] ?? null, [items, selectedId]);
  const document = useMemo(() => (selected ? adapter.project(selected) : null), [adapter, selected]);

  const replaceSelected = useCallback(
    (next: unknown) => {
      const id = documentId(next);
      setItems((current) => current.map((item) => (documentId(item) === selectedId || documentId(item) === id ? next : item)));
      if (id) setSelectedId(id);
    },
    [selectedId],
  );

  const loadMods = useCallback(async () => {
    const res = await fetch('/api/mods');
    const json = await res.json();
    const list: ModInfo[] = (json.mods ?? json ?? []).map((m: { id?: string; Id?: string; name?: string; Name?: string }) => ({
      id: m.id ?? m.Id ?? m.name ?? m.Name ?? '',
      name: m.name ?? m.Name,
    }));
    setMods(list.filter((m) => !!m.id));
  }, []);

  const loadCatalogs = useCallback(async (targetMod: string, context: TimelineContextId) => {
    const res = await fetch(`/api/mods/${encodeURIComponent(targetMod)}/timeline/catalogs`);
    const json = (await res.json()) as CatalogResponse;
    if (!json.ok) {
      setError(json.error ?? '列出时间轴目录失败');
      setCatalogs([]);
      return;
    }
    const next = (json.catalogs ?? []).filter((catalog) => catalog.context === context);
    setCatalogs(next);
    setRelativePath((current) => {
      if (current && next.some((catalog) => catalog.relativePath === current)) return current;
      return next.find((catalog) => catalog.exists)?.relativePath ?? next[0]?.relativePath ?? '';
    });
    setError('');
  }, []);

  const loadDocument = useCallback(async (targetMod: string, path: string) => {
    if (!path) {
      setItems([]);
      setSelectedId('');
      setStatus('这个上下文在该 Mod 下还没有可写文件。');
      return;
    }
    setStatus('加载中…');
    const res = await fetch(
      `/api/mods/${encodeURIComponent(targetMod)}/timeline/file?relativePath=${encodeURIComponent(path)}`,
    );
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? '加载失败');
      setStatus('');
      return;
    }
    const next = asArray<unknown>(json.items);
    setItems(next);
    setSelectedId(next.length > 0 ? documentId(next[0]) : '');
    setSelectedClipId(null);
    setError('');
    setStatus(`已加载 ${path}`);
  }, []);

  const save = useCallback(async () => {
    if (!relativePath) {
      setError('没有可写文件。');
      return;
    }
    setStatus('保存中…');
    const res = await fetch(
      `/api/mods/${encodeURIComponent(modId)}/timeline/file?relativePath=${encodeURIComponent(relativePath)}`,
      {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ items }),
      },
    );
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? '保存失败');
      setStatus('');
      return;
    }
    setError('');
    setStatus(`已写入 ${relativePath}`);
  }, [items, modId, relativePath]);

  useEffect(() => {
    void loadMods();
  }, [loadMods]);

  useEffect(() => {
    if (!modId) return;
    void loadCatalogs(modId, contextId);
  }, [modId, contextId, loadCatalogs]);

  useEffect(() => {
    if (!modId || !relativePath) {
      setItems([]);
      return;
    }
    void loadDocument(modId, relativePath);
  }, [modId, relativePath, loadDocument]);

  useEffect(() => {
    if (!playing || !document) return undefined;
    const end = document.clips.reduce((max, clip) => Math.max(max, clip.start + clip.duration), 1);
    let last = performance.now();
    let frame = 0;
    const tick = (now: number) => {
      const dt = (now - last) / 1000;
      last = now;
      setPlayhead((current) => {
        const next = (current ?? 0) + (document.timeUnit === 'ticks' ? dt * 60 : dt);
        if (next >= end) {
          setPlaying(false);
          return end;
        }
        return next;
      });
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [playing, document]);

  const mutateSelected = (mutation: TimelineMutation) => {
    if (!selected) return;
    applyMutation(
      mutation,
      (next) => {
        replaceSelected(next);
        const projected = adapter.project(next);
        if (selectedClipId && !projected.clips.some((clip) => clip.id === selectedClipId)) {
          setSelectedClipId(projected.clips[0]?.id ?? null);
        }
      },
      setError,
    );
  };

  const selectedClip = document?.clips.find((clip) => clip.id === selectedClipId) ?? null;
  const occupancy = document?.occupancy;
  const paletteLocked = occupancy !== undefined && occupancy.used >= occupancy.max;

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 p-6 font-sans">
      <div className="mb-4 flex items-center gap-4 flex-wrap">
        <Link to="/" className="text-emerald-400 hover:underline text-sm">
          ← 编辑器
        </Link>
        <h1 className="text-xl text-amber-200">时间轴</h1>
        <span className="text-xs text-zinc-500">同一套轨道编辑器，换上下文就换合同。</span>
      </div>

      <div className="flex gap-2 mb-4 flex-wrap">
        {TIMELINE_CONTEXT_ORDER.map((id) => {
          const item = adapterFor(id);
          const active = id === contextId;
          return (
            <button
              key={id}
              type="button"
              className={`px-3 py-2 rounded border text-sm ${
                active ? 'border-amber-400 bg-amber-500/10 text-amber-100' : 'border-zinc-800 bg-zinc-900 text-zinc-300'
              }`}
              onClick={() => {
                setContextId(id);
                setModId((current) => {
                  if (mods.some((mod) => mod.id === DEFAULT_MOD[id])) return DEFAULT_MOD[id];
                  return current;
                });
                setPlaying(false);
                setPlayhead(null);
                setSelectedClipId(null);
              }}
            >
              {item.label}
            </button>
          );
        })}
      </div>
      <p className="text-sm text-zinc-400 mb-4">{CONTEXT_HINT[contextId]}</p>

      <div className="grid grid-cols-12 gap-4">
        <aside className="col-span-3 space-y-3">
          <label className={labelClass}>
            目标 Mod
            <select className={fieldClass} value={modId} onChange={(e) => setModId(e.target.value)}>
              {mods.map((mod) => (
                <option key={mod.id} value={mod.id}>
                  {mod.id}
                </option>
              ))}
            </select>
          </label>

          <div className="text-xs text-zinc-400">文件</div>
          <ul className="space-y-1">
            {catalogs.map((catalog) => (
              <li key={catalog.relativePath}>
                <button
                  type="button"
                  className={`w-full text-left px-2 py-2 rounded border text-xs ${
                    relativePath === catalog.relativePath
                      ? 'border-amber-400 bg-amber-500/10 text-amber-100'
                      : 'border-zinc-800 bg-zinc-900 text-zinc-300 hover:border-zinc-600'
                  }`}
                  onClick={() => setRelativePath(catalog.relativePath)}
                >
                  {catalog.relativePath}
                  {!catalog.exists && <span className="ml-2 text-rose-400">缺失</span>}
                </button>
              </li>
            ))}
          </ul>

          <div className="text-xs text-zinc-400 pt-2">条目</div>
          <ul className="max-h-72 overflow-auto space-y-1 border border-zinc-800 rounded p-1">
            {items.map((item) => {
              const id = documentId(item);
              return (
                <li key={id}>
                  <button
                    type="button"
                    className={`w-full text-left px-2 py-1 rounded text-xs ${
                      selectedId === id ? 'bg-emerald-500/20 text-emerald-200' : 'hover:bg-zinc-800'
                    }`}
                    onClick={() => {
                      setSelectedId(id);
                      setSelectedClipId(null);
                    }}
                  >
                    {id}
                  </button>
                </li>
              );
            })}
          </ul>
        </aside>

        <main className="col-span-9 space-y-3">
          <div className="flex items-center gap-3 flex-wrap">
            <button
              type="button"
              onClick={() => void save()}
              className="px-4 py-2 rounded bg-amber-500 text-zinc-950 font-bold hover:bg-amber-400"
            >
              保存
            </button>
            <button
              type="button"
              onClick={() => void loadDocument(modId, relativePath)}
              className="px-3 py-2 rounded border border-zinc-700 text-sm hover:bg-zinc-900"
            >
              重载
            </button>
            <button
              type="button"
              onClick={() => {
                setPlayhead(0);
                setPlaying(true);
              }}
              className="px-3 py-2 rounded border border-emerald-800 text-sm text-emerald-200 hover:bg-emerald-950"
            >
              {playing ? '预览中' : '本地预览'}
            </button>
            <button
              type="button"
              onClick={() => {
                setPlaying(false);
                setPlayhead(null);
              }}
              className="px-3 py-2 rounded border border-zinc-700 text-sm hover:bg-zinc-900"
            >
              停
            </button>
            {occupancy && (
              <span className="text-xs text-zinc-400">
                占位 {occupancy.used}/{occupancy.max}
              </span>
            )}
            {status && <span className="text-xs text-emerald-400">{status}</span>}
            {error && <span className="text-xs text-rose-400">{error}</span>}
          </div>

          {document && selected ? (
            <>
              <TimelineEditor
                document={document}
                selectedClipId={selectedClipId}
                pixelsPerUnit={adapter.pixelsPerUnit}
                playhead={playhead}
                onSelectClip={setSelectedClipId}
                onChangeClip={(clipId, patch) => mutateSelected(adapter.applyClipChange(selected, clipId, patch))}
              />

              <div className="grid grid-cols-2 gap-3">
                <TimelineInspector
                  title="文档"
                  fields={document.headerFields}
                  values={document.headerValues}
                  onChange={(key, value) => mutateSelected(adapter.applyHeader(selected, { ...document.headerValues, [key]: value }))}
                />
                <TimelinePalette
                  adapter={adapter}
                  disabled={paletteLocked}
                  disabledReason={paletteLocked ? `已满 ${occupancy?.max}` : undefined}
                  onAdd={(paletteId) => {
                    const start = selectedClip?.start ?? playhead ?? 0;
                    const mutation = adapter.addFromPalette(selected, paletteId, start);
                    if (applyMutation(mutation, replaceSelected, setError) && mutation.ok) {
                      const projected = adapter.project(mutation.source);
                      setSelectedClipId(projected.clips[projected.clips.length - 1]?.id ?? null);
                    }
                  }}
                />
              </div>

              {selectedClip ? (
                <TimelineInspector
                  title="选中条目"
                  fields={adapter.clipFields(selectedClip)}
                  values={clipValues(selectedClip)}
                  onChange={(key, value) => {
                    const payload = { ...selectedClip.payload, [key]: value };
                    const patch =
                      key === 'start' || key === 'tick'
                        ? { start: Number(value) || 0, payload }
                        : key === 'duration' || key === 'durationTicks' || key === 'durationSeconds' || key === 'timeoutTicks'
                          ? { duration: Number(value) || 0, payload }
                          : { payload };
                    mutateSelected(adapter.applyClipChange(selected, selectedClip.id, patch));
                  }}
                  onRemove={() => mutateSelected(adapter.removeClip(selected, selectedClip.id))}
                  extra={
                    selectedClip.badges && selectedClip.badges.length > 0 ? (
                      <div className="text-[11px] text-zinc-500">{selectedClip.badges.join(' · ')}</div>
                    ) : null
                  }
                />
              ) : (
                <div className="text-xs text-zinc-500">点时间轴上的块，右边才会出现它的字段。</div>
              )}

              {document.issues.length > 0 && (
                <ul className="rounded border border-zinc-800 p-3 space-y-1 text-xs">
                  {document.issues.map((issue, index) => (
                    <li key={`${issue.message}-${index}`} className={issue.level === 'error' ? 'text-rose-300' : 'text-amber-200'}>
                      {issue.level === 'error' ? '错误' : '注意'} · {issue.message}
                    </li>
                  ))}
                </ul>
              )}
            </>
          ) : (
            <div className="rounded border border-zinc-800 p-6 text-sm text-zinc-400">
              这个 Mod 下没有当前上下文的文件。换一个左侧文件，或换一个带这份配置的 Mod。
            </div>
          )}

          <p className="text-xs text-zinc-500">
            本地预览只移动编辑器指针，不是引擎试播。落盘写入 {relativePath || '（未选文件）'}，形状仍是各上下文自己的 JSON。
          </p>
        </main>
      </div>
    </div>
  );
};

export default TimelineAuthoringPage;
