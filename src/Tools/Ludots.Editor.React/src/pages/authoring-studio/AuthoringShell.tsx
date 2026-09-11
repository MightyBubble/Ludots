import { useEffect, useState } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import { AUTHORING_STUDIO_HOME, AUTHORING_TOOLS, matchAuthoringTool } from './authoringTools';
import { STUDIO_CHROME } from './authoringTheme';

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
    <div className="flex h-screen flex-col bg-studio-bg text-studio-label">
      <header className="flex h-11 shrink-0 items-center gap-1 border-b border-studio-elevated bg-studio-surface px-3">
        <Link
          to={AUTHORING_STUDIO_HOME}
          className={
            location.pathname === AUTHORING_STUDIO_HOME ? STUDIO_CHROME.navOn : STUDIO_CHROME.navOff
          }
        >
          <span className="text-sm font-semibold">作者工作室</span>
        </Link>
        <nav className="flex min-w-0 flex-1 items-center gap-1 overflow-x-auto px-2">
          {AUTHORING_TOOLS.map((tool) => {
            const on = active?.id === tool.id;
            return (
              <Link
                key={tool.id}
                to={tool.path}
                data-authoring-tool={tool.id}
                className={on ? STUDIO_CHROME.navOn : STUDIO_CHROME.navOff}
              >
                {tool.title}
              </Link>
            );
          })}
        </nav>
        <span
          data-bridge-state={bridge}
          className={`shrink-0 rounded-md px-2 py-0.5 text-[10px] ${
            bridge === 'up'
              ? 'bg-studio-blue/20 text-studio-blue'
              : bridge === 'down'
                ? 'bg-studio-red/15 text-studio-red'
                : 'bg-studio-elevated text-studio-muted'
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
