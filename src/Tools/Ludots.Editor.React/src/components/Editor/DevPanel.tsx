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
        <div className="absolute top-2 right-2 bg-gray-900/80 text-gray-200 border border-gray-700 rounded p-2 text-[10px] z-50 w-64 pointer-events-none">
            <div className="flex justify-between">
                <div>UI: ok</div>
                <div>Bridge: {bridgeOk === null ? '...' : bridgeOk ? 'ok' : 'down'}</div>
            </div>
            <div>mods: {mods.length} mod: {selectedModId ?? '-'}</div>
            <div>map: {selectedMapId ?? '-'}</div>
            <div>dirty(nav): {navDirtyChunks.size}</div>
            <div>loading: {loadingState.isLoading ? `${loadingState.message} (${loadingState.progress}%)` : 'no'}</div>
            {lastError ? <div className="text-red-300 break-words">err: {lastError}</div> : null}
        </div>
    );
};
