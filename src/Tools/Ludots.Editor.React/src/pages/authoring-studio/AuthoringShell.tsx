import { useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import {
  AUTHORING_SECTIONS,
  AUTHORING_STUDIO_HOME,
  AUTHORING_TOOLS,
  matchAuthoringTool,
} from './authoringTools';
import { AgentDock } from '@/components/agent/AgentDock';
import { Badge } from '@/components/ui/Badge';
import { NavTab } from '@/components/ui/NavTab';

type BridgeState = 'checking' | 'up' | 'down';

export function AuthoringShell() {
  const location = useLocation();
  const active = matchAuthoringTool(location.pathname);
  const [bridge, setBridge] = useState<BridgeState>('checking');
  const [dockOpen, setDockOpen] = useState(false);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.ctrlKey && event.key === '`') {
        event.preventDefault();
        setDockOpen((open) => !open);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

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
        <NavTab to={AUTHORING_STUDIO_HOME} on={location.pathname === AUTHORING_STUDIO_HOME}>
          <span className="text-sm font-semibold">LudotsEditor</span>
        </NavTab>
        <nav className="flex min-w-0 flex-1 items-center gap-1 overflow-x-auto px-2">
          {AUTHORING_SECTIONS.map((section) => {
            const tools = AUTHORING_TOOLS.filter((tool) => tool.section === section.id);
            const sectionActive = tools.some((tool) => tool.id === active?.id);
            return (
              <div key={section.id} className="flex items-center gap-1" data-authoring-section={section.id}>
                <span
                  className={`shrink-0 px-1 text-[10px] uppercase tracking-wide ${
                    sectionActive ? 'text-studio-blue' : 'text-studio-muted'
                  }`}
                >
                  {section.title}
                </span>
                {tools.map((tool) => {
                  const on = active?.id === tool.id;
                  return (
                    <NavTab key={tool.id} to={tool.path} on={on} data-authoring-tool={tool.id}>
                      {tool.title}
                    </NavTab>
                  );
                })}
                <span className="mx-1 h-4 w-px shrink-0 bg-studio-elevated" />
              </div>
            );
          })}
        </nav>
        <button
          type="button"
          data-agent-dock-toggle={dockOpen ? 'open' : 'closed'}
          title="副驾（Ctrl+`）"
          onClick={() => setDockOpen((open) => !open)}
          className={`shrink-0 rounded-md px-2 py-0.5 text-[10px] ${
            dockOpen ? 'bg-studio-blue/20 text-studio-blue' : 'bg-studio-elevated text-studio-muted hover:text-studio-label'
          }`}
        >
          副驾
        </button>
        <Badge
          data-bridge-state={bridge}
          tone={bridge === 'up' ? 'blue' : bridge === 'down' ? 'red' : 'muted'}
        >
          {bridge === 'up' ? '桥已接通' : bridge === 'down' ? '桥没连上' : '在探桥…'}
        </Badge>
      </header>
      <div className="flex min-h-0 flex-1 overflow-hidden">
        <div className="min-h-0 min-w-0 flex-1 overflow-hidden">
          <Outlet />
        </div>
        {dockOpen ? <AgentDock onClose={() => setDockOpen(false)} /> : null}
      </div>
    </div>
  );
}
