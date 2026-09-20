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
import { ToolDebugView, type ToolDebugSnapshot } from "./ToolDebugView";

const DEFAULT_URL = "http://127.0.0.1:47921";
const STORAGE_KEY = "ludots.inspector.bridgeUrl";
const TOOL_STORAGE_KEY = "ludots.inspector.selectedTool";

type ToolSession = {
  params: Record<string, unknown>;
  debug: ToolDebugSnapshot | null;
};

function shortName(name: string): string {
  return name.replace(/^ludots\./, "");
}

function scrubParams(params: Record<string, unknown>): Record<string, unknown> {
  const next: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(params)) {
    if (value === "" || value === undefined) continue;
    next[key] = value;
  }
  return next;
}

export default function App() {
  const [baseUrl, setBaseUrl] = useState(() => localStorage.getItem(STORAGE_KEY) || DEFAULT_URL);
  const [urlDraft, setUrlDraft] = useState(baseUrl);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [tools, setTools] = useState<AgentTool[]>([]);
  const [selected, setSelected] = useState(() => localStorage.getItem(TOOL_STORAGE_KEY) || "");
  const [sessions, setSessions] = useState<Record<string, ToolSession>>({});
  const [busy, setBusy] = useState(false);
  const [connectError, setConnectError] = useState("");
  const [filter, setFilter] = useState("");

  const selectedTool = useMemo(
    () => tools.find((t) => t.name === selected) ?? null,
    [tools, selected]
  );

  const session = selected ? sessions[selected] : undefined;

  const grouped = useMemo(() => {
    const map = new Map<string, AgentTool[]>();
    for (const tool of tools) {
      if (filter && !tool.name.includes(filter) && !(tool.description ?? "").includes(filter)) {
        continue;
      }
      const domain = domainOf(tool.name);
      const list = map.get(domain) ?? [];
      list.push(tool);
      map.set(domain, list);
    }
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [tools, filter]);

  const ensureSession = useCallback((tool: AgentTool) => {
    setSessions((prev) => {
      if (prev[tool.name]) return prev;
      return {
        ...prev,
        [tool.name]: {
          params: emptyParamsFromSchema(tool.inputSchema),
          debug: null,
        },
      };
    });
  }, []);

  const refresh = useCallback(
    async (url: string) => {
      setBusy(true);
      setConnectError("");
      try {
        const [nextHealth, nextTools] = await Promise.all([fetchHealth(url), fetchTools(url)]);
        setHealth(nextHealth);
        setTools(nextTools);
        setSelected((prev) => {
          const keep = prev && nextTools.some((t) => t.name === prev) ? prev : nextTools[0]?.name ?? "";
          if (keep) localStorage.setItem(TOOL_STORAGE_KEY, keep);
          return keep;
        });
        localStorage.setItem(STORAGE_KEY, url);
        setBaseUrl(url);
        setSessions((prev) => {
          const next = { ...prev };
          for (const tool of nextTools) {
            if (!next[tool.name]) {
              next[tool.name] = {
                params: emptyParamsFromSchema(tool.inputSchema),
                debug: null,
              };
            }
          }
          return next;
        });
      } catch (err) {
        setHealth(null);
        setTools([]);
        setConnectError(err instanceof Error ? err.message : String(err));
      } finally {
        setBusy(false);
      }
    },
    []
  );

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
    if (selectedTool) ensureSession(selectedTool);
  }, [selectedTool, ensureSession]);

  function selectTool(name: string) {
    setSelected(name);
    localStorage.setItem(TOOL_STORAGE_KEY, name);
  }

  function setParams(next: Record<string, unknown>) {
    if (!selected) return;
    setSessions((prev) => ({
      ...prev,
      [selected]: {
        params: next,
        debug: prev[selected]?.debug ?? null,
      },
    }));
  }

  async function invoke(method: string, invokeParams: Record<string, unknown>) {
    setBusy(true);
    const started = performance.now();
    try {
      const response = await callTool(baseUrl, method, invokeParams);
      const ms = Math.round(performance.now() - started);
      setSessions((prev) => ({
        ...prev,
        [method]: {
          params: prev[method]?.params ?? invokeParams,
          debug: {
            at: new Date().toISOString(),
            ms,
            request: invokeParams,
            response,
            error: null,
          },
        },
      }));
      const nextHealth = await fetchHealth(baseUrl);
      setHealth(nextHealth);
    } catch (err) {
      const ms = Math.round(performance.now() - started);
      const message = err instanceof Error ? err.message : String(err);
      setSessions((prev) => ({
        ...prev,
        [method]: {
          params: prev[method]?.params ?? invokeParams,
          debug: {
            at: new Date().toISOString(),
            ms,
            request: invokeParams,
            response: null,
            error: message,
          },
        },
      }));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="page">
      <div className="shell" role="application" aria-label="Inspector">
        <header className="bar">
          <strong className="title">Inspector</strong>
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
              spellCheck={false}
              aria-label="桥地址"
            />
            <button type="submit" disabled={busy}>
              连接
            </button>
          </form>
          <div className={`pill ${health?.ok ? "ok" : "bad"}`}>
            {health?.ok
              ? `ok · pid ${health.instance?.pid ?? "?"} · ${tools.length} tools`
              : "offline"}
          </div>
          <div className="quick">
            <button
              type="button"
              disabled={busy || !health?.ok}
              onClick={() => void invoke("ludots.time.control", { action: "pause" })}
            >
              暂停
            </button>
            <button
              type="button"
              disabled={busy || !health?.ok}
              onClick={() => void invoke("ludots.time.control", { action: "step", steps: 1 })}
            >
              步进
            </button>
            <button
              type="button"
              disabled={busy || !health?.ok}
              onClick={() => void invoke("ludots.time.control", { action: "resume" })}
            >
              继续
            </button>
          </div>
        </header>

        {connectError && <div className="banner">{connectError}</div>}

        <div className="body">
          <aside className="nav">
            <input
              className="filter"
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              placeholder="过滤"
              aria-label="过滤工具"
            />
            <div className="nav-scroll">
              {grouped.map(([domain, list]) => (
                <section key={domain}>
                  <h2>{domain}</h2>
                  <ul>
                    {list.map((tool) => {
                      const hasDebug = Boolean(sessions[tool.name]?.debug);
                      return (
                        <li key={tool.name}>
                          <button
                            type="button"
                            className={tool.name === selected ? "active" : undefined}
                            onClick={() => selectTool(tool.name)}
                          >
                            <span>{shortName(tool.name)}</span>
                            {hasDebug && <i className="dot" aria-hidden="true" />}
                          </button>
                        </li>
                      );
                    })}
                  </ul>
                </section>
              ))}
            </div>
          </aside>

          <section className="workspace">
            {selectedTool && session ? (
              <>
                <div className="workspace-head">
                  <code>{selectedTool.name}</code>
                  <button
                    type="button"
                    className="primary"
                    disabled={busy || !health?.ok}
                    onClick={() => void invoke(selectedTool.name, scrubParams(session.params))}
                  >
                    调用
                  </button>
                </div>
                <div className="workspace-split">
                  <div className="params">
                    <SchemaForm
                      schema={selectedTool.inputSchema}
                      value={session.params}
                      onChange={setParams}
                    />
                  </div>
                  <ToolDebugView toolName={selectedTool.name} snapshot={session.debug} />
                </div>
              </>
            ) : (
              <div className="empty">选工具</div>
            )}
          </section>
        </div>
      </div>
    </div>
  );
}
