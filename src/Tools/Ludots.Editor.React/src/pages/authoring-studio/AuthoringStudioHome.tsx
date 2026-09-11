import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AUTHORING_TOOLS } from './authoringTools';

const CARD: Record<string, string> = {
  blueprint: 'border-sky-500/40 hover:border-sky-400',
  bt: 'border-violet-500/40 hover:border-violet-400',
  fsm: 'border-fuchsia-500/40 hover:border-fuchsia-400',
  dialogue: 'border-amber-500/40 hover:border-amber-400',
  timeline: 'border-rose-500/40 hover:border-rose-400',
};

const TITLE: Record<string, string> = {
  blueprint: 'text-sky-200',
  bt: 'text-violet-200',
  fsm: 'text-fuchsia-200',
  dialogue: 'text-amber-200',
  timeline: 'text-rose-200',
};

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
    <div className="h-full overflow-auto bg-zinc-950 px-8 py-10 text-zinc-100">
      <div className="mx-auto max-w-4xl">
        <p className="text-xs uppercase tracking-widest text-zinc-500">Ludots</p>
        <h1 className="mt-1 text-3xl font-semibold text-amber-100">作者工作室</h1>
        <p className="mt-3 max-w-2xl text-sm leading-relaxed text-zinc-400">
          写蓝图、行为树、状态机、对话和时间轴。一键进来，顶栏五个房间来回切。地图、面板、场图层、技能数值不在这扇门里。
        </p>
        {bridgeUp === false ? (
          <p className="mt-4 rounded border border-rose-900 bg-rose-950/40 px-3 py-2 text-sm text-rose-300">
            桥没连上。用仓库根的 <code className="font-mono">scripts/run-authoring-studio.sh</code> 或{' '}
            <code className="font-mono">scripts/run-authoring-studio.cmd</code> 一键启动；缺桥时保存会失败，不会假装写进去了。
          </p>
        ) : null}

        <ul className="mt-8 grid gap-4 sm:grid-cols-2">
          {AUTHORING_TOOLS.map((tool) => (
            <li key={tool.id}>
              <Link
                to={tool.path}
                data-authoring-card={tool.id}
                className={`block h-full rounded-lg border bg-zinc-900/60 p-4 transition-colors ${CARD[tool.id] ?? 'border-zinc-700'}`}
              >
                <div className={`text-lg font-semibold ${TITLE[tool.id] ?? 'text-zinc-100'}`}>{tool.title}</div>
                <p className="mt-2 text-sm leading-relaxed text-zinc-400">{tool.blurb}</p>
                <p className="mt-3 font-mono text-[11px] text-zinc-600">{tool.hint}</p>
              </Link>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
