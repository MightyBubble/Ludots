import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  MAX_ARGS,
  cssToArgbHex,
  issueKey,
  parseTemplatePreview,
  validateBank,
  wrapSelection,
  type LocaleRoot,
  type PreviewRun,
  type TextTokenRow,
} from './textBankModel';
import './textBank.css';

type ModInfo = { id: string; name?: string };

type EditingCell = { tokenId: string; locale: string } | null;

function RunSpan({ run }: { run: PreviewRun }) {
  if (run.placeholder !== undefined) {
    return <span className="text-bank-run-placeholder">{run.text}</span>;
  }
  const style: React.CSSProperties = {};
  if (run.bold) style.fontWeight = 700;
  if (run.italic) style.fontStyle = 'italic';
  if (run.color) style.color = run.color;
  return <span style={style}>{run.text}</span>;
}

function CellEditor({
  initial,
  onCommit,
  onCancel,
}: {
  initial: string;
  onCommit: (value: string) => void;
  onCancel: () => void;
}) {
  const [text, setText] = useState(initial);
  const ref = useRef<HTMLTextAreaElement | null>(null);
  const lastColor = useRef('#FFF6C56B');

  useEffect(() => {
    ref.current?.focus();
    ref.current?.setSelectionRange(initial.length, initial.length);
  }, [initial.length]);

  const applyMarkup = (kind: 'b' | 'i' | 'color', colorCss?: string) => {
    const node = ref.current;
    if (!node) return;
    if (kind === 'color') {
      if (!colorCss) return;
      const argb = cssToArgbHex(colorCss);
      if (!argb) return;
      lastColor.current = colorCss;
      const next = wrapSelection(text, node.selectionStart, node.selectionEnd, kind, argb);
      setText(next.text);
      requestAnimationFrame(() => {
        node.setSelectionRange(next.selectionStart, next.selectionEnd);
        node.focus();
      });
      return;
    }
    const next = wrapSelection(text, node.selectionStart, node.selectionEnd, kind);
    setText(next.text);
    requestAnimationFrame(() => {
      node.setSelectionRange(next.selectionStart, next.selectionEnd);
      node.focus();
    });
  };

  return (
    <div>
      <div className="text-bank-markup-bar">
        <button type="button" title="加粗" onClick={() => applyMarkup('b')}>
          B
        </button>
        <button type="button" title="斜体" onClick={() => applyMarkup('i')}>
          I
        </button>
        <input
          type="color"
          title="给选中文字上色"
          value={lastColor.current}
          onChange={(e) => applyMarkup('color', e.target.value)}
        />
      </div>
      <textarea
        ref={ref}
        className="text-bank-editor"
        value={text}
        spellCheck={false}
        onChange={(e) => setText(e.target.value)}
        onBlur={() => onCommit(text)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            e.preventDefault();
            onCancel();
          }
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            onCommit(text);
          }
        }}
      />
    </div>
  );
}

