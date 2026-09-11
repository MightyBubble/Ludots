import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AUTHORING_TOOLS } from './authoringTools';
import { STUDIO_THEME, TOOL_ACCENT } from './authoringTheme';

export function AuthoringStudioHome() {
  const [bridgeUp, setBridge] = useState<boolean | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetch('/health', { cache: 'no-store' });
        if (!cancelled) setBridge(res.ok);
      } catch {
        if (!cancelled) setBridge(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="h-full overflow-auto bg-studio-bg px-8 py-10 text-studio-label">
      <div className="mx-auto max-w-4xl">
        <p className="text-xs uppercase tracking-widest text-studio-muted">Ludots</p>
        <h1 className="mt-1 text-3xl font-semibold text-studio-label">作者工作室</h1>
        <p className="mt-3 max-w-2xl text-sm leading-relaxed text-studio-secondary">
          写蓝图、行为树、状态机、对话和时间轴。一键进来，顶栏五个房间来回切。地图、面板、场图层、技能数值不在这扇门里。
        </p>
        {bridgeUp === false ? (
          <p className="mt-4 rounded-md border border-studio-red/40 bg-studio-red/10 px-3 py-2 text-sm text-studio-red">
            桥没连上。用仓库根的 <code className="font-mono">scripts/run-authoring-studio.sh</code> 或{' '}
            <code className="font-mono">scripts/run-authoring-studio.cmd</code> 一键启动；缺桥时保存会失败，不会假装写进去了。
          </p>
        ) : null}

        <ul className="mt-8 grid gap-4 sm:grid-cols-2">
          {AUTHORING_TOOLS.map((tool) => {
            const accent = TOOL_ACCENT[tool.id] ?? 'blue';
            return (
              <li key={tool.id}>
                <Link
                  to={tool.path}
                  data-authoring-card={tool.id}
                  className="block h-full rounded-xl border bg-studio-surface p-4"
                  style={{ borderColor: STUDIO_THEME[accent] }}
                >
                  <div className="text-lg font-semibold" style={{ color: STUDIO_THEME[accent] }}>
                    {tool.title}
                  </div>
                  <p className="mt-2 text-sm leading-relaxed text-studio-secondary">{tool.blurb}</p>
                  <p className="mt-3 font-mono text-[11px] text-studio-muted">{tool.hint}</p>
                </Link>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}
