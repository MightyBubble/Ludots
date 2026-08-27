import React from 'react';

export type CodegenUnsupportedOp = {
  op: string;
  instructionIndex: number;
  nodeId?: string | null;
  reason: string;
};

export type CodegenPreviewResult = {
  ok: boolean;
  eligible?: boolean;
  emitMode?: string;
  backendRecommended?: string;
  instructionCount?: number;
  yieldPoints?: number[];
  unsupportedOps?: CodegenUnsupportedOp[];
  source?: string | null;
  diagnostics?: string[];
  usesSpecialize?: boolean;
  error?: string;
};

export type CodegenParityResult = {
  ok: boolean;
  matches?: boolean;
  interpretReturnInt?: number;
  codegenReturnInt?: number;
  interpretStatus?: string;
  codegenStatus?: string;
  detail?: string | null;
  emitMode?: string;
  error?: string;
};

type Props = {
  modId: string;
  graphId: string;
  graphBody: unknown;
  executionBackendLabel?: string;
};

export const GraphCodegenPanel: React.FC<Props> = ({
  modId,
  graphId,
  graphBody,
  executionBackendLabel = 'Interpret',
}) => {
  const [preview, setPreview] = React.useState<CodegenPreviewResult | null>(null);
  const [parity, setParity] = React.useState<CodegenParityResult | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [status, setStatus] = React.useState('Idle');

  const runPreview = React.useCallback(async () => {
    if (!modId || !graphId) {
      setStatus('Select a graph first.');
      return;
    }
    setBusy(true);
    setStatus('Previewing…');
    setParity(null);
    try {
      const res = await fetch(
        `/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(graphId)}/codegen/preview`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(graphBody ?? {}),
        },
      );
      const payload = (await res.json()) as CodegenPreviewResult;
      setPreview(payload);
      setStatus(
        payload.eligible
          ? `Eligible · ${payload.emitMode ?? '?'} · ${payload.instructionCount ?? 0} instructions`
          : payload.error ?? 'Not eligible',
      );
    } catch (err) {
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }, [modId, graphId, graphBody]);

  const runParity = React.useCallback(async () => {
    if (!modId || !graphId) {
      setStatus('Select a graph first.');
      return;
    }
    setBusy(true);
    setStatus('Running parity…');
    try {
      const res = await fetch(
        `/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(graphId)}/codegen/parity`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(graphBody ?? {}),
        },
      );
      const payload = (await res.json()) as CodegenParityResult;
      setParity(payload);
      setStatus(
        payload.matches
          ? `Parity OK · interpret=${payload.interpretReturnInt} codegen=${payload.codegenReturnInt}`
          : payload.error ?? payload.detail ?? 'Parity mismatch',
      );
    } catch (err) {
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }, [modId, graphId, graphBody]);

  const copySource = React.useCallback(async () => {
    if (!preview?.source) {
      setStatus('No generated source to copy.');
      return;
    }
    await navigator.clipboard.writeText(preview.source);
    setStatus('Copied generated C#.');
  }, [preview]);

  const eligible = preview?.eligible === true;
  const lightClass = preview == null
    ? 'bg-slate-700'
    : eligible
      ? 'bg-emerald-500'
      : 'bg-rose-500';

  return (
    <div className="space-y-2 border-t border-slate-800 p-3 text-xs">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 font-semibold uppercase tracking-wide text-cyan-300">
          <span className={`inline-block h-2.5 w-2.5 rounded-full ${lightClass}`} />
          Codegen
        </div>
        <span className="rounded border border-slate-700 px-1.5 py-0.5 font-mono text-[10px] text-slate-300">
          backend: {executionBackendLabel}
        </span>
      </div>
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={busy}
          onClick={() => void runPreview()}
          className="rounded bg-cyan-800 px-2 py-1 font-semibold text-cyan-50 hover:bg-cyan-700 disabled:opacity-50"
        >
          Preview C#
        </button>
        <button
          type="button"
          disabled={busy || !eligible}
          onClick={() => void runParity()}
          className="rounded bg-slate-700 px-2 py-1 font-semibold text-slate-50 hover:bg-slate-600 disabled:opacity-50"
        >
          Parity
        </button>
        <button
          type="button"
          disabled={!preview?.source}
          onClick={() => void copySource()}
          className="rounded border border-slate-600 px-2 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-50"
        >
          Copy
        </button>
      </div>
      <div className="text-[10px] text-slate-400">{status}</div>
      {preview?.unsupportedOps && preview.unsupportedOps.length > 0 ? (
        <div className="max-h-24 overflow-auto rounded border border-rose-900/60 bg-slate-950 p-2 font-mono text-[10px] text-rose-200">
          {preview.unsupportedOps.map((op) => (
            <div key={`${op.op}:${op.instructionIndex}`}>
              [{op.instructionIndex}] {op.op}
              {op.nodeId ? ` · ${op.nodeId}` : ''} — {op.reason}
            </div>
          ))}
        </div>
      ) : null}
      {parity ? (
        <div className={`rounded border p-2 font-mono text-[10px] ${parity.matches ? 'border-emerald-800 text-emerald-200' : 'border-rose-800 text-rose-200'}`}>
          {parity.matches
            ? `match · return ${parity.codegenReturnInt} · ${parity.codegenStatus}`
            : parity.detail ?? parity.error ?? 'mismatch'}
        </div>
      ) : null}
      <pre className="max-h-48 overflow-auto whitespace-pre-wrap rounded border border-slate-800 bg-slate-950 p-2 font-mono text-[10px] text-slate-300">
        {preview?.source || 'Preview to see generated C# for the current graph.'}
      </pre>
    </div>
  );
};
