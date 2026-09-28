import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { STUDIO_CHROME, diskSaveStatus } from './authoring-studio/authoringTheme';
import { DialogueTreeCanvas } from './dialogue-tree-editor/DialogueTreeCanvas';
import {
  emptyDialogue,
  validateDialogueTree,
  type DialogueTree,
  type LinePreview,
} from './dialogue-tree-editor/dialogueTreeModel';
import {
  planInlineSync,
  type LineDraft,
  type LineRow,
  type SpeakerDraft,
  type SpeakerRow,
} from './dialogue-tree-editor/inlineAuthoring';
import type { LocaleRoot, TextTokenRow } from './text-bank/textBankModel';
import { SequencerTimelineEditor } from './story/SequencerTimelineEditor';

type CatalogInfo = {
  id: string;
  relativePath: string;
  path: string;
  exists: boolean;
};

type ModInfo = { id: string; name?: string };

type DialogueRow = DialogueTree;
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
  displayName?: string;
  displayNameToken?: string;
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

const DIALOGUE_CATALOGS = new Set(['lines', 'speakers', 'dialogues', 'text_tokens']);
const TIMELINE_CATALOGS = new Set(['sequences']);

export type StoryAuthoringTool = 'dialogue' | 'timeline';

function catalogsForTool(tool: StoryAuthoringTool | undefined): Set<string> | null {
  if (tool === 'dialogue') return DIALOGUE_CATALOGS;
  if (tool === 'timeline') return TIMELINE_CATALOGS;
  return null;
}

function defaultCatalogId(tool: StoryAuthoringTool | undefined): string {
  return tool === 'timeline' ? 'sequences' : 'dialogues';
}

const fieldClass = STUDIO_CHROME.field;
const labelClass = STUDIO_CHROME.label;

function asArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

