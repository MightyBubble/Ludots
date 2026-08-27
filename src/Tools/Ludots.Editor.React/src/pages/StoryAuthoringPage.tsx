import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { SequencerTimelineEditor } from './story/SequencerTimelineEditor';

type CatalogInfo = {
  id: string;
  relativePath: string;
  path: string;
  exists: boolean;
};

type ModInfo = { id: string; name?: string };

type LineRow = { id: string; speakerId: string; textToken: string; tags?: string[] };
type SpeakerRow = {
  id: string;
  displayNameToken: string;
  portraitImageId?: string;
  standingImageId?: string;
};
type ChoiceRow = {
  id: string;
  lineId: string;
  conditionGraphId?: string;
  actionGraphId?: string;
  nextNode?: string;
};
type NodeRow = {
  id: string;
  lineId: string;
  presentationProfile?: string;
  cameraId?: string;
  choices?: ChoiceRow[];
};
type DialogueRow = {
  id: string;
  displayName: string;
  entryNode: string;
  nodes: NodeRow[];
};
type TrackRow = {
  type: string;
  profile?: string;
  lineId?: string;
  presentationProfile?: string;
  eventId?: string;
  actionGraphId?: string;
  start: number;
  duration?: number;
};
type SequenceRow = {
  id: string;
  displayName: string;
  clearCameraOnComplete?: boolean;
  clock?: { rate: number };
  tracks: TrackRow[];
};

const CATALOG_LABELS: Record<string, string> = {
  lines: '台词本',
  speakers: '说话的人',
  presentation_profiles: '怎么演',
  dialogues: '对话树',
  sequences: '演出序列',
  text_tokens: '文案词条',
  semantic_maps: '语义映射',
  image_assets: '立绘与图',
};

const FORM_CATALOGS = new Set(['lines', 'speakers', 'dialogues', 'sequences']);

const fieldClass =
  'mt-1 w-full bg-zinc-900 border border-zinc-700 rounded px-2 py-1.5 text-sm text-zinc-100';
const labelClass = 'block text-xs text-zinc-400';

function asArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

