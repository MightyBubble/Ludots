import { useCallback, useEffect, useMemo, useState } from "react";
import {
  AgentTool,
  HealthResponse,
  callTool,
  domainOf,
  emptyParamsFromSchema,
  fetchHealth,
  fetchTools,
} from "./bridgeApi";
import { SchemaForm } from "./SchemaForm";

const DEFAULT_URL = "http://127.0.0.1:47921";
const STORAGE_KEY = "ludots.inspector.bridgeUrl";

export default function App() {
  const [baseUrl, setBaseUrl] = useState(() => localStorage.getItem(STORAGE_KEY) || DEFAULT_URL);
  const [urlDraft, setUrlDraft] = useState(baseUrl);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [tools, setTools] = useState<AgentTool[]>([]);
  const [selected, setSelected] = useState<string>("");
  const [params, setParams] = useState<Record<string, unknown>>({});
  const [result, setResult] = useState<string>("");
  const [error, setError] = useState<string>("");
  const [busy, setBusy] = useState(false);
  const [filter, setFilter] = useState("");

  const selectedTool = useMemo(
    () => tools.find((t) => t.name === selected) ?? null,
    [tools, selected]
  );

  const grouped = useMemo(() => {
    const map = new Map<string, AgentTool[]>();
    for (const tool of tools) {
      if (filter && !tool.name.includes(filter) && !tool.description.includes(filter)) continue;
      const domain = domainOf(tool.name);
      const list = map.get(domain) ?? [];
      list.push(tool);
      map.set(domain, list);
    }
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [tools, filter]);

  const refresh = useCallback(async (url: string) => {
    setBusy(true);
    setError("");
    try {
      const [nextHealth, nextTools] = await Promise.all([fetchHealth(url), fetchTools(url)]);
      setHealth(nextHealth);
      setTools(nextTools);
      setSelected((prev) => {
        if (prev && nextTools.some((t) => t.name === prev)) return prev;
        return nextTools[0]?.name ?? "";
      });
      localStorage.setItem(STORAGE_KEY, url);
      setBaseUrl(url);
    } catch (err) {
      setHealth(null);
      setTools([]);
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    void refresh(baseUrl);
    const id = window.setInterval(() => {
      void fetchHealth(baseUrl)
        .then(setHealth)
        .catch(() => undefined);
    }, 2000);
    return () => window.clearInterval(id);
  }, [baseUrl, refresh]);

  useEffect(() => {
    if (!selectedTool) {
      setParams({});
      return;
    }
    setParams(emptyParamsFromSchema(selectedTool.inputSchema));
    setResult("");
  }, [selectedTool]);

  async function invoke(method: string, invokeParams: Record<string, unknown>) {
    setBusy(true);
    setError("");
    try {
      const response = await callTool(baseUrl, method, invokeParams);
      setResult(JSON.stringify(response, null, 2));
      const nextHealth = await fetchHealth(baseUrl);
      setHealth(nextHealth);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="app">
      <header className="top">
        <div className="brand">
          <h1>Ludots Inspector</h1>
          <p>人用前端 · 与 CLI / MCP 同一套指令（HTTP JSON-RPC）</p>
        </div>
        <form
          className="connect"
          onSubmit={(e) => {
            e.preventDefault();
            void refresh(urlDraft.trim() || DEFAULT_URL);
          }}
        >
          <input
            value={urlDraft}
            onChange={(e) => setUrlDraft(e.target.value)}
            placeholder={DEFAULT_URL}
            aria-label="桥地址"
          />
          <button type="submit" disabled={busy}>
            连接
          </button>
        </form>
        <div className={`status ${health?.ok ? "ok" : "bad"}`}>
          {health?.ok ? (
            <>
              <span>活着</span>
              <span>pid {health.instance?.pid ?? "?"}</span>
              <span>pump {health.pumpCount ?? 0}</span>
              <span>tools {tools.length}</span>
            </>
          ) : (
            <span>未连接</span>
          )}
        </div>
      </header>

      <div className="quick">
        <button disabled={busy || !health?.ok} onClick={() => void invoke("ludots.session.info", {})}>
          会话快照
        </button>
        <button
          disabled={busy || !health?.ok}
          onClick={() => void invoke("ludots.time.control", { action: "pause" })}
        >
          暂停
        </button>
        <button
          disabled={busy || !health?.ok}
          onClick={() => void invoke("ludots.time.control", { action: "step", steps: 1 })}
        >
          步进 1
        </button>
        <button
          disabled={busy || !health?.ok}
          onClick={() => void invoke("ludots.time.control", { action: "resume" })}
        >
          继续
        </button>
        <button
          disabled={busy || !health?.ok}
          onClick={() => void invoke("ludots.screenshot", { name: "inspector" })}
        >
          截图
        </button>
      </div>

      {error && <div className="banner error">{error}</div>}

      <main className="layout">
        <aside className="sidebar">
          <input
            className="filter"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="过滤工具…"
          />
          {grouped.map(([domain, list]) => (
            <section key={domain}>
              <h2>{domain}</h2>
              <ul>
                {list.map((tool) => (
                  <li key={tool.name}>
                    <button
                      className={tool.name === selected ? "active" : undefined}
                      onClick={() => setSelected(tool.name)}
                    >
                      {tool.name.replace(/^ludots\./, "")}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ))}
          {tools.length === 0 && <p className="muted">连接桥后会列出全部工具。</p>}
        </aside>

        <section className="panel">
          {selectedTool ? (
            <>
              <header>
                <h2>{selectedTool.name}</h2>
                <p>{selectedTool.description}</p>
              </header>
              <SchemaForm
                schema={selectedTool.inputSchema}
                value={params}
                onChange={setParams}
              />
              <div className="actions">
                <button
                  className="primary"
                  disabled={busy || !health?.ok}
                  onClick={() => void invoke(selectedTool.name, scrubParams(params))}
                >
                  调用（等同 CLI / MCP）
                </button>
              </div>
            </>
          ) : (
            <p className="muted">选择左侧工具。</p>
          )}
        </section>

        <section className="result">
          <h2>响应</h2>
          <pre>{result || "尚无调用结果。"}</pre>
        </section>
      </main>
    </div>
  );
}

function scrubParams(params: Record<string, unknown>): Record<string, unknown> {
  const next: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(params)) {
    if (value === "" || value === undefined) continue;
    next[key] = value;
  }
  return next;
}
