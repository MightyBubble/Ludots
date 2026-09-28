import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AUTHORING_SECTIONS, AUTHORING_TOOLS } from './authoringTools';
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
        <h1 className="mt-1 text-3xl font-semibold text-studio-label">LudotsEditor</h1>
        <p className="mt-3 max-w-2xl text-sm leading-relaxed text-studio-secondary">
          一个正门三件事：作者写内容，世界捏地形与面板，运行选 Mod 一键开局。顶栏按区分组来回切，保存前都过引擎校验。
        </p>
        {bridgeUp === false ? (
          <p className="mt-4 rounded-md border border-studio-red/40 bg-studio-red/10 px-3 py-2 text-sm text-studio-red">
            桥没连上。用仓库根的 <code className="font-mono">scripts/run-authoring-studio.sh</code> 或{' '}
            <code className="font-mono">scripts/run-authoring-studio.cmd</code> 一键启动；缺桥时保存会失败，不会假装写进去了。
          </p>
        ) : null}

        {AUTHORING_SECTIONS.map((section) => (
          <section key={section.id} className="mt-8" data-authoring-home-section={section.id}>
            <div className="flex items-baseline gap-3">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-studio-blue">{section.title}</h2>
              <p className="text-xs text-studio-muted">{section.blurb}</p>
            </div>
            <ul className="mt-3 grid gap-4 sm:grid-cols-2">
              {AUTHORING_TOOLS.filter((tool) => tool.section === section.id).map((tool) => {
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
          </section>
        ))}
      </div>
    </div>
  );
}