export const StoryAuthoringPage: React.FC = () => {
  const [mods, setMods] = useState<ModInfo[]>([]);
  const [modId, setModId] = useState('NarrativeShowcaseMod');
  const [catalogs, setCatalogs] = useState<CatalogInfo[]>([]);
  const [catalogId, setCatalogId] = useState('dialogues');
  const [items, setItems] = useState<unknown[]>([]);
  const [selectedId, setSelectedId] = useState('');
  const [advancedJson, setAdvancedJson] = useState(false);
  const [itemsText, setItemsText] = useState('[]');
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [selectedTrackIndex, setSelectedTrackIndex] = useState(0);
  const [textKeyCatalog, setTextKeyCatalog] = useState<Array<{ id: string; preview?: string | null }>>([]);

  const loadMods = useCallback(async () => {
    const res = await fetch('/api/mods');
    const json = await res.json();
    const list: ModInfo[] = (json.mods ?? json ?? []).map((m: any) => ({
      id: m.id ?? m.Id ?? m.name ?? m.Name,
      name: m.name ?? m.Name,
    }));
    setMods(list.filter((m) => !!m.id));
  }, []);

  const loadCatalogList = useCallback(async (targetMod: string) => {
    const res = await fetch(`/api/mods/${encodeURIComponent(targetMod)}/story/catalogs`);
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? 'failed to list catalogs');
      return;
    }
    setCatalogs(json.catalogs ?? []);
    setError('');
  }, []);

  const loadCatalog = useCallback(async (targetMod: string, id: string) => {
    setStatus('加载中…');
    const res = await fetch(`/api/mods/${encodeURIComponent(targetMod)}/story/catalogs/${encodeURIComponent(id)}`);
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? 'failed to load catalog');
      setStatus('');
      return;
    }
    const next = asArray<unknown>(json.items);
    setItems(next);
    setItemsText(JSON.stringify(next, null, 2));
    setSelectedId(next.length > 0 && typeof (next[0] as any)?.id === 'string' ? (next[0] as any).id : '');
    setError('');
    setStatus(`已加载 ${CATALOG_LABELS[id] ?? id}`);
  }, []);

  useEffect(() => {
    void loadMods();
  }, [loadMods]);

  useEffect(() => {
    if (!modId) return;
    void loadCatalogList(modId);
  }, [modId, loadCatalogList]);

  useEffect(() => {
    if (!modId) return;
    void (async () => {
      try {
        const res = await fetch(`/api/graph/text-keys/${encodeURIComponent(modId)}`);
        const json = await res.json();
        if (!res.ok || !json.ok || !Array.isArray(json.textKeys)) {
          setTextKeyCatalog([]);
          return;
        }
        setTextKeyCatalog(json.textKeys);
      } catch {
        setTextKeyCatalog([]);
      }
    })();
  }, [modId]);

  useEffect(() => {
    if (!modId || !catalogId) return;
    void loadCatalog(modId, catalogId);
  }, [modId, catalogId, loadCatalog]);

  const itemIds = useMemo(
    () =>
      items
        .map((row: any) => (typeof row?.id === 'string' ? row.id : ''))
        .filter((id) => id.length > 0),
    [items],
  );

  const selectedIndex = useMemo(
    () => items.findIndex((row: any) => row?.id === selectedId),
    [items, selectedId],
  );

  const selected = selectedIndex >= 0 ? (items[selectedIndex] as any) : null;

  const replaceSelected = (next: unknown) => {
    if (selectedIndex < 0) return;
    const copy = items.slice();
    copy[selectedIndex] = next;
    setItems(copy);
    setItemsText(JSON.stringify(copy, null, 2));
    if (typeof (next as any)?.id === 'string') setSelectedId((next as any).id);
  };

  const addEntry = () => {
    let row: Record<string, unknown>;
    if (catalogId === 'lines') {
      row = { id: `line.new.${Date.now()}`, speakerId: '', textToken: '', tags: [] };
    } else if (catalogId === 'speakers') {
      row = { id: `speaker.new.${Date.now()}`, displayNameToken: '', portraitImageId: '', standingImageId: '' };
    } else if (catalogId === 'dialogues') {
      row = {
        id: `Dialogue.New.${Date.now()}`,
        displayName: '新对话',
        entryNode: 'start',
        nodes: [{ id: 'start', lineId: '', presentationProfile: 'story.dialogue_overlay', choices: [] }],
      };
    } else if (catalogId === 'sequences') {
      row = {
        id: `Sequence.New.${Date.now()}`,
        displayName: '新演出',
        clearCameraOnComplete: true,
        clock: { rate: 1 },
        tracks: [{ type: 'Camera', profile: '', start: 0, duration: 2 }],
      };
    } else {
      row = { id: `item.new.${Date.now()}` };
    }
    const copy = [...items, row];
    setItems(copy);
    setItemsText(JSON.stringify(copy, null, 2));
    setSelectedId(String(row.id));
  };

  const removeSelected = () => {
    if (selectedIndex < 0) return;
    const copy = items.filter((_, i) => i !== selectedIndex);
    setItems(copy);
    setItemsText(JSON.stringify(copy, null, 2));
    setSelectedId(copy.length ? String((copy[0] as any).id ?? '') : '');
  };

  const save = async () => {
    setStatus('保存中…');
    let payloadItems: unknown = items;
    if (advancedJson || !FORM_CATALOGS.has(catalogId)) {
      try {
        payloadItems = JSON.parse(itemsText);
      } catch (e: any) {
        setError(`JSON 解析失败：${e.message}`);
        setStatus('');
        return;
      }
    }
    const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/story/catalogs/${encodeURIComponent(catalogId)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ items: payloadItems }),
    });
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? 'save failed');
      setStatus('');
      return;
    }
    setError('');
    setStatus(`已保存 ${CATALOG_LABELS[catalogId] ?? catalogId}`);
    if (Array.isArray(payloadItems)) {
      setItems(payloadItems);
      setItemsText(JSON.stringify(payloadItems, null, 2));
    }
  };

  const renderLineForm = (row: LineRow) => (
    <div className="space-y-3">
      <label className={labelClass}>
        台词 ID
        <input className={fieldClass} value={row.id} onChange={(e) => replaceSelected({ ...row, id: e.target.value })} />
      </label>
      <label className={labelClass}>
        说话人 ID
        <input
          className={fieldClass}
          value={row.speakerId ?? ''}
          onChange={(e) => replaceSelected({ ...row, speakerId: e.target.value })}
        />
      </label>
      <label className={labelClass}>
        文案词条
        {textKeyCatalog.length > 0 ? (
          <select
            className={fieldClass}
            value={row.textToken ?? ''}
            onChange={(e) => replaceSelected({ ...row, textToken: e.target.value })}
          >
            <option value="">选择文案键</option>
            {(() => {
              const current = row.textToken ?? '';
              const ids = textKeyCatalog.map((k) => k.id);
              const options = current && !ids.includes(current) ? [current, ...ids] : ids;
              return options.map((id) => {
                const meta = textKeyCatalog.find((k) => k.id === id);
                const suffix = meta?.preview ? ` — ${meta.preview}` : '';
                return (
                  <option key={id} value={id}>
                    {id}
                    {suffix}
                  </option>
                );
              });
            })()}
          </select>
        ) : (
          <input
            className={fieldClass}
            value={row.textToken ?? ''}
            onChange={(e) => replaceSelected({ ...row, textToken: e.target.value })}
          />
        )}
      </label>
      <label className={labelClass}>
        标签（逗号分隔）
        <input
          className={fieldClass}
          value={(row.tags ?? []).join(', ')}
          onChange={(e) =>
            replaceSelected({
              ...row,
              tags: e.target.value
                .split(',')
                .map((t) => t.trim())
                .filter(Boolean),
            })
          }
        />
      </label>
    </div>
  );

  const renderSpeakerForm = (row: SpeakerRow) => (
    <div className="space-y-3">
      <label className={labelClass}>
        说话人 ID
        <input className={fieldClass} value={row.id} onChange={(e) => replaceSelected({ ...row, id: e.target.value })} />
      </label>
      <label className={labelClass}>
        显示名词条
        {textKeyCatalog.length > 0 ? (
          <select
            className={fieldClass}
            value={row.displayNameToken ?? ''}
            onChange={(e) => replaceSelected({ ...row, displayNameToken: e.target.value })}
          >
            <option value="">选择文案键</option>
            {(() => {
              const current = row.displayNameToken ?? '';
              const ids = textKeyCatalog.map((k) => k.id);
              const options = current && !ids.includes(current) ? [current, ...ids] : ids;
              return options.map((id) => {
                const meta = textKeyCatalog.find((k) => k.id === id);
                const suffix = meta?.preview ? ` — ${meta.preview}` : '';
                return (
                  <option key={id} value={id}>
                    {id}
                    {suffix}
                  </option>
                );
              });
            })()}
          </select>
        ) : (
          <input
            className={fieldClass}
            value={row.displayNameToken ?? ''}
            onChange={(e) => replaceSelected({ ...row, displayNameToken: e.target.value })}
          />
        )}
      </label>
      <label className={labelClass}>
        半身像资产 ID
        <input
          className={fieldClass}
          value={row.portraitImageId ?? ''}
          onChange={(e) => replaceSelected({ ...row, portraitImageId: e.target.value })}
        />
      </label>
      <label className={labelClass}>
        全身立绘资产 ID
        <input
          className={fieldClass}
          value={row.standingImageId ?? ''}
          onChange={(e) => replaceSelected({ ...row, standingImageId: e.target.value })}
        />
      </label>
    </div>
  );

  const renderDialogueForm = (row: DialogueRow) => {
    const nodes = row.nodes ?? [];
    const updateNode = (idx: number, next: NodeRow) => {
      const copy = nodes.slice();
      copy[idx] = next;
      replaceSelected({ ...row, nodes: copy });
    };
    return (
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-3">
          <label className={labelClass}>
            对话 ID
            <input className={fieldClass} value={row.id} onChange={(e) => replaceSelected({ ...row, id: e.target.value })} />
          </label>
          <label className={labelClass}>
            显示名
            <input
              className={fieldClass}
              value={row.displayName ?? ''}
              onChange={(e) => replaceSelected({ ...row, displayName: e.target.value })}
            />
          </label>
          <label className={labelClass}>
            入口节点
            <input
              className={fieldClass}
              value={row.entryNode ?? ''}
              onChange={(e) => replaceSelected({ ...row, entryNode: e.target.value })}
            />
          </label>
        </div>

        <div className="flex items-center justify-between">
          <h3 className="text-sm text-amber-200">节点</h3>
          <button
            type="button"
            className="text-xs px-2 py-1 rounded border border-zinc-700 hover:bg-zinc-900"
            onClick={() =>
              replaceSelected({
                ...row,
                nodes: [
                  ...nodes,
                  { id: `node_${nodes.length + 1}`, lineId: '', presentationProfile: 'story.dialogue_overlay', choices: [] },
                ],
              })
            }
          >
            + 加节点
          </button>
        </div>

        {nodes.map((node, ni) => (
          <div key={`${node.id}-${ni}`} className="rounded border border-zinc-800 bg-zinc-950/60 p-3 space-y-2">
            <div className="grid grid-cols-2 gap-2">
              <label className={labelClass}>
                节点 ID
                <input className={fieldClass} value={node.id} onChange={(e) => updateNode(ni, { ...node, id: e.target.value })} />
              </label>
              <label className={labelClass}>
                台词 ID
                <input
                  className={fieldClass}
                  value={node.lineId ?? ''}
                  onChange={(e) => updateNode(ni, { ...node, lineId: e.target.value })}
                />
              </label>
              <label className={labelClass}>
                表现配置
                <input
                  className={fieldClass}
                  value={node.presentationProfile ?? ''}
                  onChange={(e) => updateNode(ni, { ...node, presentationProfile: e.target.value })}
                />
              </label>
              <label className={labelClass}>
                镜头
                <input
                  className={fieldClass}
                  value={node.cameraId ?? ''}
                  onChange={(e) => updateNode(ni, { ...node, cameraId: e.target.value })}
                />
              </label>
            </div>

            <div className="flex items-center justify-between pt-1">
              <div className="text-xs text-zinc-500">选项</div>
              <button
                type="button"
                className="text-[11px] px-2 py-0.5 rounded border border-zinc-700"
                onClick={() =>
                  updateNode(ni, {
                    ...node,
                    choices: [...(node.choices ?? []), { id: `choice_${(node.choices?.length ?? 0) + 1}`, lineId: '', nextNode: '' }],
                  })
                }
              >
                + 加选项
              </button>
            </div>

            {(node.choices ?? []).map((choice, ci) => (
              <div key={`${choice.id}-${ci}`} className="grid grid-cols-2 gap-2 rounded border border-zinc-800 p-2">
                <label className={labelClass}>
                  选项 ID
                  <input
                    className={fieldClass}
                    value={choice.id}
                    onChange={(e) => {
                      const choices = (node.choices ?? []).slice();
                      choices[ci] = { ...choice, id: e.target.value };
                      updateNode(ni, { ...node, choices });
                    }}
                  />
                </label>
                <label className={labelClass}>
                  台词 ID
                  <input
                    className={fieldClass}
                    value={choice.lineId ?? ''}
                    onChange={(e) => {
                      const choices = (node.choices ?? []).slice();
                      choices[ci] = { ...choice, lineId: e.target.value };
                      updateNode(ni, { ...node, choices });
                    }}
                  />
                </label>
                <label className={labelClass}>
                  条件图
                  <input
                    className={fieldClass}
                    value={choice.conditionGraphId ?? ''}
                    onChange={(e) => {
                      const choices = (node.choices ?? []).slice();
                      choices[ci] = { ...choice, conditionGraphId: e.target.value };
                      updateNode(ni, { ...node, choices });
                    }}
                  />
                </label>
                <label className={labelClass}>
                  动作图
                  <input
                    className={fieldClass}
                    value={choice.actionGraphId ?? ''}
                    onChange={(e) => {
                      const choices = (node.choices ?? []).slice();
                      choices[ci] = { ...choice, actionGraphId: e.target.value };
                      updateNode(ni, { ...node, choices });
                    }}
                  />
                </label>
                <label className={`${labelClass} col-span-2`}>
                  下一节点
                  <input
                    className={fieldClass}
                    value={choice.nextNode ?? ''}
                    onChange={(e) => {
                      const choices = (node.choices ?? []).slice();
                      choices[ci] = { ...choice, nextNode: e.target.value };
                      updateNode(ni, { ...node, choices });
                    }}
                  />
                </label>
              </div>
            ))}
          </div>
        ))}
      </div>
    );
  };

  const renderSequenceForm = (row: SequenceRow) => {
    const tracks = row.tracks ?? [];
    const updateTrack = (idx: number, next: TrackRow) => {
      const copy = tracks.slice();
      copy[idx] = next;
      replaceSelected({ ...row, tracks: copy });
    };
    const ti = Math.min(Math.max(0, selectedTrackIndex), Math.max(0, tracks.length - 1));
    const track = tracks[ti];
    return (
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-3">
          <label className={labelClass}>
            演出 ID
            <input className={fieldClass} value={row.id} onChange={(e) => replaceSelected({ ...row, id: e.target.value })} />
          </label>
          <label className={labelClass}>
            显示名
            <input
              className={fieldClass}
              value={row.displayName ?? ''}
              onChange={(e) => replaceSelected({ ...row, displayName: e.target.value })}
            />
          </label>
          <label className={labelClass}>
            时钟倍率
            <input
              className={fieldClass}
              type="number"
              step="0.1"
              value={row.clock?.rate ?? 1}
              onChange={(e) => replaceSelected({ ...row, clock: { rate: Number(e.target.value) || 1 } })}
            />
          </label>
          <label className="flex items-center gap-2 text-xs text-zinc-400 pt-5">
            <input
              type="checkbox"
              checked={!!row.clearCameraOnComplete}
              onChange={(e) => replaceSelected({ ...row, clearCameraOnComplete: e.target.checked })}
            />
            结束时清镜头
          </label>
        </div>

        <SequencerTimelineEditor
          tracks={tracks}
          selectedIndex={ti}
          onSelect={setSelectedTrackIndex}
          onChangeTrack={updateTrack}
        />

        <div className="flex items-center justify-between">
          <h3 className="text-sm text-amber-200">选中轨道属性</h3>
          <button
            type="button"
            className="text-xs px-2 py-1 rounded border border-zinc-700 hover:bg-zinc-900"
            onClick={() => {
              const nextTracks = [
                ...tracks,
                { type: 'Camera', profile: '', start: tracks.reduce((m, t) => Math.max(m, (t.start || 0) + (t.duration || 0)), 0), duration: 2 },
              ];
              replaceSelected({ ...row, tracks: nextTracks });
              setSelectedTrackIndex(nextTracks.length - 1);
            }}
          >
            + 加轨道
          </button>
        </div>

        {track && (
          <div className="rounded border border-zinc-800 bg-zinc-950/60 p-3 grid grid-cols-2 gap-2">
            <label className={labelClass}>
              类型
              <select
                className={fieldClass}
                value={track.type}
                onChange={(e) => updateTrack(ti, { ...track, type: e.target.value })}
              >
                <option value="Camera">Camera 镜头</option>
                <option value="Subtitle">Subtitle 字幕</option>
                <option value="Signal">Signal 信号</option>
              </select>
            </label>
            <label className={labelClass}>
              开始秒
              <input
                className={fieldClass}
                type="number"
                step="0.1"
                value={track.start ?? 0}
                onChange={(e) => updateTrack(ti, { ...track, start: Number(e.target.value) || 0 })}
              />
            </label>
            {track.type !== 'Signal' && (
              <label className={labelClass}>
                持续秒
                <input
                  className={fieldClass}
                  type="number"
                  step="0.1"
                  value={track.duration ?? 0}
                  onChange={(e) => updateTrack(ti, { ...track, duration: Number(e.target.value) || 0 })}
                />
              </label>
            )}
            {track.type === 'Camera' && (
              <label className={labelClass}>
                镜头配置（VirtualCamera）
                <input
                  className={fieldClass}
                  value={track.profile ?? ''}
                  onChange={(e) => updateTrack(ti, { ...track, profile: e.target.value })}
                />
              </label>
            )}
            {track.type === 'Subtitle' && (
              <>
                <label className={labelClass}>
                  台词 ID
                  <input
                    className={fieldClass}
                    value={track.lineId ?? ''}
                    onChange={(e) => updateTrack(ti, { ...track, lineId: e.target.value })}
                  />
                </label>
                <label className={labelClass}>
                  表现配置
                  <input
                    className={fieldClass}
                    value={track.presentationProfile ?? ''}
                    onChange={(e) => updateTrack(ti, { ...track, presentationProfile: e.target.value })}
                  />
                </label>
              </>
            )}
            {track.type === 'Signal' && (
              <>
                <label className={labelClass}>
                  事件 ID
                  <input
                    className={fieldClass}
                    value={track.eventId ?? ''}
                    onChange={(e) => updateTrack(ti, { ...track, eventId: e.target.value })}
                  />
                </label>
                <label className={labelClass}>
                  动作图
                  <input
                    className={fieldClass}
                    value={track.actionGraphId ?? ''}
                    onChange={(e) => updateTrack(ti, { ...track, actionGraphId: e.target.value })}
                  />
                </label>
              </>
            )}
            <button
              type="button"
              className="col-span-2 text-xs px-2 py-1 rounded border border-rose-900 text-rose-300"
              onClick={() => {
                const copy = tracks.filter((_, i) => i !== ti);
                replaceSelected({ ...row, tracks: copy });
                setSelectedTrackIndex(Math.max(0, ti - 1));
              }}
            >
              删除此轨道
            </button>
          </div>
        )}
      </div>
    );
  };

  const formBody = (() => {
    if (!selected || advancedJson || !FORM_CATALOGS.has(catalogId)) return null;
    if (catalogId === 'lines') return renderLineForm(selected as LineRow);
    if (catalogId === 'speakers') return renderSpeakerForm(selected as SpeakerRow);
    if (catalogId === 'dialogues') return renderDialogueForm(selected as DialogueRow);
    if (catalogId === 'sequences') return renderSequenceForm(selected as SequenceRow);
    return null;
  })();

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 p-6 font-sans">
      <div className="mb-4 flex items-center gap-4 flex-wrap">
        <Link to="/" className="text-emerald-400 hover:underline text-sm">
          ← 编辑器
        </Link>
        <h1 className="text-xl text-amber-200">叙事配置</h1>
        <span className="text-xs text-zinc-500">台词 / 对话树 / 演出序列用表单；换肤只动 panelTheme + CSS</span>
      </div>

      <div className="grid grid-cols-12 gap-4">
        <aside className="col-span-3 space-y-3">
          <label className={labelClass}>
            目标 Mod
            <select className={fieldClass} value={modId} onChange={(e) => setModId(e.target.value)}>
              {mods.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.id}
                </option>
              ))}
            </select>
          </label>

          <div className="text-xs text-zinc-400">目录</div>
          <ul className="space-y-1">
            {catalogs.map((c) => (
              <li key={c.id}>
                <button
                  type="button"
                  className={`w-full text-left px-2 py-2 rounded border text-sm ${
                    catalogId === c.id
                      ? 'border-amber-400 bg-amber-500/10 text-amber-100'
                      : 'border-zinc-800 bg-zinc-900 text-zinc-300 hover:border-zinc-600'
                  }`}
                  onClick={() => setCatalogId(c.id)}
                >
                  {CATALOG_LABELS[c.id] ?? c.id}
                  {!c.exists && <span className="ml-2 text-rose-400 text-[10px]">缺失</span>}
                </button>
              </li>
            ))}
          </ul>

          <div className="text-xs text-zinc-400 pt-2">条目</div>
          <ul className="max-h-64 overflow-auto space-y-1 border border-zinc-800 rounded p-1">
            {itemIds.map((id) => (
              <li key={id}>
                <button
                  type="button"
                  className={`w-full text-left px-2 py-1 rounded text-xs ${
                    selectedId === id ? 'bg-emerald-500/20 text-emerald-200' : 'hover:bg-zinc-800'
                  }`}
                  onClick={() => setSelectedId(id)}
                >
                  {id}
                </button>
              </li>
            ))}
          </ul>
          <div className="flex gap-2">
            <button type="button" onClick={addEntry} className="flex-1 text-xs px-2 py-1.5 rounded border border-zinc-700">
              新建
            </button>
            <button type="button" onClick={removeSelected} className="flex-1 text-xs px-2 py-1.5 rounded border border-rose-900 text-rose-300">
              删除
            </button>
          </div>
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
              onClick={() => void loadCatalog(modId, catalogId)}
              className="px-3 py-2 rounded border border-zinc-700 text-sm hover:bg-zinc-900"
            >
              重载
            </button>
            <label className="flex items-center gap-2 text-xs text-zinc-400">
              <input type="checkbox" checked={advancedJson} onChange={(e) => setAdvancedJson(e.target.checked)} />
              高级 JSON
            </label>
            {status && <span className="text-xs text-emerald-400">{status}</span>}
            {error && <span className="text-xs text-rose-400">{error}</span>}
          </div>

          {formBody && !advancedJson ? (
            <div className="rounded border border-zinc-800 bg-zinc-900/40 p-4 max-h-[75vh] overflow-auto">{formBody}</div>
          ) : (
            <textarea
              className="w-full h-[70vh] bg-zinc-900 border border-zinc-700 rounded p-3 text-sm font-mono leading-relaxed"
              value={itemsText}
              onChange={(e) => setItemsText(e.target.value)}
              spellCheck={false}
            />
          )}

          <p className="text-xs text-zinc-500">
            写入 {catalogs.find((c) => c.id === catalogId)?.relativePath ?? '…'}。对话树 / 演出序列用表单编节点与轨道；换肤请改
            game.json 的 panelTheme，并在 PanelThemes 下放九宫格框图。
          </p>
        </main>
      </div>
    </div>
  );
};

export default StoryAuthoringPage;