export function TextBankPage() {
  const [searchParams] = useSearchParams();
  const focusToken = searchParams.get('token');

  const [mods, setMods] = useState<ModInfo[]>([]);
  const [modId, setModId] = useState('');
  const [tokens, setTokens] = useState<TextTokenRow[]>([]);
  const [localeRoot, setLocaleRoot] = useState<LocaleRoot | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [filter, setFilter] = useState('');
  const [hiddenLocales, setHiddenLocales] = useState<Set<string>>(new Set());
  const [editing, setEditing] = useState<EditingCell>(null);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [flashToken, setFlashToken] = useState('');
  const [newLocaleDraft, setNewLocaleDraft] = useState('');
  const [showSaveIssues, setShowSaveIssues] = useState(false);

  const issues = useMemo(() => validateBank(tokens, localeRoot), [tokens, localeRoot]);
  const issuesByCell = useMemo(() => {
    const map = new Map<string, string>();
    for (const issue of issues) {
      const sep = issue.where.indexOf(' / ');
      if (sep < 0) continue;
      const locale = issue.where.slice(0, sep);
      const token = issue.where.slice(sep + 3);
      if (!localeRoot?.locales[locale]) continue;
      map.set(`${token}\u0000${locale}`, issue.message);
    }
    return map;
  }, [issues, localeRoot]);

  const visibleLocales = useMemo(() => {
    const names = Object.keys(localeRoot?.locales ?? {});
    const order = (a: string, b: string) => {
      const da = localeRoot?.defaultLocale === a ? -1 : 0;
      const db = localeRoot?.defaultLocale === b ? -1 : 0;
      return da - db || a.localeCompare(b);
    };
    return names.sort(order).filter((name) => !hiddenLocales.has(name));
  }, [localeRoot, hiddenLocales]);

  const rows = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (!needle) return tokens;
    return tokens.filter((token) => {
      if (token.id.toLowerCase().includes(needle)) return true;
      for (const table of Object.values(localeRoot?.locales ?? {})) {
        if ((table[token.id] ?? '').toLowerCase().includes(needle)) return true;
      }
      return false;
    });
  }, [tokens, localeRoot, filter]);

  useEffect(() => {
    void (async () => {
      const res = await fetch('/api/mods');
      const json = await res.json();
      const list: ModInfo[] = (json.mods ?? json ?? []).map((m: { id?: string; Id?: string; name?: string; Name?: string }) => ({
        id: m.id ?? m.Id ?? m.name ?? m.Name ?? '',
        name: m.name ?? m.Name,
      }));
      const clean = list.filter((m) => !!m.id);
      setMods(clean);
      if (clean.length > 0) setModId(clean[0]!.id);
    })();
  }, []);

  const loadBank = useCallback(async (targetMod: string) => {
    setStatus('加载中…');
    setError('');
    const tokensRes = await fetch(`/api/mods/${encodeURIComponent(targetMod)}/story/catalogs/text_tokens`);
    const tokensJson = await tokensRes.json();
    if (!tokensJson.ok) {
      setError(tokensJson.error ?? 'text_tokens 加载失败');
      setStatus('');
      return;
    }
    const tokenRows: TextTokenRow[] = (tokensJson.items ?? []).map((row: { id: string; argCount?: number }) => ({
      id: row.id,
      argCount: row.argCount ?? 0,
    }));

    let root: LocaleRoot | null = null;
    const localesRes = await fetch(`/api/mods/${encodeURIComponent(targetMod)}/story/catalogs/text_locales`);
    if (localesRes.ok) {
      const localesJson = await localesRes.json();
      if (localesJson.ok && localesJson.items && typeof localesJson.items === 'object' && !Array.isArray(localesJson.items)) {
        root = localesJson.items as LocaleRoot;
      }
    }

    setTokens(tokenRows);
    setLocaleRoot(root);
    setLoaded(true);
    setDirty(false);
    setStatus(`已加载 ${tokenRows.length} 条词条`);
  }, []);

  useEffect(() => {
    if (modId) void loadBank(modId);
  }, [modId, loadBank]);

  useEffect(() => {
    if (!loaded || !focusToken) return;
    setFlashToken(focusToken);
    const timer = window.setTimeout(() => setFlashToken(''), 1800);
    return () => window.clearTimeout(timer);
  }, [loaded, focusToken]);

  const mutate = (fn: (draft: { tokens: TextTokenRow[]; localeRoot: LocaleRoot | null }) => void) => {
    const draft = { tokens: tokens.map((t) => ({ ...t })), localeRoot: localeRoot ? structuredClone(localeRoot) : localeRoot };
    fn(draft);
    setTokens(draft.tokens);
    setLocaleRoot(draft.localeRoot);
    setDirty(true);
  };

  const commitCell = (tokenId: string, locale: string, value: string) => {
    setEditing(null);
    mutate((draft) => {
      if (!draft.localeRoot) return;
      const table = draft.localeRoot.locales[locale];
      if (!table || table[tokenId] === value) return;
      table[tokenId] = value;
    });
  };

  const addToken = () => {
    let candidate = 'text.new.1';
    let n = 1;
    const ids = new Set(tokens.map((t) => t.id));
    while (ids.has(candidate)) {
      n += 1;
      candidate = `text.new.${n}`;
    }
    mutate((draft) => {
      draft.tokens.push({ id: candidate, argCount: 0 });
      if (draft.localeRoot) {
        for (const table of Object.values(draft.localeRoot.locales)) {
          table[candidate] = '';
        }
      }
    });
    setFilter('');
  };

  const removeToken = (tokenId: string) => {
    mutate((draft) => {
      draft.tokens = draft.tokens.filter((t) => t.id !== tokenId);
      if (draft.localeRoot) {
        for (const table of Object.values(draft.localeRoot.locales)) {
          delete table[tokenId];
        }
      }
    });
  };

  const renameToken = (oldId: string, nextId: string) => {
    if (nextId === oldId) return;
    mutate((draft) => {
      for (const token of draft.tokens) {
        if (token.id === oldId) token.id = nextId;
      }
      if (draft.localeRoot) {
        for (const table of Object.values(draft.localeRoot.locales)) {
          if (oldId in table) {
            table[nextId] = table[oldId]!;
            delete table[oldId];
          }
        }
      }
    });
  };

  const addLocale = () => {
    const name = newLocaleDraft.trim();
    if (!name) return;
    if (localeRoot?.locales[name]) {
      setError(`语言 "${name}" 已存在`);
      return;
    }
    mutate((draft) => {
      if (!draft.localeRoot) {
        draft.localeRoot = { defaultLocale: name, locales: { [name]: {} } };
        return;
      }
      const source = draft.localeRoot.locales[draft.localeRoot.defaultLocale];
      draft.localeRoot.locales[name] = source ? { ...source } : {};
    });
    setNewLocaleDraft('');
    setError('');
  };

  const removeLocale = (name: string) => {
    mutate((draft) => {
      if (!draft.localeRoot) return;
      delete draft.localeRoot.locales[name];
      if (draft.localeRoot.defaultLocale === name) {
        const first = Object.keys(draft.localeRoot.locales)[0];
        draft.localeRoot.defaultLocale = first ?? '';
      }
    });
    setHiddenLocales((prev) => {
      const next = new Set(prev);
      next.delete(name);
      return next;
    });
  };

  const makeDefault = (name: string) => {
    mutate((draft) => {
      if (draft.localeRoot) draft.localeRoot.defaultLocale = name;
    });
  };

  const save = async () => {
    if (!modId) return;
    setShowSaveIssues(true);
    const bridgeValidate = await fetch(`/api/mods/${encodeURIComponent(modId)}/story/text/validate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokens, locales: localeRoot ?? {} }),
    });
    const validateJson = await bridgeValidate.json();
    if (!validateJson.ok) {
      setError(`引擎校验未通过:${validateJson.error}`);
      setStatus('');
      return;
    }

    setStatus('校验通过,写入磁盘…');
    setError('');
    const putToken = await fetch(`/api/mods/${encodeURIComponent(modId)}/story/catalogs/text_tokens`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ items: tokens }),
    });
    const tokenJson = await putToken.json();
    if (!tokenJson.ok) {
      setError(tokenJson.error ?? 'text_tokens 保存失败');
      setStatus('');
      return;
    }

    const putLocales = await fetch(`/api/mods/${encodeURIComponent(modId)}/story/catalogs/text_locales`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ items: localeRoot ?? {} }),
    });
    const localeJson = await putLocales.json();
    if (!localeJson.ok) {
      setError(localeJson.error ?? 'text_locales 保存失败');
      setStatus('');
      return;
    }

    setDirty(false);
    setStatus(`已保存 ${tokens.length} 条词条到 ${modId}(重开游戏后生效)`);
  };

  return (
    <div className="text-bank-page">
      <div className="text-bank-toolbar">
        <select value={modId} onChange={(e) => setModId(e.target.value)}>
          {mods.map((mod) => (
            <option key={mod.id} value={mod.id}>
              {mod.name ?? mod.id}
            </option>
          ))}
        </select>
        <input
          className="text-bank-search"
          type="text"
          placeholder="搜索词条 id 或任意语言内容…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
        />
        <button type="button" onClick={addToken}>
          + 加词条
        </button>
        <input
          className="text-bank-new-locale"
          type="text"
          placeholder="新语言代码,如 ja-JP"
          value={newLocaleDraft}
          onChange={(e) => setNewLocaleDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') addLocale();
          }}
        />
        <button type="button" onClick={addLocale}>
          + 加语言
        </button>
        <button type="button" className="text-bank-primary" onClick={() => void save()} disabled={!loaded}>
          {dirty ? '校验并保存' : '保存'}
        </button>
        {loaded ? (
          <button type="button" onClick={() => void loadBank(modId)} disabled={!dirty}>
            放弃修改
          </button>
        ) : null}
      </div>

      <div className="text-bank-locale-toggles">
        {Object.keys(localeRoot?.locales ?? {}).length === 0 ? (
          <span>还没有语言表:加第一条词条或直接添加语言。</span>
        ) : null}
        {Object.keys(localeRoot?.locales ?? {})
          .slice()
          .sort((a, b) => a.localeCompare(b))
          .map((name) => (
            <label key={name}>
              <input
                type="checkbox"
                checked={!hiddenLocales.has(name)}
                onChange={(e) =>
                  setHiddenLocales((prev) => {
                    const next = new Set(prev);
                    if (e.target.checked) next.delete(name);
                    else next.add(name);
                    return next;
                  })
                }
              />
              {localeRoot?.defaultLocale === name ? (
                <span className="text-bank-default-star" title="默认语言">
                  ★
                </span>
              ) : null}
              {name}
              {localeRoot && localeRoot.defaultLocale !== name ? (
                <button
                  type="button"
                  className="text-bank-row-remove"
                  title="设为默认语言"
                  onClick={() => makeDefault(name)}
                >
                  默认
                </button>
              ) : null}
              <button
                type="button"
                className="text-bank-row-remove"
                title="删除这张语言表"
                onClick={() => removeLocale(name)}
              >
                ✕
              </button>
            </label>
          ))}
      </div>

      <div className="text-bank-body">
        <table className="text-bank-table">
          <thead>
            <tr>
              <th className="text-bank-id-col">词条 id</th>
              <th>参数</th>
              {visibleLocales.map((locale) => (
                <th key={locale} className="text-bank-cell">
                  {locale}
                  {localeRoot?.defaultLocale === locale ? ' ★' : ''}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((token) => (
              <tr key={token.id} className={flashToken === token.id ? 'text-bank-row-flash' : ''}>
                <td>
                  <input
                    className="text-bank-id-input"
                    value={token.id}
                    spellCheck={false}
                    onChange={(e) => renameToken(token.id, e.target.value)}
                  />
                </td>
                <td>
                  <select
                    className="text-bank-argcount"
                    value={token.argCount}
                    onChange={(e) =>
                      mutate((draft) => {
                        const row = draft.tokens.find((t) => t.id === token.id);
                        if (row) row.argCount = Number(e.target.value);
                      })
                    }
                  >
                    {Array.from({ length: MAX_ARGS + 1 }, (_, n) => (
                      <option key={n} value={n}>
                        {n}
                      </option>
                    ))}
                  </select>
                </td>
                {visibleLocales.map((locale) => {
                  const template = localeRoot?.locales[locale]?.[token.id];
                  const isEditing = editing?.tokenId === token.id && editing.locale === locale;
                  if (isEditing) {
                    return (
                      <td key={locale} className="text-bank-cell">
                        <CellEditor
                          initial={template ?? ''}
                          onCommit={(value) => commitCell(token.id, locale, value)}
                          onCancel={() => setEditing(null)}
                        />
                      </td>
                    );
                  }
                  const cellError = issuesByCell.get(`${token.id}\u0000${locale}`);
                  const preview = typeof template === 'string' ? parseTemplatePreview(template, token.argCount) : null;
                  return (
                    <td key={locale} className="text-bank-cell">
                      <div
                        className="text-bank-cell-preview"
                        onClick={() => setEditing({ tokenId: token.id, locale })}
                        onKeyDown={() => undefined}
                        role="button"
                        tabIndex={0}
                      >
                        {typeof template !== 'string' ? (
                          <span className="text-bank-cell-missing">缺这条翻译(点开补写)</span>
                        ) : (
                          preview?.runs.map((run, idx) => <RunSpan key={idx} run={run} />)
                        )}
                      </div>
                      {showSaveIssues && cellError ? <div className="text-bank-cell-error">{cellError}</div> : null}
                    </td>
                  );
                })}
                <td>
                  <button
                    type="button"
                    className="text-bank-row-remove"
                    title="删除这条词条(所有语言)"
                    onClick={() => removeToken(token.id)}
                  >
                    ✕
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="text-bank-status">
        {error ? <span className="text-bank-status-error">{error}</span> : status}
      </div>
      {showSaveIssues && issues.length > 0 ? (
        <ul className="text-bank-issues">
          {issues.slice(0, 30).map((issue) => (
            <li key={issueKey(issue)}>
              {issue.where}:{issue.message}
            </li>
          ))}
          {issues.length > 30 ? <li>…共 {issues.length} 条问题</li> : null}
        </ul>
      ) : null}
    </div>
  );
}
