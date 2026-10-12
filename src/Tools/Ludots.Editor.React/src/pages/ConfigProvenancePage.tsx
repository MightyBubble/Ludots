import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

type ModInfo = { id: string; name?: string };

type IdWinner = { id: string; sourceUri: string };
type Deletion = { id: string; sourceUri: string };
type Duplicate = { id: string; firstSource: string; secondSource: string };
type ComponentChain = { templateId: string; component: string; writerChain: string };

type ProvenanceFile = {
  relativePath: string;
  fragments: string[];
  fileWinner: string | null;
  fileWinnerKind: string | null;
  winners: IdWinner[];
  deletions: Deletion[];
  duplicates: Duplicate[];
  componentOverrideChains: ComponentChain[];
};

function readMods(payload: { mods?: unknown; Mod?: unknown }): ModInfo[] {
  const raw = Array.isArray(payload.mods) ? payload.mods : [];
  return raw
    .map((item) => {
      const row = item as { id?: string; Id?: string; name?: string; Name?: string };
      return { id: row.id ?? row.Id ?? row.name ?? row.Name ?? '', name: row.name ?? row.Name };
    })
    .filter((mod) => mod.id.length > 0);
}

export function ConfigProvenancePage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedMod = searchParams.get('mod') ?? '';
  const [mods, setMods] = useState<ModInfo[]>([]);
  const [modId, setModId] = useState(requestedMod);
  const [files, setFiles] = useState<ProvenanceFile[]>([]);
  const [filter, setFilter] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

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
          return list[0]?.id ?? '';
        });
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : '桥没有返回 mod 列表');
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
    setError('');
    void (async () => {
      try {
        const res = await fetch(`/api/mods/${encodeURIComponent(modId)}/config-provenance`);
        const json = (await res.json()) as { ok?: boolean; error?: string; files?: ProvenanceFile[] };
        if (cancelled) return;
        setFiles(json.files ?? []);
        if (!res.ok || json.ok === false || json.error) {
          setError(json.error || '合并溯源没有算出来');
          return;
        }
      } catch (err) {
        if (!cancelled) {
          setFiles([]);
          setError(err instanceof Error ? err.message : '桥没有返回合并溯源');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [modId]);

  const shown = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    const matched = needle
      ? files.filter((file) => {
          if (file.relativePath.toLowerCase().includes(needle)) return true;
          return file.fragments.some((uri) => uri.toLowerCase().includes(needle));
        })
      : files.slice();
    matched.sort((a, b) => {
      const count = b.fragments.length - a.fragments.length;
      if (count !== 0) return count;
      const winner = Number(Boolean(b.fileWinner)) - Number(Boolean(a.fileWinner));
      if (winner !== 0) return winner;
      return a.relativePath.localeCompare(b.relativePath);
    });
    return matched;
  }, [files, filter]);

  return (
    <div className="h-full overflow-auto bg-studio-bg px-8 py-8 text-studio-label">
      <div className="mx-auto max-w-5xl">
        <h1 className="text-2xl font-semibold">合并溯源</h1>
        <p className="mt-2 max-w-3xl text-sm leading-relaxed text-studio-secondary">
          每个文件按合并顺序列出来源。DeepObject 的标记是最后一份片段，更早的键仍可能来自更早的片段，数组整段替换。带 id 的表按 id 标胜出。这一页不画逐字段时间线。
        </p>

        <div className="mt-4 flex flex-wrap items-center gap-3">
          <label className="text-sm text-studio-secondary">
            Mod
            <select
              className="ml-2 rounded-md border border-studio-elevated bg-studio-surface px-2 py-1 text-studio-label"
              value={modId}
              onChange={(event) => {
                const next = event.target.value;
                setModId(next);
                setSearchParams(next ? { mod: next } : {});
              }}
            >
              {mods.map((mod) => (
                <option key={mod.id} value={mod.id}>
                  {mod.name && mod.name !== mod.id ? `${mod.name} (${mod.id})` : mod.id}
                </option>
              ))}
            </select>
          </label>
          <label className="text-sm text-studio-secondary">
            筛选
            <input
              className="ml-2 rounded-md border border-studio-elevated bg-studio-surface px-2 py-1 text-studio-label"
              value={filter}
              onChange={(event) => setFilter(event.target.value)}
              placeholder="路径或片段地址"
            />
          </label>
        </div>

        {error ? (
          <p className="mt-4 rounded-md border border-studio-red/40 bg-studio-red/10 px-3 py-2 text-sm text-studio-red">
            {error}
          </p>
        ) : null}
        {loading ? <p className="mt-4 text-sm text-studio-muted">正在按这份 mod 的加载顺序合并…</p> : null}

        <ul className="mt-6 space-y-4">
          {shown.map((file) => (
            <li
              key={file.relativePath}
              data-provenance-file={file.relativePath}
              className="rounded-xl border border-studio-elevated bg-studio-surface p-4"
            >
              <h2 className="font-mono text-sm text-studio-label">{file.relativePath}</h2>
              <p className="mt-3 text-xs uppercase tracking-widest text-studio-muted">合并顺序</p>
              <ol className="mt-1 space-y-1">
                {file.fragments.map((uri, index) => {
                  const winsFile = file.fileWinner === uri;
                  return (
                    <li key={`${uri}-${index}`} className="flex flex-wrap items-baseline gap-2 font-mono text-xs">
                      <span className="text-studio-muted">{index + 1}.</span>
                      <span data-fragment-uri={uri}>{uri}</span>
                      {winsFile ? (
                        <span className="rounded bg-studio-blue/20 px-1.5 py-0.5 text-[10px] text-studio-blue">
                          {file.fileWinnerKind === 'applied-array' ? '采用的数组' : '最后一份片段'}
                        </span>
                      ) : null}
                    </li>
                  );
                })}
              </ol>
              {file.fileWinner ? (
                <p className="mt-2 text-sm" data-file-winner={file.fileWinner}>
                  {file.fileWinnerKind === 'applied-array' ? '采用的数组' : '最后一份片段'}
                  ：<span className="font-mono text-xs">{file.fileWinner}</span>
                  {file.fileWinnerKind === 'last-fragment' ? (
                    <span className="mt-1 block text-xs text-studio-secondary">更早的键仍可能来自更早的片段。</span>
                  ) : null}
                </p>
              ) : null}

              {file.winners.length > 0 ? (
                <table className="mt-3 w-full text-left text-xs">
                  <thead>
                    <tr className="text-studio-muted">
                      <th className="py-1 pr-3 font-normal">id</th>
                      <th className="py-1 font-normal">胜出</th>
                    </tr>
                  </thead>
                  <tbody>
                    {file.winners.map((winner) => (
                      <tr key={winner.id} className="border-t border-studio-elevated">
                        <td className="py-1 pr-3 font-mono">{winner.id}</td>
                        <td className="py-1 font-mono" data-id-winner={winner.id}>
                          {winner.sourceUri}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : null}

              {file.deletions.length > 0 ? (
                <p className="mt-2 text-xs text-studio-secondary">
                  删除：{file.deletions.map((item) => `${item.id}（${item.sourceUri}）`).join('、')}
                </p>
              ) : null}
              {file.duplicates.length > 0 ? (
                <ul className="mt-2 space-y-1 text-xs text-studio-secondary">
                  {file.duplicates.map((item) => (
                    <li key={item.id}>
                      重复 id {item.id}：{item.firstSource} 与 {item.secondSource}
                    </li>
                  ))}
                </ul>
              ) : null}
              {file.componentOverrideChains.length > 0 ? (
                <ul className="mt-2 space-y-1 text-xs text-studio-secondary">
                  {file.componentOverrideChains.map((chain) => (
                    <li key={`${chain.templateId}:${chain.component}`}>
                      {chain.templateId} / {chain.component}：{chain.writerChain}
                    </li>
                  ))}
                </ul>
              ) : null}
            </li>
          ))}
        </ul>
        {!loading && !error && shown.length === 0 ? (
          <p className="mt-6 text-sm text-studio-muted">这份加载顺序里没有合并到的配置文件。</p>
        ) : null}
      </div>
    </div>
  );
}
