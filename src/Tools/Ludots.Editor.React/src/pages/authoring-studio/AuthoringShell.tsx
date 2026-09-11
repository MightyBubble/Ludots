import { useEffect, useState } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import { AUTHORING_STUDIO_HOME, AUTHORING_TOOLS, matchAuthoringTool } from './authoringTools';

type BridgeState = 'checking' | 'up' | 'down';

export function AuthoringShell() {
  const location = useLocation();
  const active = matchAuthoringTool(location.pathname);
  const [bridge, setBridge] = useState<BridgeState>('checking');

  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      try {
        const res = await fetch('/health', { cache: 'no-store' });
        if (cancelled) return;
        setBridge(res.ok ? 'up' : 'down');
      } catch {
        if (!cancelled) setBridge('down');
      }
    };
    void tick();
    const timer = window.setInterval(() => void tick(), 3000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  return (
    <div className="flex h-screen flex-col bg-zinc-950 text-zinc-100">
      <header className="flex h-11 shrink-0 items-center gap-1 border-b border-zinc-800 bg-zinc-900 px-3">
        <Link
          to={AUTHORING_STUDIO_HOME}
          className={`rounded px-2 py-1 text-sm font-semibold ${
            location.pathname === AUTHORING_STUDIO_HOME
              ? 'text-amber-200'
              : 'text-zinc-200 hover:bg-zinc-800 hover:text-white'
          }`}
        >
          作者工作室
        </Link>
        <nav className="flex min-w-0 flex-1 items-center gap-1 overflow-x-auto px-2">
          {AUTHORING_TOOLS.map((tool) => {
            const on = active?.id === tool.id;
            return (
              <Link
                key={tool.id}
                to={tool.path}
                data-authoring-tool={tool.id}
                className={`whitespace-nowrap rounded px-2 py-1 text-xs ${
                  on
                    ? 'bg-zinc-800 text-amber-100'
                    : 'text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100'
                }`}
              >
                {tool.title}
              </Link>
            );
          })}
        </nav>
        <span
          data-bridge-state={bridge}
          className={`shrink-0 rounded px-2 py-0.5 text-[10px] ${
            bridge === 'up'
              ? 'bg-emerald-900/60 text-emerald-300'
              : bridge === 'down'
                ? 'bg-rose-950 text-rose-300'
                : 'bg-zinc-800 text-zinc-500'
          }`}
        >
          {bridge === 'up' ? '桥已接通' : bridge === 'down' ? '桥没连上' : '在探桥…'}
        </span>
      </header>
      <div className="min-h-0 flex-1 overflow-hidden">
        <Outlet />
      </div>
    </div>
  );
}