export const StoryAuthoringPage: React.FC<{ tool?: StoryAuthoringTool }> = ({ tool }) => {
  const [mods, setMods] = useState<ModInfo[]>([]);
  const [modId, setModId] = useState('NarrativeShowcaseMod');
  const [catalogs, setCatalogs] = useState<CatalogInfo[]>([]);
  const [catalogId, setCatalogId] = useState(() => defaultCatalogId(tool));
  const [items, setItems] = useState<unknown[]>([]);
  const [selectedId, setSelectedId] = useState('');
  const [advancedJson, setAdvancedJson] = useState(false);
  const [itemsText, setItemsText] = useState('[]');
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [selectedTrackIndex, setSelectedTrackIndex] = useState(0);
  const [selectedDialogueNodeId, setSelectedDialogueNodeId] = useState('');
  const [textKeyCatalog, setTextKeyCatalog] = useState<Array<{ id: string; preview?: string | null }>>([]);

  // 对话工具的共享作者面状态：画布与侧栏表单同源，保存时一起编排落盘。
  const [dialogueRows, setDialogueRows] = useState<DialogueRow[]>([]);
  const [lineRows, setLineRows] = useState<LineRow[]>([]);
  const [speakerRows, setSpeakerRows] = useState<SpeakerRow[]>([]);
  const [tokenRows, setTokenRows] = useState<TextTokenRow[]>([]);
  const [localeRoot, setLocaleRoot] = useState<LocaleRoot | null>(null);
  const [drafts, setDrafts] = useState<Record<string, LineDraft>>({});
  const [newSpeakers, setNewSpeakers] = useState<SpeakerDraft[]>([]);
  const snapshotRef = useRef<Record<string, string>>({});

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
    const listed: CatalogInfo[] = json.catalogs ?? [];
    const allowed = catalogsForTool(tool);
    setCatalogs(allowed ? listed.filter((c) => allowed.has(c.id)) : listed);
    setError('');
  }, [tool]);

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

  const fetchJson = async (targetMod: string, id: string): Promise<any | null> => {
    const res = await fetch(`/api/mods/${encodeURIComponent(targetMod)}/story/catalogs/${encodeURIComponent(id)}`);
    const json = await res.json();
    return json.ok ? json : null;
  };

  const loadDialogueAuthoringState = useCallback(async (targetMod: string) => {
    setStatus('加载中…');
    const [dialoguesJson, linesJson, speakersJson, tokensJson, localesJson] = await Promise.all([
      fetchJson(targetMod, 'dialogues'),
      fetchJson(targetMod, 'lines'),
      fetchJson(targetMod, 'speakers'),
      fetchJson(targetMod, 'text_tokens'),
      fetchJson(targetMod, 'text_locales'),
    ]);
    const dialogueList: DialogueRow[] = dialoguesJson ? asArray<DialogueRow>(dialoguesJson.items) : [];
    const lineList: LineRow[] = linesJson ? asArray<LineRow>(linesJson.items) : [];
    const speakerList: SpeakerRow[] = speakersJson ? asArray<SpeakerRow>(speakersJson.items) : [];
    const tokenList: TextTokenRow[] = tokensJson ? asArray<TextTokenRow>(tokensJson.items) : [];
    const locales: LocaleRoot | null =
      localesJson && localesJson.items && typeof localesJson.items === 'object' && !Array.isArray(localesJson.items)
        ? (localesJson.items as LocaleRoot)
        : null;

    setDialogueRows(dialogueList);
    setLineRows(lineList);
    setSpeakerRows(speakerList);
    setTokenRows(tokenList);
    setLocaleRoot(locales);
    setDrafts({});
    setNewSpeakers([]);
    snapshotRef.current = {
      dialogues: JSON.stringify(dialogueList),
      lines: JSON.stringify(lineList),
      speakers: JSON.stringify(speakerList),
      text_tokens: JSON.stringify(tokenList),
      text_locales: JSON.stringify(locales),
    };
    setSelectedId(dialogueList.length > 0 ? dialogueList[0]!.id : '');
    setError('');
    setStatus(`已加载 ${dialogueList.length} 棵对话 / ${lineList.length} 条台词`);
  }, []);

  useEffect(() => {
    setCatalogId(defaultCatalogId(tool));
  }, [tool]);

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
    if (!modId) return;
    if (tool === 'dialogue') {
      void loadDialogueAuthoringState(modId);
      return;
    }
  }, [modId, tool, loadDialogueAuthoringState]);

  useEffect(() => {
    if (!modId || !catalogId) return;
    if (tool === 'dialogue') return;
    void loadCatalog(modId, catalogId);
  }, [modId, catalogId, loadCatalog, tool]);

  const isDialogueSharedView = tool === 'dialogue' && DIALOGUE_CATALOGS.has(catalogId);

  const viewItems: unknown[] = useMemo(() => {
    if (!isDialogueSharedView) return items;
    if (catalogId === 'dialogues') return dialogueRows;
    if (catalogId === 'lines') return lineRows;
    if (catalogId === 'speakers') return speakerRows;
    if (catalogId === 'text_tokens') return tokenRows;
    return items;
  }, [isDialogueSharedView, catalogId, items, dialogueRows, lineRows, speakerRows, tokenRows]);

  const patchDialogueShared = useCallback(
    (id: string, updater: (rows: any[]) => any[]) => {
      if (id === 'dialogues') setDialogueRows((prev) => updater(prev));
      else if (id === 'lines') setLineRows((prev) => updater(prev));
      else if (id === 'speakers') setSpeakerRows((prev) => updater(prev));
      else if (id === 'text_tokens') setTokenRows((prev) => updater(prev));
    },
    [],
  );

  const itemIds = useMemo(
    () =>
      viewItems
        .map((row: any) => (typeof row?.id === 'string' ? row.id : ''))
        .filter((id: string) => id.length > 0),
    [viewItems],
  );

  const selectedIndex = useMemo(
    () => viewItems.findIndex((row: any) => row?.id === selectedId),
    [viewItems, selectedId],
  );

  const selected = selectedIndex >= 0 ? (viewItems[selectedIndex] as any) : null;

  const replaceSelected = (next: unknown) => {
    if (isDialogueSharedView) {
      patchDialogueShared(catalogId, (rows) => rows.map((row) => (row.id === (next as any).id ? next : row)));
      if (typeof (next as any)?.id === 'string') setSelectedId((next as any).id);
      return;
    }
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
      row = emptyDialogue(`Dialogue.New.${Date.now()}`);
    } else if (catalogId === 'sequences') {
      row = {
        id: `Sequence.New.${Date.now()}`,
        displayNameToken: '',
        clearCameraOnComplete: true,
        clock: { rate: 1 },
        tracks: [{ type: 'Camera', profile: '', start: 0, duration: 2 }],
      };
    } else if (catalogId === 'text_tokens') {
      row = { id: `text.new.${Date.now()}`, argCount: 0 };
    } else {
      row = { id: `item.new.${Date.now()}` };
    }
    if (isDialogueSharedView) {
      patchDialogueShared(catalogId, (rows) => [...rows, row]);
    } else {
      const copy = [...items, row];
      setItems(copy);
      setItemsText(JSON.stringify(copy, null, 2));
    }
    setSelectedId(String(row.id));
  };

  const removeSelected = () => {
    if (selectedIndex < 0) return;
    const removedId = selectedId;
    if (isDialogueSharedView) {
      patchDialogueShared(catalogId, (rows) => rows.filter((row) => row.id !== removedId));
      const rest = viewItems.filter((row) => (row as any).id !== removedId);
      setSelectedId(rest.length ? String((rest[0] as any).id ?? '') : '');
      return;
    }
    const copy = items.filter((_, i) => i !== selectedIndex);
    setItems(copy);
    setItemsText(JSON.stringify(copy, null, 2));
    setSelectedId(copy.length ? String((copy[0] as any).id ?? '') : '');
  };

  const defaultLocaleName = localeRoot?.defaultLocale?.trim() || '';
  const defaultTable = defaultLocaleName ? localeRoot?.locales?.[defaultLocaleName] : undefined;

  const defaultTextOf = useCallback(
    (token: string) => (defaultTable && typeof defaultTable[token] === 'string' ? defaultTable[token]! : ''),
    [defaultTable],
  );

  const speakerNameOf = useCallback(
    (speakerId: string) => {
      const speaker = speakerRows.find((row) => row.id === speakerId);
      if (!speaker?.displayNameToken) return undefined;
      const name = defaultTextOf(speaker.displayNameToken);
      return name || undefined;
    },
    [speakerRows, defaultTextOf],
  );

  const linePreviews: LinePreview[] = useMemo(
    () =>
      lineRows.map((line) => ({
        id: line.id,
        speakerId: line.speakerId,
        textToken: line.textToken,
        text: line.textToken ? defaultTextOf(line.textToken) || undefined : undefined,
        speakerName: line.speakerId ? speakerNameOf(line.speakerId) : undefined,
      })),
    [lineRows, defaultTextOf, speakerNameOf],
  );

  const handleDraft = useCallback((key: string, patch: LineDraft) => {
    setDrafts((prev) => ({ ...prev, [key]: { ...prev[key], ...patch } }));
  }, []);

  const clearDraft = useCallback((key: string) => {
    setDrafts((prev) => {
      const next = { ...prev };
      delete next[key];
      return next;
    });
  }, []);

  const handleQuickAddSpeaker = useCallback(
    (draft: SpeakerDraft): string | null => {
      const id = draft.id.trim();
      if (!id) return '说话人 ID 不能为空。';
      if (!draft.displayName.trim()) return '显示名不能为空。';
      if (speakerRows.some((row) => row.id === id) || newSpeakers.some((row) => row.id === id)) {
        return `说话人 ${id} 已存在。`;
      }
      setNewSpeakers((prev) => [...prev, { ...draft, id, displayName: draft.displayName.trim() }]);
      return null;
    },
    [speakerRows, newSpeakers],
  );

  const putCatalog = async (id: string, payload: unknown): Promise<boolean> => {
    const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/story/catalogs/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ items: payload }),
    });
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? `${id} 保存失败`);
      return false;
    }
    return true;
  };

  const saveDialogueAuthoring = async () => {
    setStatus('保存中…');
    let nextDialogues = dialogueRows;
    let nextLines = lineRows;
    let nextSpeakers = speakerRows;
    let nextTokens = tokenRows;
    if (advancedJson) {
      try {
        const parsed = JSON.parse(itemsText);
        if (catalogId === 'dialogues') nextDialogues = asArray<DialogueRow>(parsed);
        else if (catalogId === 'lines') nextLines = asArray<LineRow>(parsed);
        else if (catalogId === 'speakers') nextSpeakers = asArray<SpeakerRow>(parsed);
        else if (catalogId === 'text_tokens') nextTokens = asArray<TextTokenRow>(parsed);
      } catch (e: any) {
        setError(`JSON 解析失败：${e.message}`);
        setStatus('');
        return;
      }
    }

    const result = planInlineSync({
      dialogues: nextDialogues,
      lines: nextLines,
      speakers: nextSpeakers,
      tokens: nextTokens,
      localeRoot,
      drafts,
      newSpeakers,
    });
    if (result.ok === false) {
      setError(result.error);
      setStatus('');
      return;
    }
    const plan = result.plan;
    for (const tree of plan.dialogues) {
      const problem = validateDialogueTree(tree);
      if (problem) {
        setError(problem);
        setStatus('');
        return;
      }
    }

    const validateRes = await fetch(`/api/mods/${encodeURIComponent(modId)}/story/text/validate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokens: plan.tokens, locales: plan.localeRoot }),
    });
    const validateJson = await validateRes.json();
    if (!validateJson.ok) {
      setError(`引擎校验未通过：${validateJson.error}`);
      setStatus('');
      return;
    }

    const steps: Array<[string, unknown]> = [
      ['text_tokens', plan.tokens],
      ['text_locales', plan.localeRoot],
      ['lines', plan.lines],
      ['speakers', plan.speakers],
      ['dialogues', plan.dialogues],
    ];
    const written: string[] = [];
    for (const [id, payload] of steps) {
      const serialized = JSON.stringify(payload);
      if (serialized === snapshotRef.current[id]) continue;
      const ok = await putCatalog(id, payload);
      if (!ok) {
        setStatus('');
        return;
      }
      snapshotRef.current[id] = serialized;
      written.push(CATALOG_LABELS[id] ?? id);
    }
    if (written.length === 0) {
      setStatus('没有要写的改动。');
      return;
    }

    setDialogueRows(plan.dialogues);
    setLineRows(plan.lines);
    setSpeakerRows(plan.speakers);
    setTokenRows(plan.tokens);
    setLocaleRoot(plan.localeRoot);
    setDrafts({});
    setNewSpeakers([]);
    setItemsText(JSON.stringify(plan.dialogues, null, 2));
    setError('');
    const extras: string[] = [];
    if (plan.createdLineIds.length > 0) extras.push(`新建 ${plan.createdLineIds.length} 条台词`);
    if (plan.orphanLineIds.length > 0) extras.push(`未引用台词 ${plan.orphanLineIds.length} 条留在台词本`);
    setStatus(`已写入：${written.join('、')}。${extras.join('；')}。正在玩的局要重开才会按新树走。`);
  };

  const save = async () => {
    if (tool === 'dialogue') {
      await saveDialogueAuthoring();
      return;
    }
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
    if (catalogId === 'dialogues' && Array.isArray(payloadItems)) {
      for (const row of payloadItems) {
        const problem = validateDialogueTree(row as DialogueTree);
        if (problem) {
          setError(problem);
          setStatus('');
          return;
        }
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
    setStatus(typeof json.path === 'string' ? diskSaveStatus(json.path) : `已保存 ${CATALOG_LABELS[catalogId] ?? catalogId}`);
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
        说话人
        <select
          className={fieldClass}
          value={row.speakerId ?? ''}
          onChange={(e) => replaceSelected({ ...row, speakerId: e.target.value })}
        >
          <option value="">选说话人</option>
          {speakerRows.map((speaker) => (
            <option key={speaker.id} value={speaker.id}>
              {speakerNameOf(speaker.id) ? `${speakerNameOf(speaker.id)}（${speaker.id}）` : speaker.id}
            </option>
          ))}
          {row.speakerId && !speakerRows.some((speaker) => speaker.id === row.speakerId) ? (
            <option value={row.speakerId}>{row.speakerId}</option>
          ) : null}
        </select>
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
      <p className="text-[10px] text-studio-muted">说话人和正文建议直接在对话树的节点里写，保存时自动同步这张表。</p>
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
      <p className="text-[10px] text-studio-muted">立绘跟随说话人：节点只认说话人，不单独存图。</p>
    </div>
  );

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
            显示名词条
            <input
              className={fieldClass}
              value={row.displayNameToken ?? row.displayName ?? ''}
              onChange={(e) => replaceSelected({ ...row, displayNameToken: e.target.value })}
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
          <label className="flex items-center gap-2 pt-5 text-xs text-studio-muted">
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
          <h3 className="text-sm text-studio-yellow">选中轨道属性</h3>
          <button
            type="button"
            className={STUDIO_CHROME.btnGhost}
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
          <div className="grid grid-cols-2 gap-2 rounded-md border border-studio-elevated bg-studio-bg p-3">
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
              className={`col-span-2 ${STUDIO_CHROME.btnDanger}`}
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
    if (catalogId === 'sequences') return renderSequenceForm(selected as SequenceRow);
    return null;
  })();

  const dialogueTree = tool === 'dialogue' && catalogId === 'dialogues' && selected ? (selected as DialogueTree) : null;

  const jsonViewValue = advancedJson ? itemsText : JSON.stringify(viewItems, null, 2);

  const onJsonChange = (text: string) => {
    setItemsText(text);
  };

  return (
    <div className={`${STUDIO_CHROME.page} overflow-hidden p-6 font-sans`}>
      <div className="mb-4 flex items-center gap-4 flex-wrap">
        <h1 className="text-xl text-studio-label">
          {tool === 'timeline' ? '时间轴' : tool === 'dialogue' ? '对话' : '叙事配置'}
        </h1>
        <span className="text-xs text-studio-muted">
          {tool === 'timeline'
            ? '演出序列：镜头 / 字幕 / 信号轨。拖块改时长，保存进 Sequencer/sequences.json。'
            : tool === 'dialogue'
              ? '对话是树：节点里直接选说话人、写正文，保存时台词本和文本表自动同步。'
              : '台词 / 对话树 / 演出序列；换肤只动 panelTheme + CSS'}
        </span>
      </div>

      <div className="grid h-[calc(100%-3rem)] grid-cols-12 gap-4">
        <aside className="col-span-3 space-y-3 overflow-auto">
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

          <div className="text-xs text-studio-muted">目录</div>
          <ul className="space-y-1">
            {catalogs.map((c) => (
              <li key={c.id}>
                <button
                  type="button"
                  className={`w-full rounded-md border px-2 py-2 text-left text-sm ${
                    catalogId === c.id
                      ? 'border-studio-blue bg-studio-blue/10 text-studio-label'
                      : 'border-studio-elevated bg-studio-surface text-studio-secondary hover:border-studio-fill'
                  }`}
                  onClick={() => setCatalogId(c.id)}
                >
                  {CATALOG_LABELS[c.id] ?? c.id}
                  {!c.exists && <span className="ml-2 text-[10px] text-studio-red">缺失</span>}
                </button>
              </li>
            ))}
          </ul>

          <div className="pt-2 text-xs text-studio-muted">条目</div>
          <ul className="max-h-64 space-y-1 overflow-auto rounded-md border border-studio-elevated p-1">
            {itemIds.map((id) => (
              <li key={id}>
                <button
                  type="button"
                  className={`w-full rounded-md px-2 py-1 text-left text-xs ${
                    selectedId === id ? 'bg-studio-blue/20 text-studio-label' : 'hover:bg-studio-elevated'
                  }`}
                  onClick={() => setSelectedId(id)}
                >
                  {id}
                </button>
              </li>
            ))}
          </ul>
          <div className="flex gap-2">
            <button type="button" onClick={addEntry} className={`flex-1 ${STUDIO_CHROME.btnGhost}`}>
              新建
            </button>
            <button type="button" onClick={removeSelected} className={`flex-1 ${STUDIO_CHROME.btnDanger}`}>
              删除
            </button>
          </div>
          {tool === 'dialogue' && Object.keys(drafts).length + newSpeakers.length > 0 ? (
            <p className="text-[10px] text-studio-yellow">
              有 {Object.keys(drafts).length + newSpeakers.length} 处节点草稿未保存。
            </p>
          ) : null}
        </aside>

        <main className="col-span-9 flex min-h-0 flex-col gap-3">
          <div className="flex flex-wrap items-center gap-3">
            <button type="button" onClick={() => void save()} className={STUDIO_CHROME.btnPrimary}>
              保存
            </button>
            {tool === 'dialogue' ? (
              <button
                type="button"
                onClick={() => void loadDialogueAuthoringState(modId)}
                className={STUDIO_CHROME.btnGhost}
              >
                重载
              </button>
            ) : (
              <button type="button" onClick={() => void loadCatalog(modId, catalogId)} className={STUDIO_CHROME.btnGhost}>
                重载
              </button>
            )}
            <label className="flex items-center gap-2 text-xs text-studio-muted">
              <input type="checkbox" checked={advancedJson} onChange={(e) => setAdvancedJson(e.target.checked)} />
              高级 JSON
            </label>
            {status && <span className="text-xs text-studio-blue">{status}</span>}
            {error && <span className="text-xs text-studio-red">{error}</span>}
          </div>

          {dialogueTree && !advancedJson ? (
            <div className="min-h-0 flex-1">
              <DialogueTreeCanvas
                tree={dialogueTree}
                lines={linePreviews}
                selectedNodeId={selectedDialogueNodeId}
                onSelectNode={setSelectedDialogueNodeId}
                onChange={(next) => replaceSelected(next)}
                speakers={speakerRows}
                speakerNameOf={speakerNameOf}
                defaultTextOf={defaultTextOf}
                drafts={drafts}
                onDraft={handleDraft}
                onClearDraft={clearDraft}
                onQuickAddSpeaker={handleQuickAddSpeaker}
              />
            </div>
          ) : formBody && !advancedJson ? (
            <div className="max-h-[75vh] overflow-auto rounded-lg border border-studio-elevated bg-studio-surface p-4">
              {formBody}
            </div>
          ) : (
            <textarea
              className="h-[70vh] w-full rounded-md border border-studio-elevated bg-studio-bg p-3 font-mono text-sm leading-relaxed"
              value={jsonViewValue}
              onChange={(e) => onJsonChange(e.target.value)}
              spellCheck={false}
            />
          )}

          <p className="text-xs text-studio-muted">
            {tool === 'dialogue'
              ? '保存会一起写对话树 / 台词本 / 说话人 / 文案词条 / 语言表，写入前先过引擎校验。正在玩的局要重开才会按新树走。'
              : `写入 ${catalogs.find((c) => c.id === catalogId)?.relativePath ?? '…'}。保存只改磁盘；正在玩的局要重开才会按新树走。`}
          </p>
        </main>
      </div>
    </div>
  );
};

export default StoryAuthoringPage;
