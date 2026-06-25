import React from 'react';
import { useEditorStore } from './EditorStore';

export const DevPanel: React.FC = () => {
    const { bridgeBaseUrl, mods, selectedModId, selectedMapId, loadingState, navDirtyChunks } = useEditorStore();
    const [bridgeOk, setBridgeOk] = React.useState<boolean | null>(null);
    const [lastError, setLastError] = React.useState<string | null>(null);

    React.useEffect(() => {
        const onError = (e: ErrorEvent) => setLastError(String(e.message || e.error || 'Unknown error'));
        const onRejection = (e: PromiseRejectionEvent) => setLastError(String((e.reason as any)?.message ?? e.reason ?? 'Unhandled rejection'));
        window.addEventListener('error', onError);
        window.addEventListener('unhandledrejection', onRejection);
        return () => {
            window.removeEventListener('error', onError);
            window.removeEventListener('unhandledrejection', onRejection);
        };
    }, []);

    React.useEffect(() => {
        let cancelled = false;
        const run = async () => {
            try {
                const res = await fetch(`${bridgeBaseUrl}/health`);
                if (!res.ok) throw new Error(`health ${res.status}`);
                const json = await res.json();
                if (!cancelled) setBridgeOk(Boolean(json.ok));
            } catch (e: any) {
                if (!cancelled) {
                    setBridgeOk(false);
                    setLastError(String(e?.message ?? e));
                }
            }
        };
        run();
        const t = window.setInterval(run, 3000);
        return () => {
            cancelled = true;
            window.clearInterval(t);
        };
    }, [bridgeBaseUrl]);

    return (
        <div className="pointer-events-none absolute bottom-4 left-4 z-30 w-[320px] rounded-lg border border-slate-800 bg-slate-950/70 p-2 text-[10px] text-slate-300 shadow-xl backdrop-blur">
            <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                    <span className="rounded bg-slate-800 px-1.5 py-0.5 text-slate-200">UI ok</span>
                    <span className={`rounded px-1.5 py-0.5 ${bridgeOk === null ? 'bg-slate-800 text-slate-400' : bridgeOk ? 'bg-emerald-950 text-emerald-200' : 'bg-red-950 text-red-200'}`}>
                        Bridge {bridgeOk === null ? '...' : bridgeOk ? 'ok' : 'down'}
                    </span>
                </div>
                <div className="text-slate-500">dirty {navDirtyChunks.size}</div>
            </div>
            <div className="mt-1 truncate text-slate-500">
                mods {mods.length} / {selectedModId ?? '-'} / {selectedMapId ?? '-'} / {loadingState.isLoading ? `${loadingState.message} ${loadingState.progress}%` : 'idle'}
            </div>
            {lastError ? <div className="mt-1 break-words text-red-300">err: {lastError}</div> : null}
        </div>
    );
};
