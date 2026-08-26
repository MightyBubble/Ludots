import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

type CatalogInfo = {
  id: string;
  relativePath: string;
  path: string;
  exists: boolean;
};

type ModInfo = { id: string; name?: string };

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

export const StoryAuthoringPage: React.FC = () => {
  const [mods, setMods] = useState<ModInfo[]>([]);
  const [modId, setModId] = useState('NarrativeShowcaseMod');
  const [catalogs, setCatalogs] = useState<CatalogInfo[]>([]);
  const [catalogId, setCatalogId] = useState('lines');
  const [itemsText, setItemsText] = useState('[]');
  const [selectedId, setSelectedId] = useState<string>('');
  const [status, setStatus] = useState<string>('');
  const [error, setError] = useState<string>('');

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
    setStatus('Loading…');
    const res = await fetch(`/api/mods/${encodeURIComponent(targetMod)}/story/catalogs/${encodeURIComponent(id)}`);
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? 'failed to load catalog');
      setStatus('');
      return;
    }
    setItemsText(JSON.stringify(json.items ?? [], null, 2));
    setSelectedId('');
    setError('');
    setStatus(`Loaded ${id}`);
  }, []);

  useEffect(() => {
    void loadMods();
  }, [loadMods]);

  useEffect(() => {
    if (!modId) return;
    void loadCatalogList(modId);
  }, [modId, loadCatalogList]);

  useEffect(() => {
    if (!modId || !catalogId) return;
    void loadCatalog(modId, catalogId);
  }, [modId, catalogId, loadCatalog]);

  const itemIds = useMemo(() => {
    try {
      const parsed = JSON.parse(itemsText);
      if (!Array.isArray(parsed)) return [] as string[];
      return parsed
        .map((row: any) => (typeof row?.id === 'string' ? row.id : ''))
        .filter((id: string) => id.length > 0);
    } catch {
      return [] as string[];
    }
  }, [itemsText]);

  const save = async () => {
    setStatus('Saving…');
    let items: unknown;
    try {
      items = JSON.parse(itemsText);
    } catch (e: any) {
      setError(`JSON parse error: ${e.message}`);
      setStatus('');
      return;
    }
    const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/story/catalogs/${encodeURIComponent(catalogId)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ items }),
    });
    const json = await res.json();
    if (!json.ok) {
      setError(json.error ?? 'save failed');
      setStatus('');
      return;
    }
    setError('');
    setStatus(`Saved ${catalogId} → ${json.path}`);
  };

  const selectItem = (id: string) => {
    setSelectedId(id);
    try {
      const parsed = JSON.parse(itemsText);
      if (!Array.isArray(parsed)) return;
      const row = parsed.find((r: any) => r?.id === id);
      if (!row) return;
      // Keep full array in editor; scroll hint via status
      setStatus(`Selected ${id}`);
    } catch {
      /* ignore */
    }
  };

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 p-6 font-mono">
      <div className="mb-4 flex items-center gap-4">
        <Link to="/" className="text-emerald-400 hover:underline text-sm">
          ← 编辑器
        </Link>
        <h1 className="text-xl text-amber-200">叙事配置</h1>
        <span className="text-xs text-zinc-500">纯配置改台词/对话/演出；换肤只动 panelTheme + CSS</span>
      </div>

      <div className="grid grid-cols-12 gap-4">
        <aside className="col-span-3 space-y-3">
          <label className="block text-xs text-zinc-400">
            目标 Mod
            <select
              className="mt-1 w-full bg-zinc-900 border border-zinc-700 rounded px-2 py-2"
              value={modId}
              onChange={(e) => setModId(e.target.value)}
            >
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
                  onClick={() => selectItem(id)}
                >
                  {id}
                </button>
              </li>
            ))}
          </ul>
        </aside>

        <main className="col-span-9 space-y-3">
          <div className="flex items-center gap-3">
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
            {status && <span className="text-xs text-emerald-400">{status}</span>}
            {error && <span className="text-xs text-rose-400">{error}</span>}
          </div>

          <textarea
            className="w-full h-[70vh] bg-zinc-900 border border-zinc-700 rounded p-3 text-sm leading-relaxed"
            value={itemsText}
            onChange={(e) => setItemsText(e.target.value)}
            spellCheck={false}
          />
          <p className="text-xs text-zinc-500">
            写入 {catalogs.find((c) => c.id === catalogId)?.relativePath ?? '…'}；每条要有 id。换肤请改
            game.json 的 panelTheme，并在 PanelThemes 下写 CSS/HTML 素材。
          </p>
        </main>
      </div>
    </div>
  );
};

export default StoryAuthoringPage;
